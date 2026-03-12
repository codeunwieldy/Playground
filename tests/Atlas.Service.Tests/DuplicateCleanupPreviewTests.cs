using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Storage;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for duplicate cleanup preview APIs (C-027).
/// Validates that the cleanup preview composition produces correct, bounded,
/// and policy-aware results against persisted inventory data.
/// </summary>
public sealed class DuplicateCleanupPreviewTests : IDisposable
{
    private readonly CleanupPreviewTestFixture _fixture;
    private readonly InventoryRepository _repository;

    public DuplicateCleanupPreviewTests()
    {
        _fixture = new CleanupPreviewTestFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Eligible group returns bounded operations ────────────────────────────

    [Fact]
    public async Task CleanupPreview_EligibleGroup_ReturnsBoundedOperations()
    {
        var session = CreateEligibleSession();
        await _repository.SaveSessionAsync(session);

        var (planResult, evaluation) = await BuildPreview(session.SessionId, "group-eligible");

        Assert.True(evaluation.IsCleanupEligible);
        Assert.Equal(DuplicateActionPosture.QuarantineDuplicates, evaluation.RecommendedPosture);
        Assert.NotEmpty(planResult.Operations);
        Assert.All(planResult.Operations, op =>
        {
            Assert.Equal(OperationKind.DeleteToQuarantine, op.Kind);
            Assert.True(op.MarksSafeDuplicate);
        });
    }

    // ── Review group returns no actionable preview ───────────────────────────

    [Fact]
    public async Task CleanupPreview_ReviewGroup_ReturnsNoPreview()
    {
        var session = CreateSensitiveSession();
        await _repository.SaveSessionAsync(session);

        var (_, evaluation) = await BuildPreview(session.SessionId, "group-sensitive");

        Assert.False(evaluation.IsCleanupEligible);
        Assert.True(evaluation.RequiresReview);
        Assert.Equal(DuplicateActionPosture.Review, evaluation.RecommendedPosture);
        Assert.NotEmpty(evaluation.BlockedReasons);
    }

    // ── Low confidence returns no preview ────────────────────────────────────

    [Fact]
    public async Task CleanupPreview_LowConfidence_ReturnsNoPreview()
    {
        var session = CreateLowConfidenceSession();
        await _repository.SaveSessionAsync(session);

        var (_, evaluation) = await BuildPreview(session.SessionId, "group-lowconf");

        Assert.False(evaluation.IsCleanupEligible);
        Assert.Equal(DuplicateActionPosture.Keep, evaluation.RecommendedPosture);
        Assert.NotEmpty(evaluation.BlockedReasons);
    }

    // ── Missing session returns null detail ───────────────────────────────────

    [Fact]
    public async Task CleanupPreview_MissingSession_ReturnsNotFound()
    {
        var detail = await _repository.GetDuplicateGroupDetailAsync("nonexistent-session", "any-group");

        Assert.Null(detail);
    }

    // ── Missing group returns null detail ─────────────────────────────────────

    [Fact]
    public async Task CleanupPreview_MissingGroup_ReturnsNotFound()
    {
        var session = CreateEligibleSession();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "nonexistent-group");

        Assert.Null(detail);
    }

    // ── Canonical path is truthful ───────────────────────────────────────────

    [Fact]
    public async Task CleanupPreview_CanonicalPathTruthful()
    {
        var session = CreateEligibleSession();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-eligible");

        Assert.NotNull(detail);
        Assert.Equal(@"C:\Documents\report.pdf", detail!.CanonicalPath);

        var (planResult, _) = await BuildPreview(session.SessionId, "group-eligible");

        // Operations should never target the canonical path
        Assert.All(planResult.Operations, op =>
            Assert.NotEqual(@"C:\Documents\report.pdf", op.SourcePath, StringComparer.OrdinalIgnoreCase));
    }

    // ── Operations bounded at limit ──────────────────────────────────────────

    [Fact]
    public async Task CleanupPreview_OperationsBounded()
    {
        var session = CreateLargeEligibleSession(memberCount: 60);
        await _repository.SaveSessionAsync(session);

        const int maxOps = 50;
        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-large");
        Assert.NotNull(detail);

        var group = ReconstructGroup(detail!);
        var items = await _repository.GetFilesForPathsAsync(session.SessionId, detail.MemberPaths);

        var planner = new SafeDuplicateCleanupPlanner();
        var planResult = planner.BuildOperations([group], items, 0.98d, maxGroups: 1, maxOperationsPerGroup: maxOps);

        Assert.True(planResult.Operations.Count <= maxOps);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(DuplicateCleanupPlanResult PlanResult, DuplicateActionReviewResult Evaluation)> BuildPreview(
        string sessionId, string groupId)
    {
        var detail = await _repository.GetDuplicateGroupDetailAsync(sessionId, groupId);
        Assert.NotNull(detail);

        var group = ReconstructGroup(detail!);
        var items = await _repository.GetFilesForPathsAsync(sessionId, detail!.MemberPaths);

        const double threshold = 0.98d;
        var planner = new SafeDuplicateCleanupPlanner();
        var planResult = planner.BuildOperations([group], items, threshold, maxGroups: 1, maxOperationsPerGroup: 50);

        var evaluation = DuplicateActionEvaluator.Evaluate(
            planResult, detail.HasProtectedMembers, detail.CleanupConfidence, threshold);

        return (planResult, evaluation);
    }

    private static DuplicateGroup ReconstructGroup(PersistedDuplicateGroupDetail detail)
    {
        return new DuplicateGroup
        {
            GroupId = detail.GroupId,
            CanonicalPath = detail.CanonicalPath,
            Confidence = detail.CleanupConfidence,
            MatchConfidence = detail.MatchConfidence,
            CanonicalReason = detail.CanonicalReason,
            MaxSensitivity = detail.MaxSensitivity,
            HasSensitiveMembers = detail.HasSensitiveMembers,
            HasSyncManagedMembers = detail.HasSyncManagedMembers,
            HasProtectedMembers = detail.HasProtectedMembers,
            Paths = detail.MemberPaths.ToList()
        };
    }

    private static ScanSession CreateEligibleSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Documents\report.pdf",
                    Name = "report.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                },
                new FileInventoryItem
                {
                    Path = @"C:\Downloads\report-copy.pdf",
                    Name = "report-copy.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                }
            ],
            DuplicateGroups =
            [
                new DuplicateGroup
                {
                    GroupId = "group-eligible",
                    CanonicalPath = @"C:\Documents\report.pdf",
                    MatchConfidence = 0.999,
                    Confidence = 0.995,
                    CanonicalReason = "preferred location",
                    MaxSensitivity = SensitivityLevel.Low,
                    Paths = [@"C:\Documents\report.pdf", @"C:\Downloads\report-copy.pdf"],
                    Evidence =
                    [
                        new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "Identical hash" }
                    ]
                }
            ],
            DuplicateGroupCount = 1
        };
    }

    private static ScanSession CreateSensitiveSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Documents\sensitive.pdf",
                    Name = "sensitive.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.High
                },
                new FileInventoryItem
                {
                    Path = @"C:\Downloads\sensitive-copy.pdf",
                    Name = "sensitive-copy.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.High
                }
            ],
            DuplicateGroups =
            [
                new DuplicateGroup
                {
                    GroupId = "group-sensitive",
                    CanonicalPath = @"C:\Documents\sensitive.pdf",
                    MatchConfidence = 0.999,
                    Confidence = 0.995,
                    CanonicalReason = "preferred location",
                    MaxSensitivity = SensitivityLevel.High,
                    HasSensitiveMembers = true,
                    Paths = [@"C:\Documents\sensitive.pdf", @"C:\Downloads\sensitive-copy.pdf"],
                    Evidence =
                    [
                        new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "Identical hash" }
                    ]
                }
            ],
            DuplicateGroupCount = 1
        };
    }

    private static ScanSession CreateLowConfidenceSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Documents\file.pdf",
                    Name = "file.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                },
                new FileInventoryItem
                {
                    Path = @"C:\Downloads\file-copy.pdf",
                    Name = "file-copy.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 100,
                    Sensitivity = SensitivityLevel.Low
                }
            ],
            DuplicateGroups =
            [
                new DuplicateGroup
                {
                    GroupId = "group-lowconf",
                    CanonicalPath = @"C:\Documents\file.pdf",
                    MatchConfidence = 0.80,
                    Confidence = 0.80,
                    CanonicalReason = "test",
                    MaxSensitivity = SensitivityLevel.Low,
                    Paths = [@"C:\Documents\file.pdf", @"C:\Downloads\file-copy.pdf"],
                    Evidence =
                    [
                        new DuplicateEvidenceEntry { Signal = "QuickHashOnly", Detail = "Quick hash only" }
                    ]
                }
            ],
            DuplicateGroupCount = 1
        };
    }

    private static ScanSession CreateLargeEligibleSession(int memberCount)
    {
        var files = new List<FileInventoryItem>();
        var paths = new List<string>();

        // Canonical
        var canonicalPath = @"C:\Documents\original.pdf";
        files.Add(new FileInventoryItem
        {
            Path = canonicalPath,
            Name = "original.pdf",
            Extension = ".pdf",
            Category = "Documents",
            SizeBytes = 1024,
            LastModifiedUnixTimeSeconds = 100,
            Sensitivity = SensitivityLevel.Low
        });
        paths.Add(canonicalPath);

        // Duplicates
        for (int i = 1; i < memberCount; i++)
        {
            var dupPath = $@"C:\Copies\copy-{i:D3}.pdf";
            files.Add(new FileInventoryItem
            {
                Path = dupPath,
                Name = $"copy-{i:D3}.pdf",
                Extension = ".pdf",
                Category = "Documents",
                SizeBytes = 1024,
                LastModifiedUnixTimeSeconds = 100,
                Sensitivity = SensitivityLevel.Low
            });
            paths.Add(dupPath);
        }

        return new ScanSession
        {
            Roots = [@"C:\"],
            Files = files,
            DuplicateGroups =
            [
                new DuplicateGroup
                {
                    GroupId = "group-large",
                    CanonicalPath = canonicalPath,
                    MatchConfidence = 0.999,
                    Confidence = 0.999,
                    CanonicalReason = "preferred location",
                    MaxSensitivity = SensitivityLevel.Low,
                    Paths = paths,
                    Evidence =
                    [
                        new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "Identical hash" }
                    ]
                }
            ],
            DuplicateGroupCount = 1
        };
    }
}

internal sealed class CleanupPreviewTestFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public CleanupPreviewTestFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_cleanup_preview_test_{Guid.NewGuid():N}.db");
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
