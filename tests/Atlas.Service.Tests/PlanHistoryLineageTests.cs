using Atlas.Core.Contracts;
using Atlas.Storage;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for plan history lineage metadata and source-aware filtering (C-032).
/// Validates that promoted retained cleanup plans persist truthful lineage,
/// history queries expose lineage/source cleanly, optional source filtering
/// works, and older plans remain readable without new metadata.
/// </summary>
public sealed class PlanHistoryLineageTests : IDisposable
{
    private readonly LineageTestFixture _fixture;
    private readonly PlanRepository _planRepository;

    public PlanHistoryLineageTests()
    {
        _fixture = new LineageTestFixture();
        _planRepository = new PlanRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Promoted plan persists lineage metadata ─────────────────────────

    [Fact]
    public async Task PromotedPlan_PersistsLineageMetadata()
    {
        var plan = CreatePromotedPlan("session-abc");
        var savedId = await _planRepository.SavePlanAsync(plan);

        var retrieved = await _planRepository.GetPlanAsync(savedId);

        Assert.NotNull(retrieved);
        Assert.Equal("DuplicateCleanupPromotion", retrieved.Source);
        Assert.Equal("session-abc", retrieved.SourceSessionId);
    }

    // ── Normal plan has empty lineage ───────────────────────────────────

    [Fact]
    public async Task NormalPlan_HasEmptyLineage()
    {
        var plan = CreateNormalPlan();
        var savedId = await _planRepository.SavePlanAsync(plan);

        var retrieved = await _planRepository.GetPlanAsync(savedId);

        Assert.NotNull(retrieved);
        Assert.Equal("", retrieved.Source);
        Assert.Equal("", retrieved.SourceSessionId);
    }

    // ── ListPlans returns lineage in summary ────────────────────────────

    [Fact]
    public async Task ListPlans_ReturnsLineageInSummary()
    {
        var promoted = CreatePromotedPlan("session-xyz");
        await _planRepository.SavePlanAsync(promoted);

        var plans = await _planRepository.ListPlansAsync(10, 0);

        Assert.Contains(plans, p => p.Source == "DuplicateCleanupPromotion" && p.SourceSessionId == "session-xyz");
    }

    // ── Source filter returns only matching plans ────────────────────────

    [Fact]
    public async Task ListPlans_SourceFilter_ReturnOnlyMatchingPlans()
    {
        var promoted = CreatePromotedPlan("session-filter");
        var normal = CreateNormalPlan();
        await _planRepository.SavePlanAsync(promoted);
        await _planRepository.SavePlanAsync(normal);

        var filtered = await _planRepository.ListPlansAsync(50, 0, "DuplicateCleanupPromotion");

        Assert.All(filtered, p => Assert.Equal("DuplicateCleanupPromotion", p.Source));
        Assert.DoesNotContain(filtered, p => p.Source == "");
    }

    // ── Source filter with no results handled ───────────────────────────

    [Fact]
    public async Task ListPlans_SourceFilter_EmptyResultsHandled()
    {
        var normal = CreateNormalPlan();
        await _planRepository.SavePlanAsync(normal);

        var filtered = await _planRepository.ListPlansAsync(50, 0, "NonExistentSource");

        Assert.Empty(filtered);
    }

    // ── No filter returns all plans ─────────────────────────────────────

    [Fact]
    public async Task ListPlans_NoFilter_ReturnsAllPlans()
    {
        var promoted = CreatePromotedPlan("session-all");
        var normal = CreateNormalPlan();
        await _planRepository.SavePlanAsync(promoted);
        await _planRepository.SavePlanAsync(normal);

        var all = await _planRepository.ListPlansAsync(50, 0, sourceFilter: null);

        Assert.True(all.Count >= 2);
        Assert.Contains(all, p => p.Source == "DuplicateCleanupPromotion");
        Assert.Contains(all, p => p.Source == "");
    }

    // ── Older plans readable with empty lineage ─────────────────────────

    [Fact]
    public async Task OlderPlans_ReadableWithEmptyLineage()
    {
        // Simulate an older plan by saving with no Source/SourceSessionId set.
        var oldPlan = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "General Cleanup",
            Rationale = "Old plan from before lineage existed",
            Categories = ["GeneralCleanup"],
            Operations = [],
            RequiresReview = false,
            RollbackStrategy = "N/A"
        };
        var savedId = await _planRepository.SavePlanAsync(oldPlan);

        var plans = await _planRepository.ListPlansAsync(50, 0);
        var found = plans.FirstOrDefault(p => p.PlanId == savedId);

        Assert.NotNull(found);
        Assert.Equal("", found.Source);
        Assert.Equal("", found.SourceSessionId);

        var retrieved = await _planRepository.GetPlanAsync(savedId);
        Assert.NotNull(retrieved);
        Assert.Equal("", retrieved.Source);
        Assert.Equal("", retrieved.SourceSessionId);
    }

    // ── Plan detail surfaces lineage truth ──────────────────────────────

    [Fact]
    public async Task PlanDetail_SurfacesLineageTruth()
    {
        var plan = CreatePromotedPlan("session-detail");
        var savedId = await _planRepository.SavePlanAsync(plan);

        var retrieved = await _planRepository.GetPlanAsync(savedId);

        Assert.NotNull(retrieved);
        Assert.Equal("DuplicateCleanupPromotion", retrieved.Source);
        Assert.Equal("session-detail", retrieved.SourceSessionId);
        Assert.Equal("Duplicate Cleanup", retrieved.Scope);
        Assert.Contains("DuplicateCleanup", retrieved.Categories);
        Assert.True(retrieved.RequiresReview);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static PlanGraph CreatePromotedPlan(string sourceSessionId)
    {
        return new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "Duplicate Cleanup",
            Rationale = "Atlas identified 1 duplicate group(s) eligible for cleanup.",
            Categories = ["DuplicateCleanup"],
            Operations =
            [
                new PlanOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = @"C:\Downloads\report-copy.pdf",
                    Description = "Quarantine duplicate",
                    Confidence = 0.995,
                    Sensitivity = SensitivityLevel.Low,
                    GroupId = "group-test"
                }
            ],
            RiskSummary = new RiskEnvelope { Confidence = 0.98, ReversibilityScore = 1.0 },
            EstimatedBenefit = "Removes 1 duplicate file(s) via quarantine.",
            RequiresReview = true,
            RollbackStrategy = "Quarantine-first posture.",
            Source = "DuplicateCleanupPromotion",
            SourceSessionId = sourceSessionId
        };
    }

    private static PlanGraph CreateNormalPlan()
    {
        return new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "User Cleanup",
            Rationale = "Normal planner-originated plan",
            Categories = ["UserCleanup"],
            Operations = [],
            RequiresReview = false,
            RollbackStrategy = "N/A"
        };
    }
}

internal sealed class LineageTestFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public LineageTestFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_lineage_test_{Guid.NewGuid():N}.db");
        var options = new StorageOptions
        {
            DataRoot = Path.GetDirectoryName(DatabasePath)!,
            DatabaseFileName = Path.GetFileName(DatabasePath)
        };
        var bootstrapper = new AtlasDatabaseBootstrapper(options);
        bootstrapper.InitializeAsync().GetAwaiter().GetResult();
        ConnectionFactory = new SqliteConnectionFactory(bootstrapper);
    }

    public void Dispose()
    {
        try { File.Delete(DatabasePath); } catch { }
    }
}
