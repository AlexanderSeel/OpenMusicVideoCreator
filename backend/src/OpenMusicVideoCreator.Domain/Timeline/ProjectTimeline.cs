namespace OpenMusicVideoCreator.Domain.Timeline;

public enum TimelineTransitionKind
{
    Cut,
    Fade,
    Crossfade,
}

public enum TimelineEffectKind
{
    FadeToBlack,
    Vignette,
    Grayscale,
}

public sealed record TimelineClipTransform(
    double Scale,
    double PositionX,
    double PositionY,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom,
    double Opacity)
{
    public static TimelineClipTransform Default { get; } = new(1, 0, 0, 0, 0, 0, 0, 1);

    public void Validate()
    {
        if (!double.IsFinite(Scale) || Scale is < 0.25 or > 4 ||
            !NormalizedSigned(PositionX) || !NormalizedSigned(PositionY) ||
            !Crop(CropLeft) || !Crop(CropTop) || !Crop(CropRight) || !Crop(CropBottom) ||
            CropLeft + CropRight >= 0.95 || CropTop + CropBottom >= 0.95 ||
            !double.IsFinite(Opacity) || Opacity is < 0 or > 1)
        {
            throw new ArgumentException("Timeline transform values are outside supported bounds.");
        }
    }

    private static bool NormalizedSigned(double value) => double.IsFinite(value) && value is >= -1 and <= 1;
    private static bool Crop(double value) => double.IsFinite(value) && value is >= 0 and < 0.95;
}

public sealed record TimelineColorAdjustment(
    double Brightness,
    double Contrast,
    double Saturation)
{
    public static TimelineColorAdjustment Neutral { get; } = new(0, 1, 1);

    public void Validate()
    {
        if (!double.IsFinite(Brightness) || Brightness is < -1 or > 1 ||
            !double.IsFinite(Contrast) || Contrast is < 0 or > 2 ||
            !double.IsFinite(Saturation) || Saturation is < 0 or > 3)
        {
            throw new ArgumentException("Timeline color adjustment is outside supported bounds.");
        }
    }
}

public sealed record TimelineClip(
    Guid Id,
    Guid SceneId,
    int Sequence,
    Guid ClipVariantId,
    Guid MediaAssetId,
    double TimelineStartSeconds,
    double TimelineDurationSeconds,
    double SourceInSeconds,
    double SourceDurationSeconds,
    double PlaybackRate,
    double FreezeExtensionSeconds,
    TimelineTransitionKind TransitionIn,
    double TransitionDurationSeconds,
    TimelineClipTransform Transform,
    TimelineColorAdjustment Color)
{
    public double TimelineEndSeconds => TimelineStartSeconds + TimelineDurationSeconds;

    public void Validate()
    {
        if (Id == Guid.Empty || SceneId == Guid.Empty || ClipVariantId == Guid.Empty || MediaAssetId == Guid.Empty || Sequence <= 0 ||
            !FiniteNonNegative(TimelineStartSeconds) || !FinitePositive(TimelineDurationSeconds) ||
            !FiniteNonNegative(SourceInSeconds) || !FinitePositive(SourceDurationSeconds) ||
            !double.IsFinite(PlaybackRate) || PlaybackRate is < 0.5 or > 2 ||
            !FiniteNonNegative(FreezeExtensionSeconds) ||
            !FiniteNonNegative(TransitionDurationSeconds) || TransitionDurationSeconds > Math.Min(2, TimelineDurationSeconds / 2))
        {
            throw new ArgumentException("Timeline clip identity/timing values are invalid.");
        }
        if (TransitionIn == TimelineTransitionKind.Cut && TransitionDurationSeconds > 0.0001)
        {
            throw new ArgumentException("Cut transitions cannot have a transition duration.");
        }
        Transform.Validate();
        Color.Validate();
    }

    private static bool FiniteNonNegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool FinitePositive(double value) => double.IsFinite(value) && value > 0;
}

public sealed record TimelineOverlay(
    Guid Id,
    Guid MediaAssetId,
    double StartSeconds,
    double EndSeconds,
    double PositionX,
    double PositionY,
    double Scale,
    double Opacity)
{
    public void Validate(double timelineDurationSeconds)
    {
        if (Id == Guid.Empty || MediaAssetId == Guid.Empty ||
            !double.IsFinite(StartSeconds) || !double.IsFinite(EndSeconds) ||
            StartSeconds < 0 || EndSeconds <= StartSeconds || EndSeconds > timelineDurationSeconds + 0.001 ||
            !double.IsFinite(PositionX) || PositionX is < -1 or > 1 ||
            !double.IsFinite(PositionY) || PositionY is < -1 or > 1 ||
            !double.IsFinite(Scale) || Scale is < 0.1 or > 4 ||
            !double.IsFinite(Opacity) || Opacity is < 0 or > 1)
        {
            throw new ArgumentException("Timeline overlay values are invalid.");
        }
    }
}

public sealed record TimelineEffect(
    Guid Id,
    TimelineEffectKind Kind,
    double StartSeconds,
    double EndSeconds,
    double Strength)
{
    public void Validate(double timelineDurationSeconds)
    {
        if (Id == Guid.Empty || !double.IsFinite(StartSeconds) || !double.IsFinite(EndSeconds) ||
            StartSeconds < 0 || EndSeconds <= StartSeconds || EndSeconds > timelineDurationSeconds + 0.001 ||
            !double.IsFinite(Strength) || Strength is < 0 or > 1)
        {
            throw new ArgumentException("Timeline effect values are invalid.");
        }
    }
}

public sealed record TimelineSubtitle(
    Guid Id,
    string Text,
    double StartSeconds,
    double EndSeconds,
    double PositionY,
    double Size,
    double Opacity)
{
    public void Validate(double timelineDurationSeconds)
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Text) || Text.Length > 500 || Text.Contains('\0') ||
            !double.IsFinite(StartSeconds) || !double.IsFinite(EndSeconds) ||
            StartSeconds < 0 || EndSeconds <= StartSeconds || EndSeconds > timelineDurationSeconds + 0.001 ||
            !double.IsFinite(PositionY) || PositionY is < -1 or > 1 ||
            !double.IsFinite(Size) || Size is < 0.5 or > 2 ||
            !double.IsFinite(Opacity) || Opacity is < 0 or > 1)
        {
            throw new ArgumentException("Timeline subtitle values are invalid.");
        }
    }
}

public sealed record ProjectTimelineVersion(
    Guid Id,
    Guid ProjectId,
    Guid StoryboardVersionId,
    Guid SongMediaAssetId,
    int Version,
    Guid? ParentVersionId,
    bool MusicTrackLocked,
    IReadOnlyList<TimelineClip> Clips,
    IReadOnlyList<TimelineOverlay> Overlays,
    IReadOnlyList<TimelineEffect> Effects,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<TimelineSubtitle>? Subtitles = null)
{
    public double DurationSeconds => Clips.Count == 0 ? 0 : Clips.Max(clip => clip.TimelineEndSeconds);
    public IReadOnlyList<TimelineSubtitle> ResolveSubtitles() => Subtitles ?? [];

    public void Validate()
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || StoryboardVersionId == Guid.Empty || SongMediaAssetId == Guid.Empty || Version <= 0)
        {
            throw new ArgumentException("Timeline identity/version is invalid.");
        }
        if (!MusicTrackLocked)
        {
            throw new ArgumentException("The original music track must remain protected in Block 12 timeline versions.");
        }
        if (Clips.Count == 0)
        {
            throw new ArgumentException("Timeline requires at least one clip.");
        }

        var ordered = Clips.OrderBy(clip => clip.Sequence).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var clip = ordered[index];
            clip.Validate();
            if (clip.Sequence != index + 1)
            {
                throw new ArgumentException("Timeline clip sequence must be contiguous.");
            }
            if (index == 0 && Math.Abs(clip.TimelineStartSeconds) > 0.001)
            {
                throw new ArgumentException("Timeline must begin at zero.");
            }
            if (index > 0 && Math.Abs(ordered[index - 1].TimelineEndSeconds - clip.TimelineStartSeconds) > 0.002)
            {
                throw new ArgumentException("Timeline clips must remain contiguous.");
            }
        }

        var duplicateIds = Clips.GroupBy(clip => clip.Id).Any(group => group.Count() > 1);
        if (duplicateIds) throw new ArgumentException("Timeline clip IDs must be unique.");

        foreach (var overlay in Overlays) overlay.Validate(DurationSeconds);
        foreach (var effect in Effects) effect.Validate(DurationSeconds);
        foreach (var subtitle in ResolveSubtitles()) subtitle.Validate(DurationSeconds);
    }
}
