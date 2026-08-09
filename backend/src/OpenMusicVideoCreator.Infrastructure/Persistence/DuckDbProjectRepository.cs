using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbProjectRepository : IProjectRepository
{
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbProjectRepository(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<MusicVideoProject>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        await using (var connection = _connections.Create())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM projects ORDER BY updated_utc DESC, id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetFieldValue<Guid>(0));
            }
        }

        var projects = new List<MusicVideoProject>(ids.Count);
        foreach (var id in ids)
        {
            var project = await GetAsync(id, cancellationToken);
            if (project is not null)
            {
                projects.Add(project);
            }
        }

        return projects;
    }

    public async Task<MusicVideoProject?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);

        var row = await ReadProjectRowAsync(connection, id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var targets = await ReadTargetsAsync(connection, id, cancellationToken);
        var references = await ReadReferencesAsync(connection, id, cancellationToken);

        return new MusicVideoProject(
            id,
            row.Title,
            row.Artist,
            row.Lyrics,
            row.Storyline,
            row.Meaning,
            row.VisualDirection,
            row.Mood,
            row.Genre,
            row.AspectRatio,
            row.Resolution,
            targets,
            row.Preset,
            row.EstimatedBudget,
            row.MaximumBudget,
            references,
            row.CreatedUtc,
            row.UpdatedUtc);
    }

    public async Task UpsertAsync(
        MusicVideoProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO projects (
                    id,
                    title,
                    artist,
                    lyrics,
                    storyline,
                    meaning,
                    visual_direction,
                    mood,
                    genre,
                    aspect_ratio,
                    resolution_width,
                    resolution_height,
                    generation_preset,
                    estimated_budget,
                    maximum_budget,
                    created_utc,
                    updated_utc
                ) VALUES (
                    $id,
                    $title,
                    $artist,
                    $lyrics,
                    $storyline,
                    $meaning,
                    $visual_direction,
                    $mood,
                    $genre,
                    $aspect_ratio,
                    $resolution_width,
                    $resolution_height,
                    $generation_preset,
                    $estimated_budget,
                    $maximum_budget,
                    $created_utc,
                    $updated_utc
                );
                """;

            AddProjectParameters(command, project);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await DeleteProjectCollectionsAsync(connection, transaction, project.Id, cancellationToken);
        await InsertTargetsAsync(connection, transaction, project, cancellationToken);
        await InsertReferencesAsync(connection, transaction, project, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await DeleteProjectCollectionsAsync(connection, transaction, id, cancellationToken);
        await DeleteProjectSettingsAsync(connection, transaction, id, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM projects WHERE id = $id;";
        command.Parameters.Add(new DuckDBParameter("id", id));
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    private static async Task<ProjectRow?> ReadProjectRowAsync(
        DuckDBConnection connection,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                title,
                artist,
                lyrics,
                storyline,
                meaning,
                visual_direction,
                mood,
                genre,
                aspect_ratio,
                resolution_width,
                resolution_height,
                generation_preset,
                estimated_budget,
                maximum_budget,
                created_utc,
                updated_utc
            FROM projects
            WHERE id = $id;
            """;
        command.Parameters.Add(new DuckDBParameter("id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProjectRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            ParseEnum<ProjectAspectRatio>(reader.GetString(8)),
            new OutputResolution(reader.GetInt32(9), reader.GetInt32(10)),
            ParseEnum<GenerationPreset>(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetDecimal(13),
            ToUtcOffset(reader.GetDateTime(14)),
            ToUtcOffset(reader.GetDateTime(15)));
    }

    private static void AddProjectParameters(DuckDBCommand command, MusicVideoProject project)
    {
        command.Parameters.Add(new DuckDBParameter("id", project.Id));
        command.Parameters.Add(new DuckDBParameter("title", project.Title));
        command.Parameters.Add(new DuckDBParameter("artist", project.Artist));
        command.Parameters.Add(new DuckDBParameter("lyrics", project.Lyrics));
        command.Parameters.Add(new DuckDBParameter("storyline", project.Storyline));
        command.Parameters.Add(new DuckDBParameter("meaning", project.Meaning));
        command.Parameters.Add(new DuckDBParameter("visual_direction", project.VisualDirection));
        command.Parameters.Add(new DuckDBParameter("mood", project.Mood));
        command.Parameters.Add(new DuckDBParameter("genre", project.Genre));
        command.Parameters.Add(new DuckDBParameter("aspect_ratio", project.AspectRatio.ToString()));
        command.Parameters.Add(new DuckDBParameter("resolution_width", project.Resolution.Width));
        command.Parameters.Add(new DuckDBParameter("resolution_height", project.Resolution.Height));
        command.Parameters.Add(new DuckDBParameter("generation_preset", project.Preset.ToString()));
        command.Parameters.Add(new DuckDBParameter("estimated_budget", (object?)project.EstimatedBudget ?? DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("maximum_budget", (object?)project.MaximumBudget ?? DBNull.Value));
        command.Parameters.Add(new DuckDBParameter("created_utc", project.CreatedUtc.UtcDateTime));
        command.Parameters.Add(new DuckDBParameter("updated_utc", project.UpdatedUtc.UtcDateTime));
    }

    private static async Task DeleteProjectCollectionsAsync(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM project_targets WHERE project_id = $project_id;
            DELETE FROM project_references WHERE project_id = $project_id;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteProjectSettingsAsync(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM project_settings WHERE project_id = $project_id;";
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTargetsAsync(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction transaction,
        MusicVideoProject project,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < project.TargetPlatforms.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO project_targets(project_id, sort_order, platform)
                VALUES ($project_id, $sort_order, $platform);
                """;
            command.Parameters.Add(new DuckDBParameter("project_id", project.Id));
            command.Parameters.Add(new DuckDBParameter("sort_order", index));
            command.Parameters.Add(new DuckDBParameter("platform", project.TargetPlatforms[index]));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertReferencesAsync(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction transaction,
        MusicVideoProject project,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < project.References.Count; index++)
        {
            var reference = project.References[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO project_references(project_id, sort_order, reference_kind, reference_id)
                VALUES ($project_id, $sort_order, $reference_kind, $reference_id);
                """;
            command.Parameters.Add(new DuckDBParameter("project_id", project.Id));
            command.Parameters.Add(new DuckDBParameter("sort_order", index));
            command.Parameters.Add(new DuckDBParameter("reference_kind", reference.Kind.ToString()));
            command.Parameters.Add(new DuckDBParameter("reference_id", reference.ReferenceId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadTargetsAsync(
        DuckDBConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var targets = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT platform
            FROM project_targets
            WHERE project_id = $project_id
            ORDER BY sort_order;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(reader.GetString(0));
        }

        return targets;
    }

    private static async Task<IReadOnlyList<ProjectReference>> ReadReferencesAsync(
        DuckDBConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var references = new List<ProjectReference>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT reference_kind, reference_id
            FROM project_references
            WHERE project_id = $project_id
            ORDER BY sort_order;
            """;
        command.Parameters.Add(new DuckDBParameter("project_id", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            references.Add(new ProjectReference(
                ParseEnum<ProjectReferenceKind>(reader.GetString(0)),
                reader.GetFieldValue<Guid>(1)));
        }

        return references;
    }

    private static T ParseEnum<T>(string value)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Unknown persisted {typeof(T).Name} value '{value}'.");

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record ProjectRow(
        string Title,
        string Artist,
        string Lyrics,
        string Storyline,
        string Meaning,
        string VisualDirection,
        string Mood,
        string Genre,
        ProjectAspectRatio AspectRatio,
        OutputResolution Resolution,
        GenerationPreset Preset,
        decimal? EstimatedBudget,
        decimal? MaximumBudget,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc);
}
