using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Library;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Library;

public sealed class VisualLibraryService
{
    private readonly IVisualLibraryRepository _library;
    private readonly IAssetLibraryRepository _assets;
    private readonly IProjectRepository _projects;
    private readonly TimeProvider _timeProvider;

    public VisualLibraryService(
        IVisualLibraryRepository library,
        IAssetLibraryRepository assets,
        IProjectRepository projects,
        TimeProvider timeProvider)
    {
        _library = library;
        _assets = assets;
        _projects = projects;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<VisualLibraryItem>> ListAsync(
        VisualLibraryKind? kind = null,
        string? query = null,
        IReadOnlyList<string>? tags = null,
        bool favoritesOnly = false,
        CancellationToken cancellationToken = default)
    {
        var items = await _library.ListAsync(cancellationToken);
        IEnumerable<VisualLibraryItem> filtered = items;
        if (kind.HasValue)
        {
            filtered = filtered.Where(item => item.Kind == kind.Value);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            filtered = filtered.Where(item =>
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
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

    public Task<VisualLibraryItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _library.GetAsync(id, cancellationToken);

    public async Task<VisualLibraryItem> CreateAsync(
        VisualLibraryDraft draft,
        CancellationToken cancellationToken = default)
    {
        await ValidateAssetReferencesAsync(draft, cancellationToken);
        var item = VisualLibraryItem.Create(Guid.NewGuid(), draft, GetUtcNow());
        await _library.UpsertAsync(item, cancellationToken);
        return item;
    }

    public async Task<VisualLibraryItem> UpdateAsync(
        Guid id,
        VisualLibraryDraft draft,
        CancellationToken cancellationToken = default)
    {
        var existing = await _library.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Library item '{id}' was not found.");
        await ValidateAssetReferencesAsync(draft, cancellationToken);
        var updated = existing.Update(draft, GetUtcNow());
        await _library.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<LibraryDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _library.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return new LibraryDeleteResult(false, []);
        }

        var referenceKind = existing.Kind switch
        {
            VisualLibraryKind.Character => ProjectReferenceKind.Character,
            VisualLibraryKind.Style => ProjectReferenceKind.Style,
            VisualLibraryKind.Location => ProjectReferenceKind.Location,
            _ => throw new ArgumentOutOfRangeException(),
        };
        var projects = await _projects.ListAsync(cancellationToken);
        var referencingProjects = projects
            .Where(project => project.References.Any(reference =>
                reference.Kind == referenceKind && reference.ReferenceId == id))
            .Select(project => project.Id)
            .ToArray();
        if (referencingProjects.Length > 0)
        {
            return new LibraryDeleteResult(false, referencingProjects);
        }

        return new LibraryDeleteResult(await _library.DeleteAsync(id, cancellationToken), []);
    }

    private async Task ValidateAssetReferencesAsync(
        VisualLibraryDraft draft,
        CancellationToken cancellationToken)
    {
        var assetIds = draft.AssetEntryIds
            .Concat(draft.Character?.Outfits.SelectMany(outfit => outfit.AssetEntryIds) ?? [])
            .Distinct()
            .ToArray();
        foreach (var assetId in assetIds)
        {
            if (await _assets.GetAsync(assetId, cancellationToken) is null)
            {
                throw new ArgumentException($"Asset library entry '{assetId}' was not found.", nameof(draft));
            }
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

public sealed class ProjectCharacterStateService
{
    private readonly IProjectRepository _projects;
    private readonly IVisualLibraryRepository _library;
    private readonly IProjectCharacterStateRepository _states;
    private readonly TimeProvider _timeProvider;

    public ProjectCharacterStateService(
        IProjectRepository projects,
        IVisualLibraryRepository library,
        IProjectCharacterStateRepository states,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _library = library;
        _states = states;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<ProjectCharacterState>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        _states.ListAsync(projectId, cancellationToken);

    public async Task<ProjectCharacterState> SaveAsync(
        Guid projectId,
        Guid characterId,
        Guid? outfitId,
        CharacterContinuityLocks locks,
        IReadOnlyDictionary<string, double> stateValues,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        if (!project.References.Any(reference =>
                reference.Kind == ProjectReferenceKind.Character && reference.ReferenceId == characterId))
        {
            throw new InvalidOperationException("Character must be referenced by the project before project state can be configured.");
        }

        var character = await _library.GetAsync(characterId, cancellationToken)
            ?? throw new KeyNotFoundException($"Character '{characterId}' was not found.");
        if (character.Kind != VisualLibraryKind.Character || character.Character is null)
        {
            throw new ArgumentException("Referenced library item is not a character.", nameof(characterId));
        }
        if (outfitId.HasValue && character.Character.Outfits.All(outfit => outfit.Id != outfitId.Value))
        {
            throw new ArgumentException("Selected outfit does not belong to the character.", nameof(outfitId));
        }

        ProjectCharacterState.ValidateStateValues(stateValues);
        var state = new ProjectCharacterState(
            projectId,
            characterId,
            outfitId,
            locks,
            new Dictionary<string, double>(stateValues, StringComparer.OrdinalIgnoreCase),
            GetUtcNow());
        await _states.UpsertAsync(state, cancellationToken);
        return state;
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
