using System.Text;
using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Infrastructure.Jobs;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class GenerationJobExecutionDispatcher : IJobExecutionDispatcher
{
    private readonly MockJobExecutionDispatcher _fallback;
    private readonly IImageGenerationProviderResolver _imageProviders;
    private readonly KeyframeVariantService _variants;
    private readonly IMediaStorage _mediaStorage;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;

    public GenerationJobExecutionDispatcher(
        MockJobExecutionDispatcher fallback,
        IImageGenerationProviderResolver imageProviders,
        KeyframeVariantService variants,
        IMediaStorage mediaStorage,
        IMediaAssetRepository mediaAssets,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider)
    {
        _fallback = fallback;
        _imageProviders = imageProviders;
        _variants = variants;
        _mediaStorage = mediaStorage;
        _mediaAssets = mediaAssets;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public async Task<JobExecutionResult> ExecuteAsync(
        GenerationJob job,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.Type, KeyframeGenerationCoordinator.JobType, StringComparison.Ordinal))
        {
            return await _fallback.ExecuteAsync(job, cancellationToken);
        }

        KeyframeGenerationJobPayload payload;
        try
        {
            payload = KeyframeGenerationCoordinator.DeserializePayload(job.PayloadJson);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                $"Keyframe job payload is invalid: {exception.Message}",
                Retryable: false));
        }

        if (job.ProjectId is not Guid projectId || job.SceneId is not Guid sceneId ||
            string.IsNullOrWhiteSpace(job.ProviderId) || string.IsNullOrWhiteSpace(job.ModelId))
        {
            await TryMarkStateAsync(job.ProjectId, payload.VariantId, GenerationVariantState.Failed, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Keyframe generation job requires project, scene, provider, and model identifiers.",
                Retryable: false));
        }

        var variant = await _variants.GetAsync(projectId, payload.VariantId, cancellationToken);
        if (variant is null || variant.SceneId != sceneId || variant.JobId != job.Id ||
            variant.PromptVersionId != payload.PromptVersionId || variant.Role != payload.Role)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Keyframe job provenance does not match its persisted variant.",
                Retryable: false));
        }

        await _variants.MarkStateAsync(projectId, variant.Id, GenerationVariantState.Generating, cancellationToken);

        ProviderResult<ProviderAsset> providerResult;
        try
        {
            var provider = _imageProviders.Resolve(job.ProviderId);
            providerResult = await provider.GenerateImageAsync(
                new ImageGenerationRequest(
                    job.ModelId,
                    payload.Prompt,
                    payload.Width,
                    payload.Height,
                    payload.References,
                    payload.Seed,
                    payload.NegativePrompt),
                cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            await _variants.MarkStateAsync(projectId, variant.Id, GenerationVariantState.Failed, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.UnsupportedCapability,
                exception.Message,
                Retryable: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _variants.MarkStateAsync(projectId, variant.Id, GenerationVariantState.Queued, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                $"Image provider execution failed: {exception.Message}",
                Retryable: true));
        }

        if (!providerResult.IsSuccess || providerResult.Value is null)
        {
            var failure = providerResult.Failure ?? new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                "Image provider returned no asset.",
                Retryable: false);
            await _variants.MarkStateAsync(projectId, variant.Id, VariantStateForFailure(failure), cancellationToken);
            return JobExecutionResult.Failed(failure);
        }

        try
        {
            var materialized = await MaterializeAsync(providerResult.Value, payload.Width, payload.Height, cancellationToken);
            await using var content = materialized.Content;
            var fileName = $"scene-{sceneId:N}-{payload.Role.ToString().ToLowerInvariant()}-v{variant.VariantNumber}.{materialized.Extension}";
            var stored = await _mediaStorage.SaveAsync(
                projectId,
                MediaStorageArea.Keyframe,
                content,
                fileName,
                cancellationToken);
            var mediaId = Guid.NewGuid();
            await _mediaAssets.UpsertAsync(new MediaAssetMetadata(
                mediaId,
                projectId,
                stored.Location.Value,
                stored.ChecksumSha256,
                materialized.MimeType,
                providerResult.Value.Width ?? payload.Width,
                providerResult.Value.Height ?? payload.Height,
                null,
                stored.FileSize,
                MediaCreationSource.Generated,
                GetUtcNow()), cancellationToken);
            await _variants.CompleteAsync(projectId, variant.Id, mediaId, providerResult.Usage.ActualCost, cancellationToken);

            return new JobExecutionResult(
                JobState.Completed,
                providerResult.ProviderTaskId,
                ActualCost: providerResult.Usage.ActualCost,
                Currency: providerResult.Usage.Currency ?? variant.Currency);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _variants.MarkStateAsync(projectId, variant.Id, GenerationVariantState.Queued, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                $"Generated keyframe could not be persisted: {exception.Message}",
                Retryable: true));
        }
    }

    private async Task<MaterializedProviderAsset> MaterializeAsync(
        ProviderAsset asset,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        if (asset.Uri.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
        {
            var svg = $"""
                <svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
                  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#081923"/><stop offset="1" stop-color="#153d51"/></linearGradient></defs>
                  <rect width="100%" height="100%" fill="url(#g)"/>
                  <circle cx="50%" cy="43%" r="18%" fill="#66d4ff" fill-opacity=".16" stroke="#66d4ff" stroke-opacity=".55" stroke-width="4"/>
                  <text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" fill="#d8f5ff" font-family="system-ui,sans-serif" font-size="{Math.Max(18, Math.Min(width, height) / 18)}">Mock keyframe</text>
                </svg>
                """;
            return new MaterializedProviderAsset(
                new MemoryStream(Encoding.UTF8.GetBytes(svg), writable: false),
                "image/svg+xml",
                "svg");
        }

        if (asset.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = asset.Uri.IndexOf(',');
            if (comma <= 5) throw new InvalidDataException("Generated data URI is malformed.");
            var header = asset.Uri[5..comma];
            var data = asset.Uri[(comma + 1)..];
            var mimeType = header.Split(';', 2)[0];
            var bytes = header.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(data)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
            return new MaterializedProviderAsset(new MemoryStream(bytes, writable: false), mimeType, ExtensionFor(mimeType));
        }

        if (Uri.TryCreate(asset.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var client = _httpClientFactory.CreateClient(nameof(GenerationJobExecutionDispatcher));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var mimeType = response.Content.Headers.ContentType?.MediaType ?? asset.MimeType;
            return new MaterializedProviderAsset(new MemoryStream(bytes, writable: false), mimeType, ExtensionFor(mimeType));
        }

        throw new InvalidDataException("Generated provider asset URI uses an unsupported scheme.");
    }

    private async Task TryMarkStateAsync(Guid? projectId, Guid variantId, GenerationVariantState state, CancellationToken cancellationToken)
    {
        if (projectId is not Guid id) return;
        try
        {
            await _variants.MarkStateAsync(id, variantId, state, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
        }
    }

    private static GenerationVariantState VariantStateForFailure(ProviderFailure failure) =>
        failure.Retryable || failure.Code is
            ProviderFailureCode.RateLimited or
            ProviderFailureCode.ProviderUnavailable or
            ProviderFailureCode.QuotaExhausted or
            ProviderFailureCode.InsufficientCredits or
            ProviderFailureCode.NetworkFailure or
            ProviderFailureCode.Timeout or
            ProviderFailureCode.TransientFailure
                ? GenerationVariantState.Queued
                : GenerationVariantState.Failed;

    private static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" or "image/jpg" => "jpg",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        _ => "bin",
    };

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed record MaterializedProviderAsset(Stream Content, string MimeType, string Extension);
}
