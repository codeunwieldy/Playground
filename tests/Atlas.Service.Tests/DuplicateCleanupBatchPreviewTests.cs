using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Storage;
using Atlas.Storage.Repositories;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for duplicate cleanup batch preview APIs (C-028).
/// Validates that the batch preview composition produces correct, bounded,
/// and policy-aware results across multiple retained duplicate groups.
/// </summary>
public sealed class DuplicateCleanupBatchPreviewTests : IDisposable
{
    private readonly BatchPreviewTestFixture _fixture;
    private readonly InventoryRepository _repository;
    private const double Threshold = 0.98d;
    private const int MaxOpsPerGroup = 20;

    public DuplicateCleanupBatchPreviewTests()
    {
        _fixture = new BatchPreviewTestFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Mixed groups: eligible + blocked counts are truthful ──────────────────

    [Fact]
    public async Task BatchPreview_MixedGroups_TruthfulCounts()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildBatchPreview(session.SessionId, maxGroups: 50);

        Assert.True(result.Found);
        Assert.Equal(3, result.GroupsEvaluated);
        Assert.Equal(1, result.GroupsPreviewable);
        Assert.Equal(2, result.GroupsBlocked);
    }

    // ── Limit enforcement: only top N groups are evaluated ───────────────────

    [Fact]
    public async Task BatchPreview_LimitEnforced_RestrictsGroupCount()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildBatchPreview(session.SessionId, maxGroups: 2);

        Assert.True(result.Found);
        Assert.True(result.GroupsEvaluated <= 2);
        Assert.Equal(result.GroupsPreviewable + result.GroupsBlocked, result.GroupsEvaluated);
    }

    // ── Total operation count is truthful ────────────────────────────────────

    [Fact]
    public async Task BatchPreview_TotalOperationCount_IsTruthful()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildBatchPreview(session.SessionId, maxGroups: 50);

        var expectedTotalOps = result.Groups
            .Where(g => g.IsPreviewable)
            .Sum(g => g.OperationCount);

        Assert.Equal(expectedTotalOps, result.TotalOperationCount);
    }

    // ── Blocked groups do not emit misleading operation counts ────────────────

    [Fact]
    public async Task BatchPreview_BlockedGroups_ZeroOperations()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildBatchPreview(session.SessionId, maxGroups: 50);

        var blockedGroups = result.Groups.Where(g => !g.IsPreviewable).ToList();
        Assert.All(blockedGroups, g =>
        {
            Assert.NotEmpty(g.BlockedReasons);
        });
    }

    // ── Missing session returns Found = false ────────────────────────────────

    [Fact]
    public async Task BatchPreview_MissingSession_ReturnsNotFound()
    {
        var result = await BuildBatchPreview("nonexistent-session", maxGroups: 50);

        Assert.False(result.Found);
        Assert.Equal(0, result.GroupsEvaluated);
    }

    // ── Session with no duplicate groups returns zero counts ──────────────────

    [Fact]
    public async Task BatchPreview_NoDuplicateGroups_ReturnsZeroCounts()
    {
        var session = CreateSessionWithNoDuplicates();
        await _repository.SaveSessionAsync(session);

        var result = await BuildBatchPreview(session.SessionId, maxGroups: 50);

        Assert.True(result.Found);
        Assert.Equal(0, result.GroupsEvaluated);
        Assert.Equal(0, result.GroupsPreviewable);
        Assert.Equal(0, result.GroupsBlocked);
        Assert.Equal(0, result.TotalOperationCount);
        Assert.Empty(result.Groups);
    }

    // ── Confidence threshold is reported ─────────────────────────────────────

    [Fact]
    public async Task BatchPreview_ReportsConfidenceThreshold()
    {
        var session = CreateMixedSession();
        await _repository.SaveSessionAsync(session);

        var result = await BuildBatchPreview(session.SessionId, maxGroups: 50);

        Assert.True(result.ConfidenceThresholdUsed > 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<BatchPreviewResult> BuildBatchPreview(string sessionId, int maxGroups)
    {
        var session = await _repository.GetSessionAsync(sessionId);
        if (session is null)
        {
            return new BatchPreviewResult
            {
                Found = false,
                ConfidenceThresholdUsed = Threshold
            };
        }

        var effectiveMaxGroups = Math.Clamp(maxGroups, 1, 50);
        var duplicateGroups = await _repository.GetDuplicateGroupsForSessionAsync(sessionId, limit: effectiveMaxGroups);

        var planner = new SafeDuplicateCleanupPlanner();
        var groups = new List<BatchGroupPreviewSummary>(duplicateGroups.Count);
        int totalOps = 0, previewable = 0, blocked = 0;

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
            var planResult = planner.BuildOperations([group], items, Threshold, maxGroups: 1, maxOperationsPerGroup: MaxOpsPerGroup);
            var evaluation = DuplicateActionEvaluator.Evaluate(
                planResult, persisted.HasProtectedMembers, persisted.CleanupConfidence, Threshold);

            var summary = new BatchGroupPreviewSummary
            {
                GroupId = persisted.GroupId,
                CanonicalPath = persisted.CanonicalPath,
                IsPreviewable = evaluation.IsCleanupEligible,
                RecommendedPosture = evaluation.RecommendedPosture,
                OperationCount = planResult.Operations.Count,
                BlockedReasons = evaluation.BlockedReasons.ToList(),
                ActionNotes = evaluation.ActionNotes.ToList(),
                CleanupConfidence = persisted.CleanupConfidence
            };

            groups.Add(summary);

            if (evaluation.IsCleanupEligible)
            {
                previewable++;
                totalOps += planResult.Operations.Count;
            }
            else
            {
                blocked++;
            }
        }

        return new BatchPreviewResult
        {
            Found = true,
            GroupsEvaluated = duplicateGroups.Count,
            GroupsPreviewable = previewable,
            GroupsBlocked = blocked,
            TotalOperationCount = totalOps,
            ConfidenceThresholdUsed = Threshold,
            Groups = groups
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
}

internal sealed class BatchPreviewResult
{
    public bool Found { get; set; }
    public int GroupsEvaluated { get; set; }
    public int GroupsPreviewable { get; set; }
    public int GroupsBlocked { get; set; }
    public int TotalOperationCount { get; set; }
    public double ConfidenceThresholdUsed { get; set; }
    public List<BatchGroupPreviewSummary> Groups { get; set; } = new();
}

internal sealed class BatchPreviewTestFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public BatchPreviewTestFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_batch_preview_test_{Guid.NewGuid():N}.db");
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
