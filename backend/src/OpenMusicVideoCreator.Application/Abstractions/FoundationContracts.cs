namespace OpenMusicVideoCreator.Application.Abstractions;

public sealed record ProviderDescriptor(string Id, IReadOnlySet<string> Capabilities);

public interface IProviderCatalog
{
    ValueTask<IReadOnlyCollection<ProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationPersistence
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public readonly record struct MediaLocation(string Value);

public interface IMediaStorage
{
    Task<MediaLocation> SaveAsync(Stream source, string fileName, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(MediaLocation location, CancellationToken cancellationToken = default);
}

public sealed record JobEnvelope(Guid Id, string Type, string Payload);

public interface IJobQueue
{
    ValueTask EnqueueAsync(JobEnvelope job, CancellationToken cancellationToken = default);
}

public sealed record RenderRequest(Guid JobId, IReadOnlyList<MediaLocation> Inputs, MediaLocation Output);

public sealed record RenderResult(MediaLocation Output, TimeSpan Duration);

public interface IRenderEngine
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default);
}
