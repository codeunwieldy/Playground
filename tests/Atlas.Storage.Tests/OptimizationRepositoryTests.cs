using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Round-trip tests for OptimizationRepository verifying finding persistence.
/// </summary>
public sealed class OptimizationRepositoryTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly OptimizationRepository _repository;

    public OptimizationRepositoryTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _repository = new OptimizationRepository(_fixture.ConnectionFactory);
    }

    [Fact]
    public async Task SaveAndGetFinding_RoundTrip_Success()
    {
        // Arrange
        var finding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.TemporaryFiles,
            Target = "C:\\Users\\Test\\AppData\\Local\\Temp",
            Evidence = "Found 500 temporary files totaling 2.5 GB",
            CanAutoFix = true,
            RequiresApproval = false,
            RollbackPlan = "Files will be moved to quarantine before deletion"
        };

        // Act
        var savedId = await _repository.SaveFindingAsync(finding);
        var retrieved = await _repository.GetFindingAsync(savedId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(finding.FindingId, retrieved.FindingId);
        Assert.Equal(finding.Kind, retrieved.Kind);
        Assert.Equal(finding.Target, retrieved.Target);
        Assert.Equal(finding.Evidence, retrieved.Evidence);
        Assert.Equal(finding.CanAutoFix, retrieved.CanAutoFix);
        Assert.Equal(finding.RequiresApproval, retrieved.RequiresApproval);
        Assert.Equal(finding.RollbackPlan, retrieved.RollbackPlan);
    }

    [Fact]
    public async Task GetFindingAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.GetFindingAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ListFindings_ReturnsSummaries()
    {
        // Arrange
        var finding1 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.CacheCleanup,
            Target = "C:\\Users\\Test\\AppData\\Cache1",
            CanAutoFix = true
        };
        var finding2 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.UserStartupEntry,
            Target = "HKCU\\Software\\Run\\TestApp",
            CanAutoFix = false
        };

        await _repository.SaveFindingAsync(finding1);
        await Task.Delay(10); // Ensure different timestamps
        await _repository.SaveFindingAsync(finding2);

        // Act
        var summaries = await _repository.ListFindingsAsync(limit: 10);

        // Assert
        Assert.True(summaries.Count >= 2);

        var f1Summary = summaries.FirstOrDefault(s => s.FindingId == finding1.FindingId);
        var f2Summary = summaries.FirstOrDefault(s => s.FindingId == finding2.FindingId);

        Assert.NotNull(f1Summary);
        Assert.NotNull(f2Summary);
        Assert.Equal(finding1.Kind, f1Summary.Kind);
        Assert.Equal(finding1.Target, f1Summary.Target);
        Assert.Equal(finding1.CanAutoFix, f1Summary.CanAutoFix);
        Assert.Equal(finding2.Kind, f2Summary.Kind);
        Assert.False(f2Summary.CanAutoFix);

        // Verify most recent first ordering
        var f2Index = summaries.ToList().IndexOf(f2Summary);
        var f1Index = summaries.ToList().IndexOf(f1Summary);
        Assert.True(f2Index < f1Index, "Most recent finding should appear first");
    }

    [Fact]
    public async Task GetFindingsByKind_ReturnsMatchingKind()
    {
        // Arrange
        var kind = OptimizationKind.ScheduledTask;
        var finding1 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Target = "Task1"
        };
        var finding2 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Target = "Task2"
        };
        var otherFinding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.BackgroundApplication,
            Target = "OtherApp"
        };

        await _repository.SaveFindingAsync(finding1);
        await _repository.SaveFindingAsync(finding2);
        await _repository.SaveFindingAsync(otherFinding);

        // Act
        var findings = await _repository.GetFindingsByKindAsync(kind);

        // Assert
        Assert.True(findings.Count >= 2);
        Assert.All(findings, f => Assert.Equal(kind, f.Kind));
        Assert.Contains(findings, f => f.FindingId == finding1.FindingId);
        Assert.Contains(findings, f => f.FindingId == finding2.FindingId);
        Assert.DoesNotContain(findings, f => f.FindingId == otherFinding.FindingId);
    }

    [Fact]
    public async Task DeleteFindingsByKind_RemovesAll()
    {
        // Arrange
        var kindToDelete = OptimizationKind.DuplicateArchives;
        var finding1 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = kindToDelete,
            Target = "Archive1.zip"
        };
        var finding2 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = kindToDelete,
            Target = "Archive2.zip"
        };
        var keepFinding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.LowDiskPressure,
            Target = "C:\\"
        };

        await _repository.SaveFindingAsync(finding1);
        await _repository.SaveFindingAsync(finding2);
        await _repository.SaveFindingAsync(keepFinding);

        // Act
        var deletedCount = await _repository.DeleteFindingsByKindAsync(kindToDelete);

        // Assert
        Assert.Equal(2, deletedCount);

        var remaining = await _repository.GetFindingsByKindAsync(kindToDelete);
        Assert.Empty(remaining);

        var kept = await _repository.GetFindingAsync(keepFinding.FindingId);
        Assert.NotNull(kept);
    }

    [Fact]
    public async Task DeleteFinding_RemovesFinding()
    {
        // Arrange
        var finding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.TemporaryFiles,
            Target = "C:\\Temp\\ToDelete"
        };
        await _repository.SaveFindingAsync(finding);

        // Act
        var deleted = await _repository.DeleteFindingAsync(finding.FindingId);
        var retrieved = await _repository.GetFindingAsync(finding.FindingId);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteFinding_NonExistent_ReturnsFalse()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository.DeleteFindingAsync(nonExistentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetAutoFixableFindings_ReturnsOnlyAutoFixable()
    {
        // Arrange
        var autoFixable = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.TemporaryFiles,
            Target = $"C:\\Temp\\AutoFix_{Guid.NewGuid():N}",
            CanAutoFix = true
        };
        var requiresApproval = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.UserStartupEntry,
            Target = $"HKCU\\Run\\Manual_{Guid.NewGuid():N}",
            CanAutoFix = false
        };

        await _repository.SaveFindingAsync(autoFixable);
        await _repository.SaveFindingAsync(requiresApproval);

        // Act
        var findings = await _repository.GetAutoFixableFindingsAsync();

        // Assert
        Assert.Contains(findings, f => f.FindingId == autoFixable.FindingId);
        Assert.DoesNotContain(findings, f => f.FindingId == requiresApproval.FindingId);
        Assert.All(findings, f => Assert.True(f.CanAutoFix));
    }

    [Fact]
    public async Task GetFindingsByTarget_ReturnsMatchingTarget()
    {
        // Arrange
        var target = $"C:\\SharedTarget\\{Guid.NewGuid():N}";
        var finding1 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.TemporaryFiles,
            Target = target
        };
        var finding2 = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.CacheCleanup,
            Target = target
        };
        var otherFinding = new OptimizationFinding
        {
            FindingId = Guid.NewGuid().ToString("N"),
            Kind = OptimizationKind.TemporaryFiles,
            Target = $"C:\\OtherTarget\\{Guid.NewGuid():N}"
        };

        await _repository.SaveFindingAsync(finding1);
        await _repository.SaveFindingAsync(finding2);
        await _repository.SaveFindingAsync(otherFinding);

        // Act
        var findings = await _repository.GetFindingsByTargetAsync(target);

        // Assert
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(target, f.Target));
        Assert.Contains(findings, f => f.FindingId == finding1.FindingId);
        Assert.Contains(findings, f => f.FindingId == finding2.FindingId);
    }
}
