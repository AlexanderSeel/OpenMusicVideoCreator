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
        var fixture = CreateFixture();

        var preview = await fixture.Service.QueueAsync(fixture.ProjectId, ProjectRenderKind.Preview);
        var final = await fixture.Service.QueueAsync(fixture.ProjectId, ProjectRenderKind.Final);

        Assert.Equal(fixture.SongId, preview.Manifest.SongMediaAssetId);
        Assert.Equal(fixture.SongId, final.Manifest.SongMediaAssetId);
        Assert.Equal(fixture.StoryboardId, preview.Manifest.StoryboardVersionId);
        Assert.Equal(preview.Manifest.TimelineSha256, final.Manifest.TimelineSha256);
        Assert.Equal(fixture.SceneIds, preview.Manifest.Clips.Select(item => item.SceneId).ToArray());
        Assert.Equal(fixture.ClipMediaIds, preview.Manifest.Clips.Select(item => item.MediaAssetId).ToArray());
        Assert.Equal(10, preview.Manifest.DurationSeconds, 3);
        Assert.True(preview.Manifest.Width < final.Manifest.Width);
        Assert.True(preview.Manifest.Height < final.Manifest.Height);
        Assert.Equal((1920, 1080), (final.Manifest.Width, final.Manifest.Height));

        foreach (var render in new[] { preview, final })
        {
            Assert.NotNull(render.JobId);
            Assert.Equal(fixture.DependencyJobIds, await fixture.JobService.GetDependenciesAsync(render.JobId!.Value));
            var job = await fixture.JobService.GetAsync(render.JobId.Value);
            Assert.NotNull(job);
            Assert.Equal(ProjectRenderService.JobType, job!.Type);
        }
    }

    [Fact]
    public async Task RenderAttempts_RemainNonDestructiveAcrossRetryAndCompletion()
    {
        var fixture = CreateFixture();
        var render = await fixture.Service.QueueAsync(fixture.ProjectId, ProjectRenderKind.Final);

        render = await fixture.Service.MarkRenderingAsync(fixture.ProjectId, render.Id);
        render = await fixture.Service.MarkRetryPendingAsync(fixture.ProjectId, render.Id, "transient ffmpeg failure", "ffmpeg attempt-1");

        Assert.Equal(ProjectRenderState.Queued, render.State);
        var firstAttempt = Assert.Single(render.ResolveAttempts());
        Assert.Equal(ProjectRenderState.Failed, firstAttempt.State);
        Assert.NotNull(firstAttempt.CompletedUtc);
        Assert.Equal("transient ffmpeg failure", firstAttempt.ErrorMessage);
        Assert.Equal("ffmpeg attempt-1", firstAttempt.CommandLog);

        render = await fixture.Service.MarkRenderingAsync(fixture.ProjectId, render.Id);
        var outputMediaId = Guid.NewGuid();
        render = await fixture.Service.CompleteAsync(fixture.ProjectId, render.Id, outputMediaId, "ffmpeg attempt-2");

        Assert.Equal(ProjectRenderState.Completed, render.State);
        Assert.Equal(outputMediaId, render.OutputMediaAssetId);
        Assert.Equal(2, render.ResolveAttempts().Count);
        Assert.Equal(ProjectRenderState.Completed, render.ResolveAttempts()[1].State);
        Assert.Equal("ffmpeg attempt-2", render.ResolveAttempts()[1].CommandLog);
        Assert.Equal("ffmpeg attempt-2", render.CommandLog);
    }

    [Fact]
    public async Task CancelAndRetry_ReuseSameRenderManifestAndPersistedJob()
    {
        var fixture = CreateFixture();
        var render = await fixture.Service.QueueAsync(fixture.ProjectId, ProjectRenderKind.Preview);
        var jobId = Assert.IsType<Guid>(render.JobId);
        var timelineHash = render.Manifest.TimelineSha256;

        render = await fixture.Service.CancelAsync(fixture.ProjectId, render.Id);
        Assert.Equal(ProjectRenderState.Cancelled, render.State);
        Assert.Equal(JobState.Cancelled, (await fixture.JobService.GetAsync(jobId))!.State);

        render = await fixture.Service.RetryAsync(fixture.ProjectId, render.Id);
        Assert.Equal(ProjectRenderState.Queued, render.State);
        Assert.Equal(jobId, render.JobId);
        Assert.Equal(timelineHash, render.Manifest.TimelineSha256);
        Assert.Equal(JobState.Queued, (await fixture.JobService.GetAsync(jobId))!.State);
    }

    [Fact]
    public void FfmpegArguments_KeepPathsAtomicMapOnlyOriginalSongAudioAndHonorFade()
    {
        var manifest = new ProjectRenderManifest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ProjectRenderKind.Final,
            1920, 1080, 30,
            [
                new RenderTimelineClip(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, 3.5, "Cut"),
                new RenderTimelineClip(Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), 3.5, 4.5, "Fade"),
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
        var filter = arguments[Array.IndexOf(arguments, "-filter_complex") + 1];
        Assert.Contains("tpad=stop_mode=clone", filter);
        Assert.Contains("fade=t=in", filter);
    }

    private static RenderFixture CreateFixture()
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
                Scene(secondSceneId, 2, 4.25, 10, "Fade"),
            ], FixedUtc());
        var clips = new InMemoryClipRepository(
            CompletedClip(projectId, firstSceneId, 1, firstMediaId, firstJobId),
            CompletedClip(projectId, secondSceneId, 1, secondMediaId, secondJobId));
        var media = new InMemoryMediaRepository(
            Asset(songId, projectId, "source/song original.flac", "audio/flac"),
            Asset(firstMediaId, projectId, "generated/scene 1.mp4", "video/mp4"),
            Asset(secondMediaId, projectId, "generated/scene 2.mp4", "video/mp4"));
        var renderRepository = new InMemoryRenderRepository();
        var jobRepository = new InMemoryJobRepository(
            DependencyJob(firstJobId, projectId),
            DependencyJob(secondJobId, projectId));
        var jobService = new JobService(jobRepository, new NoopJobChangePublisher(), new FixedTimeProvider());
        var service = new ProjectRenderService(
            new InMemoryProjectRepository(project),
            new InMemoryStoryboardRepository(storyboard),
            clips,
            media,
            renderRepository,
            jobService,
            jobService,
            new FixedTimeProvider());

        return new RenderFixture(
            projectId,
            songId,
            storyboardId,
            [firstSceneId, secondSceneId],
            [firstMediaId, secondMediaId],
            [firstJobId, secondJobId],
            service,
            jobService);
    }

    private static StoryboardScene Scene(Guid id, int sequence, double start, double end, string transition) =>
        new(id, sequence, start, end, $"Scene {sequence}", "Intent", "Action", "Environment", "Camera", transition, [], [], [], Guid.NewGuid());

    private static SceneClipVariant CompletedClip(Guid projectId, Guid sceneId, int number, Guid mediaId, Guid jobId) =>
        new(Guid.NewGuid(), projectId, sceneId, number, Guid.NewGuid(), Guid.NewGuid(), null, jobId, mediaId,
            "mock-video", "mock-video-v1", GenerationVariantState.Completed, true, TimeSpan.FromSeconds(5),
            "16:9", "1920x1080", 0m, 0m, "USD", FixedUtc(), FixedUtc());

    private static MediaAssetMetadata Asset(Guid id, Guid projectId, string location, string mimeType) =>
        new(id, projectId, location, new string('b', 64), mimeType, null, null, null, 100, MediaCreationSource.Generated, FixedUtc());

    private static GenerationJob DependencyJob(Guid id, Guid projectId) =>
        new(id, projectId, null, null, "fixture", "{}", null, null, JobState.Completed, null, 100, 1, 0, 0,
            FixedUtc(), FixedUtc(), null, FixedUtc(), FixedUtc(), null, null, null, 0m, 0m, "USD", null, null);

    private static DateTimeOffset FixedUtc() => new(2026, 8, 10, 13, 30, 0, TimeSpan.Zero);

    private sealed record RenderFixture(
        Guid ProjectId,
        Guid SongId,
        Guid StoryboardId,
        Guid[] SceneIds,
        Guid[] ClipMediaIds,
        Guid[] DependencyJobIds,
        ProjectRenderService Service,
        JobService JobService);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed class NoopJobChangePublisher : IJobChangePublisher
    {
        public ValueTask PublishAsync(Guid jobId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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

    private sealed class InMemoryJobRepository(params GenerationJob[] jobs) : IJobRepository
    {
        private readonly Dictionary<Guid, GenerationJob> _jobs = jobs.ToDictionary(job => job.Id);
        private readonly Dictionary<Guid, IReadOnlyList<Guid>> _dependencies = [];
        private readonly Dictionary<(Guid JobId, int Attempt), JobAttempt> _attempts = [];

        public Task CreateAsync(GenerationJob job, IReadOnlyCollection<Guid> dependencyIds, CancellationToken cancellationToken = default)
        {
            _jobs[job.Id] = job;
            _dependencies[job.Id] = dependencyIds.ToArray();
            return Task.CompletedTask;
        }

        public Task<GenerationJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_jobs.GetValueOrDefault(id));
        public Task<IReadOnlyList<GenerationJob>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GenerationJob>>(_jobs.Values.ToArray());
        public Task<IReadOnlyList<Guid>> GetDependenciesAsync(Guid jobId, CancellationToken cancellationToken = default) => Task.FromResult(_dependencies.GetValueOrDefault(jobId) ?? (IReadOnlyList<Guid>)[]);
        public Task<IReadOnlyList<JobAttempt>> GetAttemptsAsync(Guid jobId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<JobAttempt>>(_attempts.Values.Where(attempt => attempt.JobId == jobId).OrderBy(attempt => attempt.AttemptNumber).ToArray());

        public Task<bool> TryUpdateAsync(GenerationJob job, JobState expectedState, CancellationToken cancellationToken = default)
        {
            if (!_jobs.TryGetValue(job.Id, out var current) || current.State != expectedState) return Task.FromResult(false);
            _jobs[job.Id] = job;
            return Task.FromResult(true);
        }

        public Task<GenerationJob?> TryClaimNextAsync(string workerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => Task.FromResult<GenerationJob?>(null);

        public Task UpsertAttemptAsync(JobAttempt attempt, CancellationToken cancellationToken = default)
        {
            _attempts[(attempt.JobId, attempt.AttemptNumber)] = attempt;
            return Task.CompletedTask;
        }
    }
}
