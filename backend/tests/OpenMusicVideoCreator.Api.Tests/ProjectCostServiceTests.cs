using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Costs;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Projects;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class ProjectCostServiceTests
{
    [Fact]
    public async Task Summary_AggregatesActualReservedProviderModelAndSceneCosts()
    {
        var projectId = Guid.NewGuid();
        var sceneA = Guid.NewGuid();
        var sceneB = Guid.NewGuid();
        var project = Project(projectId, estimatedBudget: 8m, maximumBudget: 10m);
        var jobs = new InMemoryJobRepository(
            Job(projectId, sceneA, "image", "provider-a", "model-1", JobState.Completed, 3m, 2.5m),
            Job(projectId, sceneA, "video", "provider-a", "model-2", JobState.Queued, 1.25m, null),
            Job(projectId, sceneB, "video", "provider-b", "model-9", JobState.FailedPermanent, 9m, null),
            Job(projectId, sceneB, "image", "provider-b", "model-9", JobState.Cancelled, 4m, 0.5m));
        var service = new ProjectCostService(new ProjectRepository(project), jobs);

        var summary = await service.GetAsync(projectId);

        Assert.Equal(3m, summary.ActualCost);
        Assert.Equal(1.25m, summary.ReservedEstimatedCost);
        Assert.Equal(4.25m, summary.ProjectedCost);
        Assert.Equal(5.75m, summary.RemainingBudget);
        Assert.Equal(0, summary.UnknownCostJobCount);
        Assert.Equal(3, summary.Providers.Count);
        var providerA1 = Assert.Single(summary.Providers, item => item.ProviderId == "provider-a" && item.ModelId == "model-1");
        Assert.Equal(2.5m, providerA1.ActualCost);
        Assert.Equal(0m, providerA1.ReservedEstimatedCost);
        var scene = Assert.Single(summary.Scenes, item => item.SceneId == sceneA);
        Assert.Equal(2.5m, scene.ActualCost);
        Assert.Equal(1.25m, scene.ReservedEstimatedCost);
    }

    [Fact]
    public async Task HardCap_RejectsUnknownAndOverspendButAllowsZeroCost()
    {
        var projectId = Guid.NewGuid();
        var project = Project(projectId, maximumBudget: 5m);
        var jobs = new InMemoryJobRepository(
            Job(projectId, Guid.NewGuid(), "video", "provider", "model", JobState.Completed, 4m, 3m));
        var service = new ProjectCostService(new ProjectRepository(project), jobs);

        await service.EnsureCanReserveAsync(project, 2m, "USD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureCanReserveAsync(project, 2.01m, "USD"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureCanReserveAsync(project, null, "USD"));

        await jobs.CreateAsync(
            Job(projectId, Guid.NewGuid(), "unknown", "provider", "model", JobState.Queued, null, null),
            []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureCanReserveAsync(project, 0.1m, "USD"));
        await service.EnsureCanReserveAsync(project, 0m, "USD");
    }

    [Fact]
    public async Task NoHardCap_AllowsGenerationWithoutEstimate()
    {
        var project = Project(Guid.NewGuid(), maximumBudget: null);
        var service = new ProjectCostService(new ProjectRepository(project), new InMemoryJobRepository());
        await service.EnsureCanReserveAsync(project, null, null);
    }

    [Fact]
    public async Task BudgetAwareQueue_SerializesConcurrentReservationsSoOnlyOneCanExceedCap()
    {
        var projectId = Guid.NewGuid();
        var project = Project(projectId, maximumBudget: 1m);
        var repository = new InMemoryJobRepository();
        var changes = new NoopJobChangePublisher();
        var jobService = new JobService(repository, changes, TimeProvider.System);
        var projectRepository = new ProjectRepository(project);
        var costs = new ProjectCostService(projectRepository, repository);
        var queue = new BudgetAwareJobQueue(jobService, projectRepository, costs);
        var definition = new JobDefinition(
            projectId,
            Guid.NewGuid(),
            null,
            "paid.test",
            "{}",
            "provider",
            "model",
            EstimatedCost: 0.75m,
            Currency: "USD");

        var outcomes = await Task.WhenAll(
            TryEnqueueAsync(queue, definition),
            TryEnqueueAsync(queue, definition with { SceneId = Guid.NewGuid() }));

        Assert.Equal(1, outcomes.Count(outcome => outcome.Success));
        Assert.Equal(1, outcomes.Count(outcome => outcome.Error is InvalidOperationException));
        var persisted = await repository.ListAsync();
        Assert.Single(persisted);
        Assert.Equal(0.75m, (await costs.GetAsync(projectId)).ProjectedCost);
    }

    private static async Task<(bool Success, Exception? Error)> TryEnqueueAsync(IJobQueue queue, JobDefinition definition)
    {
        try
        {
            await queue.EnqueueAsync(definition);
            return (true, null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }

    private static MusicVideoProject Project(
        Guid id,
        decimal? estimatedBudget = null,
        decimal? maximumBudget = null) =>
        new(
            id,
            "Cost fixture",
            "Artist",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            ProjectAspectRatio.Landscape16x9,
            new OutputResolution(1920, 1080),
            [],
            GenerationPreset.Balanced,
            estimatedBudget,
            maximumBudget,
            [],
            FixedUtc(),
            FixedUtc());

    private static GenerationJob Job(
        Guid projectId,
        Guid? sceneId,
        string type,
        string? provider,
        string? model,
        JobState state,
        decimal? estimated,
        decimal? actual) =>
        new(
            Guid.NewGuid(),
            projectId,
            sceneId,
            null,
            type,
            "{}",
            provider,
            model,
            state,
            null,
            100,
            1,
            0,
            2,
            FixedUtc(),
            FixedUtc(),
            null,
            FixedUtc(),
            IsTerminal(state) ? FixedUtc() : null,
            null,
            null,
            null,
            estimated,
            actual,
            "USD",
            null,
            null);

    private static bool IsTerminal(JobState state) =>
        state is JobState.Completed or JobState.Rejected or JobState.FailedPermanent or JobState.Cancelled;

    private static DateTimeOffset FixedUtc() => new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    private sealed class ProjectRepository(MusicVideoProject project) : IProjectRepository
    {
        public Task<IReadOnlyList<MusicVideoProject>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MusicVideoProject>>([project]);

        public Task<MusicVideoProject?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MusicVideoProject?>(id == project.Id ? project : null);

        public Task UpsertAsync(MusicVideoProject value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class NoopJobChangePublisher : IJobChangePublisher
    {
        public ValueTask PublishAsync(Guid jobId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class InMemoryJobRepository(params GenerationJob[] jobs) : IJobRepository
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, GenerationJob> _jobs = jobs.ToDictionary(job => job.Id);
        private readonly Dictionary<Guid, IReadOnlyList<Guid>> _dependencies = [];
        private readonly Dictionary<(Guid, int), JobAttempt> _attempts = [];

        public Task CreateAsync(GenerationJob job, IReadOnlyCollection<Guid> dependencyIds, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _jobs[job.Id] = job;
                _dependencies[job.Id] = dependencyIds.ToArray();
            }
            return Task.CompletedTask;
        }

        public Task<GenerationJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_sync) return Task.FromResult(_jobs.GetValueOrDefault(id));
        }

        public Task<IReadOnlyList<GenerationJob>> ListAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync) return Task.FromResult<IReadOnlyList<GenerationJob>>(_jobs.Values.ToArray());
        }

        public Task<IReadOnlyList<Guid>> GetDependenciesAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            lock (_sync) return Task.FromResult(_dependencies.GetValueOrDefault(jobId) ?? (IReadOnlyList<Guid>)[]);
        }

        public Task<IReadOnlyList<JobAttempt>> GetAttemptsAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            lock (_sync) return Task.FromResult<IReadOnlyList<JobAttempt>>(_attempts.Values.Where(item => item.JobId == jobId).ToArray());
        }

        public Task<bool> TryUpdateAsync(GenerationJob job, JobState expectedState, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (!_jobs.TryGetValue(job.Id, out var current) || current.State != expectedState) return Task.FromResult(false);
                _jobs[job.Id] = job;
                return Task.FromResult(true);
            }
        }

        public Task<GenerationJob?> TryClaimNextAsync(string workerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) =>
            Task.FromResult<GenerationJob?>(null);

        public Task UpsertAttemptAsync(JobAttempt attempt, CancellationToken cancellationToken = default)
        {
            lock (_sync) _attempts[(attempt.JobId, attempt.AttemptNumber)] = attempt;
            return Task.CompletedTask;
        }
    }
}
