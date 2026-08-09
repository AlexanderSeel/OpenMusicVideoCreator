using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Library;
using OpenMusicVideoCreator.Domain.Library;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbVisualLibraryRepository : IVisualLibraryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbVisualLibraryRepository(DuckDbConnectionFactory connections) => _connections = connections;

    public async Task<IReadOnlyList<VisualLibraryItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<VisualLibraryItem>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM visual_library_items ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadVisualItem(reader));
        }
        return result;
    }

    public async Task<VisualLibraryItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM visual_library_items WHERE id = $id;";
        command.Parameters.Add(new DuckDBParameter("id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVisualItem(reader) : null;
    }

    public async Task UpsertAsync(VisualLibraryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var payload = new VisualPayload(item.Character, item.Style, item.Location);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO visual_library_items(
                id, kind, name, description, tags_json, is_favorite, asset_entry_ids_json,
                payload_json, created_utc, updated_utc
            ) VALUES (
                $id, $kind, $name, $description, $tags, $favorite, $assets,
                $payload, $created, $updated
            )
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                name = excluded.name,
                description = excluded.description,
                tags_json = excluded.tags_json,
                is_favorite = excluded.is_favorite,
                asset_entry_ids_json = excluded.asset_entry_ids_json,
                payload_json = excluded.payload_json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.Add(new DuckDBParameter("id", item.Id));
        command.Parameters.Add(new DuckDBParameter("kind", item.Kind.ToString()));
        command.Parameters.Add(new DuckDBParameter("name", item.Name));
        command.Parameters.Add(new DuckDBParameter("description", item.Description));
        command.Parameters.Add(new DuckDBParameter("tags", JsonSerializer.Serialize(item.Tags, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("favorite", item.IsFavorite));
        command.Parameters.Add(new DuckDBParameter("assets", JsonSerializer.Serialize(item.AssetEntryIds, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("payload", JsonSerializer.Serialize(payload, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("created", item.CreatedUtc.UtcDateTime));
        command.Parameters.Add(new DuckDBParameter("updated", item.UpdatedUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM visual_library_items WHERE id = $id;";
        command.Parameters.Add(new DuckDBParameter("id", id));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static VisualLibraryItem ReadVisualItem(DbDataReader reader)
    {
        var kind = Enum.Parse<VisualLibraryKind>(reader.GetString(1), ignoreCase: false);
        var payload = JsonSerializer.Deserialize<VisualPayload>(reader.GetString(7), JsonOptions)
            ?? throw new InvalidDataException("Visual library payload could not be deserialized.");
        return new VisualLibraryItem(
            reader.GetFieldValue<Guid>(0),
            kind,
            reader.GetString(2),
            reader.GetString(3),
            DeserializeArray<string>(reader.GetString(4)),
            reader.GetBoolean(5),
            DeserializeArray<Guid>(reader.GetString(6)),
            payload.Character,
            payload.Style,
            payload.Location,
            ReadUtc(reader, 8),
            ReadUtc(reader, 9));
    }

    private const string SelectColumns = """
        id, kind, name, description, tags_json, is_favorite,
        asset_entry_ids_json, payload_json, created_utc, updated_utc
        """;

    private sealed record VisualPayload(
        CharacterLibraryData? Character,
        StyleLibraryData? Style,
        LocationLibraryData? Location);

    internal static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    internal static IReadOnlyList<T> DeserializeArray<T>(string json) =>
        JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];

    internal static DateTimeOffset ReadUtc(DbDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}

public sealed class DuckDbAssetLibraryRepository : IAssetLibraryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = DuckDbVisualLibraryRepository.CreateJsonOptions();
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbAssetLibraryRepository(DuckDbConnectionFactory connections) => _connections = connections;

    public async Task<IReadOnlyList<AssetLibraryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<AssetLibraryEntry>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM library_assets ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<AssetLibraryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM library_assets WHERE id = $id;";
        command.Parameters.Add(new DuckDBParameter("id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task UpsertAsync(AssetLibraryEntry item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO library_assets(
                id, media_asset_id, preview_media_asset_id, name, tags_json, is_favorite,
                source_description, created_utc, updated_utc
            ) VALUES ($id, $media, $preview, $name, $tags, $favorite, $source, $created, $updated)
            ON CONFLICT(id) DO UPDATE SET
                media_asset_id = excluded.media_asset_id,
                preview_media_asset_id = excluded.preview_media_asset_id,
                name = excluded.name,
                tags_json = excluded.tags_json,
                is_favorite = excluded.is_favorite,
                source_description = excluded.source_description,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.Add(new DuckDBParameter("id", item.Id));
        command.Parameters.Add(new DuckDBParameter("media", item.MediaAssetId));
        command.Parameters.Add(new DuckDBParameter("preview", item.PreviewMediaAssetId.HasValue ? item.PreviewMediaAssetId.Value : DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("name", item.Name));
        command.Parameters.Add(new DuckDBParameter("tags", JsonSerializer.Serialize(item.Tags, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("favorite", item.IsFavorite));
        command.Parameters.Add(new DuckDBParameter("source", item.SourceDescription));
        command.Parameters.Add(new DuckDBParameter("created", item.CreatedUtc.UtcDateTime));
        command.Parameters.Add(new DuckDBParameter("updated", item.UpdatedUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM library_assets WHERE id = $id;";
        command.Parameters.Add(new DuckDBParameter("id", id));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static AssetLibraryEntry Read(DbDataReader reader) => new(
        reader.GetFieldValue<Guid>(0),
        reader.GetFieldValue<Guid>(1),
        reader.IsDBNull(2) ? null : reader.GetFieldValue<Guid>(2),
        reader.GetString(3),
        JsonSerializer.Deserialize<string[]>(reader.GetString(4), JsonOptions) ?? [],
        reader.GetBoolean(5),
        reader.GetString(6),
        DuckDbVisualLibraryRepository.ReadUtc(reader, 7),
        DuckDbVisualLibraryRepository.ReadUtc(reader, 8));

    private const string SelectColumns = """
        id, media_asset_id, preview_media_asset_id, name, tags_json, is_favorite,
        source_description, created_utc, updated_utc
        """;
}

public sealed class DuckDbProjectCharacterStateRepository : IProjectCharacterStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = DuckDbVisualLibraryRepository.CreateJsonOptions();
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbProjectCharacterStateRepository(DuckDbConnectionFactory connections) => _connections = connections;

    public async Task<IReadOnlyList<ProjectCharacterState>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ProjectCharacterState>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM project_character_states WHERE project_id = $project ORDER BY character_id;";
        command.Parameters.Add(new DuckDBParameter("project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<ProjectCharacterState?> GetAsync(Guid projectId, Guid characterId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM project_character_states WHERE project_id = $project AND character_id = $character;";
        command.Parameters.Add(new DuckDBParameter("project", projectId));
        command.Parameters.Add(new DuckDBParameter("character", characterId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task UpsertAsync(ProjectCharacterState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProjectCharacterState.ValidateStateValues(state.StateValues);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_character_states(project_id, character_id, outfit_id, locks_json, state_values_json, updated_utc)
            VALUES ($project, $character, $outfit, $locks, $values, $updated)
            ON CONFLICT(project_id, character_id) DO UPDATE SET
                outfit_id = excluded.outfit_id,
                locks_json = excluded.locks_json,
                state_values_json = excluded.state_values_json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.Add(new DuckDBParameter("project", state.ProjectId));
        command.Parameters.Add(new DuckDBParameter("character", state.CharacterId));
        command.Parameters.Add(new DuckDBParameter("outfit", state.OutfitId.HasValue ? state.OutfitId.Value : DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("locks", JsonSerializer.Serialize(state.Locks, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("values", JsonSerializer.Serialize(state.StateValues, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("updated", state.UpdatedUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid characterId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM project_character_states WHERE project_id = $project AND character_id = $character;";
        command.Parameters.Add(new DuckDBParameter("project", projectId));
        command.Parameters.Add(new DuckDBParameter("character", characterId));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static ProjectCharacterState Read(DbDataReader reader) => new(
        reader.GetFieldValue<Guid>(0),
        reader.GetFieldValue<Guid>(1),
        reader.IsDBNull(2) ? null : reader.GetFieldValue<Guid>(2),
        JsonSerializer.Deserialize<CharacterContinuityLocks>(reader.GetString(3), JsonOptions)
            ?? throw new InvalidDataException("Character continuity locks could not be deserialized."),
        JsonSerializer.Deserialize<Dictionary<string, double>>(reader.GetString(4), JsonOptions)
            ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        DuckDbVisualLibraryRepository.ReadUtc(reader, 5));

    private const string SelectColumns = "project_id, character_id, outfit_id, locks_json, state_values_json, updated_utc";
}
