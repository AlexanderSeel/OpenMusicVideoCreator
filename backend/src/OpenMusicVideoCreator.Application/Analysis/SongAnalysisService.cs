using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Analysis;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Analysis;

public sealed class SongAnalysisService
{
    private readonly IProjectRepository _projects;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly ISongAnalysisRepository _analyses;
    private readonly IMediaProbe _mediaProbe;
    private readonly IAudioSignalAnalyzer _signalAnalyzer;
    private readonly TimeProvider _timeProvider;

    public SongAnalysisService(
        IProjectRepository projects,
        IMediaAssetRepository mediaAssets,
        ISongAnalysisRepository analyses,
        IMediaProbe mediaProbe,
        IAudioSignalAnalyzer signalAnalyzer,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _mediaAssets = mediaAssets;
        _analyses = analyses;
        _mediaProbe = mediaProbe;
        _signalAnalyzer = signalAnalyzer;
        _timeProvider = timeProvider;
    }

    public Task<SongAnalysis?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _analyses.GetLatestAsync(projectId, cancellationToken);

    public Task<IReadOnlyList<SongAnalysis>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        _analyses.ListVersionsAsync(projectId, cancellationToken);

    public async Task<SongAnalysis> AnalyzeAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var songReference = project.References.SingleOrDefault(reference => reference.Kind == ProjectReferenceKind.Song)
            ?? throw new InvalidOperationException("Project has no song attached.");
        var asset = await _mediaAssets.GetAsync(songReference.ReferenceId, cancellationToken)
            ?? throw new InvalidDataException($"Song asset '{songReference.ReferenceId}' is missing.");

        var probe = await _mediaProbe.ProbeAsync(new MediaLocation(asset.Location), cancellationToken);
        if (probe.DurationSeconds <= 0)
        {
            throw new InvalidDataException("Song duration could not be determined.");
        }

        var signal = await _signalAnalyzer.AnalyzeAsync(
            new MediaLocation(asset.Location),
            probe.DurationSeconds,
            cancellationToken);
        var latest = await _analyses.GetLatestAsync(projectId, cancellationToken);
        var analysis = new SongAnalysis(
            Guid.NewGuid(),
            projectId,
            asset.Id,
            (latest?.Version ?? 0) + 1,
            probe.DurationSeconds,
            signal.Bpm,
            probe.SampleRate,
            probe.Channels,
            probe.Codec,
            probe.BitRate,
            signal.Waveform,
            signal.Energy,
            signal.Beats,
            SuggestSections(probe.DurationSeconds, signal.Energy),
            GetUtcNow());

        SongAnalysis.ValidateSections(analysis.DurationSeconds, analysis.Sections);
        await _analyses.UpsertAsync(analysis, cancellationToken);
        return analysis;
    }

    public async Task<SongAnalysis> SaveSectionsAsync(
        Guid projectId,
        IReadOnlyList<SongSection> sections,
        CancellationToken cancellationToken = default)
    {
        var latest = await _analyses.GetLatestAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' has no song analysis.");

        var edited = sections
            .OrderBy(section => section.StartSeconds)
            .Select(section => section with
            {
                Id = section.Id == Guid.Empty ? Guid.NewGuid() : section.Id,
                Label = section.Label.Trim(),
                Confidence = 1,
                Source = AnalysisValueSource.UserEdited,
            })
            .ToArray();
        SongAnalysis.ValidateSections(latest.DurationSeconds, edited);

        var version = latest with
        {
            Id = Guid.NewGuid(),
            Version = latest.Version + 1,
            Sections = edited,
            CreatedUtc = GetUtcNow(),
        };
        await _analyses.UpsertAsync(version, cancellationToken);
        return version;
    }

    internal static IReadOnlyList<SongSection> SuggestSections(
        double durationSeconds,
        IReadOnlyList<EnergyPoint> energy)
    {
        if (durationSeconds <= 12)
        {
            return
            [
                NewSection("Full song", SongSectionKind.Unknown, 0, durationSeconds, 0.35),
            ];
        }

        var edgeLength = Math.Clamp(durationSeconds * 0.08, 6, 18);
        var boundaries = new SortedSet<double> { 0, edgeLength, durationSeconds - edgeLength, durationSeconds };
        var energyCandidates = DetectEnergyBoundaries(energy, edgeLength, durationSeconds - edgeLength);
        foreach (var candidate in energyCandidates)
        {
            if (boundaries.All(boundary => Math.Abs(boundary - candidate) >= 10))
            {
                boundaries.Add(candidate);
            }
        }

        InsertMaximumLengthBoundaries(boundaries, maxLengthSeconds: 42);
        var ordered = boundaries.OrderBy(value => value).ToArray();
        var sections = new List<SongSection>(ordered.Length - 1);
        for (var index = 0; index < ordered.Length - 1; index++)
        {
            var start = ordered[index];
            var end = ordered[index + 1];
            if (end - start < 2)
            {
                continue;
            }

            if (index == 0)
            {
                sections.Add(NewSection("Intro", SongSectionKind.Intro, start, end, 0.55));
            }
            else if (index == ordered.Length - 2)
            {
                sections.Add(NewSection("Outro", SongSectionKind.Outro, start, end, 0.55));
            }
            else
            {
                sections.Add(NewSection($"Section {index}", SongSectionKind.Unknown, start, end, 0.3));
            }
        }

        return sections;
    }

    private static IReadOnlyList<double> DetectEnergyBoundaries(
        IReadOnlyList<EnergyPoint> energy,
        double minimum,
        double maximum)
    {
        if (energy.Count < 5)
        {
            return [];
        }

        var candidates = new List<(double Time, double Change)>();
        for (var index = 2; index < energy.Count - 2; index++)
        {
            var before = (energy[index - 2].Value + energy[index - 1].Value) / 2;
            var after = (energy[index + 1].Value + energy[index + 2].Value) / 2;
            var change = Math.Abs(after - before);
            var time = energy[index].TimeSeconds;
            if (time >= minimum && time <= maximum && change >= 0.12)
            {
                candidates.Add((time, change));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Change)
            .Take(8)
            .OrderBy(candidate => candidate.Time)
            .Select(candidate => candidate.Time)
            .ToArray();
    }

    private static void InsertMaximumLengthBoundaries(SortedSet<double> boundaries, double maxLengthSeconds)
    {
        while (true)
        {
            var ordered = boundaries.OrderBy(value => value).ToArray();
            var longestStart = 0d;
            var longestEnd = 0d;
            var longest = 0d;
            for (var index = 0; index < ordered.Length - 1; index++)
            {
                var length = ordered[index + 1] - ordered[index];
                if (length > longest)
                {
                    longest = length;
                    longestStart = ordered[index];
                    longestEnd = ordered[index + 1];
                }
            }

            if (longest <= maxLengthSeconds)
            {
                return;
            }

            boundaries.Add((longestStart + longestEnd) / 2);
        }
    }

    private static SongSection NewSection(
        string label,
        SongSectionKind kind,
        double start,
        double end,
        double confidence) =>
        new(Guid.NewGuid(), label, kind, start, end, confidence, AnalysisValueSource.Detected);

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
