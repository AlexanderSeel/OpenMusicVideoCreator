using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Api.Contracts.Jobs;

public sealed record JobCreateRequest(
    Guid? ProjectId,
    Guid? SceneId,
    Guid? ParentJobId,
    string Type,
    string PayloadJson,
    string? ProviderId,
    string? ModelId,
    int Priority,
    int MaxRetries,
    decimal? EstimatedCost,
    string? Currency,
    IReadOnlyList<Guid>? Dependencies)
{
    public JobDefinition ToDefinition() => new(
        ProjectId,
        SceneId,
        ParentJobId,
        Type,
        PayloadJson,
        ProviderId,
        ModelId,
        Priority,
        MaxRetries,
        EstimatedCost,
        Currency);
}

public sealed record JobResponse(
    Guid Id,
    Guid? ProjectId,
    Guid? SceneId,
    Guid? ParentJobId,
    string Type,
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
    string? Currency)
{
    public static JobResponse FromDomain(GenerationJob job) => new(
        job.Id,
        job.ProjectId,
        job.SceneId,
        job.ParentJobId,
        job.Type,
        job.ProviderId,
        job.ModelId,
        job.State,
        job.ResumeState,
        job.Priority,
        job.AttemptCount,
        job.RetryCount,
        job.MaxRetries,
        job.CreatedUtc,
        job.UpdatedUtc,
        job.NextRunUtc,
        job.StartedUtc,
        job.CompletedUtc,
        job.ProviderTaskId,
        job.ErrorCode,
        job.ErrorMessage,
        job.EstimatedCost,
        job.ActualCost,
        job.Currency);
}

public sealed record JobAttemptResponse(
    int AttemptNumber,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    JobState State,
    string? ProviderTaskId,
    string? ErrorCode,
    string? ErrorMessage,
    decimal? EstimatedCost,
    decimal? ActualCost,
    string? Currency)
{
    public static JobAttemptResponse FromDomain(JobAttempt attempt) => new(
        attempt.AttemptNumber,
        attempt.StartedUtc,
        attempt.CompletedUtc,
        attempt.State,
        attempt.ProviderTaskId,
        attempt.ErrorCode,
        attempt.ErrorMessage,
        attempt.EstimatedCost,
        attempt.ActualCost,
        attempt.Currency);
}

public sealed record JobScopeActionResponse(int AffectedJobs);
