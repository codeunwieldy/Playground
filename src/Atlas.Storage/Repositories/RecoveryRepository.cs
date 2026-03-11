using System.Globalization;
using System.Text.Json;
using Atlas.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of the recovery repository for undo checkpoints and quarantine items.
/// </summary>
public sealed class RecoveryRepository(SqliteConnectionFactory connectionFactory) : IRecoveryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SaveCheckpointAsync(UndoCheckpoint checkpoint, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(checkpoint.CheckpointId))
        {
            checkpoint.CheckpointId = Guid.NewGuid().ToString("N");
        }

        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO undo_checkpoints (checkpoint_id, batch_id, payload, created_utc)
            VALUES (@checkpoint_id, @batch_id, @payload, @created_utc)
            """;
        command.Parameters.AddWithValue("@checkpoint_id", checkpoint.CheckpointId);
        command.Parameters.AddWithValue("@batch_id", checkpoint.BatchId);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);

        await command.ExecuteNonQueryAsync(ct);
        return checkpoint.CheckpointId;
    }

    public async Task<UndoCheckpoint?> GetCheckpointAsync(string checkpointId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM undo_checkpoints WHERE checkpoint_id = @checkpoint_id";
        command.Parameters.AddWithValue("@checkpoint_id", checkpointId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<UndoCheckpoint>(json, JsonOptions);
    }

    public async Task<UndoCheckpoint?> GetCheckpointForBatchAsync(string batchId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM undo_checkpoints WHERE batch_id = @batch_id";
        command.Parameters.AddWithValue("@batch_id", batchId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<UndoCheckpoint>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<CheckpointSummary>> ListCheckpointsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT checkpoint_id, batch_id, payload, created_utc
            FROM undo_checkpoints
            ORDER BY created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<CheckpointSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var checkpoint = JsonSerializer.Deserialize<UndoCheckpoint>(json, JsonOptions);
            var operationCount = checkpoint?.InverseOperations.Count ?? 0;

            results.Add(new CheckpointSummary(
                CheckpointId: reader.GetString(0),
                BatchId: reader.GetString(1),
                OperationCount: operationCount,
                CreatedUtc: DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<bool> DeleteCheckpointAsync(string checkpointId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM undo_checkpoints WHERE checkpoint_id = @checkpoint_id";
        command.Parameters.AddWithValue("@checkpoint_id", checkpointId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<string> SaveQuarantineItemAsync(QuarantineItem item, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(item.QuarantineId))
        {
            item.QuarantineId = Guid.NewGuid().ToString("N");
        }

        var json = JsonSerializer.Serialize(item, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var retentionUntilUtc = DateTimeOffset.FromUnixTimeSeconds(item.RetentionUntilUnixTimeSeconds).UtcDateTime.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quarantine_items (quarantine_id, original_path, current_path, plan_id, retention_until_utc, payload)
            VALUES (@quarantine_id, @original_path, @current_path, @plan_id, @retention_until_utc, @payload)
            """;
        command.Parameters.AddWithValue("@quarantine_id", item.QuarantineId);
        command.Parameters.AddWithValue("@original_path", item.OriginalPath);
        command.Parameters.AddWithValue("@current_path", item.CurrentPath);
        command.Parameters.AddWithValue("@plan_id", item.PlanId);
        command.Parameters.AddWithValue("@retention_until_utc", retentionUntilUtc);
        command.Parameters.AddWithValue("@payload", payload);

        await command.ExecuteNonQueryAsync(ct);
        return item.QuarantineId;
    }

    public async Task<QuarantineItem?> GetQuarantineItemAsync(string quarantineId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM quarantine_items WHERE quarantine_id = @quarantine_id";
        command.Parameters.AddWithValue("@quarantine_id", quarantineId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<QuarantineItem>(json, JsonOptions);
    }

    public async Task<QuarantineItem?> GetQuarantineItemByOriginalPathAsync(string originalPath, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM quarantine_items WHERE original_path = @original_path";
        command.Parameters.AddWithValue("@original_path", originalPath);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<QuarantineItem>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<QuarantineItem>> GetQuarantineItemsForPlanAsync(string planId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM quarantine_items WHERE plan_id = @plan_id";
        command.Parameters.AddWithValue("@plan_id", planId);

        var results = new List<QuarantineItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var item = JsonSerializer.Deserialize<QuarantineItem>(json, JsonOptions);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<QuarantineItemSummary>> ListQuarantineItemsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT quarantine_id, original_path, payload, retention_until_utc
            FROM quarantine_items
            ORDER BY retention_until_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<QuarantineItemSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var item = JsonSerializer.Deserialize<QuarantineItem>(json, JsonOptions);
            var reason = item?.Reason ?? string.Empty;

            results.Add(new QuarantineItemSummary(
                QuarantineId: reader.GetString(0),
                OriginalPath: reader.GetString(1),
                Reason: reason,
                RetentionUntilUtc: DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<IReadOnlyList<QuarantineItem>> GetExpiredQuarantineItemsAsync(DateTime asOfUtc, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM quarantine_items
            WHERE retention_until_utc < @as_of_utc
            """;
        command.Parameters.AddWithValue("@as_of_utc", asOfUtc.ToString("o"));

        var results = new List<QuarantineItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var item = JsonSerializer.Deserialize<QuarantineItem>(json, JsonOptions);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    public async Task<bool> DeleteQuarantineItemAsync(string quarantineId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM quarantine_items WHERE quarantine_id = @quarantine_id";
        command.Parameters.AddWithValue("@quarantine_id", quarantineId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }
}
