using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Infrastructure.Generation;
using OpenMusicVideoCreator.Infrastructure.Jobs;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using OpenMusicVideoCreator.Infrastructure.Providers;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class VideoGenerationFlowTests
{
    [Fact]
    public async Task VideoGenerationSettings_SurviveRepositoryRecreation()
    {
        using var storage = new TemporaryStorage();
        var factory = new DuckDbConnectionFactory(storage.Options);
        await new DuckDbDatabase(factory).InitializeAsync();
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var expected = new SceneVideoGenerationSettings(
            projectId,
            sceneId,
            "mock-video",
            "mock-video-v1",
            true,
            "1920x1080",
            8,
            false,
            FixedUtc());
        var first = new DuckDbVideoGenerationSettingsRepository(new DuckDbSettingsRepository(factory));

        await first.UpsertAsync(expected);

        var recreated = new DuckDbVideoGenerationSettingsRepository(
            new DuckDbSettingsRepository(new DuckDbConnectionFactory(storage.Options)));
        Assert.Equal(expected, await recreated.GetAsync(projectId, sceneId));
    }

    [Fact]
    public async Task MockVideoJob_CompletesNewClipAndKeepsOlderSelectedVariant()
    {
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var startKeyframeId = Guid.NewGuid();
        var clipRepository = new InMemoryClipRepository();
        var clips = new ClipVariantService(clipRepository, new FixedTimeProvider());
        var first = await clips.RegisterPlannedAsync(
            projectId, sceneId, promptId, startKeyframeId, null,
            "mock-video", "mock-video-v1", TimeSpan.FromSeconds(6), "16:9", "1280x720", 0m, "USD");
        var firstJob = Guid.NewGuid();
        await clips.AttachJobAsync(projectId, first.Id, firstJob);
        await clips.CompleteAsync(projectId, first.Id, Guid.NewGuid(), 0m);
        await clips.SelectAsync(projectId, first.Id);

        var queued = await clips.RegisterPlannedAsync(
            projectId, sceneId, promptId, startKeyframeId, null,
            "mock-video", "mock-video-v1", TimeSpan.FromSeconds(6), "16:9", "1280x720", 0m, "USD");
        var jobId = Guid.NewGuid();
        queued = await clips.AttachJobAsync(projectId, queued.Id, jobId);
        var payload = new SceneVideoGenerationJobPayload(
            queued.Id,
            promptId,
            startKeyframeId,
            null,
            "animate the approved frame",
            new MediaLocation("project/keyframes/start.svg"),
            null,
            6,
            "16:9",
            "1280x720",
            true,
            []);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var job = new GenerationJob(
            jobId, projectId, sceneId, null, VideoGenerationCoordinator.JobType,
            JsonSerializer.Serialize(payload, jsonOptions), "mock-video", "mock-video-v1",
            JobState.Submitting, null, 100, 1, 0, 2,
            FixedUtc(), FixedUtc(), null, FixedUtc(), null, null, null, null,
            0m, null, "USD", "test-worker", FixedUtc().AddMinutes(1));
        var mediaStorage = new InMemoryMediaStorage();
        var mediaAssets = new InMemoryMediaAssetRepository();
        var control = new MockProviderControl();
        var mockVideo = new MockVideoProvider(control);
        var keyframeFallback = new GenerationJobExecutionDispatcher(
            new MockJobExecutionDispatcher(),
            new ImageGenerationProviderResolver(new MockImageProvider(control)),
            new KeyframeVariantService(new InMemoryKeyframeRepository(), new FixedTimeProvider()),
            mediaStorage,
            mediaAssets,
            new UnusedHttpClientFactory(),
            new FixedTimeProvider());
        var dispatcher = new VideoGenerationJobExecutionDispatcher(
            keyframeFallback,
            new ImageToVideoProviderResolver(mockVideo),
            clips,
            mediaStorage,
            mediaAssets,
            new UnusedHttpClientFactory(),
            new FixedTimeProvider());

        var result = await dispatcher.ExecuteAsync(job);

        Assert.Equal(JobState.Completed, result.State);
        var variants = await clips.ListSceneAsync(projectId, sceneId);
        Assert.Equal(2, variants.Count);
        Assert.True(variants.Single(item => item.Id == first.Id).IsSelected);
        var completed = variants.Single(item => item.Id == queued.Id);
        Assert.Equal(GenerationVariantState.Completed, completed.State);
        Assert.NotNull(completed.MediaAssetId);
        Assert.False(completed.IsSelected);
        var media = await mediaAssets.GetAsync(completed.MediaAssetId!.Value);
        Assert.NotNull(media);
        Assert.Equal("video/mp4", media!.MimeType);
        Assert.Equal(MediaCreationSource.Generated, media.CreationSource);
        Assert.Equal(projectId, media.ProjectId);
        Assert.NotEmpty(mediaStorage.LastSavedBytes);
    }

    [Fact]
    public async Task ClipSelection_IsNonDestructiveAndSelectedVariantCannotBeDeleted()
    {
        var repository = new InMemoryClipRepository();
        var clips = new ClipVariantService(repository, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var startId = Guid.NewGuid();
        var first = await CompleteAsync(clips, projectId, sceneId, promptId, startId);
        var second = await CompleteAsync(clips, projectId, sceneId, promptId, startId);

        await clips.SelectAsync(projectId, first.Id);
        await clips.SelectAsync(projectId, second.Id);

        var variants = await clips.ListSceneAsync(projectId, sceneId);
        Assert.False(variants.Single(item => item.Id == first.Id).IsSelected);
        Assert.True(variants.Single(item => item.Id == second.Id).IsSelected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => clips.DeleteAsync(projectId, second.Id));
        Assert.Equal(2, (await clips.ListSceneAsync(projectId, sceneId)).Count);
    }

    private static async Task<SceneClipVariant> CompleteAsync(
        ClipVariantService clips,
        Guid projectId,
        Guid sceneId,
        Guid promptId,
        Guid startId)
    {
        var clip = await clips.RegisterPlannedAsync(
            projectId, sceneId, promptId, startId, null,
            "mock-video", "mock-video-v1", TimeSpan.FromSeconds(5), "16:9", "1280x720", 0m, "USD");
        await clips.AttachJobAsync(projectId, clip.Id, Guid.NewGuid());
        return await clips.CompleteAsync(projectId, clip.Id, Guid.NewGuid(), 0m);
    }

    private static DateTimeOffset FixedUtc() => new(2026, 8, 10, 13, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed class InMemoryClipRepository : IClipVariantRepository
    {
        private readonly Dictionary<Guid, SceneClipVariant> _items = [];
        public Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SceneClipVariant>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());
        public Task<SceneClipVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(variantId, out var value) && value.ProjectId == projectId ? value : null);
        public Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default) { _items[variant.Id] = variant; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(variantId, out var value) && value.ProjectId == projectId && _items.Remove(variantId));
    }

    private sealed class InMemoryKeyframeRepository : IKeyframeVariantRepository
    {
        public Task<IReadOnlyList<KeyframeVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KeyframeVariant>>([]);
        public Task<KeyframeVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult<KeyframeVariant?>(null);
        public Task UpsertAsync(KeyframeVariant variant, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class InMemoryMediaStorage : IMediaStorage
    {
        public byte[] LastSavedBytes { get; private set; } = [];
        public Task EnsureProjectLayoutAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<StoredMedia> SaveAsync(Guid projectId, MediaStorageArea area, Stream source, string fileName, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            LastSavedBytes = buffer.ToArray();
            var checksum = Convert.ToHexString(SHA256.HashData(LastSavedBytes)).ToLowerInvariant();
            return new StoredMedia(new MediaLocation($"{projectId:N}/generated/{fileName}"), LastSavedBytes.Length, checksum);
        }
        public Task<Stream> OpenReadAsync(MediaLocation location, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(LastSavedBytes, writable: false));
        public Task<bool> DeleteAsync(MediaLocation location, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class InMemoryMediaAssetRepository : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MediaAssetMetadata> _items = [];
        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaAssetMetadata>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());
        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default) { _items[asset.Id] = asset; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenMusicVideoCreator.VideoGenerationTests", Guid.NewGuid().ToString("N"));
        public TemporaryStorage()
        {
            Directory.CreateDirectory(_root);
            Options = new StorageOptions(Path.Combine(_root, "data", "app.duckdb"), Path.Combine(_root, "projects"));
        }
        public StorageOptions Options { get; }
        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
