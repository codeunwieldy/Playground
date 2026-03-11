using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Round-trip tests for RecoveryRepository verifying checkpoint and quarantine item persistence.
/// </summary>
public sealed class RecoveryRepositoryTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly RecoveryRepository _repository;

    public RecoveryRepositoryTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _repository = new RecoveryRepository(_fixture.ConnectionFactory);
    }

    [Fact]
    public async Task SaveAndGetCheckpoint_RoundTrip_Success()
    {
        // Arrange
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = Guid.NewGuid().ToString("N"),
            Notes = ["Checkpoint created for batch execution", "Contains 3 inverse operations"],
            VssSnapshotReferences = ["VSS_SNAPSHOT_001"],
            InverseOperations =
            [
                new InverseOperation
                {
                    Kind = OperationKind.MovePath,
                    SourcePath = "D:\\dest\\file.txt",
                    DestinationPath = "C:\\source\\file.txt",
                    Description = "Move file back to original location"
                },
                new InverseOperation
                {
                    Kind = OperationKind.RestoreFromQuarantine,
                    SourcePath = "C:\\Quarantine\\deleted.txt",
                    DestinationPath = "C:\\Users\\Documents\\deleted.txt",
                    Description = "Restore deleted file"
                }
            ],
            QuarantineItems =
            [
                new QuarantineItem
                {
                    QuarantineId = Guid.NewGuid().ToString("N"),
                    OriginalPath = "C:\\Users\\Documents\\deleted.txt",
                    CurrentPath = "C:\\Quarantine\\deleted.txt",
                    PlanId = "plan123",
                    Reason = "User requested deletion"
                }
            ]
        };

        // Act
        var savedId = await _repository.SaveCheckpointAsync(checkpoint);
        var retrieved = await _repository.GetCheckpointAsync(savedId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(checkpoint.CheckpointId, retrieved.CheckpointId);
        Assert.Equal(checkpoint.BatchId, retrieved.BatchId);
        Assert.Equal(checkpoint.Notes.Count, retrieved.Notes.Count);
        Assert.Equal(checkpoint.InverseOperations.Count, retrieved.InverseOperations.Count);
        Assert.Equal(checkpoint.InverseOperations[0].Kind, retrieved.InverseOperations[0].Kind);
        Assert.Equal(checkpoint.InverseOperations[0].SourcePath, retrieved.InverseOperations[0].SourcePath);
        Assert.Equal(checkpoint.QuarantineItems.Count, retrieved.QuarantineItems.Count);
    }

    [Fact]
    public async Task GetCheckpointAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.GetCheckpointAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCheckpointForBatch_ReturnsMatchingCheckpoint()
    {
        // Arrange
        var batchId = Guid.NewGuid().ToString("N");
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = batchId,
            InverseOperations =
            [
                new InverseOperation
                {
                    Kind = OperationKind.MovePath,
                    SourcePath = "D:\\file.txt",
                    DestinationPath = "C:\\file.txt"
                }
            ]
        };

        await _repository.SaveCheckpointAsync(checkpoint);

        // Act
        var retrieved = await _repository.GetCheckpointForBatchAsync(batchId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(checkpoint.CheckpointId, retrieved.CheckpointId);
        Assert.Equal(batchId, retrieved.BatchId);
    }

    [Fact]
    public async Task GetCheckpointForBatch_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentBatchId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.GetCheckpointForBatchAsync(nonExistentBatchId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ListCheckpoints_ReturnsSummaries()
    {
        // Arrange
        var checkpoint1 = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = Guid.NewGuid().ToString("N"),
            InverseOperations =
            [
                new InverseOperation { Kind = OperationKind.MovePath }
            ]
        };
        var checkpoint2 = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = Guid.NewGuid().ToString("N"),
            InverseOperations =
            [
                new InverseOperation { Kind = OperationKind.MovePath },
                new InverseOperation { Kind = OperationKind.RestoreFromQuarantine }
            ]
        };

        await _repository.SaveCheckpointAsync(checkpoint1);
        await Task.Delay(10); // Ensure different timestamps
        await _repository.SaveCheckpointAsync(checkpoint2);

        // Act
        var summaries = await _repository.ListCheckpointsAsync(limit: 10);

        // Assert
        Assert.True(summaries.Count >= 2);

        var cp1Summary = summaries.FirstOrDefault(s => s.CheckpointId == checkpoint1.CheckpointId);
        var cp2Summary = summaries.FirstOrDefault(s => s.CheckpointId == checkpoint2.CheckpointId);

        Assert.NotNull(cp1Summary);
        Assert.NotNull(cp2Summary);
        Assert.Equal(1, cp1Summary.OperationCount);
        Assert.Equal(2, cp2Summary.OperationCount);

        // Verify most recent first ordering
        var cp2Index = summaries.ToList().IndexOf(cp2Summary);
        var cp1Index = summaries.ToList().IndexOf(cp1Summary);
        Assert.True(cp2Index < cp1Index, "Most recent checkpoint should appear first");
    }

    [Fact]
    public async Task DeleteCheckpoint_RemovesCheckpoint()
    {
        // Arrange
        var checkpoint = new UndoCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            BatchId = Guid.NewGuid().ToString("N")
        };
        await _repository.SaveCheckpointAsync(checkpoint);

        // Act
        var deleted = await _repository.DeleteCheckpointAsync(checkpoint.CheckpointId);
        var retrieved = await _repository.GetCheckpointAsync(checkpoint.CheckpointId);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task SaveAndGetQuarantineItem_RoundTrip_Success()
    {
        // Arrange
        var retentionUntil = DateTimeOffset.UtcNow.AddDays(30);
        var item = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = "C:\\Users\\Test\\Documents\\important.docx",
            CurrentPath = "C:\\Atlas\\Quarantine\\important_abc123.docx",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "User requested safe deletion with recovery option",
            RetentionUntilUnixTimeSeconds = retentionUntil.ToUnixTimeSeconds(),
            ContentHash = "SHA256:abc123def456"
        };

        // Act
        var savedId = await _repository.SaveQuarantineItemAsync(item);
        var retrieved = await _repository.GetQuarantineItemAsync(savedId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(item.QuarantineId, retrieved.QuarantineId);
        Assert.Equal(item.OriginalPath, retrieved.OriginalPath);
        Assert.Equal(item.CurrentPath, retrieved.CurrentPath);
        Assert.Equal(item.PlanId, retrieved.PlanId);
        Assert.Equal(item.Reason, retrieved.Reason);
        Assert.Equal(item.RetentionUntilUnixTimeSeconds, retrieved.RetentionUntilUnixTimeSeconds);
        Assert.Equal(item.ContentHash, retrieved.ContentHash);
    }

    [Fact]
    public async Task GetQuarantineItemByOriginalPath_Success()
    {
        // Arrange
        var originalPath = $"C:\\Users\\Test\\unique_path_{Guid.NewGuid():N}.txt";
        var item = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = originalPath,
            CurrentPath = "C:\\Quarantine\\file.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "Test quarantine"
        };

        await _repository.SaveQuarantineItemAsync(item);

        // Act
        var retrieved = await _repository.GetQuarantineItemByOriginalPathAsync(originalPath);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(item.QuarantineId, retrieved.QuarantineId);
        Assert.Equal(originalPath, retrieved.OriginalPath);
    }

    [Fact]
    public async Task GetQuarantineItemByOriginalPath_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentPath = $"C:\\NonExistent\\{Guid.NewGuid():N}.txt";

        // Act
        var result = await _repository.GetQuarantineItemByOriginalPathAsync(nonExistentPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetExpiredQuarantineItems_ReturnsExpiredOnly()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var expiredItem = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\Expired\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\expired.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "Expired item",
            RetentionUntilUnixTimeSeconds = now.AddDays(-10).ToUnixTimeSeconds() // Expired 10 days ago
        };
        var activeItem = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\Active\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\active.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "Active item",
            RetentionUntilUnixTimeSeconds = now.AddDays(20).ToUnixTimeSeconds() // Expires in 20 days
        };

        await _repository.SaveQuarantineItemAsync(expiredItem);
        await _repository.SaveQuarantineItemAsync(activeItem);

        // Act
        var expiredItems = await _repository.GetExpiredQuarantineItemsAsync(now.UtcDateTime);

        // Assert
        Assert.Contains(expiredItems, i => i.QuarantineId == expiredItem.QuarantineId);
        Assert.DoesNotContain(expiredItems, i => i.QuarantineId == activeItem.QuarantineId);
    }

    [Fact]
    public async Task DeleteQuarantineItem_RemovesItem()
    {
        // Arrange
        var item = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\ToDelete\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\todelete.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "Item to be deleted"
        };
        await _repository.SaveQuarantineItemAsync(item);

        // Act
        var deleted = await _repository.DeleteQuarantineItemAsync(item.QuarantineId);
        var retrieved = await _repository.GetQuarantineItemAsync(item.QuarantineId);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteQuarantineItem_NonExistent_ReturnsFalse()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.DeleteQuarantineItemAsync(nonExistentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ListQuarantineItems_ReturnsSummaries()
    {
        // Arrange
        var item1 = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\List\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\item1.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "First quarantine reason",
            RetentionUntilUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds()
        };
        var item2 = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\List\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\item2.txt",
            PlanId = Guid.NewGuid().ToString("N"),
            Reason = "Second quarantine reason",
            RetentionUntilUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds()
        };

        await _repository.SaveQuarantineItemAsync(item1);
        await _repository.SaveQuarantineItemAsync(item2);

        // Act
        var summaries = await _repository.ListQuarantineItemsAsync(limit: 10);

        // Assert
        Assert.True(summaries.Count >= 2);

        var item1Summary = summaries.FirstOrDefault(s => s.QuarantineId == item1.QuarantineId);
        var item2Summary = summaries.FirstOrDefault(s => s.QuarantineId == item2.QuarantineId);

        Assert.NotNull(item1Summary);
        Assert.NotNull(item2Summary);
        Assert.Equal(item1.OriginalPath, item1Summary.OriginalPath);
        Assert.Equal(item1.Reason, item1Summary.Reason);
    }

    [Fact]
    public async Task GetQuarantineItemsForPlan_ReturnsMatchingItems()
    {
        // Arrange
        var planId = Guid.NewGuid().ToString("N");
        var otherPlanId = Guid.NewGuid().ToString("N");

        var item1 = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\Plan\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\plan_item1.txt",
            PlanId = planId,
            Reason = "Item for target plan"
        };
        var item2 = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\Plan\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\plan_item2.txt",
            PlanId = planId,
            Reason = "Another item for target plan"
        };
        var otherItem = new QuarantineItem
        {
            QuarantineId = Guid.NewGuid().ToString("N"),
            OriginalPath = $"C:\\Other\\{Guid.NewGuid():N}.txt",
            CurrentPath = "C:\\Quarantine\\other_item.txt",
            PlanId = otherPlanId,
            Reason = "Item for other plan"
        };

        await _repository.SaveQuarantineItemAsync(item1);
        await _repository.SaveQuarantineItemAsync(item2);
        await _repository.SaveQuarantineItemAsync(otherItem);

        // Act
        var items = await _repository.GetQuarantineItemsForPlanAsync(planId);

        // Assert
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(planId, i.PlanId));
        Assert.Contains(items, i => i.QuarantineId == item1.QuarantineId);
        Assert.Contains(items, i => i.QuarantineId == item2.QuarantineId);
    }
}
