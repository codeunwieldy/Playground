using Atlas.Core.Contracts;
using Atlas.Core.Scanning;

namespace Atlas.Core.Tests;

public sealed class SensitivityScorerTests
{
    private static PolicyProfile DefaultProfile() => new()
    {
        ProtectedKeywords = ["passport", "tax", "medical", "contract", "payroll",
                             "bank", "identity", "recovery", "password"]
    };

    private static PolicyProfile EmptyProfile() => new()
    {
        ProtectedKeywords = []
    };

    // ── Critical extension tests ─────────────────────────────────────

    [Theory]
    [InlineData(".kdbx")]
    [InlineData(".kdb")]
    [InlineData(".pfx")]
    [InlineData(".p12")]
    [InlineData(".pem")]
    [InlineData(".key")]
    [InlineData(".jks")]
    [InlineData(".keystore")]
    [InlineData(".ppk")]
    public void Classify_CriticalExtension_ReturnsCritical(string extension)
    {
        var result = SensitivityScorer.Classify(
            DefaultProfile(),
            $@"C:\Users\Test\Documents\secrets{extension}",
            $"secrets{extension}",
            extension,
            "Other",
            null);

        Assert.Equal(SensitivityLevel.Critical, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "CriticalExtension" && e.Detail == extension);
    }

    // ── High from policy keywords ────────────────────────────────────

    [Fact]
    public void Classify_PathContainsPassport_ReturnsHigh()
    {
        var result = SensitivityScorer.Classify(
            DefaultProfile(),
            @"C:\Users\Test\Documents\passport-scan.jpg",
            "passport-scan.jpg",
            ".jpg",
            "Images",
            "image/jpeg");

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "PolicyKeyword" && e.Detail == "passport");
    }

    [Fact]
    public void Classify_PathContainsTax_ReturnsHigh()
    {
        var result = SensitivityScorer.Classify(
            DefaultProfile(),
            @"C:\Users\Test\tax\report.pdf",
            "report.pdf",
            ".pdf",
            "Documents",
            "application/pdf");

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "PolicyKeyword" && e.Detail == "tax");
    }

    [Fact]
    public void Classify_FilenameContainsBank_ReturnsHigh()
    {
        var result = SensitivityScorer.Classify(
            DefaultProfile(),
            @"C:\Users\Test\Documents\bank-details.txt",
            "bank-details.txt",
            ".txt",
            "Documents",
            null);

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "PolicyKeyword" && e.Detail == "bank");
    }

    // ── High from path segments ──────────────────────────────────────

    [Theory]
    [InlineData("Finance", "finance")]
    [InlineData("Legal", "legal")]
    [InlineData("Medical", "medical")]
    [InlineData("Insurance", "insurance")]
    [InlineData("Personnel", "personnel")]
    [InlineData("Accounting", "accounting")]
    [InlineData("Confidential", "confidential")]
    public void Classify_SensitivePathSegment_ReturnsHigh(string directory, string expectedSegment)
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            $@"C:\Users\Test\{directory}\report.pdf",
            "report.pdf",
            ".pdf",
            "Documents",
            "application/pdf");

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "SensitivePathSegment" && e.Detail == expectedSegment);
    }

    // ── High from filename terms ─────────────────────────────────────

    [Theory]
    [InlineData("W-2.pdf", "w-2")]
    [InlineData("1099.pdf", "1099")]
    [InlineData("SSN.xlsx", "ssn")]
    [InlineData("NDA-signed.pdf", "nda")]
    [InlineData("mortgage-docs.pdf", "mortgage")]
    [InlineData("paystub-jan.pdf", "paystub")]
    [InlineData("bank-statement-march.pdf", "bank-statement")]
    [InlineData("tax-return-2025.pdf", "tax-return")]
    [InlineData("deed-of-trust.pdf", "deed")]
    public void Classify_SensitiveFilenameTerm_ReturnsHigh(string fileName, string expectedTerm)
    {
        var extension = Path.GetExtension(fileName);
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            $@"C:\Users\Test\Downloads\{fileName}",
            fileName,
            extension,
            "Documents",
            null);

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "SensitiveFilenameTerm" && e.Detail == expectedTerm);
    }

    // ── Medium from category ─────────────────────────────────────────

    [Fact]
    public void Classify_GenericPdf_ReturnsMedium()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\readme.pdf",
            "readme.pdf",
            ".pdf",
            "Documents",
            "application/pdf");

        Assert.Equal(SensitivityLevel.Medium, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "DocumentCategory" && e.Detail == "Documents");
    }

    [Fact]
    public void Classify_GenericSpreadsheet_ReturnsMedium()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\data.xlsx",
            "data.xlsx",
            ".xlsx",
            "Spreadsheets",
            null);

        Assert.Equal(SensitivityLevel.Medium, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "DocumentCategory" && e.Detail == "Spreadsheets");
    }

    [Fact]
    public void Classify_GenericPresentation_ReturnsMedium()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\slides.pptx",
            "slides.pptx",
            ".pptx",
            "Presentations",
            null);

        Assert.Equal(SensitivityLevel.Medium, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "DocumentCategory" && e.Detail == "Presentations");
    }

    [Fact]
    public void Classify_Archive_ReturnsMedium()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\photos.zip",
            "photos.zip",
            ".zip",
            "Archives",
            "application/zip");

        Assert.Equal(SensitivityLevel.Medium, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "ArchiveCategory" && e.Detail == "Archives");
    }

    [Fact]
    public void Classify_DatabaseExtension_ReturnsMedium()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\app\data.sqlite",
            "data.sqlite",
            ".sqlite",
            "Other",
            null);

        Assert.Equal(SensitivityLevel.Medium, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "DatabaseExtension" && e.Detail == ".sqlite");
    }

    // ── Low default ──────────────────────────────────────────────────

    [Fact]
    public void Classify_PlainImage_ReturnsLow()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Pictures\sunset.jpg",
            "sunset.jpg",
            ".jpg",
            "Images",
            "image/jpeg");

        Assert.Equal(SensitivityLevel.Low, result.Level);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Classify_AudioFile_ReturnsLow()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Music\song.mp3",
            "song.mp3",
            ".mp3",
            "Audio",
            "audio/mpeg");

        Assert.Equal(SensitivityLevel.Low, result.Level);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Classify_VideoFile_ReturnsLow()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Videos\clip.mp4",
            "clip.mp4",
            ".mp4",
            "Video",
            "video/mp4");

        Assert.Equal(SensitivityLevel.Low, result.Level);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Classify_UnknownFile_ReturnsLow()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\misc\data.bin",
            "data.bin",
            ".bin",
            "Other",
            null);

        Assert.Equal(SensitivityLevel.Low, result.Level);
        Assert.Empty(result.Evidence);
    }

    // ── Priority and evidence accumulation ────────────────────────────

    [Fact]
    public void Classify_CriticalBeatsHigh_WhenBothMatch()
    {
        var result = SensitivityScorer.Classify(
            DefaultProfile(),
            @"C:\Users\Test\tax\server.key",
            "server.key",
            ".key",
            "Other",
            null);

        Assert.Equal(SensitivityLevel.Critical, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "CriticalExtension" && e.Detail == ".key");
        Assert.Contains(result.Evidence, e => e.Signal == "PolicyKeyword" && e.Detail == "tax");
    }

    [Fact]
    public void Classify_HighBeatsMedium_WhenBothMatch()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Finance\report.pdf",
            "report.pdf",
            ".pdf",
            "Documents",
            "application/pdf");

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "SensitivePathSegment" && e.Detail == "finance");
        Assert.Contains(result.Evidence, e => e.Signal == "DocumentCategory" && e.Detail == "Documents");
    }

    [Fact]
    public void Classify_EvidenceAccumulatesAllSignals()
    {
        var result = SensitivityScorer.Classify(
            DefaultProfile(),
            @"C:\Users\Test\Finance\tax-return-2025.pdf",
            "tax-return-2025.pdf",
            ".pdf",
            "Documents",
            "application/pdf");

        // Should have multiple evidence entries from different signal sources
        Assert.True(result.Evidence.Count >= 3);
        Assert.Contains(result.Evidence, e => e.Signal == "PolicyKeyword" && e.Detail == "tax");
        Assert.Contains(result.Evidence, e => e.Signal == "SensitivePathSegment" && e.Detail == "finance");
        Assert.Contains(result.Evidence, e => e.Signal == "SensitiveFilenameTerm" && e.Detail == "tax-return");
        Assert.Contains(result.Evidence, e => e.Signal == "DocumentCategory" && e.Detail == "Documents");
    }

    [Fact]
    public void Classify_EmptyKeywords_StillUsesBuiltInRules()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Finance\report.pdf",
            "report.pdf",
            ".pdf",
            "Documents",
            null);

        Assert.Equal(SensitivityLevel.High, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "SensitivePathSegment" && e.Detail == "finance");
    }

    // ── Content-aware cases ──────────────────────────────────────────

    [Fact]
    public void Classify_UnknownExtensionButDocumentCategory_ReturnsMedium()
    {
        // Content sniffing detected PDF inside a .dat file
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\data.dat",
            "data.dat",
            ".dat",
            "Documents",
            "application/pdf");

        Assert.Equal(SensitivityLevel.Medium, result.Level);
        Assert.Contains(result.Evidence, e => e.Signal == "DocumentCategory" && e.Detail == "Documents");
    }

    [Fact]
    public void Classify_TxtExtensionWithImageCategory_ReturnsLow()
    {
        // Content sniffing detected image inside a .txt file
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\photo.txt",
            "photo.txt",
            ".txt",
            "Images",
            "image/png");

        Assert.Equal(SensitivityLevel.Low, result.Level);
        Assert.Empty(result.Evidence);
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Fact]
    public void Classify_NullCategory_ReturnsLow()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            @"C:\Users\Test\Downloads\random.xyz",
            "random.xyz",
            ".xyz",
            null,
            null);

        Assert.Equal(SensitivityLevel.Low, result.Level);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void Classify_EmptyPath_ReturnsLow()
    {
        var result = SensitivityScorer.Classify(
            EmptyProfile(),
            string.Empty,
            "file.txt",
            ".txt",
            "Other",
            null);

        Assert.Equal(SensitivityLevel.Low, result.Level);
    }
}
