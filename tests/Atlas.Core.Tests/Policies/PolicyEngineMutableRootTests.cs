using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.Core.Tests.Policies;

public sealed class PolicyEngineMutableRootTests
{
    private readonly AtlasPolicyEngine _policyEngine = new();
    private readonly PathSafetyClassifier _classifier = new();

    #region Operations Inside Mutable Roots (Allowed)

    [Fact]
    public void EvaluateOperation_MoveWithinMutableRoot_Allowed()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\OldFolder\file.txt",
            DestinationPath = @"C:\Users\Test\Documents\NewFolder\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.RiskEnvelope.BlockedReasons);
    }

    [Fact]
    public void EvaluateOperation_DeleteToQuarantineInsideMutableRoot_Allowed()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" },
            DuplicateAutoDeleteConfidenceThreshold = 0.98d
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = @"C:\Users\Test\Documents\duplicate.pdf",
            Confidence = 0.999d,
            MarksSafeDuplicate = true,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_RenameInsideMutableRoot_Allowed()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.RenamePath,
            SourcePath = @"C:\Users\Test\Documents\old-name.txt",
            DestinationPath = @"C:\Users\Test\Documents\new-name.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_CreateDirectoryInsideMutableRoot_Allowed()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.CreateDirectory,
            DestinationPath = @"C:\Users\Test\Documents\NewProject",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\Desktop")]
    [InlineData(@"C:\Users\Test\Documents")]
    [InlineData(@"C:\Users\Test\Pictures")]
    [InlineData(@"C:\Users\Test\Downloads")]
    public void EvaluateOperation_MultipleMutableRoots_AllAllowed(string mutableRoot)
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>
            {
                @"C:\Users\Test\Desktop",
                @"C:\Users\Test\Documents",
                @"C:\Users\Test\Pictures",
                @"C:\Users\Test\Downloads"
            }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = Path.Combine(mutableRoot, "source.txt"),
            DestinationPath = Path.Combine(mutableRoot, "dest.txt"),
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.True(result.IsAllowed);
    }

    #endregion

    #region Operations Outside Mutable Roots (Blocked)

    [Fact]
    public void EvaluateOperation_MoveFromOutsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\OtherLocation\file.txt",
            DestinationPath = @"C:\Users\Test\Documents\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("outside mutable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateOperation_MoveToOutsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            DestinationPath = @"C:\OtherLocation\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("outside mutable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateOperation_DeleteOutsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = @"C:\OtherLocation\file.txt",
            Confidence = 0.999d,
            MarksSafeDuplicate = true,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("outside mutable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateOperation_RenameOutsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.RenamePath,
            SourcePath = @"C:\SystemFiles\config.txt",
            DestinationPath = @"C:\SystemFiles\config-backup.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_CreateDirectoryOutsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.CreateDirectory,
            DestinationPath = @"C:\SystemFolder\NewDir",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    #endregion

    #region Empty Mutable Roots (All Blocked)

    [Fact]
    public void EvaluateOperation_EmptyMutableRoots_AllMovesBlocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>() // Empty list
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            DestinationPath = @"C:\Users\Test\Documents\Other\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_EmptyMutableRoots_AllDeletesBlocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>()
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = @"C:\Users\Test\Documents\file.txt",
            Confidence = 0.999d,
            MarksSafeDuplicate = true,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_EmptyMutableRoots_AllRenamesBlocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>()
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.RenamePath,
            SourcePath = @"C:\Users\Test\old.txt",
            DestinationPath = @"C:\Users\Test\new.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void IsMutablePath_EmptyMutableRoots_AlwaysReturnsFalse()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>()
        };

        var result = _classifier.IsMutablePath(profile, @"C:\AnyPath\file.txt");

        Assert.False(result);
    }

    #endregion

    #region Protected Path Inside Mutable Root (Still Blocked)

    [Fact]
    public void EvaluateOperation_ProtectedPathInsideMutableRoot_StillBlocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\" }, // Entire C: drive is mutable
            ProtectedPaths = new List<string> { @"C:\Windows" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Windows\System32\notepad.exe",
            DestinationPath = @"C:\Users\Test\notepad.exe",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateOperation_MoveToProtectedPathInsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\" },
            ProtectedPaths = new List<string> { @"C:\Program Files" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\malware.exe",
            DestinationPath = @"C:\Program Files\App\malware.exe",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateOperation_DeleteFromProtectedPathInsideMutableRoot_Blocked()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\" },
            ProtectedPaths = new List<string> { @"C:\Windows" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = @"C:\Windows\System32\drivers\etc\hosts",
            Confidence = 0.999d,
            MarksSafeDuplicate = true,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\config")]
    [InlineData(@"C:\Program Files\Microsoft Office")]
    [InlineData(@"C:\Program Files (x86)\Common Files")]
    [InlineData(@"C:\ProgramData\Microsoft")]
    public void EvaluateOperation_VariousProtectedPaths_AllBlocked(string protectedPath)
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\" },
            ProtectedPaths = new List<string>
            {
                @"C:\Windows",
                @"C:\Program Files",
                @"C:\Program Files (x86)",
                @"C:\ProgramData"
            }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = Path.Combine(protectedPath, "file.dll"),
            DestinationPath = @"C:\Users\Test\file.dll",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void EvaluateOperation_ProtectedSubfolderOverridesMutableParent()
    {
        // Scenario: Documents is mutable, but Documents\Protected is protected
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" },
            ProtectedPaths = new List<string> { @"C:\Users\Test\Documents\Protected" }
        };
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\Test\Documents\Protected\important.txt",
            DestinationPath = @"C:\Users\Test\Documents\Other\important.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void IsMutablePath_PathExactlyMatchesMutableRoot_ReturnsTrue()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };

        var result = _classifier.IsMutablePath(profile, @"C:\Users\Test\Documents");

        Assert.True(result);
    }

    [Fact]
    public void IsMutablePath_EmptyPath_ReturnsFalse()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };

        var result = _classifier.IsMutablePath(profile, string.Empty);

        Assert.False(result);
    }

    [Fact]
    public void IsMutablePath_WhitespacePath_ReturnsFalse()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test\Documents" }
        };

        var result = _classifier.IsMutablePath(profile, "   ");

        Assert.False(result);
    }

    [Fact]
    public void EvaluateOperation_NonMutableScopedOperation_NotBlockedByMutableCheck()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string>() // Empty
        };

        // MergeDuplicateGroup doesn't require mutable scope
        var operation = new PlanOperation
        {
            Kind = OperationKind.MergeDuplicateGroup,
            SourcePath = @"C:\Anywhere\file.txt",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = _policyEngine.EvaluateOperation(profile, operation);

        // Should not be blocked for mutable root reasons
        Assert.DoesNotContain(result.RiskEnvelope.BlockedReasons,
            reason => reason.Contains("mutable", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
