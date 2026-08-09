using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Media;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbMediaAssetRepository : IMediaAssetRepository
{
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbMediaAssetRepository(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, project_id, location, checksum_sha256, mime_type, width, height,
                   duration_ms, file_size, creation_source, created_utc
            FROM media_assets
            WHERE id = $id;
            """;
        command.Parameters.Add(new DuckDBParameter("id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    public async Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var assets = new List<MediaAssetMetadata>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, project_id, location, checksum_sha256, mime_type, width, height,
                   duration_ms, file_size, creation_source, created_utc
            FROM media_assets
            WHERE project_id = $project_id
            ORDER BY created_utc, id;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assets.Add(ReadAsset(reader));
        }

        return assets;
    }

    public async Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.FileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(asset), "Media file size cannot be negative.");
        }

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO media_assets(
                id, project_id, location, checksum_sha256, mime_type, width, height,
                duration_ms, file_size, creation_source, created_utc
            ) VALUES (
                $id, $project_id, $location, $checksum, $mime_type, $width, $height,
                $duration_ms, $file_size, $creation_source, $created_utc
            );
            """;
        command.Parameters.Add(new DuckDBParameter("id", asset.Id));
        command.Parameters.Add(new DuckDBParameter("project_id", (object?)asset.ProjectId ?? DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("location", asset.Location));
        command.Parameters.Add(new DuckDBParameter("checksum", asset.ChecksumSha256));
        command.Parameters.Add(new DuckDBParameter("mime_type", asset.MimeType));
        command.Parameters.Add(new DuckDBParameter("width", (object?)asset.Width ?? DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("height", (object?)asset.Height ?? DBNull.Value));
        command.Parameters.Add(new DuckDBParameter(
            "duration_ms",
            asset.Duration.HasValue ? (object)(long)asset.Duration.Value.TotalMilliseconds : DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("file_size", asset.FileSize));
        command.Parameters.Add(new DuckDBParameter("creation_source", asset.CreationSource.ToString()));
        command.Parameters.Add(new DuckDBParameter("created_utc", asset.CreatedUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM media_assets WHERE id = $id;";
        command.Parameters.Add(new DuckDBParameter("id", id));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static MediaAssetMetadata ReadAsset(System.Data.Common.DbDataReader reader)
    {
        var creationSourceText = reader.GetString(9);
        if (!Enum.TryParse<MediaCreationSource>(creationSourceText, out var creationSource))
        {
            throw new InvalidDataException($"Unknown media creation source '{creationSourceText}'.");
        }

        return new MediaAssetMetadata(
            reader.GetFieldValue<Guid>(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<Guid>(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(7)),
            reader.GetInt64(8),
            creationSource,
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc)));
    }
}
