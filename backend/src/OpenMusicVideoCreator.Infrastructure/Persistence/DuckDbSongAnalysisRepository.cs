using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Domain.Analysis;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbSongAnalysisRepository : ISongAnalysisRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DuckDbConnectionFactory _connections;

    public DuckDbSongAnalysisRepository(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<SongAnalysis?> GetLatestAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM song_analyses
            WHERE project_id = $project_id
            ORDER BY version DESC
            LIMIT 1;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<SongAnalysis?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM song_analyses
            WHERE id = $id;
            """;
        command.Parameters.Add(new DuckDBParameter("id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<SongAnalysis>> ListVersionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SongAnalysis>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM song_analyses
            WHERE project_id = $project_id
            ORDER BY version DESC;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    public async Task UpsertAsync(SongAnalysis analysis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        SongAnalysis.ValidateSections(analysis.DurationSeconds, analysis.Sections);

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO song_analyses(
                id, project_id, source_asset_id, version, duration_seconds, bpm,
                sample_rate, channels, codec, bit_rate, waveform_json, energy_json,
                beats_json, sections_json, created_utc
            ) VALUES (
                $id, $project_id, $source_asset_id, $version, $duration_seconds, $bpm,
                $sample_rate, $channels, $codec, $bit_rate, $waveform_json, $energy_json,
                $beats_json, $sections_json, $created_utc
            )
            ON CONFLICT(id) DO UPDATE SET
                project_id = excluded.project_id,
                source_asset_id = excluded.source_asset_id,
                version = excluded.version,
                duration_seconds = excluded.duration_seconds,
                bpm = excluded.bpm,
                sample_rate = excluded.sample_rate,
                channels = excluded.channels,
                codec = excluded.codec,
                bit_rate = excluded.bit_rate,
                waveform_json = excluded.waveform_json,
                energy_json = excluded.energy_json,
                beats_json = excluded.beats_json,
                sections_json = excluded.sections_json,
                created_utc = excluded.created_utc;
            """;
        command.Parameters.Add(new DuckDBParameter("id", analysis.Id));
        command.Parameters.Add(new DuckDBParameter("project_id", analysis.ProjectId));
        command.Parameters.Add(new DuckDBParameter("source_asset_id", analysis.SourceAssetId));
        command.Parameters.Add(new DuckDBParameter("version", analysis.Version));
        command.Parameters.Add(new DuckDBParameter("duration_seconds", analysis.DurationSeconds));
        command.Parameters.Add(new DuckDBParameter("bpm", DbValue(analysis.Bpm)));
        command.Parameters.Add(new DuckDBParameter("sample_rate", DbValue(analysis.SampleRate)));
        command.Parameters.Add(new DuckDBParameter("channels", DbValue(analysis.Channels)));
        command.Parameters.Add(new DuckDBParameter("codec", DbValue(analysis.Codec)));
        command.Parameters.Add(new DuckDBParameter("bit_rate", DbValue(analysis.BitRate)));
        command.Parameters.Add(new DuckDBParameter("waveform_json", JsonSerializer.Serialize(analysis.Waveform, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("energy_json", JsonSerializer.Serialize(analysis.Energy, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("beats_json", JsonSerializer.Serialize(analysis.Beats, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("sections_json", JsonSerializer.Serialize(analysis.Sections, JsonOptions)));
        command.Parameters.Add(new DuckDBParameter("created_utc", analysis.CreatedUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SongAnalysis Read(DbDataReader reader) => new(
        reader.GetFieldValue<Guid>(0),
        reader.GetFieldValue<Guid>(1),
        reader.GetFieldValue<Guid>(2),
        reader.GetInt32(3),
        reader.GetDouble(4),
        reader.IsDBNull(5) ? null : reader.GetDouble(5),
        reader.IsDBNull(6) ? null : reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetInt32(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetInt64(9),
        Deserialize<WaveformBucket>(reader.GetString(10)),
        Deserialize<EnergyPoint>(reader.GetString(11)),
        Deserialize<BeatMarker>(reader.GetString(12)),
        Deserialize<SongSection>(reader.GetString(13)),
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(14), DateTimeKind.Utc)));

    private static IReadOnlyList<T> Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];

    private static object DbValue<T>(T? value) where T : struct =>
        value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => value ?? (object)DBNull.Value;

    private const string SelectColumns = """
        id, project_id, source_asset_id, version, duration_seconds, bpm,
        sample_rate, channels, codec, bit_rate, waveform_json, energy_json,
        beats_json, sections_json, created_utc
        """;
}
