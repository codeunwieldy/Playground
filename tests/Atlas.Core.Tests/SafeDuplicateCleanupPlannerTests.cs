using Atlas.Core.Contracts;
using Atlas.Core.Planning;

namespace Atlas.Core.Tests;

public sealed class SafeDuplicateCleanupPlannerTests
{
    private readonly SafeDuplicateCleanupPlanner planner = new();

    [Fact]
    public void BuildOperations_CreatesQuarantineOperation_ForLowRiskDuplicate()
    {
        var duplicate = CreateGroup(
            canonicalPath: @"C:\Users\Test\Documents\report.pdf",
            duplicatePaths:
            [
                @"C:\Users\Test\Documents\report.pdf",
                @"C:\Users\Test\Downloads\report-copy.pdf"
            ]);

        var inventory = new[]
        {
            CreateItem(@"C:\Users\Test\Documents\report.pdf"),
            CreateItem(@"C:\Users\Test\Downloads\report-copy.pdf")
        };

        var result = planner.BuildOperations([duplicate], inventory, 0.98d);

        var operation = Assert.Single(result.Operations);
        Assert.Equal(OperationKind.DeleteToQuarantine, operation.Kind);
        Assert.Equal(@"C:\Users\Test\Downloads\report-copy.pdf", operation.SourcePath);
        Assert.True(operation.MarksSafeDuplicate);
        Assert.Equal(SensitivityLevel.Low, operation.Sensitivity);
        Assert.Contains("canonical copy", operation.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildOperations_SkipsSensitiveDuplicates()
    {
        var duplicate = CreateGroup(
            canonicalPath: @"C:\Users\Test\Documents\tax.pdf",
            duplicatePaths:
            [
                @"C:\Users\Test\Documents\tax.pdf",
                @"C:\Users\Test\Finance\tax-copy.pdf"
            ]);

        var inventory = new[]
        {
            CreateItem(@"C:\Users\Test\Documents\tax.pdf"),
            CreateItem(@"C:\Users\Test\Finance\tax-copy.pdf", sensitivity: SensitivityLevel.High)
        };

        var result = planner.BuildOperations([duplicate], inventory, 0.98d);

        Assert.Empty(result.Operations);
        Assert.Equal(1, result.SkippedSensitive);
    }

    [Fact]
    public void BuildOperations_SkipsSyncManagedAndProtectedCandidates()
    {
        var syncGroup = CreateGroup(
            canonicalPath: @"C:\Users\Test\Documents\photo.jpg",
            duplicatePaths:
            [
                @"C:\Users\Test\Documents\photo.jpg",
                @"C:\Users\Test\OneDrive\photo.jpg"
            ]);

        var protectedGroup = CreateGroup(
            canonicalPath: @"C:\Users\Test\Documents\plan.md",
            duplicatePaths:
            [
                @"C:\Users\Test\Documents\plan.md",
                @"C:\Users\Test\Downloads\plan-copy.md"
            ]);

        var inventory = new[]
        {
            CreateItem(@"C:\Users\Test\Documents\photo.jpg"),
            CreateItem(@"C:\Users\Test\OneDrive\photo.jpg", isSyncManaged: true),
            CreateItem(@"C:\Users\Test\Documents\plan.md"),
            CreateItem(@"C:\Users\Test\Downloads\plan-copy.md", isProtectedByUser: true)
        };

        var result = planner.BuildOperations([syncGroup, protectedGroup], inventory, 0.98d);

        Assert.Empty(result.Operations);
        Assert.Equal(1, result.SkippedSyncManaged);
        Assert.Equal(1, result.SkippedProtectedByUser);
        Assert.True(result.HasSkippedRiskyCandidates);
    }

    [Fact]
    public void BuildOperations_SkipsGroupsBelowConfidenceThreshold()
    {
        var duplicate = CreateGroup(
            canonicalPath: @"C:\Users\Test\Documents\notes.txt",
            confidence: 0.8d,
            duplicatePaths:
            [
                @"C:\Users\Test\Documents\notes.txt",
                @"C:\Users\Test\Downloads\notes-copy.txt"
            ]);

        var inventory = new[]
        {
            CreateItem(@"C:\Users\Test\Documents\notes.txt"),
            CreateItem(@"C:\Users\Test\Downloads\notes-copy.txt")
        };

        var result = planner.BuildOperations([duplicate], inventory, 0.98d);

        Assert.Empty(result.Operations);
        Assert.Equal(0, result.ConsideredGroups);
    }

    private static DuplicateGroup CreateGroup(string canonicalPath, IReadOnlyList<string> duplicatePaths, double confidence = 0.995d)
    {
        return new DuplicateGroup
        {
            CanonicalPath = canonicalPath,
            Confidence = confidence,
            Paths = duplicatePaths.ToList()
        };
    }

    private static FileInventoryItem CreateItem(
        string path,
        SensitivityLevel sensitivity = SensitivityLevel.Low,
        bool isSyncManaged = false,
        bool isProtectedByUser = false)
    {
        return new FileInventoryItem
        {
            Path = path,
            Name = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            Category = "Documents",
            MimeType = "application/octet-stream",
            ContentFingerprint = "fingerprint",
            SizeBytes = 2048,
            LastModifiedUnixTimeSeconds = 100,
            Sensitivity = sensitivity,
            IsSyncManaged = isSyncManaged,
            IsProtectedByUser = isProtectedByUser,
            IsDuplicateCandidate = true
        };
    }
}
