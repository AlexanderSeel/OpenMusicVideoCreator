using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Timeline;
using OpenMusicVideoCreator.Domain.Timeline;

namespace OpenMusicVideoCreator.Infrastructure.Timeline;

public sealed class DuckDbProjectTimelineRepository : IProjectTimelineRepository
{
    private const string Key = "timeline.versions.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbProjectTimelineRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<ProjectTimelineVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken))
            .OrderByDescending(version => version.Version)
            .FirstOrDefault();

    public async Task<IReadOnlyList<ProjectTimelineVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken))
            .OrderByDescending(version => version.Version)
            .ToArray();

    public async Task UpsertAsync(ProjectTimelineVersion timeline, CancellationToken cancellationToken = default)
    {
        timeline.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(timeline.ProjectId, cancellationToken)).ToList();
            current.RemoveAll(version => version.Id == timeline.Id);
            current.Add(timeline);
            current.Sort((left, right) => left.Version.CompareTo(right.Version));
            await _settings.SetAsync(
                timeline.ProjectId,
                Key,
                JsonSerializer.Serialize(current, JsonOptions),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ProjectTimelineVersion>> ReadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<ProjectTimelineVersion[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Timeline versions could not be deserialized.");
    }
}
