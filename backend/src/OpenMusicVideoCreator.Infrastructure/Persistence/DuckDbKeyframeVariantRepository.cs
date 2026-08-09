using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbKeyframeVariantRepository : IKeyframeVariantRepository
{
    private const string Key = "generation.keyframe-variants.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbKeyframeVariantRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<KeyframeVariant>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken))
            .OrderBy(variant => variant.SceneId)
            .ThenBy(variant => variant.Role)
            .ThenBy(variant => variant.VariantNumber)
            .ToArray();

    public async Task<KeyframeVariant?> GetAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken)).FirstOrDefault(variant => variant.Id == variantId);

    public async Task UpsertAsync(KeyframeVariant variant, CancellationToken cancellationToken = default)
    {
        variant.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(variant.ProjectId, cancellationToken)).ToList();
            current.RemoveAll(item => item.Id == variant.Id);
            current.Add(variant);
            await WriteAsync(variant.ProjectId, current, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = (await ReadAsync(projectId, cancellationToken)).ToList();
            var removed = current.RemoveAll(variant => variant.Id == variantId) > 0;
            if (removed) await WriteAsync(projectId, current, cancellationToken);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<KeyframeVariant>> ReadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<KeyframeVariant[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Keyframe variant persistence could not be deserialized.");
    }

    private Task WriteAsync(Guid projectId, IReadOnlyList<KeyframeVariant> variants, CancellationToken cancellationToken) =>
        _settings.SetAsync(projectId, Key, JsonSerializer.Serialize(variants, JsonOptions), cancellationToken);
}
