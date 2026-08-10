using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Application.Generation;

public interface IClipVariantRepository
{
    Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<SceneClipVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default);
    Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default);
}

public sealed class ClipVariantService
{
    private readonly IClipVariantRepository _variants;
    private readonly TimeProvider _timeProvider;

    public ClipVariantService(IClipVariantRepository variants, TimeProvider timeProvider)
    {
        _variants = variants;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<SceneClipVariant>> ListSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default) =>
        (await _variants.ListByProjectAsync(projectId, cancellationToken))
            .Where(variant => variant.SceneId == sceneId)
            .OrderBy(variant => variant.VariantNumber)
            .ToArray();

    public Task<SceneClipVariant?> GetAsync(
        Guid projectId,
        Guid variantId,
        CancellationToken cancellationToken = default) =>
        _variants.GetAsync(projectId, variantId, cancellationToken);

    public async Task<SceneClipVariant> RegisterPlannedAsync(
        Guid projectId,
        Guid sceneId,
        Guid promptVersionId,
        Guid startKeyframeVariantId,
        Guid? endKeyframeVariantId,
        string providerId,
        string modelId,
        TimeSpan duration,
        string aspectRatio,
        string resolution,
        decimal? estimatedCost,
        string currency,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var existing = await ListSceneAsync(projectId, sceneId, cancellationToken);
        var number = existing.Select(variant => variant.VariantNumber).DefaultIfEmpty(0).Max() + 1;
        var now = GetUtcNow();
        var variant = new SceneClipVariant(
            Guid.NewGuid(), projectId, sceneId, number, promptVersionId,
            startKeyframeVariantId, endKeyframeVariantId, null, null,
            providerId.Trim(), modelId.Trim(), GenerationVariantState.Planned, false,
            duration, aspectRatio.Trim(), resolution.Trim(), estimatedCost, null,
            NormalizeCurrency(currency), now, now);
        variant.Validate();
        await _variants.UpsertAsync(variant, cancellationToken);
        return variant;
    }

    public async Task<SceneClipVariant> AttachJobAsync(
        Guid projectId,
        Guid variantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("Job ID is required.", nameof(jobId));
        var existing = await RequireAsync(projectId, variantId, cancellationToken);
        if (existing.JobId is not null && existing.JobId != jobId)
        {
            throw new InvalidOperationException("Clip variant is already attached to another job.");
        }

        var updated = existing with
        {
            JobId = jobId,
            State = GenerationVariantState.Queued,
            UpdatedUtc = GetUtcNow(),
        };
        updated.Validate();
        await _variants.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<SceneClipVariant> CompleteAsync(
        Guid projectId,
        Guid variantId,
        Guid mediaAssetId,
        decimal? actualCost,
        CancellationToken cancellationToken = default)
    {
        if (mediaAssetId == Guid.Empty) throw new ArgumentException("Media asset ID is required.", nameof(mediaAssetId));
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

    public async Task<SceneClipVariant> MarkStateAsync(
        Guid projectId,
        Guid variantId,
        GenerationVariantState state,
        CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(projectId, variantId, cancellationToken);
        if (state == GenerationVariantState.Completed && existing.MediaAssetId is null)
        {
            throw new InvalidOperationException("CompleteAsync must attach the media asset before a clip can be completed.");
        }

        var updated = existing with { State = state, UpdatedUtc = GetUtcNow() };
        updated.Validate();
        await _variants.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<SceneClipVariant> UpdateProviderAsync(
        Guid projectId,
        Guid variantId,
        string providerId,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var existing = await RequireAsync(projectId, variantId, cancellationToken);
        var updated = existing with
        {
            ProviderId = providerId.Trim(),
            ModelId = modelId.Trim(),
            UpdatedUtc = GetUtcNow(),
        };
        updated.Validate();
        await _variants.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<SceneClipVariant> SelectAsync(
        Guid projectId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        var selected = await RequireAsync(projectId, variantId, cancellationToken);
        if (selected.State != GenerationVariantState.Completed || selected.MediaAssetId is null)
        {
            throw new InvalidOperationException("Only completed clip variants can be selected.");
        }

        var sceneVariants = await ListSceneAsync(projectId, selected.SceneId, cancellationToken);
        var now = GetUtcNow();
        SceneClipVariant? selectedResult = null;
        foreach (var variant in sceneVariants)
        {
            var shouldSelect = variant.Id == selected.Id;
            if (variant.IsSelected == shouldSelect)
            {
                if (shouldSelect) selectedResult = variant;
                continue;
            }

            var updated = variant with { IsSelected = shouldSelect, UpdatedUtc = now };
            await _variants.UpsertAsync(updated, cancellationToken);
            if (shouldSelect) selectedResult = updated;
        }

        return selectedResult ?? selected with { IsSelected = true, UpdatedUtc = now };
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
            throw new InvalidOperationException("Selected clip variants must be replaced before deletion.");
        }
        return await _variants.DeleteAsync(projectId, variantId, cancellationToken);
    }

    private async Task<SceneClipVariant> RequireAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken) =>
        await _variants.GetAsync(projectId, variantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Clip variant '{variantId}' was not found.");

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
