using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Round-trip tests for PlanRepository verifying plan and batch persistence.
/// </summary>
public sealed class PlanRepositoryTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly PlanRepository _repository;

    public PlanRepositoryTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _repository = new PlanRepository(_fixture.ConnectionFactory);
    }

    [Fact]
    public async Task SaveAndGetPlan_RoundTrip_Success()
    {
        // Arrange
        var plan = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "C:\\Users\\Test\\Documents",
            Rationale = "Organize duplicate files",
            Categories = ["Documents", "Images"],
            EstimatedBenefit = "500 MB disk space",
            RequiresReview = true,
            RollbackStrategy = "Restore from quarantine",
            Operations =
            [
                new PlanOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    Kind = OperationKind.MovePath,
                    SourcePath = "C:\\Users\\Test\\Documents\\file1.txt",
                    DestinationPath = "C:\\Users\\Test\\Documents\\Organized\\file1.txt",
                    Description = "Move file to organized folder",
                    Confidence = 0.95
                }
            ],
            RiskSummary = new RiskEnvelope
            {
                SensitivityScore = 0.3,
                SystemScore = 0.1,
                ReversibilityScore = 0.9,
                Confidence = 0.85,
                ApprovalRequirement = ApprovalRequirement.Review
            }
        };

        // Act
        var savedId = await _repository.SavePlanAsync(plan);
        var retrieved = await _repository.GetPlanAsync(savedId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(plan.PlanId, retrieved.PlanId);
        Assert.Equal(plan.Scope, retrieved.Scope);
        Assert.Equal(plan.Rationale, retrieved.Rationale);
        Assert.Equal(plan.Categories.Count, retrieved.Categories.Count);
        Assert.Equal(plan.Operations.Count, retrieved.Operations.Count);
        Assert.Equal(plan.Operations[0].Kind, retrieved.Operations[0].Kind);
        Assert.Equal(plan.Operations[0].SourcePath, retrieved.Operations[0].SourcePath);
        Assert.Equal(plan.RiskSummary.ApprovalRequirement, retrieved.RiskSummary.ApprovalRequirement);
    }

    [Fact]
    public async Task GetPlanAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.GetPlanAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ListPlans_ReturnsSummaries()
    {
        // Arrange
        var plan1 = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "C:\\Users\\Test\\Scope1",
            Rationale = "First plan summary"
        };
        var plan2 = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "C:\\Users\\Test\\Scope2",
            Rationale = "Second plan summary"
        };

        await _repository.SavePlanAsync(plan1);
        await Task.Delay(10); // Ensure different timestamps
        await _repository.SavePlanAsync(plan2);

        // Act
        var summaries = await _repository.ListPlansAsync(limit: 10);

        // Assert
        Assert.True(summaries.Count >= 2);

        // Verify most recent first ordering
        var plan2Summary = summaries.FirstOrDefault(s => s.PlanId == plan2.PlanId);
        var plan1Summary = summaries.FirstOrDefault(s => s.PlanId == plan1.PlanId);
        Assert.NotNull(plan2Summary);
        Assert.NotNull(plan1Summary);

        var plan2Index = summaries.ToList().IndexOf(plan2Summary);
        var plan1Index = summaries.ToList().IndexOf(plan1Summary);
        Assert.True(plan2Index < plan1Index, "Most recent plan should appear first");
    }

    [Fact]
    public async Task DeletePlan_RemovesPlan()
    {
        // Arrange
        var plan = new PlanGraph
        {
            PlanId = Guid.NewGuid().ToString("N"),
            Scope = "C:\\Users\\Test\\ToDelete",
            Rationale = "Plan to be deleted"
        };
        await _repository.SavePlanAsync(plan);

        // Act
        var deleted = await _repository.DeletePlanAsync(plan.PlanId);
        var retrieved = await _repository.GetPlanAsync(plan.PlanId);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeletePlan_NonExistent_ReturnsFalse()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.DeletePlanAsync(nonExistentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SaveAndGetBatch_RoundTrip_Success()
    {
        // Arrange
        var planId = Guid.NewGuid().ToString("N");
        var batch = new ExecutionBatch
        {
            BatchId = Guid.NewGuid().ToString("N"),
            PlanId = planId,
            TouchedVolumes = ["C:\\", "D:\\"],
            RequiresCheckpoint = true,
            IsDryRun = false,
            EstimatedImpact = "Moves 10 files",
            Operations =
            [
                new PlanOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    Kind = OperationKind.MovePath,
                    SourcePath = "C:\\source.txt",
                    DestinationPath = "D:\\dest.txt",
                    Confidence = 0.99
                }
            ]
        };

        // Act
        var savedId = await _repository.SaveBatchAsync(batch);
        var retrieved = await _repository.GetBatchAsync(savedId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(batch.BatchId, retrieved.BatchId);
        Assert.Equal(batch.PlanId, retrieved.PlanId);
        Assert.Equal(batch.TouchedVolumes.Count, retrieved.TouchedVolumes.Count);
        Assert.Equal(batch.RequiresCheckpoint, retrieved.RequiresCheckpoint);
        Assert.Equal(batch.Operations.Count, retrieved.Operations.Count);
    }

    [Fact]
    public async Task GetBatchesForPlan_ReturnsMatchingBatches()
    {
        // Arrange
        var planId = Guid.NewGuid().ToString("N");
        var otherPlanId = Guid.NewGuid().ToString("N");

        var batch1 = new ExecutionBatch
        {
            BatchId = Guid.NewGuid().ToString("N"),
            PlanId = planId,
            EstimatedImpact = "Batch 1 for target plan"
        };
        var batch2 = new ExecutionBatch
        {
            BatchId = Guid.NewGuid().ToString("N"),
            PlanId = planId,
            EstimatedImpact = "Batch 2 for target plan"
        };
        var otherBatch = new ExecutionBatch
        {
            BatchId = Guid.NewGuid().ToString("N"),
            PlanId = otherPlanId,
            EstimatedImpact = "Batch for other plan"
        };

        await _repository.SaveBatchAsync(batch1);
        await _repository.SaveBatchAsync(batch2);
        await _repository.SaveBatchAsync(otherBatch);

        // Act
        var batches = await _repository.GetBatchesForPlanAsync(planId);

        // Assert
        Assert.Equal(2, batches.Count);
        Assert.All(batches, b => Assert.Equal(planId, b.PlanId));
        Assert.Contains(batches, b => b.BatchId == batch1.BatchId);
        Assert.Contains(batches, b => b.BatchId == batch2.BatchId);
    }

    [Fact]
    public async Task GetBatchAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.GetBatchAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }
}
