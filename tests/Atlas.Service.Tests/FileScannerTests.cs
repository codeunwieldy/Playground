using Atlas.Core.Contracts;
using Atlas.Core.Policies;
using Atlas.Service.Services;

namespace Atlas.Service.Tests;

public sealed class FileScannerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FileScanner _scanner;
    private readonly PolicyProfile _profile;

    public FileScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-scanner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _scanner = new FileScanner(new PathSafetyClassifier());
        _profile = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            MutableRoots = [_testRoot]
        };
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
    public async Task ScanAsync_PrefersStableLibraryPath_AsDuplicateCanonical()
    {
        var documentsCopy = CreateFile(@"Documents\budget.xlsx", "same-content");
        CreateFile(@"Downloads\budget (1).xlsx", "same-content");

        var response = await _scanner.ScanAsync(_profile, new ScanRequest
        {
            Roots = { _testRoot }
        }, CancellationToken.None);

        var duplicate = Assert.Single(response.Duplicates);
        Assert.Equal(documentsCopy, duplicate.CanonicalPath);
    }

    [Fact]
    public async Task ScanAsync_PrefersHigherSensitivityDuplicate_AsCanonical()
    {
        var financeCopy = CreateFile(@"Finance\statement.pdf", "same-content");
        CreateFile(@"Documents\statement-copy.pdf", "same-content");

        var response = await _scanner.ScanAsync(_profile, new ScanRequest
        {
            Roots = { _testRoot }
        }, CancellationToken.None);

        var duplicate = Assert.Single(response.Duplicates);
        Assert.Equal(financeCopy, duplicate.CanonicalPath);
    }

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
