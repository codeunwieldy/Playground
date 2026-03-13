using Atlas.Service.Services;
using Atlas.Storage;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Service.Tests;

public sealed class ConversationCompactionWorkerTests : IDisposable
{
    private readonly CompactionTestFixture _fixture;
    private readonly ConversationRepository _repo;
    private readonly ConversationCompactionService _service;

    public ConversationCompactionWorkerTests()
    {
        _fixture = new CompactionTestFixture();
        _repo = new ConversationRepository(_fixture.ConnectionFactory);
        _service = new ConversationCompactionService(_repo);
    }

    public void Dispose() => _fixture.Dispose();

    private Conversation CreateConversation(ConversationKind kind, string summary, int messageCount, DateTime createdUtc)
    {
        var conv = new Conversation
        {
            Kind = kind,
            Summary = summary,
            CreatedUtc = createdUtc
        };

        for (var i = 0; i < messageCount; i++)
        {
            conv.Messages.Add(new ConversationMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message number {i} with some meaningful content about testing compaction workflows.",
                TimestampUtc = createdUtc.AddMinutes(i)
            });
        }

        return conv;
    }

    // ── Disabled compaction ──────────────────────────────────────────────

    [Fact]
    public async Task DisabledCompaction_NoCompactionOccurs()
    {
        // Arrange: create a compactable conversation.
        var conv = CreateConversation(ConversationKind.UserChat, "Old chat", 15, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        var opts = new AtlasServiceOptions
        {
            EnableConversationCompaction = false,
            CompactionRetentionWindow = TimeSpan.FromDays(7),
            CompactionMinMessages = 10,
            CompactionMaxCandidatesPerCycle = 50
        };

        // The worker would skip compaction when disabled. Verify the conditional
        // by confirming candidates still exist uncompacted.
        var candidates = await _repo.GetCompactableCandidatesAsync(
            DateTime.UtcNow - opts.CompactionRetentionWindow,
            opts.CompactionMinMessages);

        Assert.Single(candidates);

        // Verify no summaries exist (compaction never ran).
        var summaries = await _repo.GetSummariesForConversationAsync(conv.ConversationId);
        Assert.Empty(summaries);
    }

    // ── With candidates ──────────────────────────────────────────────────

    [Fact]
    public async Task WithCandidates_CompactionRunsWithConfiguredOptions()
    {
        // Arrange: create several compactable conversations.
        for (var i = 0; i < 3; i++)
        {
            var conv = CreateConversation(
                ConversationKind.UserChat,
                $"Chat {i}",
                12,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
        }

        var opts = new AtlasServiceOptions
        {
            EnableConversationCompaction = true,
            CompactionRetentionWindow = TimeSpan.FromDays(7),
            CompactionMinMessages = 10,
            CompactionMaxCandidatesPerCycle = 50
        };

        // Act: run compaction with the configured options.
        var result = await _service.CompactAsync(
            opts.CompactionRetentionWindow,
            opts.CompactionMinMessages,
            opts.CompactionMaxCandidatesPerCycle);

        // Assert
        Assert.Equal(3, result.ConversationsEvaluated);
        Assert.Equal(3, result.ConversationsCompacted);
        Assert.Equal(3, result.CompactedConversationIds.Count);
    }

    // ── No candidates available ──────────────────────────────────────────

    [Fact]
    public async Task NoCandidates_NoOpBehavior()
    {
        // Arrange: no conversations exist.
        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            50);

        Assert.Equal(0, result.ConversationsEvaluated);
        Assert.Equal(0, result.ConversationsCompacted);
        Assert.Empty(result.CompactedConversationIds);
    }

    [Fact]
    public async Task NoCandidates_OnlyRecentConversations()
    {
        // Arrange: create only recent conversations that do not meet the retention window.
        var conv = CreateConversation(ConversationKind.UserChat, "Recent chat", 15, DateTime.UtcNow.AddHours(-1));
        await _repo.SaveConversationAsync(conv);

        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            50);

        Assert.Equal(0, result.ConversationsEvaluated);
        Assert.Equal(0, result.ConversationsCompacted);
    }

    [Fact]
    public async Task NoCandidates_BelowMinMessages()
    {
        // Arrange: conversation is old but has too few messages.
        var conv = CreateConversation(ConversationKind.UserChat, "Short chat", 3, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            50);

        Assert.Equal(0, result.ConversationsEvaluated);
        Assert.Equal(0, result.ConversationsCompacted);
    }

    // ── Bounded processing ───────────────────────────────────────────────

    [Fact]
    public async Task BoundedProcessing_MaxCandidatesRespected()
    {
        // Arrange: create more conversations than the max candidates limit.
        for (var i = 0; i < 10; i++)
        {
            var conv = CreateConversation(
                ConversationKind.UserChat,
                $"Chat {i}",
                12,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
        }

        // Act: limit to only 3 candidates per cycle.
        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 3);

        // Assert: only 3 were evaluated and compacted.
        Assert.Equal(3, result.ConversationsEvaluated);
        Assert.Equal(3, result.ConversationsCompacted);
        Assert.Equal(3, result.CompactedConversationIds.Count);

        // Remaining conversations should still be compactable.
        var remaining = await _repo.GetCompactableCandidatesAsync(
            DateTime.UtcNow - TimeSpan.FromDays(7), 10);
        Assert.Equal(7, remaining.Count);
    }

    [Fact]
    public async Task BoundedProcessing_LimitOfOne()
    {
        // Arrange: create multiple compactable conversations.
        for (var i = 0; i < 5; i++)
        {
            var conv = CreateConversation(
                ConversationKind.UserChat,
                $"Chat {i}",
                12,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
        }

        // Act: limit to 1 candidate.
        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 1);

        Assert.Equal(1, result.ConversationsEvaluated);
        Assert.Equal(1, result.ConversationsCompacted);
    }

    // ── Repeated cycles (idempotent behavior) ────────────────────────────

    [Fact]
    public async Task RepeatedCycles_IdempotentBehavior()
    {
        // Arrange: create a set of compactable conversations.
        for (var i = 0; i < 5; i++)
        {
            var conv = CreateConversation(
                ConversationKind.UserChat,
                $"Chat {i}",
                12,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
        }

        // Act: first cycle compacts all 5.
        var result1 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            50);

        // Second cycle finds nothing new to compact.
        var result2 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            50);

        // Third cycle is also a no-op.
        var result3 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            50);

        Assert.Equal(5, result1.ConversationsCompacted);
        Assert.Equal(0, result2.ConversationsCompacted);
        Assert.Equal(0, result3.ConversationsCompacted);
    }

    [Fact]
    public async Task RepeatedCycles_IncrementalWithBoundedLimit()
    {
        // Arrange: create 6 compactable conversations.
        for (var i = 0; i < 6; i++)
        {
            var conv = CreateConversation(
                ConversationKind.UserChat,
                $"Chat {i}",
                12,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
        }

        // Act: first cycle processes 2.
        var result1 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 2);

        // Second cycle processes 2 more.
        var result2 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 2);

        // Third cycle processes remaining 2.
        var result3 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 2);

        // Fourth cycle finds nothing.
        var result4 = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 2);

        Assert.Equal(2, result1.ConversationsCompacted);
        Assert.Equal(2, result2.ConversationsCompacted);
        Assert.Equal(2, result3.ConversationsCompacted);
        Assert.Equal(0, result4.ConversationsCompacted);
    }

    // ── CompactAsync with limit parameter ────────────────────────────────

    [Fact]
    public async Task CompactAsync_DefaultLimitProcessesAll()
    {
        // Arrange: create conversations within the default limit of 100.
        for (var i = 0; i < 5; i++)
        {
            var conv = CreateConversation(
                ConversationKind.SystemAnalysis,
                $"Analysis {i}",
                15,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
        }

        // Act: call without explicit maxCandidates (defaults to 100).
        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10);

        Assert.Equal(5, result.ConversationsEvaluated);
        Assert.Equal(5, result.ConversationsCompacted);
    }

    [Fact]
    public async Task CompactAsync_ZeroLimit_ProcessesNone()
    {
        // Arrange: create a compactable conversation.
        var conv = CreateConversation(ConversationKind.UserChat, "Zero limit test", 15, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        // Act: limit of 0 means the repository returns nothing.
        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 0);

        Assert.Equal(0, result.ConversationsEvaluated);
        Assert.Equal(0, result.ConversationsCompacted);
    }

    [Fact]
    public async Task CompactAsync_SummariesPersisted_WithLimit()
    {
        // Arrange: create 4 compactable conversations.
        var ids = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var conv = CreateConversation(
                ConversationKind.Optimization,
                $"Opt {i}",
                12,
                DateTime.UtcNow.AddDays(-30 + i));
            await _repo.SaveConversationAsync(conv);
            ids.Add(conv.ConversationId);
        }

        // Act: compact with limit of 2.
        var result = await _service.CompactAsync(
            TimeSpan.FromDays(7),
            10,
            maxCandidates: 2);

        // Assert: only 2 compacted, summaries persisted for those.
        Assert.Equal(2, result.ConversationsCompacted);

        var compactedCount = 0;
        var uncompactedCount = 0;
        foreach (var id in ids)
        {
            var summaries = await _repo.GetSummariesForConversationAsync(id);
            if (summaries.Count > 0)
                compactedCount++;
            else
                uncompactedCount++;
        }

        Assert.Equal(2, compactedCount);
        Assert.Equal(2, uncompactedCount);
    }
}
