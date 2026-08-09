using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Infrastructure.Jobs;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class JobEngineIntegrationTests
{
    [Fact]
    public void StateMachine_EnforcesNormalAndExplicitRestartTransitions()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Queued, JobState.Submitting));
        Assert.True(JobStateMachine.CanTransition(JobState.Generating, JobState.WaitingForQuota));
        Assert.False(JobStateMachine.CanTransition(JobState.Completed, JobState.Queued));
        Assert.False(JobStateMachine.CanTransition(JobState.Completed, JobState.Generating));
        Assert.True(JobStateMachine.CanTransition(JobState.Completed, JobState.Queued, explicitRestart: true));
        Assert.True(JobStateMachine.CanTransition(JobState.Completed, JobState.WaitingForDependency, explicitRestart: true));
        Assert.Throws<InvalidOperationException>(() =>
            JobStateMachine.EnsureTransition(JobState.Completed, JobState.Generating));
    }

    [Fact]
    public async Task JobsDependenciesAndAttempts_SurviveRepositoryRecreation()
    {
        using var context = await JobTestContext.CreateAsync();
        var parent = await context.Service.EnqueueAsync(Definition("mock:success"));
        var child = await context.Service.EnqueueAsync(
            Definition("mock:success", parentJobId: parent.Id),
            [parent.Id]);

        Assert.Equal(JobState.Queued, parent.State);
        Assert.Equal(JobState.WaitingForDependency, child.State);

        Assert.True(await context.Processor.ProcessNextAsync("worker-a", TimeSpan.FromMinutes(1)));
        await context.Service.MaintainRunnableStatesAsync();

        var recreated = await context.RecreateAsync();
        var restoredParent = await recreated.Service.GetAsync(parent.Id);
        var restoredChild = await recreated.Service.GetAsync(child.Id);
        var dependencies = await recreated.Service.GetDependenciesAsync(child.Id);
        var attempts = await recreated.Service.GetAttemptsAsync(parent.Id);

        Assert.NotNull(restoredParent);
        Assert.NotNull(restoredChild);
        Assert.Equal(JobState.Completed, restoredParent.State);
        Assert.Equal(JobState.Queued, restoredChild.State);
        Assert.Equal([parent.Id], dependencies);
        Assert.Single(attempts);
        Assert.Equal(JobState.Completed, attempts[0].State);
        Assert.NotNull(attempts[0].CompletedUtc);
    }

    [Fact]
    public async Task ConcurrentWorkers_CannotClaimSameJobTwice()
    {
        using var context = await JobTestContext.CreateAsync();
        var job = await context.Service.EnqueueAsync(Definition("mock:success"));
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            context.Repository.TryClaimNextAsync("worker-one", now, TimeSpan.FromMinutes(1)),
            context.Repository.TryClaimNextAsync("worker-two", now, TimeSpan.FromMinutes(1)));

        Assert.Single(claims.Where(claim => claim is not null));
        Assert.Equal(job.Id, claims.Single(claim => claim is not null)!.Id);
        Assert.Equal(JobState.Submitting, (await context.Service.GetAsync(job.Id))!.State);
        Assert.Single(await context.Service.GetAttemptsAsync(job.Id));
    }

    [Fact]
    public async Task PauseResumeCancelAndExplicitRestart_PreserveSafeSemantics()
    {
        using var context = await JobTestContext.CreateAsync();
        var job = await context.Service.EnqueueAsync(Definition("mock:success"));

        Assert.True(await context.Service.PauseAsync(job.Id));
        Assert.Equal(JobState.Paused, (await context.Service.GetAsync(job.Id))!.State);
        Assert.True(await context.Service.ResumeAsync(job.Id));
        Assert.Equal(JobState.Queued, (await context.Service.GetAsync(job.Id))!.State);
        Assert.True(await context.Service.CancelAsync(job.Id));
        Assert.Equal(JobState.Cancelled, (await context.Service.GetAsync(job.Id))!.State);

        Assert.True(await context.Service.RestartAsync(job.Id));
        Assert.Equal(JobState.Queued, (await context.Service.GetAsync(job.Id))!.State);
        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));
        Assert.Equal(JobState.Completed, (await context.Service.GetAsync(job.Id))!.State);

        Assert.False(await context.Service.ResumeAsync(job.Id));
        Assert.Equal(JobState.Completed, (await context.Service.GetAsync(job.Id))!.State);
        Assert.True(await context.Service.RestartAsync(job.Id));
        Assert.Equal(JobState.Queued, (await context.Service.GetAsync(job.Id))!.State);
    }

    [Fact]
    public async Task QuotaWait_SurvivesRestartAndResumesSameJobGraph()
    {
        using var context = await JobTestContext.CreateAsync();
        var dependency = await context.Service.EnqueueAsync(Definition("mock:success"));
        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));

        var quotaJob = await context.Service.EnqueueAsync(
            Definition("mock:quota", parentJobId: dependency.Id),
            [dependency.Id]);
        var originalDependencies = await context.Service.GetDependenciesAsync(quotaJob.Id);

        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));
        Assert.Equal(JobState.WaitingForQuota, (await context.Service.GetAsync(quotaJob.Id))!.State);

        var recreated = await context.RecreateAsync();
        var restored = await recreated.Service.GetAsync(quotaJob.Id);
        Assert.NotNull(restored);
        Assert.Equal(JobState.WaitingForQuota, restored.State);
        Assert.Equal(originalDependencies, await recreated.Service.GetDependenciesAsync(quotaJob.Id));

        Assert.True(await recreated.Service.RetryAsync(quotaJob.Id));
        var retried = await recreated.Service.GetAsync(quotaJob.Id);
        Assert.NotNull(retried);
        Assert.Equal(quotaJob.Id, retried.Id);
        Assert.Equal(JobState.Queued, retried.State);
        Assert.Equal(originalDependencies, await recreated.Service.GetDependenciesAsync(quotaJob.Id));
    }

    [Fact]
    public async Task StartupRecovery_ReconcilesProviderTasksAndRetriesInterruptedLocalWork()
    {
        using var context = await JobTestContext.CreateAsync();
        var localJob = await context.Service.EnqueueAsync(Definition("mock:success"));
        var providerJob = await context.Service.EnqueueAsync(Definition("mock:provider-wait"));

        var claimedLocal = await context.Repository.TryClaimNextAsync(
            "worker-local",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
        Assert.NotNull(claimedLocal);
        Assert.Equal(localJob.Id, claimedLocal.Id);

        var claimedProvider = await context.Repository.TryClaimNextAsync(
            "worker-provider",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
        Assert.NotNull(claimedProvider);
        Assert.Equal(providerJob.Id, claimedProvider.Id);
        await context.Service.ApplyExecutionResultAsync(
            providerJob.Id,
            new JobExecutionResult(JobState.Generating, ProviderTaskId: "provider-task-123"));

        await context.Service.RecoverInterruptedJobsAsync();
        var localRecovered = await context.Service.GetAsync(localJob.Id);
        var providerRecovered = await context.Service.GetAsync(providerJob.Id);

        Assert.NotNull(localRecovered);
        Assert.NotNull(providerRecovered);
        Assert.Equal(JobState.RetryScheduled, localRecovered.State);
        Assert.Equal(1, localRecovered.RetryCount);
        Assert.Equal(JobState.WaitingForProvider, providerRecovered.State);
        Assert.Equal("provider-task-123", providerRecovered.ProviderTaskId);

        await context.Service.MaintainRunnableStatesAsync();
        Assert.Equal(JobState.Queued, (await context.Service.GetAsync(localJob.Id))!.State);
        Assert.False(await context.Service.RetryAsync(providerJob.Id));
        Assert.Equal(JobState.WaitingForProvider, (await context.Service.GetAsync(providerJob.Id))!.State);
    }

    [Fact]
    public async Task ProviderFailures_MapToRecoverableAndTerminalStates()
    {
        using var context = await JobTestContext.CreateAsync();

        var rateLimited = await context.Service.EnqueueAsync(Definition("mock:rate-limit", maxRetries: 2));
        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));
        var rateState = await context.Service.GetAsync(rateLimited.Id);
        Assert.NotNull(rateState);
        Assert.Equal(JobState.RetryScheduled, rateState.State);
        Assert.Equal(1, rateState.RetryCount);
        Assert.NotNull(rateState.NextRunUtc);

        var providerUnavailable = await context.Service.EnqueueAsync(Definition("mock:provider-unavailable"));
        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));
        Assert.Equal(
            JobState.WaitingForProvider,
            (await context.Service.GetAsync(providerUnavailable.Id))!.State);

        var rejected = await context.Service.EnqueueAsync(Definition("mock:rejected"));
        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));
        Assert.Equal(JobState.Rejected, (await context.Service.GetAsync(rejected.Id))!.State);

        var permanent = await context.Service.EnqueueAsync(Definition("mock:permanent"));
        Assert.True(await context.Processor.ProcessNextAsync("worker", TimeSpan.FromMinutes(1)));
        Assert.Equal(JobState.FailedPermanent, (await context.Service.GetAsync(permanent.Id))!.State);
    }

    private static JobDefinition Definition(
        string type,
        Guid? parentJobId = null,
        int maxRetries = 3) => new(
        ProjectId: Guid.NewGuid(),
        SceneId: null,
        ParentJobId: parentJobId,
        Type: type,
        PayloadJson: "{}",
        Priority: 100,
        MaxRetries: maxRetries);

    private sealed class JobTestContext : IDisposable
    {
        private readonly string _root;

        private JobTestContext(
            string root,
            StorageOptions options,
            DuckDbJobRepository repository,
            JobChangeHub changes,
            JobService service,
            JobProcessor processor)
        {
            _root = root;
            Options = options;
            Repository = repository;
            Changes = changes;
            Service = service;
            Processor = processor;
        }

        public StorageOptions Options { get; }
        public DuckDbJobRepository Repository { get; }
        public JobChangeHub Changes { get; }
        public JobService Service { get; }
        public JobProcessor Processor { get; }

        public static async Task<JobTestContext> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "OpenMusicVideoCreator.JobTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var options = new StorageOptions(
                Path.Combine(root, "data", "app.duckdb"),
                Path.Combine(root, "projects"));
            return await CreateAsync(root, options);
        }

        public Task<JobTestContext> RecreateAsync() => CreateAsync(_root, Options);

        private static async Task<JobTestContext> CreateAsync(string root, StorageOptions options)
        {
            var connections = new DuckDbConnectionFactory(options);
            var database = new DuckDbDatabase(connections);
            await database.InitializeAsync();
            var repository = new DuckDbJobRepository(connections);
            var changes = new JobChangeHub();
            var service = new JobService(repository, changes, TimeProvider.System);
            var dispatcher = new MockJobExecutionDispatcher();
            var processor = new JobProcessor(
                repository,
                service,
                dispatcher,
                changes,
                TimeProvider.System);
            return new JobTestContext(root, options, repository, changes, service, processor);
        }

        public void Dispose()
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; DuckDB handles may close shortly after the test.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }
}
