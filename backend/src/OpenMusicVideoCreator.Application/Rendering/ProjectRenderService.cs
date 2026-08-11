using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Application.Timeline;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Jobs;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Domain.Timeline;

namespace OpenMusicVideoCreator.Application.Rendering;

public interface IProjectRenderRepository
{
    Task<IReadOnlyList<ProjectRenderRecord>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectRenderRecord?> GetAsync(Guid projectId, Guid renderId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ProjectRenderRecord render, CancellationToken cancellationToken = default);
}

public sealed record RenderEngineResult(
    Stream Content,
    string FileName,
    string MimeType,
    int Width,
    int Height,
    TimeSpan Duration,
    string CommandLog) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IProjectRenderEngine
{
    Task<RenderEngineResult> RenderAsync(ProjectRenderManifest manifest, CancellationToken cancellationToken = default);
}

public sealed record ProjectRenderJobPayload(Guid RenderId);

public sealed class ProjectRenderService
{
    public const string JobType = "project.render";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProjectRepository _projects;
    private readonly IStoryboardRepository _storyboards;
    private readonly IClipVariantRepository _clips;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IProjectRenderRepository _renders;
    private readonly IJobQueue _jobs;
    private readonly JobService _jobService;
    private readonly TimeProvider _timeProvider;
    private readonly IProjectTimelineRepository? _timelines;

    public ProjectRenderService(
        IProjectRepository projects,
        IStoryboardRepository storyboards,
        IClipVariantRepository clips,
        IMediaAssetRepository mediaAssets,
        IProjectRenderRepository renders,
        IJobQueue jobs,
        JobService jobService,
        TimeProvider timeProvider,
        IProjectTimelineRepository? timelines = null)
    {
        _projects = projects;
        _storyboards = storyboards;
        _clips = clips;
        _mediaAssets = mediaAssets;
        _renders = renders;
        _jobs = jobs;
        _jobService = jobService;
        _timeProvider = timeProvider;
        _timelines = timelines;
    }

    public async Task<IReadOnlyList<ProjectRenderRecord>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var renders = await _renders.ListAsync(projectId, cancellationToken);
        var reconciled = new List<ProjectRenderRecord>(renders.Count);
        foreach (var render in renders)
        {
            reconciled.Add(await ReconcileCancelledJobAsync(render, cancellationToken));
        }
        return reconciled;
    }

    public async Task<ProjectRenderRecord?> GetAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default)
    {
        var render = await _renders.GetAsync(projectId, renderId, cancellationToken);
        return render is null ? null : await ReconcileCancelledJobAsync(render, cancellationToken);
    }

    public async Task<ProjectRenderRecord> QueueAsync(
        Guid projectId,
        ProjectRenderKind kind,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var storyboard = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Create a storyboard before rendering.");
        var songReference = project.References.SingleOrDefault(reference => reference.Kind == ProjectReferenceKind.Song)
            ?? throw new InvalidOperationException("Attach the original song before rendering.");
        var song = await _mediaAssets.GetAsync(songReference.ReferenceId, cancellationToken)
            ?? throw new InvalidOperationException("The original song media asset is missing.");
        if (song.ProjectId != projectId)
        {
            throw new InvalidOperationException("The project's Song reference does not point to project-owned media.");
        }

        var projectClips = await _clips.ListByProjectAsync(projectId, cancellationToken);
        var advancedTimeline = _timelines is null ? null : await _timelines.GetLatestAsync(projectId, cancellationToken);
        if (advancedTimeline is not null &&
            (advancedTimeline.StoryboardVersionId != storyboard.Id || advancedTimeline.SongMediaAssetId != song.Id))
        {
            advancedTimeline = null;
        }

        IReadOnlyList<RenderTimelineClip> timeline;
        IReadOnlyList<TimelineOverlay> overlays;
        IReadOnlyList<TimelineEffect> effects;
        IReadOnlyList<TimelineSubtitle> subtitles;
        Guid? timelineVersionId;
        if (advancedTimeline is not null)
        {
            timeline = await BuildAdvancedTimelineAsync(projectId, advancedTimeline, projectClips, cancellationToken);
            overlays = advancedTimeline.Overlays;
            effects = advancedTimeline.Effects;
            subtitles = advancedTimeline.ResolveSubtitles();
            timelineVersionId = advancedTimeline.Id;
            var storyboardDuration = storyboard.Scenes.Max(scene => scene.EndSeconds);
            if (Math.Abs(advancedTimeline.DurationSeconds - storyboardDuration) > 0.002)
            {
                throw new InvalidOperationException("Advanced timeline must retain the storyboard/song duration before rendering.");
            }
            foreach (var overlay in overlays)
            {
                var media = await _mediaAssets.GetAsync(overlay.MediaAssetId, cancellationToken)
                    ?? throw new InvalidOperationException($"Overlay media '{overlay.MediaAssetId}' is missing.");
                if (media.ProjectId != projectId)
                {
                    throw new InvalidOperationException("Advanced timeline overlay media belongs to another project.");
                }
            }
        }
        else
        {
            timeline = await BuildStoryboardTimelineAsync(projectId, storyboard, projectClips, cancellationToken);
            overlays = [];
            effects = [];
            subtitles = [];
            timelineVersionId = null;
        }

        var dependencies = timeline
            .Select(item => projectClips.FirstOrDefault(variant => variant.Id == item.ClipVariantId)?.JobId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        var durationSeconds = timeline.Max(item => item.TimelineStartSeconds + item.DurationSeconds);
        var (width, height) = ResolveOutputSize(project, kind);
        var timelineHash = ComputeTimelineHash(storyboard.Id, song.Id, timeline, overlays, effects, subtitles);
        var manifest = new ProjectRenderManifest(
            project.Id,
            storyboard.Id,
            song.Id,
            kind,
            width,
            height,
            30,
            timeline,
            durationSeconds,
            timelineHash,
            timelineVersionId,
            overlays,
            effects,
            subtitles);
        manifest.Validate();

        var existing = await _renders.ListAsync(projectId, cancellationToken);
        var now = GetUtcNow();
        var record = new ProjectRenderRecord(
            Guid.NewGuid(),
            projectId,
            existing.Select(item => item.Version).DefaultIfEmpty(0).Max() + 1,
            manifest,
            null,
            null,
            ProjectRenderState.Planned,
            null,
            null,
            now,
            now,
            []);
        record.Validate();
        await _renders.UpsertAsync(record, cancellationToken);

        var job = await _jobs.EnqueueAsync(
            new JobDefinition(
                projectId,
                SceneId: null,
                ParentJobId: null,
                Type: JobType,
                PayloadJson: JsonSerializer.Serialize(new ProjectRenderJobPayload(record.Id), JsonOptions),
                Priority: kind == ProjectRenderKind.Preview ? 120 : 100,
                MaxRetries: 1,
                EstimatedCost: 0m,
                Currency: "USD"),
            dependencies,
            cancellationToken);

        record = record with
        {
            JobId = job.Id,
            State = ProjectRenderState.Queued,
            UpdatedUtc = GetUtcNow(),
        };
        record.Validate();
        await _renders.UpsertAsync(record, cancellationToken);
        return record;
    }

    public async Task<ProjectRenderRecord> MarkRenderingAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        if (existing.State == ProjectRenderState.Cancelled)
        {
            return existing;
        }

        var attempts = existing.ResolveAttempts().ToList();
        attempts.Add(new ProjectRenderAttempt(
            attempts.Count + 1,
            ProjectRenderState.Rendering,
            GetUtcNow(),
            null,
            null,
            null));
        return await SaveAsync(existing with
        {
            State = ProjectRenderState.Rendering,
            ErrorMessage = null,
            Attempts = attempts,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    public async Task<ProjectRenderRecord> CompleteAsync(
        Guid projectId,
        Guid renderId,
        Guid outputMediaAssetId,
        string commandLog,
        CancellationToken cancellationToken = default)
    {
        if (outputMediaAssetId == Guid.Empty) throw new ArgumentException("Output media asset ID is required.", nameof(outputMediaAssetId));
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        existing = await ReconcileCancelledJobAsync(existing, cancellationToken);
        if (existing.State == ProjectRenderState.Cancelled)
        {
            throw new OperationCanceledException("Render was cancelled before completion.");
        }

        var attempts = CompleteCurrentAttempt(existing, ProjectRenderState.Completed, commandLog, null);
        return await SaveAsync(existing with
        {
            OutputMediaAssetId = outputMediaAssetId,
            State = ProjectRenderState.Completed,
            CommandLog = commandLog,
            ErrorMessage = null,
            Attempts = attempts,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    public async Task<ProjectRenderRecord> FailAsync(
        Guid projectId,
        Guid renderId,
        string errorMessage,
        string? commandLog,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        existing = await ReconcileCancelledJobAsync(existing, cancellationToken);
        if (existing.State == ProjectRenderState.Cancelled)
        {
            return existing;
        }

        var attempts = CompleteCurrentAttempt(existing, ProjectRenderState.Failed, commandLog, errorMessage);
        return await SaveAsync(existing with
        {
            State = ProjectRenderState.Failed,
            ErrorMessage = errorMessage,
            CommandLog = commandLog ?? existing.CommandLog,
            Attempts = attempts,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    public async Task<ProjectRenderRecord> MarkRetryPendingAsync(
        Guid projectId,
        Guid renderId,
        string errorMessage,
        string? commandLog,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        existing = await ReconcileCancelledJobAsync(existing, cancellationToken);
        if (existing.State == ProjectRenderState.Cancelled)
        {
            return existing;
        }

        var attempts = CompleteCurrentAttempt(existing, ProjectRenderState.Failed, commandLog, errorMessage);
        return await SaveAsync(existing with
        {
            State = ProjectRenderState.Queued,
            ErrorMessage = errorMessage,
            CommandLog = commandLog ?? existing.CommandLog,
            Attempts = attempts,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    public async Task<ProjectRenderRecord> CancelAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        if (existing.State == ProjectRenderState.Completed)
        {
            throw new InvalidOperationException("Completed renders cannot be cancelled.");
        }
        if (existing.JobId is not Guid jobId)
        {
            throw new InvalidOperationException("Render has no persisted job to cancel.");
        }

        var changed = await _jobService.CancelAsync(jobId, cancellationToken);
        if (!changed)
        {
            var job = await _jobService.GetAsync(jobId, cancellationToken);
            if (job?.State != JobState.Cancelled)
            {
                throw new InvalidOperationException("Render job cannot be cancelled in its current state.");
            }
        }

        var attempts = CompleteCurrentAttempt(existing, ProjectRenderState.Cancelled, existing.CommandLog, "Cancelled by user.");
        return await SaveAsync(existing with
        {
            State = ProjectRenderState.Cancelled,
            ErrorMessage = "Cancelled by user.",
            Attempts = attempts,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    public async Task<ProjectRenderRecord> RetryAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        existing = await ReconcileCancelledJobAsync(existing, cancellationToken);
        if (existing.State is not (ProjectRenderState.Failed or ProjectRenderState.Cancelled))
        {
            throw new InvalidOperationException("Only failed or cancelled renders can be retried.");
        }
        if (existing.JobId is not Guid jobId)
        {
            throw new InvalidOperationException("Render has no persisted job to restart.");
        }

        if (!await _jobService.RestartAsync(jobId, cancellationToken))
        {
            throw new InvalidOperationException("Render job could not be restarted.");
        }

        return await SaveAsync(existing with
        {
            State = ProjectRenderState.Queued,
            OutputMediaAssetId = null,
            ErrorMessage = null,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    public static ProjectRenderJobPayload DeserializePayload(string json) =>
        JsonSerializer.Deserialize<ProjectRenderJobPayload>(json, JsonOptions)
        ?? throw new InvalidDataException("Render job payload could not be deserialized.");

    private async Task<IReadOnlyList<RenderTimelineClip>> BuildStoryboardTimelineAsync(
        Guid projectId,
        StoryboardVersion storyboard,
        IReadOnlyList<SceneClipVariant> projectClips,
        CancellationToken cancellationToken)
    {
        var timeline = new List<RenderTimelineClip>(storyboard.Scenes.Count);
        foreach (var scene in storyboard.Scenes.OrderBy(scene => scene.Sequence))
        {
            var selected = projectClips.SingleOrDefault(clip =>
                clip.SceneId == scene.Id && clip.IsSelected && clip.State == GenerationVariantState.Completed && clip.MediaAssetId is not null)
                ?? throw new InvalidOperationException($"Scene {scene.Sequence} requires one selected completed clip before rendering.");
            var media = await _mediaAssets.GetAsync(selected.MediaAssetId!.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Selected clip media for Scene {scene.Sequence} is missing.");
            ValidateProjectVideo(projectId, media.ProjectId, media.MimeType, scene.Sequence);
            var transition = ParseTransition(scene.TransitionIn);
            timeline.Add(new RenderTimelineClip(
                scene.Id,
                scene.Sequence,
                selected.Id,
                media.Id,
                scene.StartSeconds,
                scene.DurationSeconds,
                scene.TransitionIn ?? string.Empty,
                SourceInSeconds: 0,
                SourceDurationSeconds: media.Duration?.TotalSeconds ?? selected.Duration.TotalSeconds,
                PlaybackRate: 1,
                FreezeExtensionSeconds: Math.Max(0, scene.DurationSeconds - (media.Duration?.TotalSeconds ?? selected.Duration.TotalSeconds)),
                Transform: TimelineClipTransform.Default,
                Color: TimelineColorAdjustment.Neutral,
                TransitionKind: transition,
                TransitionDurationSeconds: transition == TimelineTransitionKind.Cut ? 0 : Math.Min(0.35, scene.DurationSeconds / 2)));
        }
        return timeline;
    }

    private async Task<IReadOnlyList<RenderTimelineClip>> BuildAdvancedTimelineAsync(
        Guid projectId,
        ProjectTimelineVersion advancedTimeline,
        IReadOnlyList<SceneClipVariant> projectClips,
        CancellationToken cancellationToken)
    {
        var timeline = new List<RenderTimelineClip>(advancedTimeline.Clips.Count);
        foreach (var clip in advancedTimeline.Clips.OrderBy(item => item.Sequence))
        {
            var variant = projectClips.SingleOrDefault(item => item.Id == clip.ClipVariantId && item.State == GenerationVariantState.Completed && item.MediaAssetId == clip.MediaAssetId)
                ?? throw new InvalidOperationException($"Timeline clip {clip.Sequence} no longer references a completed scene variant.");
            var media = await _mediaAssets.GetAsync(clip.MediaAssetId, cancellationToken)
                ?? throw new InvalidOperationException($"Timeline media for clip {clip.Sequence} is missing.");
            ValidateProjectVideo(projectId, media.ProjectId, media.MimeType, clip.Sequence);
            if (media.Duration is TimeSpan mediaDuration && clip.SourceInSeconds + clip.SourceDurationSeconds > mediaDuration.TotalSeconds + 0.05)
            {
                throw new InvalidOperationException($"Timeline source trim for clip {clip.Sequence} exceeds its media duration.");
            }

            timeline.Add(new RenderTimelineClip(
                clip.SceneId,
                clip.Sequence,
                variant.Id,
                clip.MediaAssetId,
                clip.TimelineStartSeconds,
                clip.TimelineDurationSeconds,
                clip.TransitionIn.ToString(),
                clip.SourceInSeconds,
                clip.SourceDurationSeconds,
                clip.PlaybackRate,
                clip.FreezeExtensionSeconds,
                clip.Transform,
                clip.Color,
                clip.TransitionIn,
                clip.TransitionDurationSeconds));
        }
        return timeline;
    }

    private async Task<ProjectRenderRecord> ReconcileCancelledJobAsync(
        ProjectRenderRecord render,
        CancellationToken cancellationToken)
    {
        if (render.State is ProjectRenderState.Completed or ProjectRenderState.Cancelled || render.JobId is not Guid jobId)
        {
            return render;
        }

        var job = await _jobService.GetAsync(jobId, cancellationToken);
        if (job?.State != JobState.Cancelled)
        {
            return render;
        }

        var attempts = CompleteCurrentAttempt(render, ProjectRenderState.Cancelled, render.CommandLog, "Cancelled through the persistent job queue.");
        return await SaveAsync(render with
        {
            State = ProjectRenderState.Cancelled,
            OutputMediaAssetId = null,
            ErrorMessage = "Cancelled through the persistent job queue.",
            Attempts = attempts,
            UpdatedUtc = GetUtcNow(),
        }, cancellationToken);
    }

    private IReadOnlyList<ProjectRenderAttempt> CompleteCurrentAttempt(
        ProjectRenderRecord render,
        ProjectRenderState state,
        string? commandLog,
        string? errorMessage)
    {
        var attempts = render.ResolveAttempts().ToList();
        if (attempts.Count == 0 || attempts[^1].CompletedUtc is not null)
        {
            return attempts;
        }
        attempts[^1] = attempts[^1] with
        {
            State = state,
            CompletedUtc = GetUtcNow(),
            CommandLog = commandLog ?? attempts[^1].CommandLog,
            ErrorMessage = errorMessage,
        };
        return attempts;
    }

    private async Task<ProjectRenderRecord> SaveAsync(ProjectRenderRecord render, CancellationToken cancellationToken)
    {
        render.Validate();
        await _renders.UpsertAsync(render, cancellationToken);
        return render;
    }

    private async Task<ProjectRenderRecord> RequireAsync(Guid projectId, Guid renderId, CancellationToken cancellationToken) =>
        await _renders.GetAsync(projectId, renderId, cancellationToken)
        ?? throw new KeyNotFoundException($"Render '{renderId}' was not found.");

    private static void ValidateProjectVideo(Guid projectId, Guid? mediaProjectId, string mimeType, int sequence)
    {
        if (mediaProjectId != projectId || !mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Timeline clip media for position {sequence} is not a project video asset.");
        }
    }

    private static (int Width, int Height) ResolveOutputSize(MusicVideoProject project, ProjectRenderKind kind)
    {
        if (kind == ProjectRenderKind.Final)
        {
            return (Even(project.Resolution.Width), Even(project.Resolution.Height));
        }

        const int maxDimension = 960;
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(project.Resolution.Width, project.Resolution.Height));
        return (Even(Math.Max(2, (int)Math.Round(project.Resolution.Width * scale))),
            Even(Math.Max(2, (int)Math.Round(project.Resolution.Height * scale))));
    }

    private static int Even(int value) => value % 2 == 0 ? value : value - 1;

    private static TimelineTransitionKind ParseTransition(string? value)
    {
        if (value?.Contains("cross", StringComparison.OrdinalIgnoreCase) == true) return TimelineTransitionKind.Crossfade;
        if (value?.Contains("fade", StringComparison.OrdinalIgnoreCase) == true) return TimelineTransitionKind.Fade;
        return TimelineTransitionKind.Cut;
    }

    private static string ComputeTimelineHash(
        Guid storyboardId,
        Guid songId,
        IReadOnlyList<RenderTimelineClip> clips,
        IReadOnlyList<TimelineOverlay> overlays,
        IReadOnlyList<TimelineEffect> effects,
        IReadOnlyList<TimelineSubtitle> subtitles)
    {
        var canonical = new StringBuilder()
            .Append(storyboardId.ToString("N")).Append('|')
            .Append(songId.ToString("N"));
        foreach (var clip in clips.OrderBy(item => item.Sequence))
        {
            var transform = clip.ResolveTransform();
            var color = clip.ResolveColor();
            canonical.Append('|')
                .Append(clip.Sequence).Append(':')
                .Append(clip.SceneId.ToString("N")).Append(':')
                .Append(clip.ClipVariantId.ToString("N")).Append(':')
                .Append(clip.MediaAssetId.ToString("N")).Append(':')
                .Append(F(clip.TimelineStartSeconds)).Append(':')
                .Append(F(clip.DurationSeconds)).Append(':')
                .Append(F(clip.SourceInSeconds)).Append(':')
                .Append(F(clip.SourceDurationSeconds ?? clip.DurationSeconds)).Append(':')
                .Append(F(clip.PlaybackRate)).Append(':')
                .Append(F(clip.FreezeExtensionSeconds)).Append(':')
                .Append(clip.TransitionKind ?? TimelineTransitionKind.Cut).Append(':')
                .Append(F(clip.TransitionDurationSeconds)).Append(':')
                .Append(F(transform.Scale)).Append(':').Append(F(transform.PositionX)).Append(':').Append(F(transform.PositionY)).Append(':')
                .Append(F(transform.CropLeft)).Append(':').Append(F(transform.CropTop)).Append(':').Append(F(transform.CropRight)).Append(':').Append(F(transform.CropBottom)).Append(':').Append(F(transform.Opacity)).Append(':')
                .Append(F(color.Brightness)).Append(':').Append(F(color.Contrast)).Append(':').Append(F(color.Saturation));
        }
        foreach (var overlay in overlays.OrderBy(item => item.StartSeconds).ThenBy(item => item.Id))
        {
            canonical.Append("|o:").Append(overlay.MediaAssetId.ToString("N")).Append(':').Append(F(overlay.StartSeconds)).Append(':').Append(F(overlay.EndSeconds)).Append(':').Append(F(overlay.PositionX)).Append(':').Append(F(overlay.PositionY)).Append(':').Append(F(overlay.Scale)).Append(':').Append(F(overlay.Opacity));
        }
        foreach (var effect in effects.OrderBy(item => item.StartSeconds).ThenBy(item => item.Id))
        {
            canonical.Append("|e:").Append(effect.Kind).Append(':').Append(F(effect.StartSeconds)).Append(':').Append(F(effect.EndSeconds)).Append(':').Append(F(effect.Strength));
        }
        foreach (var subtitle in subtitles.OrderBy(item => item.StartSeconds).ThenBy(item => item.Id))
        {
            canonical.Append("|s:")
                .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(subtitle.Text))).Append(':')
                .Append(F(subtitle.StartSeconds)).Append(':')
                .Append(F(subtitle.EndSeconds)).Append(':')
                .Append(F(subtitle.PositionY)).Append(':')
                .Append(F(subtitle.Size)).Append(':')
                .Append(F(subtitle.Opacity));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static string F(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
