using Atlas.Core.Contracts;

namespace Atlas.AI.Tests;

public sealed class PlanningContextProjectorTests
{
    private readonly PlanningContextProjector projector = new();

    [Fact]
    public void Project_BuildsInventorySummaryAndDuplicateEvidence()
    {
        var request = new PlanRequest
        {
            UserIntent = "Organize files",
            PolicyProfile = new PolicyProfile
            {
                DuplicateAutoDeleteConfidenceThreshold = 0.98d
            },
            Scan = new ScanResponse
            {
                Volumes =
                {
                    new VolumeSnapshot
                    {
                        RootPath = @"C:\",
                        IsReady = true,
                        TotalSizeBytes = 1000,
                        FreeSpaceBytes = 100
                    }
                },
                Inventory =
                {
                    CreateItem(@"C:\Users\Test\Finance\statement.pdf", "Documents", "application/pdf", SensitivityLevel.High, hasFingerprint: true),
                    CreateItem(@"C:\Users\Test\Downloads\statement-copy.pdf", "Documents", "application/pdf", SensitivityLevel.Low),
                    CreateItem(@"C:\Users\Test\Pictures\photo.png", "Images", "image/png", SensitivityLevel.Low)
                },
                Duplicates =
                {
                    new DuplicateGroup
                    {
                        GroupId = "dup-001",
                        CanonicalPath = @"C:\Users\Test\Finance\statement.pdf",
                        Confidence = 0.995d,
                        MatchConfidence = 0.9995d,
                        CanonicalReason = "high sensitivity (High); preferred location",
                        HasSensitiveMembers = true,
                        MaxSensitivity = SensitivityLevel.High,
                        Paths =
                        {
                            @"C:\Users\Test\Finance\statement.pdf",
                            @"C:\Users\Test\Downloads\statement-copy.pdf"
                        }
                    }
                }
            }
        };

        var context = projector.Project(request, 10);

        Assert.Equal(3, context.InventorySummary.TotalInventoryCount);
        Assert.Equal(1, context.InventorySummary.Sensitivity.High);
        Assert.Equal(1, context.InventorySummary.ContentIdentifiedCount);
        Assert.Equal(1, context.Duplicates.TotalDuplicateGroupCount);
        Assert.Equal(1, context.Duplicates.HighConfidenceGroupCount);
        Assert.Equal(1, context.VolumeSummary.LowFreeSpaceVolumeCount);

        var duplicate = Assert.Single(context.Duplicates.Groups);
        Assert.Equal("dup-001", duplicate.GroupId);
        Assert.Equal("Documents", duplicate.CanonicalCategory);
        Assert.Equal("application/pdf", duplicate.CanonicalMimeType);
        Assert.Equal(0.9995d, duplicate.MatchConfidence);
        Assert.Equal("high sensitivity (High); preferred location", duplicate.CanonicalReason);
        Assert.Equal(SensitivityLevel.High, duplicate.MaxSensitivity);
        Assert.True(duplicate.HasSensitiveMember);
        Assert.Single(duplicate.DuplicatePaths);
    }

    [Fact]
    public void Project_BoundsDuplicatePaths_ForPromptSafety()
    {
        var duplicatePaths = new List<string> { @"C:\Users\Test\Documents\master.txt" };
        for (var i = 0; i < 20; i++)
        {
            duplicatePaths.Add($@"C:\Users\Test\Downloads\copy-{i}.txt");
        }

        var request = new PlanRequest
        {
            PolicyProfile = new PolicyProfile(),
            Scan = new ScanResponse
            {
                Inventory = duplicatePaths.Select(path => CreateItem(path, "Documents", "text/plain", SensitivityLevel.Low)).ToList(),
                Duplicates =
                {
                    new DuplicateGroup
                    {
                        CanonicalPath = @"C:\Users\Test\Documents\master.txt",
                        Confidence = 0.999d,
                        Paths = duplicatePaths
                    }
                }
            }
        };

        var context = projector.Project(request, 5);

        var duplicate = Assert.Single(context.Duplicates.Groups);
        Assert.Equal(20, duplicate.DuplicatePathCount);
        Assert.Equal(8, duplicate.DuplicatePaths.Count);
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
            ContentFingerprint = hasFingerprint ? "fp-001" : string.Empty
        };
    }
}
