using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Abstractions;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbDatabase : IApplicationPersistence
{
    private const int CurrentSchemaVersion = 1;
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbDatabase(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);

        await using (var migrationTable = connection.CreateCommand())
        {
            migrationTable.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_utc TIMESTAMPTZ NOT NULL
                );
                """;
            await migrationTable.ExecuteNonQueryAsync(cancellationToken);
        }

        var currentVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {currentVersion} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (currentVersion < 1)
        {
            await ApplyVersionOneAsync(connection, cancellationToken);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connections.Create();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        DuckDBConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ApplyVersionOneAsync(
        DuckDBConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE projects (
                id UUID PRIMARY KEY,
                title VARCHAR NOT NULL,
                artist VARCHAR NOT NULL,
                lyrics VARCHAR NOT NULL,
                storyline VARCHAR NOT NULL,
                meaning VARCHAR NOT NULL,
                visual_direction VARCHAR NOT NULL,
                mood VARCHAR NOT NULL,
                genre VARCHAR NOT NULL,
                aspect_ratio VARCHAR NOT NULL,
                resolution_width INTEGER NOT NULL,
                resolution_height INTEGER NOT NULL,
                generation_preset VARCHAR NOT NULL,
                estimated_budget DECIMAL(18, 4),
                maximum_budget DECIMAL(18, 4),
                created_utc TIMESTAMPTZ NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE project_targets (
                project_id UUID NOT NULL,
                sort_order INTEGER NOT NULL,
                platform VARCHAR NOT NULL,
                PRIMARY KEY (project_id, platform)
            );

            CREATE TABLE project_references (
                project_id UUID NOT NULL,
                sort_order INTEGER NOT NULL,
                reference_kind VARCHAR NOT NULL,
                reference_id UUID NOT NULL,
                PRIMARY KEY (project_id, reference_kind, reference_id)
            );

            CREATE TABLE application_settings (
                setting_key VARCHAR PRIMARY KEY,
                value_json VARCHAR NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE project_settings (
                project_id UUID NOT NULL,
                setting_key VARCHAR NOT NULL,
                value_json VARCHAR NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (project_id, setting_key)
            );

            CREATE TABLE media_assets (
                id UUID PRIMARY KEY,
                project_id UUID,
                location VARCHAR NOT NULL,
                checksum_sha256 VARCHAR NOT NULL,
                mime_type VARCHAR NOT NULL,
                width INTEGER,
                height INTEGER,
                duration_ms BIGINT,
                file_size BIGINT NOT NULL,
                creation_source VARCHAR NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX idx_media_assets_project_id ON media_assets(project_id);

            INSERT INTO schema_migrations(version, applied_utc)
            VALUES (1, current_timestamp);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
