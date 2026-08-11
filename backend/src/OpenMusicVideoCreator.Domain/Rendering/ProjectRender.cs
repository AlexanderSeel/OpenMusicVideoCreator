using OpenMusicVideoCreator.Domain.Timeline;

namespace OpenMusicVideoCreator.Domain.Rendering;

public enum ProjectRenderKind
{
    Preview,
    Final,
}

public enum ProjectRenderState
{
    Planned,
    Queued,
    Rendering,
    Completed,
    Failed,
    Cancelled,
}

public sealed record RenderTimelineClip(
    Guid SceneId,
    int Sequence,
    Guid ClipVariantId,
    Guid MediaAssetId,
    double TimelineStartSeconds,
    double DurationSeconds,
    string TransitionIn,
    double SourceInSeconds = 0,
    double? SourceDurationSeconds = null,
    double PlaybackRate = 1,
    double FreezeExtensionSeconds = 0,
    TimelineClipTransform? Transform = null,
    TimelineColorAdjustment? Color = null,
    TimelineTransitionKind? TransitionKind = null,
    double TransitionDurationSeconds = 0)
{
    public TimelineClipTransform ResolveTransform() => Transform ?? TimelineClipTransform.Default;
    public TimelineColorAdjustment ResolveColor() => Color ?? TimelineColorAdjustment.Neutral;

    public void Validate()
    {
        if (SceneId == Guid.Empty || ClipVariantId == Guid.Empty || MediaAssetId == Guid.Empty || Sequence <= 0 ||
            !double.IsFinite(TimelineStartSeconds) || TimelineStartSeconds < 0 ||
            !double.IsFinite(DurationSeconds) || DurationSeconds <= 0 ||
            !double.IsFinite(SourceInSeconds) || SourceInSeconds < 0 ||
            SourceDurationSeconds is double sourceDuration && (!double.IsFinite(sourceDuration) || sourceDuration <= 0) ||
            !double.IsFinite(PlaybackRate) || PlaybackRate is < 0.5 or > 2 ||
            !double.IsFinite(FreezeExtensionSeconds) || FreezeExtensionSeconds < 0 ||
            !double.IsFinite(TransitionDurationSeconds) || TransitionDurationSeconds < 0 || TransitionDurationSeconds > Math.Min(2, DurationSeconds / 2))
        {
            throw new ArgumentException("Render timeline clip contains invalid identity, timing, or edit data.");
        }
        ResolveTransform().Validate();
        ResolveColor().Validate();
        if ((TransitionKind ?? TimelineTransitionKind.Cut) == TimelineTransitionKind.Cut && TransitionDurationSeconds > 0.0001)
        {
            throw new ArgumentException("Cut render transitions cannot have a duration.");
        }
    }
}

public sealed record ProjectRenderManifest(
    Guid ProjectId,
    Guid StoryboardVersionId,
    Guid SongMediaAssetId,
    ProjectRenderKind Kind,
    int Width,
    int Height,
    int FramesPerSecond,
    IReadOnlyList<RenderTimelineClip> Clips,
    double DurationSeconds,
    string TimelineSha256,
    Guid? TimelineVersionId = null,
    IReadOnlyList<TimelineOverlay>? Overlays = null,
    IReadOnlyList<TimelineEffect>? Effects = null,
    IReadOnlyList<TimelineSubtitle>? Subtitles = null)
{
    public IReadOnlyList<TimelineOverlay> ResolveOverlays() => Overlays ?? [];
    public IReadOnlyList<TimelineEffect> ResolveEffects() => Effects ?? [];
    public IReadOnlyList<TimelineSubtitle> ResolveSubtitles() => Subtitles ?? [];

    public void Validate()
    {
        if (ProjectId == Guid.Empty || StoryboardVersionId == Guid.Empty || SongMediaAssetId == Guid.Empty ||
            Width <= 0 || Height <= 0 || FramesPerSecond <= 0 ||
            !double.IsFinite(DurationSeconds) || DurationSeconds <= 0 ||
            string.IsNullOrWhiteSpace(TimelineSha256) || Clips.Count == 0)
        {
            throw new ArgumentException("Render manifest is incomplete.");
        }

        var ordered = Clips.OrderBy(clip => clip.Sequence).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var clip = ordered[index];
            clip.Validate();
            if (clip.Sequence != index + 1)
            {
                throw new ArgumentException("Render timeline clip sequence must be contiguous.");
            }

            if (index == 0 && Math.Abs(clip.TimelineStartSeconds) > 0.001)
            {
                throw new ArgumentException("Render timeline must begin at zero.");
            }

            if (index > 0)
            {
                var previousEnd = ordered[index - 1].TimelineStartSeconds + ordered[index - 1].DurationSeconds;
                if (Math.Abs(previousEnd - clip.TimelineStartSeconds) > 0.002)
                {
                    throw new ArgumentException("Render timeline clips must be contiguous.");
                }
            }
        }

        var finalEnd = ordered[^1].TimelineStartSeconds + ordered[^1].DurationSeconds;
        if (Math.Abs(finalEnd - DurationSeconds) > 0.002)
        {
            throw new ArgumentException("Render duration must match the final timeline boundary.");
        }

        foreach (var overlay in ResolveOverlays()) overlay.Validate(DurationSeconds);
        foreach (var effect in ResolveEffects()) effect.Validate(DurationSeconds);
        foreach (var subtitle in ResolveSubtitles()) subtitle.Validate(DurationSeconds);
    }
}

public sealed record ProjectRenderAttempt(
    int AttemptNumber,
    ProjectRenderState State,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? CommandLog,
    string? ErrorMessage)
{
    public void Validate()
    {
        if (AttemptNumber <= 0)
        {
            throw new ArgumentException("Render attempt number must be positive.");
        }

        if (CompletedUtc is not null && CompletedUtc < StartedUtc)
        {
            throw new ArgumentException("Render attempt completion cannot precede its start.");
        }
    }
}

public sealed record ProjectRenderRecord(
    Guid Id,
    Guid ProjectId,
    int Version,
    ProjectRenderManifest Manifest,
    Guid? JobId,
    Guid? OutputMediaAssetId,
    ProjectRenderState State,
    string? CommandLog,
    string? ErrorMessage,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<ProjectRenderAttempt>? Attempts = null)
{
    public IReadOnlyList<ProjectRenderAttempt> ResolveAttempts() => Attempts ?? [];

    public void Validate()
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || Version <= 0 || Manifest.ProjectId != ProjectId)
        {
            throw new ArgumentException("Render record identity/version is invalid.");
        }

        Manifest.Validate();
        if (State == ProjectRenderState.Completed && OutputMediaAssetId is null)
        {
            throw new ArgumentException("Completed renders require an output media asset.");
        }

        var attempts = ResolveAttempts();
        for (var index = 0; index < attempts.Count; index++)
        {
            attempts[index].Validate();
            if (attempts[index].AttemptNumber != index + 1)
            {
                throw new ArgumentException("Render attempt numbers must be contiguous.");
            }
        }
    }
}
