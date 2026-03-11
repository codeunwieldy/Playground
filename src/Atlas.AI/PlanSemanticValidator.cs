using Atlas.Core.Contracts;

namespace Atlas.AI;

/// <summary>
/// Report returned by <see cref="PlanSemanticValidator"/> containing validation results.
/// </summary>
public sealed class PlanValidationReport
{
    public bool IsValid { get; init; }
    public List<string> Violations { get; init; } = new();
}

/// <summary>
/// Post-parse semantic validator for AI-generated plan responses.
/// Enforces domain invariants that the JSON schema alone cannot express.
/// </summary>
public sealed class PlanSemanticValidator
{
    private static readonly HashSet<string> ProtectedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "AppData",
        "$Recycle.Bin",
        ".git",
        ".ssh"
    };

    /// <summary>
    /// Validates a <see cref="PlanResponse"/> against domain-level semantic rules.
    /// </summary>
    public PlanValidationReport Validate(PlanResponse response)
    {
        var violations = new List<string>();

        if (response.Plan is null)
        {
            violations.Add("Plan is null.");
            return new PlanValidationReport { IsValid = false, Violations = violations };
        }

        var plan = response.Plan;

        ValidatePathRequirements(plan.Operations, violations);
        ValidateProtectedPaths(plan.Operations, violations);
        ValidateConfidenceAndRiskScores(plan, violations);
        ValidateReviewEscalation(plan, violations);
        ValidateDuplicateDeleteRules(plan.Operations, violations);
        ValidateRollbackRequirement(plan, violations);

        return new PlanValidationReport
        {
            IsValid = violations.Count == 0,
            Violations = violations
        };
    }

    /// <summary>
    /// Rule 1: Validates that operations have the required path fields based on their kind.
    /// </summary>
    private static void ValidatePathRequirements(List<PlanOperation> operations, List<string> violations)
    {
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            var prefix = $"Operation[{i}] ({op.Kind})";

            switch (op.Kind)
            {
                case OperationKind.MovePath:
                case OperationKind.RenamePath:
                    if (string.IsNullOrWhiteSpace(op.SourcePath))
                        violations.Add($"{prefix}: SourcePath is required.");
                    if (string.IsNullOrWhiteSpace(op.DestinationPath))
                        violations.Add($"{prefix}: DestinationPath is required.");
                    break;

                case OperationKind.DeleteToQuarantine:
                    if (string.IsNullOrWhiteSpace(op.SourcePath))
                        violations.Add($"{prefix}: SourcePath is required.");
                    break;

                case OperationKind.RestoreFromQuarantine:
                    if (string.IsNullOrWhiteSpace(op.SourcePath))
                        violations.Add($"{prefix}: SourcePath is required.");
                    if (string.IsNullOrWhiteSpace(op.DestinationPath))
                        violations.Add($"{prefix}: DestinationPath is required.");
                    break;

                case OperationKind.CreateDirectory:
                    if (string.IsNullOrWhiteSpace(op.DestinationPath))
                        violations.Add($"{prefix}: DestinationPath is required.");
                    break;

                case OperationKind.MergeDuplicateGroup:
                    if (string.IsNullOrWhiteSpace(op.GroupId))
                        violations.Add($"{prefix}: GroupId is required.");
                    break;
            }
        }
    }

    /// <summary>
    /// Rule 2: Rejects operations targeting protected system paths.
    /// </summary>
    private static void ValidateProtectedPaths(List<PlanOperation> operations, List<string> violations)
    {
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            var prefix = $"Operation[{i}] ({op.Kind})";

            if (IsProtectedPath(op.SourcePath))
                violations.Add($"{prefix}: SourcePath targets protected path '{op.SourcePath}'.");

            if (IsProtectedPath(op.DestinationPath))
                violations.Add($"{prefix}: DestinationPath targets protected path '{op.DestinationPath}'.");
        }
    }

    /// <summary>
    /// Rule 3: Validates that all confidence and risk scores are within [0.0, 1.0].
    /// </summary>
    private static void ValidateConfidenceAndRiskScores(PlanGraph plan, List<string> violations)
    {
        for (var i = 0; i < plan.Operations.Count; i++)
        {
            var op = plan.Operations[i];
            if (op.Confidence < 0.0 || op.Confidence > 1.0)
                violations.Add($"Operation[{i}] ({op.Kind}): Confidence {op.Confidence} is outside [0.0, 1.0].");
        }

        if (plan.RiskSummary is not null)
        {
            var risk = plan.RiskSummary;

            if (risk.SensitivityScore < 0.0 || risk.SensitivityScore > 1.0)
                violations.Add($"RiskSummary.SensitivityScore {risk.SensitivityScore} is outside [0.0, 1.0].");
            if (risk.SystemScore < 0.0 || risk.SystemScore > 1.0)
                violations.Add($"RiskSummary.SystemScore {risk.SystemScore} is outside [0.0, 1.0].");
            if (risk.SyncRisk < 0.0 || risk.SyncRisk > 1.0)
                violations.Add($"RiskSummary.SyncRisk {risk.SyncRisk} is outside [0.0, 1.0].");
            if (risk.ReversibilityScore < 0.0 || risk.ReversibilityScore > 1.0)
                violations.Add($"RiskSummary.ReversibilityScore {risk.ReversibilityScore} is outside [0.0, 1.0].");
            if (risk.Confidence < 0.0 || risk.Confidence > 1.0)
                violations.Add($"RiskSummary.Confidence {risk.Confidence} is outside [0.0, 1.0].");
        }
    }

    /// <summary>
    /// Rule 4: Ensures high/critical sensitivity operations trigger review escalation.
    /// </summary>
    private static void ValidateReviewEscalation(PlanGraph plan, List<string> violations)
    {
        var hasHighOrCritical = plan.Operations.Any(static op =>
            op.Sensitivity is SensitivityLevel.High or SensitivityLevel.Critical);

        if (!hasHighOrCritical)
            return;

        if (!plan.RequiresReview)
            violations.Add("Plan contains High or Critical sensitivity operations but RequiresReview is false.");

        if (plan.RiskSummary is not null && plan.RiskSummary.ApprovalRequirement == ApprovalRequirement.None)
            violations.Add("Plan contains High or Critical sensitivity operations but ApprovalRequirement is None.");
    }

    /// <summary>
    /// Rule 5: DeleteToQuarantine operations marked as safe duplicates must have a GroupId.
    /// </summary>
    private static void ValidateDuplicateDeleteRules(List<PlanOperation> operations, List<string> violations)
    {
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op.Kind == OperationKind.DeleteToQuarantine
                && op.MarksSafeDuplicate
                && string.IsNullOrWhiteSpace(op.GroupId))
            {
                violations.Add(
                    $"Operation[{i}] ({op.Kind}): MarksSafeDuplicate is true but GroupId is empty.");
            }
        }
    }

    /// <summary>
    /// Rule 6: Plans containing destructive or move operations must specify a rollback strategy.
    /// </summary>
    private static void ValidateRollbackRequirement(PlanGraph plan, List<string> violations)
    {
        var hasDestructiveOrMove = plan.Operations.Any(static op =>
            op.Kind is OperationKind.DeleteToQuarantine or OperationKind.MovePath);

        if (hasDestructiveOrMove && string.IsNullOrWhiteSpace(plan.RollbackStrategy))
            violations.Add("Plan contains DeleteToQuarantine or MovePath operations but RollbackStrategy is empty.");
    }

    /// <summary>
    /// Checks whether a path contains any protected directory segment.
    /// </summary>
    private static bool IsProtectedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Normalize to forward slashes for consistent splitting, then split on both separators.
        var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (ProtectedSegments.Contains(segment))
                return true;
        }

        return false;
    }
}
