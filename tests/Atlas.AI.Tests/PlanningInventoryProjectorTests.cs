using Atlas.Core.Contracts;

namespace Atlas.AI.Tests;

public sealed class PlanningInventoryProjectorTests
{
    private readonly PlanningInventoryProjector projector = new();

    [Fact]
    public void Project_PrioritizesHighRiskItems_WhenBudgetIsSmall()
    {
        var inventory = new[]
        {
            CreateItem(@"C:\Users\Test\Pictures\photo.jpg", "Images", "image/jpeg", SensitivityLevel.Low),
            CreateItem(@"C:\Users\Test\Documents\notes.txt", "Documents", "text/plain", SensitivityLevel.Low),
            CreateItem(@"C:\Users\Test\Finance\tax-return.pdf", "Documents", "application/pdf", SensitivityLevel.High, hasFingerprint: true)
        };

        var projected = projector.Project(inventory, 1);

        var item = Assert.Single(projected);
        Assert.Equal(@"C:\Users\Test\Finance\tax-return.pdf", item.Path);
    }

    [Fact]
    public void Project_PreservesCategoryDiversity_BeforeFillingRemainingBudget()
    {
        var inventory = new[]
        {
            CreateItem(@"C:\Users\Test\Documents\contract.pdf", "Documents", "application/pdf", SensitivityLevel.High),
            CreateItem(@"C:\Users\Test\Pictures\photo.png", "Images", "image/png", SensitivityLevel.Low),
            CreateItem(@"C:\Users\Test\Music\song.mp3", "Audio", "audio/mpeg", SensitivityLevel.Low),
            CreateItem(@"C:\Users\Test\Downloads\copy.pdf", "Documents", "application/pdf", SensitivityLevel.Low)
        };

        var projected = projector.Project(inventory, 3);

        Assert.Equal(3, projected.Count);
        Assert.Contains(projected, static item => item.Category == "Documents");
        Assert.Contains(projected, static item => item.Category == "Images");
        Assert.Contains(projected, static item => item.Category == "Audio");
    }

    [Fact]
    public void Project_ExposesMimeAndFingerprintPresence_WithoutRawFingerprint()
    {
        var inventory = new[]
        {
            CreateItem(
                @"C:\Users\Test\Documents\statement.pdf",
                "Documents",
                "application/pdf",
                SensitivityLevel.High,
                hasFingerprint: true)
        };

        var projected = projector.Project(inventory, 5);

        var item = Assert.Single(projected);
        Assert.Equal("application/pdf", item.MimeType);
        Assert.True(item.HasContentFingerprint);
    }

    private static FileInventoryItem CreateItem(
        string path,
        string category,
        string mimeType,
        SensitivityLevel sensitivity,
        bool hasFingerprint = false)
    {
        return new FileInventoryItem
        {
            Path = path,
            Name = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            Category = category,
            MimeType = mimeType,
            SizeBytes = 1024,
            LastModifiedUnixTimeSeconds = 100,
            Sensitivity = sensitivity,
            IsSyncManaged = false,
            IsDuplicateCandidate = true,
            IsProtectedByUser = false,
            ContentFingerprint = hasFingerprint ? "abc123" : string.Empty
        };
    }
}
