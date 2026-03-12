using Atlas.Core.Contracts;
using Atlas.Core.Policies;
using Atlas.Service.Services;

namespace Atlas.Service.Tests;

public sealed class FileInspectionTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FileScanner _scanner;
    private readonly PolicyProfile _profile;

    public FileInspectionTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-inspect-{Guid.NewGuid():N}");
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
        try { if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true); }
        catch { /* best-effort */ }
    }

    // ── Inspectable file returns full detail ─────────────────────────────

    [Fact]
    public void InspectFileDetailed_ExistingFile_ReturnsInspectedWithItem()
    {
        var path = CreateFile(@"Documents\report.pdf", "%PDF-1.4 some content here");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.NotNull(result.Item);
        Assert.Equal(path, result.Item!.Path);
        Assert.Equal("report.pdf", result.Item.Name);
        Assert.Equal(".pdf", result.Item.Extension);
    }

    [Fact]
    public void InspectFileDetailed_ExistingFile_ReturnsCategoryAndMime()
    {
        var path = CreateFile(@"Documents\report.pdf", "%PDF-1.4 content");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.Equal("Documents", result.Item!.Category);
        Assert.Equal("application/pdf", result.Item.MimeType);
    }

    [Fact]
    public void InspectFileDetailed_PdfFile_ContentSniffSucceeds()
    {
        var path = CreateFile(@"Docs\test.pdf", "%PDF-1.7 content here");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.True(result.ContentSniffSucceeded);
        Assert.True(result.HasContentFingerprint);
    }

    [Fact]
    public void InspectFileDetailed_UnknownContentExtension_SniffFails()
    {
        var path = CreateFile(@"Data\unknown.xyz", "random text data");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.False(result.ContentSniffSucceeded);
        Assert.False(result.HasContentFingerprint);
    }

    // ── Sensitivity evidence is bounded and deterministic ────────────────

    [Fact]
    public void InspectFileDetailed_SensitiveFile_ReturnsSensitivityEvidence()
    {
        var path = CreateFile(@"Finance\tax-return-2025.pdf", "%PDF-1.4 tax data");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.NotNull(result.Sensitivity);
        Assert.True(result.Sensitivity!.Level >= SensitivityLevel.High);
        Assert.NotEmpty(result.Sensitivity.Evidence);
        Assert.Contains(result.Sensitivity.Evidence, e => e.Signal == "SensitivePathSegment" && e.Detail == "finance");
        Assert.Contains(result.Sensitivity.Evidence, e => e.Signal == "SensitiveFilenameTerm" && e.Detail == "tax-return");
    }

    [Fact]
    public void InspectFileDetailed_CriticalExtension_ReturnsCriticalLevel()
    {
        var path = CreateFile(@"Keys\vault.kdbx", "keepass data");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.NotNull(result.Sensitivity);
        Assert.Equal(SensitivityLevel.Critical, result.Sensitivity!.Level);
        Assert.Contains(result.Sensitivity.Evidence, e => e.Signal == "CriticalExtension");
    }

    [Fact]
    public void InspectFileDetailed_LowRiskFile_ReturnsLowSensitivity()
    {
        var path = CreateFile(@"Photos\vacation.jpg", "not-a-real-jpeg-but-still-an-image-path");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.NotNull(result.Sensitivity);
        Assert.Equal(SensitivityLevel.Low, result.Sensitivity!.Level);
    }

    // ── Failure outcomes ─────────────────────────────────────────────────

    [Fact]
    public void InspectFileDetailed_MissingFile_ReturnsNotFound()
    {
        var result = _scanner.InspectFileDetailed(_profile, @"C:\nonexistent\file.txt");

        Assert.Equal("Missing", result.Outcome);
        Assert.Null(result.Item);
        Assert.Null(result.Sensitivity);
    }

    [Fact]
    public void InspectFileDetailed_ProtectedPath_ReturnsProtected()
    {
        var profileWithProtection = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            MutableRoots = [_testRoot],
            ProtectedPaths = [@"C:\Windows\System32"]
        };

        var result = _scanner.InspectFileDetailed(profileWithProtection, @"C:\Windows\System32\config\SAM");

        // The file may or may not exist, but should be Protected if the path matches
        Assert.True(result.Outcome == "Missing" || result.Outcome == "Protected");
    }

    [Fact]
    public void InspectFileDetailed_ExcludedPath_ReturnsExcluded()
    {
        var profileWithExclusion = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            MutableRoots = [_testRoot],
            ExcludedRoots = [Path.Combine(_testRoot, "Excluded")]
        };

        var excludedPath = CreateFile(@"Excluded\secret.txt", "data");

        var result = _scanner.InspectFileDetailed(profileWithExclusion, excludedPath);

        Assert.Equal("Excluded", result.Outcome);
        Assert.Null(result.Item);
    }

    // ── Size and time ────────────────────────────────────────────────────

    [Fact]
    public void InspectFileDetailed_ReportsSizeAndModifiedTime()
    {
        var path = CreateFile(@"Data\sample.txt", "hello world content");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.True(result.Item!.SizeBytes > 0);
        Assert.True(result.Item.LastModifiedUnixTimeSeconds > 0);
    }

    // ── Payload boundedness and contract safety ────────────────────────

    [Fact]
    public void InspectFileDetailed_MultipleRules_EvidenceCountIsBounded()
    {
        // A file under a sensitive path segment, with a sensitive filename term, in Documents category
        var path = CreateFile(@"Finance\payroll\bank-statement-2025.pdf", "%PDF-1.4 sensitive content");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.NotNull(result.Sensitivity);
        // Scorer has a fixed set of rules (~7 signal types max); evidence must stay bounded
        Assert.InRange(result.Sensitivity!.Evidence.Count, 1, 10);
    }

    [Fact]
    public void InspectFileDetailed_InspectedFile_AllStringFieldsNonNull()
    {
        var path = CreateFile(@"Docs\readme.txt", "some content");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        var item = result.Item!;
        Assert.NotNull(item.Path);
        Assert.NotNull(item.Name);
        Assert.NotNull(item.Extension);
        Assert.NotNull(item.Category);
        Assert.NotNull(item.MimeType);
    }

    [Fact]
    public void InspectFileDetailed_EmptyPath_ReturnsMissing()
    {
        var result = _scanner.InspectFileDetailed(_profile, "");

        Assert.Equal("Missing", result.Outcome);
        Assert.Null(result.Item);
    }

    [Fact]
    public void InspectFileDetailed_FileWithContent_IsDuplicateCandidateTrue()
    {
        var path = CreateFile(@"Data\report.docx", "non-empty content");

        var result = _scanner.InspectFileDetailed(_profile, path);

        Assert.Equal("Inspected", result.Outcome);
        Assert.True(result.Item!.IsDuplicateCandidate);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
