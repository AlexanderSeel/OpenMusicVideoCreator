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

public sealed class TimelineSubtitleFlowTests
{
    [Fact]
    public async Task SubtitleChanges_CreateVersionsAndSurviveUnrelatedTimelineEdits()
    {
        var fixture = CreateFixture();
        var initial = await fixture.Service.GetOrCreateAsync(fixture.ProjectId);
        var withSubtitle = await fixture.Service.UpsertSubtitleAsync(
            fixture.ProjectId,
            new TimelineSubtitleEdit(null, "First line", 0.5, 2.5, 0.8, 1, 0.9));

        var subtitle = Assert.Single(withSubtitle.ResolveSubtitles());
        Assert.Equal(initial.Version + 1, withSubtitle.Version);
        Assert.Equal(initial.Id, withSubtitle.ParentVersionId);
        Assert.Equal("First line", subtitle.Text);

        var clip = withSubtitle.Clips[0];
        var editedClip = await fixture.Service.UpdateClipAsync(
            fixture.ProjectId,
            clip.Id,
            new TimelineClipEdit(
                clip.SourceInSeconds,
                clip.SourceDurationSeconds,
                clip.PlaybackRate,
                clip.FreezeExtensionSeconds,
                TimelineTransitionKind.Fade,
                0.25,
                clip.Transform,
                clip.Color));

        Assert.Equal(subtitle, Assert.Single(editedClip.ResolveSubtitles()));
        Assert.Equal(fixture.SongId, editedClip.SongMediaAssetId);
        Assert.True(editedClip.MusicTrackLocked);

        var updated = await fixture.Service.UpsertSubtitleAsync(
            fixture.ProjectId,
            new TimelineSubtitleEdit(subtitle.Id, "Updated line", 1, 3, 0.6, 1.2, 1));
        Assert.Equal("Updated line", Assert.Single(updated.ResolveSubtitles()).Text);

        var removed = await fixture.Service.DeleteSubtitleAsync(fixture.ProjectId, subtitle.Id);
        Assert.Empty(removed.ResolveSubtitles());

        var versions = await fixture.Service.ListVersionsAsync(fixture.ProjectId);
        Assert.Contains(versions, version => version.Id == withSubtitle.Id && Assert.Single(version.ResolveSubtitles()).Text == "First line");
        Assert.Contains(versions, version => version.Id == updated.Id && Assert.Single(version.ResolveSubtitles()).Text == "Updated line");
    }

    private static Fixture CreateFixture()
    {
        var projectId = Guid.NewGuid();
        var songId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var storyboard = new StoryboardVersion(
            Guid.NewGuid(), projectId, Guid.NewGuid(), Guid.NewGuid(), 1,
            [new StoryboardScene(sceneId, 1, 0, 5, "Scene", "Intent", "Action", "Room", "Static", "Cut", [], [], [], Guid.NewGuid())],
            Now());
        var project = new MusicVideoProject(
            projectId, "Subtitle fixture", "Artist", "", "", "", "", "", "",
            ProjectAspectRatio.Landscape16x9, new OutputResolution(1920, 1080), [], GenerationPreset.Balanced,
            null, null, [new ProjectReference(ProjectReferenceKind.Song, songId)], Now(), Now());
        var clip = new SceneClipVariant(
            variantId, projectId, sceneId, 1, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), mediaId,
            "mock-video", "mock-video-v1", GenerationVariantState.Completed, true, TimeSpan.FromSeconds(5),
            "16:9", "1920x1080", 0m, 0m, "USD", Now(), Now());
        var service = new TimelineEditorService(
            new ProjectRepository(project),
            new StoryboardRepository(storyboard),
            new ClipRepository(clip),
            new MediaRepository(
                Asset(songId, projectId, "source/song.flac", "audio/flac", 5),
                Asset(mediaId, projectId, "generated/clip.mp4", "video/mp4", 5)),
            new TimelineRepository(),
            new FixedTimeProvider());
        return new Fixture(projectId, songId, service);
    }

    private static MediaAssetMetadata Asset(Guid id, Guid projectId, string location, string mime, double duration) =>
        new(id, projectId, location, new string('a', 64), mime, null, null, TimeSpan.FromSeconds(duration), 100, MediaCreationSource.Generated, Now());

    private static DateTimeOffset Now() => new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(Guid ProjectId, Guid SongId, TimelineEditorService Service);

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

    private sealed class StoryboardRepository(StoryboardVersion storyboard) : IStoryboardRepository
    {
        public Task<StoryboardVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<StoryboardVersion?>(projectId == storyboard.ProjectId ? storyboard : null);
        public Task<IReadOnlyList<StoryboardVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoryboardVersion>>(projectId == storyboard.ProjectId ? [storyboard] : []);
        public Task UpsertAsync(StoryboardVersion value, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
