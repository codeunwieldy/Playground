using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Core.Policies;
using Atlas.Service.Services;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for CheckpointEligibilityEvaluator and its integration with PlanExecutionService (C-033).
/// Validates deterministic eligibility rules, metadata population, and dry-run behavior.
/// </summary>
public sealed class CheckpointEligibilityTests : IDisposable
{
    private readonly string _testRoot;
    private readonly PolicyProfile _profile;

    public CheckpointEligibilityTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-ckpt-test-{Guid.NewGuid():N}");
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

    // ── Unit tests for CheckpointEligibilityEvaluator ───────────────────────

    [Fact]
    public void SmallReversibleBatch_NotNeeded()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-safe",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation { Kind = OperationKind.CreateDirectory, DestinationPath = @"C:\NewDir" },
                new PlanOperation { Kind = OperationKind.MovePath, SourcePath = @"C:\a.txt", DestinationPath = @"C:\b.txt" }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.NotNeeded, result.Requirement);
        Assert.False(result.HasDestructiveOperations);
        Assert.Equal(0, result.DestructiveOperationCount);
        Assert.NotEmpty(result.Reasons);
        Assert.Contains(result.Reasons, r => r.Contains("safe"));
    }

    [Fact]
    public void SingleDestructiveOp_Recommended()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-one-delete",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation { Kind = OperationKind.DeleteToQuarantine, SourcePath = @"C:\file.txt" }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.Recommended, result.Requirement);
        Assert.True(result.HasDestructiveOperations);
        Assert.Equal(1, result.DestructiveOperationCount);
        Assert.Contains(result.Reasons, r => r.Contains("Recommended") || r.Contains("recommended"));
    }

    [Fact]
    public void LargeDestructiveBatch_Required()
    {
        var operations = Enumerable.Range(0, 6)
            .Select(i => new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = $@"C:\file{i}.txt"
            })
            .ToList();

        var batch = new ExecutionBatch
        {
            PlanId = "plan-large-delete",
            TouchedVolumes = [@"C:\"],
            Operations = operations
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.Required, result.Requirement);
        Assert.True(result.HasDestructiveOperations);
        Assert.Equal(6, result.DestructiveOperationCount);
        Assert.Contains(result.Reasons, r => r.Contains("threshold"));
    }

    [Fact]
    public void CrossVolumeOperations_Required()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-cross-vol",
            TouchedVolumes = [@"C:\", @"D:\"],
            Operations =
            [
                new PlanOperation { Kind = OperationKind.MovePath, SourcePath = @"C:\a.txt", DestinationPath = @"D:\a.txt" }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.Required, result.Requirement);
        Assert.Contains(result.Reasons, r => r.Contains("cross-volume") || r.Contains("volumes"));
        Assert.Equal(2, result.CoveredVolumes.Count);
    }

    [Fact]
    public void UntrustedSession_Required()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-untrusted",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation { Kind = OperationKind.CreateDirectory, DestinationPath = @"C:\SafeDir" }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch, isTrustedSession: false);

        Assert.Equal(CheckpointRequirement.Required, result.Requirement);
        Assert.Contains(result.Reasons, r => r.Contains("degraded") || r.Contains("untrusted"));
    }

    [Fact]
    public void MixedSafeAndDestructive_Recommended()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-mixed",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation { Kind = OperationKind.CreateDirectory, DestinationPath = @"C:\NewDir" },
                new PlanOperation { Kind = OperationKind.MovePath, SourcePath = @"C:\a.txt", DestinationPath = @"C:\b.txt" },
                new PlanOperation { Kind = OperationKind.DeleteToQuarantine, SourcePath = @"C:\dup.txt" },
                new PlanOperation { Kind = OperationKind.RenamePath, SourcePath = @"C:\old.txt", DestinationPath = @"C:\new.txt" }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.Recommended, result.Requirement);
        Assert.True(result.HasDestructiveOperations);
        Assert.Equal(1, result.DestructiveOperationCount);
    }

    [Fact]
    public void SafeOptimizationFix_NotDestructive()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-safe-opt",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation
                {
                    Kind = OperationKind.ApplyOptimizationFix,
                    SourcePath = @"C:\Temp",
                    OptimizationKind = OptimizationKind.TemporaryFiles
                },
                new PlanOperation
                {
                    Kind = OperationKind.ApplyOptimizationFix,
                    SourcePath = @"C:\Cache",
                    OptimizationKind = OptimizationKind.CacheCleanup
                }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.NotNeeded, result.Requirement);
        Assert.False(result.HasDestructiveOperations);
    }

    [Fact]
    public void UnsafeOptimizationFix_IsDestructive()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-unsafe-opt",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation
                {
                    Kind = OperationKind.ApplyOptimizationFix,
                    SourcePath = @"C:\Startup",
                    OptimizationKind = OptimizationKind.UserStartupEntry
                }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.Recommended, result.Requirement);
        Assert.True(result.HasDestructiveOperations);
        Assert.Equal(1, result.DestructiveOperationCount);
    }

    [Fact]
    public void MergeDuplicateGroup_IsDestructive()
    {
        var batch = new ExecutionBatch
        {
            PlanId = "plan-merge",
            TouchedVolumes = [@"C:\"],
            Operations =
            [
                new PlanOperation
                {
                    Kind = OperationKind.MergeDuplicateGroup,
                    SourcePath = @"C:\Docs\dup.pdf",
                    DestinationPath = @"C:\Docs\original.pdf"
                }
            ]
        };

        var result = CheckpointEligibilityEvaluator.Evaluate(batch);

        Assert.Equal(CheckpointRequirement.Recommended, result.Requirement);
        Assert.True(result.HasDestructiveOperations);
    }

    // ── Integration tests: PlanExecutionService populates metadata ───────────

    [Fact]
    public async Task ExecutionService_PopulatesCheckpointMetadata()
    {
        var service = CreateService();
        var src = CreateTestFile("meta-source.txt", "metadata test content");
        var dest = TestPath("meta-dest.txt");

        // A batch with a destructive operation to trigger Recommended.
        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-meta",
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
        var checkpoint = response.UndoCheckpoint;
        Assert.False(string.IsNullOrEmpty(checkpoint.CheckpointEligibility));
        Assert.False(string.IsNullOrEmpty(checkpoint.EligibilityReason));
        Assert.NotNull(checkpoint.CoveredVolumes);
        Assert.False(checkpoint.VssSnapshotCreated); // Never created yet — deferred to VSS packet.
    }

    [Fact]
    public async Task DryRun_StillEvaluatesEligibility()
    {
        var service = CreateService();
        var src = CreateTestFile("dry-source.txt", "dry run content");
        var dest = TestPath("dry-dest.txt");

        var request = new ExecutionRequest
        {
            Execute = false, // Dry-run
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-dry",
                TouchedVolumes = [Path.GetPathRoot(_testRoot)!],
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.DeleteToQuarantine,
                        SourcePath = src,
                        Description = "Dry run test quarantine"
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        var checkpoint = response.UndoCheckpoint;

        // Eligibility should still be populated even though this is a dry-run.
        Assert.Equal("Recommended", checkpoint.CheckpointEligibility);
        Assert.True(checkpoint.EligibilityReason.Length > 0);
        Assert.NotNull(checkpoint.CoveredVolumes);

        // VssSnapshotCreated should remain false.
        Assert.False(checkpoint.VssSnapshotCreated);

        // The source file should still exist (dry-run does not mutate).
        Assert.True(File.Exists(src));
    }

    [Fact]
    public async Task ExecutionService_RequiredEligibility_BlocksWhenVssFails()
    {
        var service = CreateService();

        // Create 6 files to quarantine (above the threshold of 5).
        var operations = new List<PlanOperation>();
        for (var i = 0; i < 6; i++)
        {
            var src = CreateTestFile($"del-{i}.txt", $"content {i}");
            operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = src,
                Description = $"Delete file {i}"
            });
        }

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = _profile,
            Batch = new ExecutionBatch
            {
                PlanId = "plan-required",
                TouchedVolumes = [Path.GetPathRoot(_testRoot)!],
                Operations = operations
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        // C-036: Required checkpoint + VSS failure in non-elevated test env → execution blocked.
        Assert.False(response.Success);
        Assert.Contains(response.Messages, m => m.Contains("VSS checkpoint was required"));
    }

    [Fact]
    public async Task ExecutionService_SafeBatch_NotNeeded()
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
                PlanId = "plan-safe-exec",
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
        Assert.DoesNotContain(response.UndoCheckpoint.Notes, n => n.Contains("VSS checkpoint was required"));
    }
}
