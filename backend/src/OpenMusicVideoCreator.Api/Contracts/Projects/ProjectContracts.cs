using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Api.Contracts.Projects;

public sealed record ProjectReferenceRequest(ProjectReferenceKind Kind, Guid ReferenceId);

public sealed record ProjectUpsertRequest(
    string Title,
    string Artist,
    string Lyrics,
    string Storyline,
    string Meaning,
    string VisualDirection,
    string Mood,
    string Genre,
    ProjectAspectRatio AspectRatio,
    int ResolutionWidth,
    int ResolutionHeight,
    IReadOnlyList<string>? TargetPlatforms,
    GenerationPreset Preset,
    decimal? EstimatedBudget,
    decimal? MaximumBudget,
    IReadOnlyList<ProjectReferenceRequest>? References)
{
    public ProjectDraft ToDraft() => new(
        Title,
        Artist,
        Lyrics,
        Storyline,
        Meaning,
        VisualDirection,
        Mood,
        Genre,
        AspectRatio,
        new OutputResolution(ResolutionWidth, ResolutionHeight),
        TargetPlatforms?.ToArray() ?? [],
        Preset,
        EstimatedBudget,
        MaximumBudget,
        References?.Select(reference => new ProjectReference(reference.Kind, reference.ReferenceId)).ToArray() ?? []);
}

public sealed record ProjectReferenceResponse(ProjectReferenceKind Kind, Guid ReferenceId);

public sealed record ProjectResponse(
    Guid Id,
    string Title,
    string Artist,
    string Lyrics,
    string Storyline,
    string Meaning,
    string VisualDirection,
    string Mood,
    string Genre,
    ProjectAspectRatio AspectRatio,
    int ResolutionWidth,
    int ResolutionHeight,
    IReadOnlyList<string> TargetPlatforms,
    GenerationPreset Preset,
    decimal? EstimatedBudget,
    decimal? MaximumBudget,
    IReadOnlyList<ProjectReferenceResponse> References,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static ProjectResponse FromDomain(MusicVideoProject project) => new(
        project.Id,
        project.Title,
        project.Artist,
        project.Lyrics,
        project.Storyline,
        project.Meaning,
        project.VisualDirection,
        project.Mood,
        project.Genre,
        project.AspectRatio,
        project.Resolution.Width,
        project.Resolution.Height,
        project.TargetPlatforms,
        project.Preset,
        project.EstimatedBudget,
        project.MaximumBudget,
        project.References
            .Select(reference => new ProjectReferenceResponse(reference.Kind, reference.ReferenceId))
            .ToArray(),
        project.CreatedUtc,
        project.UpdatedUtc);
}
