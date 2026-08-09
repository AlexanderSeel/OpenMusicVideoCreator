using OpenMusicVideoCreator.Application.Providers;

namespace OpenMusicVideoCreator.Application.Jobs;

public sealed class JobProcessor
{
    private readonly IJobRepository _jobs;
    private readonly JobService _jobService;
    private readonly IJobExecutionDispatcher _dispatcher;
    private readonly IJobChangePublisher _publisher;
    private readonly TimeProvider _timeProvider;

    public JobProcessor(
        IJobRepository jobs,
        JobService jobService,
        IJobExecutionDispatcher dispatcher,
        IJobChangePublisher publisher,
        TimeProvider timeProvider)
    {
        _jobs = jobs;
        _jobService = jobService;
        _dispatcher = dispatcher;
        _publisher = publisher;
        _timeProvider = timeProvider;
    }

    public async Task<bool> ProcessNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Worker lease must be positive.");
        }

        await _jobService.MaintainRunnableStatesAsync(cancellationToken);
        var claimed = await _jobs.TryClaimNextAsync(
            workerId,
            GetUtcNow(),
            leaseDuration,
            cancellationToken);
        if (claimed is null)
        {
            return false;
        }

        await _publisher.PublishAsync(claimed.Id, cancellationToken);

        try
        {
            var result = await _dispatcher.ExecuteAsync(claimed, cancellationToken);
            await _jobService.ApplyExecutionResultAsync(claimed.Id, result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _jobService.ApplyProviderFailureAsync(
                claimed.Id,
                new ProviderFailure(
                    ProviderFailureCode.TransientFailure,
                    $"Job execution failed: {exception.Message}",
                    Retryable: true,
                    RetryAfter: TimeSpan.FromSeconds(1),
                    ProviderCode: "worker_exception"),
                cancellationToken);
        }

        return true;
    }

    private DateTimeOffset GetUtcNow()
    {
        var value = _timeProvider.GetUtcNow().ToUniversalTime();
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
