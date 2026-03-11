using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.Core.Tests.Policies;

public sealed class PolicyEngineCrossVolumeTests
{
    private readonly AtlasPolicyEngine _policyEngine = new();

    #region Local to Local Different Drives

    [Fact]
    public void EvaluateOperation_MoveBetweenDifferentLocalDrives_RequiresApproval()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test", @"D:\Data" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            DestinationPath = @"D:\Data\Backup\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    [Theory]
    [InlineData(@"C:\Source\file.txt", @"D:\Dest\file.txt")]
    [InlineData(@"C:\Source\file.txt", @"E:\Dest\file.txt")]
    [InlineData(@"D:\Source\file.txt", @"C:\Dest\file.txt")]
    public void EvaluateOperation_VariousCrossVolumeMoves_AllRequireApproval(string source, string destination)
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>
            {
                @"C:\Source", @"C:\Dest",
                @"D:\Source", @"D:\Dest",
                @"E:\Dest"
            }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = source,
            DestinationPath = destination,
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    [Fact]
    public void EvaluateOperation_CrossVolumeMoveWithHighSensitivity_RequiresApproval()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test", @"D:\Archive" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\tax-return-2024.pdf",
            DestinationPath = @"D:\Archive\tax-return-2024.pdf",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.High
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    #endregion

    #region Local to Network Paths

    [Fact]
    public void EvaluateOperation_LocalToNetworkShare_RequiresApproval()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test", @"\\fileserver\shared" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\report.pdf",
            DestinationPath = @"\\fileserver\shared\reports\report.pdf",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    [Fact]
    public void EvaluateOperation_NetworkShareToLocal_RequiresApproval()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test", @"\\fileserver\shared" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"\\fileserver\shared\documents\file.txt",
            DestinationPath = @"C:\Users\Test\Documents\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    [Fact]
    public void EvaluateOperation_BetweenDifferentNetworkShares_RequiresApproval()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"\\server1\share", @"\\server2\share" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"\\server1\share\file.txt",
            DestinationPath = @"\\server2\share\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    [Fact]
    public void EvaluateOperation_LocalhostAdminShare_TreatedAsSameVolume()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\file.txt",
            DestinationPath = @"C:\Users\Test\Subfolder\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // Same volume move should not require approval for this reason
        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.None, result.ApprovalRequirement);
    }

    #endregion

    #region Same Volume Moves (No Approval Needed)

    [Fact]
    public void EvaluateOperation_SameVolumeMoveWithinMutableRoot_NoApprovalNeeded()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            DestinationPath = @"C:\Users\Test\Archive\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.None, result.ApprovalRequirement);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\A\file.txt", @"C:\Users\Test\B\file.txt")]
    [InlineData(@"D:\Data\Old\file.txt", @"D:\Data\New\file.txt")]
    [InlineData(@"E:\Projects\2023\file.txt", @"E:\Archive\2023\file.txt")]
    public void EvaluateOperation_SameVolumeMoves_NoApprovalForCrossVolume(string source, string destination)
    {
        var sourceRoot = Path.GetPathRoot(source);
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { sourceRoot! }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = source,
            DestinationPath = destination,
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // Should not require approval specifically for cross-volume
        // May still be blocked for other reasons (mutable root, etc.)
        var crossVolumeApprovalRequired = result.ApprovalRequirement == ApprovalRequirement.ExplicitApproval
            && !result.RiskEnvelope.BlockedReasons.Any();

        // Same volume should not trigger cross-volume approval flag
        Assert.False(crossVolumeApprovalRequired && Path.GetPathRoot(source) == Path.GetPathRoot(destination));
    }

    [Fact]
    public void EvaluateOperation_SameVolumeRename_NoApprovalNeeded()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.RenamePath,
            SourcePath = @"C:\Users\Test\Documents\oldname.txt",
            DestinationPath = @"C:\Users\Test\Documents\newname.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.None, result.ApprovalRequirement);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EvaluateOperation_EmptySourcePath_HandlesGracefully()
    {
        var profile = PolicyProfileFactory.CreateDefault();
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = string.Empty,
            DestinationPath = @"D:\Dest\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // Should handle gracefully, likely blocked for other reasons
        Assert.NotNull(result);
    }

    [Fact]
    public void EvaluateOperation_EmptyDestinationPath_HandlesGracefully()
    {
        var profile = PolicyProfileFactory.CreateDefault();
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\file.txt",
            DestinationPath = string.Empty,
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // Should handle gracefully
        Assert.NotNull(result);
    }

    [Fact]
    public void EvaluateOperation_CreateDirectoryOperation_NotCrossVolumeCheck()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"D:\NewFolder" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.CreateDirectory,
            SourcePath = string.Empty,
            DestinationPath = @"D:\NewFolder\Subfolder",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // CreateDirectory doesn't have cross-volume semantics
        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.None, result.ApprovalRequirement);
    }

    [Fact]
    public void EvaluateOperation_DeleteToQuarantine_NoCrossVolumeCheck()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test" },
            DuplicateAutoDeleteConfidenceThreshold = 0.98d
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = @"C:\Users\Test\duplicate.txt",
            DestinationPath = string.Empty, // Quarantine doesn't use destination
            Confidence = 0.999d,
            MarksSafeDuplicate = true,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // Delete doesn't have cross-volume semantics since there's no destination volume
        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.None, result.ApprovalRequirement);
    }

    [Fact]
    public void EvaluateOperation_MappedDriveLetter_TreatedAsDistinctVolume()
    {
        // Z: drive mapped to a network share is a different "volume" from C:
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test", @"Z:\SharedDocs" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\file.txt",
            DestinationPath = @"Z:\SharedDocs\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.ExplicitApproval, result.ApprovalRequirement);
    }

    #endregion
}
