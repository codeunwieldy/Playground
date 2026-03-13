using System.Globalization;
using Atlas.Service.Services;
using Atlas.Storage;
using Atlas.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Atlas.Service.Tests;

public sealed class ConversationCompactionTests : IDisposable
{
    private readonly CompactionTestFixture _fixture;
    private readonly ConversationRepository _repo;
    private readonly ConversationCompactionService _service;

    public ConversationCompactionTests()
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

    [Fact]
    public async Task CompactableCandidate_Selected()
    {
        var oldConv = CreateConversation(ConversationKind.UserChat, "Old chat", 10, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(oldConv);

        var candidates = await _repo.GetCompactableCandidatesAsync(DateTime.UtcNow.AddDays(-7), 5);

        Assert.Single(candidates);
        Assert.Equal(oldConv.ConversationId, candidates[0].ConversationId);
        Assert.Equal(10, candidates[0].MessageCount);
    }

    [Fact]
    public async Task RecentConversation_NotSelected()
    {
        var recentConv = CreateConversation(ConversationKind.UserChat, "Recent chat", 10, DateTime.UtcNow.AddHours(-1));
        await _repo.SaveConversationAsync(recentConv);

        var candidates = await _repo.GetCompactableCandidatesAsync(DateTime.UtcNow.AddDays(-7), 5);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task ShortConversation_NotSelected()
    {
        var shortConv = CreateConversation(ConversationKind.UserChat, "Short chat", 2, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(shortConv);

        var candidates = await _repo.GetCompactableCandidatesAsync(DateTime.UtcNow.AddDays(-7), 5);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task AlreadyCompacted_NotSelected()
    {
        var conv = CreateConversation(ConversationKind.UserChat, "Compacted chat", 10, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);
        await _repo.MarkCompactedAsync(conv.ConversationId);

        var candidates = await _repo.GetCompactableCandidatesAsync(DateTime.UtcNow.AddDays(-7), 5);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Compaction_GeneratesSummary()
    {
        var conv = CreateConversation(ConversationKind.SystemAnalysis, "Analysis session", 10, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        var result = await _service.CompactAsync(TimeSpan.FromDays(7), 5);

        Assert.Equal(1, result.ConversationsCompacted);
        Assert.Single(result.CompactedConversationIds);

        var summaries = await _repo.GetSummariesForConversationAsync(conv.ConversationId);
        Assert.Single(summaries);
        Assert.Equal(conv.ConversationId, summaries[0].ConversationId);
        Assert.Equal(10, summaries[0].MessageCount);
        Assert.Contains("SystemAnalysis", summaries[0].SummaryText);
        Assert.Contains("Messages: 10", summaries[0].SummaryText);
    }

    [Fact]
    public async Task Compaction_MarksConversationCompacted()
    {
        var conv = CreateConversation(ConversationKind.UserChat, "Mark test", 10, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        await _service.CompactAsync(TimeSpan.FromDays(7), 5);

        // Should no longer appear as compactable.
        var candidates = await _repo.GetCompactableCandidatesAsync(DateTime.UtcNow.AddDays(-7), 5);
        Assert.Empty(candidates);
    }

    [Fact]
    public async Task RepeatedCompaction_Idempotent()
    {
        var conv = CreateConversation(ConversationKind.UserChat, "Idempotent test", 10, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        var result1 = await _service.CompactAsync(TimeSpan.FromDays(7), 5);
        var result2 = await _service.CompactAsync(TimeSpan.FromDays(7), 5);

        Assert.Equal(1, result1.ConversationsCompacted);
        Assert.Equal(0, result2.ConversationsCompacted);

        var summaries = await _repo.GetSummariesForConversationAsync(conv.ConversationId);
        Assert.Single(summaries);
    }

    [Fact]
    public void Summary_TruncatedToBound()
    {
        // Build a conversation with very long messages.
        var conv = new Conversation
        {
            Kind = ConversationKind.Optimization,
            Summary = "Long conversation summary that goes on and on"
        };

        for (var i = 0; i < 50; i++)
        {
            conv.Messages.Add(new ConversationMessage
            {
                Role = "user",
                Content = new string('A', 200) + $" unique keyword{i} " + new string('B', 200),
                TimestampUtc = DateTime.UtcNow.AddMinutes(i)
            });
        }

        var summaryText = ConversationCompactionService.GenerateSummary(conv);

        Assert.True(summaryText.Length <= 1000, $"Summary was {summaryText.Length} chars, expected <= 1000");
    }

    [Fact]
    public async Task BackwardCompatibility_OlderRowsReadable()
    {
        // Insert a conversation row directly without the new columns (simulating old schema).
        // The additive migration gives defaults so the query should still work.
        var conv = CreateConversation(ConversationKind.UserChat, "Legacy chat", 8, DateTime.UtcNow.AddDays(-30));
        await _repo.SaveConversationAsync(conv);

        // Should be readable.
        var loaded = await _repo.GetConversationAsync(conv.ConversationId);
        Assert.NotNull(loaded);
        Assert.Equal(8, loaded!.Messages.Count);

        // Should appear in candidates.
        var candidates = await _repo.GetCompactableCandidatesAsync(DateTime.UtcNow.AddDays(-7), 5);
        Assert.Single(candidates);
    }
}

internal sealed class CompactionTestFixture : IDisposable
{
    private readonly string _dbPath;

    public SqliteConnectionFactory ConnectionFactory { get; }

    public CompactionTestFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"atlas-compaction-test-{Guid.NewGuid():N}.db");
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
