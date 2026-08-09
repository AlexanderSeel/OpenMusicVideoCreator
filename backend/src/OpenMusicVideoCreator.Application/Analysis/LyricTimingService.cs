using System.Security.Cryptography;
using System.Text;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Analysis;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Analysis;

public sealed class LyricTimingService
{
    private const double MinimumMatchScore = 0.18;

    private readonly IProjectRepository _projects;
    private readonly ISongAnalysisRepository _analyses;
    private readonly ILyricTimingRepository _timings;
    private readonly TimeProvider _timeProvider;

    public LyricTimingService(
        IProjectRepository projects,
        ISongAnalysisRepository analyses,
        ILyricTimingRepository timings,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _analyses = analyses;
        _timings = timings;
        _timeProvider = timeProvider;
    }

    public Task<LyricTimingAnalysis?> GetLatestAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        _timings.GetLatestAsync(projectId, cancellationToken);

    public Task<IReadOnlyList<LyricTimingAnalysis>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        _timings.ListVersionsAsync(projectId, cancellationToken);

    public Task<LyricTimingAnalysis> ApplyTranscriptionAsync(
        Guid projectId,
        TranscriptionResponse transcription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcription);
        return ApplyTranscriptionAsync(projectId, transcription.Segments, cancellationToken);
    }

    public async Task<LyricTimingAnalysis> ApplyTranscriptionAsync(
        Guid projectId,
        IReadOnlyList<TranscriptionSegment> transcriptionSegments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcriptionSegments);
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        if (string.IsNullOrWhiteSpace(project.Lyrics))
        {
            throw new InvalidOperationException("Project has no supplied lyrics to align.");
        }

        var songReference = project.References.SingleOrDefault(reference => reference.Kind == ProjectReferenceKind.Song)
            ?? throw new InvalidOperationException("Project has no song attached.");
        var analysis = await _analyses.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Analyze the song before aligning lyrics.");
        if (analysis.SourceAssetId != songReference.ReferenceId)
        {
            throw new InvalidOperationException("Song analysis is stale for the currently attached song.");
        }

        var orderedSegments = ValidateAndOrderSegments(transcriptionSegments, analysis.DurationSeconds);
        var lines = SplitAuthoritativeLyrics(project.Lyrics);
        var aligned = Align(lines, orderedSegments);
        var latest = await _timings.GetLatestAsync(projectId, cancellationToken);
        var timing = new LyricTimingAnalysis(
            Guid.NewGuid(),
            projectId,
            analysis.SourceAssetId,
            analysis.Id,
            (latest?.Version ?? 0) + 1,
            ComputeSha256(project.Lyrics),
            aligned,
            GetUtcNow());
        await _timings.UpsertAsync(timing, cancellationToken);
        return timing;
    }

    internal static IReadOnlyList<LyricTimingLine> Align(
        IReadOnlyList<(int LineNumber, string Text)> lines,
        IReadOnlyList<TranscriptionSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(segments);
        if (lines.Count == 0)
        {
            return [];
        }

        var result = new List<LyricTimingLine>(lines.Count);
        var cursor = 0;
        foreach (var line in lines)
        {
            if (cursor >= segments.Count)
            {
                result.Add(Unmatched(line));
                continue;
            }

            var bestScore = 0d;
            var bestStart = -1;
            var bestEnd = -1;
            var maxStart = Math.Min(segments.Count - 1, cursor + 4);
            for (var start = cursor; start <= maxStart; start++)
            {
                var combined = new StringBuilder();
                var maxEnd = Math.Min(segments.Count - 1, start + 2);
                for (var end = start; end <= maxEnd; end++)
                {
                    if (combined.Length > 0)
                    {
                        combined.Append(' ');
                    }
                    combined.Append(segments[end].Text);
                    var score = Similarity(line.Text, combined.ToString());
                    score = Math.Max(0, score - (start - cursor) * 0.025);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestStart = start;
                        bestEnd = end;
                    }
                }
            }

            if (bestStart < 0 || bestScore < MinimumMatchScore)
            {
                result.Add(Unmatched(line));
                continue;
            }

            result.Add(new LyricTimingLine(
                line.LineNumber,
                line.Text,
                segments[bestStart].Start.TotalSeconds,
                segments[bestEnd].End.TotalSeconds,
                Math.Round(Math.Clamp(bestScore, 0, 1), 3)));
            cursor = bestEnd + 1;
        }

        return result;
    }

    private static IReadOnlyList<TranscriptionSegment> ValidateAndOrderSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        double durationSeconds)
    {
        var ordered = segments.OrderBy(segment => segment.Start).ToArray();
        foreach (var segment in ordered)
        {
            if (segment.Start < TimeSpan.Zero ||
                segment.End <= segment.Start ||
                segment.End.TotalSeconds > durationSeconds + 0.5)
            {
                throw new ArgumentException("Transcription segment has an invalid time range.", nameof(segments));
            }
        }

        return ordered;
    }

    private static IReadOnlyList<(int LineNumber, string Text)> SplitAuthoritativeLyrics(string lyrics) =>
        lyrics.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select((text, index) => (LineNumber: index + 1, Text: text))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToArray();

    private static LyricTimingLine Unmatched((int LineNumber, string Text) line) =>
        new(line.LineNumber, line.Text, null, null, 0);

    private static double Similarity(string authoritative, string transcript)
    {
        var left = Tokenize(authoritative);
        var right = Tokenize(transcript);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Intersect(right, StringComparer.Ordinal).Count();
        var precision = intersection / (double)right.Count;
        var recall = intersection / (double)left.Count;
        var f1 = precision + recall <= 0 ? 0 : 2 * precision * recall / (precision + recall);

        var compactLeft = Compact(authoritative);
        var compactRight = Compact(transcript);
        var containmentBonus = compactLeft.Length > 0 && compactRight.Length > 0 &&
            (compactLeft.Contains(compactRight, StringComparison.Ordinal) ||
             compactRight.Contains(compactLeft, StringComparison.Ordinal))
            ? 0.12
            : 0;
        return Math.Clamp(f1 + containmentBonus, 0, 1);
    }

    private static IReadOnlySet<string> Tokenize(string value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            FlushToken();
        }
        FlushToken();
        return tokens;

        void FlushToken()
        {
            if (current.Length == 0)
            {
                return;
            }
            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static string Compact(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(char.ToLowerInvariant(character));
            }
        }
        return result.ToString();
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
