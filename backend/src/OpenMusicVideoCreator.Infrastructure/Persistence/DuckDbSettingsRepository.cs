using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Abstractions;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbSettingsRepository : IApplicationSettingsRepository, IProjectSettingsRepository
{
    private readonly DuckDbConnectionFactory _connections;

    public DuckDbSettingsRepository(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        GetValueAsync(
            "SELECT value_json FROM application_settings WHERE setting_key = $key;",
            key,
            projectId: null,
            cancellationToken);

    public Task<string?> GetAsync(
        Guid projectId,
        string key,
        CancellationToken cancellationToken = default) =>
        GetValueAsync(
            "SELECT value_json FROM project_settings WHERE project_id = $project_id AND setting_key = $key;",
            key,
            projectId,
            cancellationToken);

    public Task SetAsync(string key, string valueJson, CancellationToken cancellationToken = default) =>
        SetValueAsync(
            """
            INSERT OR REPLACE INTO application_settings(setting_key, value_json, updated_utc)
            VALUES ($key, $value_json, current_timestamp);
            """,
            key,
            valueJson,
            projectId: null,
            cancellationToken);

    public Task SetAsync(
        Guid projectId,
        string key,
        string valueJson,
        CancellationToken cancellationToken = default) =>
        SetValueAsync(
            """
            INSERT OR REPLACE INTO project_settings(project_id, setting_key, value_json, updated_utc)
            VALUES ($project_id, $key, $value_json, current_timestamp);
            """,
            key,
            valueJson,
            projectId,
            cancellationToken);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        DeleteValueAsync(
            "DELETE FROM application_settings WHERE setting_key = $key;",
            key,
            projectId: null,
            cancellationToken);

    public Task<bool> DeleteAsync(
        Guid projectId,
        string key,
        CancellationToken cancellationToken = default) =>
        DeleteValueAsync(
            "DELETE FROM project_settings WHERE project_id = $project_id AND setting_key = $key;",
            key,
            projectId,
            cancellationToken);

    private async Task<string?> GetValueAsync(
        string sql,
        string key,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddCommonParameters(command, key, projectId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private async Task SetValueAsync(
        string sql,
        string key,
        string valueJson,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueJson);

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddCommonParameters(command, key, projectId);
        command.Parameters.Add(new DuckDBParameter("value_json", valueJson));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> DeleteValueAsync(
        string sql,
        string key,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddCommonParameters(command, key, projectId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void AddCommonParameters(DuckDBCommand command, string key, Guid? projectId)
    {
        command.Parameters.Add(new DuckDBParameter("key", key));
        if (projectId is not null)
        {
            command.Parameters.Add(new DuckDBParameter("project_id", projectId.Value));
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Setting keys are limited to 200 characters.");
        }
    }
}
