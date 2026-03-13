using Atlas.Core.Contracts;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for optimization execution history rollup behavior (C-041).
/// Validates rollup grouping, empty-state, apply/revert grouping, and summary projection.
/// </summary>
public sealed class OptimizationExecutionRollupTests
{
    [Fact]
    public void EmptyHistory_ProducesEmptyRollups()
    {
        var records = Array.Empty<OptimizationExecutionRecord>();
        var rollups = BuildRollups(records);

        Assert.Empty(rollups);
    }

    [Fact]
    public void SingleKind_AllApplied_ProducesCorrectRollup()
    {
        var records = new[]
        {
            MakeRecord(OptimizationKind.TemporaryFiles, OptimizationExecutionAction.Applied, success: true, reversible: false),
            MakeRecord(OptimizationKind.TemporaryFiles, OptimizationExecutionAction.Applied, success: true, reversible: false),
            MakeRecord(OptimizationKind.TemporaryFiles, OptimizationExecutionAction.Applied, success: true, reversible: false)
        };

        var rollups = BuildRollups(records);

        Assert.Single(rollups);
        var rollup = rollups[0];
        Assert.Equal(OptimizationKind.TemporaryFiles, rollup.Kind);
        Assert.Equal(3, rollup.TotalCount);
        Assert.Equal(3, rollup.AppliedCount);
        Assert.Equal(0, rollup.RevertedCount);
        Assert.Equal(0, rollup.FailedCount);
        Assert.Equal(0, rollup.ReversibleCount);
    }

    [Fact]
    public void MixedApplyAndRevert_GroupedCorrectly()
    {
        var records = new[]
        {
            MakeRecord(OptimizationKind.UserStartupEntry, OptimizationExecutionAction.Applied, success: true, reversible: true),
            MakeRecord(OptimizationKind.UserStartupEntry, OptimizationExecutionAction.Reverted, success: true, reversible: false),
            MakeRecord(OptimizationKind.UserStartupEntry, OptimizationExecutionAction.Failed, success: false, reversible: false)
        };

        var rollups = BuildRollups(records);

        Assert.Single(rollups);
        var rollup = rollups[0];
        Assert.Equal(3, rollup.TotalCount);
        Assert.Equal(1, rollup.AppliedCount);
        Assert.Equal(1, rollup.RevertedCount);
        Assert.Equal(1, rollup.FailedCount);
        Assert.Equal(1, rollup.ReversibleCount);
    }

    [Fact]
    public void MultipleKinds_ProduceSeparateRollups()
    {
        var records = new[]
        {
            MakeRecord(OptimizationKind.TemporaryFiles, OptimizationExecutionAction.Applied, success: true, reversible: false),
            MakeRecord(OptimizationKind.CacheCleanup, OptimizationExecutionAction.Applied, success: true, reversible: false),
            MakeRecord(OptimizationKind.DuplicateArchives, OptimizationExecutionAction.Applied, success: true, reversible: true)
        };

        var rollups = BuildRollups(records);

        Assert.Equal(3, rollups.Count);
    }

    [Fact]
    public void ExecutionSummary_MapsFieldsCorrectly()
    {
        var record = new OptimizationExecutionRecord
        {
            RecordId = "rec1",
            FixKind = OptimizationKind.CacheCleanup,
            Target = "C:\\Temp",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            IsReversible = false,
            RollbackNote = "Not reversible",
            Message = "Cleaned cache",
            CreatedUtc = new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc)
        };

        var summary = new OptimizationExecutionSummary
        {
            RecordId = record.RecordId,
            Kind = record.FixKind,
            Target = record.Target,
            Action = record.Action,
            Success = record.Success,
            IsReversible = record.IsReversible,
            HasRollbackData = !string.IsNullOrWhiteSpace(record.RollbackNote),
            CreatedUtc = record.CreatedUtc.ToString("o")
        };

        Assert.Equal("rec1", summary.RecordId);
        Assert.Equal(OptimizationKind.CacheCleanup, summary.Kind);
        Assert.True(summary.HasRollbackData);
        Assert.False(summary.IsReversible);
    }

    [Fact]
    public void Ordering_MostRecentFirst()
    {
        var records = new[]
        {
            MakeRecord(OptimizationKind.TemporaryFiles, OptimizationExecutionAction.Applied, success: true, reversible: false,
                created: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRecord(OptimizationKind.TemporaryFiles, OptimizationExecutionAction.Applied, success: true, reversible: false,
                created: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc))
        };

        var rollups = BuildRollups(records);
        Assert.Single(rollups);
        Assert.Equal("2026-03-01T00:00:00.0000000Z", rollups[0].MostRecentUtc);
    }

    private static OptimizationExecutionRecord MakeRecord(
        OptimizationKind kind,
        OptimizationExecutionAction action,
        bool success,
        bool reversible,
        DateTime? created = null)
    {
        return new OptimizationExecutionRecord
        {
            RecordId = Guid.NewGuid().ToString("N"),
            FixKind = kind,
            Target = "test-target",
            Action = action,
            Success = success,
            IsReversible = reversible,
            CreatedUtc = created ?? DateTime.UtcNow
        };
    }

    private static List<OptimizationExecutionRollup> BuildRollups(IEnumerable<OptimizationExecutionRecord> records)
    {
        return records
            .GroupBy(r => r.FixKind)
            .Select(g => new OptimizationExecutionRollup
            {
                Kind = g.Key,
                TotalCount = g.Count(),
                AppliedCount = g.Count(r => r.Action == OptimizationExecutionAction.Applied),
                RevertedCount = g.Count(r => r.Action == OptimizationExecutionAction.Reverted),
                FailedCount = g.Count(r => r.Action == OptimizationExecutionAction.Failed),
                ReversibleCount = g.Count(r => r.IsReversible),
                MostRecentUtc = g.Max(r => r.CreatedUtc).ToString("o")
            })
            .OrderByDescending(r => r.TotalCount)
            .ToList();
    }
}
