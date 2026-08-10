using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class DuckDbKeyframeGenerationSettingsRepository : IKeyframeGenerationSettingsRepository
{
    private const string KeyPrefix = "generation.keyframes.settings.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectSettingsRepository _settings;

    public DuckDbKeyframeGenerationSettingsRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<SceneKeyframeGenerationSettings?> GetAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var json = await _settings.GetAsync(projectId, Key(sceneId), cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;
        var value = JsonSerializer.Deserialize<SceneKeyframeGenerationSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("Persisted keyframe generation settings are invalid.");
        if (value.ProjectId != projectId || value.SceneId != sceneId)
        {
            throw new InvalidDataException("Persisted keyframe generation settings identity does not match the requested scene.");
        }
        value.Validate();
        return value;
    }

    public Task UpsertAsync(
        SceneKeyframeGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();
        return _settings.SetAsync(
            settings.ProjectId,
            Key(settings.SceneId),
            JsonSerializer.Serialize(settings, JsonOptions),
            cancellationToken);
    }

    private static string Key(Guid sceneId) => KeyPrefix + sceneId.ToString("N");
}
