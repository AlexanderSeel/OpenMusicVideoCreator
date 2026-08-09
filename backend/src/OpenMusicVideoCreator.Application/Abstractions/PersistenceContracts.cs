using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Abstractions;

public interface IApplicationPersistence
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public interface IProjectRepository
{
    Task<IReadOnlyList<MusicVideoProject>> ListAsync(CancellationToken cancellationToken = default);

    Task<MusicVideoProject?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(MusicVideoProject project, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IApplicationSettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string valueJson, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public interface IProjectSettingsRepository
{
    Task<string?> GetAsync(Guid projectId, string key, CancellationToken cancellationToken = default);

    Task SetAsync(Guid projectId, string key, string valueJson, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid projectId, string key, CancellationToken cancellationToken = default);
}

public interface IMediaAssetRepository
{
    Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
