using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Infrastructure.Generation;
using OpenMusicVideoCreator.Infrastructure.Jobs;
using OpenMusicVideoCreator.Infrastructure.Providers;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class VideoFallbackTests
{
    [Fact]
    public async Task QuotaFailure_UsesConfiguredFallbackAndPersistsActualProvider()
    {
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var startKeyframeId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var clipRepository = new ClipRepository();
        var clips = new ClipVariantService(clipRepository, new FixedTimeProvider());
        var clip = await clips.RegisterPlannedAsync(
            projectId, sceneId, promptId, startKeyframeId, null,
            "primary-video", "primary-v1", TimeSpan.FromSeconds(6), "16:9", "1280x720", null, "USD");
        clip = await clips.AttachJobAsync(projectId, clip.Id, jobId);

        var payload = new SceneVideoGenerationJobPayload(
            clip.Id,
            promptId,
            startKeyframeId,
            null,
            "animate",
            new MediaLocation("project/keyframes/start.png"),
            null,
            6,
            "16:9",
            "1280x720",
            true,
            [new VideoProviderCandidate("fallback-video", "fallback-v1")]);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var job = new GenerationJob(
            jobId, projectId, sceneId, null, VideoGenerationCoordinator.JobType,
            JsonSerializer.Serialize(payload, jsonOptions), "primary-video", "primary-v1",
            JobState.Submitting, null, 100, 1, 0, 2,
            FixedUtc(), FixedUtc(), null, FixedUtc(), null, null, null, null,
            null, null, "USD", "worker", FixedUtc().AddMinutes(1));
        var storage = new MediaStorage();
        var mediaAssets = new MediaAssets();
        var fallbackDispatcher = CreateFallbackDispatcher(storage, mediaAssets);
        var resolver = new ProviderResolver(
            new FailingProvider(),
            new SuccessfulProvider());
        var dispatcher = new VideoGenerationJobExecutionDispatcher(
            fallbackDispatcher,
            resolver,
            clips,
            storage,
            mediaAssets,
            new HttpFactory(),
            new FixedTimeProvider());

        var result = await dispatcher.ExecuteAsync(job);

        Assert.Equal(JobState.Completed, result.State);
        var completed = await clips.GetAsync(projectId, clip.Id);
        Assert.NotNull(completed);
        Assert.Equal("fallback-video", completed!.ProviderId);
        Assert.Equal("fallback-v1", completed.ModelId);
        Assert.Equal(GenerationVariantState.Completed, completed.State);
        Assert.NotNull(completed.MediaAssetId);
    }

    [Fact]
    public async Task DisabledFallback_ReturnsPrimaryQuotaFailureWithoutTryingAlternative()
    {
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var startKeyframeId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var repository = new ClipRepository();
        var clips = new ClipVariantService(repository, new FixedTimeProvider());
        var clip = await clips.RegisterPlannedAsync(
            projectId, sceneId, promptId, startKeyframeId, null,
            "primary-video", "primary-v1", TimeSpan.FromSeconds(6), "16:9", "1280x720", null, "USD");
        clip = await clips.AttachJobAsync(projectId, clip.Id, jobId);
        var payload = new SceneVideoGenerationJobPayload(
            clip.Id, promptId, startKeyframeId, null, "animate",
            new MediaLocation("project/keyframes/start.png"), null, 6, "16:9", "1280x720",
            false,
            [new VideoProviderCandidate("fallback-video", "fallback-v1")]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var job = new GenerationJob(
            jobId, projectId, sceneId, null, VideoGenerationCoordinator.JobType,
            JsonSerializer.Serialize(payload, options), "primary-video", "primary-v1",
            JobState.Submitting, null, 100, 1, 0, 2,
            FixedUtc(), FixedUtc(), null, FixedUtc(), null, null, null, null,
            null, null, "USD", "worker", FixedUtc().AddMinutes(1));
        var storage = new MediaStorage();
        var mediaAssets = new MediaAssets();
        var fallback = new CountingSuccessProvider();
        var dispatcher = new VideoGenerationJobExecutionDispatcher(
            CreateFallbackDispatcher(storage, mediaAssets),
            new ProviderResolver(new FailingProvider(), fallback),
            clips,
            storage,
            mediaAssets,
            new HttpFactory(),
            new FixedTimeProvider());

        var result = await dispatcher.ExecuteAsync(job);

        Assert.NotNull(result.Failure);
        Assert.Equal(ProviderFailureCode.QuotaExhausted, result.Failure!.Code);
        Assert.Equal(0, fallback.Calls);
        var persisted = await clips.GetAsync(projectId, clip.Id);
        Assert.Equal("primary-video", persisted!.ProviderId);
    }

    private static GenerationJobExecutionDispatcher CreateFallbackDispatcher(IMediaStorage storage, IMediaAssetRepository mediaAssets)
    {
        var control = new MockProviderControl();
        return new GenerationJobExecutionDispatcher(
            new MockJobExecutionDispatcher(),
            new ImageGenerationProviderResolver(new MockImageProvider(control)),
            new KeyframeVariantService(new EmptyKeyframes(), new FixedTimeProvider()),
            storage,
            mediaAssets,
            new HttpFactory(),
            new FixedTimeProvider());
    }

    private static DateTimeOffset FixedUtc() => new(2026, 8, 10, 13, 30, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed class FailingProvider : IImageToVideoProvider
    {
        public Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<ProviderAsset>.Failed(new ProviderFailure(
                ProviderFailureCode.QuotaExhausted,
                "quota",
                Retryable: false,
                ProviderCode: "quota")));
    }

    private class SuccessfulProvider : IImageToVideoProvider
    {
        public virtual Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<ProviderAsset>.Success(
                new ProviderAsset("mock://video/fallback.mp4", "video/mp4", Duration: request.Duration),
                providerTaskId: "fallback-task"));
    }

    private sealed class CountingSuccessProvider : SuccessfulProvider
    {
        public int Calls { get; private set; }
        public override Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return base.GenerateVideoAsync(request, cancellationToken);
        }
    }

    private sealed class ProviderResolver : IImageToVideoProviderResolver
    {
        private readonly IImageToVideoProvider _primary;
        private readonly IImageToVideoProvider _fallback;
        public ProviderResolver(IImageToVideoProvider primary, IImageToVideoProvider fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }
        public IImageToVideoProvider Resolve(string providerId) => providerId switch
        {
            "primary-video" => _primary,
            "fallback-video" => _fallback,
            _ => throw new KeyNotFoundException(providerId),
        };
    }

    private sealed class ClipRepository : IClipVariantRepository
    {
        private readonly Dictionary<Guid, SceneClipVariant> _items = [];
        public Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SceneClipVariant>>(_items.Values.Where(value => value.ProjectId == projectId).ToArray());
        public Task<SceneClipVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(variantId, out var value) && value.ProjectId == projectId ? value : null);
        public Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default) { _items[variant.Id] = variant; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_items.Remove(variantId));
    }

    private sealed class EmptyKeyframes : IKeyframeVariantRepository
    {
        public Task<IReadOnlyList<KeyframeVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KeyframeVariant>>([]);
        public Task<KeyframeVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult<KeyframeVariant?>(null);
        public Task UpsertAsync(KeyframeVariant variant, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class MediaStorage : IMediaStorage
    {
        private byte[] _bytes = [];
        public Task EnsureProjectLayoutAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<StoredMedia> SaveAsync(Guid projectId, MediaStorageArea area, Stream source, string fileName, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            _bytes = buffer.ToArray();
            return new StoredMedia(new MediaLocation($"{projectId:N}/generated/{fileName}"), _bytes.Length, Convert.ToHexString(SHA256.HashData(_bytes)).ToLowerInvariant());
        }
        public Task<Stream> OpenReadAsync(MediaLocation location, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        public Task<bool> DeleteAsync(MediaLocation location, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class MediaAssets : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MediaAssetMetadata> _items = [];
        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaAssetMetadata>>(_items.Values.Where(value => value.ProjectId == projectId).ToArray());
        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default) { _items[asset.Id] = asset; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class HttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
