using OpenMusicVideoCreator.Domain.Planning;

namespace OpenMusicVideoCreator.Api.Contracts.Planning;

public sealed record DirectorPlanRequest(DirectorControls Controls);

public sealed record VisualArcPointRequest(
    Guid? Id,
    double TimeSeconds,
    string Label,
    string Description,
    double EmotionalIntensity,
    double VisualIntensity,
    double CameraEnergy);

public sealed record VisualArcUpdateRequest(
    string Summary,
    DirectorControls Controls,
    IReadOnlyList<VisualArcPointRequest>? Points);

public sealed record SceneUpdateRequest(
    double StartSeconds,
    double EndSeconds,
    string Title,
    string DirectorIntent,
    string Action,
    string Environment,
    string Camera,
    string TransitionIn,
    IReadOnlyList<Guid>? CharacterIds,
    IReadOnlyList<Guid>? StyleIds,
    IReadOnlyList<Guid>? LocationIds);

public sealed record SceneReorderRequest(IReadOnlyList<Guid>? SceneIds);
public sealed record PromptRegenerateRequest(string? Notes);

public sealed record VisualArcPointResponse(
    Guid Id,
    double TimeSeconds,
    string Label,
    string Description,
    double EmotionalIntensity,
    double VisualIntensity,
    double CameraEnergy)
{
    public static VisualArcPointResponse FromDomain(VisualArcPoint point) => new(
        point.Id, point.TimeSeconds, point.Label, point.Description,
        point.EmotionalIntensity, point.VisualIntensity, point.CameraEnergy);
}

public sealed record VisualArcResponse(
    Guid Id,
    Guid ProjectId,
    Guid SongAnalysisId,
    int Version,
    string Summary,
    DirectorControls Controls,
    IReadOnlyList<VisualArcPointResponse> Points,
    DateTimeOffset CreatedUtc)
{
    public static VisualArcResponse FromDomain(VisualArcVersion arc) => new(
        arc.Id, arc.ProjectId, arc.SongAnalysisId, arc.Version, arc.Summary, arc.Controls,
        arc.Points.Select(VisualArcPointResponse.FromDomain).ToArray(), arc.CreatedUtc);
}

public sealed record StoryboardSceneResponse(
    Guid Id,
    int Sequence,
    double StartSeconds,
    double EndSeconds,
    string Title,
    string DirectorIntent,
    string Action,
    string Environment,
    string Camera,
    string TransitionIn,
    IReadOnlyList<Guid> CharacterIds,
    IReadOnlyList<Guid> StyleIds,
    IReadOnlyList<Guid> LocationIds,
    Guid? SelectedPromptVersionId)
{
    public static StoryboardSceneResponse FromDomain(StoryboardScene scene) => new(
        scene.Id, scene.Sequence, scene.StartSeconds, scene.EndSeconds, scene.Title,
        scene.DirectorIntent, scene.Action, scene.Environment, scene.Camera, scene.TransitionIn,
        scene.CharacterIds, scene.StyleIds, scene.LocationIds, scene.SelectedPromptVersionId);
}

public sealed record StoryboardResponse(
    Guid Id,
    Guid ProjectId,
    Guid SongAnalysisId,
    Guid VisualArcId,
    int Version,
    IReadOnlyList<StoryboardSceneResponse> Scenes,
    DateTimeOffset CreatedUtc)
{
    public static StoryboardResponse FromDomain(StoryboardVersion storyboard) => new(
        storyboard.Id, storyboard.ProjectId, storyboard.SongAnalysisId, storyboard.VisualArcId,
        storyboard.Version, storyboard.Scenes.Select(StoryboardSceneResponse.FromDomain).ToArray(), storyboard.CreatedUtc);
}

public sealed record PromptVersionResponse(
    Guid Id,
    Guid ProjectId,
    Guid SceneId,
    Guid StoryboardVersionId,
    int Version,
    string TemplateName,
    int TemplateVersion,
    string DirectorIntent,
    string FinalProviderPrompt,
    DateTimeOffset CreatedUtc)
{
    public static PromptVersionResponse FromDomain(PromptVersion prompt) => new(
        prompt.Id, prompt.ProjectId, prompt.SceneId, prompt.StoryboardVersionId, prompt.Version,
        prompt.TemplateName, prompt.TemplateVersion, prompt.DirectorIntent, prompt.FinalProviderPrompt, prompt.CreatedUtc);
}

public sealed record DirectorPlanResponse(
    VisualArcResponse VisualArc,
    StoryboardResponse Storyboard,
    IReadOnlyList<PromptVersionResponse> InitialPrompts);

public sealed record PromptRegenerateResponse(
    StoryboardResponse Storyboard,
    PromptVersionResponse Prompt);
