using System.Data.Common;
using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Domain.Jobs;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbJobRepository : IJobRepository
{
    private readonly DuckDbConnectionFactory _connections;
    private readonly SemaphoreSlim _claimGate = new(1, 1);

    public DuckDbJobRepository(DuckDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task CreateAsync(
        GenerationJob job,
        IReadOnlyCollection<Guid> dependencyIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(dependencyIds);

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await InsertJobAsync(connection, transaction, job, cancellationToken);
        foreach (var dependencyId in dependencyIds.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO job_dependencies(job_id, depends_on_job_id)
                VALUES ($job_id, $dependency_id);
                """;
            command.Parameters.Add(new DuckDBParameter("job_id", job.Id));
            command.Parameters.Add(new DuckDBParameter("dependency_id", dependencyId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<GenerationJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await GetAsync(connection, transaction: null, id, cancellationToken);
    }

    public async Task<IReadOnlyList<GenerationJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = new List<GenerationJob>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM jobs
            ORDER BY created_utc, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    public async Task<IReadOnlyList<Guid>> GetDependenciesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var dependencies = new List<Guid>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT depends_on_job_id
            FROM job_dependencies
            WHERE job_id = $job_id
            ORDER BY depends_on_job_id;
            """;
        command.Parameters.Add(new DuckDBParameter("job_id", jobId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dependencies.Add(reader.GetFieldValue<Guid>(0));
        }

        return dependencies;
    }

    public async Task<IReadOnlyList<JobAttempt>> GetAttemptsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var attempts = new List<JobAttempt>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, attempt_number, started_utc, completed_utc, state,
                   provider_task_id, error_code, error_message, estimated_cost,
                   actual_cost, currency
            FROM job_attempts
            WHERE job_id = $job_id
            ORDER BY attempt_number;
            """;
        command.Parameters.Add(new DuckDBParameter("job_id", jobId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(ReadAttempt(reader));
        }

        return attempts;
    }

    public async Task<bool> TryUpdateAsync(
        GenerationJob job,
        JobState expectedState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateUpdateCommand(connection, transaction: null, job, expectedState);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<GenerationJob?> TryClaimNextAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Claim lease must be positive.");
        }

        await _claimGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = _connections.Create();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            Guid? candidateId;
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = """
                    SELECT id
                    FROM jobs
                    WHERE state = 'Queued'
                      AND (next_run_utc IS NULL OR next_run_utc <= $now)
                      AND (claimed_by IS NULL OR claim_expires_utc IS NULL OR claim_expires_utc <= $now)
                    ORDER BY priority ASC, created_utc ASC, id ASC
                    LIMIT 1;
                    """;
                select.Parameters.Add(new DuckDBParameter("now", now.UtcDateTime));
                var scalar = await select.ExecuteScalarAsync(cancellationToken);
                candidateId = scalar is null or DBNull ? null : (Guid)scalar;
            }

            if (!candidateId.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var current = await GetAsync(connection, transaction, candidateId.Value, cancellationToken);
            if (current is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var claimed = current with
            {
                State = JobState.Submitting,
                AttemptCount = current.AttemptCount + 1,
                StartedUtc = current.StartedUtc ?? now,
                UpdatedUtc = now,
                ClaimedBy = workerId,
                ClaimExpiresUtc = now + leaseDuration,
                ErrorCode = null,
                ErrorMessage = null,
            };

            await using (var update = CreateUpdateCommand(connection, transaction, claimed, JobState.Queued))
            {
                var affected = await update.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            await UpsertAttemptAsync(
                connection,
                transaction,
                new JobAttempt(
                    claimed.Id,
                    claimed.AttemptCount,
                    now,
                    CompletedUtc: null,
                    JobState.Submitting,
                    claimed.ProviderTaskId,
                    ErrorCode: null,
                    ErrorMessage: null,
                    claimed.EstimatedCost,
                    ActualCost: null,
                    claimed.Currency),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }
        finally
        {
            _claimGate.Release();
        }
    }

    public async Task UpsertAttemptAsync(
        JobAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken);
        await UpsertAttemptAsync(connection, transaction: null, attempt, cancellationToken);
    }

    private static async Task InsertJobAsync(
        DuckDBConnection connection,
        DbTransaction transaction,
        GenerationJob job,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO jobs ({InsertColumns})
            VALUES ({InsertValues});
            """;
        AddJobParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DuckDBCommand CreateUpdateCommand(
        DuckDBConnection connection,
        DbTransaction? transaction,
        GenerationJob job,
        JobState expectedState)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE jobs SET
                project_id = $project_id,
                scene_id = $scene_id,
                parent_job_id = $parent_job_id,
                job_type = $job_type,
                payload_json = $payload_json,
                provider_id = $provider_id,
                model_id = $model_id,
                state = $state,
                resume_state = $resume_state,
                priority = $priority,
                attempt_count = $attempt_count,
                retry_count = $retry_count,
                max_retries = $max_retries,
                created_utc = $created_utc,
                updated_utc = $updated_utc,
                next_run_utc = $next_run_utc,
                started_utc = $started_utc,
                completed_utc = $completed_utc,
                provider_task_id = $provider_task_id,
                error_code = $error_code,
                error_message = $error_message,
                estimated_cost = $estimated_cost,
                actual_cost = $actual_cost,
                currency = $currency,
                claimed_by = $claimed_by,
                claim_expires_utc = $claim_expires_utc
            WHERE id = $id AND state = $expected_state;
            """;
        AddJobParameters(command, job);
        command.Parameters.Add(new DuckDBParameter("expected_state", expectedState.ToString()));
        return command;
    }

    private static async Task<GenerationJob?> GetAsync(
        DuckDBConnection connection,
        DbTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM jobs
            WHERE id = $id;
            """;
        command.Parameters.Add(new DuckDBParameter("id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    private static async Task UpsertAttemptAsync(
        DuckDBConnection connection,
        DbTransaction? transaction,
        JobAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO job_attempts(
                job_id, attempt_number, started_utc, completed_utc, state,
                provider_task_id, error_code, error_message, estimated_cost,
                actual_cost, currency
            ) VALUES (
                $job_id, $attempt_number, $started_utc, $completed_utc, $state,
                $provider_task_id, $error_code, $error_message, $estimated_cost,
                $actual_cost, $currency
            );
            """;
        command.Parameters.Add(new DuckDBParameter("job_id", attempt.JobId));
        command.Parameters.Add(new DuckDBParameter("attempt_number", attempt.AttemptNumber));
        command.Parameters.Add(new DuckDBParameter("started_utc", attempt.StartedUtc.UtcDateTime));
        command.Parameters.Add(new DuckDBParameter("completed_utc", DbValue(attempt.CompletedUtc)));
        command.Parameters.Add(new DuckDBParameter("state", attempt.State.ToString()));
        command.Parameters.Add(new DuckDBParameter("provider_task_id", DbValue(attempt.ProviderTaskId)));
        command.Parameters.Add(new DuckDBParameter("error_code", DbValue(attempt.ErrorCode)));
        command.Parameters.Add(new DuckDBParameter("error_message", DbValue(attempt.ErrorMessage)));
        command.Parameters.Add(new DuckDBParameter("estimated_cost", DbValue(attempt.EstimatedCost)));
        command.Parameters.Add(new DuckDBParameter("actual_cost", DbValue(attempt.ActualCost)));
        command.Parameters.Add(new DuckDBParameter("currency", DbValue(attempt.Currency)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddJobParameters(DuckDBCommand command, GenerationJob job)
    {
        command.Parameters.Add(new DuckDBParameter("id", job.Id));
        command.Parameters.Add(new DuckDBParameter("project_id", DbValue(job.ProjectId)));
        command.Parameters.Add(new DuckDBParameter("scene_id", DbValue(job.SceneId)));
        command.Parameters.Add(new DuckDBParameter("parent_job_id", DbValue(job.ParentJobId)));
        command.Parameters.Add(new DuckDBParameter("job_type", job.Type));
        command.Parameters.Add(new DuckDBParameter("payload_json", job.PayloadJson));
        command.Parameters.Add(new DuckDBParameter("provider_id", DbValue(job.ProviderId)));
        command.Parameters.Add(new DuckDBParameter("model_id", DbValue(job.ModelId)));
        command.Parameters.Add(new DuckDBParameter("state", job.State.ToString()));
        command.Parameters.Add(new DuckDBParameter("resume_state", DbValue(job.ResumeState?.ToString())));
        command.Parameters.Add(new DuckDBParameter("priority", job.Priority));
        command.Parameters.Add(new DuckDBParameter("attempt_count", job.AttemptCount));
        command.Parameters.Add(new DuckDBParameter("retry_count", job.RetryCount));
        command.Parameters.Add(new DuckDBParameter("max_retries", job.MaxRetries));
        command.Parameters.Add(new DuckDBParameter("created_utc", job.CreatedUtc.UtcDateTime));
        command.Parameters.Add(new DuckDBParameter("updated_utc", job.UpdatedUtc.UtcDateTime));
        command.Parameters.Add(new DuckDBParameter("next_run_utc", DbValue(job.NextRunUtc)));
        command.Parameters.Add(new DuckDBParameter("started_utc", DbValue(job.StartedUtc)));
        command.Parameters.Add(new DuckDBParameter("completed_utc", DbValue(job.CompletedUtc)));
        command.Parameters.Add(new DuckDBParameter("provider_task_id", DbValue(job.ProviderTaskId)));
        command.Parameters.Add(new DuckDBParameter("error_code", DbValue(job.ErrorCode)));
        command.Parameters.Add(new DuckDBParameter("error_message", DbValue(job.ErrorMessage)));
        command.Parameters.Add(new DuckDBParameter("estimated_cost", DbValue(job.EstimatedCost)));
        command.Parameters.Add(new DuckDBParameter("actual_cost", DbValue(job.ActualCost)));
        command.Parameters.Add(new DuckDBParameter("currency", DbValue(job.Currency)));
        command.Parameters.Add(new DuckDBParameter("claimed_by", DbValue(job.ClaimedBy)));
        command.Parameters.Add(new DuckDBParameter("claim_expires_utc", DbValue(job.ClaimExpiresUtc)));
    }

    private static GenerationJob ReadJob(DbDataReader reader) => new(
        reader.GetFieldValue<Guid>(0),
        reader.IsDBNull(1) ? null : reader.GetFieldValue<Guid>(1),
        reader.IsDBNull(2) ? null : reader.GetFieldValue<Guid>(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<Guid>(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        ParseState(reader.GetString(8)),
        reader.IsDBNull(9) ? null : ParseState(reader.GetString(9)),
        reader.GetInt32(10),
        reader.GetInt32(11),
        reader.GetInt32(12),
        reader.GetInt32(13),
        ToUtc(reader.GetDateTime(14)),
        ToUtc(reader.GetDateTime(15)),
        reader.IsDBNull(16) ? null : ToUtc(reader.GetDateTime(16)),
        reader.IsDBNull(17) ? null : ToUtc(reader.GetDateTime(17)),
        reader.IsDBNull(18) ? null : ToUtc(reader.GetDateTime(18)),
        reader.IsDBNull(19) ? null : reader.GetString(19),
        reader.IsDBNull(20) ? null : reader.GetString(20),
        reader.IsDBNull(21) ? null : reader.GetString(21),
        reader.IsDBNull(22) ? null : reader.GetDecimal(22),
        reader.IsDBNull(23) ? null : reader.GetDecimal(23),
        reader.IsDBNull(24) ? null : reader.GetString(24),
        reader.IsDBNull(25) ? null : reader.GetString(25),
        reader.IsDBNull(26) ? null : ToUtc(reader.GetDateTime(26)));

    private static JobAttempt ReadAttempt(DbDataReader reader) => new(
        reader.GetFieldValue<Guid>(0),
        reader.GetInt32(1),
        ToUtc(reader.GetDateTime(2)),
        reader.IsDBNull(3) ? null : ToUtc(reader.GetDateTime(3)),
        ParseState(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetDecimal(8),
        reader.IsDBNull(9) ? null : reader.GetDecimal(9),
        reader.IsDBNull(10) ? null : reader.GetString(10));

    private static JobState ParseState(string value) =>
        Enum.TryParse<JobState>(value, ignoreCase: false, out var state)
            ? state
            : throw new InvalidDataException($"Unknown persisted job state '{value}'.");

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static object DbValue<T>(T? value) where T : struct =>
        value.HasValue
            ? value.Value switch
            {
                DateTimeOffset timestamp => timestamp.UtcDateTime,
                _ => value.Value,
            }
            : DBNull.Value;

    private static object DbValue(string? value) => value ?? (object)DBNull.Value;

    private const string SelectColumns = """
        id, project_id, scene_id, parent_job_id, job_type, payload_json,
        provider_id, model_id, state, resume_state, priority, attempt_count,
        retry_count, max_retries, created_utc, updated_utc, next_run_utc,
        started_utc, completed_utc, provider_task_id, error_code, error_message,
        estimated_cost, actual_cost, currency, claimed_by, claim_expires_utc
        """;

    private const string InsertColumns = """
        id, project_id, scene_id, parent_job_id, job_type, payload_json,
        provider_id, model_id, state, resume_state, priority, attempt_count,
        retry_count, max_retries, created_utc, updated_utc, next_run_utc,
        started_utc, completed_utc, provider_task_id, error_code, error_message,
        estimated_cost, actual_cost, currency, claimed_by, claim_expires_utc
        """;

    private const string InsertValues = """
        $id, $project_id, $scene_id, $parent_job_id, $job_type, $payload_json,
        $provider_id, $model_id, $state, $resume_state, $priority, $attempt_count,
        $retry_count, $max_retries, $created_utc, $updated_utc, $next_run_utc,
        $started_utc, $completed_utc, $provider_task_id, $error_code, $error_message,
        $estimated_cost, $actual_cost, $currency, $claimed_by, $claim_expires_utc
        """;
}
