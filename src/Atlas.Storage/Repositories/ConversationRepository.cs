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
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations (conversation_id, kind, summary, payload, created_utc, expires_utc)
            VALUES (@conversation_id, @kind, @summary, @payload, @created_utc, @expires_utc)
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
