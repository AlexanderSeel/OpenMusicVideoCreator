using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Infrastructure.Persistence;

namespace OpenMusicVideoCreator.Infrastructure.Media;

public sealed class LocalMediaPathResolver
{
    private readonly string _root;

    public LocalMediaPathResolver(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(options.ProjectsRoot);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string GetProjectRoot(Guid projectId) =>
        EnsureInsideRoot(Path.Combine(_root, projectId.ToString("D")));

    public string Resolve(MediaLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.Value) || Path.IsPathRooted(location.Value))
        {
            throw new ArgumentException("Media location must be a relative storage path.", nameof(location));
        }

        return EnsureInsideRoot(Path.Combine(_root, location.Value.Replace('/', Path.DirectorySeparatorChar)));
    }

    public string EnsureInsideRoot(string candidate)
    {
        var fullPath = Path.GetFullPath(candidate);
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved media path escapes the configured storage root.");
        }

        return fullPath;
    }
}
