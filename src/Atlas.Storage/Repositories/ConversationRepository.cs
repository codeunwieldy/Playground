using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of the conversation and prompt trace repository.
/// </summary>
public sealed class ConversationRepository(SqliteConnectionFactory connectionFactory) : IConversationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SaveConversationAsync(Conversation conversation, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(conversation.ConversationId))
        {
            conversation.ConversationId = Guid.NewGuid().ToString("N");
        }

        var json = JsonSerializer.Serialize(conversation, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var createdUtc = conversation.CreatedUtc != default
            ? conversation.CreatedUtc.ToString("o")
            : DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations (conversation_id, kind, summary, payload, created_utc, expires_utc, message_count)
            VALUES (@conversation_id, @kind, @summary, @payload, @created_utc, @expires_utc, @message_count)
            """;
        command.Parameters.AddWithValue("@conversation_id", conversation.ConversationId);
        command.Parameters.AddWithValue("@kind", conversation.Kind.ToString());
        command.Parameters.AddWithValue("@summary", conversation.Summary);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);
        command.Parameters.AddWithValue("@expires_utc",
            conversation.ExpiresUtc.HasValue
                ? conversation.ExpiresUtc.Value.ToString("o")
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("@message_count", conversation.Messages.Count);

        await command.ExecuteNonQueryAsync(ct);

        // Index in FTS for full-text search
        var content = BuildFtsContent(conversation);

        await using var ftsCommand = connection.CreateCommand();
        ftsCommand.CommandText = """
            INSERT INTO conversation_fts (conversation_id, summary, content)
            VALUES (@conversation_id, @summary, @content)
            """;
        ftsCommand.Parameters.AddWithValue("@conversation_id", conversation.ConversationId);
        ftsCommand.Parameters.AddWithValue("@summary", conversation.Summary);
        ftsCommand.Parameters.AddWithValue("@content", content);

        await ftsCommand.ExecuteNonQueryAsync(ct);

        return conversation.ConversationId;
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM conversations WHERE conversation_id = @conversation_id";
        command.Parameters.AddWithValue("@conversation_id", conversationId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<Conversation>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT conversation_id, kind, summary, created_utc
            FROM conversations
            ORDER BY created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<ConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ConversationSummary(
                ConversationId: reader.GetString(0),
                Kind: Enum.Parse<ConversationKind>(reader.GetString(1)),
                Summary: reader.GetString(2),
                CreatedUtc: DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<IReadOnlyList<ConversationSearchResult>> SearchConversationsAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT conversation_id, summary, snippet(conversation_fts, 2, '<b>', '</b>', '...', 32) AS snip, rank
            FROM conversation_fts
            WHERE conversation_fts MATCH @query
            ORDER BY rank
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@query", query);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<ConversationSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ConversationSearchResult(
                ConversationId: reader.GetString(0),
                Summary: reader.GetString(1),
                Snippet: reader.GetString(2),
                Rank: reader.GetDouble(3)
            ));
        }

        return results;
    }

    public async Task<bool> DeleteConversationAsync(string conversationId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var ftsCommand = connection.CreateCommand();
        ftsCommand.CommandText = "DELETE FROM conversation_fts WHERE conversation_id = @conversation_id";
        ftsCommand.Parameters.AddWithValue("@conversation_id", conversationId);
        await ftsCommand.ExecuteNonQueryAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversations WHERE conversation_id = @conversation_id";
        command.Parameters.AddWithValue("@conversation_id", conversationId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<IReadOnlyList<string>> GetExpiredConversationIdsAsync(DateTime asOfUtc, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT conversation_id
            FROM conversations
            WHERE expires_utc IS NOT NULL AND expires_utc < @as_of_utc
            """;
        command.Parameters.AddWithValue("@as_of_utc", asOfUtc.ToString("o"));

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task<string> SavePromptTraceAsync(PromptTrace trace, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(trace.TraceId))
        {
            trace.TraceId = Guid.NewGuid().ToString("N");
        }

        var promptPayload = AtlasJsonCompression.Compress(trace.PromptPayload);
        var responsePayload = AtlasJsonCompression.Compress(trace.ResponsePayload);
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO prompt_traces (trace_id, stage, prompt_payload, response_payload, created_utc)
            VALUES (@trace_id, @stage, @prompt_payload, @response_payload, @created_utc)
            """;
        command.Parameters.AddWithValue("@trace_id", trace.TraceId);
        command.Parameters.AddWithValue("@stage", trace.Stage);
        command.Parameters.AddWithValue("@prompt_payload", promptPayload);
        command.Parameters.AddWithValue("@response_payload", responsePayload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);

        await command.ExecuteNonQueryAsync(ct);
        return trace.TraceId;
    }

    public async Task<PromptTrace?> GetPromptTraceAsync(string traceId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT trace_id, stage, prompt_payload, response_payload, created_utc
            FROM prompt_traces
            WHERE trace_id = @trace_id
            """;
        command.Parameters.AddWithValue("@trace_id", traceId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var promptPayload = (byte[])reader["prompt_payload"];
        var responsePayload = (byte[])reader["response_payload"];

        return new PromptTrace
        {
            TraceId = reader.GetString(0),
            Stage = reader.GetString(1),
            PromptPayload = AtlasJsonCompression.Decompress(promptPayload),
            ResponsePayload = AtlasJsonCompression.Decompress(responsePayload),
            CreatedUtc = DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind)
        };
    }

    public async Task<IReadOnlyList<PromptTraceSummary>> ListPromptTracesAsync(string? stage = null, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();

        if (stage is not null)
        {
            command.CommandText = """
                SELECT trace_id, stage, created_utc
                FROM prompt_traces
                WHERE stage = @stage
                ORDER BY created_utc DESC
                LIMIT @limit OFFSET @offset
                """;
            command.Parameters.AddWithValue("@stage", stage);
        }
        else
        {
            command.CommandText = """
                SELECT trace_id, stage, created_utc
                FROM prompt_traces
                ORDER BY created_utc DESC
                LIMIT @limit OFFSET @offset
                """;
        }

        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<PromptTraceSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new PromptTraceSummary(
                TraceId: reader.GetString(0),
                Stage: reader.GetString(1),
                CreatedUtc: DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<bool> DeletePromptTraceAsync(string traceId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM prompt_traces WHERE trace_id = @trace_id";
        command.Parameters.AddWithValue("@trace_id", traceId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    // ── Compaction (C-035) ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<CompactableConversation>> GetCompactableCandidatesAsync(DateTime olderThan, int minMessages, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT conversation_id, kind, summary, message_count, created_utc
            FROM conversations
            WHERE is_compacted = 0
              AND created_utc < @older_than
              AND message_count >= @min_messages
            ORDER BY created_utc ASC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@older_than", olderThan.ToString("o"));
        command.Parameters.AddWithValue("@min_messages", minMessages);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<CompactableConversation>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new CompactableConversation(
                ConversationId: reader.GetString(0),
                Kind: Enum.Parse<ConversationKind>(reader.GetString(1)),
                Summary: reader.GetString(2),
                MessageCount: reader.GetInt32(3),
                CreatedUtc: DateTime.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<string> SaveSummaryAsync(ConversationSummaryRecord summary, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(summary.SummaryId))
        {
            summary.SummaryId = Guid.NewGuid().ToString("N");
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversation_summaries (summary_id, conversation_id, covered_from_utc, covered_until_utc, message_count, summary_text, created_utc, is_compacted)
            VALUES (@summary_id, @conversation_id, @covered_from_utc, @covered_until_utc, @message_count, @summary_text, @created_utc, @is_compacted)
            """;
        command.Parameters.AddWithValue("@summary_id", summary.SummaryId);
        command.Parameters.AddWithValue("@conversation_id", summary.ConversationId);
        command.Parameters.AddWithValue("@covered_from_utc", summary.CoveredFromUtc.ToString("o"));
        command.Parameters.AddWithValue("@covered_until_utc", summary.CoveredUntilUtc.ToString("o"));
        command.Parameters.AddWithValue("@message_count", summary.MessageCount);
        command.Parameters.AddWithValue("@summary_text", summary.SummaryText);
        command.Parameters.AddWithValue("@created_utc", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("@is_compacted", summary.IsCompacted ? 1 : 0);

        await command.ExecuteNonQueryAsync(ct);
        return summary.SummaryId;
    }

    public async Task MarkCompactedAsync(string conversationId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE conversations SET is_compacted = 1 WHERE conversation_id = @conversation_id";
        command.Parameters.AddWithValue("@conversation_id", conversationId);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationSummaryRecord>> GetSummariesForConversationAsync(string conversationId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT summary_id, conversation_id, covered_from_utc, covered_until_utc, message_count, summary_text, created_utc, is_compacted
            FROM conversation_summaries
            WHERE conversation_id = @conversation_id
            ORDER BY created_utc ASC
            """;
        command.Parameters.AddWithValue("@conversation_id", conversationId);

        var results = new List<ConversationSummaryRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ConversationSummaryRecord
            {
                SummaryId = reader.GetString(0),
                ConversationId = reader.GetString(1),
                CoveredFromUtc = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
                CoveredUntilUtc = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
                MessageCount = reader.GetInt32(4),
                SummaryText = reader.GetString(5),
                CreatedUtc = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                IsCompacted = reader.GetInt32(7) != 0
            });
        }

        return results;
    }

    // ── Summary query (C-039) ──────────────────────────────────────────────

    public async Task<IReadOnlyList<ConversationSummaryRecord>> ListSummariesAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT summary_id, conversation_id, covered_from_utc, covered_until_utc, message_count, summary_text, created_utc, is_compacted
            FROM conversation_summaries
            ORDER BY created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<ConversationSummaryRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ConversationSummaryRecord
            {
                SummaryId = reader.GetString(0),
                ConversationId = reader.GetString(1),
                CoveredFromUtc = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
                CoveredUntilUtc = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
                MessageCount = reader.GetInt32(4),
                SummaryText = reader.GetString(5),
                CreatedUtc = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                IsCompacted = reader.GetInt32(7) != 0
            });
        }

        return results;
    }

    public async Task<int> GetSummaryCountAsync(CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM conversation_summaries";

        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    // ── Summary snapshot integration (C-043) ──────────────────────────────

    public async Task<(int CompactedCount, int RetainedCount)> GetSummaryCompactionCountsAsync(CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN is_compacted = 1 THEN 1 ELSE 0 END) AS compacted_count,
                SUM(CASE WHEN is_compacted = 0 THEN 1 ELSE 0 END) AS retained_count
            FROM conversation_summaries
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var compacted = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var retained = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return (compacted, retained);
        }

        return (0, 0);
    }

    public async Task<(int CompactedConversations, int NonCompactedConversations)> GetConversationCompactionCountsAsync(CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN is_compacted = 1 THEN 1 ELSE 0 END) AS compacted,
                SUM(CASE WHEN is_compacted = 0 THEN 1 ELSE 0 END) AS non_compacted
            FROM conversations
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var compacted = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var nonCompacted = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return (compacted, nonCompacted);
        }

        return (0, 0);
    }

    public async Task<DateTime?> GetMostRecentSummaryUtcAsync(CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(created_utc) FROM conversation_summaries";

        var result = await command.ExecuteScalarAsync(ct);
        if (result is string s && !string.IsNullOrWhiteSpace(s))
        {
            return DateTime.Parse(s, null, DateTimeStyles.RoundtripKind);
        }

        return null;
    }

    private static string BuildFtsContent(Conversation conversation)
    {
        var sb = new StringBuilder();
        foreach (var message in conversation.Messages)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(message.Content);
        }

        return sb.ToString();
    }
}
