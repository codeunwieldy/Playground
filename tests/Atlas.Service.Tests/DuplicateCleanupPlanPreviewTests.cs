using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Storage;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for duplicate cleanup plan preview APIs (C-029).
/// Validates that the plan preview composition produces correct, bounded,
/// plan-shaped results with included-vs-blocked group separation,
/// rationale, and rollback posture suitable for app review surfaces.
/// </summary>
public sealed class DuplicateCleanupPlanPreviewTests : IDisposable
{
    private readonly PlanPreviewTestFixture _fixture;
    private readonly InventoryRepository _repository;
    private const double Threshold = 0.98d;
    private const int MaxOpsPerGroup = 20;
    private const int MaxGroups = 50;

    public DuplicateCleanupPlanPreviewTests()
    {
        _fixture = new PlanPreviewTestFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Eligible groups produce a bounded plan preview ───────────────────────

    [Fact]
    public async Task PlanPreview_EligibleGroups_ProduceBoundedPlan()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId);

        Assert.True(result.Found);
        Assert.Equal(1, result.IncludedGroupCount);
        Assert.True(result.TotalPlannedOperations > 0);
        Assert.NotEmpty(result.IncludedGroups);
        Assert.All(result.IncludedGroups, g =>
        {
            Assert.NotEmpty(g.GroupId);
            Assert.NotEmpty(g.CanonicalPath);
            Assert.True(g.OperationCount > 0);
            Assert.NotEmpty(g.Operations);
        });
    }

    // ── Blocked groups stay out of the included set ──────────────────────────

    [Fact]
    public async Task PlanPreview_BlockedGroups_StayOutOfIncludedSet()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId);

        Assert.True(result.Found);
        Assert.Equal(2, result.BlockedGroupCount);
        Assert.NotEmpty(result.BlockedGroups);
        Assert.All(result.BlockedGroups, g =>
        {
            Assert.NotEmpty(g.GroupId);
            Assert.NotEmpty(g.BlockedReasons);
        });

        // Blocked group IDs should not appear in included groups
        var blockedIds = result.BlockedGroups.Select(g => g.GroupId).ToHashSet();
        Assert.All(result.IncludedGroups, g => Assert.DoesNotContain(g.GroupId, blockedIds));
    }

    // ── Mixed sessions report included vs blocked truthfully ────────────────

    [Fact]
    public async Task PlanPreview_MixedSession_TruthfulCounts()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId);

        Assert.True(result.Found);
        Assert.Equal(1, result.IncludedGroupCount);
        Assert.Equal(2, result.BlockedGroupCount);
        Assert.Equal(result.IncludedGroups.Count, result.IncludedGroupCount);
        Assert.Equal(result.BlockedGroups.Count, result.BlockedGroupCount);
    }

    // ── Missing session returns Found = false ────────────────────────────────

    [Fact]
    public async Task PlanPreview_MissingSession_ReturnsNotFound()
    {
        var result = await BuildPlanPreview("nonexistent-session");

        Assert.False(result.Found);
        Assert.Equal(0, result.IncludedGroupCount);
        Assert.Equal(0, result.BlockedGroupCount);
    }

    // ── Session with no duplicates returns empty plan ────────────────────────

    [Fact]
    public async Task PlanPreview_NoDuplicates_ReturnsEmptyPlan()
    {
        var session = CreateSessionWithNoDuplicates();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId);

        Assert.True(result.Found);
        Assert.Equal(0, result.IncludedGroupCount);
        Assert.Equal(0, result.BlockedGroupCount);
        Assert.Equal(0, result.TotalPlannedOperations);
        Assert.Empty(result.IncludedGroups);
        Assert.Empty(result.BlockedGroups);
    }

    // ── Bounded group limit is enforced ──────────────────────────────────────

    [Fact]
    public async Task PlanPreview_BoundedGroupLimit_Enforced()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId, maxGroups: 2);

        Assert.True(result.Found);
        Assert.True(result.IncludedGroupCount + result.BlockedGroupCount <= 2);
    }

    // ── Rationale and rollback posture are populated ────────────────────────

    [Fact]
    public async Task PlanPreview_RationaleAndRollback_ArePopulated()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId);

        Assert.True(result.Found);
        Assert.NotEmpty(result.Rationale);
        Assert.NotEmpty(result.RollbackPosture);
        Assert.True(result.ConfidenceThresholdUsed > 0);
    }

    // ── All-blocked session produces no included groups ──────────────────────

    [Fact]
    public async Task PlanPreview_AllBlocked_NoIncludedGroups()
    {
        var session = CreateAllBlockedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildPlanPreview(session.SessionId);

        Assert.True(result.Found);
        Assert.Equal(0, result.IncludedGroupCount);
        Assert.True(result.BlockedGroupCount > 0);
        Assert.Equal(0, result.TotalPlannedOperations);
        Assert.Empty(result.IncludedGroups);
        Assert.NotEmpty(result.BlockedGroups);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<DuplicateCleanupPlanPreviewResponse> BuildPlanPreview(
        string sessionId, int maxGroups = MaxGroups)
    {
        var session = await _repository.GetSessionAsync(sessionId);
        if (session is null)
        {
            return new DuplicateCleanupPlanPreviewResponse { Found = false };
        }

        var effectiveMaxGroups = Math.Clamp(maxGroups, 1, MaxGroups);
        var duplicateGroups = await _repository.GetDuplicateGroupsForSessionAsync(
            sessionId, limit: effectiveMaxGroups);

        var planner = new SafeDuplicateCleanupPlanner();
        var includedGroups = new List<PlanPreviewIncludedGroup>();
        var blockedGroups = new List<PlanPreviewBlockedGroup>();
        int totalOps = 0;

        foreach (var persisted in duplicateGroups)
        {
            var group = new DuplicateGroup
            {
                GroupId = persisted.GroupId,
                CanonicalPath = persisted.CanonicalPath,
                Confidence = persisted.CleanupConfidence,
                MatchConfidence = persisted.MatchConfidence,
                CanonicalReason = persisted.CanonicalReason,
                MaxSensitivity = persisted.MaxSensitivity,
                HasSensitiveMembers = persisted.HasSensitiveMembers,
                HasSyncManagedMembers = persisted.HasSyncManagedMembers,
                HasProtectedMembers = persisted.HasProtectedMembers,
                Paths = persisted.MemberPaths.ToList()
            };

            var items = await _repository.GetFilesForPathsAsync(sessionId, persisted.MemberPaths);
            var planResult = planner.BuildOperations(
                [group], items, Threshold, maxGroups: 1, maxOperationsPerGroup: MaxOpsPerGroup);

            var evaluation = DuplicateActionEvaluator.Evaluate(
                planResult, persisted.HasProtectedMembers, persisted.CleanupConfidence, Threshold);

            if (evaluation.IsCleanupEligible)
            {
                var operations = planResult.Operations
                    .Take(MaxOpsPerGroup)
                    .Select(op => new CleanupOperationPreview
                    {
                        SourcePath = op.SourcePath,
                        Kind = op.Kind.ToString(),
                        Description = op.Description,
                        Confidence = op.Confidence,
                        Sensitivity = op.Sensitivity.ToString()
                    })
                    .ToList();

                includedGroups.Add(new PlanPreviewIncludedGroup
                {
                    GroupId = persisted.GroupId,
                    CanonicalPath = persisted.CanonicalPath,
                    CleanupConfidence = persisted.CleanupConfidence,
                    OperationCount = planResult.Operations.Count,
                    Operations = operations,
                    ActionNotes = evaluation.ActionNotes.ToList()
                });

                totalOps += planResult.Operations.Count;
            }
            else
            {
                blockedGroups.Add(new PlanPreviewBlockedGroup
                {
                    GroupId = persisted.GroupId,
                    CanonicalPath = persisted.CanonicalPath,
                    CleanupConfidence = persisted.CleanupConfidence,
                    RecommendedPosture = evaluation.RecommendedPosture,
                    BlockedReasons = evaluation.BlockedReasons.ToList()
                });
            }
        }

        var rationale = includedGroups.Count > 0
            ? $"Atlas identified {includedGroups.Count} duplicate group(s) eligible for cleanup across {totalOps} operation(s). All included groups meet the confidence threshold ({Threshold:F3}) and contain only low-sensitivity, non-protected, non-sync-managed members."
            : "No duplicate groups are currently eligible for cleanup under current policy.";

        var rollbackPosture = includedGroups.Count > 0
            ? "All cleanup operations use quarantine-first posture. Originals are preserved and duplicates can be restored from quarantine after review."
            : "No operations staged; no rollback needed.";

        return new DuplicateCleanupPlanPreviewResponse
        {
            Found = true,
            IncludedGroupCount = includedGroups.Count,
            BlockedGroupCount = blockedGroups.Count,
            TotalPlannedOperations = totalOps,
            ConfidenceThresholdUsed = Threshold,
            Rationale = rationale,
            RollbackPosture = rollbackPosture,
            IncludedGroups = includedGroups,
            BlockedGroups = blockedGroups
        };
    }

    // ── Test session builders ────────────────────────────────────────────────

    private static ScanSession CreateMixedSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                // Eligible group files
                new FileInventoryItem
                {
                    Path = @"C:\Docs\report.pdf", Name = "report.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 2048, LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                },
                new FileInventoryItem
                {
                    Path = @"C:\Downloads\report-copy.pdf", Name = "report-copy.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 2048, LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                },
                // Sensitive group files
                new FileInventoryItem
                {
                    Path = @"C:\Docs\secret.pdf", Name = "secret.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 1024, LastModifiedUnixTimeSeconds = 200,
                    Sensitivity = SensitivityLevel.High
                },
                new FileInventoryItem
                {
                    Path = @"C:\Backup\secret-copy.pdf", Name = "secret-copy.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 1024, LastModifiedUnixTimeSeconds = 200,
                    Sensitivity = SensitivityLevel.High
                },
                // Low-confidence group files
                new FileInventoryItem
                {
                    Path = @"C:\Docs\maybe.txt", Name = "maybe.txt", Extension = ".txt",
                    Category = "Documents", SizeBytes = 512, LastModifiedUnixTimeSeconds = 300,
                    Sensitivity = SensitivityLevel.Low
                },
                new FileInventoryItem
                {
                    Path = @"C:\Temp\maybe-copy.txt", Name = "maybe-copy.txt", Extension = ".txt",
                    Category = "Documents", SizeBytes = 512, LastModifiedUnixTimeSeconds = 300,
                    Sensitivity = SensitivityLevel.Low
                }
            ],
            DuplicateGroups =
            [
                new DuplicateGroup
                {
                    GroupId = "group-eligible",
                    CanonicalPath = @"C:\Docs\report.pdf",
                    MatchConfidence = 0.999, Confidence = 0.995,
                    CanonicalReason = "preferred location",
                    MaxSensitivity = SensitivityLevel.Low,
                    Paths = [@"C:\Docs\report.pdf", @"C:\Downloads\report-copy.pdf"],
                    Evidence = [new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "Identical hash" }]
                },
                new DuplicateGroup
                {
                    GroupId = "group-sensitive",
                    CanonicalPath = @"C:\Docs\secret.pdf",
                    MatchConfidence = 0.999, Confidence = 0.995,
                    CanonicalReason = "preferred location",
                    MaxSensitivity = SensitivityLevel.High,
                    HasSensitiveMembers = true,
                    Paths = [@"C:\Docs\secret.pdf", @"C:\Backup\secret-copy.pdf"],
                    Evidence = [new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "Identical hash" }]
                },
                new DuplicateGroup
                {
                    GroupId = "group-lowconf",
                    CanonicalPath = @"C:\Docs\maybe.txt",
                    MatchConfidence = 0.80, Confidence = 0.80,
                    CanonicalReason = "test",
                    MaxSensitivity = SensitivityLevel.Low,
                    Paths = [@"C:\Docs\maybe.txt", @"C:\Temp\maybe-copy.txt"],
                    Evidence = [new DuplicateEvidenceEntry { Signal = "QuickHashOnly", Detail = "Quick hash only" }]
                }
            ],
            DuplicateGroupCount = 3
        };
    }

    private static ScanSession CreateSessionWithNoDuplicates()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Docs\unique.pdf", Name = "unique.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 1024, LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                }
            ],
            DuplicateGroupCount = 0
        };
    }

    private static ScanSession CreateAllBlockedSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Docs\sensitive.pdf", Name = "sensitive.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 2048, LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.High
                },
                new FileInventoryItem
                {
                    Path = @"C:\Backup\sensitive-copy.pdf", Name = "sensitive-copy.pdf", Extension = ".pdf",
                    Category = "Documents", SizeBytes = 2048, LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.High
                },
                new FileInventoryItem
                {
                    Path = @"C:\Docs\lowconf.txt", Name = "lowconf.txt", Extension = ".txt",
                    Category = "Documents", SizeBytes = 512, LastModifiedUnixTimeSeconds = 200,
                    Sensitivity = SensitivityLevel.Low
                },
                new FileInventoryItem
                {
                    Path = @"C:\Temp\lowconf-copy.txt", Name = "lowconf-copy.txt", Extension = ".txt",
                    Category = "Documents", SizeBytes = 512, LastModifiedUnixTimeSeconds = 200,
                    Sensitivity = SensitivityLevel.Low
                }
            ],
            DuplicateGroups =
            [
                new DuplicateGroup
                {
                    GroupId = "group-sensitive-blocked",
                    CanonicalPath = @"C:\Docs\sensitive.pdf",
                    MatchConfidence = 0.999, Confidence = 0.995,
                    CanonicalReason = "preferred location",
                    MaxSensitivity = SensitivityLevel.High,
                    HasSensitiveMembers = true,
                    Paths = [@"C:\Docs\sensitive.pdf", @"C:\Backup\sensitive-copy.pdf"],
                    Evidence = [new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "Identical hash" }]
                },
                new DuplicateGroup
                {
                    GroupId = "group-lowconf-blocked",
                    CanonicalPath = @"C:\Docs\lowconf.txt",
                    MatchConfidence = 0.70, Confidence = 0.70,
                    CanonicalReason = "test",
                    MaxSensitivity = SensitivityLevel.Low,
                    Paths = [@"C:\Docs\lowconf.txt", @"C:\Temp\lowconf-copy.txt"],
                    Evidence = [new DuplicateEvidenceEntry { Signal = "QuickHashOnly", Detail = "Quick hash only" }]
                }
            ],
            DuplicateGroupCount = 2
        };
    }
}

internal sealed class PlanPreviewTestFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public PlanPreviewTestFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_plan_preview_test_{Guid.NewGuid():N}.db");
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
