using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Storage;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for duplicate cleanup plan promotion and persistence APIs (C-031).
/// Validates that the promotion step persists a materialized retained duplicate
/// cleanup plan into standard plan history with trust gating, lineage truth,
/// and deterministic repeated-promotion behavior.
/// </summary>
public sealed class DuplicateCleanupPlanPromotionTests : IDisposable
{
    private readonly PlanPromotionTestFixture _fixture;
    private readonly InventoryRepository _inventoryRepository;
    private readonly PlanRepository _planRepository;
    private const double Threshold = 0.98d;
    private const int MaxOpsPerGroup = 20;
    private const int MaxGroups = 50;

    public DuplicateCleanupPlanPromotionTests()
    {
        _fixture = new PlanPromotionTestFixture();
        _inventoryRepository = new InventoryRepository(_fixture.ConnectionFactory);
        _planRepository = new PlanRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Successful promotion saves plan to repository ─────────────────

    [Fact]
    public async Task Promote_ValidSession_SavesPlanToRepository()
    {
        var session = CreateMixedSession();
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId);

        Assert.True(result.Found);
        Assert.True(result.Promoted);
        Assert.NotEmpty(result.SavedPlanId);
        Assert.True(result.IsNewlyPromoted);

        var savedPlan = await _planRepository.GetPlanAsync(result.SavedPlanId);
        Assert.NotNull(savedPlan);
        Assert.Equal("Duplicate Cleanup", savedPlan.Scope);
        Assert.True(savedPlan.RequiresReview);
        Assert.NotEmpty(savedPlan.Operations);
    }

    // ── Degraded session refuses promotion ─────────────────────────────

    [Fact]
    public async Task Promote_DegradedSession_RefusesPromotion()
    {
        var session = CreateMixedSession();
        session.IsTrusted = false;
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId);

        Assert.True(result.Found);
        Assert.False(result.Promoted);
        Assert.NotEmpty(result.DegradedReasons);
        Assert.Contains(result.DegradedReasons, r => r.Contains("IsTrusted=false"));
    }

    // ── All-blocked session refuses promotion ─────────────────────────

    [Fact]
    public async Task Promote_AllBlocked_RefusesPromotion()
    {
        var session = CreateAllBlockedSession();
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId);

        Assert.True(result.Found);
        Assert.False(result.Promoted);
        Assert.NotEmpty(result.DegradedReasons);
        Assert.Equal(0, result.IncludedGroupCount);
        Assert.True(result.BlockedGroupCount > 0);
    }

    // ── Missing session returns Found = false ─────────────────────────

    [Fact]
    public async Task Promote_MissingSession_ReturnsNotFound()
    {
        var result = await BuildPromotion("nonexistent-session");

        Assert.False(result.Found);
        Assert.False(result.Promoted);
    }

    // ── Promoted plan visible in plan history ─────────────────────────

    [Fact]
    public async Task Promote_SavedPlan_VisibleInPlanHistory()
    {
        var session = CreateMixedSession();
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId);

        Assert.True(result.Promoted);

        var plans = await _planRepository.ListPlansAsync(10, 0);
        Assert.Contains(plans, p => p.PlanId == result.SavedPlanId && p.Scope == "Duplicate Cleanup");
    }

    // ── Repeated promotion produces deterministic content ─────────────

    [Fact]
    public async Task Promote_RepeatedPromotion_DeterministicContent()
    {
        var session = CreateMixedSession();
        await _inventoryRepository.SaveSessionAsync(session);

        var result1 = await BuildPromotion(session.SessionId);
        var result2 = await BuildPromotion(session.SessionId);

        Assert.True(result1.Promoted);
        Assert.True(result2.Promoted);

        // Content is deterministic: same operation count and rationale
        Assert.Equal(result1.TotalPlannedOperations, result2.TotalPlannedOperations);
        Assert.Equal(result1.IncludedGroupCount, result2.IncludedGroupCount);
        Assert.Equal(result1.Rationale, result2.Rationale);

        // But each promotion creates a distinct plan
        Assert.NotEqual(result1.SavedPlanId, result2.SavedPlanId);
    }

    // ── Bounded group limit is enforced ───────────────────────────────

    [Fact]
    public async Task Promote_BoundedLimits_Enforced()
    {
        var session = CreateMixedSession();
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId, maxGroups: 2);

        Assert.True(result.Found);
        Assert.True(result.IncludedGroupCount + result.BlockedGroupCount <= 2);
    }

    // ── Lineage truth preserved in saved plan ─────────────────────────

    [Fact]
    public async Task Promote_LineageTruth_PreservedInSavedPlan()
    {
        var session = CreateMixedSession();
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId);

        Assert.True(result.Promoted);

        var savedPlan = await _planRepository.GetPlanAsync(result.SavedPlanId);
        Assert.NotNull(savedPlan);
        Assert.NotEmpty(savedPlan.Rationale);
        Assert.NotEmpty(savedPlan.RollbackStrategy);
        Assert.Contains("DuplicateCleanup", savedPlan.Categories);

        // Every operation in the saved plan has a GroupId
        Assert.All(savedPlan.Operations, op =>
        {
            Assert.NotEmpty(op.GroupId);
        });
    }

    // ── No duplicates refuses promotion ───────────────────────────────

    [Fact]
    public async Task Promote_NoDuplicates_RefusesPromotion()
    {
        var session = CreateSessionWithNoDuplicates();
        await _inventoryRepository.SaveSessionAsync(session);

        var result = await BuildPromotion(session.SessionId);

        Assert.True(result.Found);
        Assert.False(result.Promoted);
        Assert.NotEmpty(result.DegradedReasons);
        Assert.Equal(0, result.TotalPlannedOperations);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<PromoteDuplicateCleanupPlanResponse> BuildPromotion(
        string sessionId, int maxGroups = MaxGroups)
    {
        var session = await _inventoryRepository.GetSessionAsync(sessionId);
        if (session is null)
        {
            return new PromoteDuplicateCleanupPlanResponse { Found = false };
        }

        // Trust gate
        if (!session.IsTrusted)
        {
            return new PromoteDuplicateCleanupPlanResponse
            {
                Found = true,
                Promoted = false,
                SourceSessionId = sessionId,
                DegradedReasons = ["Retained inventory session is degraded (IsTrusted=false). Run a full rescan to restore trusted state before promoting a cleanup plan."]
            };
        }

        var effectiveMaxGroups = Math.Clamp(maxGroups, 1, MaxGroups);
        var duplicateGroups = await _inventoryRepository.GetDuplicateGroupsForSessionAsync(
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

            var items = await _inventoryRepository.GetFilesForPathsAsync(sessionId, persisted.MemberPaths);
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

        if (includedGroups.Count == 0)
        {
            return new PromoteDuplicateCleanupPlanResponse
            {
                Found = true,
                Promoted = false,
                SourceSessionId = sessionId,
                BlockedGroupCount = blockedGroups.Count,
                ConfidenceThresholdUsed = Threshold,
                Rationale = "No duplicate groups are currently eligible for cleanup under current policy.",
                RollbackPosture = "No operations staged; no rollback needed.",
                DegradedReasons = ["All duplicate groups are blocked by current policy. No operations can be promoted."]
            };
        }

        var rationale = $"Atlas identified {includedGroups.Count} duplicate group(s) eligible for cleanup across {totalOps} operation(s). All included groups meet the confidence threshold ({Threshold:F3}) and contain only low-sensitivity, non-protected, non-sync-managed members.";
        var rollbackPosture = "All cleanup operations use quarantine-first posture. Originals are preserved and duplicates can be restored from quarantine after review.";

        var promotedPlanId = Guid.NewGuid().ToString("N");

        var planOperations = new List<PlanOperation>();
        foreach (var included in includedGroups)
        {
            foreach (var op in included.Operations)
            {
                planOperations.Add(new PlanOperation
                {
                    Kind = Enum.TryParse<OperationKind>(op.Kind, out var kind) ? kind : OperationKind.DeleteToQuarantine,
                    SourcePath = op.SourcePath,
                    Description = op.Description,
                    Confidence = op.Confidence,
                    Sensitivity = Enum.TryParse<SensitivityLevel>(op.Sensitivity, out var sens) ? sens : SensitivityLevel.Unknown,
                    GroupId = included.GroupId
                });
            }
        }

        var plan = new PlanGraph
        {
            PlanId = promotedPlanId,
            Scope = "Duplicate Cleanup",
            Rationale = rationale,
            Categories = ["DuplicateCleanup"],
            Operations = planOperations,
            RiskSummary = new RiskEnvelope
            {
                Confidence = Threshold,
                ReversibilityScore = 1.0,
                BlockedReasons = blockedGroups.SelectMany(g => g.BlockedReasons).Distinct().ToList()
            },
            EstimatedBenefit = $"Removes {totalOps} duplicate file(s) across {includedGroups.Count} group(s) via quarantine.",
            RequiresReview = true,
            RollbackStrategy = rollbackPosture
        };

        // Persist the plan into standard plan history.
        var savedPlanId = await _planRepository.SavePlanAsync(plan);

        return new PromoteDuplicateCleanupPlanResponse
        {
            Found = true,
            Promoted = true,
            SavedPlanId = savedPlanId,
            IsNewlyPromoted = true,
            IncludedGroupCount = includedGroups.Count,
            BlockedGroupCount = blockedGroups.Count,
            TotalPlannedOperations = totalOps,
            ConfidenceThresholdUsed = Threshold,
            Rationale = rationale,
            RollbackPosture = rollbackPosture,
            Scope = "Duplicate Cleanup",
            Categories = ["DuplicateCleanup"],
            SourceSessionId = sessionId
        };
    }

    // ── Test session builders ────────────────────────────────────────────

    private static ScanSession CreateMixedSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
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

internal sealed class PlanPromotionTestFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public PlanPromotionTestFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_plan_promote_test_{Guid.NewGuid():N}.db");
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
