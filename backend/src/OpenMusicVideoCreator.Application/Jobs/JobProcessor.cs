using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Application.Jobs;

public sealed class JobProcessor
{
    private readonly IJobRepository _jobs;
    private readonly JobService _jobService;
    private readonly IJobExecutionDispatcher _dispatcher;
    private readonly IJobChangePublisher _publisher;
    private readonly IJobExecutionCancellationRegistry _executionCancellations;
    private readonly TimeProvider _timeProvider;

    public JobProcessor(
        IJobRepository jobs,
        JobService jobService,
        IJobExecutionDispatcher dispatcher,
        IJobChangePublisher publisher,
        IJobExecutionCancellationRegistry executionCancellations,
        TimeProvider timeProvider)
    {
        _jobs = jobs;
        _jobService = jobService;
        _dispatcher = dispatcher;
        _publisher = publisher;
        _executionCancellations = executionCancellations;
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

        var executionToken = _executionCancellations.Register(claimed.Id, cancellationToken);
        await _publisher.PublishAsync(claimed.Id, cancellationToken);

        try
        {
            var result = await _dispatcher.ExecuteAsync(claimed, executionToken);
            if (await WasCancelledAsync(claimed.Id))
            {
                return true;
            }

            await _jobService.ApplyExecutionResultAsync(claimed.Id, result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (await WasCancelledAsync(claimed.Id))
        {
            // User cancellation is already persisted. Do not turn the cancelled local execution into a retry/failure.
        }
        catch (Exception exception)
        {
            if (await WasCancelledAsync(claimed.Id))
            {
                return true;
            }

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
        finally
        {
            _executionCancellations.Unregister(claimed.Id);
        }

        return true;
    }

    private async Task<bool> WasCancelledAsync(Guid jobId)
    {
        var current = await _jobs.GetAsync(jobId, CancellationToken.None);
        return current?.State == JobState.Cancelled;
    }

    private DateTimeOffset GetUtcNow()
    {
        var value = _timeProvider.GetUtcNow().ToUniversalTime();
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
