using Atlas.Core.Contracts;

namespace Atlas.Service.Services;

/// <summary>
/// Deterministic evaluator that inspects an <see cref="ExecutionBatch"/> and returns
/// a checkpoint eligibility result indicating whether a VSS snapshot is needed.
/// All rules are bounded, deterministic, and produce human-readable reasons.
/// </summary>
public static class CheckpointEligibilityEvaluator
{
    /// <summary>
    /// The minimum number of destructive operations that forces a <see cref="CheckpointRequirement.Required"/> result.
    /// </summary>
    private const int DestructiveThreshold = 5;

    /// <summary>
    /// Operation kinds considered destructive for checkpoint eligibility purposes.
    /// </summary>
    private static readonly HashSet<OperationKind> DestructiveKinds =
    [
        OperationKind.DeleteToQuarantine,
        OperationKind.MergeDuplicateGroup
    ];

    /// <summary>
    /// Operation kinds that are always considered safe and never trigger a checkpoint recommendation.
    /// </summary>
    private static readonly HashSet<OperationKind> AlwaysSafeKinds =
    [
        OperationKind.CreateDirectory,
        OperationKind.MovePath,
        OperationKind.RenamePath,
        OperationKind.RestoreFromQuarantine
    ];

    /// <summary>
    /// Optimization sub-kinds that are considered safe when used with <see cref="OperationKind.ApplyOptimizationFix"/>.
    /// </summary>
    private static readonly HashSet<OptimizationKind> SafeOptimizationKinds =
    [
        OptimizationKind.TemporaryFiles,
        OptimizationKind.CacheCleanup
    ];

    /// <summary>
    /// Evaluates checkpoint eligibility for the given execution batch.
    /// </summary>
    /// <param name="batch">The execution batch to evaluate.</param>
    /// <param name="isTrustedSession">
    /// Whether the current inventory session is trusted. When false (degraded), the result is forced to Required.
    /// Defaults to true when not supplied.
    /// </param>
    /// <returns>A deterministic eligibility result with reasons and covered volumes.</returns>
    public static CheckpointEligibilityResult Evaluate(ExecutionBatch batch, bool isTrustedSession = true)
    {
        var reasons = new List<string>();
        var destructiveCount = 0;
        var hasDestructive = false;
        var requirement = CheckpointRequirement.NotNeeded;

        // Count destructive operations.
        foreach (var op in batch.Operations)
        {
            if (IsDestructiveOperation(op))
            {
                destructiveCount++;
                hasDestructive = true;
            }
        }

        // Collect covered volumes from the batch.
        var coveredVolumes = new List<string>(batch.TouchedVolumes.Distinct(StringComparer.OrdinalIgnoreCase));

        // ── Rule 1: Untrusted / degraded session → Required ─────────────────
        if (!isTrustedSession)
        {
            requirement = CheckpointRequirement.Required;
            reasons.Add("Inventory session is degraded (untrusted); VSS checkpoint required for safety.");
        }

        // ── Rule 2: High destructive count → Required ───────────────────────
        if (destructiveCount >= DestructiveThreshold)
        {
            requirement = CheckpointRequirement.Required;
            reasons.Add($"Batch contains {destructiveCount} destructive operations (threshold: {DestructiveThreshold}); VSS checkpoint required.");
        }

        // ── Rule 3: Cross-volume operations → Required ──────────────────────
        if (coveredVolumes.Count > 1)
        {
            requirement = CheckpointRequirement.Required;
            reasons.Add($"Operations span {coveredVolumes.Count} volumes ({string.Join(", ", coveredVolumes)}); cross-volume VSS checkpoint required.");
        }

        // ── Rule 4: Any destructive operation → at least Recommended ────────
        if (hasDestructive && requirement == CheckpointRequirement.NotNeeded)
        {
            requirement = CheckpointRequirement.Recommended;
            reasons.Add($"Batch contains {destructiveCount} destructive operation(s); VSS checkpoint recommended.");
        }

        // ── Rule 5: NotNeeded when all operations are safe ──────────────────
        if (!hasDestructive && requirement == CheckpointRequirement.NotNeeded)
        {
            reasons.Add("All operations are reversible and safe; no VSS checkpoint needed.");
        }

        return new CheckpointEligibilityResult
        {
            Requirement = requirement,
            Reasons = reasons,
            CoveredVolumes = coveredVolumes,
            HasDestructiveOperations = hasDestructive,
            DestructiveOperationCount = destructiveCount
        };
    }

    /// <summary>
    /// Determines whether an operation is considered destructive for checkpoint purposes.
    /// </summary>
    private static bool IsDestructiveOperation(PlanOperation op)
    {
        // Explicitly destructive kinds.
        if (DestructiveKinds.Contains(op.Kind))
            return true;

        // ApplyOptimizationFix is destructive unless it targets only safe optimization sub-kinds.
        if (op.Kind == OperationKind.ApplyOptimizationFix)
            return !SafeOptimizationKinds.Contains(op.OptimizationKind);

        // Everything else (CreateDirectory, MovePath, RenamePath, RestoreFromQuarantine, RevertOptimizationFix) is safe.
        return false;
    }
}

/// <summary>
/// The degree to which a VSS checkpoint is needed before executing the batch.
/// </summary>
public enum CheckpointRequirement
{
    /// <summary>All operations are reversible; no checkpoint needed.</summary>
    NotNeeded = 0,

    /// <summary>Some destructive operations exist; a checkpoint is advised but not mandatory.</summary>
    Recommended = 1,

    /// <summary>High-risk conditions detected; a checkpoint must be taken before live execution.</summary>
    Required = 2
}

/// <summary>
/// The result of evaluating checkpoint eligibility for an execution batch.
/// </summary>
public sealed class CheckpointEligibilityResult
{
    /// <summary>The determined checkpoint requirement level.</summary>
    public CheckpointRequirement Requirement { get; init; }

    /// <summary>Human-readable reasons explaining the decision.</summary>
    public List<string> Reasons { get; init; } = new();

    /// <summary>Volume roots covered by this evaluation.</summary>
    public List<string> CoveredVolumes { get; init; } = new();

    /// <summary>Whether the batch contains any destructive operations.</summary>
    public bool HasDestructiveOperations { get; init; }

    /// <summary>Count of destructive operations in the batch.</summary>
    public int DestructiveOperationCount { get; init; }
}
