using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class DuckDbVideoGenerationSettingsRepository : IVideoGenerationSettingsRepository
{
    private const string Key = "generation.video-settings.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbVideoGenerationSettingsRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<SceneVideoGenerationSettings?> GetAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken)).FirstOrDefault(item => item.SceneId == sceneId);

    public async Task UpsertAsync(SceneVideoGenerationSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(settings.ProjectId, cancellationToken)).ToList();
            current.RemoveAll(item => item.SceneId == settings.SceneId);
            current.Add(settings);
            await WriteAsync(settings.ProjectId, current, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<SceneVideoGenerationSettings>> ReadAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<SceneVideoGenerationSettings[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Video generation settings could not be deserialized.");
    }

    private Task WriteAsync(
        Guid projectId,
        IReadOnlyList<SceneVideoGenerationSettings> settings,
        CancellationToken cancellationToken) =>
        _settings.SetAsync(projectId, Key, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
}
