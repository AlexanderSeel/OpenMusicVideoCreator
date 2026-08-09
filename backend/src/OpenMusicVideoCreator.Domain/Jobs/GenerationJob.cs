namespace OpenMusicVideoCreator.Domain.Jobs;

public enum JobState
{
    Draft,
    Queued,
    Submitting,
    ProviderQueued,
    Generating,
    Downloading,
    Validating,
    Completed,
    Paused,
    WaitingForQuota,
    WaitingForProvider,
    WaitingForDependency,
    RetryScheduled,
    Rejected,
    FailedRetryable,
    FailedPermanent,
    Cancelled,
}

public sealed record GenerationJob(
    Guid Id,
    Guid? ProjectId,
    Guid? SceneId,
    Guid? ParentJobId,
    string Type,
    string PayloadJson,
    string? ProviderId,
    string? ModelId,
    JobState State,
    JobState? ResumeState,
    int Priority,
    int AttemptCount,
    int RetryCount,
    int MaxRetries,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? NextRunUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? ProviderTaskId,
    string? ErrorCode,
    string? ErrorMessage,
    decimal? EstimatedCost,
    decimal? ActualCost,
    string? Currency,
    string? ClaimedBy,
    DateTimeOffset? ClaimExpiresUtc);

public sealed record JobAttempt(
    Guid JobId,
    int AttemptNumber,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    JobState State,
    string? ProviderTaskId,
    string? ErrorCode,
    string? ErrorMessage,
    decimal? EstimatedCost,
    decimal? ActualCost,
    string? Currency);

public static class JobStateMachine
{
    private static readonly IReadOnlyDictionary<JobState, IReadOnlySet<JobState>> Transitions =
        new Dictionary<JobState, IReadOnlySet<JobState>>
        {
            [JobState.Draft] = Set(JobState.Queued, JobState.Cancelled),
            [JobState.Queued] = Set(
                JobState.Submitting,
                JobState.Paused,
                JobState.WaitingForDependency,
                JobState.Cancelled),
            [JobState.Submitting] = ActiveTransitions(
                JobState.ProviderQueued,
                JobState.Generating,
                JobState.Completed),
            [JobState.ProviderQueued] = ActiveTransitions(
                JobState.Generating,
                JobState.Downloading,
                JobState.Completed),
            [JobState.Generating] = ActiveTransitions(
                JobState.Downloading,
                JobState.Validating,
                JobState.Completed),
            [JobState.Downloading] = ActiveTransitions(
                JobState.Validating,
                JobState.Completed),
            [JobState.Validating] = ActiveTransitions(JobState.Completed),
            [JobState.Paused] = Set(
                JobState.Queued,
                JobState.WaitingForDependency,
                JobState.WaitingForProvider,
                JobState.Cancelled),
            [JobState.WaitingForQuota] = Set(
                JobState.Queued,
                JobState.Paused,
                JobState.Cancelled,
                JobState.FailedPermanent),
            [JobState.WaitingForProvider] = Set(
                JobState.Queued,
                JobState.ProviderQueued,
                JobState.Generating,
                JobState.Downloading,
                JobState.RetryScheduled,
                JobState.Paused,
                JobState.Cancelled,
                JobState.FailedPermanent),
            [JobState.WaitingForDependency] = Set(
                JobState.Queued,
                JobState.Paused,
                JobState.Cancelled,
                JobState.FailedPermanent),
            [JobState.RetryScheduled] = Set(
                JobState.Queued,
                JobState.Paused,
                JobState.Cancelled,
                JobState.FailedPermanent),
            [JobState.FailedRetryable] = Set(
                JobState.RetryScheduled,
                JobState.Queued,
                JobState.Paused,
                JobState.Cancelled,
                JobState.FailedPermanent),
            [JobState.Completed] = Set(),
            [JobState.Rejected] = Set(),
            [JobState.FailedPermanent] = Set(),
            [JobState.Cancelled] = Set(),
        };

    public static bool CanTransition(JobState from, JobState to, bool explicitRestart = false)
    {
        if (from == to)
        {
            return true;
        }

        if (explicitRestart && to == JobState.Queued && from.IsTerminal())
        {
            return true;
        }

        return Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static void EnsureTransition(JobState from, JobState to, bool explicitRestart = false)
    {
        if (!CanTransition(from, to, explicitRestart))
        {
            throw new InvalidOperationException(
                $"Illegal job state transition from '{from}' to '{to}'.");
        }
    }

    public static bool IsTerminal(this JobState state) => state is
        JobState.Completed or
        JobState.Rejected or
        JobState.FailedPermanent or
        JobState.Cancelled;

    public static bool IsActivelyProcessing(this JobState state) => state is
        JobState.Submitting or
        JobState.ProviderQueued or
        JobState.Generating or
        JobState.Downloading or
        JobState.Validating;

    private static IReadOnlySet<JobState> ActiveTransitions(params JobState[] successful) =>
        successful
            .Concat(
            [
                JobState.WaitingForQuota,
                JobState.WaitingForProvider,
                JobState.RetryScheduled,
                JobState.Rejected,
                JobState.FailedRetryable,
                JobState.FailedPermanent,
                JobState.Paused,
                JobState.Cancelled,
            ])
            .ToHashSet();

    private static IReadOnlySet<JobState> Set(params JobState[] states) => states.ToHashSet();
}
