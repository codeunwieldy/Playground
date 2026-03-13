using Atlas.Core.Contracts;
using Atlas.Storage;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Service.Tests;

public sealed class OptimizationExecutionHistoryTests : IDisposable
{
    private readonly ExecutionHistoryTestFixture _fixture;
    private readonly OptimizationRepository _repo;

    public OptimizationExecutionHistoryTests()
    {
        _fixture = new ExecutionHistoryTestFixture();
        _repo = new OptimizationRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task SaveAndRetrieve_SingleRecord()
    {
        var record = new OptimizationExecutionRecord
        {
            PlanId = "plan-001",
            FixKind = OptimizationKind.TemporaryFiles,
            Target = @"C:\Temp\cache",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            IsReversible = false,
            RollbackNote = "Not reversible: temporary files will repopulate naturally.",
            Message = "Cleared 42 temporary files under C:\\Temp\\cache.",
            CreatedUtc = DateTime.UtcNow
        };

        var recordId = await _repo.SaveExecutionRecordAsync(record);

        Assert.False(string.IsNullOrEmpty(recordId));
        Assert.Equal(record.RecordId, recordId);

        var history = await _repo.GetExecutionHistoryAsync();
        Assert.Single(history);

        var retrieved = history[0];
        Assert.Equal(record.RecordId, retrieved.RecordId);
        Assert.Equal("plan-001", retrieved.PlanId);
        Assert.Equal(OptimizationKind.TemporaryFiles, retrieved.FixKind);
        Assert.Equal(@"C:\Temp\cache", retrieved.Target);
        Assert.Equal(OptimizationExecutionAction.Applied, retrieved.Action);
        Assert.True(retrieved.Success);
        Assert.False(retrieved.IsReversible);
        Assert.Equal("Not reversible: temporary files will repopulate naturally.", retrieved.RollbackNote);
        Assert.Equal("Cleared 42 temporary files under C:\\Temp\\cache.", retrieved.Message);
    }

    [Fact]
    public async Task GetExecutionHistory_FilterByPlanId()
    {
        await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
        {
            PlanId = "plan-A",
            FixKind = OptimizationKind.CacheCleanup,
            Target = @"C:\Users\cache",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            CreatedUtc = DateTime.UtcNow
        });

        await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
        {
            PlanId = "plan-B",
            FixKind = OptimizationKind.UserStartupEntry,
            Target = "SomeApp",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            CreatedUtc = DateTime.UtcNow
        });

        await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
        {
            PlanId = "plan-A",
            FixKind = OptimizationKind.DuplicateArchives,
            Target = @"C:\archive.zip",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            CreatedUtc = DateTime.UtcNow
        });

        var planAHistory = await _repo.GetExecutionHistoryAsync(planId: "plan-A");
        Assert.Equal(2, planAHistory.Count);
        Assert.All(planAHistory, r => Assert.Equal("plan-A", r.PlanId));

        var planBHistory = await _repo.GetExecutionHistoryAsync(planId: "plan-B");
        Assert.Single(planBHistory);
        Assert.Equal("plan-B", planBHistory[0].PlanId);

        var allHistory = await _repo.GetExecutionHistoryAsync();
        Assert.Equal(3, allHistory.Count);
    }

    [Fact]
    public async Task GetExecutionHistoryForTarget_ReturnsMatchingRecords()
    {
        var target = @"C:\Temp\cleanup-target";

        await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
        {
            PlanId = "plan-1",
            FixKind = OptimizationKind.TemporaryFiles,
            Target = target,
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-10)
        });

        await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
        {
            PlanId = "plan-2",
            FixKind = OptimizationKind.TemporaryFiles,
            Target = target,
            Action = OptimizationExecutionAction.Reverted,
            Success = true,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5)
        });

        await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
        {
            PlanId = "plan-3",
            FixKind = OptimizationKind.CacheCleanup,
            Target = @"C:\Other\path",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            CreatedUtc = DateTime.UtcNow
        });

        var targetHistory = await _repo.GetExecutionHistoryForTargetAsync(target);
        Assert.Equal(2, targetHistory.Count);
        Assert.All(targetHistory, r => Assert.Equal(target, r.Target));
    }

    [Fact]
    public async Task EmptyHistory_ReturnsEmptyList()
    {
        var history = await _repo.GetExecutionHistoryAsync();
        Assert.Empty(history);

        var targetHistory = await _repo.GetExecutionHistoryForTargetAsync(@"C:\nonexistent");
        Assert.Empty(targetHistory);

        var planHistory = await _repo.GetExecutionHistoryAsync(planId: "no-such-plan");
        Assert.Empty(planHistory);
    }

    [Fact]
    public async Task MultipleRecords_SameTarget_AllPersisted()
    {
        var target = @"C:\Users\cache\browser";

        for (var i = 0; i < 5; i++)
        {
            await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
            {
                PlanId = $"plan-{i}",
                FixKind = OptimizationKind.CacheCleanup,
                Target = target,
                Action = i % 2 == 0 ? OptimizationExecutionAction.Applied : OptimizationExecutionAction.Reverted,
                Success = true,
                IsReversible = false,
                Message = $"Operation {i} on cache cleanup.",
                CreatedUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        var targetHistory = await _repo.GetExecutionHistoryForTargetAsync(target);
        Assert.Equal(5, targetHistory.Count);

        var allHistory = await _repo.GetExecutionHistoryAsync();
        Assert.Equal(5, allHistory.Count);
    }

    [Fact]
    public async Task Pagination_LimitAndOffset()
    {
        for (var i = 0; i < 10; i++)
        {
            await _repo.SaveExecutionRecordAsync(new OptimizationExecutionRecord
            {
                PlanId = "plan-paging",
                FixKind = OptimizationKind.TemporaryFiles,
                Target = $@"C:\target-{i}",
                Action = OptimizationExecutionAction.Applied,
                Success = true,
                CreatedUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        var firstPage = await _repo.GetExecutionHistoryAsync(limit: 3, offset: 0);
        Assert.Equal(3, firstPage.Count);

        var secondPage = await _repo.GetExecutionHistoryAsync(limit: 3, offset: 3);
        Assert.Equal(3, secondPage.Count);

        // Ensure no overlap.
        var firstPageIds = firstPage.Select(r => r.RecordId).ToHashSet();
        var secondPageIds = secondPage.Select(r => r.RecordId).ToHashSet();
        Assert.Empty(firstPageIds.Intersect(secondPageIds));
    }

    [Fact]
    public async Task FailedAction_PersistedCorrectly()
    {
        var record = new OptimizationExecutionRecord
        {
            PlanId = "plan-fail",
            FixKind = OptimizationKind.UserStartupEntry,
            Target = "BrokenApp",
            Action = OptimizationExecutionAction.Failed,
            Success = false,
            IsReversible = false,
            Message = "Cannot open HKCU Run registry key.",
            CreatedUtc = DateTime.UtcNow
        };

        await _repo.SaveExecutionRecordAsync(record);

        var history = await _repo.GetExecutionHistoryAsync(planId: "plan-fail");
        Assert.Single(history);

        var retrieved = history[0];
        Assert.Equal(OptimizationExecutionAction.Failed, retrieved.Action);
        Assert.False(retrieved.Success);
        Assert.Equal("Cannot open HKCU Run registry key.", retrieved.Message);
    }

    [Fact]
    public async Task RecordId_AutoGenerated_WhenEmpty()
    {
        var record = new OptimizationExecutionRecord
        {
            RecordId = string.Empty,
            PlanId = "plan-auto",
            FixKind = OptimizationKind.CacheCleanup,
            Target = @"C:\cache",
            Action = OptimizationExecutionAction.Applied,
            Success = true,
            CreatedUtc = DateTime.UtcNow
        };

        var id = await _repo.SaveExecutionRecordAsync(record);
        Assert.False(string.IsNullOrEmpty(id));

        var history = await _repo.GetExecutionHistoryAsync();
        Assert.Single(history);
        Assert.Equal(id, history[0].RecordId);
    }

    [Fact]
    public async Task CreatedUtc_DefaultsToNow_WhenDefault()
    {
        var before = DateTime.UtcNow;

        var record = new OptimizationExecutionRecord
        {
            PlanId = "plan-time",
            FixKind = OptimizationKind.TemporaryFiles,
            Target = @"C:\tmp",
            Action = OptimizationExecutionAction.Applied,
            Success = true
            // CreatedUtc intentionally left as default
        };

        await _repo.SaveExecutionRecordAsync(record);

        var after = DateTime.UtcNow;

        var history = await _repo.GetExecutionHistoryAsync();
        Assert.Single(history);
        Assert.InRange(history[0].CreatedUtc, before.AddSeconds(-1), after.AddSeconds(1));
    }
}

internal sealed class ExecutionHistoryTestFixture : IDisposable
{
    private readonly string _dbPath;

    public SqliteConnectionFactory ConnectionFactory { get; }

    public ExecutionHistoryTestFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"atlas-exec-history-test-{Guid.NewGuid():N}.db");
        var dataRoot = Path.GetDirectoryName(_dbPath)!;
        var fileName = Path.GetFileName(_dbPath);

        var bootstrapper = new AtlasDatabaseBootstrapper(new StorageOptions
        {
            DataRoot = dataRoot,
            DatabaseFileName = fileName
        });

        bootstrapper.InitializeAsync().GetAwaiter().GetResult();
        ConnectionFactory = new SqliteConnectionFactory(bootstrapper);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
