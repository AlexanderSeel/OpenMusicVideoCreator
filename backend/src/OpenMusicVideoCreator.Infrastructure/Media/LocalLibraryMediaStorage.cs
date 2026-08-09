using System.Security.Cryptography;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Library;

namespace OpenMusicVideoCreator.Infrastructure.Media;

public sealed class LocalLibraryMediaStorage : ILibraryMediaStorage
{
    private readonly LocalMediaPathResolver _paths;

    public LocalLibraryMediaStorage(LocalMediaPathResolver paths)
    {
        _paths = paths;
    }

    public Task<StoredMedia> SaveOriginalAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken = default) =>
        SaveAsync("originals", source, fileName, cancellationToken);

    public Task<StoredMedia> SavePreviewAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken = default) =>
        SaveAsync("previews", source, fileName, cancellationToken);

    private async Task<StoredMedia> SaveAsync(
        string area,
        Stream source,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateFileName(fileName);

        var directory = _paths.EnsureInsideRoot(Path.Combine(_paths.Root, "library", area));
        Directory.CreateDirectory(directory);
        var storedName = $"{Guid.NewGuid():N}-{fileName}";
        var targetPath = _paths.EnsureInsideRoot(Path.Combine(directory, storedName));

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
            var checksum = await SHA256.HashDataAsync(checksumStream, cancellationToken);
            var relative = Path.GetRelativePath(_paths.Root, targetPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            return new StoredMedia(
                new MediaLocation(relative),
                new FileInfo(targetPath).Length,
                Convert.ToHexString(checksum).ToLowerInvariant());
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

    private static void ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Library media file name must be a safe leaf name.", nameof(fileName));
        }
    }
}
