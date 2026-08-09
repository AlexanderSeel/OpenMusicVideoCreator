namespace OpenMusicVideoCreator.Domain.Media;

public enum MediaCreationSource
{
    Uploaded,
    Imported,
    Generated,
    Derived,
    Rendered,
}

public sealed record MediaAssetMetadata(
    Guid Id,
    Guid? ProjectId,
    string Location,
    string ChecksumSha256,
    string MimeType,
    int? Width,
    int? Height,
    TimeSpan? Duration,
    long FileSize,
    MediaCreationSource CreationSource,
    DateTimeOffset CreatedUtc);
