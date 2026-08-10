namespace OpenMusicVideoCreator.Domain.Planning;

public sealed record DirectorControls(
    double LiteralToSymbolic,
    double NarrativeStrength,
    double Abstraction,
    double Emotion,
    double Darkness,
    double Surrealism,
    double Complexity,
    double ActingIntensity,
    double CameraEnergy)
{
    public static DirectorControls Balanced { get; } = new(0.55, 0.65, 0.45, 0.7, 0.4, 0.4, 0.55, 0.55, 0.55);

    public void Validate()
    {
        foreach (var value in new[]
        {
            LiteralToSymbolic, NarrativeStrength, Abstraction, Emotion, Darkness,
            Surrealism, Complexity, ActingIntensity, CameraEnergy,
        })
        {
            if (!double.IsFinite(value) || value is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(DirectorControls), "Director controls must be normalized from 0 to 1.");
            }
        }
    }
}

public sealed record VisualArcPoint(
    Guid Id,
    double TimeSeconds,
    string Label,
    string Description,
    double EmotionalIntensity,
    double VisualIntensity,
    double CameraEnergy)
{
    public void Validate(double durationSeconds)
    {
        if (Id == Guid.Empty || TimeSeconds < 0 || TimeSeconds > durationSeconds ||
            string.IsNullOrWhiteSpace(Label) ||
            !IsNormalized(EmotionalIntensity) || !IsNormalized(VisualIntensity) || !IsNormalized(CameraEnergy))
        {
            throw new ArgumentException("Visual Arc point contains invalid data.");
        }
    }

    private static bool IsNormalized(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}

public sealed record VisualArcVersion(
    Guid Id,
    Guid ProjectId,
    Guid SongAnalysisId,
    int Version,
    string Summary,
    DirectorControls Controls,
    IReadOnlyList<VisualArcPoint> Points,
    DateTimeOffset CreatedUtc)
{
    public void Validate(double durationSeconds)
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || SongAnalysisId == Guid.Empty || Version <= 0 || durationSeconds <= 0)
        {
            throw new ArgumentException("Visual Arc identity/version is invalid.");
        }
        Controls.Validate();
        if (Points.Count < 2)
        {
            throw new ArgumentException("Visual Arc requires at least two points.");
        }
        foreach (var point in Points) point.Validate(durationSeconds);
        if (!Points.OrderBy(point => point.TimeSeconds).SequenceEqual(Points))
        {
            throw new ArgumentException("Visual Arc points must be time ordered.");
        }
    }
}

public sealed record StoryboardSceneDetails(
    string SongSection,
    string AssociatedLyric,
    string Purpose,
    string Emotion,
    string Composition,
    string Lighting,
    string EnvironmentMotion,
    string VisualSymbolism,
    string ContinuityRequirements)
{
    public static StoryboardSceneDetails Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    public void Validate()
    {
        if (new[]
            {
                SongSection, AssociatedLyric, Purpose, Emotion, Composition,
                Lighting, EnvironmentMotion, VisualSymbolism, ContinuityRequirements,
            }
            .Any(value => value is null))
        {
            throw new ArgumentException("Storyboard scene details cannot contain null text values.");
        }
    }
}

public sealed record StoryboardScene(
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
    Guid? SelectedPromptVersionId,
    StoryboardSceneDetails? Details = null)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
    public StoryboardSceneDetails ResolveDetails() => Details ?? StoryboardSceneDetails.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public StoryboardSceneDetails EffectiveDetails => ResolveDetails();
}

public sealed record StoryboardVersion(
    Guid Id,
    Guid ProjectId,
    Guid SongAnalysisId,
    Guid VisualArcId,
    int Version,
    IReadOnlyList<StoryboardScene> Scenes,
    DateTimeOffset CreatedUtc)
{
    public void Validate(double songDurationSeconds)
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || SongAnalysisId == Guid.Empty || VisualArcId == Guid.Empty || Version <= 0)
        {
            throw new ArgumentException("Storyboard identity/version is invalid.");
        }
        ValidateScenes(songDurationSeconds, Scenes);
    }

    public static void ValidateScenes(double songDurationSeconds, IReadOnlyList<StoryboardScene> scenes)
    {
        if (songDurationSeconds <= 0 || scenes.Count == 0)
        {
            throw new ArgumentException("Storyboard requires a positive song duration and at least one scene.");
        }

        var ordered = scenes.OrderBy(scene => scene.Sequence).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var scene = ordered[index];
            if (scene.Id == Guid.Empty || scene.Sequence != index + 1 || string.IsNullOrWhiteSpace(scene.Title) ||
                string.IsNullOrWhiteSpace(scene.DirectorIntent) || scene.StartSeconds < 0 ||
                scene.EndSeconds <= scene.StartSeconds || scene.EndSeconds > songDurationSeconds + 0.001)
            {
                throw new ArgumentException($"Storyboard scene {index + 1} contains invalid data.");
            }
            if (index > 0 && scene.StartSeconds < ordered[index - 1].EndSeconds - 0.001)
            {
                throw new ArgumentException("Storyboard scenes cannot overlap.");
            }
            scene.Details?.Validate();
        }
    }
}

public sealed record PromptVersion(
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
    public void Validate()
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || SceneId == Guid.Empty || StoryboardVersionId == Guid.Empty ||
            Version <= 0 || TemplateVersion <= 0 || string.IsNullOrWhiteSpace(TemplateName) ||
            string.IsNullOrWhiteSpace(DirectorIntent) || string.IsNullOrWhiteSpace(FinalProviderPrompt))
        {
            throw new ArgumentException("Prompt version contains invalid data.");
        }
    }
}

public sealed record PromptTemplate(
    string Name,
    int Version,
    string Template)
{
    public static PromptTemplate StoryboardSceneV1 { get; } = new(
        "storyboard-scene",
        1,
        "Intent: {intent}\nAction: {action}\nEnvironment: {environment}\nCamera: {camera}\nContinuity: {continuity}");

    public static PromptTemplate StoryboardSceneV2 { get; } = new(
        "storyboard-scene",
        2,
        "Intent: {intent}\nPurpose: {purpose}\nSong section: {songSection}\nAssociated lyric: {lyric}\nAction: {action}\nEmotion: {emotion}\nComposition: {composition}\nCamera: {camera}\nLighting: {lighting}\nEnvironment: {environment}\nEnvironment motion: {environmentMotion}\nVisual symbolism: {visualSymbolism}\nContinuity requirements: {continuityRequirements}\nReference continuity: {continuity}");
}
