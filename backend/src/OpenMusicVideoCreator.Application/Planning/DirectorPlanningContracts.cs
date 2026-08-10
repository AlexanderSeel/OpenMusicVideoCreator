using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Planning;

namespace OpenMusicVideoCreator.Application.Planning;

public sealed record PlanningMusicalSection(
    Guid Id,
    string Label,
    string Kind,
    double StartSeconds,
    double EndSeconds,
    double Confidence);

public sealed record PlanningPhrase(
    int Number,
    double StartSeconds,
    double EndSeconds,
    double Confidence);

public sealed record PlanningReference(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    string ContinuityContext);

public sealed record DirectorPlanningInput(
    Guid ProjectId,
    Guid SongAnalysisId,
    double DurationSeconds,
    double? Bpm,
    string Lyrics,
    string Storyline,
    string Meaning,
    string VisualDirection,
    string Mood,
    string Genre,
    DirectorControls Controls,
    IReadOnlyList<PlanningMusicalSection> Sections,
    IReadOnlyList<PlanningPhrase> Phrases,
    IReadOnlyList<PlanningReference> Characters,
    IReadOnlyList<PlanningReference> Styles,
    IReadOnlyList<PlanningReference> Locations);

public sealed record PlannedVisualArcPoint(
    double TimeSeconds,
    string Label,
    string Description,
    double EmotionalIntensity,
    double VisualIntensity,
    double CameraEnergy);

public sealed record PlannedScene(
    double StartSeconds,
    double EndSeconds,
    string Title,
    string Intent,
    string Action,
    string Environment,
    string Camera,
    string TransitionIn,
    IReadOnlyList<Guid> CharacterIds,
    IReadOnlyList<Guid> StyleIds,
    IReadOnlyList<Guid> LocationIds,
    StoryboardSceneDetails? Details = null);

public sealed record DirectorPlanningCandidate(
    string Summary,
    IReadOnlyList<PlannedVisualArcPoint> VisualArc,
    IReadOnlyList<PlannedScene> Scenes);

public interface IDirectorPlanningProvider
{
    string ProviderId { get; }
    Task<ProviderResult<DirectorPlanningCandidate>> PlanAsync(
        DirectorPlanningInput input,
        CancellationToken cancellationToken = default);
}

public interface IVisualArcRepository
{
    Task<VisualArcVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VisualArcVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task UpsertAsync(VisualArcVersion visualArc, CancellationToken cancellationToken = default);
}

public interface IStoryboardRepository
{
    Task<StoryboardVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryboardVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task UpsertAsync(StoryboardVersion storyboard, CancellationToken cancellationToken = default);
}

public interface IPromptHistoryRepository
{
    Task<IReadOnlyList<PromptVersion>> ListBySceneAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task<PromptVersion?> GetLatestBySceneAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task UpsertAsync(PromptVersion prompt, CancellationToken cancellationToken = default);
}

public sealed record DirectorPlanningResult(
    VisualArcVersion VisualArc,
    StoryboardVersion Storyboard,
    IReadOnlyList<PromptVersion> InitialPrompts);
