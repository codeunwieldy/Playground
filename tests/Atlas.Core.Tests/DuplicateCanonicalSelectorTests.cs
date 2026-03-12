using Atlas.Core.Contracts;
using Atlas.Core.Scanning;

namespace Atlas.Core.Tests;

public sealed class DuplicateCanonicalSelectorTests
{
    [Fact]
    public void SelectCanonical_PrefersUserProtectedCandidate()
    {
        var protectedCandidate = CreateItem(
            path: @"C:\Users\Test\Downloads\report-copy.pdf",
            isProtectedByUser: true,
            sensitivity: SensitivityLevel.Low);

        var normalCandidate = CreateItem(
            path: @"C:\Users\Test\Documents\report.pdf",
            sensitivity: SensitivityLevel.High);

        var canonical = DuplicateCanonicalSelector.SelectCanonical([normalCandidate, protectedCandidate]);

        Assert.Equal(protectedCandidate.Path, canonical.Path);
    }

    [Fact]
    public void SelectCanonical_PrefersHigherSensitivity_WhenProtectionIsEqual()
    {
        var highSensitivity = CreateItem(
            path: @"C:\Users\Test\Finance\statement.pdf",
            sensitivity: SensitivityLevel.High);

        var lowSensitivity = CreateItem(
            path: @"C:\Users\Test\Documents\statement-copy.pdf",
            sensitivity: SensitivityLevel.Low);

        var canonical = DuplicateCanonicalSelector.SelectCanonical([lowSensitivity, highSensitivity]);

        Assert.Equal(highSensitivity.Path, canonical.Path);
    }

    [Fact]
    public void SelectCanonical_PrefersStableLibraryLocation_OverDownloads()
    {
        var documentsCopy = CreateItem(path: @"C:\Users\Test\Documents\budget.xlsx");
        var downloadsCopy = CreateItem(path: @"C:\Users\Test\Downloads\budget (1).xlsx");

        var canonical = DuplicateCanonicalSelector.SelectCanonical([downloadsCopy, documentsCopy]);

        Assert.Equal(documentsCopy.Path, canonical.Path);
    }

    [Fact]
    public void SelectCanonical_PrefersRicherMetadata_WhenSafetySignalsTie()
    {
        var richMetadata = CreateItem(
            path: @"C:\Users\Test\Projects\spec.pdf",
            mimeType: "application/pdf",
            contentFingerprint: "fp-001",
            category: "Documents");

        var sparseMetadata = CreateItem(
            path: @"C:\Users\Test\Projects\spec copy.pdf",
            mimeType: string.Empty,
            contentFingerprint: string.Empty,
            category: "Other");

        var canonical = DuplicateCanonicalSelector.SelectCanonical([sparseMetadata, richMetadata]);

        Assert.Equal(richMetadata.Path, canonical.Path);
    }

    [Fact]
    public void SelectCanonical_UsesRecencyAndPathLength_AsTieBreakers()
    {
        var newer = CreateItem(
            path: @"C:\Users\Test\Projects\notes.txt",
            lastModifiedUnixTimeSeconds: 200);

        var older = CreateItem(
            path: @"C:\Users\Test\Projects\Archive\notes.txt",
            lastModifiedUnixTimeSeconds: 100);

        var canonical = DuplicateCanonicalSelector.SelectCanonical([older, newer]);

        Assert.Equal(newer.Path, canonical.Path);
    }

    [Fact]
    public void SelectCanonical_Throws_WhenNoCandidatesExist()
    {
        Assert.Throws<ArgumentException>(() => DuplicateCanonicalSelector.SelectCanonical([]));
    }

    private static FileInventoryItem CreateItem(
        string path,
        SensitivityLevel sensitivity = SensitivityLevel.Low,
        bool isProtectedByUser = false,
        bool isSyncManaged = false,
        string mimeType = "application/octet-stream",
        string contentFingerprint = "fingerprint",
        string category = "Documents",
        long lastModifiedUnixTimeSeconds = 100)
    {
        return new FileInventoryItem
        {
            Path = path,
            Name = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            Category = category,
            MimeType = mimeType,
            ContentFingerprint = contentFingerprint,
            SizeBytes = 1024,
            LastModifiedUnixTimeSeconds = lastModifiedUnixTimeSeconds,
            Sensitivity = sensitivity,
            IsSyncManaged = isSyncManaged,
            IsProtectedByUser = isProtectedByUser,
            IsDuplicateCandidate = true
        };
    }
}
