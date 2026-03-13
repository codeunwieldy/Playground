using Atlas.Core.Contracts;

namespace Atlas.Storage.Repositories;

/// <summary>
/// Repository for storing and retrieving optimization findings.
/// </summary>
public interface IOptimizationRepository
{
    /// <summary>
    /// Persists an optimization finding and returns its identifier.
    /// </summary>
    Task<string> SaveFindingAsync(OptimizationFinding finding, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an optimization finding by its identifier.
    /// </summary>
    Task<OptimizationFinding?> GetFindingAsync(string findingId, CancellationToken ct = default);

    /// <summary>
    /// Lists optimization finding summaries with pagination support.
    /// </summary>
    Task<IReadOnlyList<OptimizationFindingSummary>> ListFindingsAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Lists optimization findings filtered by kind.
    /// </summary>
    Task<IReadOnlyList<OptimizationFinding>> GetFindingsByKindAsync(OptimizationKind kind, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all findings that can be auto-fixed.
    /// </summary>
    Task<IReadOnlyList<OptimizationFinding>> GetAutoFixableFindingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves findings targeting a specific path or resource.
    /// </summary>
    Task<IReadOnlyList<OptimizationFinding>> GetFindingsByTargetAsync(string target, CancellationToken ct = default);

    /// <summary>
    /// Deletes an optimization finding by its identifier.
    /// </summary>
    Task<bool> DeleteFindingAsync(string findingId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all findings of a specific kind.
    /// </summary>
    Task<int> DeleteFindingsByKindAsync(OptimizationKind kind, CancellationToken ct = default);

    // ── Execution history (C-037) ──────────────────────────────────────────

    /// <summary>
    /// Persists an optimization execution record and returns its identifier.
    /// </summary>
    Task<string> SaveExecutionRecordAsync(OptimizationExecutionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Retrieves execution history with optional plan filter and pagination.
    /// </summary>
    Task<IReadOnlyList<OptimizationExecutionRecord>> GetExecutionHistoryAsync(string? planId = null, int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Retrieves execution history for a specific target path or resource.
    /// </summary>
    Task<IReadOnlyList<OptimizationExecutionRecord>> GetExecutionHistoryForTargetAsync(string target, CancellationToken ct = default);

    // ── Execution history rollups (C-041) ──────────────────────────────────

    /// <summary>
    /// Returns bounded rollup summaries of execution history grouped by optimization kind.
    /// </summary>
    Task<IReadOnlyList<OptimizationExecutionRollup>> GetExecutionRollupsAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Returns compact execution summary projections for recent history.
    /// </summary>
    Task<IReadOnlyList<OptimizationExecutionSummary>> GetRecentExecutionSummariesAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single execution record by its identifier.
    /// </summary>
    Task<OptimizationExecutionRecord?> GetExecutionRecordAsync(string recordId, CancellationToken ct = default);
}

/// <summary>
/// Summary projection of an optimization finding for listing purposes.
/// </summary>
/// <param name="FindingId">Unique identifier of the finding.</param>
/// <param name="Kind">The type of optimization opportunity.</param>
/// <param name="Target">The target path or resource.</param>
/// <param name="CanAutoFix">Whether this finding can be automatically fixed.</param>
/// <param name="CreatedUtc">When the finding was discovered.</param>
public sealed record OptimizationFindingSummary(string FindingId, OptimizationKind Kind, string Target, bool CanAutoFix, DateTime CreatedUtc);
