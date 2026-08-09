using System.Security.Cryptography;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Infrastructure.Persistence;

namespace OpenMusicVideoCreator.Infrastructure.Media;

public sealed class LocalMediaStorage : IMediaStorage
{
    private static readonly string[] ProjectDirectories =
    [
        "source",
        "references",
        Path.Combine("references", "characters"),
        Path.Combine("references", "styles"),
        Path.Combine("references", "locations"),
        "analysis",
        "keyframes",
        "generated",
        "proxies",
        "renders",
    ];

    private readonly LocalMediaPathResolver _paths;

    public LocalMediaStorage(LocalMediaPathResolver paths)
    {
        _paths = paths;
    }

    public LocalMediaStorage(StorageOptions options)
        : this(new LocalMediaPathResolver(options))
    {
    }

    public Task EnsureProjectLayoutAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectRoot = _paths.GetProjectRoot(projectId);
        foreach (var directory in ProjectDirectories)
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, directory));
        }

        return Task.CompletedTask;
    }

    public async Task<StoredMedia> SaveAsync(
        Guid projectId,
        MediaStorageArea area,
        Stream source,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateFileName(fileName);
        await EnsureProjectLayoutAsync(projectId, cancellationToken);

        var targetDirectory = Path.Combine(_paths.GetProjectRoot(projectId), GetAreaDirectory(area));
        Directory.CreateDirectory(targetDirectory);

        var storedFileName = $"{Guid.NewGuid():N}-{fileName}";
        var targetPath = _paths.EnsureInsideRoot(Path.Combine(targetDirectory, storedFileName));

        try
        {
            await using (var target = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            await using var checksumStream = new FileStream(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            var hash = await SHA256.HashDataAsync(checksumStream, cancellationToken);
            var fileInfo = new FileInfo(targetPath);
            var relative = Path.GetRelativePath(_paths.Root, targetPath).Replace(Path.DirectorySeparatorChar, '/');

            return new StoredMedia(
                new MediaLocation(relative),
                fileInfo.Length,
                Convert.ToHexString(hash).ToLowerInvariant());
        }
        catch
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            throw;
        }
    }

    public Task<Stream> OpenReadAsync(
        MediaLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = _paths.Resolve(location);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> DeleteAsync(MediaLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = _paths.Resolve(location);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private static string GetAreaDirectory(MediaStorageArea area) => area switch
    {
        MediaStorageArea.Source => "source",
        MediaStorageArea.Reference => "references",
        MediaStorageArea.Analysis => "analysis",
        MediaStorageArea.Keyframe => "keyframes",
        MediaStorageArea.Generated => "generated",
        MediaStorageArea.Proxy => "proxies",
        MediaStorageArea.Render => "renders",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown media storage area."),
    };

    private static void ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("File name must be a safe leaf name without path segments.", nameof(fileName));
        }
    }
}
