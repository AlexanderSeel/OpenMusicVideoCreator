using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Abstractions;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbDatabase : IApplicationPersistence
{
    private const int CurrentSchemaVersion = 5;
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
            currentVersion = 1;
        }
        if (currentVersion < 2)
        {
            await ApplyVersionTwoAsync(connection, cancellationToken);
            currentVersion = 2;
        }
        if (currentVersion < 3)
        {
            await ApplyVersionThreeAsync(connection, cancellationToken);
            currentVersion = 3;
        }
        if (currentVersion < 4)
        {
            await ApplyVersionFourAsync(connection, cancellationToken);
            currentVersion = 4;
        }
        if (currentVersion < 5)
        {
            await ApplyVersionFiveAsync(connection, cancellationToken);
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

    private static async Task<int> ReadSchemaVersionAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ApplyVersionOneAsync(DuckDBConnection connection, CancellationToken cancellationToken)
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
            INSERT INTO schema_migrations(version, applied_utc) VALUES (1, current_timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersionTwoAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE jobs (
                id UUID PRIMARY KEY,
                project_id UUID,
                scene_id UUID,
                parent_job_id UUID,
                job_type VARCHAR NOT NULL,
                payload_json VARCHAR NOT NULL,
                provider_id VARCHAR,
                model_id VARCHAR,
                state VARCHAR NOT NULL,
                resume_state VARCHAR,
                priority INTEGER NOT NULL,
                attempt_count INTEGER NOT NULL,
                retry_count INTEGER NOT NULL,
                max_retries INTEGER NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                next_run_utc TIMESTAMPTZ,
                started_utc TIMESTAMPTZ,
                completed_utc TIMESTAMPTZ,
                provider_task_id VARCHAR,
                error_code VARCHAR,
                error_message VARCHAR,
                estimated_cost DECIMAL(18, 4),
                actual_cost DECIMAL(18, 4),
                currency VARCHAR,
                claimed_by VARCHAR,
                claim_expires_utc TIMESTAMPTZ
            );

            CREATE TABLE job_dependencies (
                job_id UUID NOT NULL,
                depends_on_job_id UUID NOT NULL,
                PRIMARY KEY (job_id, depends_on_job_id)
            );

            CREATE TABLE job_attempts (
                job_id UUID NOT NULL,
                attempt_number INTEGER NOT NULL,
                started_utc TIMESTAMPTZ NOT NULL,
                completed_utc TIMESTAMPTZ,
                state VARCHAR NOT NULL,
                provider_task_id VARCHAR,
                error_code VARCHAR,
                error_message VARCHAR,
                estimated_cost DECIMAL(18, 4),
                actual_cost DECIMAL(18, 4),
                currency VARCHAR,
                PRIMARY KEY (job_id, attempt_number)
            );

            CREATE INDEX idx_jobs_state_schedule ON jobs(state, next_run_utc, priority, created_utc);
            CREATE INDEX idx_jobs_project_id ON jobs(project_id);
            CREATE INDEX idx_jobs_scene_id ON jobs(scene_id);
            CREATE INDEX idx_jobs_parent_job_id ON jobs(parent_job_id);
            CREATE INDEX idx_job_dependencies_dependency ON job_dependencies(depends_on_job_id);
            INSERT INTO schema_migrations(version, applied_utc) VALUES (2, current_timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersionThreeAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE song_analyses (
                id UUID PRIMARY KEY,
                project_id UUID NOT NULL,
                source_asset_id UUID NOT NULL,
                version INTEGER NOT NULL,
                duration_seconds DOUBLE NOT NULL,
                bpm DOUBLE,
                sample_rate INTEGER,
                channels INTEGER,
                codec VARCHAR,
                bit_rate BIGINT,
                waveform_json VARCHAR NOT NULL,
                energy_json VARCHAR NOT NULL,
                beats_json VARCHAR NOT NULL,
                sections_json VARCHAR NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                UNIQUE(project_id, version)
            );
            CREATE INDEX idx_song_analyses_project_version ON song_analyses(project_id, version DESC);
            INSERT INTO schema_migrations(version, applied_utc) VALUES (3, current_timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersionFourAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE song_analyses ADD COLUMN vocal_activity_json VARCHAR;

            CREATE TABLE lyric_timing_analyses (
                id UUID PRIMARY KEY,
                project_id UUID NOT NULL,
                source_asset_id UUID NOT NULL,
                song_analysis_id UUID NOT NULL,
                version INTEGER NOT NULL,
                supplied_lyrics_sha256 VARCHAR NOT NULL,
                lines_json VARCHAR NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                UNIQUE(project_id, version)
            );
            CREATE INDEX idx_lyric_timing_project_version ON lyric_timing_analyses(project_id, version DESC);
            INSERT INTO schema_migrations(version, applied_utc) VALUES (4, current_timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersionFiveAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE library_assets (
                id UUID PRIMARY KEY,
                media_asset_id UUID NOT NULL,
                preview_media_asset_id UUID,
                name VARCHAR NOT NULL,
                tags_json VARCHAR NOT NULL,
                is_favorite BOOLEAN NOT NULL,
                source_description VARCHAR NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE visual_library_items (
                id UUID PRIMARY KEY,
                kind VARCHAR NOT NULL,
                name VARCHAR NOT NULL,
                description VARCHAR NOT NULL,
                tags_json VARCHAR NOT NULL,
                is_favorite BOOLEAN NOT NULL,
                asset_entry_ids_json VARCHAR NOT NULL,
                payload_json VARCHAR NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE project_character_states (
                project_id UUID NOT NULL,
                character_id UUID NOT NULL,
                outfit_id UUID,
                locks_json VARCHAR NOT NULL,
                state_values_json VARCHAR NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (project_id, character_id)
            );

            CREATE INDEX idx_library_assets_favorite_name ON library_assets(is_favorite, name);
            CREATE INDEX idx_visual_library_kind_favorite_name ON visual_library_items(kind, is_favorite, name);
            CREATE INDEX idx_project_character_states_project ON project_character_states(project_id);

            INSERT INTO schema_migrations(version, applied_utc) VALUES (5, current_timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
