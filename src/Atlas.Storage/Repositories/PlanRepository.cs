using System.Globalization;
using System.Text.Json;
using Atlas.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of the plan repository.
/// </summary>
public sealed class PlanRepository(SqliteConnectionFactory connectionFactory) : IPlanRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SavePlanAsync(PlanGraph plan, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plan.PlanId))
        {
            plan.PlanId = Guid.NewGuid().ToString("N");
        }

        var json = JsonSerializer.Serialize(plan, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO plans (plan_id, scope, summary, payload, created_utc)
            VALUES (@plan_id, @scope, @summary, @payload, @created_utc)
            """;
        command.Parameters.AddWithValue("@plan_id", plan.PlanId);
        command.Parameters.AddWithValue("@scope", plan.Scope);
        command.Parameters.AddWithValue("@summary", plan.Rationale);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);

        await command.ExecuteNonQueryAsync(ct);
        return plan.PlanId;
    }

    public async Task<PlanGraph?> GetPlanAsync(string planId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM plans WHERE plan_id = @plan_id";
        command.Parameters.AddWithValue("@plan_id", planId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<PlanGraph>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<PlanSummary>> ListPlansAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT plan_id, scope, summary, created_utc
            FROM plans
            ORDER BY created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<PlanSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new PlanSummary(
                PlanId: reader.GetString(0),
                Scope: reader.GetString(1),
                Summary: reader.GetString(2),
                CreatedUtc: DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<bool> DeletePlanAsync(string planId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM plans WHERE plan_id = @plan_id";
        command.Parameters.AddWithValue("@plan_id", planId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<string> SaveBatchAsync(ExecutionBatch batch, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(batch.BatchId))
        {
            batch.BatchId = Guid.NewGuid().ToString("N");
        }

        var json = JsonSerializer.Serialize(batch, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO execution_batches (batch_id, plan_id, payload, created_utc)
            VALUES (@batch_id, @plan_id, @payload, @created_utc)
            """;
        command.Parameters.AddWithValue("@batch_id", batch.BatchId);
        command.Parameters.AddWithValue("@plan_id", batch.PlanId);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);

        await command.ExecuteNonQueryAsync(ct);
        return batch.BatchId;
    }

    public async Task<ExecutionBatch?> GetBatchAsync(string batchId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM execution_batches WHERE batch_id = @batch_id";
        command.Parameters.AddWithValue("@batch_id", batchId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<ExecutionBatch>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<ExecutionBatch>> GetBatchesForPlanAsync(string planId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM execution_batches
            WHERE plan_id = @plan_id
            ORDER BY created_utc ASC
            """;
        command.Parameters.AddWithValue("@plan_id", planId);

        var results = new List<ExecutionBatch>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var batch = JsonSerializer.Deserialize<ExecutionBatch>(json, JsonOptions);
            if (batch is not null)
            {
                results.Add(batch);
            }
        }

        return results;
    }
}
