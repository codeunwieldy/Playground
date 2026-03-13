using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Core.Policies;
using Atlas.Service.Services;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for VssSnapshotOrchestrator and its integration with PlanExecutionService (C-036).
/// Validates orchestration behavior, fail-closed semantics, and checkpoint metadata population.
/// </summary>
public sealed class VssSnapshotOrchestrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly PolicyProfile _profile;

    public VssSnapshotOrchestrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-vss-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _profile = new PolicyProfile
        {
            MutableRoots = [_testRoot],
            ScanRoots = [_testRoot]
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private string CreateTestFile(string relativePath, string content = "test content")
    {
        var path = Path.Combine(_testRoot, relativePath);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    private string TestPath(string relativePath) => Path.Combine(_testRoot, relativePath);

    // ── Unit tests for VssSnapshotOrchestrator ─────────────────────────────

    [Fact]
    public void NotNeeded_ReturnsNotNeededStatus()
    {
        var orchestrator = new VssSnapshotOrchestrator();
        var result = orchestrator.TryCreateSnapshots(
            CheckpointRequirement.NotNeeded,
            [@"C:\"],
            isDryRun: false);

        Assert.Equal(VssSnapshotStatus.NotNeeded, result.Status);
        Assert.Empty(result.References);
        Assert.Contains("skipped", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DryRun_ReturnsSkippedStatus()
    {
        var orchestrator = new VssSnapshotOrchestrator();
        var result = orchestrator.TryCreateSnapshots(
            CheckpointRequirement.Required,
            [@"C:\"],
            isDryRun: true);

        Assert.Equal(VssSnapshotStatus.Skipped, result.Status);
        Assert.Empty(result.References);
        Assert.Contains("Dry-run", result.Message);
    }

    [Fact]
    public void EmptyVolumes_ReturnsUnavailable()
    {
        var orchestrator = new VssSnapshotOrchestrator();
        var result = orchestrator.TryCreateSnapshots(
            CheckpointRequirement.Required,
            [],
            isDryRun: false);

        Assert.Equal(VssSnapshotStatus.Unavailable, result.Status);
        Assert.False(result.Success);
    }

    [Fact]
    public void RecommendedCheckpoint_ProceedsOnFailure()
    {
        // On non-elevated Windows or CI environments, VSS creation will fail.
        // The key behavior: Recommended + failure should still return a result
        // (not throw) and the status should indicate failure or unavailability.
        var orchestrator = new VssSnapshotOrchestrator();

        if (!OperatingSystem.IsWindows())
        {
            var result = orchestrator.TryCreateSnapshots(
                CheckpointRequirement.Recommended,
                [@"C:\"],
                isDryRun: false);

            Assert.Equal(VssSnapshotStatus.Unavailable, result.Status);
            return;
        }

        // On Windows (non-elevated): VSS creation will likely fail but shouldn't throw.
        var windowsResult = orchestrator.TryCreateSnapshots(
            CheckpointRequirement.Recommended,
            [@"C:\"],
            isDryRun: false);

        // Either Failed or Success depending on elevation — just verify no exception.
        Assert.NotNull(windowsResult);
        Assert.False(string.IsNullOrEmpty(windowsResult.Message));
    }

    [Fact]
    public void RequiredCheckpoint_FailsClosedOnUnavailable()
    {
        if (OperatingSystem.IsWindows())
        {
            // Skip on Windows — behavior depends on elevation.
            return;
        }

        var orchestrator = new VssSnapshotOrchestrator();
        var result = orchestrator.TryCreateSnapshots(
            CheckpointRequirement.Required,
            [@"C:\"],
            isDryRun: false);

        Assert.Equal(VssSnapshotStatus.Unavailable, result.Status);
        Assert.False(result.Success);
    }

    // ── Integration tests: PlanExecutionService with VSS orchestrator ──────

    private PlanExecutionService CreateService(IInventoryRepository? inventoryRepository = null)
    {
        var options = Options.Create(new AtlasServiceOptions
        {
            QuarantineFolderName = ".atlas-quarantine-test"
        });

        return new PlanExecutionService(
            new AtlasPolicyEngine(),
            new RollbackPlanner(),
            options,
            inventoryRepository ?? new TrustedInventoryStub(),
            new SafeOptimizationFixExecutor(options),
            new VssSnapshotOrchestrator(),
            new NoOpOptimizationRepository());
    }

    [Fact]
    public async Task SafeBatch_VssNotAttempted()
    {
        var service = CreateService();
        var src = CreateTestFile("safe-move.txt", "safe content");
        var dest = TestPath("safe-dest.txt");

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-safe-vss",
                TouchedVolumes = [Path.GetPathRoot(_testRoot)!],
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.MovePath,
                        SourcePath = src,
                        DestinationPath = dest
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("NotNeeded", response.UndoCheckpoint.CheckpointEligibility);
        Assert.False(response.UndoCheckpoint.VssSnapshotCreated);
    }

    [Fact]
    public async Task DryRun_VssNotCreated()
    {
        var service = CreateService();
        var src = CreateTestFile("dry-del.txt", "dry run content");

        var request = new ExecutionRequest
        {
            Execute = false, // Dry-run
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-dry-vss",
                TouchedVolumes = [Path.GetPathRoot(_testRoot)!],
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.DeleteToQuarantine,
                        SourcePath = src,
                        Description = "Dry run delete"
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.UndoCheckpoint.VssSnapshotCreated);
        // Eligibility is still evaluated even in dry-run.
        Assert.False(string.IsNullOrEmpty(response.UndoCheckpoint.CheckpointEligibility));
    }

    [Fact]
    public async Task DestructiveBatch_EligibilityPopulated()
    {
        var service = CreateService();
        var src = CreateTestFile("dest-move.txt", "destructive batch");
        var dest = TestPath("dest-moved.txt");

        // Single destructive op → Recommended.
        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-dest-vss",
                TouchedVolumes = [Path.GetPathRoot(_testRoot)!],
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.DeleteToQuarantine,
                        SourcePath = src,
                        Description = "Test quarantine"
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Recommended", response.UndoCheckpoint.CheckpointEligibility);
        Assert.NotNull(response.UndoCheckpoint.CoveredVolumes);
        Assert.NotEmpty(response.UndoCheckpoint.EligibilityReason);
        // VssSnapshotReferences should be populated (possibly empty if VSS failed).
        Assert.NotNull(response.UndoCheckpoint.VssSnapshotReferences);
    }

    [Fact]
    public async Task CheckpointMetadata_HasVssFields()
    {
        var service = CreateService();
        var src = CreateTestFile("meta-vss.txt", "metadata vss");
        var dest = TestPath("meta-vss-dest.txt");

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-meta-vss",
                TouchedVolumes = [Path.GetPathRoot(_testRoot)!],
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.MovePath,
                        SourcePath = src,
                        DestinationPath = dest
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        var cp = response.UndoCheckpoint;
        // VssSnapshotCreated and VssSnapshotReferences should be populated (truthfully).
        Assert.NotNull(cp.VssSnapshotReferences);
        // For a safe batch, VSS is not needed, so no snapshot created.
        Assert.False(cp.VssSnapshotCreated);
    }
}
