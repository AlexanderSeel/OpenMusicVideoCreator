using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Domain.Planning;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbPlanningRepository :
    IVisualArcRepository,
    IStoryboardRepository,
    IPromptHistoryRepository
{
    private const string VisualArcsKey = "planning.visual-arcs.v1";
    private const string StoryboardsKey = "planning.storyboards.v1";
    private const string PromptsKey = "planning.prompts.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbPlanningRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    async Task<VisualArcVersion?> IVisualArcRepository.GetLatestAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await ReadAsync<VisualArcVersion>(projectId, VisualArcsKey, cancellationToken))
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

    async Task<IReadOnlyList<VisualArcVersion>> IVisualArcRepository.ListVersionsAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await ReadAsync<VisualArcVersion>(projectId, VisualArcsKey, cancellationToken))
            .OrderByDescending(item => item.Version)
            .ToArray();

    Task IVisualArcRepository.UpsertAsync(VisualArcVersion visualArc, CancellationToken cancellationToken) =>
        UpsertAsync(visualArc.ProjectId, VisualArcsKey, visualArc, item => item.Id, item => item.Version, cancellationToken);

    async Task<StoryboardVersion?> IStoryboardRepository.GetLatestAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await ReadAsync<StoryboardVersion>(projectId, StoryboardsKey, cancellationToken))
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

    async Task<IReadOnlyList<StoryboardVersion>> IStoryboardRepository.ListVersionsAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await ReadAsync<StoryboardVersion>(projectId, StoryboardsKey, cancellationToken))
            .OrderByDescending(item => item.Version)
            .ToArray();

    Task IStoryboardRepository.UpsertAsync(StoryboardVersion storyboard, CancellationToken cancellationToken) =>
        UpsertAsync(storyboard.ProjectId, StoryboardsKey, storyboard, item => item.Id, item => item.Version, cancellationToken);

    async Task<IReadOnlyList<PromptVersion>> IPromptHistoryRepository.ListBySceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken) =>
        (await ReadAsync<PromptVersion>(projectId, PromptsKey, cancellationToken))
            .Where(item => item.SceneId == sceneId)
            .OrderByDescending(item => item.Version)
            .ToArray();

    async Task<PromptVersion?> IPromptHistoryRepository.GetLatestBySceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken) =>
        (await ReadAsync<PromptVersion>(projectId, PromptsKey, cancellationToken))
            .Where(item => item.SceneId == sceneId)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

    Task IPromptHistoryRepository.UpsertAsync(PromptVersion prompt, CancellationToken cancellationToken) =>
        UpsertAsync(prompt.ProjectId, PromptsKey, prompt, item => item.Id, item => item.Version, cancellationToken);

    private async Task<IReadOnlyList<T>> ReadAsync<T>(
        Guid projectId,
        string key,
        CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<T[]>(json, JsonOptions)
            ?? throw new InvalidDataException($"Planning setting '{key}' could not be deserialized.");
    }

    private async Task UpsertAsync<T>(
        Guid projectId,
        string key,
        T value,
        Func<T, Guid> idSelector,
        Func<T, int> versionSelector,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync<T>(projectId, key, cancellationToken)).ToList();
            var id = idSelector(value);
            current.RemoveAll(item => idSelector(item) == id);
            current.Add(value);
            current.Sort((left, right) => versionSelector(left).CompareTo(versionSelector(right)));
            await _settings.SetAsync(
                projectId,
                key,
                JsonSerializer.Serialize(current, JsonOptions),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
