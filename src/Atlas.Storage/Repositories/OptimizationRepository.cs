using System.Globalization;
using System.Text.Json;
using Atlas.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of the optimization findings repository.
/// </summary>
public sealed class OptimizationRepository(SqliteConnectionFactory connectionFactory) : IOptimizationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SaveFindingAsync(OptimizationFinding finding, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(finding.FindingId))
        {
            finding.FindingId = Guid.NewGuid().ToString("N");
        }

        var json = JsonSerializer.Serialize(finding, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO optimization_findings (finding_id, kind, target, payload, created_utc)
            VALUES (@finding_id, @kind, @target, @payload, @created_utc)
            """;
        command.Parameters.AddWithValue("@finding_id", finding.FindingId);
        command.Parameters.AddWithValue("@kind", finding.Kind.ToString());
        command.Parameters.AddWithValue("@target", finding.Target);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);

        await command.ExecuteNonQueryAsync(ct);
        return finding.FindingId;
    }

    public async Task<OptimizationFinding?> GetFindingAsync(string findingId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM optimization_findings WHERE finding_id = @finding_id";
        command.Parameters.AddWithValue("@finding_id", findingId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<OptimizationFinding>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<OptimizationFindingSummary>> ListFindingsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT finding_id, kind, target, payload, created_utc
            FROM optimization_findings
            ORDER BY created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<OptimizationFindingSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var finding = JsonSerializer.Deserialize<OptimizationFinding>(json, JsonOptions);
            var canAutoFix = finding?.CanAutoFix ?? false;

            results.Add(new OptimizationFindingSummary(
                FindingId: reader.GetString(0),
                Kind: Enum.Parse<OptimizationKind>(reader.GetString(1)),
                Target: reader.GetString(2),
                CanAutoFix: canAutoFix,
                CreatedUtc: DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<IReadOnlyList<OptimizationFinding>> GetFindingsByKindAsync(OptimizationKind kind, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM optimization_findings
            WHERE kind = @kind
            ORDER BY created_utc DESC
            """;
        command.Parameters.AddWithValue("@kind", kind.ToString());

        var results = new List<OptimizationFinding>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var finding = JsonSerializer.Deserialize<OptimizationFinding>(json, JsonOptions);
            if (finding is not null)
            {
                results.Add(finding);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<OptimizationFinding>> GetAutoFixableFindingsAsync(CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM optimization_findings
            ORDER BY created_utc DESC
            """;

        var results = new List<OptimizationFinding>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var finding = JsonSerializer.Deserialize<OptimizationFinding>(json, JsonOptions);
            if (finding is not null && finding.CanAutoFix)
            {
                results.Add(finding);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<OptimizationFinding>> GetFindingsByTargetAsync(string target, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM optimization_findings
            WHERE target = @target
            ORDER BY created_utc DESC
            """;
        command.Parameters.AddWithValue("@target", target);

        var results = new List<OptimizationFinding>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var finding = JsonSerializer.Deserialize<OptimizationFinding>(json, JsonOptions);
            if (finding is not null)
            {
                results.Add(finding);
            }
        }

        return results;
    }

    public async Task<bool> DeleteFindingAsync(string findingId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM optimization_findings WHERE finding_id = @finding_id";
        command.Parameters.AddWithValue("@finding_id", findingId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteFindingsByKindAsync(OptimizationKind kind, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM optimization_findings WHERE kind = @kind";
        command.Parameters.AddWithValue("@kind", kind.ToString());

        return await command.ExecuteNonQueryAsync(ct);
    }

    // ── Execution history (C-037) ──────────────────────────────────────────

    public async Task<string> SaveExecutionRecordAsync(OptimizationExecutionRecord record, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(record.RecordId))
        {
            record.RecordId = Guid.NewGuid().ToString("N");
        }

        if (record.CreatedUtc == default)
        {
            record.CreatedUtc = DateTime.UtcNow;
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO optimization_execution_history
                (record_id, plan_id, fix_kind, target, action, success, is_reversible, rollback_note, message, created_utc)
            VALUES
                (@record_id, @plan_id, @fix_kind, @target, @action, @success, @is_reversible, @rollback_note, @message, @created_utc)
            """;
        command.Parameters.AddWithValue("@record_id", record.RecordId);
        command.Parameters.AddWithValue("@plan_id", record.PlanId);
        command.Parameters.AddWithValue("@fix_kind", record.FixKind.ToString());
        command.Parameters.AddWithValue("@target", record.Target);
        command.Parameters.AddWithValue("@action", record.Action.ToString());
        command.Parameters.AddWithValue("@success", record.Success ? 1 : 0);
        command.Parameters.AddWithValue("@is_reversible", record.IsReversible ? 1 : 0);
        command.Parameters.AddWithValue("@rollback_note", record.RollbackNote);
        command.Parameters.AddWithValue("@message", record.Message);
        command.Parameters.AddWithValue("@created_utc", record.CreatedUtc.ToString("o"));

        await command.ExecuteNonQueryAsync(ct);
        return record.RecordId;
    }

    public async Task<IReadOnlyList<OptimizationExecutionRecord>> GetExecutionHistoryAsync(string? planId = null, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();

        if (planId is not null)
        {
            command.CommandText = """
                SELECT record_id, plan_id, fix_kind, target, action, success, is_reversible, rollback_note, message, created_utc
                FROM optimization_execution_history
                WHERE plan_id = @plan_id
                ORDER BY created_utc DESC
                LIMIT @limit OFFSET @offset
                """;
            command.Parameters.AddWithValue("@plan_id", planId);
        }
        else
        {
            command.CommandText = """
                SELECT record_id, plan_id, fix_kind, target, action, success, is_reversible, rollback_note, message, created_utc
                FROM optimization_execution_history
                ORDER BY created_utc DESC
                LIMIT @limit OFFSET @offset
                """;
        }

        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        return await ReadExecutionRecordsAsync(command, ct);
    }

    public async Task<IReadOnlyList<OptimizationExecutionRecord>> GetExecutionHistoryForTargetAsync(string target, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT record_id, plan_id, fix_kind, target, action, success, is_reversible, rollback_note, message, created_utc
            FROM optimization_execution_history
            WHERE target = @target
            ORDER BY created_utc DESC
            """;
        command.Parameters.AddWithValue("@target", target);

        return await ReadExecutionRecordsAsync(command, ct);
    }

    private static async Task<IReadOnlyList<OptimizationExecutionRecord>> ReadExecutionRecordsAsync(SqliteCommand command, CancellationToken ct)
    {
        var results = new List<OptimizationExecutionRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new OptimizationExecutionRecord
            {
                RecordId = reader.GetString(0),
                PlanId = reader.GetString(1),
                FixKind = Enum.Parse<OptimizationKind>(reader.GetString(2)),
                Target = reader.GetString(3),
                Action = Enum.Parse<OptimizationExecutionAction>(reader.GetString(4)),
                Success = reader.GetInt64(5) != 0,
                IsReversible = reader.GetInt64(6) != 0,
                RollbackNote = reader.GetString(7),
                Message = reader.GetString(8),
                CreatedUtc = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind)
            });
        }

        return results;
    }

    // ── Execution history rollups (C-041) ──────────────────────────────────

    public async Task<IReadOnlyList<OptimizationExecutionRollup>> GetExecutionRollupsAsync(int limit = 20, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fix_kind,
                   COUNT(*) AS total_count,
                   SUM(CASE WHEN action = 'Applied' THEN 1 ELSE 0 END) AS applied_count,
                   SUM(CASE WHEN action = 'Reverted' THEN 1 ELSE 0 END) AS reverted_count,
                   SUM(CASE WHEN action = 'Failed' THEN 1 ELSE 0 END) AS failed_count,
                   SUM(CASE WHEN is_reversible = 1 THEN 1 ELSE 0 END) AS reversible_count,
                   MAX(created_utc) AS most_recent_utc
            FROM optimization_execution_history
            GROUP BY fix_kind
            ORDER BY total_count DESC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<OptimizationExecutionRollup>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new OptimizationExecutionRollup
            {
                Kind = Enum.Parse<OptimizationKind>(reader.GetString(0)),
                TotalCount = reader.GetInt32(1),
                AppliedCount = reader.GetInt32(2),
                RevertedCount = reader.GetInt32(3),
                FailedCount = reader.GetInt32(4),
                ReversibleCount = reader.GetInt32(5),
                MostRecentUtc = reader.GetString(6)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<OptimizationExecutionSummary>> GetRecentExecutionSummariesAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT record_id, fix_kind, target, action, success, is_reversible, rollback_note, created_utc
            FROM optimization_execution_history
            ORDER BY created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<OptimizationExecutionSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new OptimizationExecutionSummary
            {
                RecordId = reader.GetString(0),
                Kind = Enum.Parse<OptimizationKind>(reader.GetString(1)),
                Target = reader.GetString(2),
                Action = Enum.Parse<OptimizationExecutionAction>(reader.GetString(3)),
                Success = reader.GetInt64(4) != 0,
                IsReversible = reader.GetInt64(5) != 0,
                HasRollbackData = !string.IsNullOrWhiteSpace(reader.GetString(6)),
                CreatedUtc = reader.GetString(7)
            });
        }

        return results;
    }

    public async Task<OptimizationExecutionRecord?> GetExecutionRecordAsync(string recordId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT record_id, plan_id, fix_kind, target, action, success, is_reversible, rollback_note, message, created_utc
            FROM optimization_execution_history
            WHERE record_id = @record_id
            """;
        command.Parameters.AddWithValue("@record_id", recordId);

        var results = await ReadExecutionRecordsAsync(command, ct);
        return results.Count > 0 ? results[0] : null;
    }
}
