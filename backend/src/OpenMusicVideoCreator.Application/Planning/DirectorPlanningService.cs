using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Application.Library;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Library;
using OpenMusicVideoCreator.Domain.Planning;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Planning;

public sealed class DirectorPlanningService
{
    private readonly IProjectRepository _projects;
    private readonly ISongAnalysisRepository _analyses;
    private readonly IVisualLibraryRepository _library;
    private readonly IProjectCharacterStateRepository _characterStates;
    private readonly IVisualArcRepository _visualArcs;
    private readonly IStoryboardRepository _storyboards;
    private readonly IPromptHistoryRepository _prompts;
    private readonly IDirectorPlanningProvider _planner;
    private readonly TimeProvider _timeProvider;

    public DirectorPlanningService(
        IProjectRepository projects,
        ISongAnalysisRepository analyses,
        IVisualLibraryRepository library,
        IProjectCharacterStateRepository characterStates,
        IVisualArcRepository visualArcs,
        IStoryboardRepository storyboards,
        IPromptHistoryRepository prompts,
        IDirectorPlanningProvider planner,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _analyses = analyses;
        _library = library;
        _characterStates = characterStates;
        _visualArcs = visualArcs;
        _storyboards = storyboards;
        _prompts = prompts;
        _planner = planner;
        _timeProvider = timeProvider;
    }

    public Task<VisualArcVersion?> GetLatestVisualArcAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _visualArcs.GetLatestAsync(projectId, cancellationToken);

    public Task<IReadOnlyList<VisualArcVersion>> ListVisualArcVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _visualArcs.ListVersionsAsync(projectId, cancellationToken);

    public Task<StoryboardVersion?> GetLatestStoryboardAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _storyboards.GetLatestAsync(projectId, cancellationToken);

    public Task<IReadOnlyList<StoryboardVersion>> ListStoryboardVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _storyboards.ListVersionsAsync(projectId, cancellationToken);

    public Task<IReadOnlyList<PromptVersion>> ListPromptHistoryAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default) =>
        _prompts.ListBySceneAsync(projectId, sceneId, cancellationToken);

    public async Task<DirectorPlanningResult> PlanAsync(
        Guid projectId,
        DirectorControls controls,
        CancellationToken cancellationToken = default)
    {
        controls.Validate();
        var context = await BuildContextAsync(projectId, controls, cancellationToken);
        var providerResult = await _planner.PlanAsync(context, cancellationToken);
        if (!providerResult.IsSuccess || providerResult.Value is null)
        {
            var failure = providerResult.Failure;
            throw new DirectorPlanningException(
                failure?.Message ?? "Director planner returned no plan.",
                failure?.Code,
                failure?.Retryable ?? false);
        }

        ValidateCandidate(context, providerResult.Value);
        var now = GetUtcNow();
        var latestArc = await _visualArcs.GetLatestAsync(projectId, cancellationToken);
        var visualArc = new VisualArcVersion(
            Guid.NewGuid(),
            projectId,
            context.SongAnalysisId,
            (latestArc?.Version ?? 0) + 1,
            providerResult.Value.Summary.Trim(),
            controls,
            providerResult.Value.VisualArc.Select(point => new VisualArcPoint(
                Guid.NewGuid(),
                point.TimeSeconds,
                point.Label.Trim(),
                point.Description.Trim(),
                point.EmotionalIntensity,
                point.VisualIntensity,
                point.CameraEnergy)).ToArray(),
            now);
        visualArc.Validate(context.DurationSeconds);

        var latestStoryboard = await _storyboards.GetLatestAsync(projectId, cancellationToken);
        var storyboardId = Guid.NewGuid();
        var promptTemplate = PromptTemplate.StoryboardSceneV1;
        var prompts = new List<PromptVersion>(providerResult.Value.Scenes.Count);
        var scenes = new List<StoryboardScene>(providerResult.Value.Scenes.Count);

        for (var index = 0; index < providerResult.Value.Scenes.Count; index++)
        {
            var candidate = providerResult.Value.Scenes[index];
            var sceneId = Guid.NewGuid();
            var scene = new StoryboardScene(
                sceneId,
                index + 1,
                candidate.StartSeconds,
                candidate.EndSeconds,
                candidate.Title.Trim(),
                candidate.Intent.Trim(),
                candidate.Action.Trim(),
                candidate.Environment.Trim(),
                candidate.Camera.Trim(),
                candidate.TransitionIn.Trim(),
                candidate.CharacterIds.Distinct().ToArray(),
                candidate.StyleIds.Distinct().ToArray(),
                candidate.LocationIds.Distinct().ToArray(),
                null);
            var prompt = CreatePromptVersion(context, scene, storyboardId, 1, null, now);
            prompts.Add(prompt);
            scenes.Add(scene with { SelectedPromptVersionId = prompt.Id });
        }

        var storyboard = new StoryboardVersion(
            storyboardId,
            projectId,
            context.SongAnalysisId,
            visualArc.Id,
            (latestStoryboard?.Version ?? 0) + 1,
            scenes,
            now);
        storyboard.Validate(context.DurationSeconds);

        await _visualArcs.UpsertAsync(visualArc, cancellationToken);
        await _storyboards.UpsertAsync(storyboard, cancellationToken);
        foreach (var prompt in prompts)
        {
            await _prompts.UpsertAsync(prompt, cancellationToken);
        }

        return new DirectorPlanningResult(visualArc, storyboard, prompts);
    }

    public async Task<VisualArcVersion> SaveVisualArcAsync(
        Guid projectId,
        string summary,
        DirectorControls controls,
        IReadOnlyList<VisualArcPoint> points,
        CancellationToken cancellationToken = default)
    {
        var analysis = await RequireLatestAnalysisAsync(projectId, cancellationToken);
        var latest = await _visualArcs.GetLatestAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' has no Visual Arc.");
        var version = new VisualArcVersion(
            Guid.NewGuid(),
            projectId,
            analysis.Id,
            latest.Version + 1,
            summary.Trim(),
            controls,
            points.Select(point => point with { Id = point.Id == Guid.Empty ? Guid.NewGuid() : point.Id }).ToArray(),
            GetUtcNow());
        version.Validate(analysis.DurationSeconds);
        await _visualArcs.UpsertAsync(version, cancellationToken);
        return version;
    }

    public async Task<StoryboardVersion> UpdateSceneAsync(
        Guid projectId,
        Guid sceneId,
        StoryboardScene edited,
        CancellationToken cancellationToken = default)
    {
        var analysis = await RequireLatestAnalysisAsync(projectId, cancellationToken);
        var latest = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' has no storyboard.");
        var existing = latest.Scenes.SingleOrDefault(scene => scene.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene '{sceneId}' was not found.");
        if (edited.Id != sceneId || edited.Sequence != existing.Sequence)
        {
            throw new ArgumentException("Scene identity and sequence cannot be changed through scene editing.", nameof(edited));
        }
        await ValidateSceneReferencesAsync(projectId, edited, cancellationToken);

        var newStoryboardId = Guid.NewGuid();
        var promptVersionNumber = (await _prompts.GetLatestBySceneAsync(projectId, sceneId, cancellationToken))?.Version + 1 ?? 1;
        var context = await BuildContextAsync(projectId, DirectorControls.Balanced, cancellationToken);
        var prompt = CreatePromptVersion(context, edited, newStoryboardId, promptVersionNumber, null, GetUtcNow());
        var replaced = edited with { SelectedPromptVersionId = prompt.Id };
        var scenes = latest.Scenes.Select(scene => scene.Id == sceneId ? replaced : scene).ToArray();
        StoryboardVersion.ValidateScenes(analysis.DurationSeconds, scenes);

        var version = new StoryboardVersion(
            newStoryboardId,
            projectId,
            analysis.Id,
            latest.VisualArcId,
            latest.Version + 1,
            scenes,
            GetUtcNow());
        await _storyboards.UpsertAsync(version, cancellationToken);
        await _prompts.UpsertAsync(prompt, cancellationToken);
        return version;
    }

    public async Task<StoryboardVersion> ReorderScenesAsync(
        Guid projectId,
        IReadOnlyList<Guid> orderedSceneIds,
        CancellationToken cancellationToken = default)
    {
        var analysis = await RequireLatestAnalysisAsync(projectId, cancellationToken);
        var latest = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' has no storyboard.");
        if (orderedSceneIds.Count != latest.Scenes.Count ||
            orderedSceneIds.Distinct().Count() != latest.Scenes.Count ||
            orderedSceneIds.Any(id => latest.Scenes.All(scene => scene.Id != id)))
        {
            throw new ArgumentException("Scene order must contain every current scene exactly once.", nameof(orderedSceneIds));
        }

        var slots = latest.Scenes.OrderBy(scene => scene.Sequence).Select(scene => (scene.StartSeconds, scene.EndSeconds)).ToArray();
        var byId = latest.Scenes.ToDictionary(scene => scene.Id);
        var scenes = orderedSceneIds.Select((id, index) => byId[id] with
        {
            Sequence = index + 1,
            StartSeconds = slots[index].StartSeconds,
            EndSeconds = slots[index].EndSeconds,
        }).ToArray();
        StoryboardVersion.ValidateScenes(analysis.DurationSeconds, scenes);

        var version = new StoryboardVersion(
            Guid.NewGuid(),
            projectId,
            analysis.Id,
            latest.VisualArcId,
            latest.Version + 1,
            scenes,
            GetUtcNow());
        await _storyboards.UpsertAsync(version, cancellationToken);
        return version;
    }

    public async Task<(StoryboardVersion Storyboard, PromptVersion Prompt)> RegeneratePromptAsync(
        Guid projectId,
        Guid sceneId,
        string? userNotes,
        CancellationToken cancellationToken = default)
    {
        var latest = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' has no storyboard.");
        var scene = latest.Scenes.SingleOrDefault(candidate => candidate.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene '{sceneId}' was not found.");
        var analysis = await RequireLatestAnalysisAsync(projectId, cancellationToken);
        var context = await BuildContextAsync(projectId, DirectorControls.Balanced, cancellationToken);
        var promptNumber = (await _prompts.GetLatestBySceneAsync(projectId, sceneId, cancellationToken))?.Version + 1 ?? 1;
        var storyboardId = Guid.NewGuid();
        var prompt = CreatePromptVersion(context, scene, storyboardId, promptNumber, userNotes, GetUtcNow());
        var scenes = latest.Scenes.Select(candidate => candidate.Id == sceneId
            ? candidate with { SelectedPromptVersionId = prompt.Id }
            : candidate).ToArray();
        var storyboard = new StoryboardVersion(
            storyboardId,
            projectId,
            analysis.Id,
            latest.VisualArcId,
            latest.Version + 1,
            scenes,
            GetUtcNow());
        storyboard.Validate(analysis.DurationSeconds);
        await _storyboards.UpsertAsync(storyboard, cancellationToken);
        await _prompts.UpsertAsync(prompt, cancellationToken);
        return (storyboard, prompt);
    }

    private async Task<DirectorPlanningInput> BuildContextAsync(
        Guid projectId,
        DirectorControls controls,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var analysis = await RequireLatestAnalysisAsync(projectId, cancellationToken);
        var library = await _library.ListAsync(cancellationToken);
        var characterStates = await _characterStates.ListAsync(projectId, cancellationToken);

        var characters = ResolveReferences(project, library, ProjectReferenceKind.Character)
            .Select(item => new PlanningReference(
                item.Id,
                item.Name,
                item.Description,
                item.Tags,
                BuildCharacterContinuity(item, characterStates.FirstOrDefault(state => state.CharacterId == item.Id))))
            .ToArray();
        var styles = ResolveReferences(project, library, ProjectReferenceKind.Style)
            .Select(item => new PlanningReference(
                item.Id,
                item.Name,
                item.Description,
                item.Tags,
                item.Style is null
                    ? string.Empty
                    : $"Prompt: {item.Style.Prompt}; camera: {item.Style.CameraCharacteristics}; lighting: {item.Style.LightingCharacteristics}; animation: {item.Style.AnimationCharacteristics}"))
            .ToArray();
        var locations = ResolveReferences(project, library, ProjectReferenceKind.Location)
            .Select(item => new PlanningReference(
                item.Id,
                item.Name,
                item.Description,
                item.Tags,
                item.Location is null
                    ? string.Empty
                    : $"Environment: {item.Location.EnvironmentDescription}; constraints: {string.Join(", ", item.Location.Constraints)}; lighting: {item.Location.Lighting}; weather: {item.Location.Weather}; time: {item.Location.TimeOfDay}"))
            .ToArray();

        return new DirectorPlanningInput(
            project.Id,
            analysis.Id,
            analysis.DurationSeconds,
            analysis.Bpm,
            project.Lyrics,
            project.Storyline,
            project.Meaning,
            project.VisualDirection,
            project.Mood,
            project.Genre,
            controls,
            analysis.Sections.Select(section => new PlanningMusicalSection(
                section.Id,
                section.Label,
                section.Kind.ToString(),
                section.StartSeconds,
                section.EndSeconds,
                section.Confidence)).ToArray(),
            analysis.Phrases.Select(phrase => new PlanningPhrase(
                phrase.Number,
                phrase.StartSeconds,
                phrase.EndSeconds,
                phrase.Confidence)).ToArray(),
            characters,
            styles,
            locations);
    }

    private async Task<SongAnalysis> RequireLatestAnalysisAsync(Guid projectId, CancellationToken cancellationToken) =>
        await _analyses.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Analyze the song before Director planning.");

    private static IReadOnlyList<VisualLibraryItem> ResolveReferences(
        MusicVideoProject project,
        IReadOnlyList<VisualLibraryItem> library,
        ProjectReferenceKind kind)
    {
        var ids = project.References.Where(reference => reference.Kind == kind).Select(reference => reference.ReferenceId).ToHashSet();
        return library.Where(item => ids.Contains(item.Id)).ToArray();
    }

    private static string BuildCharacterContinuity(VisualLibraryItem item, ProjectCharacterState? state)
    {
        if (item.Character is null) return string.Empty;
        var outfit = state?.OutfitId is Guid outfitId
            ? item.Character.Outfits.FirstOrDefault(candidate => candidate.Id == outfitId)?.Name
            : null;
        var locks = state?.Locks ?? item.Character.DefaultLocks;
        var values = state is null ? string.Empty : string.Join(", ", state.StateValues.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value:0.##}"));
        return $"Appearance: {item.Character.AppearanceDescription}; forbidden: {string.Join(", ", item.Character.ForbiddenChanges)}; outfit: {outfit ?? "default"}; locks identity={locks.Identity}, face={locks.Face}, hair={locks.Hair}, body={locks.Body}, age={locks.Age}, wardrobe={locks.Wardrobe}; state: {values}";
    }

    private static void ValidateCandidate(DirectorPlanningInput input, DirectorPlanningCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Summary) || candidate.VisualArc.Count < 2 || candidate.Scenes.Count == 0 || candidate.Scenes.Count > 100)
        {
            throw new InvalidDataException("Director returned an incomplete structured plan.");
        }
        foreach (var point in candidate.VisualArc)
        {
            new VisualArcPoint(Guid.NewGuid(), point.TimeSeconds, point.Label, point.Description, point.EmotionalIntensity, point.VisualIntensity, point.CameraEnergy)
                .Validate(input.DurationSeconds);
        }

        var allowedCharacters = input.Characters.Select(reference => reference.Id).ToHashSet();
        var allowedStyles = input.Styles.Select(reference => reference.Id).ToHashSet();
        var allowedLocations = input.Locations.Select(reference => reference.Id).ToHashSet();
        var scenes = candidate.Scenes.Select((scene, index) => new StoryboardScene(
            Guid.NewGuid(),
            index + 1,
            scene.StartSeconds,
            scene.EndSeconds,
            scene.Title,
            scene.Intent,
            scene.Action,
            scene.Environment,
            scene.Camera,
            scene.TransitionIn,
            scene.CharacterIds,
            scene.StyleIds,
            scene.LocationIds,
            null)).ToArray();
        StoryboardVersion.ValidateScenes(input.DurationSeconds, scenes);
        if (Math.Abs(scenes[0].StartSeconds) > 0.05 || Math.Abs(scenes[^1].EndSeconds - input.DurationSeconds) > 0.05)
        {
            throw new InvalidDataException("Director storyboard must cover the full song.");
        }
        for (var index = 1; index < scenes.Length; index++)
        {
            if (Math.Abs(scenes[index].StartSeconds - scenes[index - 1].EndSeconds) > 0.05)
            {
                throw new InvalidDataException("Director storyboard must not contain uncovered timing gaps.");
            }
        }
        if (scenes.Any(scene => scene.CharacterIds.Any(id => !allowedCharacters.Contains(id)) ||
                                scene.StyleIds.Any(id => !allowedStyles.Contains(id)) ||
                                scene.LocationIds.Any(id => !allowedLocations.Contains(id))))
        {
            throw new InvalidDataException("Director referenced a visual-library item that is not attached to the project.");
        }
    }

    private async Task ValidateSceneReferencesAsync(Guid projectId, StoryboardScene scene, CancellationToken cancellationToken)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        ValidateIds(scene.CharacterIds, ProjectReferenceKind.Character);
        ValidateIds(scene.StyleIds, ProjectReferenceKind.Style);
        ValidateIds(scene.LocationIds, ProjectReferenceKind.Location);
        return;

        void ValidateIds(IReadOnlyList<Guid> ids, ProjectReferenceKind kind)
        {
            var allowed = project.References.Where(reference => reference.Kind == kind).Select(reference => reference.ReferenceId).ToHashSet();
            if (ids.Any(id => !allowed.Contains(id)))
            {
                throw new ArgumentException($"Scene contains a {kind} reference not attached to the project.");
            }
        }
    }

    private static PromptVersion CreatePromptVersion(
        DirectorPlanningInput context,
        StoryboardScene scene,
        Guid storyboardVersionId,
        int version,
        string? userNotes,
        DateTimeOffset now)
    {
        var template = PromptTemplate.StoryboardSceneV1;
        var continuity = string.Join(" | ", context.Characters
            .Where(reference => scene.CharacterIds.Contains(reference.Id))
            .Select(reference => $"{reference.Name}: {reference.ContinuityContext}")
            .Concat(context.Styles.Where(reference => scene.StyleIds.Contains(reference.Id)).Select(reference => $"Style {reference.Name}: {reference.ContinuityContext}"))
            .Concat(context.Locations.Where(reference => scene.LocationIds.Contains(reference.Id)).Select(reference => $"Location {reference.Name}: {reference.ContinuityContext}")));
        var prompt = template.Template
            .Replace("{intent}", scene.DirectorIntent, StringComparison.Ordinal)
            .Replace("{action}", scene.Action, StringComparison.Ordinal)
            .Replace("{environment}", scene.Environment, StringComparison.Ordinal)
            .Replace("{camera}", scene.Camera, StringComparison.Ordinal)
            .Replace("{continuity}", continuity, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            prompt += $"\nUser refinement: {userNotes.Trim()}";
        }

        var result = new PromptVersion(
            Guid.NewGuid(),
            context.ProjectId,
            scene.Id,
            storyboardVersionId,
            version,
            template.Name,
            template.Version,
            scene.DirectorIntent,
            prompt,
            now);
        result.Validate();
        return result;
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

public sealed class DirectorPlanningException : Exception
{
    public DirectorPlanningException(string message, ProviderFailureCode? failureCode, bool retryable)
        : base(message)
    {
        FailureCode = failureCode;
        Retryable = retryable;
    }

    public ProviderFailureCode? FailureCode { get; }
    public bool Retryable { get; }
}
