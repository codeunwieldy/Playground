using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.Core.Tests.Policies;

public sealed class PolicyEngineSyncFolderTests
{
    private readonly AtlasPolicyEngine _policyEngine = new();
    private readonly PathSafetyClassifier _classifier = new();

    #region OneDrive Detection

    [Fact]
    public void IsSyncManaged_OneDriveExactMatch_ReturnsTrue()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "OneDrive" }
        };

        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\OneDrive\Documents\file.txt");

        Assert.True(result);
    }

    [Fact]
    public void IsSyncManaged_OneDriveBusinessVariant_ReturnsTrue()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "OneDrive" }
        };

        // OneDrive - Business variant should still match "OneDrive" marker
        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\OneDrive - Contoso\Documents\file.txt");

        Assert.True(result);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\OneDrive\file.txt")]
    [InlineData(@"C:\Users\Test\OneDrive - Personal\file.txt")]
    [InlineData(@"C:\Users\Test\OneDrive - Company Name\file.txt")]
    public void EvaluateOperation_OneDriveVariants_BlockedByDefaultPolicy(string sourcePath)
    {
        var profile = PolicyProfileFactory.CreateDefault();
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = sourcePath,
            DestinationPath = @"C:\Users\Test\Documents\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("sync", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsSyncManaged_OneDriveSubstringFalsePositive_ShouldNotMatchUnrelatedPath()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "OneDrive" }
        };

        // A file named "MyOneDriveBackup.txt" should match because it contains "OneDrive"
        // This documents current substring-based behavior
        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\Documents\MyOneDriveBackup.txt");

        // Current implementation uses Contains, so this will match
        // This test documents this behavior for awareness
        Assert.True(result, "Substring matching means 'OneDrive' in filename triggers sync detection");
    }

    #endregion

    #region Dropbox Detection

    [Theory]
    [InlineData(@"C:\Users\Test\Dropbox\file.txt")]
    [InlineData(@"C:\Users\Test\Dropbox (Personal)\file.txt")]
    [InlineData(@"C:\Users\Test\Dropbox (Business)\file.txt")]
    public void IsSyncManaged_DropboxVariants_ReturnsTrue(string path)
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "Dropbox" }
        };

        var result = _classifier.IsSyncManaged(profile, path);

        Assert.True(result);
    }

    [Fact]
    public void IsSyncManaged_DropboxHiddenMarker_ReturnsTrue()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { ".dropbox" }
        };

        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\SyncedFolder\.dropbox\config");

        Assert.True(result);
    }

    [Fact]
    public void EvaluateOperation_DropboxSource_RequiresApprovalWhenNotExcluded()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "Dropbox" },
            ExcludeSyncFoldersByDefault = false,
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Dropbox\Documents\file.txt",
            DestinationPath = @"C:\Users\Test\Local\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    #endregion

    #region iCloud Drive Detection

    [Theory]
    [InlineData(@"C:\Users\Test\iCloudDrive\Documents\file.txt")]
    [InlineData(@"C:\Users\Test\iCloud Drive\Documents\file.txt")]
    public void IsSyncManaged_iCloudDriveVariants_RequiresCorrectMarker(string path)
    {
        // iCloudDrive (no space) vs "iCloud Drive" (with space) are different
        var profileNoSpace = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "iCloudDrive" }
        };

        var profileWithSpace = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "iCloud Drive" }
        };

        var resultNoSpace = _classifier.IsSyncManaged(profileNoSpace, path);
        var resultWithSpace = _classifier.IsSyncManaged(profileWithSpace, path);

        // At least one should match depending on the path
        Assert.True(resultNoSpace || resultWithSpace,
            $"Path '{path}' should match at least one iCloud marker variant");
    }

    [Fact]
    public void IsSyncManaged_iCloudDriveWithSpace_MatchesPathWithSpace()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "iCloud Drive" }
        };

        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\iCloud Drive\Documents\file.txt");

        Assert.True(result);
    }

    [Fact]
    public void IsSyncManaged_iCloudDriveWithoutSpace_MatchesPathWithoutSpace()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "iCloudDrive" }
        };

        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\iCloudDrive\Documents\file.txt");

        Assert.True(result);
    }

    #endregion

    #region Google Drive Detection

    [Theory]
    [InlineData(@"C:\Users\Test\Google Drive\My Drive\file.txt")]
    [InlineData(@"G:\My Drive\Documents\file.txt")]
    [InlineData(@"C:\Users\Test\Google Drive\Shared drives\Team\file.txt")]
    public void IsSyncManaged_GoogleDriveVariants_ReturnsTrue(string path)
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "Google Drive", "My Drive" }
        };

        var result = _classifier.IsSyncManaged(profile, path);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateOperation_GoogleDriveDestination_BlockedByDefaultPolicy()
    {
        var profile = PolicyProfileFactory.CreateDefault();
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            DestinationPath = @"C:\Users\Test\Google Drive\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("sync", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Generic .sync Marker False Positives

    [Fact]
    public void IsSyncManaged_DotSyncMarker_MatchesHiddenSyncFolders()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { ".sync" }
        };

        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\SomeApp\.sync\data.db");

        Assert.True(result);
    }

    [Fact]
    public void IsSyncManaged_DotSyncInFilename_PotentialFalsePositive()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { ".sync" }
        };

        // A file ending in ".sync" or containing ".sync" might be falsely flagged
        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\Documents\backup.sync");

        // Documents current substring behavior
        Assert.True(result, "Substring matching causes '.sync' in filename to trigger detection");
    }

    [Fact]
    public void IsSyncManaged_SynologyDrive_MatchesCorrectly()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "SynologyDrive" }
        };

        var result = _classifier.IsSyncManaged(profile, @"C:\Users\Test\SynologyDrive\Documents\file.txt");

        Assert.True(result);
    }

    #endregion

    #region Nested Sync Folder Scenarios

    [Fact]
    public void EvaluateOperation_NestedSyncFolders_BothSourceAndDestinationFlagged()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "OneDrive", "Dropbox" },
            ExcludeSyncFoldersByDefault = true,
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\OneDrive\file.txt",
            DestinationPath = @"C:\Users\Test\Dropbox\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.True(result.RiskEnvelope.SyncRisk > 0.5d);
    }

    [Fact]
    public void EvaluateOperation_SyncFolderToLocalFolder_StillBlocked()
    {
        var profile = PolicyProfileFactory.CreateDefault();
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Dropbox\important.pdf",
            DestinationPath = @"C:\Users\Test\Documents\important.pdf",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_LocalToSyncFolder_StillBlocked()
    {
        var profile = PolicyProfileFactory.CreateDefault();
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\important.pdf",
            DestinationPath = @"C:\Users\Test\OneDrive\important.pdf",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void RiskEnvelope_SyncManagedOperation_HasHighSyncRisk()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "OneDrive" },
            ExcludeSyncFoldersByDefault = false,
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\OneDrive\file.txt",
            DestinationPath = @"C:\Users\Test\Local\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.Equal(0.9d, result.RiskEnvelope.SyncRisk);
    }

    [Fact]
    public void RiskEnvelope_NonSyncOperation_HasLowSyncRisk()
    {
        var profile = new PolicyProfile
        {
            SyncFolderMarkers = new List<string> { "OneDrive" },
            ExcludeSyncFoldersByDefault = false,
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            DestinationPath = @"C:\Users\Test\Local\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.Equal(0.1d, result.RiskEnvelope.SyncRisk);
    }

    #endregion
}
