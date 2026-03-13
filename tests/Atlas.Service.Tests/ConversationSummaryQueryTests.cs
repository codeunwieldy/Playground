using System.Globalization;
using Atlas.Storage;
using Atlas.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Atlas.Service.Tests;

public sealed class ConversationSummaryQueryTests : IDisposable
{
    private readonly SummaryQueryTestFixture _fixture;
    private readonly ConversationRepository _repo;

    public ConversationSummaryQueryTests()
    {
        _fixture = new SummaryQueryTestFixture();
        _repo = new ConversationRepository(_fixture.ConnectionFactory);
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
                Content = $"Message number {i} with content about summary query testing.",
                TimestampUtc = createdUtc.AddMinutes(i)
            });
        }

        return conv;
    }

    private ConversationSummaryRecord CreateSummaryRecord(string conversationId, int messageCount, DateTime coveredFrom, DateTime coveredUntil, bool isCompacted = true)
    {
        return new ConversationSummaryRecord
        {
            ConversationId = conversationId,
            CoveredFromUtc = coveredFrom,
            CoveredUntilUtc = coveredUntil,
            MessageCount = messageCount,
            SummaryText = $"Summary for {conversationId}: {messageCount} messages compacted.",
            CreatedUtc = DateTime.UtcNow,
            IsCompacted = isCompacted
        };
    }

    // ── ListSummariesAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListSummariesAsync_WithData_ReturnsPaginatedResults()
    {
        var conv1 = CreateConversation(ConversationKind.UserChat, "Chat 1", 8, DateTime.UtcNow.AddDays(-10));
        var conv2 = CreateConversation(ConversationKind.SystemAnalysis, "Analysis 1", 12, DateTime.UtcNow.AddDays(-5));
        await _repo.SaveConversationAsync(conv1);
        await _repo.SaveConversationAsync(conv2);

        var summary1 = CreateSummaryRecord(conv1.ConversationId, 8, conv1.CreatedUtc, conv1.CreatedUtc.AddMinutes(7));
        var summary2 = CreateSummaryRecord(conv2.ConversationId, 12, conv2.CreatedUtc, conv2.CreatedUtc.AddMinutes(11));
        await _repo.SaveSummaryAsync(summary1);
        await _repo.SaveSummaryAsync(summary2);

        var results = await _repo.ListSummariesAsync(limit: 50, offset: 0);

        Assert.Equal(2, results.Count);
        // Ordered by created_utc DESC, so summary2 should come first (it was saved second with a later created_utc).
        Assert.Equal(conv2.ConversationId, results[0].ConversationId);
        Assert.Equal(12, results[0].MessageCount);
        Assert.Equal(conv1.ConversationId, results[1].ConversationId);
        Assert.Equal(8, results[1].MessageCount);
    }

    [Fact]
    public async Task ListSummariesAsync_Empty_ReturnsEmptyList()
    {
        var results = await _repo.ListSummariesAsync();

        Assert.Empty(results);
    }

    // ── GetSummaryCountAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryCountAsync_ReturnsCorrectCount()
    {
        var conv = CreateConversation(ConversationKind.UserChat, "Count test", 10, DateTime.UtcNow.AddDays(-20));
        await _repo.SaveConversationAsync(conv);

        var s1 = CreateSummaryRecord(conv.ConversationId, 5, conv.CreatedUtc, conv.CreatedUtc.AddMinutes(4));
        var s2 = CreateSummaryRecord(conv.ConversationId, 5, conv.CreatedUtc.AddMinutes(5), conv.CreatedUtc.AddMinutes(9));
        await _repo.SaveSummaryAsync(s1);
        await _repo.SaveSummaryAsync(s2);

        var count = await _repo.GetSummaryCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetSummaryCountAsync_Empty_ReturnsZero()
    {
        var count = await _repo.GetSummaryCountAsync();

        Assert.Equal(0, count);
    }

    // ── GetSummariesForConversationAsync ────────────────────────────────────

    [Fact]
    public async Task GetSummariesForConversationAsync_WithSummaries_ReturnsRecords()
    {
        var conv = CreateConversation(ConversationKind.PlanGeneration, "Plan session", 15, DateTime.UtcNow.AddDays(-14));
        await _repo.SaveConversationAsync(conv);

        var s1 = CreateSummaryRecord(conv.ConversationId, 8, conv.CreatedUtc, conv.CreatedUtc.AddMinutes(7));
        var s2 = CreateSummaryRecord(conv.ConversationId, 7, conv.CreatedUtc.AddMinutes(8), conv.CreatedUtc.AddMinutes(14));
        await _repo.SaveSummaryAsync(s1);
        await _repo.SaveSummaryAsync(s2);

        var results = await _repo.GetSummariesForConversationAsync(conv.ConversationId);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(conv.ConversationId, r.ConversationId));
        Assert.Equal(8, results[0].MessageCount);
        Assert.Equal(7, results[1].MessageCount);
    }

    [Fact]
    public async Task GetSummariesForConversationAsync_NoSummaries_ReturnsEmpty()
    {
        var conv = CreateConversation(ConversationKind.UserChat, "No summaries", 3, DateTime.UtcNow.AddDays(-2));
        await _repo.SaveConversationAsync(conv);

        var results = await _repo.GetSummariesForConversationAsync(conv.ConversationId);

        Assert.Empty(results);
    }

    // ── Pagination offset/limit ────────────────────────────────────────────

    [Fact]
    public async Task ListSummariesAsync_PaginationOffsetLimit_Respected()
    {
        var conv = CreateConversation(ConversationKind.UserChat, "Pagination test", 20, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        // Insert 5 summaries with slightly different created times so ordering is deterministic.
        for (var i = 0; i < 5; i++)
        {
            var s = new ConversationSummaryRecord
            {
                ConversationId = conv.ConversationId,
                CoveredFromUtc = conv.CreatedUtc.AddMinutes(i * 4),
                CoveredUntilUtc = conv.CreatedUtc.AddMinutes((i * 4) + 3),
                MessageCount = 4,
                SummaryText = $"Page summary {i}",
                CreatedUtc = DateTime.UtcNow.AddSeconds(i), // Ensure ordering
                IsCompacted = true
            };
            await _repo.SaveSummaryAsync(s);
        }

        // Page 1: limit 2, offset 0 => should return 2 items.
        var page1 = await _repo.ListSummariesAsync(limit: 2, offset: 0);
        Assert.Equal(2, page1.Count);

        // Page 2: limit 2, offset 2 => should return 2 items.
        var page2 = await _repo.ListSummariesAsync(limit: 2, offset: 2);
        Assert.Equal(2, page2.Count);

        // Page 3: limit 2, offset 4 => should return 1 item.
        var page3 = await _repo.ListSummariesAsync(limit: 2, offset: 4);
        Assert.Single(page3);

        // Total count should remain 5 regardless of pagination.
        var totalCount = await _repo.GetSummaryCountAsync();
        Assert.Equal(5, totalCount);

        // Pages should not overlap.
        var allIds = page1.Select(s => s.SummaryId)
            .Concat(page2.Select(s => s.SummaryId))
            .Concat(page3.Select(s => s.SummaryId))
            .ToList();
        Assert.Equal(5, allIds.Distinct().Count());
    }
}

internal sealed class SummaryQueryTestFixture : IDisposable
{
    private readonly string _dbPath;

    public SqliteConnectionFactory ConnectionFactory { get; }

    public SummaryQueryTestFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"atlas-summary-query-test-{Guid.NewGuid():N}.db");
        var dataRoot = Path.GetDirectoryName(_dbPath)!;
        var fileName = Path.GetFileName(_dbPath);

        var bootstrapper = new AtlasDatabaseBootstrapper(new StorageOptions
        {
            DataRoot = dataRoot,
            DatabaseFileName = fileName
        });

        bootstrapper.InitializeAsync().GetAwaiter().GetResult();
        ConnectionFactory = new SqliteConnectionFactory(bootstrapper);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
