using System.Text;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Services;

/// <summary>
/// Performs deterministic, local-first compaction of conversations into bounded summaries.
/// No AI model calls required — uses simple text extraction.
/// </summary>
public sealed class ConversationCompactionService(IConversationRepository conversationRepository)
{
    private const int MaxSummaryLength = 1000;
    private const int TopKeywordsCount = 10;
    private const int MinKeywordLength = 5;
    private const int FirstMessagesCount = 3;
    private const int LastMessagesCount = 2;
    private const int MessagePreviewLength = 80;

    /// <summary>
    /// Compacts conversations older than the retention window that have enough messages.
    /// </summary>
    public async Task<CompactionResult> CompactAsync(
        TimeSpan retentionWindow,
        int minMessagesToCompact,
        int maxCandidates = 100,
        CancellationToken ct = default)
    {
        var cutoffUtc = DateTime.UtcNow - retentionWindow;
        var candidates = await conversationRepository.GetCompactableCandidatesAsync(cutoffUtc, minMessagesToCompact, limit: maxCandidates, ct: ct);

        var compactedIds = new List<string>();

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var conversation = await conversationRepository.GetConversationAsync(candidate.ConversationId, ct);
            if (conversation is null || conversation.Messages.Count < minMessagesToCompact)
            {
                continue;
            }

            var summaryText = GenerateSummary(conversation);

            var firstMessage = conversation.Messages.MinBy(m => m.TimestampUtc);
            var lastMessage = conversation.Messages.MaxBy(m => m.TimestampUtc);

            var summaryRecord = new ConversationSummaryRecord
            {
                ConversationId = conversation.ConversationId,
                CoveredFromUtc = firstMessage?.TimestampUtc ?? conversation.CreatedUtc,
                CoveredUntilUtc = lastMessage?.TimestampUtc ?? conversation.CreatedUtc,
                MessageCount = conversation.Messages.Count,
                SummaryText = summaryText,
                IsCompacted = true
            };

            await conversationRepository.SaveSummaryAsync(summaryRecord, ct);
            await conversationRepository.MarkCompactedAsync(conversation.ConversationId, ct);
            compactedIds.Add(conversation.ConversationId);
        }

        return new CompactionResult
        {
            ConversationsEvaluated = candidates.Count,
            ConversationsCompacted = compactedIds.Count,
            CompactedConversationIds = compactedIds
        };
    }

    /// <summary>
    /// Generates a deterministic bounded summary from a conversation.
    /// </summary>
    internal static string GenerateSummary(Conversation conversation)
    {
        var messages = conversation.Messages;
        if (messages.Count == 0)
        {
            return $"[{conversation.Kind}] {conversation.Summary} | Messages: 0";
        }

        // Take first N and last M messages.
        var first = messages.Take(FirstMessagesCount).ToList();
        var last = messages.TakeLast(LastMessagesCount).ToList();

        var firstPreview = first.Count > 0
            ? Truncate(first[0].Content, MessagePreviewLength)
            : string.Empty;
        var lastPreview = last.Count > 0
            ? Truncate(last[^1].Content, MessagePreviewLength)
            : string.Empty;

        // Extract keywords from all messages.
        var keywords = ExtractKeywords(messages);
        var keywordStr = keywords.Count > 0
            ? string.Join(", ", keywords)
            : "general";

        var sb = new StringBuilder();
        sb.Append($"[{conversation.Kind}] {conversation.Summary}");
        sb.Append($" | Messages: {messages.Count}");
        sb.Append($" | Key topics: {keywordStr}");
        if (!string.IsNullOrWhiteSpace(firstPreview))
        {
            sb.Append($" | First: {firstPreview}");
        }
        if (!string.IsNullOrWhiteSpace(lastPreview))
        {
            sb.Append($" | Last: {lastPreview}");
        }

        return Truncate(sb.ToString(), MaxSummaryLength);
    }

    private static List<string> ExtractKeywords(List<ConversationMessage> messages)
    {
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            var words = message.Content
                .Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}'],
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                if (word.Length >= MinKeywordLength)
                {
                    wordCounts.TryGetValue(word, out var count);
                    wordCounts[word] = count + 1;
                }
            }
        }

        return wordCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopKeywordsCount)
            .Select(kv => kv.Key)
            .ToList();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 3)] + "...";
    }
}

/// <summary>
/// Result of a compaction run.
/// </summary>
public sealed class CompactionResult
{
    public int ConversationsEvaluated { get; set; }
    public int ConversationsCompacted { get; set; }
    public List<string> CompactedConversationIds { get; set; } = new();
}
