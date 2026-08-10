using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Rendering;
using OpenMusicVideoCreator.Domain.Rendering;

namespace OpenMusicVideoCreator.Infrastructure.Rendering;

public sealed class DuckDbProjectRenderRepository : IProjectRenderRepository
{
    private const string Key = "render.history.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbProjectRenderRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<ProjectRenderRecord>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken))
            .OrderByDescending(render => render.Version)
            .ToArray();

    public async Task<ProjectRenderRecord?> GetAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken)).FirstOrDefault(render => render.Id == renderId);

    public async Task UpsertAsync(ProjectRenderRecord render, CancellationToken cancellationToken = default)
    {
        render.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(render.ProjectId, cancellationToken)).ToList();
            current.RemoveAll(item => item.Id == render.Id);
            current.Add(render);
            current.Sort((left, right) => left.Version.CompareTo(right.Version));
            await _settings.SetAsync(
                render.ProjectId,
                Key,
                JsonSerializer.Serialize(current, JsonOptions),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ProjectRenderRecord>> ReadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<ProjectRenderRecord[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Render history could not be deserialized.");
    }
}
