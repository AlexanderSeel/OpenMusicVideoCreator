using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Planning;

namespace OpenMusicVideoCreator.Infrastructure.Planning;

public sealed class StructuredMockDirectorProvider : IDirectorPlanningProvider
{
    public string ProviderId => "mock-director-structured";

    public Task<ProviderResult<DirectorPlanningCandidate>> PlanAsync(
        DirectorPlanningInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        input.Controls.Validate();
        if (input.DurationSeconds <= 0)
        {
            return Task.FromResult(ProviderResult<DirectorPlanningCandidate>.Failed(new ProviderFailure(
                ProviderFailureCode.InvalidParameters,
                "Song duration must be positive.",
                Retryable: false)));
        }

        var boundaries = BuildSceneBoundaries(input);
        var sceneCount = boundaries.Count - 1;
        var scenes = new List<PlannedScene>(sceneCount);
        for (var index = 0; index < sceneCount; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            var center = (start + end) / 2;
            var section = input.Sections.FirstOrDefault(candidate =>
                center >= candidate.StartSeconds && center < candidate.EndSeconds);
            var location = input.Locations.Count == 0
                ? null
                : input.Locations[index % input.Locations.Count];
            var characters = input.Characters.Count == 0
                ? Array.Empty<Guid>()
                : new[] { input.Characters[index % input.Characters.Count].Id };
            var styles = input.Styles.Select(style => style.Id).Take(2).ToArray();
            var locations = location is null ? Array.Empty<Guid>() : new[] { location.Id };
            var details = BuildSceneDetails(input, section, center, index, sceneCount);

            scenes.Add(new PlannedScene(
                start,
                end,
                section is null ? $"Scene {index + 1}" : $"{section.Label} · {index + 1}",
                BuildIntent(input, section?.Label, index),
                BuildAction(input, index),
                location is null
                    ? BuildFallbackEnvironment(input, index)
                    : $"{location.Name}. {location.Description}".Trim(),
                BuildCamera(input.Controls.CameraEnergy, index),
                IsNearAnchor(start, input) ? "Musical cut on structure boundary" : "Rhythmic cut",
                characters,
                styles,
                locations,
                details));
        }

        var candidate = new DirectorPlanningCandidate(
            BuildSummary(input, scenes.Count),
            BuildVisualArc(input),
            scenes);
        return Task.FromResult(ProviderResult<DirectorPlanningCandidate>.Success(candidate));
    }

    internal static IReadOnlyList<double> BuildSceneBoundaries(DirectorPlanningInput input)
    {
        var duration = input.DurationSeconds;
        var targetCount = Math.Clamp((int)Math.Round(duration / 7d), 4, 60);
        var anchors = input.Sections
            .SelectMany(section => new[] { section.StartSeconds, section.EndSeconds })
            .Concat(input.Phrases.SelectMany(phrase => new[] { phrase.StartSeconds, phrase.EndSeconds }))
            .Where(value => value > 0.1 && value < duration - 0.1)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var expectedSceneDuration = duration / targetCount;
        var anchorTolerance = Math.Max(2.25, Math.Min(4d, expectedSceneDuration * 0.55));

        var boundaries = new List<double>(targetCount + 1) { 0 };
        for (var index = 1; index < targetCount; index++)
        {
            var expected = duration * index / targetCount;
            var nearest = anchors
                .Select(anchor => new { Anchor = anchor, Distance = Math.Abs(anchor - expected) })
                .Where(candidate => candidate.Distance <= anchorTolerance)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();
            var proposed = nearest?.Anchor ?? expected;
            var previous = boundaries[^1];
            var remainingSlots = targetCount - index;
            var minimum = previous + 3.25;
            var maximum = duration - remainingSlots * 3.25;
            if (maximum <= minimum)
            {
                proposed = expected;
            }
            else
            {
                proposed = Math.Clamp(proposed, minimum, maximum);
            }
            boundaries.Add(Math.Round(proposed, 3));
        }
        boundaries.Add(duration);
        return boundaries;
    }

    private static IReadOnlyList<PlannedVisualArcPoint> BuildVisualArc(DirectorPlanningInput input)
    {
        var duration = input.DurationSeconds;
        var emotion = input.Controls.Emotion;
        var camera = input.Controls.CameraEnergy;
        return new[]
        {
            Arc(0, "Setup", "Establish the visual grammar and emotional baseline.", 0.25 + emotion * 0.2, 0.25, camera * 0.45),
            Arc(duration * 0.25, "Rising", "Increase visual pressure and character engagement.", 0.4 + emotion * 0.3, 0.45, camera * 0.65),
            Arc(duration * 0.5, "Turn", "Shift the visual idea so the middle feels intentional rather than repetitive.", 0.55 + emotion * 0.3, 0.62, camera * 0.8),
            Arc(duration * 0.75, "Peak", "Reach the strongest emotional and visual statement.", 0.7 + emotion * 0.25, 0.82, Math.Min(1, 0.55 + camera * 0.45)),
            Arc(duration, "Release", "Resolve or deliberately leave the final image open.", 0.35 + emotion * 0.2, 0.4, camera * 0.35),
        };

        static PlannedVisualArcPoint Arc(double time, string label, string description, double emotional, double visual, double cameraEnergy) =>
            new(time, label, description, Math.Clamp(emotional, 0, 1), Math.Clamp(visual, 0, 1), Math.Clamp(cameraEnergy, 0, 1));
    }

    private static StoryboardSceneDetails BuildSceneDetails(
        DirectorPlanningInput input,
        PlanningMusicalSection? section,
        double centerSeconds,
        int index,
        int sceneCount)
    {
        var lyricLines = input.Lyrics.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lyric = lyricLines.Length == 0
            ? string.Empty
            : lyricLines[Math.Clamp((int)Math.Floor(centerSeconds / input.DurationSeconds * lyricLines.Length), 0, lyricLines.Length - 1)];
        var symbolic = input.Controls.LiteralToSymbolic >= 0.55;
        var abstractVisuals = input.Controls.Abstraction >= 0.55;
        var lighting = input.Controls.Darkness switch
        {
            >= 0.7 => "Low-key, shadow-led lighting with restrained practical highlights.",
            <= 0.3 => "Warm, open lighting with soft practical or natural highlights.",
            _ => "Balanced cinematic contrast with controlled warmth and shadow.",
        };
        var composition = index % 4 switch
        {
            0 => "Establish spatial relationships with a readable wide or medium-wide frame.",
            1 => "Move closer and isolate the emotional subject within the frame.",
            2 => "Use layered foreground/background depth and purposeful negative space.",
            _ => "Resolve the phrase with a contrasting angle or intimate detail composition.",
        };
        var purpose = $"Advance the {section?.Label ?? "current musical phrase"} without repeating the previous visual beat; scene {index + 1} of {sceneCount}.";
        var emotion = $"{input.Mood}; emotional intensity {input.Controls.Emotion:0.00}, acting intensity {input.Controls.ActingIntensity:0.00}.";
        var environmentMotion = input.Controls.Complexity >= 0.6
            ? "Use layered but readable environmental motion synchronized to the scene energy."
            : "Keep environmental motion restrained so the primary action remains legible.";
        var symbolism = symbolic
            ? abstractVisuals
                ? "Prefer symbolic, metaphorical visual change over literal lyric illustration."
                : "Use symbolic imagery grounded in recognizable physical action and space."
            : "Prefer direct narrative imagery; symbolism may support but must not replace the readable action.";
        var continuity = "Preserve selected character identity/state, wardrobe locks, style grammar, location constraints, and visible changes established by preceding scenes.";

        return new StoryboardSceneDetails(
            section?.Label ?? "Musical phrase",
            lyric,
            purpose,
            emotion,
            composition,
            lighting,
            environmentMotion,
            symbolism,
            continuity);
    }

    private static string BuildSummary(DirectorPlanningInput input, int sceneCount) =>
        $"{sceneCount} scene visual arc for {input.Mood} {input.Genre}; " +
        $"literal/symbolic {input.Controls.LiteralToSymbolic:0.00}, narrative {input.Controls.NarrativeStrength:0.00}, " +
        $"abstraction {input.Controls.Abstraction:0.00}, surrealism {input.Controls.Surrealism:0.00}. {input.VisualDirection}".Trim();

    private static string BuildIntent(DirectorPlanningInput input, string? section, int index) =>
        $"{section ?? "Musical phrase"}: express {input.Meaning} through " +
        $"{(input.Controls.LiteralToSymbolic >= 0.55 ? "symbolic" : "more literal")} imagery; " +
        $"preserve the project storyline while advancing visual beat {index + 1}.";

    private static string BuildAction(DirectorPlanningInput input, int index)
    {
        var acting = input.Controls.ActingIntensity >= 0.65 ? "clear physical acting" : "restrained readable behavior";
        return $"Advance the story with {acting}; scene {index + 1} should change one meaningful visual condition instead of repeating the prior shot.";
    }

    private static string BuildFallbackEnvironment(DirectorPlanningInput input, int index) =>
        string.IsNullOrWhiteSpace(input.VisualDirection)
            ? $"Environment derived from {input.Mood} mood, variation {index + 1}."
            : $"{input.VisualDirection}; environment variation {index + 1}.";

    private static string BuildCamera(double energy, int index)
    {
        var baseMove = energy switch
        {
            < 0.3 => "mostly locked composition with deliberate slow reframing",
            < 0.65 => "controlled dolly or restrained handheld movement",
            _ => "energetic tracking, orbit, or purposeful handheld motion",
        };
        return $"{baseMove}; vary angle family for scene {index + 1}.";
    }

    private static bool IsNearAnchor(double time, DirectorPlanningInput input) =>
        input.Sections.Any(section => Math.Abs(section.StartSeconds - time) <= 0.15 || Math.Abs(section.EndSeconds - time) <= 0.15) ||
        input.Phrases.Any(phrase => Math.Abs(phrase.StartSeconds - time) <= 0.15 || Math.Abs(phrase.EndSeconds - time) <= 0.15);
}
