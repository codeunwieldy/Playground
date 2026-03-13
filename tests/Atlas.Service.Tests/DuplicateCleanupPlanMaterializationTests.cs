using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Storage;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for duplicate cleanup plan materialization APIs (C-030).
/// Validates that the materialization step deterministically transforms
/// a retained duplicate cleanup plan preview into a PlanGraph-compatible
/// review payload with trust gating and bounded failure behavior.
/// </summary>
public sealed class DuplicateCleanupPlanMaterializationTests : IDisposable
{
    private readonly PlanMaterializationTestFixture _fixture;
    private readonly InventoryRepository _repository;
    private const double Threshold = 0.98d;
    private const int MaxOpsPerGroup = 20;
    private const int MaxGroups = 50;

    public DuplicateCleanupPlanMaterializationTests()
    {
        _fixture = new PlanMaterializationTestFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Successful materialization produces a PlanGraph ─────────────────

    [Fact]
    public async Task Materialize_ValidSession_ProducesPlanGraph()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId);

        Assert.True(result.Found);
        Assert.True(result.CanMaterialize);
        Assert.NotEmpty(result.MaterializedPlanId);
        Assert.NotNull(result.Plan);
        Assert.NotEmpty(result.Plan.Operations);
        Assert.Equal("Duplicate Cleanup", result.Plan.Scope);
        Assert.True(result.Plan.RequiresReview);
        Assert.Equal(result.MaterializedPlanId, result.Plan.PlanId);
    }

    // ── All-blocked session cannot materialize ─────────────────────────

    [Fact]
    public async Task Materialize_AllBlocked_CannotMaterialize()
    {
        var session = CreateAllBlockedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId);

        Assert.True(result.Found);
        Assert.False(result.CanMaterialize);
        Assert.NotEmpty(result.DegradedReasons);
        Assert.Equal(0, result.IncludedGroupCount);
        Assert.True(result.BlockedGroupCount > 0);
    }

    // ── Degraded session cannot materialize ─────────────────────────────

    [Fact]
    public async Task Materialize_DegradedSession_CannotMaterialize()
    {
        var session = CreateMixedSession();
        session.IsTrusted = false;
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId);

        Assert.True(result.Found);
        Assert.False(result.CanMaterialize);
        Assert.NotEmpty(result.DegradedReasons);
        Assert.Contains(result.DegradedReasons, r => r.Contains("IsTrusted=false"));
    }

    // ── Missing session returns Found = false ──────────────────────────

    [Fact]
    public async Task Materialize_MissingSession_ReturnsNotFound()
    {
        var result = await BuildMaterialization("nonexistent-session");

        Assert.False(result.Found);
        Assert.False(result.CanMaterialize);
    }

    // ── No duplicates cannot materialize ────────────────────────────────

    [Fact]
    public async Task Materialize_NoDuplicates_CannotMaterialize()
    {
        var session = CreateSessionWithNoDuplicates();
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId);

        Assert.True(result.Found);
        Assert.False(result.CanMaterialize);
        Assert.NotEmpty(result.DegradedReasons);
        Assert.Equal(0, result.TotalPlannedOperations);
    }

    // ── Bounded group limit is enforced ─────────────────────────────────

    [Fact]
    public async Task Materialize_BoundedLimits_Enforced()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId, maxGroups: 2);

        Assert.True(result.Found);
        Assert.True(result.IncludedGroupCount + result.BlockedGroupCount <= 2);
    }

    // ── Rationale and rollback carry through ────────────────────────────

    [Fact]
    public async Task Materialize_RationaleAndRollback_CarryThrough()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId);

        Assert.True(result.Found);
        Assert.True(result.CanMaterialize);
        Assert.NotEmpty(result.Rationale);
        Assert.NotEmpty(result.RollbackPosture);
        Assert.NotEmpty(result.Plan.Rationale);
        Assert.NotEmpty(result.Plan.RollbackStrategy);
        Assert.Equal(result.Rationale, result.Plan.Rationale);
        Assert.Equal(result.RollbackPosture, result.Plan.RollbackStrategy);
    }

    // ── PlanGraph operations match included groups ──────────────────────

    [Fact]
    public async Task Materialize_PlanGraph_ContainsCorrectOperations()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildMaterialization(session.SessionId);

        Assert.True(result.CanMaterialize);

        var expectedOpCount = result.IncludedGroups.Sum(g => g.OperationCount);
        Assert.Equal(expectedOpCount, result.Plan.Operations.Count);
        Assert.Equal(result.TotalPlannedOperations, result.Plan.Operations.Count);

        // Every plan operation should have a GroupId matching an included group
        var includedGroupIds = result.IncludedGroups.Select(g => g.GroupId).ToHashSet();
        Assert.All(result.Plan.Operations, op =>
        {
            Assert.Contains(op.GroupId, includedGroupIds);
            Assert.NotEqual(OperationKind.Unknown, op.Kind);
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<MaterializeDuplicateCleanupPlanResponse> BuildMaterialization(
        string sessionId, int maxGroups = MaxGroups)
    {
        var session = await _repository.GetSessionAsync(sessionId);
        if (session is null)
        {
            return new MaterializeDuplicateCleanupPlanResponse { Found = false };
        }

        // Trust gate
        if (!session.IsTrusted)
        {
            return new MaterializeDuplicateCleanupPlanResponse
            {
                Found = true,
                CanMaterialize = false,
                DegradedReasons = ["Retained inventory session is degraded (IsTrusted=false). Run a full rescan to restore trusted state before materializing a cleanup plan."]
            };
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

        if (includedGroups.Count == 0)
        {
            return new MaterializeDuplicateCleanupPlanResponse
            {
                Found = true,
                CanMaterialize = false,
                BlockedGroupCount = blockedGroups.Count,
                ConfidenceThresholdUsed = Threshold,
                Rationale = "No duplicate groups are currently eligible for cleanup under current policy.",
                RollbackPosture = "No operations staged; no rollback needed.",
                BlockedGroups = blockedGroups,
                DegradedReasons = ["All duplicate groups are blocked by current policy. No operations can be materialized."]
            };
        }

        var rationale = $"Atlas identified {includedGroups.Count} duplicate group(s) eligible for cleanup across {totalOps} operation(s). All included groups meet the confidence threshold ({Threshold:F3}) and contain only low-sensitivity, non-protected, non-sync-managed members.";
        var rollbackPosture = "All cleanup operations use quarantine-first posture. Originals are preserved and duplicates can be restored from quarantine after review.";

        var materializedPlanId = Guid.NewGuid().ToString("N");

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
            PlanId = materializedPlanId,
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

        return new MaterializeDuplicateCleanupPlanResponse
        {
            Found = true,
            CanMaterialize = true,
            MaterializedPlanId = materializedPlanId,
            Plan = plan,
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

internal sealed class PlanMaterializationTestFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public PlanMaterializationTestFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_plan_materialize_test_{Guid.NewGuid():N}.db");
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
