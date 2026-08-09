using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Application.Generation;

public interface IKeyframeVariantRepository
{
    Task<IReadOnlyList<KeyframeVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<KeyframeVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default);
    Task UpsertAsync(KeyframeVariant variant, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default);
}

public sealed class KeyframeVariantService
{
    private readonly IKeyframeVariantRepository _variants;
    private readonly TimeProvider _timeProvider;

    public KeyframeVariantService(IKeyframeVariantRepository variants, TimeProvider timeProvider)
    {
        _variants = variants;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<KeyframeVariant>> ListSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default) =>
        (await _variants.ListByProjectAsync(projectId, cancellationToken))
            .Where(variant => variant.SceneId == sceneId)
            .OrderBy(variant => variant.Role)
            .ThenBy(variant => variant.VariantNumber)
            .ToArray();

    public async Task<KeyframeVariant> RegisterPlannedAsync(
        Guid projectId,
        Guid sceneId,
        KeyframeRole role,
        Guid promptVersionId,
        Guid jobId,
        string providerId,
        string modelId,
        decimal? estimatedCost,
        string currency,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var existing = await ListSceneAsync(projectId, sceneId, cancellationToken);
        var number = existing.Where(variant => variant.Role == role).Select(variant => variant.VariantNumber).DefaultIfEmpty(0).Max() + 1;
        var now = GetUtcNow();
        var variant = new KeyframeVariant(
            Guid.NewGuid(), projectId, sceneId, role, number, promptVersionId, jobId, null,
            providerId.Trim(), modelId.Trim(), GenerationVariantState.Queued, false,
            estimatedCost, null, NormalizeCurrency(currency), now, now);
        variant.Validate();
        await _variants.UpsertAsync(variant, cancellationToken);
        return variant;
    }

    public async Task<KeyframeVariant> CompleteAsync(
        Guid projectId,
        Guid variantId,
        Guid mediaAssetId,
        decimal? actualCost,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, variantId, cancellationToken);
        var updated = existing with
        {
            MediaAssetId = mediaAssetId,
            State = GenerationVariantState.Completed,
            ActualCost = actualCost,
            UpdatedUtc = GetUtcNow(),
        };
        updated.Validate();
        await _variants.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<KeyframeVariant> MarkStateAsync(
        Guid projectId,
        Guid variantId,
        GenerationVariantState state,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, variantId, cancellationToken);
        if (state == GenerationVariantState.Completed && existing.MediaAssetId is null)
        {
            throw new InvalidOperationException("CompleteAsync must attach the media asset before a variant can be completed.");
        }
        var updated = existing with { State = state, UpdatedUtc = GetUtcNow() };
        updated.Validate();
        await _variants.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<KeyframeVariant> SelectAsync(
        Guid projectId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        var selected = await RequireAsync(projectId, variantId, cancellationToken);
        if (selected.State != GenerationVariantState.Completed || selected.MediaAssetId is null)
        {
            throw new InvalidOperationException("Only completed keyframe variants can be selected.");
        }

        var sceneVariants = await ListSceneAsync(projectId, selected.SceneId, cancellationToken);
        foreach (var variant in sceneVariants.Where(variant => variant.Role == selected.Role))
        {
            var shouldSelect = variant.Id == selected.Id;
            if (variant.IsSelected == shouldSelect) continue;
            await _variants.UpsertAsync(variant with { IsSelected = shouldSelect, UpdatedUtc = GetUtcNow() }, cancellationToken);
        }
        return selected with { IsSelected = true, UpdatedUtc = GetUtcNow() };
    }

    public async Task<bool> DeleteAsync(
        Guid projectId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _variants.GetAsync(projectId, variantId, cancellationToken);
        if (existing is null) return false;
        if (existing.IsSelected)
        {
            throw new InvalidOperationException("Selected variants must be deselected by selecting another completed variant before deletion.");
        }
        return await _variants.DeleteAsync(projectId, variantId, cancellationToken);
    }

    private async Task<KeyframeVariant> RequireAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken) =>
        await _variants.GetAsync(projectId, variantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Keyframe variant '{variantId}' was not found.");

    private static string NormalizeCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return currency.Trim().ToUpperInvariant();
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
