using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OpenMusicVideoCreator.Application.Jobs;

namespace OpenMusicVideoCreator.Infrastructure.Jobs;

public sealed class JobChangeHub : IJobChangePublisher, IJobChangeStream
{
    private readonly ConcurrentDictionary<Guid, Channel<Guid>> _subscribers = new();

    public ValueTask PublishAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(jobId);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<Guid> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        if (!_subscribers.TryAdd(subscriberId, channel))
        {
            throw new InvalidOperationException("Could not register job-change subscriber.");
        }

        try
        {
            await foreach (var jobId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return jobId;
            }
        }
        finally
        {
            if (_subscribers.TryRemove(subscriberId, out var removed))
            {
                removed.Writer.TryComplete();
            }
        }
    }
}
