using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Application.Timeline;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Planning;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Domain.Timeline;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class TimelineCurrentStoryboardTests
{
    [Fact]
    public async Task StoryboardChange_RebasesEditsAndRejectsRestoringStaleTimeline()
    {
        var projectId = Guid.NewGuid();
        var songId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var firstStoryboard = Storyboard(projectId, sceneId, version: 1);
        var secondStoryboard = Storyboard(projectId, sceneId, version: 2);
        var storyboardRepository = new MutableStoryboardRepository(firstStoryboard);
        var project = new MusicVideoProject(
            projectId, "Current storyboard", "Artist", "", "", "", "", "", "",
            ProjectAspectRatio.Landscape16x9, new OutputResolution(1920, 1080), [], GenerationPreset.Balanced,
            null, null, [new ProjectReference(ProjectReferenceKind.Song, songId)], Now(), Now());
        var variant = new SceneClipVariant(
            variantId, projectId, sceneId, 1, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), mediaId,
            "mock-video", "mock-video-v1", GenerationVariantState.Completed, true, TimeSpan.FromSeconds(5),
            "16:9", "1920x1080", 0m, 0m, "USD", Now(), Now());
        var timelines = new TimelineRepository();
        var service = new TimelineEditorService(
            new ProjectRepository(project),
            storyboardRepository,
            new ClipRepository(variant),
            new MediaRepository(
                Asset(songId, projectId, "source/song.flac", "audio/flac"),
                Asset(mediaId, projectId, "generated/clip.mp4", "video/mp4")),
            timelines,
            new FixedTimeProvider());

        var firstTimeline = await service.GetOrCreateAsync(projectId);
        var staleClipId = firstTimeline.Clips[0].Id;
        storyboardRepository.Current = secondStoryboard;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateClipAsync(
            projectId,
            staleClipId,
            Edit(firstTimeline.Clips[0])));

        var currentTimeline = await service.GetLatestAsync(projectId);
        Assert.NotNull(currentTimeline);
        Assert.Equal(secondStoryboard.Id, currentTimeline!.StoryboardVersionId);
        Assert.NotEqual(staleClipId, currentTimeline.Clips[0].Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreVersionAsync(projectId, firstTimeline.Id));
        Assert.Contains("older storyboard version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static StoryboardVersion Storyboard(Guid projectId, Guid sceneId, int version) =>
        new(Guid.NewGuid(), projectId, Guid.NewGuid(), Guid.NewGuid(), version,
            [new StoryboardScene(sceneId, 1, 0, 5, "Scene", "Intent", "Action", "Room", "Static", "Cut", [], [], [], Guid.NewGuid())],
            Now());

    private static TimelineClipEdit Edit(TimelineClip clip) =>
        new(clip.SourceInSeconds, clip.SourceDurationSeconds, clip.PlaybackRate, clip.FreezeExtensionSeconds,
            clip.TransitionIn, clip.TransitionDurationSeconds, clip.Transform, clip.Color);

    private static MediaAssetMetadata Asset(Guid id, Guid projectId, string location, string mime) =>
        new(id, projectId, location, new string('b', 64), mime, null, null, TimeSpan.FromSeconds(5), 100, MediaCreationSource.Generated, Now());

    private static DateTimeOffset Now() => new(2026, 8, 11, 10, 30, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now();
    }

    private sealed class ProjectRepository(MusicVideoProject project) : IProjectRepository
    {
        public Task<IReadOnlyList<MusicVideoProject>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MusicVideoProject>>([project]);
        public Task<MusicVideoProject?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<MusicVideoProject?>(id == project.Id ? project : null);
        public Task UpsertAsync(MusicVideoProject value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class MutableStoryboardRepository(StoryboardVersion current) : IStoryboardRepository
    {
        public StoryboardVersion Current { get; set; } = current;
        public Task<StoryboardVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<StoryboardVersion?>(projectId == Current.ProjectId ? Current : null);
        public Task<IReadOnlyList<StoryboardVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoryboardVersion>>(projectId == Current.ProjectId ? [Current] : []);
        public Task UpsertAsync(StoryboardVersion value, CancellationToken cancellationToken = default) { Current = value; return Task.CompletedTask; }
    }

    private sealed class ClipRepository(SceneClipVariant clip) : IClipVariantRepository
    {
        public Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SceneClipVariant>>(projectId == clip.ProjectId ? [clip] : []);
        public Task<SceneClipVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult<SceneClipVariant?>(projectId == clip.ProjectId && variantId == clip.Id ? clip : null);
        public Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class MediaRepository(params MediaAssetMetadata[] assets) : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MediaAssetMetadata> _assets = assets.ToDictionary(asset => asset.Id);
        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_assets.GetValueOrDefault(id));
        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaAssetMetadata>>(_assets.Values.Where(asset => asset.ProjectId == projectId).ToArray());
        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default) { _assets[asset.Id] = asset; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_assets.Remove(id));
    }

    private sealed class TimelineRepository : IProjectTimelineRepository
    {
        private readonly List<ProjectTimelineVersion> _versions = [];
        public Task<ProjectTimelineVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(_versions.Where(version => version.ProjectId == projectId).OrderByDescending(version => version.Version).FirstOrDefault());
        public Task<IReadOnlyList<ProjectTimelineVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectTimelineVersion>>(_versions.Where(version => version.ProjectId == projectId).OrderByDescending(version => version.Version).ToArray());
        public Task UpsertAsync(ProjectTimelineVersion timeline, CancellationToken cancellationToken = default) { _versions.Add(timeline); return Task.CompletedTask; }
    }
}
