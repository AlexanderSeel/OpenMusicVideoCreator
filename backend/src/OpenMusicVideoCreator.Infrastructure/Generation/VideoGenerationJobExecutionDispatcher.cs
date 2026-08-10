using System.Text;
using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Media;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class VideoGenerationJobExecutionDispatcher : IJobExecutionDispatcher
{
    private const string MockMp4Base64 = "AAAAIGZ0eXBpc29tAAACAGlzb21pc28yYXZjMW1wNDEAAAPVbW9vdgAAAGxtdmhkAAAAAAAAAAAAAAAAAAAD6AAAAggAAQAAAQAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAwB0cmFrAAAAXHRraGQAAAADAAAAAAAAAAAAAAABAAAAAAAAAggAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAEAAAABAAAAAAAAkZWR0cwAAABxlbHN0AAAAAAAAAAEAAAIIAAAEAAABAAAAAAJ4bWRpYQAAACBtZGhkAAAAAAAAAAAAAAAAAAAyAAAAGgBVxAAAAAAALWhkbHIAAAAAAAAAAHZpZGUAAAAAAAAAAAAAAABWaWRlb0hhbmRsZXIAAAACI21pbmYAAAAUdm1oZAAAAAEAAAAAAAAAAAAAACRkaW5mAAAAHGRyZWYAAAAAAAAAAQAAAAx1cmwgAAAAAQAAAeNzdGJsAAAAv3N0c2QAAAAAAAAAAQAAAK9hdmMxAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAEAAQABIAAAASAAAAAAAAAABFUxhdmM2MS4xOS4xMDEgbGlieDI2NAAAAAAAAAAAAAAAGP//AAAANWF2Y0MBZAAK/+EAGGdkAAqs2UQmwEQAAAMABAAAAwDIPEiWWAEABmjr48siwP34+AAAAAAQcGFzcAAAAAEAAAABAAAAFGJ0cnQAAAAAAAA1mwAAAAAAAAAYc3R0cwAAAAAAAAABAAAADQAAAgAAAAAUc3RzcwAAAAAAAAABAAAAAQAAAHhjdHRzAAAAAAAAAA0AAAABAAAEAAAAAAEAAAoAAAAAAQAABAAAAAABAAAAAAAAAAEAAAIAAAAAAQAACgAAAAABAAAEAAAAAAEAAAAAAAAAAQAAAgAAAAABAAAKAAAAAAEAAAQAAAAAAQAAAAAAAAABAAACAAAAABxzdHNjAAAAAAAAAAEAAAABAAAADQAAAAEAAABIc3RzegAAAAAAAAAAAAAADQAAAtYAAAAOAAAADAAAAAwAAAAMAAAAFAAAAA4AAAAMAAAADAAAABQAAAAOAAAADAAAAAwAAAAUc3RjbwAAAAAAAAABAAAEBQAAAGF1ZHRhAAAAWW1ldGEAAAAAAAAAIWhkbHIAAAAAAAAAAG1kaXJhcHBsAAAAAAAAAAAAAAAALGlsc3QAAAAkqXRvbwAAABxkYXRhAAAAAQAAAABMYXZmNjEuNy4xMDMAAAAIZnJlZQAAA4RtZGF0AAACrgYF//+q3EXpvebZSLeWLNgg2SPu73gyNjQgLSBjb3JlIDE2NCByMzEwOCAzMWUxOWY5IC0gSC4yNjQvTVBFRy00IEFWQyBjb2RlYyAtIENvcHlsZWZ0IDIwMDMtMjAyMyAtIGh0dHA6Ly93d3cudmlkZW9sYW4ub3JnL3gyNjQuaHRtbCAtIG9wdGlvbnM6IGNhYmFjPTEgcmVmPTMgZGVibG9jaz0xOjA6MCBhbmFseXNlPTB4MzoweDExMyBtZT1oZXggc3VibWU9NyBwc3k9MSBwc3lfcmQ9MS4wMDowLjAwIG1peGVkX3JlZj0xIG1lX3JhbmdlPTE2IGNocm9tYV9tZT0xIHRyZWxsaXM9MSA4eDhkY3Q9MSBjcW09MCBkZWFkem9uZT0yMSwxMSBmYXN0X3Bza2lwPTEgY2hyb21hX3FwX29mZnNldD0tMiB0aHJlYWRzPTIgbG9va2FoZWFkX3RocmVhZHM9MSBzbGljZWRfdGhyZWFkcz0wIG5yPTAgZGVjaW1hdGU9MSBpbnRlcmxhY2VkPTAgYmx1cmF5X2NvbXBhdD0wIGNvbnN0cmFpbmVkX2ludHJhPTAgYmZyYW1lcz0zIGJfcHlyYW1pZD0yIGJfYWRhcHQ9MSBiX2JpYXM9MCBkaXJlY3Q9MSB3ZWlnaHRiPTEgb3Blbl9nb3A9MCB3ZWlnaHRwPTIga2V5aW50PTI1MCBrZXlpbnRfbWluPTI1IHNjZW5lY3V0PTQwIGludHJhX3JlZnJlc2g9MCByY19sb29rYWhlYWQ9NDAgcmM9Y3JmIG1idHJlZT0xIGNyZj0yMy4wIHFjb21wPTAuNjAgcXBtaW49MCBxcG1heD02OSBxcHN0ZXA9NCBpcF9yYXRpbz0xLjQwIGFxPTE6MS4wMACAAAAAIGWIhAA7//73Tr8Cm1TCKgOSVwrqg7oK2KdPKm0Gjfu5AAAACkGaJGxDf/6nj4gAAAAIQZ5CeIX/CbkAAAAIAZ5hdEK/DDgAAAAIAZ5jakK/DDkAAAAQQZpoSahBaJlMCGf//p4t8QAAAApBnoZFESwv/wm5AAAACAGepXRCvww5AAAACAGep2pCvww4AAAAEEGarEmoQWyZTAhX//44jcAAAAAKQZ7KRRUsL/8JuQAAAAgBnul0Qr8MOAAAAAgBnutqQr8MOA==";

    private readonly GenerationJobExecutionDispatcher _fallback;
    private readonly IImageToVideoProviderResolver _videoProviders;
    private readonly ClipVariantService _clips;
    private readonly IMediaStorage _mediaStorage;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;

    public VideoGenerationJobExecutionDispatcher(
        GenerationJobExecutionDispatcher fallback,
        IImageToVideoProviderResolver videoProviders,
        ClipVariantService clips,
        IMediaStorage mediaStorage,
        IMediaAssetRepository mediaAssets,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider)
    {
        _fallback = fallback;
        _videoProviders = videoProviders;
        _clips = clips;
        _mediaStorage = mediaStorage;
        _mediaAssets = mediaAssets;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public async Task<JobExecutionResult> ExecuteAsync(GenerationJob job, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.Type, VideoGenerationCoordinator.JobType, StringComparison.Ordinal))
        {
            return await _fallback.ExecuteAsync(job, cancellationToken);
        }

        SceneVideoGenerationJobPayload payload;
        try
        {
            payload = VideoGenerationCoordinator.DeserializePayload(job.PayloadJson);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                $"Video job payload is invalid: {exception.Message}",
                Retryable: false));
        }

        if (job.ProjectId is not Guid projectId || job.SceneId is not Guid sceneId ||
            string.IsNullOrWhiteSpace(job.ProviderId) || string.IsNullOrWhiteSpace(job.ModelId))
        {
            await TryMarkStateAsync(job.ProjectId, payload.VariantId, GenerationVariantState.Failed, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Scene video generation requires project, scene, provider, and model identifiers.",
                Retryable: false));
        }

        var clip = await _clips.GetAsync(projectId, payload.VariantId, cancellationToken);
        if (clip is null || clip.SceneId != sceneId || clip.JobId != job.Id ||
            clip.PromptVersionId != payload.PromptVersionId ||
            clip.StartKeyframeVariantId != payload.StartKeyframeVariantId ||
            clip.EndKeyframeVariantId != payload.EndKeyframeVariantId)
        {
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Video job provenance does not match its persisted clip variant.",
                Retryable: false));
        }

        await _clips.MarkStateAsync(projectId, clip.Id, GenerationVariantState.Generating, cancellationToken);

        var candidates = new List<VideoProviderCandidate>
        {
            new(job.ProviderId, job.ModelId),
        };
        if (payload.AllowFallback)
        {
            foreach (var candidate in payload.Fallbacks ?? [])
            {
                if (candidates.Any(existing => string.Equals(existing.ProviderId, candidate.ProviderId, StringComparison.Ordinal) &&
                                               string.Equals(existing.ModelId, candidate.ModelId, StringComparison.Ordinal)))
                {
                    continue;
                }
                candidates.Add(candidate);
            }
        }

        ProviderExecution? execution = null;
        ProviderFailure? finalFailure = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            ProviderResult<ProviderAsset> result;
            try
            {
                var provider = _videoProviders.Resolve(candidate.ProviderId);
                result = await provider.GenerateVideoAsync(
                    new ImageToVideoRequest(
                        candidate.ModelId,
                        payload.StartFrame,
                        payload.EndFrame,
                        payload.Prompt,
                        TimeSpan.FromSeconds(payload.DurationSeconds)),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (KeyNotFoundException exception)
            {
                result = ProviderResult<ProviderAsset>.Failed(new ProviderFailure(
                    ProviderFailureCode.UnsupportedCapability,
                    exception.Message,
                    Retryable: false));
            }
            catch (Exception exception)
            {
                result = ProviderResult<ProviderAsset>.Failed(new ProviderFailure(
                    ProviderFailureCode.TransientFailure,
                    $"Video provider execution failed: {exception.Message}",
                    Retryable: true));
            }

            if (result.IsSuccess && result.Value is not null)
            {
                execution = new ProviderExecution(candidate.ProviderId, candidate.ModelId, result);
                break;
            }

            finalFailure = result.Failure ?? new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                "Video provider returned no asset.",
                Retryable: false);
            var hasNext = index + 1 < candidates.Count;
            if (!hasNext || !CanFallback(finalFailure))
            {
                break;
            }
        }

        if (execution is null)
        {
            var failure = finalFailure ?? new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                "No image-to-video provider completed the request.",
                Retryable: false);
            await _clips.MarkStateAsync(projectId, clip.Id, VariantStateForFailure(failure), cancellationToken);
            return JobExecutionResult.Failed(failure);
        }

        if (!string.Equals(execution.ProviderId, clip.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(execution.ModelId, clip.ModelId, StringComparison.Ordinal))
        {
            clip = await _clips.UpdateProviderAsync(projectId, clip.Id, execution.ProviderId, execution.ModelId, cancellationToken);
        }

        var providerResult = execution.Result;
        try
        {
            var materialized = await MaterializeAsync(providerResult.Value!, cancellationToken);
            await using var content = materialized.Content;
            var fileName = $"scene-{sceneId:N}-clip-v{clip.VariantNumber}.{materialized.Extension}";
            var stored = await _mediaStorage.SaveAsync(projectId, MediaStorageArea.Generated, content, fileName, cancellationToken);
            var mediaId = Guid.NewGuid();
            var (width, height) = ParseResolution(payload.Resolution);
            await _mediaAssets.UpsertAsync(new MediaAssetMetadata(
                mediaId,
                projectId,
                stored.Location.Value,
                stored.ChecksumSha256,
                materialized.MimeType,
                providerResult.Value!.Width ?? width,
                providerResult.Value.Height ?? height,
                providerResult.Value.Duration ?? TimeSpan.FromSeconds(payload.DurationSeconds),
                stored.FileSize,
                MediaCreationSource.Generated,
                GetUtcNow()), cancellationToken);
            await _clips.CompleteAsync(projectId, clip.Id, mediaId, providerResult.Usage.ActualCost, cancellationToken);

            return new JobExecutionResult(
                JobState.Completed,
                providerResult.ProviderTaskId,
                ActualCost: providerResult.Usage.ActualCost,
                Currency: providerResult.Usage.Currency ?? clip.Currency);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _clips.MarkStateAsync(projectId, clip.Id, GenerationVariantState.Queued, cancellationToken);
            return JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                $"Generated video could not be persisted: {exception.Message}",
                Retryable: true));
        }
    }

    private async Task<MaterializedProviderAsset> MaterializeAsync(ProviderAsset asset, CancellationToken cancellationToken)
    {
        if (asset.Uri.StartsWith("mock://video/", StringComparison.OrdinalIgnoreCase))
        {
            return new MaterializedProviderAsset(
                new MemoryStream(Convert.FromBase64String(MockMp4Base64), writable: false),
                "video/mp4",
                "mp4");
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
            var client = _httpClientFactory.CreateClient(nameof(VideoGenerationJobExecutionDispatcher));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var mimeType = response.Content.Headers.ContentType?.MediaType ?? asset.MimeType;
            return new MaterializedProviderAsset(new MemoryStream(bytes, writable: false), mimeType, ExtensionFor(mimeType));
        }

        throw new InvalidDataException("Generated video asset URI uses an unsupported scheme.");
    }

    private async Task TryMarkStateAsync(
        Guid? projectId,
        Guid variantId,
        GenerationVariantState state,
        CancellationToken cancellationToken)
    {
        if (projectId is not Guid id) return;
        try
        {
            await _clips.MarkStateAsync(id, variantId, state, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
        }
    }

    private static bool CanFallback(ProviderFailure failure) => failure.Code is
        ProviderFailureCode.RateLimited or
        ProviderFailureCode.ProviderUnavailable or
        ProviderFailureCode.QuotaExhausted or
        ProviderFailureCode.InsufficientCredits or
        ProviderFailureCode.AuthenticationFailed or
        ProviderFailureCode.UnsupportedCapability or
        ProviderFailureCode.NetworkFailure or
        ProviderFailureCode.Timeout or
        ProviderFailureCode.TransientFailure;

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

    private static (int Width, int Height) ParseResolution(string resolution)
    {
        if (!SceneVideoGenerationSettings.TryParseResolution(resolution, out var width, out var height))
        {
            throw new InvalidDataException($"Invalid persisted video resolution '{resolution}'.");
        }
        return (width, height);
    }

    private static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "video/mp4" => "mp4",
        "video/webm" => "webm",
        "video/quicktime" => "mov",
        _ => "bin",
    };

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed record ProviderExecution(
        string ProviderId,
        string ModelId,
        ProviderResult<ProviderAsset> Result);

    private sealed record MaterializedProviderAsset(Stream Content, string MimeType, string Extension);
}
