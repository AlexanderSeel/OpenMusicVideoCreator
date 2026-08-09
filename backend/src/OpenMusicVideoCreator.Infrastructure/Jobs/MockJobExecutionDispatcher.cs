using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Infrastructure.Jobs;

public sealed class MockJobExecutionDispatcher : IJobExecutionDispatcher
{
    public Task<JobExecutionResult> ExecuteAsync(
        GenerationJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        var result = job.Type switch
        {
            "mock:success" => JobExecutionResult.Completed(),
            "mock:provider-wait" => new JobExecutionResult(
                JobState.WaitingForProvider,
                ProviderTaskId: $"mock:{job.Id:N}"),
            "mock:quota" => JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.QuotaExhausted,
                "Mock quota exhausted.",
                Retryable: false,
                ProviderCode: "mock_quota")),
            "mock:rate-limit" => JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.RateLimited,
                "Mock rate limit reached.",
                Retryable: true,
                RetryAfter: TimeSpan.FromSeconds(1),
                ProviderCode: "mock_rate_limit")),
            "mock:provider-unavailable" => JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.ProviderUnavailable,
                "Mock provider unavailable.",
                Retryable: true,
                ProviderCode: "mock_provider_unavailable")),
            "mock:rejected" => JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.ModerationRejected,
                "Mock request rejected.",
                Retryable: false,
                ProviderCode: "mock_rejected")),
            "mock:permanent" => JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                "Mock permanent failure.",
                Retryable: false,
                ProviderCode: "mock_permanent")),
            _ => JobExecutionResult.Failed(new ProviderFailure(
                ProviderFailureCode.UnsupportedCapability,
                $"No job execution handler is registered for '{job.Type}'.",
                Retryable: false,
                ProviderCode: "unsupported_job_type")),
        };

        return Task.FromResult(result);
    }
}
