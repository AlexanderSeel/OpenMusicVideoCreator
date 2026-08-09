using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class KeyframeApprovalTests
{
    [Fact]
    public async Task Approval_RequiresCompletedSelectedStartAndBecomesIneffectiveAfterSelectionChanges()
    {
        using var storage = new TemporaryStorage();
        var factory = new DuckDbConnectionFactory(storage.Options);
        await new DuckDbDatabase(factory).InitializeAsync();
        var settings = new DuckDbSettingsRepository(factory);
        var variants = new DuckDbKeyframeVariantRepository(settings);
        var approvals = new DuckDbKeyframeApprovalRepository(settings);
        var variantService = new KeyframeVariantService(variants, new FixedTimeProvider());
        var approvalService = new KeyframeApprovalService(variants, approvals, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => approvalService.ApproveAsync(projectId, sceneId));

        var first = await variantService.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.Start, Guid.NewGuid(), Guid.NewGuid(), "mock-image", "mock-image-v1", 0m, "USD");
        await variantService.CompleteAsync(projectId, first.Id, Guid.NewGuid(), 0m);
        await variantService.SelectAsync(projectId, first.Id);
        var approval = await approvalService.ApproveAsync(projectId, sceneId);

        Assert.True(await approvalService.IsCurrentSelectionApprovedAsync(projectId, sceneId));
        Assert.Equal(first.Id, approval.StartVariantId);

        var second = await variantService.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.Start, Guid.NewGuid(), Guid.NewGuid(), "mock-image", "mock-image-v1", 0m, "USD");
        await variantService.CompleteAsync(projectId, second.Id, Guid.NewGuid(), 0m);
        await variantService.SelectAsync(projectId, second.Id);

        Assert.False(await approvalService.IsCurrentSelectionApprovedAsync(projectId, sceneId));
        var historicApproval = await approvalService.GetAsync(projectId, sceneId);
        Assert.NotNull(historicApproval);
        Assert.Equal(first.Id, historicApproval.StartVariantId);
    }

    [Fact]
    public async Task Approval_WithOptionalEndVariantSurvivesRepositoryRecreation()
    {
        using var storage = new TemporaryStorage();
        var factory = new DuckDbConnectionFactory(storage.Options);
        await new DuckDbDatabase(factory).InitializeAsync();
        var settings = new DuckDbSettingsRepository(factory);
        var variants = new DuckDbKeyframeVariantRepository(settings);
        var approvals = new DuckDbKeyframeApprovalRepository(settings);
        var variantService = new KeyframeVariantService(variants, new FixedTimeProvider());
        var approvalService = new KeyframeApprovalService(variants, approvals, new FixedTimeProvider());
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();

        var start = await variantService.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.Start, Guid.NewGuid(), Guid.NewGuid(), "mock-image", "mock-image-v1", 0m, "USD");
        var end = await variantService.RegisterPlannedAsync(projectId, sceneId, KeyframeRole.End, Guid.NewGuid(), Guid.NewGuid(), "mock-image", "mock-image-v1", 0m, "USD");
        await variantService.CompleteAsync(projectId, start.Id, Guid.NewGuid(), 0m);
        await variantService.CompleteAsync(projectId, end.Id, Guid.NewGuid(), 0m);
        await variantService.SelectAsync(projectId, start.Id);
        await variantService.SelectAsync(projectId, end.Id);
        await approvalService.ApproveAsync(projectId, sceneId);

        var recreatedSettings = new DuckDbSettingsRepository(new DuckDbConnectionFactory(storage.Options));
        var recreatedVariants = new DuckDbKeyframeVariantRepository(recreatedSettings);
        var recreatedApprovals = new DuckDbKeyframeApprovalRepository(recreatedSettings);
        var recreatedService = new KeyframeApprovalService(recreatedVariants, recreatedApprovals, new FixedTimeProvider());
        var restored = await recreatedService.GetAsync(projectId, sceneId);

        var actual = restored ?? throw new InvalidOperationException("Keyframe approval was not restored.");
        Assert.Equal(start.Id, actual.StartVariantId);
        Assert.Equal(end.Id, actual.EndVariantId);
        Assert.True(await recreatedService.IsCurrentSelectionApprovedAsync(projectId, sceneId));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 9, 19, 15, 0, TimeSpan.Zero);
    }

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenMusicVideoCreator.KeyframeApprovalTests", Guid.NewGuid().ToString("N"));

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
