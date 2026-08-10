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
        TimeProvider timeProvider,
        IJobExecutionCancellationRegistry? executionCancellations = null)
    {
        _jobs = jobs;
        _jobService = jobService;
        _dispatcher = dispatcher;
        _publisher = publisher;
        _executionCancellations = executionCancellations ?? PassiveExecutionCancellationRegistry.Instance;
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
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (!await WasCancelledAsync(claimed.Id))
            {
                await ApplyWorkerFailureAsync(
                    claimed.Id,
                    "Job execution was cancelled without a persisted user cancellation.",
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            if (await WasCancelledAsync(claimed.Id))
            {
                return true;
            }

            await ApplyWorkerFailureAsync(
                claimed.Id,
                $"Job execution failed: {exception.Message}",
                cancellationToken);
        }
        finally
        {
            _executionCancellations.Unregister(claimed.Id);
        }

        return true;
    }

    private Task<GenerationJob> ApplyWorkerFailureAsync(
        Guid jobId,
        string message,
        CancellationToken cancellationToken) =>
        _jobService.ApplyProviderFailureAsync(
            jobId,
            new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                message,
                Retryable: true,
                RetryAfter: TimeSpan.FromSeconds(1),
                ProviderCode: "worker_exception"),
            cancellationToken);

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

    private sealed class PassiveExecutionCancellationRegistry : IJobExecutionCancellationRegistry
    {
        public static PassiveExecutionCancellationRegistry Instance { get; } = new();

        public CancellationToken Register(Guid jobId, CancellationToken hostCancellationToken = default) =>
            hostCancellationToken;

        public void Cancel(Guid jobId)
        {
        }

        public void Unregister(Guid jobId)
        {
        }
    }
}
