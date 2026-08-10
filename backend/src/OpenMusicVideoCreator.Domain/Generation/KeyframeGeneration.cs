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

public sealed record SceneKeyframeGenerationSettings(
    Guid ProjectId,
    Guid SceneId,
    string? ProviderId,
    string? ModelId,
    bool GenerateEndFrame,
    string? Resolution,
    int? Seed,
    string? NegativePrompt,
    DateTimeOffset UpdatedUtc)
{
    public void Validate()
    {
        if (ProjectId == Guid.Empty || SceneId == Guid.Empty)
        {
            throw new ArgumentException("Scene keyframe settings require project and scene IDs.");
        }
        if ((ProviderId is null) != (ModelId is null))
        {
            throw new ArgumentException("Provider and model must either both be selected or both use automatic routing.");
        }
        if (ProviderId is not null && (string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(ModelId)))
        {
            throw new ArgumentException("Provider and model cannot be blank.");
        }
        if (Resolution is not null && !TryParseResolution(Resolution, out _, out _))
        {
            throw new ArgumentException("Resolution must use WIDTHxHEIGHT format.");
        }
    }

    public static bool TryParseResolution(string value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.ToLowerInvariant().Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0;
    }
}

public sealed record SceneKeyframeSelection(
    Guid ProjectId,
    Guid SceneId,
    Guid? StartVariantId,
    Guid? EndVariantId,
    DateTimeOffset UpdatedUtc);

public sealed record SceneKeyframeApproval(
    Guid ProjectId,
    Guid SceneId,
    Guid StartVariantId,
    Guid? EndVariantId,
    bool Approved,
    DateTimeOffset ApprovedUtc)
{
    public void Validate()
    {
        if (ProjectId == Guid.Empty || SceneId == Guid.Empty || StartVariantId == Guid.Empty)
        {
            throw new ArgumentException("Keyframe approval identity and selected start variant are required.");
        }

        if (!Approved)
        {
            throw new ArgumentException("Persisted keyframe approvals must be approved.");
        }

        if (EndVariantId == StartVariantId)
        {
            throw new ArgumentException("Start and end keyframe variants must be distinct.");
        }
    }
}
