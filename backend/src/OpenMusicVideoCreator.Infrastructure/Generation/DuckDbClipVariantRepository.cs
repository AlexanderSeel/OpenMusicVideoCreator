using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class DuckDbClipVariantRepository : IClipVariantRepository
{
    private const string Key = "generation.clip-variants.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectSettingsRepository _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DuckDbClipVariantRepository(IProjectSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<SceneClipVariant>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken))
            .OrderBy(variant => variant.SceneId)
            .ThenBy(variant => variant.VariantNumber)
            .ToArray();

    public async Task<SceneClipVariant?> GetAsync(
        Guid projectId,
        Guid variantId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(projectId, cancellationToken)).FirstOrDefault(variant => variant.Id == variantId);

    public async Task UpsertAsync(SceneClipVariant variant, CancellationToken cancellationToken = default)
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

    public async Task<bool> DeleteAsync(
        Guid projectId,
        Guid variantId,
        CancellationToken cancellationToken = default)
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

    private async Task<IReadOnlyList<SceneClipVariant>> ReadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(projectId, Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<SceneClipVariant[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Clip variant persistence could not be deserialized.");
    }

    private Task WriteAsync(
        Guid projectId,
        IReadOnlyList<SceneClipVariant> variants,
        CancellationToken cancellationToken) =>
        _settings.SetAsync(projectId, Key, JsonSerializer.Serialize(variants, JsonOptions), cancellationToken);
}
