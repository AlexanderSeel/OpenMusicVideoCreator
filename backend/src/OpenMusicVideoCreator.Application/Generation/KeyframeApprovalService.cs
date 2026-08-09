using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Application.Generation;

public interface IKeyframeApprovalRepository
{
    Task<SceneKeyframeApproval?> GetAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task UpsertAsync(SceneKeyframeApproval approval, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default);
}

public sealed class KeyframeApprovalService
{
    private readonly IKeyframeVariantRepository _variants;
    private readonly IKeyframeApprovalRepository _approvals;
    private readonly TimeProvider _timeProvider;

    public KeyframeApprovalService(
        IKeyframeVariantRepository variants,
        IKeyframeApprovalRepository approvals,
        TimeProvider timeProvider)
    {
        _variants = variants;
        _approvals = approvals;
        _timeProvider = timeProvider;
    }

    public Task<SceneKeyframeApproval?> GetAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default) =>
        _approvals.GetAsync(projectId, sceneId, cancellationToken);

    public async Task<SceneKeyframeApproval> ApproveAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var sceneVariants = (await _variants.ListByProjectAsync(projectId, cancellationToken))
            .Where(variant => variant.SceneId == sceneId)
            .ToArray();
        var start = sceneVariants.SingleOrDefault(variant =>
            variant.Role == KeyframeRole.Start &&
            variant.IsSelected &&
            variant.State == GenerationVariantState.Completed &&
            variant.MediaAssetId is not null)
            ?? throw new InvalidOperationException("Select a completed Start keyframe before approval.");
        var end = sceneVariants.SingleOrDefault(variant =>
            variant.Role == KeyframeRole.End &&
            variant.IsSelected &&
            variant.State == GenerationVariantState.Completed &&
            variant.MediaAssetId is not null);

        var approval = new SceneKeyframeApproval(
            projectId,
            sceneId,
            start.Id,
            end?.Id,
            Approved: true,
            GetUtcNow());
        approval.Validate();
        await _approvals.UpsertAsync(approval, cancellationToken);
        return approval;
    }

    public Task<bool> RevokeAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default) =>
        _approvals.DeleteAsync(projectId, sceneId, cancellationToken);

    public async Task<bool> IsCurrentSelectionApprovedAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var approval = await _approvals.GetAsync(projectId, sceneId, cancellationToken);
        if (approval is null) return false;

        var sceneVariants = (await _variants.ListByProjectAsync(projectId, cancellationToken))
            .Where(variant => variant.SceneId == sceneId && variant.IsSelected)
            .ToArray();
        var selectedStart = sceneVariants.SingleOrDefault(variant => variant.Role == KeyframeRole.Start);
        var selectedEnd = sceneVariants.SingleOrDefault(variant => variant.Role == KeyframeRole.End);
        return selectedStart?.Id == approval.StartVariantId && selectedEnd?.Id == approval.EndVariantId;
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
