using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Library;

namespace OpenMusicVideoCreator.Application.Library;

public interface IVisualLibraryRepository
{
    Task<IReadOnlyList<VisualLibraryItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<VisualLibraryItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertAsync(VisualLibraryItem item, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IAssetLibraryRepository
{
    Task<IReadOnlyList<AssetLibraryEntry>> ListAsync(CancellationToken cancellationToken = default);
    Task<AssetLibraryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertAsync(AssetLibraryEntry item, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IProjectCharacterStateRepository
{
    Task<IReadOnlyList<ProjectCharacterState>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectCharacterState?> GetAsync(Guid projectId, Guid characterId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ProjectCharacterState state, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid projectId, Guid characterId, CancellationToken cancellationToken = default);
}

public interface ILibraryMediaStorage
{
    Task<StoredMedia> SaveOriginalAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<StoredMedia> SavePreviewAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken = default);
}

public sealed record GeneratedMediaPreview(Stream Content, string FileName, string MimeType) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IMediaPreviewGenerator
{
    Task<GeneratedMediaPreview?> GenerateAsync(
        MediaLocation source,
        string mimeType,
        CancellationToken cancellationToken = default);
}

public sealed record LibraryDeleteResult(bool Deleted, IReadOnlyList<Guid> ReferencingProjectIds);
public sealed record AssetDeleteResult(bool Deleted, IReadOnlyList<Guid> ReferencingLibraryItemIds);
