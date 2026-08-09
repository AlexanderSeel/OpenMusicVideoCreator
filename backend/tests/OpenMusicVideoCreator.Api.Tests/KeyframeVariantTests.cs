using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class KeyframeVariantTests
{
    [Fact]
    public async Task CompletedVariants_SelectOnePerSceneRoleAndKeepOlderVariants()
    {
        using var storage = new TemporaryStorage();
        var repository = await CreateRepositoryAsync(storage.Options);
        var service = new KeyframeVariantService(repository, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        var first = await service.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.Start, promptId, Guid.NewGuid(), "mock-image", "mock-image-v1", 0m, "USD");
        var second = await service.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.Start, promptId, Guid.NewGuid(), "mock-image", "mock-image-v1", 0m, "USD");
        await service.CompleteAsync(projectId, first.Id, Guid.NewGuid(), 0m);
        await service.CompleteAsync(projectId, second.Id, Guid.NewGuid(), 0m);

        await service.SelectAsync(projectId, first.Id);
        await service.SelectAsync(projectId, second.Id);

        var variants = await service.ListSceneAsync(projectId, sceneId);
        Assert.Equal(2, variants.Count);
        Assert.False(variants.Single(variant => variant.Id == first.Id).IsSelected);
        Assert.True(variants.Single(variant => variant.Id == second.Id).IsSelected);
        Assert.Equal(1, variants.Count(variant => variant.Role == KeyframeRole.Start && variant.IsSelected));
    }

    [Fact]
    public async Task VariantHistory_SurvivesRepositoryRecreationWithPromptJobAssetAndCostProvenance()
    {
        using var storage = new TemporaryStorage();
        var firstRepository = await CreateRepositoryAsync(storage.Options);
        var service = new KeyframeVariantService(firstRepository, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var variant = await service.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.End, promptId, jobId, "mock-image", "mock-image-v1", 0.25m, "USD");
        await service.CompleteAsync(projectId, variant.Id, mediaId, 0.2m);
        await service.SelectAsync(projectId, variant.Id);

        var recreatedSettings = new DuckDbSettingsRepository(new DuckDbConnectionFactory(storage.Options));
        var recreated = new DuckDbKeyframeVariantRepository(recreatedSettings);
        var restored = await recreated.GetAsync(projectId, variant.Id);

        var actual = restored ?? throw new InvalidOperationException("Keyframe variant was not restored.");
        Assert.Equal(promptId, actual.PromptVersionId);
        Assert.Equal(jobId, actual.JobId);
        Assert.Equal(mediaId, actual.MediaAssetId);
        Assert.Equal(0.25m, actual.EstimatedCost);
        Assert.Equal(0.2m, actual.ActualCost);
        Assert.True(actual.IsSelected);
    }

    [Fact]
    public async Task SelectedVariant_CannotBeDeletedAndIncompleteVariantCannotBeSelected()
    {
        using var storage = new TemporaryStorage();
        var repository = await CreateRepositoryAsync(storage.Options);
        var service = new KeyframeVariantService(repository, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var pending = await service.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.Start, Guid.NewGuid(), Guid.NewGuid(), "mock-image", "mock-image-v1", null, "USD");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectAsync(projectId, pending.Id));

        await service.CompleteAsync(projectId, pending.Id, Guid.NewGuid(), null);
        await service.SelectAsync(projectId, pending.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(projectId, pending.Id));
    }

    private static async Task<DuckDbKeyframeVariantRepository> CreateRepositoryAsync(StorageOptions options)
    {
        var factory = new DuckDbConnectionFactory(options);
        await new DuckDbDatabase(factory).InitializeAsync();
        return new DuckDbKeyframeVariantRepository(new DuckDbSettingsRepository(factory));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 9, 19, 0, 0, TimeSpan.Zero);
    }

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenMusicVideoCreator.KeyframeTests", Guid.NewGuid().ToString("N"));

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
