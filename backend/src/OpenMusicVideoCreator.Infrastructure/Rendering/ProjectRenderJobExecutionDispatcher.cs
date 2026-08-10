using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
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
    private readonly TimeProvider _timeProvider;

    public ProjectRenderJobExecutionDispatcher(
        VideoGenerationJobExecutionDispatcher fallback,
        ProjectRenderService renders,
        IProjectRenderEngine engine,
        IMediaStorage mediaStorage,
        IMediaAssetRepository mediaAssets,
        TimeProvider timeProvider)
    {
        _fallback = fallback;
        _renders = renders;
        _engine = engine;
        _mediaStorage = mediaStorage;
        _mediaAssets = mediaAssets;
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

        await _renders.MarkRenderingAsync(projectId, render.Id, cancellationToken);
        try
        {
            await using var result = await _engine.RenderAsync(render.Manifest, cancellationToken);
            var area = render.Manifest.Kind == ProjectRenderKind.Preview ? MediaStorageArea.Proxy : MediaStorageArea.Render;
            var fileName = $"render-v{render.Version}-{render.Manifest.Kind.ToString().ToLowerInvariant()}.mp4";
            var stored = await _mediaStorage.SaveAsync(
                projectId,
                area,
                result.Content,
                fileName,
                cancellationToken);
            var mediaId = Guid.NewGuid();
            await _mediaAssets.UpsertAsync(new MediaAssetMetadata(
                mediaId,
                projectId,
                stored.Location.Value,
                stored.ChecksumSha256,
                result.MimeType,
                result.Width,
                result.Height,
                result.Duration,
                stored.FileSize,
                MediaCreationSource.Rendered,
                GetUtcNow()), cancellationToken);
            await _renders.CompleteAsync(projectId, render.Id, mediaId, result.CommandLog, cancellationToken);
            return JobExecutionResult.Completed(actualCost: 0m, currency: "USD");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            await _renders.FailAsync(projectId, render.Id, exception.Message, null, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                exception.Message,
                Retryable: false,
                ProviderCode: "render_source_missing"));
        }
        catch (InvalidDataException exception)
        {
            await _renders.FailAsync(projectId, render.Id, exception.Message, null, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                exception.Message,
                Retryable: false,
                ProviderCode: "render_invalid_media"));
        }
        catch (Exception exception)
        {
            await _renders.FailAsync(projectId, render.Id, exception.Message, null, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                $"Project rendering failed: {exception.Message}",
                Retryable: true,
                ProviderCode: "render_transient"));
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
