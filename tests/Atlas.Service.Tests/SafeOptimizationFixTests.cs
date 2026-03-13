using System.Runtime.InteropServices;
using System.Text.Json;
using Atlas.Core.Contracts;
using Atlas.Service.Services;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Tests;

public sealed class SafeOptimizationFixTests : IDisposable
{
    private readonly string _testRoot;
    private readonly SafeOptimizationFixExecutor _executor;
    private readonly IOptions<AtlasServiceOptions> _options;

    public SafeOptimizationFixTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-optfix-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _options = Options.Create(new AtlasServiceOptions
        {
            QuarantineFolderName = ".atlas-quarantine-test"
        });

        _executor = new SafeOptimizationFixExecutor(_options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
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

    private string TestDir(string relativePath)
    {
        var path = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    #region TemporaryFiles Tests

    [Fact]
    public void Apply_TemporaryFiles_DeletesFiles()
    {
        var tempDir = TestDir("temp-cleanup");
        CreateTestFile("temp-cleanup/file1.tmp", "temp data 1");
        CreateTestFile("temp-cleanup/file2.tmp", "temp data 2");
        CreateTestFile("temp-cleanup/sub/file3.tmp", "temp data 3");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = tempDir
        };

        var result = _executor.Apply(operation, "plan-temp");

        Assert.True(result.Success);
        Assert.Contains("Cleared", result.Message);
        Assert.Contains("3", result.Message);
        Assert.Empty(Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories));
        Assert.NotNull(result.RollbackState);
        Assert.False(result.RollbackState!.IsReversible);
        Assert.Equal(OptimizationKind.TemporaryFiles, result.RollbackState.Kind);
    }

    [Fact]
    public void Rollback_TemporaryFiles_NotReversible()
    {
        var tempDir = TestDir("temp-rollback");
        CreateTestFile("temp-rollback/file1.tmp");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = tempDir
        };

        var result = _executor.Apply(operation, "plan-temp-rb");

        Assert.NotNull(result.RollbackState);
        Assert.False(result.RollbackState!.IsReversible);
        Assert.Contains("Not reversible", result.RollbackState.Description);

        // Revert should report the non-reversible description.
        var revertResult = _executor.Revert(result.RollbackState);
        Assert.True(revertResult.Success);
        Assert.Contains("Not reversible", revertResult.Message);
    }

    #endregion

    #region CacheCleanup Tests

    [Fact]
    public void Apply_CacheCleanup_DeletesFiles()
    {
        var cacheDir = TestDir("cache-cleanup");
        CreateTestFile("cache-cleanup/data_0.cache", "cached data 1");
        CreateTestFile("cache-cleanup/data_1.cache", "cached data 2");
        CreateTestFile("cache-cleanup/nested/data_2.cache", "cached data 3");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.CacheCleanup,
            SourcePath = cacheDir
        };

        var result = _executor.Apply(operation, "plan-cache");

        Assert.True(result.Success);
        Assert.Contains("Cleared", result.Message);
        Assert.Contains("cache", result.Message);
        Assert.Empty(Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories));
        Assert.NotNull(result.RollbackState);
        Assert.False(result.RollbackState!.IsReversible);
        Assert.Equal(OptimizationKind.CacheCleanup, result.RollbackState.Kind);
    }

    #endregion

    #region DuplicateArchives Tests

    [Fact]
    public void Apply_DuplicateArchives_QuarantinesFiles()
    {
        var archivePath = CreateTestFile("archives/duplicate.zip", "archive data");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.DuplicateArchives,
            SourcePath = archivePath
        };

        var result = _executor.Apply(operation, "plan-dup-arch");

        Assert.True(result.Success);
        Assert.Contains("Quarantined", result.Message);
        Assert.False(File.Exists(archivePath));
        Assert.NotNull(result.RollbackState);
        Assert.True(result.RollbackState!.IsReversible);
        Assert.Equal(OptimizationKind.DuplicateArchives, result.RollbackState.Kind);

        // Verify rollback data contains quarantine path.
        var rollbackData = JsonSerializer.Deserialize<JsonElement>(result.RollbackState.RollbackData);
        var quarantinePath = rollbackData.GetProperty("QuarantinePath").GetString();
        Assert.False(string.IsNullOrWhiteSpace(quarantinePath));
        Assert.True(File.Exists(quarantinePath));
    }

    [Fact]
    public void Rollback_DuplicateArchives_IsReversible()
    {
        var archivePath = CreateTestFile("archives/restore-me.zip", "original archive data");
        var originalContent = File.ReadAllText(archivePath);

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.DuplicateArchives,
            SourcePath = archivePath
        };

        var applyResult = _executor.Apply(operation, "plan-dup-restore");
        Assert.True(applyResult.Success);
        Assert.True(applyResult.RollbackState!.IsReversible);
        Assert.False(File.Exists(archivePath));

        // Revert should restore the file.
        var revertResult = _executor.Revert(applyResult.RollbackState);
        Assert.True(revertResult.Success);
        Assert.Contains("Restored", revertResult.Message);
        Assert.True(File.Exists(archivePath));
        Assert.Equal(originalContent, File.ReadAllText(archivePath));
    }

    #endregion

    #region UserStartupEntry Tests

    [Fact]
    public void Apply_UserStartupEntry_Blocked_OnNonWindows()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // This test only validates non-Windows behavior; skip on Windows.
            return;
        }

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.UserStartupEntry,
            SourcePath = "SomeStartupApp"
        };

        var result = _executor.Apply(operation, "plan-startup");

        Assert.False(result.Success);
        Assert.Contains("only supported on Windows", result.Message);
    }

    #endregion

    #region Unsupported Kinds Tests

    [Theory]
    [InlineData(OptimizationKind.ScheduledTask)]
    [InlineData(OptimizationKind.BackgroundApplication)]
    [InlineData(OptimizationKind.LowDiskPressure)]
    public void Apply_UnsupportedKind_ReturnsBlocked(OptimizationKind kind)
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = kind,
            SourcePath = "some-target"
        };

        var result = _executor.Apply(operation, "plan-blocked");

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Message);
        Assert.Null(result.RollbackState);
    }

    [Fact]
    public void Apply_UnknownKind_ReturnsBlocked()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.Unknown,
            SourcePath = "some-target"
        };

        var result = _executor.Apply(operation, "plan-unknown");

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Message);
    }

    #endregion

    #region Missing Target Tests

    [Fact]
    public void Apply_MissingTarget_ReturnsFailure_TemporaryFiles()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = Path.Combine(_testRoot, "nonexistent-temp-dir")
        };

        var result = _executor.Apply(operation, "plan-missing");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public void Apply_MissingTarget_ReturnsFailure_CacheCleanup()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.CacheCleanup,
            SourcePath = Path.Combine(_testRoot, "nonexistent-cache-dir")
        };

        var result = _executor.Apply(operation, "plan-missing-cache");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public void Apply_MissingTarget_ReturnsFailure_DuplicateArchives()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.DuplicateArchives,
            SourcePath = Path.Combine(_testRoot, "nonexistent-archive.zip")
        };

        var result = _executor.Apply(operation, "plan-missing-dup");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    #endregion

    #region Already Clean / No-Op Tests

    [Fact]
    public void Apply_AlreadyClean_ReportsNoOp()
    {
        var emptyDir = TestDir("already-clean");
        // Directory exists but has no files.

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = emptyDir
        };

        var result = _executor.Apply(operation, "plan-noop");

        Assert.True(result.Success);
        Assert.Contains("already clean", result.Message);
        Assert.NotNull(result.RollbackState);
        Assert.False(result.RollbackState!.IsReversible);
    }

    [Fact]
    public void Apply_AlreadyClean_CacheCleanup_ReportsNoOp()
    {
        var emptyDir = TestDir("cache-already-clean");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.CacheCleanup,
            SourcePath = emptyDir
        };

        var result = _executor.Apply(operation, "plan-noop-cache");

        Assert.True(result.Success);
        Assert.Contains("already clean", result.Message);
    }

    #endregion

    #region Partial Failure Tests

    [Fact]
    public void PartialFailure_SomeFilesLocked()
    {
        var tempDir = TestDir("partial-failure");
        CreateTestFile("partial-failure/deletable1.tmp", "can delete");
        CreateTestFile("partial-failure/deletable2.tmp", "can delete too");
        var lockedFilePath = CreateTestFile("partial-failure/locked.tmp", "locked content");

        // Open a file handle to simulate a locked file.
        using var lockedStream = new FileStream(lockedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = tempDir
        };

        var result = _executor.Apply(operation, "plan-partial");

        Assert.True(result.Success); // Partial success is still reported as success.
        Assert.Contains("locked/failed", result.Message);
        Assert.Contains("2 deleted", result.Message);
        Assert.Contains("1 locked/failed", result.Message);

        Assert.NotNull(result.RollbackState);
        Assert.False(result.RollbackState!.IsReversible);

        // Verify the locked file still exists.
        lockedStream.Close();
        Assert.True(File.Exists(lockedFilePath));
    }

    #endregion

    #region Rollback Data Integrity Tests

    [Fact]
    public void Apply_TemporaryFiles_RollbackState_RecordsPaths()
    {
        var tempDir = TestDir("temp-paths-check");
        CreateTestFile("temp-paths-check/a.tmp");
        CreateTestFile("temp-paths-check/b.tmp");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = tempDir
        };

        var result = _executor.Apply(operation, "plan-paths");

        Assert.NotNull(result.RollbackState);
        Assert.False(string.IsNullOrWhiteSpace(result.RollbackState!.RollbackData));

        var data = JsonSerializer.Deserialize<JsonElement>(result.RollbackState.RollbackData);
        var deletedPaths = data.GetProperty("DeletedPaths");
        Assert.Equal(2, deletedPaths.GetArrayLength());
    }

    [Fact]
    public void Apply_DuplicateArchives_RollbackState_ContainsQuarantinePath()
    {
        var archivePath = CreateTestFile("dup-rb-data/archive.7z", "seven zip data");

        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.DuplicateArchives,
            SourcePath = archivePath
        };

        var result = _executor.Apply(operation, "plan-dup-rb-data");

        Assert.NotNull(result.RollbackState);
        Assert.True(result.RollbackState!.IsReversible);

        var data = JsonSerializer.Deserialize<JsonElement>(result.RollbackState.RollbackData);
        var originalPath = data.GetProperty("OriginalPath").GetString();
        var quarantinePath = data.GetProperty("QuarantinePath").GetString();
        var contentHash = data.GetProperty("ContentHash").GetString();

        Assert.Equal(archivePath, originalPath);
        Assert.False(string.IsNullOrWhiteSpace(quarantinePath));
        Assert.False(string.IsNullOrWhiteSpace(contentHash));
    }

    #endregion

    #region Empty SourcePath Tests

    [Fact]
    public void Apply_EmptySourcePath_DuplicateArchives_Fails()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.DuplicateArchives,
            SourcePath = ""
        };

        var result = _executor.Apply(operation, "plan-empty-src");

        Assert.False(result.Success);
        Assert.Contains("requires a source path", result.Message);
    }

    [Fact]
    public void Apply_EmptySourcePath_TemporaryFiles_Fails()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.ApplyOptimizationFix,
            OptimizationKind = OptimizationKind.TemporaryFiles,
            SourcePath = ""
        };

        var result = _executor.Apply(operation, "plan-empty-tmp");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    #endregion
}
