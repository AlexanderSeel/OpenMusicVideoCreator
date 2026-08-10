using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Domain.Timeline;

namespace OpenMusicVideoCreator.Application.Timeline;

public interface IProjectTimelineRepository
{
    Task<ProjectTimelineVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectTimelineVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ProjectTimelineVersion timeline, CancellationToken cancellationToken = default);
}

public sealed record TimelineClipEdit(
    double SourceInSeconds,
    double SourceDurationSeconds,
    double PlaybackRate,
    double FreezeExtensionSeconds,
    TimelineTransitionKind TransitionIn,
    double TransitionDurationSeconds,
    TimelineClipTransform Transform,
    TimelineColorAdjustment Color);

public sealed record TimelineOverlayEdit(
    Guid? Id,
    Guid MediaAssetId,
    double StartSeconds,
    double EndSeconds,
    double PositionX,
    double PositionY,
    double Scale,
    double Opacity);

public sealed record TimelineEffectEdit(
    Guid? Id,
    TimelineEffectKind Kind,
    double StartSeconds,
    double EndSeconds,
    double Strength);

public sealed class TimelineEditorService
{
    private readonly IProjectRepository _projects;
    private readonly IStoryboardRepository _storyboards;
    private readonly IClipVariantRepository _clipVariants;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IProjectTimelineRepository _timelines;
    private readonly TimeProvider _timeProvider;

    public TimelineEditorService(
        IProjectRepository projects,
        IStoryboardRepository storyboards,
        IClipVariantRepository clipVariants,
        IMediaAssetRepository mediaAssets,
        IProjectTimelineRepository timelines,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _storyboards = storyboards;
        _clipVariants = clipVariants;
        _mediaAssets = mediaAssets;
        _timelines = timelines;
        _timeProvider = timeProvider;
    }

    public Task<ProjectTimelineVersion?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _timelines.GetLatestAsync(projectId, cancellationToken);

    public Task<IReadOnlyList<ProjectTimelineVersion>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _timelines.ListVersionsAsync(projectId, cancellationToken);

    public async Task<ProjectTimelineVersion> GetOrCreateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken);
        var storyboard = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Create a storyboard before opening the Advanced timeline.");
        var songId = RequireSongId(project);
        var latest = await _timelines.GetLatestAsync(projectId, cancellationToken);
        if (latest is not null && latest.StoryboardVersionId == storyboard.Id && latest.SongMediaAssetId == songId)
        {
            return latest;
        }

        return await CreateFromStoryboardAsync(project, storyboard, latest?.Id, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> ResetFromStoryboardAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken);
        var storyboard = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Create a storyboard before resetting the timeline.");
        var latest = await _timelines.GetLatestAsync(projectId, cancellationToken);
        return await CreateFromStoryboardAsync(project, storyboard, latest?.Id, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> UpdateClipAsync(
        Guid projectId,
        Guid clipId,
        TimelineClipEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        var existing = latest.Clips.SingleOrDefault(clip => clip.Id == clipId)
            ?? throw new KeyNotFoundException($"Timeline clip '{clipId}' was not found.");
        await ValidateSourceRangeAsync(projectId, existing.MediaAssetId, edit.SourceInSeconds, edit.SourceDurationSeconds, cancellationToken);

        var updated = existing with
        {
            SourceInSeconds = edit.SourceInSeconds,
            SourceDurationSeconds = edit.SourceDurationSeconds,
            PlaybackRate = edit.PlaybackRate,
            FreezeExtensionSeconds = edit.FreezeExtensionSeconds,
            TransitionIn = edit.TransitionIn,
            TransitionDurationSeconds = edit.TransitionIn == TimelineTransitionKind.Cut ? 0 : edit.TransitionDurationSeconds,
            Transform = edit.Transform,
            Color = edit.Color,
        };
        updated.Validate();
        return await SaveNextAsync(latest, latest.Clips.Select(clip => clip.Id == clipId ? updated : clip).ToArray(), latest.Overlays, latest.Effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> ReorderAsync(
        Guid projectId,
        IReadOnlyList<Guid> orderedClipIds,
        CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        if (orderedClipIds.Count != latest.Clips.Count || orderedClipIds.Distinct().Count() != latest.Clips.Count)
        {
            throw new ArgumentException("Timeline reorder must contain every clip exactly once.", nameof(orderedClipIds));
        }

        var current = latest.Clips.ToDictionary(clip => clip.Id);
        if (orderedClipIds.Any(id => !current.ContainsKey(id)))
        {
            throw new ArgumentException("Timeline reorder contains an unknown clip.", nameof(orderedClipIds));
        }

        var slots = latest.Clips.OrderBy(clip => clip.Sequence)
            .Select(clip => (clip.TimelineStartSeconds, clip.TimelineDurationSeconds))
            .ToArray();
        var reordered = orderedClipIds.Select((id, index) => current[id] with
        {
            Sequence = index + 1,
            TimelineStartSeconds = slots[index].TimelineStartSeconds,
            TimelineDurationSeconds = slots[index].TimelineDurationSeconds,
        }).ToArray();
        return await SaveNextAsync(latest, reordered, latest.Overlays, latest.Effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> ReplaceClipVariantAsync(
        Guid projectId,
        Guid clipId,
        Guid clipVariantId,
        CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        var existing = latest.Clips.SingleOrDefault(clip => clip.Id == clipId)
            ?? throw new KeyNotFoundException($"Timeline clip '{clipId}' was not found.");
        var variant = await _clipVariants.GetAsync(projectId, clipVariantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Clip variant '{clipVariantId}' was not found.");
        if (variant.SceneId != existing.SceneId || variant.State != GenerationVariantState.Completed || variant.MediaAssetId is not Guid mediaId)
        {
            throw new InvalidOperationException("Timeline replacement requires a completed variant from the same scene.");
        }
        var media = await _mediaAssets.GetAsync(mediaId, cancellationToken)
            ?? throw new InvalidOperationException("Replacement clip media is missing.");
        if (media.ProjectId != projectId || !media.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Replacement clip must reference project video media.");
        }

        var available = media.Duration?.TotalSeconds ?? variant.Duration.TotalSeconds;
        var updated = existing with
        {
            ClipVariantId = variant.Id,
            MediaAssetId = mediaId,
            SourceInSeconds = 0,
            SourceDurationSeconds = Math.Max(0.001, Math.Min(available, existing.TimelineDurationSeconds)),
            PlaybackRate = 1,
            FreezeExtensionSeconds = Math.Max(0, existing.TimelineDurationSeconds - available),
        };
        updated.Validate();
        return await SaveNextAsync(latest, latest.Clips.Select(clip => clip.Id == clipId ? updated : clip).ToArray(), latest.Overlays, latest.Effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> SplitClipAsync(
        Guid projectId,
        Guid clipId,
        double splitAtSeconds,
        CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        var ordered = latest.Clips.OrderBy(clip => clip.Sequence).ToList();
        var index = ordered.FindIndex(clip => clip.Id == clipId);
        if (index < 0) throw new KeyNotFoundException($"Timeline clip '{clipId}' was not found.");
        var existing = ordered[index];
        if (!double.IsFinite(splitAtSeconds) || splitAtSeconds <= 0.1 || splitAtSeconds >= existing.TimelineDurationSeconds - 0.1)
        {
            throw new ArgumentOutOfRangeException(nameof(splitAtSeconds), "Split point must leave at least 100 ms on both sides.");
        }

        var leftSource = Math.Min(existing.SourceDurationSeconds, splitAtSeconds * existing.PlaybackRate);
        var rightSource = Math.Max(0.001, existing.SourceDurationSeconds - leftSource);
        var left = existing with
        {
            Id = Guid.NewGuid(),
            TimelineDurationSeconds = splitAtSeconds,
            SourceDurationSeconds = Math.Max(0.001, leftSource),
            FreezeExtensionSeconds = Math.Max(0, splitAtSeconds - leftSource / existing.PlaybackRate),
        };
        var right = existing with
        {
            Id = Guid.NewGuid(),
            TimelineStartSeconds = existing.TimelineStartSeconds + splitAtSeconds,
            TimelineDurationSeconds = existing.TimelineDurationSeconds - splitAtSeconds,
            SourceInSeconds = existing.SourceInSeconds + leftSource,
            SourceDurationSeconds = rightSource,
            FreezeExtensionSeconds = Math.Max(0, existing.TimelineDurationSeconds - splitAtSeconds - rightSource / existing.PlaybackRate),
            TransitionIn = TimelineTransitionKind.Cut,
            TransitionDurationSeconds = 0,
        };
        ordered.RemoveAt(index);
        ordered.InsertRange(index, [left, right]);
        var normalized = NormalizeSequenceAndStarts(ordered);
        return await SaveNextAsync(latest, normalized, latest.Overlays, latest.Effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> UpsertOverlayAsync(
        Guid projectId,
        TimelineOverlayEdit edit,
        CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        var media = await _mediaAssets.GetAsync(edit.MediaAssetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Overlay media '{edit.MediaAssetId}' was not found.");
        if (media.ProjectId != projectId)
        {
            throw new InvalidOperationException("Overlay media must belong to the current project.");
        }

        var overlay = new TimelineOverlay(
            edit.Id is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            edit.MediaAssetId,
            edit.StartSeconds,
            edit.EndSeconds,
            edit.PositionX,
            edit.PositionY,
            edit.Scale,
            edit.Opacity);
        overlay.Validate(latest.DurationSeconds);
        var overlays = latest.Overlays.Where(item => item.Id != overlay.Id).Append(overlay).OrderBy(item => item.StartSeconds).ToArray();
        return await SaveNextAsync(latest, latest.Clips, overlays, latest.Effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> DeleteOverlayAsync(Guid projectId, Guid overlayId, CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        if (latest.Overlays.All(item => item.Id != overlayId)) throw new KeyNotFoundException($"Overlay '{overlayId}' was not found.");
        return await SaveNextAsync(latest, latest.Clips, latest.Overlays.Where(item => item.Id != overlayId).ToArray(), latest.Effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> UpsertEffectAsync(
        Guid projectId,
        TimelineEffectEdit edit,
        CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        var effect = new TimelineEffect(
            edit.Id is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            edit.Kind,
            edit.StartSeconds,
            edit.EndSeconds,
            edit.Strength);
        effect.Validate(latest.DurationSeconds);
        var effects = latest.Effects.Where(item => item.Id != effect.Id).Append(effect).OrderBy(item => item.StartSeconds).ToArray();
        return await SaveNextAsync(latest, latest.Clips, latest.Overlays, effects, cancellationToken);
    }

    public async Task<ProjectTimelineVersion> DeleteEffectAsync(Guid projectId, Guid effectId, CancellationToken cancellationToken = default)
    {
        var latest = await RequireLatestAsync(projectId, cancellationToken);
        if (latest.Effects.All(item => item.Id != effectId)) throw new KeyNotFoundException($"Effect '{effectId}' was not found.");
        return await SaveNextAsync(latest, latest.Clips, latest.Overlays, latest.Effects.Where(item => item.Id != effectId).ToArray(), cancellationToken);
    }

    public async Task<ProjectTimelineVersion> RestoreVersionAsync(
        Guid projectId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var project = await RequireProjectAsync(projectId, cancellationToken);
        var versions = await _timelines.ListVersionsAsync(projectId, cancellationToken);
        var source = versions.SingleOrDefault(version => version.Id == versionId)
            ?? throw new KeyNotFoundException($"Timeline version '{versionId}' was not found.");
        if (source.SongMediaAssetId != RequireSongId(project))
        {
            throw new InvalidOperationException("Cannot restore a timeline created for a different Song asset.");
        }
        var latest = versions.OrderByDescending(version => version.Version).FirstOrDefault();
        var restored = source with
        {
            Id = Guid.NewGuid(),
            Version = (latest?.Version ?? 0) + 1,
            ParentVersionId = latest?.Id,
            CreatedUtc = GetUtcNow(),
        };
        restored.Validate();
        await _timelines.UpsertAsync(restored, cancellationToken);
        return restored;
    }

    private async Task<ProjectTimelineVersion> CreateFromStoryboardAsync(
        MusicVideoProject project,
        Domain.Planning.StoryboardVersion storyboard,
        Guid? parentVersionId,
        CancellationToken cancellationToken)
    {
        var variants = await _clipVariants.ListByProjectAsync(project.Id, cancellationToken);
        var clips = new List<TimelineClip>(storyboard.Scenes.Count);
        foreach (var scene in storyboard.Scenes.OrderBy(scene => scene.Sequence))
        {
            var selected = variants.SingleOrDefault(variant => variant.SceneId == scene.Id && variant.IsSelected && variant.State == GenerationVariantState.Completed && variant.MediaAssetId is not null)
                ?? throw new InvalidOperationException($"Scene {scene.Sequence} needs a selected completed clip before opening the timeline.");
            var mediaId = selected.MediaAssetId!.Value;
            var media = await _mediaAssets.GetAsync(mediaId, cancellationToken)
                ?? throw new InvalidOperationException($"Selected media for Scene {scene.Sequence} is missing.");
            var available = media.Duration?.TotalSeconds ?? selected.Duration.TotalSeconds;
            var sourceDuration = Math.Max(0.001, Math.Min(available, scene.DurationSeconds));
            var transition = ParseTransition(scene.TransitionIn);
            clips.Add(new TimelineClip(
                Guid.NewGuid(),
                scene.Id,
                scene.Sequence,
                selected.Id,
                mediaId,
                scene.StartSeconds,
                scene.DurationSeconds,
                0,
                sourceDuration,
                1,
                Math.Max(0, scene.DurationSeconds - sourceDuration),
                transition,
                transition == TimelineTransitionKind.Cut ? 0 : Math.Min(0.35, scene.DurationSeconds / 2),
                TimelineClipTransform.Default,
                TimelineColorAdjustment.Neutral));
        }

        var versions = await _timelines.ListVersionsAsync(project.Id, cancellationToken);
        var timeline = new ProjectTimelineVersion(
            Guid.NewGuid(),
            project.Id,
            storyboard.Id,
            RequireSongId(project),
            versions.Select(version => version.Version).DefaultIfEmpty(0).Max() + 1,
            parentVersionId,
            MusicTrackLocked: true,
            clips,
            [],
            [],
            GetUtcNow());
        timeline.Validate();
        await _timelines.UpsertAsync(timeline, cancellationToken);
        return timeline;
    }

    private async Task<ProjectTimelineVersion> SaveNextAsync(
        ProjectTimelineVersion latest,
        IReadOnlyList<TimelineClip> clips,
        IReadOnlyList<TimelineOverlay> overlays,
        IReadOnlyList<TimelineEffect> effects,
        CancellationToken cancellationToken)
    {
        var next = new ProjectTimelineVersion(
            Guid.NewGuid(),
            latest.ProjectId,
            latest.StoryboardVersionId,
            latest.SongMediaAssetId,
            latest.Version + 1,
            latest.Id,
            MusicTrackLocked: true,
            clips,
            overlays,
            effects,
            GetUtcNow());
        next.Validate();
        await _timelines.UpsertAsync(next, cancellationToken);
        return next;
    }

    private async Task<ProjectTimelineVersion> RequireLatestAsync(Guid projectId, CancellationToken cancellationToken) =>
        await _timelines.GetLatestAsync(projectId, cancellationToken)
        ?? await GetOrCreateAsync(projectId, cancellationToken);

    private async Task<MusicVideoProject> RequireProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await _projects.GetAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

    private async Task ValidateSourceRangeAsync(
        Guid projectId,
        Guid mediaAssetId,
        double sourceIn,
        double sourceDuration,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(sourceIn) || sourceIn < 0 || !double.IsFinite(sourceDuration) || sourceDuration <= 0)
        {
            throw new ArgumentException("Source trim range is invalid.");
        }
        var media = await _mediaAssets.GetAsync(mediaAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Timeline source media is missing.");
        if (media.ProjectId != projectId) throw new InvalidOperationException("Timeline source media belongs to another project.");
        if (media.Duration is TimeSpan duration && sourceIn + sourceDuration > duration.TotalSeconds + 0.05)
        {
            throw new ArgumentException("Source trim range exceeds the source clip duration.");
        }
    }

    private static Guid RequireSongId(MusicVideoProject project) =>
        project.References.SingleOrDefault(reference => reference.Kind == ProjectReferenceKind.Song)?.ReferenceId
        ?? throw new InvalidOperationException("Attach the original Song before opening the timeline.");

    private static TimelineTransitionKind ParseTransition(string? value)
    {
        if (value?.Contains("cross", StringComparison.OrdinalIgnoreCase) == true) return TimelineTransitionKind.Crossfade;
        if (value?.Contains("fade", StringComparison.OrdinalIgnoreCase) == true) return TimelineTransitionKind.Fade;
        return TimelineTransitionKind.Cut;
    }

    private static IReadOnlyList<TimelineClip> NormalizeSequenceAndStarts(IReadOnlyList<TimelineClip> clips)
    {
        var start = 0d;
        var normalized = new List<TimelineClip>(clips.Count);
        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index] with { Sequence = index + 1, TimelineStartSeconds = start };
            normalized.Add(clip);
            start += clip.TimelineDurationSeconds;
        }
        return normalized;
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
