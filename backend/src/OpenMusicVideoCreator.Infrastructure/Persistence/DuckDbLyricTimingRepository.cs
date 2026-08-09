using System.Data.Common;
using System.Text.Json;
using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Domain.Analysis;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbLyricTimingRepository : ILyricTimingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbLyricTimingRepository(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<LyricTimingAnalysis?> GetLatestAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM lyric_timing_analyses
            WHERE project_id = $project_id
            ORDER BY version DESC
            LIMIT 1;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<LyricTimingAnalysis>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<LyricTimingAnalysis>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM lyric_timing_analyses
            WHERE project_id = $project_id
            ORDER BY version DESC;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Read(reader));
        }
        return result;
    }

    public async Task UpsertAsync(
        LyricTimingAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO lyric_timing_analyses(
                id, project_id, source_asset_id, song_analysis_id, version,
                supplied_lyrics_sha256, lines_json, created_utc
            ) VALUES (
                $id, $project_id, $source_asset_id, $song_analysis_id, $version,
                $supplied_lyrics_sha256, $lines_json, $created_utc
            )
            ON CONFLICT(id) DO UPDATE SET
                project_id = excluded.project_id,
                source_asset_id = excluded.source_asset_id,
                song_analysis_id = excluded.song_analysis_id,
                version = excluded.version,
                supplied_lyrics_sha256 = excluded.supplied_lyrics_sha256,
                lines_json = excluded.lines_json,
                created_utc = excluded.created_utc;
            """;
        command.Parameters.Add(new DuckDBParameter("id", analysis.Id));
        command.Parameters.Add(new DuckDBParameter("project_id", analysis.ProjectId));
        command.Parameters.Add(new DuckDBParameter("source_asset_id", analysis.SourceAssetId));
        command.Parameters.Add(new DuckDBParameter("song_analysis_id", analysis.SongAnalysisId));
        command.Parameters.Add(new DuckDBParameter("version", analysis.Version));
        command.Parameters.Add(new DuckDBParameter("supplied_lyrics_sha256", analysis.SuppliedLyricsSha256));
        command.Parameters.Add(new DuckDBParameter("lines_json", JsonSerializer.Serialize(analysis.Lines, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("created_utc", analysis.CreatedUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LyricTimingAnalysis Read(DbDataReader reader) => new(
        reader.GetFieldValue<Guid>(0),
        reader.GetFieldValue<Guid>(1),
        reader.GetFieldValue<Guid>(2),
        reader.GetFieldValue<Guid>(3),
        reader.GetInt32(4),
        reader.GetString(5),
        JsonSerializer.Deserialize<LyricTimingLine[]>(reader.GetString(6), JsonOptions) ?? [],
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)));

    private const string SelectColumns = """
        id, project_id, source_asset_id, song_analysis_id, version,
        supplied_lyrics_sha256, lines_json, created_utc
        """;
}
