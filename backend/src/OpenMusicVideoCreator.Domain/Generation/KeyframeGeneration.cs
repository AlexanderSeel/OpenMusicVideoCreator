namespace OpenMusicVideoCreator.Domain.Generation;

public enum KeyframeRole
{
    Start,
    End,
}

public enum GenerationVariantState
{
    Planned,
    Queued,
    Generating,
    Completed,
    Failed,
    Cancelled,
}

public sealed record KeyframeVariant(
    Guid Id,
    Guid ProjectId,
    Guid SceneId,
    KeyframeRole Role,
    int VariantNumber,
    Guid PromptVersionId,
    Guid? JobId,
    Guid? MediaAssetId,
    string? ProviderId,
    string? ModelId,
    GenerationVariantState State,
    bool IsSelected,
    decimal? EstimatedCost,
    decimal? ActualCost,
    string Currency,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public void Validate()
    {
        if (Id == Guid.Empty || ProjectId == Guid.Empty || SceneId == Guid.Empty || PromptVersionId == Guid.Empty || VariantNumber <= 0)
        {
            throw new ArgumentException("Keyframe variant identity/provenance is invalid.");
        }
        if (State == GenerationVariantState.Completed && MediaAssetId is null)
        {
            throw new ArgumentException("Completed keyframe variants require a media asset.");
        }
        if (EstimatedCost is < 0 || ActualCost is < 0)
        {
            throw new ArgumentException("Generation costs cannot be negative.");
        }
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new ArgumentException("Generation currency is required.");
        }
    }
}

public sealed record SceneKeyframeSelection(
    Guid ProjectId,
    Guid SceneId,
    Guid? StartVariantId,
    Guid? EndVariantId,
    DateTimeOffset UpdatedUtc);
