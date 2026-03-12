using Atlas.AI;
using Atlas.Core.Contracts;

namespace Atlas.AI.Tests;

/// <summary>
/// Tests for PlanSemanticValidator - verifies that the post-parse semantic validation
/// layer correctly accepts valid plans and rejects invalid ones.
/// </summary>
public class PlanSemanticValidatorTests
{
    private readonly PlanSemanticValidator _validator = new();

    #region Helpers

    private static PlanResponse BuildValidPlan(Action<PlanGraph>? configure = null)
    {
        var plan = new PlanGraph
        {
            Scope = "Organize documents",
            Rationale = "Sort files by category for easier navigation.",
            RollbackStrategy = "Inverse operations plus quarantine restores.",
            RequiresReview = false,
            RiskSummary = new RiskEnvelope
            {
                SensitivityScore = 0.2,
                SystemScore = 0.2,
                SyncRisk = 0.1,
                ReversibilityScore = 0.9,
                Confidence = 0.85,
                ApprovalRequirement = ApprovalRequirement.None
            },
            Operations =
            {
                new PlanOperation
                {
                    Kind = OperationKind.CreateDirectory,
                    DestinationPath = @"C:\Users\Test\Documents\Organized",
                    Description = "Create organized folder.",
                    Confidence = 0.95,
                    Sensitivity = SensitivityLevel.Low
                }
            }
        };

        configure?.Invoke(plan);

        return new PlanResponse { Plan = plan, Summary = "Test plan." };
    }

    #endregion

    // ---- Valid plan acceptance ----

    [Fact]
    public void ValidPlan_IsAccepted()
    {
        var response = BuildValidPlan();
        var report = _validator.Validate(response);

        Assert.True(report.IsValid);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void ValidPlan_WithMoveOperation_IsAccepted()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\Downloads\file.txt",
                DestinationPath = @"C:\Users\Test\Documents\file.txt",
                Description = "Move file to documents.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void ValidPlan_WithDuplicateQuarantine_IsAccepted()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = true;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.Review;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Users\Test\Downloads\dup.txt",
                Description = "Quarantine duplicate.",
                Confidence = 0.99,
                Sensitivity = SensitivityLevel.Low,
                MarksSafeDuplicate = true,
                GroupId = "abc123"
            });
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    // ---- Path requirement violations ----

    [Fact]
    public void MovePath_MissingSourcePath_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = "",
                DestinationPath = @"C:\Users\Test\dest.txt",
                Description = "Move file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("SourcePath is required"));
    }

    [Fact]
    public void MovePath_MissingDestinationPath_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\src.txt",
                DestinationPath = "",
                Description = "Move file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("DestinationPath is required"));
    }

    [Fact]
    public void RenamePath_MissingBothPaths_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.RenamePath,
                Description = "Rename file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.True(report.Violations.Count >= 2);
    }

    [Fact]
    public void DeleteToQuarantine_MissingSource_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = "",
                Description = "Quarantine file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("SourcePath is required"));
    }

    [Fact]
    public void CreateDirectory_MissingDestination_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.CreateDirectory,
                DestinationPath = "",
                Description = "Create folder.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("DestinationPath is required"));
    }

    [Fact]
    public void RestoreFromQuarantine_MissingPaths_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.RestoreFromQuarantine,
                Description = "Restore file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.True(report.Violations.Count >= 2);
    }

    [Fact]
    public void MergeDuplicateGroup_MissingGroupId_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MergeDuplicateGroup,
                GroupId = "",
                Description = "Merge duplicates.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("GroupId is required"));
    }

    // ---- Protected path violations ----

    [Theory]
    [InlineData(@"C:\Windows\System32\config.sys")]
    [InlineData(@"C:\Program Files\SomeApp\file.dll")]
    [InlineData(@"C:\Program Files (x86)\App\file.exe")]
    [InlineData(@"C:\ProgramData\Microsoft\file.dat")]
    [InlineData(@"C:\Users\Test\AppData\Local\file.dat")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\item")]
    [InlineData(@"C:\Users\Test\project\.git\HEAD")]
    [InlineData(@"C:\Users\Test\.ssh\id_rsa")]
    public void ProtectedPath_SourcePath_Fails(string protectedPath)
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = protectedPath,
                Description = "Quarantine protected file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("protected path"));
    }

    [Fact]
    public void ProtectedPath_DestinationPath_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\file.txt",
                DestinationPath = @"C:\Windows\file.txt",
                Description = "Move to Windows.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("protected path"));
    }

    [Fact]
    public void SafePath_NotFlaggedAsProtected()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\Documents\file.txt",
                DestinationPath = @"C:\Users\Test\Documents\Organized\file.txt",
                Description = "Move to organized.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    // ---- Confidence/score range violations ----

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2.0)]
    public void OutOfRangeConfidence_Fails(double confidence)
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.CreateDirectory,
                DestinationPath = @"C:\Users\Test\NewFolder",
                Description = "Create folder.",
                Confidence = confidence,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("outside [0.0, 1.0]"));
    }

    [Fact]
    public void OutOfRangeRiskScore_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RiskSummary.SensitivityScore = 1.5;
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("SensitivityScore") && v.Contains("outside [0.0, 1.0]"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ValidConfidence_Accepted(double confidence)
    {
        var response = BuildValidPlan(plan =>
        {
            plan.Operations[0].Confidence = confidence;
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    // ---- Review escalation violations ----

    [Fact]
    public void HighSensitivity_RequiresReviewFalse_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = false;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.None;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\taxes.pdf",
                DestinationPath = @"C:\Users\Test\Archive\taxes.pdf",
                Description = "Archive tax document.",
                Confidence = 0.8,
                Sensitivity = SensitivityLevel.High
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("RequiresReview is false"));
        Assert.Contains(report.Violations, v => v.Contains("ApprovalRequirement is None"));
    }

    [Fact]
    public void CriticalSensitivity_WithReview_Accepted()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = true;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.ExplicitApproval;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\credentials.json",
                DestinationPath = @"C:\Users\Test\Archive\credentials.json",
                Description = "Archive credentials.",
                Confidence = 0.7,
                Sensitivity = SensitivityLevel.Critical
            });
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    // ---- Duplicate-delete rules ----

    [Fact]
    public void SafeDuplicate_MissingGroupId_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = true;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.Review;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Users\Test\dup.txt",
                Description = "Quarantine duplicate.",
                Confidence = 0.99,
                Sensitivity = SensitivityLevel.Low,
                MarksSafeDuplicate = true,
                GroupId = "" // Missing!
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("MarksSafeDuplicate") && v.Contains("GroupId is empty"));
    }

    [Fact]
    public void SafeDuplicate_WithGroupId_Accepted()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = true;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.Review;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Users\Test\dup.txt",
                Description = "Quarantine duplicate.",
                Confidence = 0.99,
                Sensitivity = SensitivityLevel.Low,
                MarksSafeDuplicate = true,
                GroupId = "group-abc123"
            });
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void SafeDuplicate_HighSensitivity_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = true;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.Review;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Users\Test\Finance\tax-copy.pdf",
                Description = "Quarantine duplicate.",
                Confidence = 0.99,
                Sensitivity = SensitivityLevel.High,
                MarksSafeDuplicate = true,
                GroupId = "group-abc123"
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("requires Low sensitivity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeleteToQuarantine_WithoutReview_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RequiresReview = false;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.None;
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Users\Test\Downloads\dup.txt",
                Description = "Quarantine duplicate.",
                Confidence = 0.99,
                Sensitivity = SensitivityLevel.Low,
                MarksSafeDuplicate = true,
                GroupId = "group-abc123"
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("DeleteToQuarantine operations but RequiresReview is false", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Violations, v => v.Contains("DeleteToQuarantine operations but ApprovalRequirement is None", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Rollback requirement ----

    [Fact]
    public void DeleteOperations_WithoutRollbackStrategy_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RollbackStrategy = ""; // Missing!
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Users\Test\file.txt",
                Description = "Quarantine file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("RollbackStrategy is empty"));
    }

    [Fact]
    public void MoveOperations_WithoutRollbackStrategy_Fails()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RollbackStrategy = "";
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.MovePath,
                SourcePath = @"C:\Users\Test\a.txt",
                DestinationPath = @"C:\Users\Test\b.txt",
                Description = "Move file.",
                Confidence = 0.9,
                Sensitivity = SensitivityLevel.Low
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("RollbackStrategy is empty"));
    }

    [Fact]
    public void CreateDirOnly_WithoutRollbackStrategy_IsAccepted()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RollbackStrategy = "";
            // Default operation is CreateDirectory only - no destructive ops
        });

        var report = _validator.Validate(response);
        Assert.True(report.IsValid);
    }

    // ---- Null plan ----

    [Fact]
    public void NullPlan_Fails()
    {
        var response = new PlanResponse { Plan = null!, Summary = "Bad plan." };
        var report = _validator.Validate(response);

        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.Contains("Plan is null"));
    }

    // ---- Multiple violations ----

    [Fact]
    public void MultipleViolations_AllReported()
    {
        var response = BuildValidPlan(plan =>
        {
            plan.RollbackStrategy = "";
            plan.RequiresReview = false;
            plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.None;
            plan.RiskSummary.SensitivityScore = 2.0; // out of range
            plan.Operations.Clear();
            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.DeleteToQuarantine,
                SourcePath = @"C:\Windows\System32\important.dll",
                Description = "Bad delete.",
                Confidence = -0.5, // out of range
                Sensitivity = SensitivityLevel.Critical,
                MarksSafeDuplicate = true,
                GroupId = "" // missing
            });
        });

        var report = _validator.Validate(response);
        Assert.False(report.IsValid);
        // Should have multiple violations: protected path, confidence range, risk score range,
        // review escalation, missing GroupId, rollback strategy
        Assert.True(report.Violations.Count >= 5, $"Expected >=5 violations but got {report.Violations.Count}: {string.Join(", ", report.Violations)}");
    }
}
