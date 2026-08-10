using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Application.Rendering;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Infrastructure.Generation;

namespace OpenMusicVideoCreator.Infrastructure.Rendering;

public sealed class ProjectRenderJobExecutionDispatcher : IJobExecutionDispatcher
{
    private readonly VideoGenerationJobExecutionDispatcher _fallback;
    private readonly ProjectRenderService _renders;
    private readonly IProjectRenderEngine _engine;
    private readonly IMediaStorage _mediaStorage;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IMediaProbe _mediaProbe;
    private readonly TimeProvider _timeProvider;

    public ProjectRenderJobExecutionDispatcher(
        VideoGenerationJobExecutionDispatcher fallback,
        ProjectRenderService renders,
        IProjectRenderEngine engine,
        IMediaStorage mediaStorage,
        IMediaAssetRepository mediaAssets,
        IMediaProbe mediaProbe,
        TimeProvider timeProvider)
    {
        _fallback = fallback;
        _renders = renders;
        _engine = engine;
        _mediaStorage = mediaStorage;
        _mediaAssets = mediaAssets;
        _mediaProbe = mediaProbe;
        _timeProvider = timeProvider;
    }

    public async Task<JobExecutionResult> ExecuteAsync(GenerationJob job, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.Type, ProjectRenderService.JobType, StringComparison.Ordinal))
        {
            return await _fallback.ExecuteAsync(job, cancellationToken);
        }

        ProjectRenderJobPayload payload;
        try
        {
            payload = ProjectRenderService.DeserializePayload(job.PayloadJson);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                $"Render job payload is invalid: {exception.Message}",
                Retryable: false));
        }

        if (job.ProjectId is not Guid projectId)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Render job requires a project identifier.",
                Retryable: false));
        }

        var render = await _renders.GetAsync(projectId, payload.RenderId, cancellationToken);
        if (render is null || render.JobId != job.Id)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Render job provenance does not match its persisted render record.",
                Retryable: false));
        }
        if (render.State == ProjectRenderState.Cancelled)
        {
            return new JobExecutionResult(JobState.Cancelled);
        }

        render = await _renders.MarkRenderingAsync(projectId, render.Id, cancellationToken);
        if (render.State == ProjectRenderState.Cancelled)
        {
            return new JobExecutionResult(JobState.Cancelled);
        }

        MediaLocation? storedLocation = null;
        Guid? outputMediaId = null;
        try
        {
            await using var result = await _engine.RenderAsync(render.Manifest, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var area = render.Manifest.Kind == ProjectRenderKind.Preview ? MediaStorageArea.Proxy : MediaStorageArea.Render;
            var fileName = $"render-v{render.Version}-{render.Manifest.Kind.ToString().ToLowerInvariant()}.mp4";
            var stored = await _mediaStorage.SaveAsync(
                projectId,
                area,
                result.Content,
                fileName,
                cancellationToken);
            storedLocation = stored.Location;

            var probe = await _mediaProbe.ProbeAsync(stored.Location, cancellationToken);
            ValidateProbe(render.Manifest, probe);
            cancellationToken.ThrowIfCancellationRequested();

            outputMediaId = Guid.NewGuid();
            await _mediaAssets.UpsertAsync(new MediaAssetMetadata(
                outputMediaId.Value,
                projectId,
                stored.Location.Value,
                stored.ChecksumSha256,
                result.MimeType,
                result.Width,
                result.Height,
                TimeSpan.FromSeconds(probe.DurationSeconds),
                stored.FileSize,
                MediaCreationSource.Rendered,
                GetUtcNow()), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await _renders.CompleteAsync(projectId, render.Id, outputMediaId.Value, result.CommandLog, cancellationToken);
            storedLocation = null;
            outputMediaId = null;
            return JobExecutionResult.Completed(actualCost: 0m, currency: "USD");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupOutputAsync(storedLocation, outputMediaId);
            throw;
        }
        catch (FileNotFoundException exception)
        {
            await CleanupOutputAsync(storedLocation, outputMediaId);
            await _renders.FailAsync(projectId, render.Id, exception.Message, null, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                exception.Message,
                Retryable: false,
                ProviderCode: "render_source_missing"));
        }
        catch (InvalidDataException exception)
        {
            await CleanupOutputAsync(storedLocation, outputMediaId);
            await _renders.FailAsync(projectId, render.Id, exception.Message, null, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                exception.Message,
                Retryable: false,
                ProviderCode: "render_invalid_media"));
        }
        catch (Exception exception)
        {
            await CleanupOutputAsync(storedLocation, outputMediaId);
            var message = $"Project rendering failed: {exception.Message}";
            if (job.RetryCount < job.MaxRetries)
            {
                await _renders.MarkRetryPendingAsync(projectId, render.Id, message, null, cancellationToken);
            }
            else
            {
                await _renders.FailAsync(projectId, render.Id, message, null, cancellationToken);
            }

            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                message,
                Retryable: true,
                ProviderCode: "render_transient"));
        }
    }

    private async Task CleanupOutputAsync(MediaLocation? storedLocation, Guid? mediaId)
    {
        if (mediaId is Guid id)
        {
            try
            {
                await _mediaAssets.DeleteAsync(id, CancellationToken.None);
            }
            catch
            {
                // Best effort: original exception/cancellation remains authoritative.
            }
        }

        if (storedLocation is MediaLocation location)
        {
            try
            {
                await _mediaStorage.DeleteAsync(location, CancellationToken.None);
            }
            catch
            {
                // Best effort: source/generated media is never touched by this cleanup.
            }
        }
    }

    private static void ValidateProbe(ProjectRenderManifest manifest, MediaProbeResult probe)
    {
        var durationTolerance = Math.Max(0.15, 2d / manifest.FramesPerSecond);
        if (!double.IsFinite(probe.DurationSeconds) || probe.DurationSeconds <= 0 ||
            Math.Abs(probe.DurationSeconds - manifest.DurationSeconds) > durationTolerance)
        {
            throw new InvalidDataException(
                $"Rendered duration {probe.DurationSeconds:0.###}s does not match manifest duration {manifest.DurationSeconds:0.###}s within {durationTolerance:0.###}s.");
        }

        if (probe.Channels is null or <= 0)
        {
            throw new InvalidDataException("Rendered MP4 does not contain a valid audio stream from the original Song source.");
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
