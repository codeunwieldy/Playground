using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.Core.Tests;

public sealed class PolicyEngineTests
{
    private readonly PolicyProfile policyProfile = PolicyProfileFactory.CreateDefault();
    private readonly AtlasPolicyEngine policyEngine = new();

    [Fact]
    public void BlocksProtectedSystemPaths()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Windows\notepad.exe",
            DestinationPath = @"C:\Users\jscel\Documents\notepad.exe",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = policyEngine.EvaluateOperation(policyProfile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons, reason => reason.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RequiresApprovalForSyncManagedOperations()
    {
        var operation = new PlanOperation
        {
            Kind = OperationKind.MovePath,
            SourcePath = @"C:\Users\jscel\OneDrive\Notes\todo.md",
            DestinationPath = @"C:\Users\jscel\Documents\Atlas Organized\todo.md",
            Confidence = 0.99d,
            Sensitivity = SensitivityLevel.Low
        };

        var result = policyEngine.EvaluateOperation(policyProfile, operation);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.RiskEnvelope.BlockedReasons, reason => reason.Contains("sync-managed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AllowsSafeDuplicateQuarantineInsideMutableRoots()
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "draft-copy.pdf");
        var operation = new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = source,
            Confidence = 0.999d,
            MarksSafeDuplicate = true,
            Sensitivity = SensitivityLevel.Low
        };

        var result = policyEngine.EvaluateOperation(policyProfile, operation);

        Assert.True(result.IsAllowed);
        Assert.Equal(ApprovalRequirement.None, result.ApprovalRequirement);
    }
}