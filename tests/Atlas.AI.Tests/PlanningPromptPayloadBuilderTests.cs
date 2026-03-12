using Atlas.Core.Contracts;

namespace Atlas.AI.Tests;

public sealed class PlanningPromptPayloadBuilderTests
{
    private readonly PlanningPromptPayloadBuilder builder = new();

    [Fact]
    public void Build_RedactsHighSensitivityInventoryPaths_WhenUploadsDisabled()
    {
        var request = BuildRequest(uploadSensitiveContent: false);

        var payload = builder.Build(request, 10);

        var sensitiveItems = payload.Inventory.Where(static item => item.Sensitivity == SensitivityLevel.High).ToList();
        Assert.Equal(2, sensitiveItems.Count);
        Assert.All(sensitiveItems, static item =>
        {
            Assert.True(item.PathRedacted);
            Assert.StartsWith("sensitive-item-", item.Path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(item.Path, item.ReferenceId);
            Assert.Equal("[redacted].pdf", item.Name);
        });

        var low = Assert.Single(payload.Inventory.Where(static item => item.Sensitivity == SensitivityLevel.Low));
        Assert.False(low.PathRedacted);
        Assert.Equal(@"C:\Users\Test\Pictures\photo.png", low.Path);
    }

    [Fact]
    public void Build_PreservesSensitivePaths_WhenUploadsEnabled()
    {
        var request = BuildRequest(uploadSensitiveContent: true);

        var payload = builder.Build(request, 10);

        var sensitiveItems = payload.Inventory.Where(static item => item.Sensitivity == SensitivityLevel.High).ToList();
        Assert.Equal(2, sensitiveItems.Count);
        Assert.All(sensitiveItems, static item => Assert.False(item.PathRedacted));
        Assert.Contains(sensitiveItems, static item => item.Path == @"C:\Users\Test\Finance\statement.pdf" && item.Name == "statement.pdf");
        Assert.Contains(sensitiveItems, static item => item.Path == @"C:\Users\Test\Downloads\statement-copy.pdf" && item.Name == "statement-copy.pdf");
    }

    [Fact]
    public void Build_RedactsSensitiveDuplicatePaths_Consistently()
    {
        var request = BuildRequest(uploadSensitiveContent: false);

        var payload = builder.Build(request, 10);

        var group = Assert.Single(payload.DuplicateProjection.Groups);
        Assert.True(group.ContainsRedactedPaths);
        Assert.StartsWith("sensitive-item-", group.CanonicalPath, StringComparison.OrdinalIgnoreCase);
        Assert.All(group.DuplicatePaths, static path =>
            Assert.StartsWith("sensitive-item-", path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0.9995d, group.MatchConfidence);
        Assert.Equal("high sensitivity (High); preferred location", group.CanonicalReason);
        Assert.Equal(SensitivityLevel.High, group.MaxSensitivity);
    }

    private static PlanRequest BuildRequest(bool uploadSensitiveContent)
    {
        return new PlanRequest
        {
            UserIntent = "Organize files",
            PolicyProfile = new PolicyProfile
            {
                UploadSensitiveContent = uploadSensitiveContent,
                DuplicateAutoDeleteConfidenceThreshold = 0.98d
            },
            Scan = new ScanResponse
            {
                Inventory =
                {
                    new FileInventoryItem
                    {
                        Path = @"C:\Users\Test\Finance\statement.pdf",
                        Name = "statement.pdf",
                        Extension = ".pdf",
                        Category = "Documents",
                        MimeType = "application/pdf",
                        Sensitivity = SensitivityLevel.High,
                        IsDuplicateCandidate = true
                    },
                    new FileInventoryItem
                    {
                        Path = @"C:\Users\Test\Pictures\photo.png",
                        Name = "photo.png",
                        Extension = ".png",
                        Category = "Images",
                        MimeType = "image/png",
                        Sensitivity = SensitivityLevel.Low,
                        IsDuplicateCandidate = false
                    },
                    new FileInventoryItem
                    {
                        Path = @"C:\Users\Test\Downloads\statement-copy.pdf",
                        Name = "statement-copy.pdf",
                        Extension = ".pdf",
                        Category = "Documents",
                        MimeType = "application/pdf",
                        Sensitivity = SensitivityLevel.High,
                        IsDuplicateCandidate = true
                    }
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
    }
}
