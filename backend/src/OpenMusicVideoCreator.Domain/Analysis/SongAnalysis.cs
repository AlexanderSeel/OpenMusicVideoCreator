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
