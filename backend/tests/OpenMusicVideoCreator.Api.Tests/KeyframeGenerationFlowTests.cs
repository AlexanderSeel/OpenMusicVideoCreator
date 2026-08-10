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

public sealed class KeyframeGenerationFlowTests
{
    [Fact]
    public async Task SceneGenerationSettings_SurviveRepositoryRecreation()
    {
        using var storage = new TemporaryStorage();
        var factory = new DuckDbConnectionFactory(storage.Options);
        await new DuckDbDatabase(factory).InitializeAsync();
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var first = new DuckDbKeyframeGenerationSettingsRepository(new DuckDbSettingsRepository(factory));
        var expected = new SceneKeyframeGenerationSettings(
            projectId,
            sceneId,
            "mock-image",
            "mock-image-v1",
            true,
            "1920x1080",
            42,
            "avoid duplicate faces",
            FixedUtc());

        await first.UpsertAsync(expected);

        var recreated = new DuckDbKeyframeGenerationSettingsRepository(
            new DuckDbSettingsRepository(new DuckDbConnectionFactory(storage.Options)));
        var restored = await recreated.GetAsync(projectId, sceneId);

        Assert.Equal(expected, restored);
    }

    [Fact]
    public async Task MockImageJob_CompletesVariantAndPersistsGeneratedMediaWithoutReplacingHistory()
    {
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var variantRepository = new InMemoryVariantRepository();
        var variantService = new KeyframeVariantService(variantRepository, new FixedTimeProvider());
        var first = await variantService.RegisterPlannedAsync(
            projectId, sceneId, KeyframeRole.Start, promptId, Guid.NewGuid(),
            "mock-image", "mock-image-v1", 0m, "USD");
        await variantService.CompleteAsync(projectId, first.Id, Guid.NewGuid(), 0m);
        await variantService.SelectAsync(projectId, first.Id);
        var queued = await variantService.RegisterPlannedAsync(
            projectId, sceneId, KeyframeRole.Start, promptId, jobId,
            "mock-image", "mock-image-v1", 0m, "USD");

        var payload = new KeyframeGenerationJobPayload(
            queued.Id,
            promptId,
            KeyframeRole.Start,
            "cinematic close-up",
            1280,
            720,
            [],
            7,
            "artifacting");
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var job = new GenerationJob(
            jobId,
            projectId,
            sceneId,
            null,
            KeyframeGenerationCoordinator.JobType,
            JsonSerializer.Serialize(payload, jsonOptions),
            "mock-image",
            "mock-image-v1",
            JobState.Submitting,
            null,
            100,
            1,
            0,
            2,
            FixedUtc(),
            FixedUtc(),
            null,
            FixedUtc(),
            null,
            null,
            null,
            null,
            0m,
            null,
            "USD",
            "test-worker",
            FixedUtc().AddMinutes(1));
        var mediaStorage = new InMemoryMediaStorage();
        var mediaAssets = new InMemoryMediaAssetRepository();
        var mockImage = new MockImageProvider(new MockProviderControl());
        var dispatcher = new GenerationJobExecutionDispatcher(
            new MockJobExecutionDispatcher(),
            new ImageGenerationProviderResolver(mockImage),
            variantService,
            mediaStorage,
            mediaAssets,
            new UnusedHttpClientFactory(),
            new FixedTimeProvider());

        var result = await dispatcher.ExecuteAsync(job);

        Assert.Equal(JobState.Completed, result.State);
        var variants = await variantService.ListSceneAsync(projectId, sceneId);
        Assert.Equal(2, variants.Count);
        Assert.True(variants.Single(variant => variant.Id == first.Id).IsSelected);
        var completed = variants.Single(variant => variant.Id == queued.Id);
        Assert.Equal(GenerationVariantState.Completed, completed.State);
        Assert.NotNull(completed.MediaAssetId);
        Assert.False(completed.IsSelected);
        var media = await mediaAssets.GetAsync(completed.MediaAssetId!.Value);
        Assert.NotNull(media);
        Assert.Equal(MediaCreationSource.Generated, media!.CreationSource);
        Assert.Equal(projectId, media.ProjectId);
        Assert.Equal("image/svg+xml", media.MimeType);
        Assert.NotEmpty(mediaStorage.LastSavedBytes);
    }

    [Fact]
    public async Task PlannedVariant_CanAttachPersistedJobWithoutChangingPromptProvenance()
    {
        var repository = new InMemoryVariantRepository();
        var service = new KeyframeVariantService(repository, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var planned = await service.RegisterPlannedAsync(
            projectId, sceneId, KeyframeRole.End, promptId, null,
            "mock-image", "mock-image-v1", null, "USD");
        var jobId = Guid.NewGuid();

        var queued = await service.AttachJobAsync(projectId, planned.Id, jobId);

        Assert.Equal(GenerationVariantState.Queued, queued.State);
        Assert.Equal(jobId, queued.JobId);
        Assert.Equal(promptId, queued.PromptVersionId);
    }

    private static DateTimeOffset FixedUtc() => new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed class InMemoryVariantRepository : IKeyframeVariantRepository
    {
        private readonly Dictionary<Guid, KeyframeVariant> _items = [];

        public Task<IReadOnlyList<KeyframeVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KeyframeVariant>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());

        public Task<KeyframeVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.TryGetValue(variantId, out var value) && value.ProjectId == projectId ? value : null);

        public Task UpsertAsync(KeyframeVariant variant, CancellationToken cancellationToken = default)
        {
            _items[variant.Id] = variant;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.TryGetValue(variantId, out var value) && value.ProjectId == projectId && _items.Remove(variantId));
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
            return new StoredMedia(new MediaLocation($"{projectId:N}/keyframes/{fileName}"), LastSavedBytes.Length, checksum);
        }

        public Task<Stream> OpenReadAsync(MediaLocation location, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(LastSavedBytes, writable: false));

        public Task<bool> DeleteAsync(MediaLocation location, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class InMemoryMediaAssetRepository : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MediaAssetMetadata> _items = [];

        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaAssetMetadata>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());

        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default)
        {
            _items[asset.Id] = asset;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenMusicVideoCreator.KeyframeGenerationTests", Guid.NewGuid().ToString("N"));

        public TemporaryStorage()
        {
            Directory.CreateDirectory(_root);
            Options = new StorageOptions(Path.Combine(_root, "data", "app.duckdb"), Path.Combine(_root, "projects"));
        }

        public StorageOptions Options { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
