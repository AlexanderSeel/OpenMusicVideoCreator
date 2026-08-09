namespace OpenMusicVideoCreator.Application.Abstractions;

public sealed record RenderRequest(Guid JobId, IReadOnlyList<MediaLocation> Inputs, MediaLocation Output);

public sealed record RenderResult(MediaLocation Output, TimeSpan Duration);

public interface IRenderEngine
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default);
}
