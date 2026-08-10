using System.Collections.Concurrent;
using OpenMusicVideoCreator.Application.Jobs;

namespace OpenMusicVideoCreator.Infrastructure.Jobs;

public sealed class InMemoryJobExecutionCancellationRegistry : IJobExecutionCancellationRegistry, IDisposable
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();

    public CancellationToken Register(Guid jobId, CancellationToken hostCancellationToken = default)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("Job ID is required.", nameof(jobId));

        var source = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        if (!_active.TryAdd(jobId, source))
        {
            source.Dispose();
            throw new InvalidOperationException($"Job '{jobId}' already has an active execution cancellation signal.");
        }

        return source.Token;
    }

    public void Cancel(Guid jobId)
    {
        if (_active.TryGetValue(jobId, out var source))
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Execution completed concurrently with cancellation.
            }
        }
    }

    public void Unregister(Guid jobId)
    {
        if (_active.TryRemove(jobId, out var source))
        {
            source.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var pair in _active.ToArray())
        {
            if (_active.TryRemove(pair.Key, out var source))
            {
                source.Cancel();
                source.Dispose();
            }
        }
    }
}
