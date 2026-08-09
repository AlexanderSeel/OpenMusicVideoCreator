using OpenMusicVideoCreator.Domain.Analysis;

namespace OpenMusicVideoCreator.Api.Contracts.Analysis;

public sealed record SongSectionRequest(
    Guid? Id,
    string Label,
    SongSectionKind Kind,
    double StartSeconds,
    double EndSeconds);

public sealed record SongSectionResponse(
    Guid Id,
    string Label,
    SongSectionKind Kind,
    double StartSeconds,
    double EndSeconds,
    double Confidence,
    AnalysisValueSource Source)
{
    public static SongSectionResponse FromDomain(SongSection section) => new(
        section.Id,
        section.Label,
        section.Kind,
        section.StartSeconds,
        section.EndSeconds,
        section.Confidence,
        section.Source);
}

public sealed record WaveformBucketResponse(
    double StartSeconds,
    double EndSeconds,
    double Minimum,
    double Maximum,
    double Rms)
{
    public static WaveformBucketResponse FromDomain(WaveformBucket bucket) => new(
        bucket.StartSeconds,
        bucket.EndSeconds,
        bucket.Minimum,
        bucket.Maximum,
        bucket.Rms);
}

public sealed record EnergyPointResponse(double TimeSeconds, double Value)
{
    public static EnergyPointResponse FromDomain(EnergyPoint point) => new(point.TimeSeconds, point.Value);
}

public sealed record BeatMarkerResponse(double TimeSeconds, double Confidence)
{
    public static BeatMarkerResponse FromDomain(BeatMarker beat) => new(beat.TimeSeconds, beat.Confidence);
}

public sealed record SongAnalysisResponse(
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
    IReadOnlyList<WaveformBucketResponse> Waveform,
    IReadOnlyList<EnergyPointResponse> Energy,
    IReadOnlyList<BeatMarkerResponse> Beats,
    IReadOnlyList<SongSectionResponse> Sections,
    DateTimeOffset CreatedUtc)
{
    public static SongAnalysisResponse FromDomain(SongAnalysis analysis) => new(
        analysis.Id,
        analysis.ProjectId,
        analysis.SourceAssetId,
        analysis.Version,
        analysis.DurationSeconds,
        analysis.Bpm,
        analysis.SampleRate,
        analysis.Channels,
        analysis.Codec,
        analysis.BitRate,
        analysis.Waveform.Select(WaveformBucketResponse.FromDomain).ToArray(),
        analysis.Energy.Select(EnergyPointResponse.FromDomain).ToArray(),
        analysis.Beats.Select(BeatMarkerResponse.FromDomain).ToArray(),
        analysis.Sections.Select(SongSectionResponse.FromDomain).ToArray(),
        analysis.CreatedUtc);
}
