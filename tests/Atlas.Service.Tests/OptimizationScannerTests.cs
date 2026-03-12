using Atlas.Core.Contracts;
using Atlas.Service.Services;

namespace Atlas.Service.Tests;

public sealed class OptimizationScannerTests : IDisposable
{
    private readonly string _testRoot;

    public OptimizationScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-opt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
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

    [Fact]
    public void InspectCacheRoots_ProducesFinding_WhenThresholdExceeded()
    {
        var cacheRoot = Directory.CreateDirectory(Path.Combine(_testRoot, "cache")).FullName;
        CreateFile(cacheRoot, "cache.bin", 2048);

        var findings = OptimizationScanner.InspectCacheRoots(
            [cacheRoot],
            thresholdBytes: 1024,
            CancellationToken.None).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal(OptimizationKind.CacheCleanup, finding.Kind);
        Assert.Equal(cacheRoot, finding.Target);
        Assert.True(finding.CanAutoFix);
        Assert.False(finding.RequiresApproval);
    }

    [Fact]
    public void InspectCacheRoots_SkipsFinding_WhenBelowThreshold()
    {
        var cacheRoot = Directory.CreateDirectory(Path.Combine(_testRoot, "cache")).FullName;
        CreateFile(cacheRoot, "small.bin", 128);

        var findings = OptimizationScanner.InspectCacheRoots(
            [cacheRoot],
            thresholdBytes: 4096,
            CancellationToken.None).ToList();

        Assert.Empty(findings);
    }

    [Fact]
    public void InspectDuplicateArchivesInDirectory_FindsMatchingArchiveCopies()
    {
        var downloadsRoot = Directory.CreateDirectory(Path.Combine(_testRoot, "downloads")).FullName;
        CreateFile(downloadsRoot, "archive.zip", 512, "same-archive");
        CreateFile(downloadsRoot, "archive-copy.zip", 512, "same-archive");

        var findings = OptimizationScanner.InspectDuplicateArchivesInDirectory(downloadsRoot, CancellationToken.None).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal(OptimizationKind.DuplicateArchives, finding.Kind);
        Assert.True(finding.CanAutoFix);
        Assert.True(finding.RequiresApproval);
        Assert.Contains("matching files", finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectDuplicateArchivesInDirectory_IgnoresNonArchiveDuplicates()
    {
        var downloadsRoot = Directory.CreateDirectory(Path.Combine(_testRoot, "downloads")).FullName;
        CreateFile(downloadsRoot, "notes.txt", 256, "same-text");
        CreateFile(downloadsRoot, "notes-copy.txt", 256, "same-text");

        var findings = OptimizationScanner.InspectDuplicateArchivesInDirectory(downloadsRoot, CancellationToken.None).ToList();

        Assert.Empty(findings);
    }

    private static void CreateFile(string root, string fileName, int length, string fill = "x")
    {
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string(fill[0], length));
    }
}
