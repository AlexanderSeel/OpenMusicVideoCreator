namespace OpenMusicVideoCreator.Domain.Analysis;

public enum SongSectionKind
{
    Unknown,
    Intro,
    Verse,
    PreChorus,
    Chorus,
    Bridge,
    Breakdown,
    Instrumental,
    Outro,
}

public enum AnalysisValueSource
{
    Detected,
    UserEdited,
    Imported,
}

public sealed record WaveformBucket(
    double StartSeconds,
    double EndSeconds,
    double Minimum,
    double Maximum,
    double Rms);

public sealed record EnergyPoint(double TimeSeconds, double Value);

public sealed record BeatMarker(double TimeSeconds, double Confidence);

public sealed record BarMarker(int Number, double TimeSeconds, double Confidence);

public sealed record PhraseMarker(
    int Number,
    double StartSeconds,
    double EndSeconds,
    double Confidence);

public sealed record QuietRange(
    double StartSeconds,
    double EndSeconds,
    double AverageEnergy);

public sealed record VocalActivityEstimate(
    double VocalFraction,
    double InstrumentalFraction,
    double Confidence,
    string Method);

public sealed record SongSection(
    Guid Id,
    string Label,
    SongSectionKind Kind,
    double StartSeconds,
    double EndSeconds,
    double Confidence,
    AnalysisValueSource Source);

public sealed record SongAnalysis(
    Guid Id,
    Guid ProjectId,
    Guid SourceAssetId,
    int Version,
    double DurationSeconds,
    double? Bpm,
    int? SampleRate,
    int? Channels,
    string? Codec,
    long? BitRate,
    IReadOnlyList<WaveformBucket> Waveform,
    IReadOnlyList<EnergyPoint> Energy,
    IReadOnlyList<BeatMarker> Beats,
    IReadOnlyList<SongSection> Sections,
    DateTimeOffset CreatedUtc)
{
    public VocalActivityEstimate? VocalActivity { get; init; }

    public IReadOnlyList<BarMarker> Bars => SongRhythmInference.InferBars(Beats);

    public IReadOnlyList<PhraseMarker> Phrases =>
        SongRhythmInference.InferPhrases(Bars, DurationSeconds);

    public IReadOnlyList<QuietRange> QuietRanges =>
        SongRhythmInference.DetectQuietRanges(Energy, DurationSeconds);

    public static void ValidateSections(double durationSeconds, IReadOnlyList<SongSection> sections)
    {
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Song duration must be positive.");
        }

        ArgumentNullException.ThrowIfNull(sections);
        var ordered = sections.OrderBy(section => section.StartSeconds).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var section = ordered[index];
            if (string.IsNullOrWhiteSpace(section.Label))
            {
                throw new ArgumentException("Song section label is required.", nameof(sections));
            }

            if (section.StartSeconds < 0 || section.EndSeconds <= section.StartSeconds || section.EndSeconds > durationSeconds + 0.001)
            {
                throw new ArgumentException(
                    $"Song section '{section.Label}' has an invalid time range.",
                    nameof(sections));
            }

            if (index > 0 && section.StartSeconds < ordered[index - 1].EndSeconds - 0.001)
            {
                throw new ArgumentException("Song sections cannot overlap.", nameof(sections));
            }
        }
    }
}

public static class SongRhythmInference
{
    public static IReadOnlyList<BarMarker> InferBars(
        IReadOnlyList<BeatMarker> beats,
        int beatsPerBar = 4)
    {
        ArgumentNullException.ThrowIfNull(beats);
        if (beatsPerBar <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar));
        }

        if (beats.Count < beatsPerBar)
        {
            return [];
        }

        var ordered = beats.OrderBy(beat => beat.TimeSeconds).ToArray();
        var bars = new List<BarMarker>();
        for (var offset = 0; offset + beatsPerBar - 1 < ordered.Length; offset += beatsPerBar)
        {
            var group = ordered.Skip(offset).Take(beatsPerBar).ToArray();
            bars.Add(new BarMarker(
                bars.Count + 1,
                group[0].TimeSeconds,
                Math.Clamp(group.Average(beat => beat.Confidence) * 0.8, 0, 1)));
        }

        return bars;
    }

    public static IReadOnlyList<PhraseMarker> InferPhrases(
        IReadOnlyList<BarMarker> bars,
        double durationSeconds,
        int barsPerPhrase = 4)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }
        if (barsPerPhrase <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(barsPerPhrase));
        }
        if (bars.Count == 0)
        {
            return [];
        }

        var ordered = bars.OrderBy(bar => bar.TimeSeconds).ToArray();
        var phrases = new List<PhraseMarker>();
        for (var offset = 0; offset < ordered.Length; offset += barsPerPhrase)
        {
            var phraseBars = ordered.Skip(offset).Take(barsPerPhrase).ToArray();
            var start = phraseBars[0].TimeSeconds;
            var endIndex = offset + barsPerPhrase;
            var end = endIndex < ordered.Length
                ? ordered[endIndex].TimeSeconds
                : durationSeconds;
            if (end <= start)
            {
                continue;
            }

            phrases.Add(new PhraseMarker(
                phrases.Count + 1,
                start,
                Math.Min(end, durationSeconds),
                Math.Clamp(phraseBars.Average(bar => bar.Confidence) * 0.8, 0, 1)));
        }

        return phrases;
    }

    public static IReadOnlyList<QuietRange> DetectQuietRanges(
        IReadOnlyList<EnergyPoint> energy,
        double durationSeconds,
        double threshold = 0.12,
        double minimumDurationSeconds = 1.25)
    {
        ArgumentNullException.ThrowIfNull(energy);
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }
        if (energy.Count == 0)
        {
            return [];
        }

        var ordered = energy.OrderBy(point => point.TimeSeconds).ToArray();
        var typicalStep = ordered.Length > 1
            ? ordered.Zip(ordered.Skip(1), (left, right) => right.TimeSeconds - left.TimeSeconds)
                .Where(step => step > 0)
                .DefaultIfEmpty(0.05)
                .Median()
            : 0.05;
        var allowedGap = Math.Max(0.1, typicalStep * 2.5);
        var result = new List<QuietRange>();
        var startIndex = -1;

        for (var index = 0; index <= ordered.Length; index++)
        {
            var quiet = index < ordered.Length && ordered[index].Value <= threshold;
            var connected = quiet && (startIndex < 0 || index == startIndex ||
                ordered[index].TimeSeconds - ordered[index - 1].TimeSeconds <= allowedGap);

            if (quiet && connected)
            {
                if (startIndex < 0)
                {
                    startIndex = index;
                }
                continue;
            }

            if (startIndex >= 0)
            {
                var endIndex = index - 1;
                var start = Math.Max(0, ordered[startIndex].TimeSeconds - typicalStep / 2);
                var end = Math.Min(durationSeconds, ordered[endIndex].TimeSeconds + typicalStep / 2);
                if (end - start >= minimumDurationSeconds)
                {
                    result.Add(new QuietRange(
                        start,
                        end,
                        ordered.Skip(startIndex).Take(endIndex - startIndex + 1).Average(point => point.Value)));
                }
                startIndex = -1;
            }

            if (quiet)
            {
                startIndex = index;
            }
        }

        return result;
    }

    private static double Median(this IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return 0;
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
