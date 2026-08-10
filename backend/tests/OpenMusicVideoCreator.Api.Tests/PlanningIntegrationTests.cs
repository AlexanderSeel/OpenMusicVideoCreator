using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Domain.Planning;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using OpenMusicVideoCreator.Infrastructure.Planning;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class PlanningIntegrationTests
{
    [Fact]
    public async Task StructuredMockDirector_CreatesMusicAwareTypicalSceneCountWithoutRigidEqualSlices()
    {
        var planner = new StructuredMockDirectorProvider();
        var input = new DirectorPlanningInput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            180,
            120,
            "Verse one\nChorus\nVerse two\nChorus",
            "Two people miss each other across changing places.",
            "Wrong time, real connection.",
            "Mystic cinematic realism.",
            "Intimate and hopeful",
            "Drum & Bass",
            DirectorControls.Balanced,
            [
                new PlanningMusicalSection(Guid.NewGuid(), "Intro", "Intro", 0, 16, 0.9),
                new PlanningMusicalSection(Guid.NewGuid(), "Verse 1", "Verse", 16, 58, 0.9),
                new PlanningMusicalSection(Guid.NewGuid(), "Chorus", "Chorus", 58, 88, 0.95),
                new PlanningMusicalSection(Guid.NewGuid(), "Verse 2", "Verse", 88, 132, 0.9),
                new PlanningMusicalSection(Guid.NewGuid(), "Final chorus", "Chorus", 132, 168, 0.95),
                new PlanningMusicalSection(Guid.NewGuid(), "Outro", "Outro", 168, 180, 0.9),
            ],
            Enumerable.Range(1, 12)
                .Select(index => new PlanningPhrase(index, (index - 1) * 15, index * 15, 0.8))
                .ToArray(),
            [],
            [],
            []);

        var result = await planner.PlanAsync(input);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.InRange(result.Value.Scenes.Count, 20, 35);
        Assert.Equal(0, result.Value.Scenes[0].StartSeconds, 3);
        Assert.Equal(180, result.Value.Scenes[^1].EndSeconds, 3);
        for (var index = 1; index < result.Value.Scenes.Count; index++)
        {
            Assert.Equal(result.Value.Scenes[index - 1].EndSeconds, result.Value.Scenes[index].StartSeconds, 3);
        }

        var durations = result.Value.Scenes.Select(scene => Math.Round(scene.EndSeconds - scene.StartSeconds, 2)).Distinct().ToArray();
        Assert.True(durations.Length > 1);
        Assert.Contains(result.Value.Scenes, scene => Math.Abs(scene.StartSeconds - 58) < 0.15 || Math.Abs(scene.EndSeconds - 58) < 0.15);
        Assert.All(result.Value.Scenes, scene =>
        {
            Assert.NotNull(scene.Details);
            Assert.False(string.IsNullOrWhiteSpace(scene.Details!.SongSection));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.Purpose));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.Emotion));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.Composition));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.Lighting));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.EnvironmentMotion));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.VisualSymbolism));
            Assert.False(string.IsNullOrWhiteSpace(scene.Details.ContinuityRequirements));
        });
    }

    [Fact]
    public async Task PlanningHistory_SurvivesRepositoryRecreationAndPreservesPromptProvenance()
    {
        using var storage = new TemporaryStorage();
        var connectionFactory = new DuckDbConnectionFactory(storage.Options);
        var database = new DuckDbDatabase(connectionFactory);
        await database.InitializeAsync();
        var settings = new DuckDbSettingsRepository(connectionFactory);
        var first = new DuckDbPlanningRepository(settings);
        var projectId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var arc = new VisualArcVersion(
            Guid.NewGuid(),
            projectId,
            analysisId,
            1,
            "Rising arc",
            DirectorControls.Balanced,
            [
                new VisualArcPoint(Guid.NewGuid(), 0, "Start", "Begin", 0.2, 0.2, 0.2),
                new VisualArcPoint(Guid.NewGuid(), 180, "Release", "End", 0.4, 0.35, 0.2),
            ],
            FixedUtc());
        var sceneId = Guid.NewGuid();
        var storyboardId = Guid.NewGuid();
        var prompt = new PromptVersion(
            Guid.NewGuid(),
            projectId,
            sceneId,
            storyboardId,
            1,
            "storyboard-scene",
            1,
            "Keep the character emotionally restrained.",
            "Intent: Keep the character emotionally restrained.\nCamera: close and still.",
            FixedUtc());
        var details = new StoryboardSceneDetails(
            "Verse",
            "A line of lyrics",
            "Advance the emotional beat.",
            "Restrained longing.",
            "Close layered composition.",
            "Soft low-key lighting.",
            "Slow rain and distant traffic.",
            "Reflections imply emotional distance.",
            "Preserve wardrobe and weather continuity.");
        var storyboard = new StoryboardVersion(
            storyboardId,
            projectId,
            analysisId,
            arc.Id,
            1,
            [new StoryboardScene(sceneId, 1, 0, 180, "Single scene", prompt.DirectorIntent, "Wait", "Station", "Locked", "Cut", [], [], [], prompt.Id, details)],
            FixedUtc());

        await ((IVisualArcRepository)first).UpsertAsync(arc);
        await ((IStoryboardRepository)first).UpsertAsync(storyboard);
        await ((IPromptHistoryRepository)first).UpsertAsync(prompt);

        var recreatedSettings = new DuckDbSettingsRepository(new DuckDbConnectionFactory(storage.Options));
        var recreated = new DuckDbPlanningRepository(recreatedSettings);
        var restoredArc = await ((IVisualArcRepository)recreated).GetLatestAsync(projectId);
        var restoredStoryboard = await ((IStoryboardRepository)recreated).GetLatestAsync(projectId);
        var restoredPrompts = await ((IPromptHistoryRepository)recreated).ListBySceneAsync(projectId, sceneId);

        Assert.NotNull(restoredArc);
        Assert.NotNull(restoredStoryboard);
        Assert.Equal(arc.Id, restoredArc.Id);
        Assert.Equal(storyboard.Id, restoredStoryboard.Id);
        Assert.Equal(details, restoredStoryboard.Scenes[0].Details);
        Assert.Single(restoredPrompts);
        Assert.Equal(prompt.Id, restoredPrompts[0].Id);
        Assert.Equal(storyboardId, restoredPrompts[0].StoryboardVersionId);
        Assert.Equal(prompt.DirectorIntent, restoredPrompts[0].DirectorIntent);
        Assert.Equal(prompt.FinalProviderPrompt, restoredPrompts[0].FinalProviderPrompt);
    }

    [Fact]
    public void StoryboardValidation_RejectsOverlapButAllowsIndependentSceneContentChanges()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new StoryboardScene(firstId, 1, 0, 8, "One", "Intent one", "Action", "Room", "Wide", "Cut", [], [], [], null);
        var second = new StoryboardScene(secondId, 2, 8, 16, "Two", "Intent two", "Action", "Street", "Close", "Cut", [], [], [], null);

        StoryboardVersion.ValidateScenes(16, [first, second]);
        StoryboardVersion.ValidateScenes(16, [first with { DirectorIntent = "Changed only this scene" }, second]);

        Assert.Throws<ArgumentException>(() =>
            StoryboardVersion.ValidateScenes(16, [first, second with { StartSeconds = 7.5 }]));
    }

    private static DateTimeOffset FixedUtc() => new(2026, 8, 9, 18, 30, 0, TimeSpan.Zero);

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenMusicVideoCreator.PlanningTests", Guid.NewGuid().ToString("N"));

        public TemporaryStorage()
        {
            Directory.CreateDirectory(_root);
            Options = new StorageOptions(Path.Combine(_root, "data", "app.duckdb"), Path.Combine(_root, "projects"));
        }

        public StorageOptions Options { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
