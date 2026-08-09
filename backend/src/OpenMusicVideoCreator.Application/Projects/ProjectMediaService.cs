using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Projects;

public sealed record ProjectSong(
    Guid AssetId,
    string MimeType,
    long FileSize,
    string ChecksumSha256,
    DateTimeOffset CreatedUtc);

public sealed class ProjectMediaService
{
    public const long MaxSongBytes = 512L * 1024L * 1024L;

    private static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".wav",
        ".m4a",
        ".aac",
        ".flac",
        ".ogg",
        ".opus",
        ".webm",
    };

    private readonly IProjectRepository _projects;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IMediaStorage _mediaStorage;
    private readonly TimeProvider _timeProvider;

    public ProjectMediaService(
        IProjectRepository projects,
        IMediaAssetRepository mediaAssets,
        IMediaStorage mediaStorage,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _mediaAssets = mediaAssets;
        _mediaStorage = mediaStorage;
        _timeProvider = timeProvider;
    }

    public async Task<ProjectSong?> GetSongAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var songReference = project.References.FirstOrDefault(reference => reference.Kind == ProjectReferenceKind.Song);
        if (songReference is null)
        {
            return null;
        }

        var asset = await _mediaAssets.GetAsync(songReference.ReferenceId, cancellationToken);
        if (asset is null)
        {
            throw new InvalidDataException(
                $"Project '{projectId}' references missing song asset '{songReference.ReferenceId}'.");
        }

        return ToSong(asset);
    }

    public async Task<ProjectSong> UploadSongAsync(
        Guid projectId,
        Stream source,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        ValidateSong(fileName, contentType, fileSize);
        await _mediaStorage.EnsureProjectLayoutAsync(projectId, cancellationToken);
        var stored = await _mediaStorage.SaveAsync(
            projectId,
            MediaStorageArea.Source,
            source,
            fileName,
            cancellationToken);

        var now = GetUtcNow();
        var asset = new MediaAssetMetadata(
            Guid.NewGuid(),
            projectId,
            stored.Location.Value,
            stored.ChecksumSha256,
            NormalizeContentType(contentType),
            Width: null,
            Height: null,
            Duration: null,
            stored.FileSize,
            MediaCreationSource.Uploaded,
            now);
        await _mediaAssets.UpsertAsync(asset, cancellationToken);

        var references = project.References
            .Where(reference => reference.Kind != ProjectReferenceKind.Song)
            .Append(new ProjectReference(ProjectReferenceKind.Song, asset.Id))
            .ToArray();
        var updated = project with
        {
            References = references,
            UpdatedUtc = now,
        };
        await _projects.UpsertAsync(updated, cancellationToken);

        return ToSong(asset);
    }

    private static void ValidateSong(string fileName, string contentType, long fileSize)
    {
        if (fileSize <= 0)
        {
            throw new ArgumentException("Song file must not be empty.", nameof(fileSize));
        }

        if (fileSize > MaxSongBytes)
        {
            throw new ArgumentException(
                $"Song file exceeds the {MaxSongBytes / (1024 * 1024)} MB upload limit.",
                nameof(fileSize));
        }

        if (fileName.IndexOfAny(['/', '\\']) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Song file name must not contain a path.", nameof(fileName));
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException($"Unsupported song file extension '{extension}'.", nameof(fileName));
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (!normalizedContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedContentType, "video/webm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported song MIME type '{contentType}'.", nameof(contentType));
        }
    }

    private static string NormalizeContentType(string contentType) =>
        contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();

    private static ProjectSong ToSong(MediaAssetMetadata asset) => new(
        asset.Id,
        asset.MimeType,
        asset.FileSize,
        asset.ChecksumSha256,
        asset.CreatedUtc);

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
