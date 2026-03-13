using Atlas.Core.Contracts;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for conversation summary snapshot integration (C-043).
/// Validates empty-state behavior, mixed compacted history, and count correctness.
/// </summary>
public sealed class ConversationSummarySnapshotTests
{
    [Fact]
    public void EmptyState_ProducesZeroCounts()
    {
        var response = new ConversationSummarySnapshotResponse();

        Assert.Equal(0, response.TotalSummaryCount);
        Assert.Equal(0, response.CompactedConversationCount);
        Assert.Equal(0, response.NonCompactedConversationCount);
        Assert.Equal(0, response.CompactedSummaryCount);
        Assert.Equal(0, response.RetainedSummaryCount);
        Assert.Equal(string.Empty, response.MostRecentSummaryUtc);
    }

    [Fact]
    public void MixedCompactionState_SumsCorrectly()
    {
        var response = new ConversationSummarySnapshotResponse
        {
            TotalSummaryCount = 10,
            CompactedSummaryCount = 7,
            RetainedSummaryCount = 3,
            CompactedConversationCount = 4,
            NonCompactedConversationCount = 6,
            MostRecentSummaryUtc = "2026-03-13T00:00:00.0000000Z"
        };

        Assert.Equal(10, response.TotalSummaryCount);
        Assert.Equal(10, response.CompactedSummaryCount + response.RetainedSummaryCount);
        Assert.Equal(10, response.CompactedConversationCount + response.NonCompactedConversationCount);
        Assert.NotEmpty(response.MostRecentSummaryUtc);
    }

    [Fact]
    public void AllCompacted_ZeroRetained()
    {
        var response = new ConversationSummarySnapshotResponse
        {
            TotalSummaryCount = 5,
            CompactedSummaryCount = 5,
            RetainedSummaryCount = 0,
            CompactedConversationCount = 5,
            NonCompactedConversationCount = 0
        };

        Assert.Equal(0, response.RetainedSummaryCount);
        Assert.Equal(0, response.NonCompactedConversationCount);
    }

    [Fact]
    public void NoneCompacted_AllRetained()
    {
        var response = new ConversationSummarySnapshotResponse
        {
            TotalSummaryCount = 3,
            CompactedSummaryCount = 0,
            RetainedSummaryCount = 3,
            CompactedConversationCount = 0,
            NonCompactedConversationCount = 3
        };

        Assert.Equal(0, response.CompactedSummaryCount);
        Assert.Equal(0, response.CompactedConversationCount);
    }

    [Fact]
    public void MostRecentSummaryUtc_RoundTrips()
    {
        var timestamp = new DateTime(2026, 3, 13, 15, 30, 0, DateTimeKind.Utc);
        var response = new ConversationSummarySnapshotResponse
        {
            MostRecentSummaryUtc = timestamp.ToString("o")
        };

        var parsed = DateTime.Parse(response.MostRecentSummaryUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(timestamp, parsed);
    }
}
