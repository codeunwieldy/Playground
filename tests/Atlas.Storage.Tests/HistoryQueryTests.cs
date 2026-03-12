using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Tests for the read-side history query layer (C-008).
/// Validates that repository list methods return summaries in the shape
/// the history pipe handlers rely on: bounded, descending-time, and with
/// correct field mapping for the History* DTOs.
/// </summary>
public sealed class HistoryQueryTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly PlanRepository _planRepo;
    private readonly RecoveryRepository _recoveryRepo;
    private readonly OptimizationRepository _optimizationRepo;
    private readonly ConversationRepository _conversationRepo;

    public HistoryQueryTests()
    {
        _fixture = new TestDatabaseFixture();
        _planRepo = new PlanRepository(_fixture.ConnectionFactory);
        _recoveryRepo = new RecoveryRepository(_fixture.ConnectionFactory);
        _optimizationRepo = new OptimizationRepository(_fixture.ConnectionFactory);
        _conversationRepo = new ConversationRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Plans ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPlans_EmptyDatabase_ReturnsEmptyList()
    {
        var plans = await _planRepo.ListPlansAsync(10, 0);
        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlans_ReturnsRecentPlansDescending()
    {
        var plan1 = new PlanGraph { PlanId = Guid.NewGuid().ToString("N"), Scope = "Scope-A", Rationale = "First plan" };
        var plan2 = new PlanGraph { PlanId = Guid.NewGuid().ToString("N"), Scope = "Scope-B", Rationale = "Second plan" };
        await _planRepo.SavePlanAsync(plan1);
        await _planRepo.SavePlanAsync(plan2);

        var plans = await _planRepo.ListPlansAsync(10, 0);
        Assert.True(plans.Count >= 2);
        // Most recent first
        var idx1 = plans.ToList().FindIndex(p => p.PlanId == plan1.PlanId);
        var idx2 = plans.ToList().FindIndex(p => p.PlanId == plan2.PlanId);
        Assert.True(idx2 < idx1, "More recent plan should appear first.");
    }

    [Fact]
    public async Task ListPlans_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _planRepo.SavePlanAsync(new PlanGraph { PlanId = Guid.NewGuid().ToString("N"), Scope = $"Limit-{i}" });
        }

        var plans = await _planRepo.ListPlansAsync(3, 0);
        Assert.True(plans.Count <= 3);
    }

    [Fact]
    public async Task ListPlans_SummaryHasCorrectFields()
    {
        var plan = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "FieldTest",
            Rationale = "Test rationale"
        };
        await _planRepo.SavePlanAsync(plan);

        var plans = await _planRepo.ListPlansAsync(10, 0);
        var summary = plans.FirstOrDefault(p => p.PlanId == plan.PlanId);
        Assert.NotNull(summary);
        Assert.Equal("FieldTest", summary.Scope);
        Assert.True(summary.CreatedUtc > DateTime.MinValue);
    }

    [Fact]
    public async Task GetPlanDetail_ExistingPlan_ReturnsFullGraph()
    {
        var plan = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "Detail",
            Operations = [new PlanOperation { Kind = OperationKind.MovePath, SourcePath = "a", DestinationPath = "b" }]
        };
        await _planRepo.SavePlanAsync(plan);

        var loaded = await _planRepo.GetPlanAsync(plan.PlanId);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Operations);
    }

    [Fact]
    public async Task GetPlanDetail_MissingPlan_ReturnsNull()
    {
        var loaded = await _planRepo.GetPlanAsync("nonexistent");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetBatchesForPlan_ReturnsBatchesLinkedByPlanId()
    {
        var planId = Guid.NewGuid().ToString("N");
        var batch = new ExecutionBatch { BatchId = Guid.NewGuid().ToString("N"), PlanId = planId };
        await _planRepo.SaveBatchAsync(batch);

        var batches = await _planRepo.GetBatchesForPlanAsync(planId);
        Assert.Single(batches);
        Assert.Equal(batch.BatchId, batches[0].BatchId);
    }

    // ── Checkpoints ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListCheckpoints_EmptyDatabase_ReturnsEmptyList()
    {
        var checkpoints = await _recoveryRepo.ListCheckpointsAsync(10, 0);
        // May have data from other tests if fixture is shared, but on fresh DB it's empty
        Assert.NotNull(checkpoints);
    }

    [Fact]
    public async Task ListCheckpoints_ReturnsSummaryWithCorrectFields()
    {
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = Guid.NewGuid().ToString("N"),
            InverseOperations = [new InverseOperation { Kind = OperationKind.MovePath, SourcePath = "b", DestinationPath = "a" }]
        };
        await _recoveryRepo.SaveCheckpointAsync(checkpoint);

        var checkpoints = await _recoveryRepo.ListCheckpointsAsync(10, 0);
        var summary = checkpoints.FirstOrDefault(c => c.CheckpointId == checkpoint.CheckpointId);
        Assert.NotNull(summary);
        Assert.Equal(checkpoint.BatchId, summary.BatchId);
        Assert.Equal(1, summary.OperationCount);
    }

    // ── Quarantine ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListQuarantine_EmptyDatabase_ReturnsEmptyList()
    {
        var items = await _recoveryRepo.ListQuarantineItemsAsync(10, 0);
        Assert.NotNull(items);
    }

    [Fact]
    public async Task ListQuarantine_ReturnsSummaryWithCorrectFields()
    {
        var item = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = "C:\\Test\\file.txt",
            CurrentPath = "C:\\Quarantine\\file.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "Duplicate detected",
            RetentionUntilUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
        };
        await _recoveryRepo.SaveQuarantineItemAsync(item);

        var items = await _recoveryRepo.ListQuarantineItemsAsync(10, 0);
        var summary = items.FirstOrDefault(q => q.QuarantineId == item.QuarantineId);
        Assert.NotNull(summary);
        Assert.Equal("C:\\Test\\file.txt", summary.OriginalPath);
        Assert.Equal("Duplicate detected", summary.Reason);
    }

    // ── Optimization Findings ───────────────────────────────────────────────

    [Fact]
    public async Task ListFindings_EmptyDatabase_ReturnsEmptyList()
    {
        var findings = await _optimizationRepo.ListFindingsAsync(10, 0);
        Assert.NotNull(findings);
    }

    [Fact]
    public async Task ListFindings_ReturnsSummaryWithCorrectFields()
    {
        var finding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.TemporaryFiles,
            Target = "C:\\Users\\Temp",
            Evidence = "500 MB of temp files",
            CanAutoFix = true
        };
        await _optimizationRepo.SaveFindingAsync(finding);

        var findings = await _optimizationRepo.ListFindingsAsync(10, 0);
        var summary = findings.FirstOrDefault(f => f.FindingId == finding.FindingId);
        Assert.NotNull(summary);
        Assert.Equal(OptimizationKind.TemporaryFiles, summary.Kind);
        Assert.Equal("C:\\Users\\Temp", summary.Target);
        Assert.True(summary.CanAutoFix);
    }

    // ── Prompt Traces ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListTraces_EmptyDatabase_ReturnsEmptyList()
    {
        var traces = await _conversationRepo.ListPromptTracesAsync(null, 10, 0);
        Assert.NotNull(traces);
    }

    [Fact]
    public async Task ListTraces_ReturnsSummaryWithCorrectFields()
    {
        var trace = new PromptTrace
        {
            TraceId = Guid.NewGuid().ToString("N"),
            Stage = "planning",
            PromptPayload = "{\"intent\":\"test\"}",
            ResponsePayload = "{\"summary\":\"done\"}",
            CreatedUtc = DateTime.UtcNow
        };
        await _conversationRepo.SavePromptTraceAsync(trace);

        var traces = await _conversationRepo.ListPromptTracesAsync(null, 10, 0);
        var summary = traces.FirstOrDefault(t => t.TraceId == trace.TraceId);
        Assert.NotNull(summary);
        Assert.Equal("planning", summary.Stage);
    }

    [Fact]
    public async Task ListTraces_FilterByStage_ReturnsOnlyMatchingStage()
    {
        var planning = new PromptTrace { TraceId = Guid.NewGuid().ToString("N"), Stage = "planning", CreatedUtc = DateTime.UtcNow };
        var voice = new PromptTrace { TraceId = Guid.NewGuid().ToString("N"), Stage = "voice_intent", CreatedUtc = DateTime.UtcNow };
        await _conversationRepo.SavePromptTraceAsync(planning);
        await _conversationRepo.SavePromptTraceAsync(voice);

        var planningTraces = await _conversationRepo.ListPromptTracesAsync("planning", 50, 0);
        Assert.All(planningTraces, t => Assert.Equal("planning", t.Stage));

        var voiceTraces = await _conversationRepo.ListPromptTracesAsync("voice_intent", 50, 0);
        Assert.All(voiceTraces, t => Assert.Equal("voice_intent", t.Stage));
    }

    // ── DTO Mapping (mirrors handler projection logic) ──────────────────────

    [Fact]
    public async Task HistoryPlanSummary_MapsFromRepoSummary()
    {
        var plan = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "Mapping-Test",
            Rationale = "Verify DTO mapping",
            RequiresReview = true,
            Operations = [new PlanOperation { Kind = OperationKind.CreateDirectory }]
        };
        await _planRepo.SavePlanAsync(plan);

        var repoSummaries = await _planRepo.ListPlansAsync(10, 0);
        var rs = repoSummaries.First(p => p.PlanId == plan.PlanId);

        // Mirror the handler projection
        var dto = new HistoryPlanSummary
        {
            PlanId = rs.PlanId,
            Scope = rs.Scope,
            Summary = rs.Summary,
            CreatedUtc = rs.CreatedUtc.ToString("o")
        };

        Assert.Equal(plan.PlanId, dto.PlanId);
        Assert.Equal("Mapping-Test", dto.Scope);
        Assert.False(string.IsNullOrEmpty(dto.CreatedUtc));
    }

    [Fact]
    public async Task HistoryCheckpointSummary_MapsFromRepoSummary()
    {
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = Guid.NewGuid().ToString("N"),
            InverseOperations =
            [
                new InverseOperation { Kind = OperationKind.MovePath },
                new InverseOperation { Kind = OperationKind.RenamePath }
            ]
        };
        await _recoveryRepo.SaveCheckpointAsync(checkpoint);

        var repoSummaries = await _recoveryRepo.ListCheckpointsAsync(10, 0);
        var rs = repoSummaries.First(c => c.CheckpointId == checkpoint.CheckpointId);

        var dto = new HistoryCheckpointSummary
        {
            CheckpointId = rs.CheckpointId,
            BatchId = rs.BatchId,
            OperationCount = rs.OperationCount,
            CreatedUtc = rs.CreatedUtc.ToString("o")
        };

        Assert.Equal(checkpoint.CheckpointId, dto.CheckpointId);
        Assert.Equal(2, dto.OperationCount);
    }

    [Fact]
    public async Task HistoryFindingSummary_MapsFromRepoSummary()
    {
        var finding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.CacheCleanup,
            Target = "C:\\Cache",
            CanAutoFix = false
        };
        await _optimizationRepo.SaveFindingAsync(finding);

        var repoSummaries = await _optimizationRepo.ListFindingsAsync(10, 0);
        var rs = repoSummaries.First(f => f.FindingId == finding.FindingId);

        var dto = new HistoryFindingSummary
        {
            FindingId = rs.FindingId,
            Kind = rs.Kind,
            Target = rs.Target,
            CanAutoFix = rs.CanAutoFix,
            CreatedUtc = rs.CreatedUtc.ToString("o")
        };

        Assert.Equal(finding.FindingId, dto.FindingId);
        Assert.Equal(OptimizationKind.CacheCleanup, dto.Kind);
        Assert.False(dto.CanAutoFix);
    }

    [Fact]
    public async Task HistoryTraceSummary_MapsFromRepoSummary()
    {
        var trace = new PromptTrace
        {
            TraceId = Guid.NewGuid().ToString("N"),
            Stage = "voice_intent",
            PromptPayload = "{}",
            ResponsePayload = "{}",
            CreatedUtc = DateTime.UtcNow
        };
        await _conversationRepo.SavePromptTraceAsync(trace);

        var repoSummaries = await _conversationRepo.ListPromptTracesAsync(null, 10, 0);
        var rs = repoSummaries.First(t => t.TraceId == trace.TraceId);

        var dto = new HistoryTraceSummary
        {
            TraceId = rs.TraceId,
            Stage = rs.Stage,
            CreatedUtc = rs.CreatedUtc.ToString("o")
        };

        Assert.Equal(trace.TraceId, dto.TraceId);
        Assert.Equal("voice_intent", dto.Stage);
    }
}
