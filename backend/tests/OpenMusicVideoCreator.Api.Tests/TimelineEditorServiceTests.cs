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

public sealed class TimelineEditorServiceTests
{
    [Fact]
    public async Task InitializeAndEdit_CreateImmutableVersionsAndKeepOriginalSongLocked()
    {
        var fixture = CreateFixture();
        var initial = await fixture.Service.GetOrCreateAsync(fixture.ProjectId);

        Assert.True(initial.MusicTrackLocked);
        Assert.Equal(fixture.SongId, initial.SongMediaAssetId);
        Assert.Equal(fixture.StoryboardId, initial.StoryboardVersionId);
        Assert.Equal(2, initial.Clips.Count);

        var first = initial.Clips[0];
        var edit = new TimelineClipEdit(
            SourceInSeconds: 0.25,
            SourceDurationSeconds: 3.5,
            PlaybackRate: 1.1,
            FreezeExtensionSeconds: 0.5,
            TransitionIn: TimelineTransitionKind.Fade,
            TransitionDurationSeconds: 0.3,
            Transform: TimelineClipTransform.Default with { Scale = 1.2, PositionX = 0.2 },
            Color: TimelineColorAdjustment.Neutral with { Saturation = 0.8 });
        var edited = await fixture.Service.UpdateClipAsync(fixture.ProjectId, first.Id, edit);

        Assert.Equal(initial.Version + 1, edited.Version);
        Assert.Equal(initial.Id, edited.ParentVersionId);
        Assert.Equal(0.25, edited.Clips[0].SourceInSeconds, 3);
        Assert.Equal(1.2, edited.Clips[0].Transform.Scale, 3);
        Assert.Equal(fixture.SongId, edited.SongMediaAssetId);
        Assert.True(edited.MusicTrackLocked);

        var versions = await fixture.Service.ListVersionsAsync(fixture.ProjectId);
        Assert.Equal(2, versions.Count);
        Assert.Equal(0, versions.Single(version => version.Id == initial.Id).Clips[0].SourceInSeconds);
    }

    [Fact]
    public async Task SplitReorderReplaceAndRestore_AreNonDestructive()
    {
        var fixture = CreateFixture();
        var initial = await fixture.Service.GetOrCreateAsync(fixture.ProjectId);
        var split = await fixture.Service.SplitClipAsync(fixture.ProjectId, initial.Clips[0].Id, 2);

        Assert.Equal(3, split.Clips.Count);
        Assert.Equal(10, split.DurationSeconds, 3);
        Assert.Equal([1, 2, 3], split.Clips.Select(clip => clip.Sequence).ToArray());

        var reordered = await fixture.Service.ReorderAsync(
            fixture.ProjectId,
            [split.Clips[2].Id, split.Clips[0].Id, split.Clips[1].Id]);
        Assert.Equal(split.Clips[2].Id, reordered.Clips[0].Id);
        Assert.Equal(0, reordered.Clips[0].TimelineStartSeconds, 3);

        var target = reordered.Clips.Single(clip => clip.SceneId == fixture.SecondSceneId);
        var replaced = await fixture.Service.ReplaceClipVariantAsync(fixture.ProjectId, target.Id, fixture.AlternateVariantId);
        Assert.Equal(fixture.AlternateVariantId, replaced.Clips.Single(clip => clip.Id == target.Id).ClipVariantId);
        Assert.Equal(fixture.AlternateMediaId, replaced.Clips.Single(clip => clip.Id == target.Id).MediaAssetId);

        var restored = await fixture.Service.RestoreVersionAsync(fixture.ProjectId, initial.Id);
        Assert.Equal(replaced.Version + 1, restored.Version);
        Assert.Equal(replaced.Id, restored.ParentVersionId);
        Assert.Equal(initial.Clips.Select(clip => clip.ClipVariantId), restored.Clips.Select(clip => clip.ClipVariantId));
        Assert.NotEqual(initial.Id, restored.Id);
    }

    [Fact]
    public async Task OverlayAndEffectEdits_CreateNewVersionsWithoutChangingClipMedia()
    {
        var fixture = CreateFixture();
        var initial = await fixture.Service.GetOrCreateAsync(fixture.ProjectId);
        var overlay = await fixture.Service.UpsertOverlayAsync(
            fixture.ProjectId,
            new TimelineOverlayEdit(null, fixture.OverlayMediaId, 1, 3, 0.2, -0.1, 0.5, 0.7));
        var effect = await fixture.Service.UpsertEffectAsync(
            fixture.ProjectId,
            new TimelineEffectEdit(null, TimelineEffectKind.Grayscale, 2, 4, 0.6));

        Assert.Single(overlay.Overlays);
        Assert.Single(effect.Overlays);
        Assert.Single(effect.Effects);
        Assert.Equal(initial.Clips.Select(clip => clip.MediaAssetId), effect.Clips.Select(clip => clip.MediaAssetId));
        Assert.Equal(fixture.SongId, effect.SongMediaAssetId);
    }

    private static Fixture CreateFixture()
    {
        var projectId = Guid.NewGuid();
        var songId = Guid.NewGuid();
        var firstSceneId = Guid.NewGuid();
        var secondSceneId = Guid.NewGuid();
        var firstVariantId = Guid.NewGuid();
        var secondVariantId = Guid.NewGuid();
        var alternateVariantId = Guid.NewGuid();
        var firstMediaId = Guid.NewGuid();
        var secondMediaId = Guid.NewGuid();
        var alternateMediaId = Guid.NewGuid();
        var overlayMediaId = Guid.NewGuid();
        var storyboardId = Guid.NewGuid();
        var project = new MusicVideoProject(
            projectId, "Timeline fixture", "Artist", "", "", "", "", "", "",
            ProjectAspectRatio.Landscape16x9, new OutputResolution(1920, 1080), [], GenerationPreset.Balanced,
            null, null, [new ProjectReference(ProjectReferenceKind.Song, songId)], Now(), Now());
        var storyboard = new StoryboardVersion(
            storyboardId, projectId, Guid.NewGuid(), Guid.NewGuid(), 1,
            [
                Scene(firstSceneId, 1, 0, 4, "Cut"),
                Scene(secondSceneId, 2, 4, 10, "Fade"),
            ], Now());
        var clipRepository = new ClipRepository(
            Clip(firstVariantId, projectId, firstSceneId, firstMediaId, 1, selected: true, 4),
            Clip(secondVariantId, projectId, secondSceneId, secondMediaId, 1, selected: true, 6),
            Clip(alternateVariantId, projectId, secondSceneId, alternateMediaId, 2, selected: false, 6));
        var media = new MediaRepository(
            Asset(songId, projectId, "source/song.flac", "audio/flac", 10),
            Asset(firstMediaId, projectId, "generated/one.mp4", "video/mp4", 4),
            Asset(secondMediaId, projectId, "generated/two.mp4", "video/mp4", 6),
            Asset(alternateMediaId, projectId, "generated/two-alt.mp4", "video/mp4", 6),
            Asset(overlayMediaId, projectId, "source/logo.png", "image/png", null));
        var timelines = new TimelineRepository();
        var service = new TimelineEditorService(
            new ProjectRepository(project),
            new StoryboardRepository(storyboard),
            clipRepository,
            media,
            timelines,
            new FixedTimeProvider());
        return new Fixture(projectId, songId, storyboardId, secondSceneId, alternateVariantId, alternateMediaId, overlayMediaId, service);
    }

    private static StoryboardScene Scene(Guid id, int sequence, double start, double end, string transition) =>
        new(id, sequence, start, end, $"Scene {sequence}", "Intent", "Action", "Environment", "Camera", transition, [], [], [], Guid.NewGuid());

    private static SceneClipVariant Clip(Guid id, Guid projectId, Guid sceneId, Guid mediaId, int number, bool selected, double duration) =>
        new(id, projectId, sceneId, number, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), mediaId,
            "mock-video", "mock-video-v1", GenerationVariantState.Completed, selected, TimeSpan.FromSeconds(duration),
            "16:9", "1920x1080", 0m, 0m, "USD", Now(), Now());

    private static MediaAssetMetadata Asset(Guid id, Guid projectId, string location, string mime, double? duration) =>
        new(id, projectId, location, new string('c', 64), mime, null, null, duration is null ? null : TimeSpan.FromSeconds(duration.Value), 100, MediaCreationSource.Generated, Now());

    private static DateTimeOffset Now() => new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(Guid ProjectId, Guid SongId, Guid StoryboardId, Guid SecondSceneId, Guid AlternateVariantId, Guid AlternateMediaId, Guid OverlayMediaId, TimelineEditorService Service);

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

    private sealed class ClipRepository(params SceneClipVariant[] items) : IClipVariantRepository
    {
        private readonly Dictionary<Guid, SceneClipVariant> _items = items.ToDictionary(item => item.Id);
        public Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SceneClipVariant>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());
        public Task<SceneClipVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryGetValue(variantId, out var item) && item.ProjectId == projectId ? item : null);
        public Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default) { _items[variant.Id] = variant; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class MediaRepository(params MediaAssetMetadata[] items) : IMediaAssetRepository
    {
        private readonly Dictionary<Guid, MediaAssetMetadata> _items = items.ToDictionary(item => item.Id);
        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaAssetMetadata>>(_items.Values.Where(item => item.ProjectId == projectId).ToArray());
        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default) { _items[asset.Id] = asset; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.Remove(id));
    }

    private sealed class TimelineRepository : IProjectTimelineRepository
    {
        private readonly List<ProjectTimelineVersion> _versions = [];
        public Task<ProjectTimelineVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(_versions.Where(version => version.ProjectId == projectId).OrderByDescending(version => version.Version).FirstOrDefault());
        public Task<IReadOnlyList<ProjectTimelineVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectTimelineVersion>>(_versions.Where(version => version.ProjectId == projectId).OrderByDescending(version => version.Version).ToArray());
        public Task UpsertAsync(ProjectTimelineVersion timeline, CancellationToken cancellationToken = default) { _versions.RemoveAll(version => version.Id == timeline.Id); _versions.Add(timeline); return Task.CompletedTask; }
    }
}
