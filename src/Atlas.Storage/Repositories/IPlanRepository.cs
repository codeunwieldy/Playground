using Atlas.Core.Contracts;

namespace Atlas.Storage.Repositories;

/// <summary>
/// Repository for storing and retrieving execution plans and batches.
/// </summary>
public interface IPlanRepository
{
    /// <summary>
    /// Persists a plan graph and returns its identifier.
    /// </summary>
    Task<string> SavePlanAsync(PlanGraph plan, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a plan by its identifier.
    /// </summary>
    Task<PlanGraph?> GetPlanAsync(string planId, CancellationToken ct = default);

    /// <summary>
    /// Lists plan summaries with pagination support.
    /// </summary>
    Task<IReadOnlyList<PlanSummary>> ListPlansAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Deletes a plan by its identifier.
    /// </summary>
    Task<bool> DeletePlanAsync(string planId, CancellationToken ct = default);

    /// <summary>
    /// Persists an execution batch and returns its identifier.
    /// </summary>
    Task<string> SaveBatchAsync(ExecutionBatch batch, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an execution batch by its identifier.
    /// </summary>
    Task<ExecutionBatch?> GetBatchAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all execution batches associated with a plan.
    /// </summary>
    Task<IReadOnlyList<ExecutionBatch>> GetBatchesForPlanAsync(string planId, CancellationToken ct = default);
}

/// <summary>
/// Summary projection of a plan for listing purposes.
/// </summary>
/// <param name="PlanId">Unique identifier of the plan.</param>
/// <param name="Scope">The scope or target of the plan.</param>
/// <param name="Summary">Brief description of the plan.</param>
/// <param name="CreatedUtc">When the plan was created.</param>
public sealed record PlanSummary(string PlanId, string Scope, string Summary, DateTime CreatedUtc);
