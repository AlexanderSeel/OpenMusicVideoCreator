using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Application.Jobs;

public sealed record JobDefinition(
    Guid? ProjectId,
    Guid? SceneId,
    Guid? ParentJobId,
    string Type,
    string PayloadJson,
    string? ProviderId = null,
    string? ModelId = null,
    int Priority = 100,
    int MaxRetries = 3,
    decimal? EstimatedCost = null,
    string? Currency = "USD");

public interface IJobRepository
{
    Task CreateAsync(
        GenerationJob job,
        IReadOnlyCollection<Guid> dependencyIds,
        CancellationToken cancellationToken = default);

    Task<GenerationJob?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GenerationJob>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetDependenciesAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobAttempt>> GetAttemptsAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<bool> TryUpdateAsync(
        GenerationJob job,
        JobState expectedState,
        CancellationToken cancellationToken = default);

    Task<GenerationJob?> TryClaimNextAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task UpsertAttemptAsync(JobAttempt attempt, CancellationToken cancellationToken = default);
}

public interface IJobQueue
{
    Task<GenerationJob> EnqueueAsync(
        JobDefinition definition,
        IReadOnlyCollection<Guid>? dependencyIds = null,
        CancellationToken cancellationToken = default);
}

public interface IJobChangePublisher
{
    ValueTask PublishAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public interface IJobChangeStream
{
    IAsyncEnumerable<Guid> SubscribeAsync(CancellationToken cancellationToken = default);
}

public sealed record JobExecutionResult(
    JobState State,
    string? ProviderTaskId = null,
    ProviderFailure? Failure = null,
    decimal? ActualCost = null,
    string? Currency = null)
{
    public static JobExecutionResult Completed(decimal? actualCost = null, string? currency = null) =>
        new(JobState.Completed, ActualCost: actualCost, Currency: currency);

    public static JobExecutionResult Failed(ProviderFailure failure) =>
        new(JobState.FailedRetryable, Failure: failure);
}

public interface IJobExecutionDispatcher
{
    Task<JobExecutionResult> ExecuteAsync(
        GenerationJob job,
        CancellationToken cancellationToken = default);
}
