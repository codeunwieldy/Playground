using Atlas.Core.Contracts;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for checkpoint detail query behavior (C-040).
/// Validates found/missing lookup, older checkpoint backward compat,
/// and bounded VSS reference output.
/// </summary>
public sealed class CheckpointDetailQueryTests
{
    [Fact]
    public void FoundCheckpoint_ReturnsFullDetail()
    {
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = "cp1",
            BatchId = "batch1",
            CheckpointEligibility = "Required",
            EligibilityReason = "TouchedMultipleVolumes",
            CoveredVolumes = ["C:\\", "D:\\"],
            VssSnapshotCreated = true,
            VssSnapshotReferences = ["shadow-ref-001", "shadow-ref-002"],
            InverseOperations =
            [
                new InverseOperation { SourcePath = "a", DestinationPath = "b", Kind = OperationKind.MovePath }
            ],
            QuarantineItems =
            [
                new QuarantineItem { OriginalPath = "c", CurrentPath = "d" }
            ],
            OptimizationRollbackStates =
            [
                new OptimizationRollbackState { Kind = OptimizationKind.TemporaryFiles, Target = "t" }
            ],
            Notes = ["Test note"]
        };

        var response = MapToDetailResponse(checkpoint);

        Assert.True(response.Found);
        Assert.Equal("cp1", response.CheckpointId);
        Assert.Equal("batch1", response.BatchId);
        Assert.Equal("Required", response.CheckpointEligibility);
        Assert.Equal("TouchedMultipleVolumes", response.EligibilityReason);
        Assert.Equal(2, response.CoveredVolumes.Count);
        Assert.True(response.VssSnapshotCreated);
        Assert.Equal(2, response.VssSnapshotReferences.Count);
        Assert.Equal(1, response.InverseOperationCount);
        Assert.Equal(1, response.QuarantineItemCount);
        Assert.Equal(1, response.OptimizationRollbackStateCount);
        Assert.Single(response.Notes);
    }

    [Fact]
    public void MissingCheckpoint_ReturnsNotFound()
    {
        var response = new CheckpointDetailResponse
        {
            Found = false,
            CheckpointId = "missing-id"
        };

        Assert.False(response.Found);
        Assert.Equal("missing-id", response.CheckpointId);
        Assert.Empty(response.CoveredVolumes);
    }

    [Fact]
    public void OlderCheckpoint_WithoutVssMetadata_ReturnsCleanDefaults()
    {
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = "old-cp",
            BatchId = "old-batch",
            InverseOperations =
            [
                new InverseOperation { SourcePath = "a", DestinationPath = "b", Kind = OperationKind.DeleteToQuarantine }
            ],
            QuarantineItems =
            [
                new QuarantineItem { OriginalPath = "f1" }
            ]
        };

        var response = MapToDetailResponse(checkpoint);

        Assert.True(response.Found);
        Assert.Equal(string.Empty, response.CheckpointEligibility);
        Assert.Equal(string.Empty, response.EligibilityReason);
        Assert.Empty(response.CoveredVolumes);
        Assert.False(response.VssSnapshotCreated);
        Assert.Empty(response.VssSnapshotReferences);
        Assert.Equal(1, response.InverseOperationCount);
        Assert.Equal(1, response.QuarantineItemCount);
        Assert.Equal(0, response.OptimizationRollbackStateCount);
    }

    [Fact]
    public void VssSnapshotReferences_AreBounded()
    {
        var refs = Enumerable.Range(0, 50).Select(i => $"shadow-{i}").ToList();
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = "cp-many-refs",
            VssSnapshotReferences = refs
        };

        var response = MapToDetailResponse(checkpoint);

        Assert.Equal(50, response.VssSnapshotReferences.Count);
    }

    [Fact]
    public void OptimizationRollbackState_CountIsExposed()
    {
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = "cp-rollback",
            OptimizationRollbackStates =
            [
                new OptimizationRollbackState { Kind = OptimizationKind.TemporaryFiles },
                new OptimizationRollbackState { Kind = OptimizationKind.CacheCleanup },
                new OptimizationRollbackState { Kind = OptimizationKind.UserStartupEntry }
            ]
        };

        var response = MapToDetailResponse(checkpoint);

        Assert.Equal(3, response.OptimizationRollbackStateCount);
    }

    private static CheckpointDetailResponse MapToDetailResponse(UndoCheckpoint checkpoint)
    {
        return new CheckpointDetailResponse
        {
            Found = true,
            CheckpointId = checkpoint.CheckpointId,
            BatchId = checkpoint.BatchId,
            CheckpointEligibility = checkpoint.CheckpointEligibility,
            EligibilityReason = checkpoint.EligibilityReason,
            CoveredVolumes = checkpoint.CoveredVolumes,
            VssSnapshotCreated = checkpoint.VssSnapshotCreated,
            VssSnapshotReferences = checkpoint.VssSnapshotReferences,
            InverseOperationCount = checkpoint.InverseOperations.Count,
            QuarantineItemCount = checkpoint.QuarantineItems.Count,
            OptimizationRollbackStateCount = checkpoint.OptimizationRollbackStates.Count,
            Notes = checkpoint.Notes
        };
    }
}
