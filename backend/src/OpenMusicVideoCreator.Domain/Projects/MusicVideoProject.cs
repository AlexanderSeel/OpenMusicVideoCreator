namespace OpenMusicVideoCreator.Domain.Projects;

public enum ProjectAspectRatio
{
    Landscape16x9,
    Portrait9x16,
    Square1x1,
}

public enum GenerationPreset
{
    Fast,
    Balanced,
    BestQuality,
    Cheapest,
    Custom,
}

public enum ProjectReferenceKind
{
    Character,
    Style,
    Location,
    AdditionalMedia,
}

public sealed record OutputResolution(int Width, int Height)
{
    public static readonly OutputResolution FullHd = new(1920, 1080);
}

public sealed record ProjectReference(ProjectReferenceKind Kind, Guid ReferenceId);

public sealed record MusicVideoProject(
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
    OutputResolution Resolution,
    IReadOnlyList<string> TargetPlatforms,
    GenerationPreset Preset,
    decimal? EstimatedBudget,
    decimal? MaximumBudget,
    IReadOnlyList<ProjectReference> References,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static MusicVideoProject Create(
        Guid id,
        ProjectDraft draft,
        DateTimeOffset nowUtc)
    {
        Validate(draft);

        return new MusicVideoProject(
            id,
            draft.Title.Trim(),
            draft.Artist.Trim(),
            draft.Lyrics,
            draft.Storyline,
            draft.Meaning,
            draft.VisualDirection,
            draft.Mood.Trim(),
            draft.Genre.Trim(),
            draft.AspectRatio,
            draft.Resolution,
            NormalizeTargets(draft.TargetPlatforms),
            draft.Preset,
            draft.EstimatedBudget,
            draft.MaximumBudget,
            draft.References.Distinct().ToArray(),
            nowUtc,
            nowUtc);
    }

    public MusicVideoProject Update(ProjectDraft draft, DateTimeOffset nowUtc)
    {
        Validate(draft);

        return this with
        {
            Title = draft.Title.Trim(),
            Artist = draft.Artist.Trim(),
            Lyrics = draft.Lyrics,
            Storyline = draft.Storyline,
            Meaning = draft.Meaning,
            VisualDirection = draft.VisualDirection,
            Mood = draft.Mood.Trim(),
            Genre = draft.Genre.Trim(),
            AspectRatio = draft.AspectRatio,
            Resolution = draft.Resolution,
            TargetPlatforms = NormalizeTargets(draft.TargetPlatforms),
            Preset = draft.Preset,
            EstimatedBudget = draft.EstimatedBudget,
            MaximumBudget = draft.MaximumBudget,
            References = draft.References.Distinct().ToArray(),
            UpdatedUtc = nowUtc,
        };
    }

    private static void Validate(ProjectDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ArgumentException("Project title is required.", nameof(draft));
        }

        if (draft.Resolution.Width <= 0 || draft.Resolution.Height <= 0)
        {
            throw new ArgumentException("Project resolution must be positive.", nameof(draft));
        }

        if (draft.EstimatedBudget is < 0 || draft.MaximumBudget is < 0)
        {
            throw new ArgumentException("Project budgets cannot be negative.", nameof(draft));
        }

        if (draft.EstimatedBudget is not null &&
            draft.MaximumBudget is not null &&
            draft.EstimatedBudget > draft.MaximumBudget)
        {
            throw new ArgumentException("Estimated budget cannot exceed maximum budget.", nameof(draft));
        }
    }

    private static IReadOnlyList<string> NormalizeTargets(IEnumerable<string> targets) => targets
        .Where(target => !string.IsNullOrWhiteSpace(target))
        .Select(target => target.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed record ProjectDraft(
    string Title,
    string Artist,
    string Lyrics,
    string Storyline,
    string Meaning,
    string VisualDirection,
    string Mood,
    string Genre,
    ProjectAspectRatio AspectRatio,
    OutputResolution Resolution,
    IReadOnlyList<string> TargetPlatforms,
    GenerationPreset Preset,
    decimal? EstimatedBudget,
    decimal? MaximumBudget,
    IReadOnlyList<ProjectReference> References);
