namespace OpenMusicVideoCreator.Application.Abstractions;

public enum MediaStorageArea
{
    Source,
    Reference,
    Analysis,
    Keyframe,
    Generated,
    Proxy,
    Render,
}

public readonly record struct MediaLocation(string Value)
{
    public override string ToString() => Value;
}

public sealed record StoredMedia(MediaLocation Location, long FileSize, string ChecksumSha256);

public interface IMediaStorage
{
    Task EnsureProjectLayoutAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<StoredMedia> SaveAsync(
        Guid projectId,
        MediaStorageArea area,
        Stream source,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(MediaLocation location, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(MediaLocation location, CancellationToken cancellationToken = default);
}
