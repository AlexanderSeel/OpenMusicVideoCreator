using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Api.Contracts.Generation;

public sealed record KeyframeVariantResponse(
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
    public static KeyframeVariantResponse FromDomain(KeyframeVariant variant) => new(
        variant.Id,
        variant.ProjectId,
        variant.SceneId,
        variant.Role,
        variant.VariantNumber,
        variant.PromptVersionId,
        variant.JobId,
        variant.MediaAssetId,
        variant.ProviderId,
        variant.ModelId,
        variant.State,
        variant.IsSelected,
        variant.EstimatedCost,
        variant.ActualCost,
        variant.Currency,
        variant.CreatedUtc,
        variant.UpdatedUtc);
}

public sealed record KeyframeGenerationSettingsRequest(
    string? ProviderId,
    string? ModelId,
    bool GenerateEndFrame,
    string? Resolution,
    int? Seed,
    string? NegativePrompt);

public sealed record KeyframeGenerationSettingsResponse(
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
    public static KeyframeGenerationSettingsResponse FromDomain(SceneKeyframeGenerationSettings settings) => new(
        settings.ProjectId,
        settings.SceneId,
        settings.ProviderId,
        settings.ModelId,
        settings.GenerateEndFrame,
        settings.Resolution,
        settings.Seed,
        settings.NegativePrompt,
        settings.UpdatedUtc);
}

public sealed record KeyframeGenerateRequest(KeyframeRole? Role);

public sealed record KeyframeGenerationResponse(IReadOnlyList<KeyframeVariantResponse> Variants)
{
    public static KeyframeGenerationResponse FromDomain(IEnumerable<KeyframeVariant> variants) =>
        new(variants.Select(KeyframeVariantResponse.FromDomain).ToArray());
}

public sealed record KeyframeApprovalStatusResponse(
    bool IsApproved,
    Guid? StartVariantId,
    Guid? EndVariantId,
    DateTimeOffset? ApprovedUtc)
{
    public static KeyframeApprovalStatusResponse FromDomain(SceneKeyframeApproval? approval, bool isCurrentSelectionApproved) => new(
        isCurrentSelectionApproved,
        approval?.StartVariantId,
        approval?.EndVariantId,
        approval?.ApprovedUtc);
}
