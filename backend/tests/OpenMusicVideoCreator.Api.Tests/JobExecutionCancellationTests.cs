using OpenMusicVideoCreator.Infrastructure.Jobs;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class JobExecutionCancellationTests
{
    [Fact]
    public void Cancel_SignalsRegisteredExecutionToken()
    {
        using var registry = new InMemoryJobExecutionCancellationRegistry();
        var jobId = Guid.NewGuid();
        var token = registry.Register(jobId);

        registry.Cancel(jobId);

        Assert.True(token.IsCancellationRequested);
        registry.Unregister(jobId);
    }

    [Fact]
    public void Unregister_RemovesCompletedExecutionSignal()
    {
        using var registry = new InMemoryJobExecutionCancellationRegistry();
        var jobId = Guid.NewGuid();
        var token = registry.Register(jobId);

        registry.Unregister(jobId);
        registry.Cancel(jobId);

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Register_RejectsDuplicateActiveExecutionForSameJob()
    {
        using var registry = new InMemoryJobExecutionCancellationRegistry();
        var jobId = Guid.NewGuid();
        _ = registry.Register(jobId);

        Assert.Throws<InvalidOperationException>(() => registry.Register(jobId));
    }
}
