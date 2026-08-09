namespace OpenMusicVideoCreator.Application.Abstractions;

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
