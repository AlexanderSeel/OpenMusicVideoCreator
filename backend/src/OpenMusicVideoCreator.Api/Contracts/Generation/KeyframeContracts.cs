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
