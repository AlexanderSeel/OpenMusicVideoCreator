namespace OpenMusicVideoCreator.Domain.Generation;

public sealed record SceneClipVariant(
    Guid Id,
    Guid ProjectId,
    Guid SceneId,
    int VariantNumber,
    Guid PromptVersionId,
    Guid StartKeyframeVariantId,
    Guid? EndKeyframeVariantId,
    Guid? JobId,
    Guid? MediaAssetId,
    string? ProviderId,
    string? ModelId,
    GenerationVariantState State,
    bool IsSelected,
    TimeSpan Duration,
    string AspectRatio,
    string Resolution,
    decimal? EstimatedCost,
    decimal? ActualCost,
    string Currency,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public void Validate()
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || SceneId == Guid.Empty || PromptVersionId == Guid.Empty ||
            StartKeyframeVariantId == Guid.Empty || VariantNumber <= 0)
        {
            throw new ArgumentException("Clip variant identity/provenance is invalid.");
        }

        if (EndKeyframeVariantId == StartKeyframeVariantId)
        {
            throw new ArgumentException("Start and end keyframe variants must be distinct.");
        }

        if (Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Clip duration must be positive.");
        }

        if (string.IsNullOrWhiteSpace(AspectRatio) || string.IsNullOrWhiteSpace(Resolution) || string.IsNullOrWhiteSpace(Currency))
        {
            throw new ArgumentException("Clip aspect ratio, resolution, and currency are required.");
        }

        if (State == GenerationVariantState.Completed && MediaAssetId is null)
        {
            throw new ArgumentException("Completed clip variants require a media asset.");
        }

        if (EstimatedCost is < 0 || ActualCost is < 0)
        {
            throw new ArgumentException("Generation costs cannot be negative.");
        }
    }
}

public sealed record SceneVideoGenerationSettings(
    Guid ProjectId,
    Guid SceneId,
    string? ProviderId,
    string? ModelId,
    bool UseEndFrame,
    string? Resolution,
    int? DurationSeconds,
    bool AllowFallback,
    DateTimeOffset UpdatedUtc)
{
    public void Validate()
    {
        if (ProjectId == Guid.Empty || SceneId == Guid.Empty)
        {
            throw new ArgumentException("Project and scene IDs are required.");
        }

        if ((ProviderId is null) != (ModelId is null))
        {
            throw new ArgumentException("Provider and model must either both be automatic or both be specified.");
        }

        if (DurationSeconds is <= 0)
        {
            throw new ArgumentException("Video generation duration must be positive when specified.");
        }

        if (Resolution is not null && !TryParseResolution(Resolution, out _, out _))
        {
            throw new ArgumentException("Video resolution must use WIDTHxHEIGHT format.");
        }
    }

    public static bool TryParseResolution(string value, out int width, out int height)
    {
        width = 0;
        height = 0;
        var parts = value.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0;
    }
}
