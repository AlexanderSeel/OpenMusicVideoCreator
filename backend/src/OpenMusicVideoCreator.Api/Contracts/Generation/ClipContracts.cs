using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Api.Contracts.Generation;

public sealed record ClipVariantResponse(
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
    double DurationSeconds,
    string AspectRatio,
    string Resolution,
    decimal? EstimatedCost,
    decimal? ActualCost,
    string Currency,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static ClipVariantResponse FromDomain(SceneClipVariant variant) => new(
        variant.Id,
        variant.ProjectId,
        variant.SceneId,
        variant.VariantNumber,
        variant.PromptVersionId,
        variant.StartKeyframeVariantId,
        variant.EndKeyframeVariantId,
        variant.JobId,
        variant.MediaAssetId,
        variant.ProviderId,
        variant.ModelId,
        variant.State,
        variant.IsSelected,
        variant.Duration.TotalSeconds,
        variant.AspectRatio,
        variant.Resolution,
        variant.EstimatedCost,
        variant.ActualCost,
        variant.Currency,
        variant.CreatedUtc,
        variant.UpdatedUtc);
}

public sealed record VideoGenerationSettingsRequest(
    string? ProviderId,
    string? ModelId,
    bool UseEndFrame,
    string? Resolution,
    int? DurationSeconds,
    bool AllowFallback);

public sealed record VideoGenerationSettingsResponse(
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
    public static VideoGenerationSettingsResponse FromDomain(SceneVideoGenerationSettings settings) => new(
        settings.ProjectId,
        settings.SceneId,
        settings.ProviderId,
        settings.ModelId,
        settings.UseEndFrame,
        settings.Resolution,
        settings.DurationSeconds,
        settings.AllowFallback,
        settings.UpdatedUtc);
}

public sealed record ClipGenerationResponse(ClipVariantResponse Variant)
{
    public static ClipGenerationResponse FromDomain(SceneClipVariant variant) =>
        new(ClipVariantResponse.FromDomain(variant));
}
