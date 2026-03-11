using Atlas.Core.Contracts;

namespace Atlas.Core.Policies;

public sealed class AtlasPolicyEngine
{
    private readonly PathSafetyClassifier _pathSafetyClassifier = new();

    public PolicyDecision EvaluateOperation(PolicyProfile profile, PlanOperation operation)
    {
        var reasons = new List<string>();
        var source = _pathSafetyClassifier.Normalize(operation.SourcePath);
        var destination = _pathSafetyClassifier.Normalize(operation.DestinationPath);
        var approval = ApprovalRequirement.None;

        if (!string.IsNullOrWhiteSpace(source) && _pathSafetyClassifier.IsProtectedPath(profile, source))
        {
            reasons.Add($"Source path is protected: {source}");
        }

        if (!string.IsNullOrWhiteSpace(destination) && _pathSafetyClassifier.IsProtectedPath(profile, destination))
        {
            reasons.Add($"Destination path is protected: {destination}");
        }

        if (RequiresMutableScope(operation.Kind))
        {
            if (!string.IsNullOrWhiteSpace(source) && !_pathSafetyClassifier.IsMutablePath(profile, source))
            {
                reasons.Add($"Source path is outside mutable user content roots: {source}");
            }

            if (!string.IsNullOrWhiteSpace(destination) && !_pathSafetyClassifier.IsMutablePath(profile, destination))
            {
                reasons.Add($"Destination path is outside mutable user content roots: {destination}");
            }
        }

        var touchesSyncFolder = (!string.IsNullOrWhiteSpace(source) && _pathSafetyClassifier.IsSyncManaged(profile, source))
            || (!string.IsNullOrWhiteSpace(destination) && _pathSafetyClassifier.IsSyncManaged(profile, destination));

        if (touchesSyncFolder && profile.ExcludeSyncFoldersByDefault)
        {
            reasons.Add("Operation touches a sync-managed folder that is excluded by policy.");
        }
        else if (touchesSyncFolder)
        {
            approval = ApprovalRequirement.ExplicitApproval;
        }

        if (operation.Sensitivity is SensitivityLevel.High or SensitivityLevel.Critical)
        {
            approval = ApprovalRequirement.ExplicitApproval;
        }

        if (operation.Kind == OperationKind.DeleteToQuarantine && !operation.MarksSafeDuplicate)
        {
            approval = ApprovalRequirement.ExplicitApproval;
        }

        if (operation.Kind == OperationKind.DeleteToQuarantine && operation.MarksSafeDuplicate)
        {
            if (operation.Confidence < profile.DuplicateAutoDeleteConfidenceThreshold)
            {
                approval = ApprovalRequirement.ExplicitApproval;
            }

            if (operation.Sensitivity != SensitivityLevel.Low)
            {
                approval = ApprovalRequirement.ExplicitApproval;
            }
        }

        if (operation.Kind == OperationKind.MovePath && IsCrossVolumeMove(source, destination))
        {
            approval = ApprovalRequirement.ExplicitApproval;
        }

        if (operation.Kind == OperationKind.RenamePath && IsRootFolderRename(source, destination))
        {
            approval = ApprovalRequirement.ExplicitApproval;
        }

        var risk = BuildRiskEnvelope(operation, reasons, approval, touchesSyncFolder);
        return new PolicyDecision(reasons.Count == 0, approval, risk);
    }

    public PlanValidationResult ValidatePlan(PolicyProfile profile, PlanGraph plan)
    {
        var operationDecisions = plan.Operations
            .Select(operation => new OperationDecision(operation, EvaluateOperation(profile, operation)))
            .ToList();

        return new PlanValidationResult
        {
            IsAllowed = operationDecisions.All(static result => result.Decision.IsAllowed),
            RequiresReview = operationDecisions.Any(static result => result.Decision.ApprovalRequirement != ApprovalRequirement.None),
            Decisions = operationDecisions
        };
    }

    private static bool RequiresMutableScope(OperationKind kind) =>
        kind is OperationKind.CreateDirectory or OperationKind.MovePath or OperationKind.RenamePath or OperationKind.DeleteToQuarantine or OperationKind.RestoreFromQuarantine;

    private static bool IsCrossVolumeMove(string source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        return !string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRootFolderRename(string source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        var sourceParent = Path.GetDirectoryName(source);
        var destinationParent = Path.GetDirectoryName(destination);
        return string.IsNullOrWhiteSpace(sourceParent)
            || string.IsNullOrWhiteSpace(destinationParent)
            || string.Equals(sourceParent, Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase)
            || string.Equals(destinationParent, Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
    }

    private static RiskEnvelope BuildRiskEnvelope(PlanOperation operation, List<string> reasons, ApprovalRequirement approval, bool touchesSyncFolder)
    {
        var sensitivityScore = operation.Sensitivity switch
        {
            SensitivityLevel.Low => 0.2d,
            SensitivityLevel.Medium => 0.45d,
            SensitivityLevel.High => 0.8d,
            SensitivityLevel.Critical => 1.0d,
            _ => 0.3d
        };

        var systemScore = operation.Kind switch
        {
            OperationKind.DeleteToQuarantine => 0.6d,
            OperationKind.MovePath => 0.45d,
            OperationKind.RenamePath => 0.35d,
            OperationKind.ApplyOptimizationFix => 0.5d,
            _ => 0.2d
        };

        return new RiskEnvelope
        {
            SensitivityScore = sensitivityScore,
            SystemScore = systemScore,
            SyncRisk = touchesSyncFolder ? 0.9d : 0.1d,
            ReversibilityScore = operation.Kind is OperationKind.DeleteToQuarantine or OperationKind.MovePath or OperationKind.RenamePath or OperationKind.CreateDirectory ? 0.9d : 0.5d,
            Confidence = operation.Confidence,
            ApprovalRequirement = approval,
            BlockedReasons = reasons
        };
    }
}

public sealed record PolicyDecision(bool IsAllowed, ApprovalRequirement ApprovalRequirement, RiskEnvelope RiskEnvelope);

public sealed record OperationDecision(PlanOperation Operation, PolicyDecision Decision);

public sealed class PlanValidationResult
{
    public bool IsAllowed { get; init; }
    public bool RequiresReview { get; init; }
    public List<OperationDecision> Decisions { get; init; } = new();
}