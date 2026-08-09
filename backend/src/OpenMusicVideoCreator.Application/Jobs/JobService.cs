using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Application.Jobs;

public sealed class JobService : IJobQueue
{
    private readonly IJobRepository _jobs;
    private readonly IJobChangePublisher _publisher;
    private readonly TimeProvider _timeProvider;

    public JobService(
        IJobRepository jobs,
        IJobChangePublisher publisher,
        TimeProvider timeProvider)
    {
        _jobs = jobs;
        _publisher = publisher;
        _timeProvider = timeProvider;
    }

    public async Task<GenerationJob> EnqueueAsync(
        JobDefinition definition,
        IReadOnlyCollection<Guid>? dependencyIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Type);
        ArgumentNullException.ThrowIfNull(definition.PayloadJson);

        if (definition.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Max retries cannot be negative.");
        }

        var dependencies = (dependencyIds ?? []).Distinct().ToArray();
        var dependencyJobs = new List<GenerationJob>(dependencies.Length);
        foreach (var dependencyId in dependencies)
        {
            var dependency = await _jobs.GetAsync(dependencyId, cancellationToken)
                ?? throw new ArgumentException($"Dependency job '{dependencyId}' was not found.", nameof(dependencyIds));
            dependencyJobs.Add(dependency);
        }

        var now = GetUtcNow();
        var id = Guid.NewGuid();
        var state = dependencies.Length == 0 || dependencyJobs.All(job => job.State == JobState.Completed)
            ? JobState.Queued
            : JobState.WaitingForDependency;

        var job = new GenerationJob(
            id,
            definition.ProjectId,
            definition.SceneId,
            definition.ParentJobId,
            definition.Type,
            definition.PayloadJson,
            definition.ProviderId,
            definition.ModelId,
            state,
            ResumeState: null,
            definition.Priority,
            AttemptCount: 0,
            RetryCount: 0,
            definition.MaxRetries,
            now,
            now,
            NextRunUtc: null,
            StartedUtc: null,
            CompletedUtc: null,
            ProviderTaskId: null,
            ErrorCode: null,
            ErrorMessage: null,
            definition.EstimatedCost,
            ActualCost: null,
            definition.Currency,
            ClaimedBy: null,
            ClaimExpiresUtc: null);

        await _jobs.CreateAsync(job, dependencies, cancellationToken);
        await _publisher.PublishAsync(job.Id, cancellationToken);
        return job;
    }

    public Task<GenerationJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _jobs.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<GenerationJob>> ListAsync(CancellationToken cancellationToken = default) =>
        _jobs.ListAsync(cancellationToken);

    public Task<IReadOnlyList<JobAttempt>> GetAttemptsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _jobs.GetAttemptsAsync(id, cancellationToken);

    public Task<IReadOnlyList<Guid>> GetDependenciesAsync(Guid id, CancellationToken cancellationToken = default) =>
        _jobs.GetDependenciesAsync(id, cancellationToken);

    public async Task<bool> PauseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequireAsync(id, cancellationToken);
        if (job.State.IsTerminal() || job.State == JobState.Paused)
        {
            return false;
        }

        var resumeState = !string.IsNullOrWhiteSpace(job.ProviderTaskId)
            ? JobState.WaitingForProvider
            : job.State;
        var updated = await TransitionAsync(
            job,
            JobState.Paused,
            current => current with
            {
                ResumeState = resumeState,
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            cancellationToken: cancellationToken);

        await RecordCurrentAttemptAsync(
            updated,
            complete: string.IsNullOrWhiteSpace(updated.ProviderTaskId),
            cancellationToken);
        return true;
    }

    public async Task<bool> ResumeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequireAsync(id, cancellationToken);
        if (job.State != JobState.Paused)
        {
            return false;
        }

        var dependenciesSatisfied = await DependenciesCompletedAsync(job.Id, cancellationToken);
        var target = dependenciesSatisfied
            ? job.ResumeState == JobState.WaitingForProvider && !string.IsNullOrWhiteSpace(job.ProviderTaskId)
                ? JobState.WaitingForProvider
                : JobState.Queued
            : JobState.WaitingForDependency;

        await TransitionAsync(
            job,
            target,
            current => current with
            {
                ResumeState = null,
                NextRunUtc = null,
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequireAsync(id, cancellationToken);
        if (job.State.IsTerminal())
        {
            return false;
        }

        var updated = await TransitionAsync(
            job,
            JobState.Cancelled,
            current => current with
            {
                CompletedUtc = GetUtcNow(),
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            cancellationToken: cancellationToken);
        await RecordCurrentAttemptAsync(updated, complete: true, cancellationToken);
        return true;
    }

    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequireAsync(id, cancellationToken);
        if (job.State is not (
            JobState.FailedRetryable or
            JobState.RetryScheduled or
            JobState.WaitingForQuota or
            JobState.WaitingForProvider))
        {
            return false;
        }

        if (job.State == JobState.WaitingForProvider && !string.IsNullOrWhiteSpace(job.ProviderTaskId))
        {
            return false;
        }

        await TransitionAsync(
            job,
            JobState.Queued,
            current => current with
            {
                RetryCount = 0,
                NextRunUtc = null,
                ProviderTaskId = null,
                ErrorCode = null,
                ErrorMessage = null,
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> RestartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequireAsync(id, cancellationToken);
        if (job.AttemptCount > 0 && !job.State.IsTerminal())
        {
            await RecordCurrentAttemptAsync(
                job with { State = JobState.Cancelled },
                complete: true,
                cancellationToken);
        }

        var dependenciesSatisfied = await DependenciesCompletedAsync(id, cancellationToken);
        var target = dependenciesSatisfied ? JobState.Queued : JobState.WaitingForDependency;

        await TransitionAsync(
            job,
            target,
            current => current with
            {
                ResumeState = null,
                RetryCount = 0,
                NextRunUtc = null,
                StartedUtc = null,
                CompletedUtc = null,
                ProviderTaskId = null,
                ErrorCode = null,
                ErrorMessage = null,
                ActualCost = null,
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            explicitRestart: true,
            cancellationToken: cancellationToken);
        return true;
    }

    public Task<int> PauseProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        ApplyToScopeAsync(projectId, sceneId: null, PauseAsync, cancellationToken);

    public Task<int> ResumeProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        ApplyToScopeAsync(projectId, sceneId: null, ResumeOrRetryAsync, cancellationToken);

    public Task<int> CancelProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        ApplyToScopeAsync(projectId, sceneId: null, CancelAsync, cancellationToken);

    public Task<int> PauseSceneAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default) =>
        ApplyToScopeAsync(projectId, sceneId, PauseAsync, cancellationToken);

    public Task<int> ResumeSceneAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default) =>
        ApplyToScopeAsync(projectId, sceneId, ResumeOrRetryAsync, cancellationToken);

    public Task<int> CancelSceneAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default) =>
        ApplyToScopeAsync(projectId, sceneId, CancelAsync, cancellationToken);

    public async Task MaintainRunnableStatesAsync(CancellationToken cancellationToken = default)
    {
        var now = GetUtcNow();
        var jobs = await _jobs.ListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            if (job.State == JobState.RetryScheduled && job.NextRunUtc <= now)
            {
                await TransitionAsync(
                    job,
                    JobState.Queued,
                    current => current with { NextRunUtc = null },
                    ignoreConcurrencyConflict: true,
                    cancellationToken: cancellationToken);
                continue;
            }

            if (job.State != JobState.WaitingForDependency)
            {
                continue;
            }

            var dependencies = await LoadDependenciesAsync(job.Id, cancellationToken);
            if (dependencies.Any(dependency => dependency.State.IsTerminal() && dependency.State != JobState.Completed))
            {
                await TransitionAsync(
                    job,
                    JobState.FailedPermanent,
                    current => current with
                    {
                        ErrorCode = "dependency_failed",
                        ErrorMessage = "A dependency finished without completing successfully.",
                        CompletedUtc = now,
                    },
                    ignoreConcurrencyConflict: true,
                    cancellationToken: cancellationToken);
            }
            else if (dependencies.All(dependency => dependency.State == JobState.Completed))
            {
                await TransitionAsync(
                    job,
                    JobState.Queued,
                    current => current with { ErrorCode = null, ErrorMessage = null },
                    ignoreConcurrencyConflict: true,
                    cancellationToken: cancellationToken);
            }
        }
    }

    public async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await _jobs.ListAsync(cancellationToken);
        var now = GetUtcNow();

        foreach (var job in jobs.Where(candidate => candidate.State.IsActivelyProcessing()))
        {
            if (!string.IsNullOrWhiteSpace(job.ProviderTaskId))
            {
                var updated = await TransitionAsync(
                    job,
                    JobState.WaitingForProvider,
                    current => current with
                    {
                        ClaimedBy = null,
                        ClaimExpiresUtc = null,
                        ErrorCode = "startup_reconcile",
                        ErrorMessage = "Provider-side work must be reconciled after restart.",
                    },
                    ignoreConcurrencyConflict: true,
                    cancellationToken: cancellationToken);
                await RecordCurrentAttemptAsync(updated, complete: false, cancellationToken);
                continue;
            }

            if (job.RetryCount < job.MaxRetries)
            {
                var updated = await TransitionAsync(
                    job,
                    JobState.RetryScheduled,
                    current => current with
                    {
                        RetryCount = job.RetryCount + 1,
                        NextRunUtc = now,
                        ClaimedBy = null,
                        ClaimExpiresUtc = null,
                        ErrorCode = "startup_retry",
                        ErrorMessage = "Interrupted local work was scheduled for retry after restart.",
                    },
                    ignoreConcurrencyConflict: true,
                    cancellationToken: cancellationToken);
                await RecordCurrentAttemptAsync(updated, complete: true, cancellationToken);
            }
            else
            {
                var updated = await TransitionAsync(
                    job,
                    JobState.FailedRetryable,
                    current => current with
                    {
                        ClaimedBy = null,
                        ClaimExpiresUtc = null,
                        ErrorCode = "startup_retry_exhausted",
                        ErrorMessage = "Interrupted local work requires a manual retry.",
                    },
                    ignoreConcurrencyConflict: true,
                    cancellationToken: cancellationToken);
                await RecordCurrentAttemptAsync(updated, complete: true, cancellationToken);
            }
        }
    }

    public async Task<GenerationJob> ApplyExecutionResultAsync(
        Guid jobId,
        JobExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Failure is not null)
        {
            return await ApplyProviderFailureAsync(jobId, result.Failure, cancellationToken);
        }

        var job = await RequireAsync(jobId, cancellationToken);
        var now = GetUtcNow();
        var updated = await TransitionAsync(
            job,
            result.State,
            current => current with
            {
                ProviderTaskId = result.ProviderTaskId ?? current.ProviderTaskId,
                ActualCost = result.ActualCost ?? current.ActualCost,
                Currency = result.Currency ?? current.Currency,
                CompletedUtc = result.State.IsTerminal() ? now : null,
                ErrorCode = null,
                ErrorMessage = null,
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            cancellationToken: cancellationToken);

        await RecordCurrentAttemptAsync(updated, complete: result.State.IsTerminal(), cancellationToken);
        return updated;
    }

    public async Task<GenerationJob> ApplyProviderFailureAsync(
        Guid jobId,
        ProviderFailure failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var job = await RequireAsync(jobId, cancellationToken);
        var now = GetUtcNow();

        JobState target;
        DateTimeOffset? nextRun = null;
        var retryCount = job.RetryCount;

        switch (failure.Code)
        {
            case ProviderFailureCode.QuotaExhausted:
            case ProviderFailureCode.InsufficientCredits:
                target = JobState.WaitingForQuota;
                break;
            case ProviderFailureCode.ProviderUnavailable:
                target = JobState.WaitingForProvider;
                break;
            case ProviderFailureCode.RateLimited:
            case ProviderFailureCode.NetworkFailure:
            case ProviderFailureCode.Timeout:
            case ProviderFailureCode.TransientFailure:
                if (job.RetryCount < job.MaxRetries)
                {
                    target = JobState.RetryScheduled;
                    retryCount++;
                    nextRun = now + (failure.RetryAfter ?? BackoffFor(retryCount));
                }
                else
                {
                    target = JobState.FailedRetryable;
                }
                break;
            case ProviderFailureCode.ModerationRejected:
                target = JobState.Rejected;
                break;
            case ProviderFailureCode.AuthenticationFailed:
            case ProviderFailureCode.InvalidParameters:
            case ProviderFailureCode.UnsupportedCapability:
            case ProviderFailureCode.PermanentFailure:
                target = JobState.FailedPermanent;
                break;
            default:
                target = failure.Retryable ? JobState.FailedRetryable : JobState.FailedPermanent;
                break;
        }

        var updated = await TransitionAsync(
            job,
            target,
            current => current with
            {
                RetryCount = retryCount,
                NextRunUtc = nextRun,
                ErrorCode = failure.ProviderCode ?? failure.Code.ToString(),
                ErrorMessage = failure.Message,
                CompletedUtc = target.IsTerminal() ? now : null,
                ClaimedBy = null,
                ClaimExpiresUtc = null,
            },
            cancellationToken: cancellationToken);

        await RecordCurrentAttemptAsync(updated, complete: true, cancellationToken);
        return updated;
    }

    private async Task<int> ApplyToScopeAsync(
        Guid projectId,
        Guid? sceneId,
        Func<Guid, CancellationToken, Task<bool>> action,
        CancellationToken cancellationToken)
    {
        var jobs = await _jobs.ListAsync(cancellationToken);
        var matching = jobs.Where(job =>
            job.ProjectId == projectId &&
            (!sceneId.HasValue || job.SceneId == sceneId));

        var count = 0;
        foreach (var job in matching)
        {
            if (await action(job.Id, cancellationToken))
            {
                count++;
            }
        }

        return count;
    }

    private async Task<bool> ResumeOrRetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await RequireAsync(id, cancellationToken);
        return job.State == JobState.Paused
            ? await ResumeAsync(id, cancellationToken)
            : await RetryAsync(id, cancellationToken);
    }

    private async Task<GenerationJob> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await _jobs.GetAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException($"Job '{id}' was not found.");

    private async Task<bool> DependenciesCompletedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var dependencies = await LoadDependenciesAsync(jobId, cancellationToken);
        return dependencies.All(dependency => dependency.State == JobState.Completed);
    }

    private async Task<IReadOnlyList<GenerationJob>> LoadDependenciesAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var dependencyIds = await _jobs.GetDependenciesAsync(jobId, cancellationToken);
        var dependencies = new List<GenerationJob>(dependencyIds.Count);
        foreach (var dependencyId in dependencyIds)
        {
            var dependency = await _jobs.GetAsync(dependencyId, cancellationToken)
                ?? throw new InvalidDataException($"Dependency job '{dependencyId}' is missing.");
            dependencies.Add(dependency);
        }

        return dependencies;
    }

    private async Task<GenerationJob> TransitionAsync(
        GenerationJob job,
        JobState target,
        Func<GenerationJob, GenerationJob> mutate,
        bool explicitRestart = false,
        bool ignoreConcurrencyConflict = false,
        CancellationToken cancellationToken = default)
    {
        JobStateMachine.EnsureTransition(job.State, target, explicitRestart);
        var now = GetUtcNow();
        var updated = mutate(job) with
        {
            State = target,
            UpdatedUtc = now,
        };

        var saved = await _jobs.TryUpdateAsync(updated, job.State, cancellationToken);
        if (!saved)
        {
            if (ignoreConcurrencyConflict)
            {
                return await RequireAsync(job.Id, cancellationToken);
            }

            throw new InvalidOperationException($"Job '{job.Id}' changed concurrently.");
        }

        await _publisher.PublishAsync(job.Id, cancellationToken);
        return updated;
    }

    private async Task RecordCurrentAttemptAsync(
        GenerationJob job,
        bool complete,
        CancellationToken cancellationToken)
    {
        if (job.AttemptCount <= 0)
        {
            return;
        }

        var attempts = await _jobs.GetAttemptsAsync(job.Id, cancellationToken);
        var existing = attempts.FirstOrDefault(attempt => attempt.AttemptNumber == job.AttemptCount);
        if (existing is null)
        {
            return;
        }

        await _jobs.UpsertAttemptAsync(
            existing with
            {
                CompletedUtc = complete ? GetUtcNow() : null,
                State = job.State,
                ProviderTaskId = job.ProviderTaskId,
                ErrorCode = job.ErrorCode,
                ErrorMessage = job.ErrorMessage,
                EstimatedCost = job.EstimatedCost,
                ActualCost = job.ActualCost,
                Currency = job.Currency,
            },
            cancellationToken);
    }

    private DateTimeOffset GetUtcNow()
    {
        var value = _timeProvider.GetUtcNow().ToUniversalTime();
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static TimeSpan BackoffFor(int retryCount) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(0, retryCount - 1))));
}
