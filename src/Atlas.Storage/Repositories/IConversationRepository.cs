namespace Atlas.Storage.Repositories;

/// <summary>
/// Repository for storing and searching conversations and prompt traces.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Persists a conversation and returns its identifier.
    /// </summary>
    Task<string> SaveConversationAsync(Conversation conversation, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a conversation by its identifier.
    /// </summary>
    Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default);

    /// <summary>
    /// Lists conversation summaries with pagination support.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Searches conversations using full-text search.
    /// </summary>
    Task<IReadOnlyList<ConversationSearchResult>> SearchConversationsAsync(string query, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation by its identifier.
    /// </summary>
    Task<bool> DeleteConversationAsync(string conversationId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves expired conversations based on their expiration time.
    /// </summary>
    Task<IReadOnlyList<string>> GetExpiredConversationIdsAsync(DateTime asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Persists a prompt trace and returns its identifier.
    /// </summary>
    Task<string> SavePromptTraceAsync(PromptTrace trace, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a prompt trace by its identifier.
    /// </summary>
    Task<PromptTrace?> GetPromptTraceAsync(string traceId, CancellationToken ct = default);

    /// <summary>
    /// Lists prompt traces filtered by stage with pagination support.
    /// </summary>
    Task<IReadOnlyList<PromptTraceSummary>> ListPromptTracesAsync(string? stage = null, int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Deletes a prompt trace by its identifier.
    /// </summary>
    Task<bool> DeletePromptTraceAsync(string traceId, CancellationToken ct = default);
}

/// <summary>
/// The kind of conversation.
/// </summary>
public enum ConversationKind
{
    Unknown = 0,
    UserChat = 1,
    SystemAnalysis = 2,
    PlanGeneration = 3,
    Optimization = 4
}

/// <summary>
/// Represents a stored conversation with the AI.
/// </summary>
public sealed class Conversation
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString("N");
    public ConversationKind Kind { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
    public DateTime CreatedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

/// <summary>
/// A single message within a conversation.
/// </summary>
public sealed class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}

/// <summary>
/// Summary projection of a conversation for listing purposes.
/// </summary>
/// <param name="ConversationId">Unique identifier of the conversation.</param>
/// <param name="Kind">The type of conversation.</param>
/// <param name="Summary">Brief description of the conversation.</param>
/// <param name="CreatedUtc">When the conversation was created.</param>
public sealed record ConversationSummary(string ConversationId, ConversationKind Kind, string Summary, DateTime CreatedUtc);

/// <summary>
/// Result from a full-text search on conversations.
/// </summary>
/// <param name="ConversationId">Unique identifier of the matching conversation.</param>
/// <param name="Summary">Brief description of the conversation.</param>
/// <param name="Snippet">Highlighted snippet showing the match context.</param>
/// <param name="Rank">Relevance score from FTS5.</param>
public sealed record ConversationSearchResult(string ConversationId, string Summary, string Snippet, double Rank);

/// <summary>
/// Represents a traced prompt/response pair for debugging and analysis.
/// </summary>
public sealed class PromptTrace
{
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
    public string Stage { get; set; } = string.Empty;
    public string PromptPayload { get; set; } = string.Empty;
    public string ResponsePayload { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// Summary projection of a prompt trace for listing purposes.
/// </summary>
/// <param name="TraceId">Unique identifier of the trace.</param>
/// <param name="Stage">The pipeline stage that generated this trace.</param>
/// <param name="CreatedUtc">When the trace was recorded.</param>
public sealed record PromptTraceSummary(string TraceId, string Stage, DateTime CreatedUtc);
