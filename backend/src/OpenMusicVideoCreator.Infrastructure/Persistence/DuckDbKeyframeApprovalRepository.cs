using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbKeyframeApprovalRepository : IKeyframeApprovalRepository
{
    private const string Key = "generation.keyframe-approvals.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbKeyframeApprovalRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<SceneKeyframeApproval?> GetAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken)).FirstOrDefault(item => item.SceneId == sceneId);

    public async Task UpsertAsync(SceneKeyframeApproval approval, CancellationToken cancellationToken = default)
    {
        approval.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(approval.ProjectId, cancellationToken)).ToList();
            current.RemoveAll(item => item.SceneId == approval.SceneId);
            current.Add(approval);
            await WriteAsync(approval.ProjectId, current, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(projectId, cancellationToken)).ToList();
            var removed = current.RemoveAll(item => item.SceneId == sceneId) > 0;
            if (removed) await WriteAsync(projectId, current, cancellationToken);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<SceneKeyframeApproval>> ReadAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<SceneKeyframeApproval[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Keyframe approval persistence could not be deserialized.");
    }

    private Task WriteAsync(
        Guid projectId,
        IReadOnlyList<SceneKeyframeApproval> approvals,
        CancellationToken cancellationToken) =>
        _settings.SetAsync(projectId, Key, JsonSerializer.Serialize(approvals, JsonOptions), cancellationToken);
}
