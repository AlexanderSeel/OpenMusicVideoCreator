using OpenMusicVideoCreator.Application.Jobs;

namespace OpenMusicVideoCreator.Api.Jobs;

public sealed class PersistentJobWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    private readonly JobProcessor _processor;
    private readonly JobService _jobService;
    private readonly ILogger<PersistentJobWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public PersistentJobWorker(
        JobProcessor processor,
        JobService jobService,
        ILogger<PersistentJobWorker> logger)
    {
        _processor = processor;
        _jobService = jobService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _jobService.RecoverInterruptedJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _processor.ProcessNextAsync(
                    _workerId,
                    ClaimLease,
                    stoppingToken);
                if (!processed)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Persistent job worker {WorkerId} failed while processing the queue.",
                    _workerId);
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
