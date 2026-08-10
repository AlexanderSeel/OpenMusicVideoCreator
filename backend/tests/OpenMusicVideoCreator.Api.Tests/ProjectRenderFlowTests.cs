using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Application.Rendering;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Planning;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Infrastructure.Rendering;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class ProjectRenderFlowTests
{
    [Fact]
    public async Task PreviewAndFinal_UseSameSelectedTimelineAndOriginalSongProvenance()
    {
        var projectId = Guid.NewGuid();
        var songId = Guid.NewGuid();
        var firstSceneId = Guid.NewGuid();
        var secondSceneId = Guid.NewGuid();
        var firstMediaId = Guid.NewGuid();
        var secondMediaId = Guid.NewGuid();
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();
        var storyboardId = Guid.NewGuid();
        var project = new MusicVideoProject(
            projectId, "Render fixture", "Artist", "", "", "", "", "", "",
            ProjectAspectRatio.Landscape16x9, new OutputResolution(1920, 1080), [], GenerationPreset.Balanced,
            null, null, [new ProjectReference(ProjectReferenceKind.Song, songId)], FixedUtc(), FixedUtc());
        var storyboard = new StoryboardVersion(
            storyboardId, projectId, Guid.NewGuid(), Guid.NewGuid(), 3,
            [
                Scene(firstSceneId, 1, 0, 4.25, "Cut"),
                Scene(secondSceneId, 2, 4.25, 10, "Crossfade"),
            ], FixedUtc());
        var clips = new InMemoryClipRepository(
            CompletedClip(projectId, firstSceneId, 1, firstMediaId, firstJobId),
            CompletedClip(projectId, secondSceneId, 1, secondMediaId, secondJobId));
        var media = new InMemoryMediaRepository(
            Asset(songId, projectId, "source/song original.flac", "audio/flac"),
            Asset(firstMediaId, projectId, "generated/scene 1.mp4", "video/mp4"),
            Asset(secondMediaId, projectId, "generated/scene 2.mp4", "video/mp4"));
        var renders = new InMemoryRenderRepository();
        var jobs = new CapturingJobQueue();
        var service = new ProjectRenderService(
            new InMemoryProjectRepository(project),
            new InMemoryStoryboardRepository(storyboard),
            clips,
            media,
            renders,
            jobs,
            new FixedTimeProvider());

        var preview = await service.QueueAsync(projectId, ProjectRenderKind.Preview);
        var final = await service.QueueAsync(projectId, ProjectRenderKind.Final);

        Assert.Equal(songId, preview.Manifest.SongMediaAssetId);
        Assert.Equal(songId, final.Manifest.SongMediaAssetId);
        Assert.Equal(storyboardId, preview.Manifest.StoryboardVersionId);
        Assert.Equal(preview.Manifest.TimelineSha256, final.Manifest.TimelineSha256);
        Assert.Equal([firstSceneId, secondSceneId], preview.Manifest.Clips.Select(item => item.SceneId).ToArray());
        Assert.Equal([firstMediaId, secondMediaId], preview.Manifest.Clips.Select(item => item.MediaAssetId).ToArray());
        Assert.Equal(10, preview.Manifest.DurationSeconds, 3);
        Assert.True(preview.Manifest.Width < final.Manifest.Width);
        Assert.True(preview.Manifest.Height < final.Manifest.Height);
        Assert.Equal((1920, 1080), (final.Manifest.Width, final.Manifest.Height));
        Assert.All(jobs.Dependencies, dependencySet => Assert.Equal([firstJobId, secondJobId], dependencySet));
        Assert.All(jobs.Definitions, definition => Assert.Equal(ProjectRenderService.JobType, definition.Type));
    }

    [Fact]
    public void FfmpegArguments_KeepPathsAtomicAndMapOnlyOriginalSongAudio()
    {
        var manifest = new ProjectRenderManifest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ProjectRenderKind.Final,
            1920, 1080, 30,
            [
                new RenderTimelineClip(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, 3.5, "Cut"),
                new RenderTimelineClip(Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), 3.5, 4.5, "Cut"),
            ],
            8,
            new string('a', 64));
        var first = "/tmp/clip one;$(touch nope).mp4";
        var second = "/tmp/clip two & echo nope.mp4";
        var song = "/tmp/original song.flac";
        var output = "/tmp/final output.mp4";

        var arguments = FfmpegProjectRenderEngine.BuildArguments(manifest, [first, second], song, output).ToArray();

        Assert.Contains(first, arguments);
        Assert.Contains(second, arguments);
        Assert.Contains(song, arguments);
        Assert.Contains(output, arguments);
        Assert.DoesNotContain("sh", arguments);
        Assert.DoesNotContain("-c", arguments);
        var maps = arguments.Select((value, index) => (value, index)).Where(item => item.value == "-map").Select(item => arguments[item.index + 1]).ToArray();
        Assert.Equal(["[outv]", "2:a:0"], maps);
        Assert.Equal("libx264", arguments[Array.IndexOf(arguments, "-c:v") + 1]);
        Assert.Equal("aac", arguments[Array.IndexOf(arguments, "-c:a") + 1]);
    }

    private static StoryboardScene Scene(Guid id, int sequence, double start, double end, string transition) =>
        new(id, sequence, start, end, $"Scene {sequence}", "Intent", "Action", "Environment", "Camera", transition, [], [], [], Guid.NewGuid());

    private static SceneClipVariant CompletedClip(Guid projectId, Guid sceneId, int number, Guid mediaId, Guid jobId) =>
        new(Guid.NewGuid(), projectId, sceneId, number, Guid.NewGuid(), Guid.NewGuid(), null, jobId, mediaId,
            "mock-video", "mock-video-v1", GenerationVariantState.Completed, true, TimeSpan.FromSeconds(5),
            "16:9", "1920x1080", 0m, 0m, "USD", FixedUtc(), FixedUtc());

    private static MediaAssetMetadata Asset(Guid id, Guid projectId, string location, string mimeType) =>
        new(id, projectId, location, new string('b', 64), mimeType, null, null, null, 100, MediaCreationSource.Generated, FixedUtc());

    private static DateTimeOffset FixedUtc() => new(2026, 8, 10, 13, 30, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed class InMemoryProjectRepository(MusicVideoProject project) : IProjectRepository
    {
        public Task<IReadOnlyList<MusicVideoProject>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MusicVideoProject>>([project]);
        public Task<MusicVideoProject?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<MusicVideoProject?>(id == project.Id ? project : null);
        public Task UpsertAsync(MusicVideoProject value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class InMemoryStoryboardRepository(StoryboardVersion storyboard) : IStoryboardRepository
    {
        public Task<StoryboardVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<StoryboardVersion?>(projectId == storyboard.ProjectId ? storyboard : null);
        public Task<IReadOnlyList<StoryboardVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoryboardVersion>>(projectId == storyboard.ProjectId ? [storyboard] : []);
        public Task UpsertAsync(StoryboardVersion value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryClipRepository(params SceneClipVariant[] clips) : IClipVariantRepository
    {
        private readonly Dictionary<Guid, SceneClipVariant> _items = clips.ToDictionary(item => item.Id);
        public Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SceneClipVariant>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());
        public Task<SceneClipVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(variantId, out var item) && item.ProjectId == projectId ? item : null);
        public Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default) { _items[variant.Id] = variant; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class InMemoryMediaRepository(params MediaAssetMetadata[] assets) : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MediaAssetMetadata> _items = assets.ToDictionary(item => item.Id);
        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaAssetMetadata>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());
        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default) { _items[asset.Id] = asset; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class InMemoryRenderRepository : IProjectRenderRepository
    {
        private readonly Dictionary<Guid, ProjectRenderRecord> _items = [];
        public Task<IReadOnlyList<ProjectRenderRecord>> ListAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectRenderRecord>>(_items.Values.Where(item => item.ProjectId == projectId).OrderByDescending(item => item.Version).ToArray());
        public Task<ProjectRenderRecord?> GetAsync(Guid projectId, Guid renderId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(renderId, out var item) && item.ProjectId == projectId ? item : null);
        public Task UpsertAsync(ProjectRenderRecord render, CancellationToken cancellationToken = default) { _items[render.Id] = render; return Task.CompletedTask; }
    }

    private sealed class CapturingJobQueue : IJobQueue
    {
        public List<JobDefinition> Definitions { get; } = [];
        public List<IReadOnlyList<Guid>> Dependencies { get; } = [];

        public Task<GenerationJob> EnqueueAsync(JobDefinition definition, IReadOnlyCollection<Guid>? dependencyIds = null, CancellationToken cancellationToken = default)
        {
            Definitions.Add(definition);
            Dependencies.Add((dependencyIds ?? []).ToArray());
            var now = FixedUtc();
            return Task.FromResult(new GenerationJob(
                Guid.NewGuid(), definition.ProjectId, definition.SceneId, definition.ParentJobId, definition.Type,
                definition.PayloadJson, definition.ProviderId, definition.ModelId, JobState.Queued, null,
                definition.Priority, 0, 0, definition.MaxRetries, now, now, null, null, null, null, null, null,
                definition.EstimatedCost, null, definition.Currency, null, null));
        }
    }
}
