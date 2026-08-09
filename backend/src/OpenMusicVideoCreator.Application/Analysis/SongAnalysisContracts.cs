using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Analysis;

namespace OpenMusicVideoCreator.Application.Analysis;

public sealed record MediaProbeResult(
    double DurationSeconds,
    int? SampleRate,
    int? Channels,
    string? Codec,
    long? BitRate);

public sealed record AudioSignalAnalysis(
    IReadOnlyList<WaveformBucket> Waveform,
    IReadOnlyList<EnergyPoint> Energy,
    IReadOnlyList<BeatMarker> Beats,
    double? Bpm);

public interface IMediaProbe
{
    Task<MediaProbeResult> ProbeAsync(
        MediaLocation location,
        CancellationToken cancellationToken = default);
}

public interface IAudioSignalAnalyzer
{
    Task<AudioSignalAnalysis> AnalyzeAsync(
        MediaLocation location,
        double durationSeconds,
        CancellationToken cancellationToken = default);
}

public interface ISongAnalysisRepository
{
    Task<SongAnalysis?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<SongAnalysis?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SongAnalysis>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(SongAnalysis analysis, CancellationToken cancellationToken = default);
}
