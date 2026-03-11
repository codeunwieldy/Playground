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
}
