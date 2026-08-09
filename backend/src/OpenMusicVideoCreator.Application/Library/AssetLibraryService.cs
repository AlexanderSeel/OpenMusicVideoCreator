using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Library;
using OpenMusicVideoCreator.Domain.Media;

namespace OpenMusicVideoCreator.Application.Library;

public sealed class AssetLibraryService
{
    public const long MaxAssetBytes = 256L * 1024L * 1024L;

    private static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4", ".mov", ".webm",
    };

    private readonly IAssetLibraryRepository _assets;
    private readonly IVisualLibraryRepository _visualLibrary;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly ILibraryMediaStorage _storage;
    private readonly IMediaPreviewGenerator _previewGenerator;
    private readonly TimeProvider _timeProvider;

    public AssetLibraryService(
        IAssetLibraryRepository assets,
        IVisualLibraryRepository visualLibrary,
        IMediaAssetRepository mediaAssets,
        ILibraryMediaStorage storage,
        IMediaPreviewGenerator previewGenerator,
        TimeProvider timeProvider)
    {
        _assets = assets;
        _visualLibrary = visualLibrary;
        _mediaAssets = mediaAssets;
        _storage = storage;
        _previewGenerator = previewGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<AssetLibraryEntry>> ListAsync(
        string? query = null,
        IReadOnlyList<string>? tags = null,
        bool favoritesOnly = false,
        CancellationToken cancellationToken = default)
    {
        var items = await _assets.ListAsync(cancellationToken);
        IEnumerable<AssetLibraryEntry> filtered = items;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            filtered = filtered.Where(item =>
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.SourceDescription.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        if (tags is { Count: > 0 })
        {
            var required = tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToArray();
            filtered = filtered.Where(item => required.All(requiredTag =>
                item.Tags.Contains(requiredTag, StringComparer.OrdinalIgnoreCase)));
        }
        if (favoritesOnly)
        {
            filtered = filtered.Where(item => item.IsFavorite);
        }

        return filtered
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<AssetLibraryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _assets.GetAsync(id, cancellationToken);

    public async Task<AssetLibraryEntry> UploadAsync(
        Stream source,
        string fileName,
        string mimeType,
        long fileSize,
        string? name,
        IReadOnlyList<string>? tags,
        string? sourceDescription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateUpload(fileName, mimeType, fileSize);
        var safeMimeType = NormalizeMimeType(mimeType);
        var stored = await _storage.SaveOriginalAsync(source, fileName, cancellationToken);
        var now = GetUtcNow();
        var media = new MediaAssetMetadata(
            Guid.NewGuid(),
            ProjectId: null,
            stored.Location.Value,
            stored.ChecksumSha256,
            safeMimeType,
            Width: null,
            Height: null,
            Duration: null,
            stored.FileSize,
            MediaCreationSource.Uploaded,
            now);
        await _mediaAssets.UpsertAsync(media, cancellationToken);

        Guid? previewMediaId = null;
        await using var preview = await _previewGenerator.GenerateAsync(stored.Location, safeMimeType, cancellationToken);
        if (preview is not null)
        {
            var previewStored = await _storage.SavePreviewAsync(
                preview.Content,
                preview.FileName,
                cancellationToken);
            var previewMedia = new MediaAssetMetadata(
                Guid.NewGuid(),
                ProjectId: null,
                previewStored.Location.Value,
                previewStored.ChecksumSha256,
                preview.MimeType,
                Width: null,
                Height: null,
                Duration: null,
                previewStored.FileSize,
                MediaCreationSource.Derived,
                now);
            await _mediaAssets.UpsertAsync(previewMedia, cancellationToken);
            previewMediaId = previewMedia.Id;
        }

        var entry = new AssetLibraryEntry(
            Guid.NewGuid(),
            media.Id,
            previewMediaId,
            string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name.Trim(),
            NormalizeTags(tags),
            IsFavorite: false,
            sourceDescription?.Trim() ?? "Uploaded visual reference",
            now,
            now);
        await _assets.UpsertAsync(entry, cancellationToken);
        return entry;
    }

    public async Task<AssetLibraryEntry> UpdateAsync(
        Guid id,
        string name,
        IReadOnlyList<string>? tags,
        bool isFavorite,
        string? sourceDescription,
        CancellationToken cancellationToken = default)
    {
        var existing = await _assets.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Asset library entry '{id}' was not found.");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Asset name is required.", nameof(name));
        }

        var updated = existing with
        {
            Name = name.Trim(),
            Tags = NormalizeTags(tags),
            IsFavorite = isFavorite,
            SourceDescription = sourceDescription?.Trim() ?? string.Empty,
            UpdatedUtc = GetUtcNow(),
        };
        await _assets.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<AssetDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await _assets.GetAsync(id, cancellationToken) is null)
        {
            return new AssetDeleteResult(false, []);
        }

        var visualItems = await _visualLibrary.ListAsync(cancellationToken);
        var references = visualItems
            .Where(item => item.AssetEntryIds.Contains(id) ||
                (item.Character?.Outfits.Any(outfit => outfit.AssetEntryIds.Contains(id)) ?? false))
            .Select(item => item.Id)
            .ToArray();
        if (references.Length > 0)
        {
            return new AssetDeleteResult(false, references);
        }

        // Entry deletion is intentionally metadata-only. Underlying media remains available for
        // explicit cleanup/recovery and is never silently destroyed by a library metadata action.
        return new AssetDeleteResult(await _assets.DeleteAsync(id, cancellationToken), []);
    }

    private static void ValidateUpload(string fileName, string mimeType, long fileSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (fileSize <= 0 || fileSize > MaxAssetBytes)
        {
            throw new ArgumentException($"Visual asset must be between 1 byte and {MaxAssetBytes / (1024 * 1024)} MB.", nameof(fileSize));
        }
        if (fileName.IndexOfAny(['/', '\\']) >= 0 || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Visual asset file name must be a safe leaf name.", nameof(fileName));
        }
        if (!AllowedExtensions.Contains(Path.GetExtension(fileName)))
        {
            throw new ArgumentException("Unsupported visual asset file extension.", nameof(fileName));
        }
        var normalized = NormalizeMimeType(mimeType);
        if (!normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Visual asset must be an image or video.", nameof(mimeType));
        }
    }

    private static string NormalizeMimeType(string value) =>
        value.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags) => (tags ?? [])
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
