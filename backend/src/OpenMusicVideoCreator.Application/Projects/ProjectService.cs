using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Projects;

public sealed class ProjectService
{
    public const int PortableDocumentVersion = 1;

    private static readonly JsonSerializerOptions PortableJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectRepository _projects;
    private readonly TimeProvider _timeProvider;

    public ProjectService(IProjectRepository projects, TimeProvider timeProvider)
    {
        _projects = projects;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<MusicVideoProject>> ListAsync(CancellationToken cancellationToken = default) =>
        _projects.ListAsync(cancellationToken);

    public Task<MusicVideoProject?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _projects.GetAsync(id, cancellationToken);

    public async Task<MusicVideoProject> CreateAsync(
        ProjectDraft draft,
        CancellationToken cancellationToken = default)
    {
        var project = MusicVideoProject.Create(Guid.NewGuid(), draft, GetUtcNow());
        await _projects.UpsertAsync(project, cancellationToken);
        return project;
    }

    public async Task<MusicVideoProject> UpdateAsync(
        Guid id,
        ProjectDraft draft,
        CancellationToken cancellationToken = default)
    {
        var existing = await _projects.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        var updated = existing.Update(draft, GetUtcNow());
        await _projects.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _projects.DeleteAsync(id, cancellationToken);

    public async Task<string> ExportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        return JsonSerializer.Serialize(
            new PortableProjectDocument(PortableDocumentVersion, project),
            PortableJsonOptions);
    }

    public async Task<MusicVideoProject> ImportAsync(
        string portableJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portableJson))
        {
            throw new ArgumentException("Portable project JSON is required.", nameof(portableJson));
        }

        var document = JsonSerializer.Deserialize<PortableProjectDocument>(portableJson, PortableJsonOptions)
            ?? throw new InvalidDataException("Portable project JSON could not be deserialized.");

        if (document.Version != PortableDocumentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported portable project document version '{document.Version}'.");
        }

        if (document.Project is null ||
            document.Project.TargetPlatforms is null ||
            document.Project.References is null)
        {
            throw new InvalidDataException("Portable project document is missing required project data.");
        }

        var imported = document.Project with
        {
            TargetPlatforms = document.Project.TargetPlatforms.ToArray(),
            References = document.Project.References.ToArray(),
        };

        await _projects.UpsertAsync(imported, cancellationToken);
        return imported;
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

public sealed record PortableProjectDocument(int Version, MusicVideoProject Project);
