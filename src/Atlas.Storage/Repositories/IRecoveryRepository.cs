using Atlas.Core.Contracts;

namespace Atlas.Storage.Repositories;

/// <summary>
/// Repository for managing undo checkpoints and quarantined items.
/// </summary>
public interface IRecoveryRepository
{
    /// <summary>
    /// Persists an undo checkpoint and returns its identifier.
    /// </summary>
    Task<string> SaveCheckpointAsync(UndoCheckpoint checkpoint, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an undo checkpoint by its identifier.
    /// </summary>
    Task<UndoCheckpoint?> GetCheckpointAsync(string checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the undo checkpoint associated with a batch.
    /// </summary>
    Task<UndoCheckpoint?> GetCheckpointForBatchAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// Lists checkpoint summaries with pagination support.
    /// </summary>
    Task<IReadOnlyList<CheckpointSummary>> ListCheckpointsAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Deletes an undo checkpoint by its identifier.
    /// </summary>
    Task<bool> DeleteCheckpointAsync(string checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Persists a quarantine item and returns its identifier.
    /// </summary>
    Task<string> SaveQuarantineItemAsync(QuarantineItem item, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a quarantine item by its identifier.
    /// </summary>
    Task<QuarantineItem?> GetQuarantineItemAsync(string quarantineId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a quarantine item by its original path.
    /// </summary>
    Task<QuarantineItem?> GetQuarantineItemByOriginalPathAsync(string originalPath, CancellationToken ct = default);

    /// <summary>
    /// Lists all quarantine items associated with a plan.
    /// </summary>
    Task<IReadOnlyList<QuarantineItem>> GetQuarantineItemsForPlanAsync(string planId, CancellationToken ct = default);

    /// <summary>
    /// Lists quarantine items with pagination support.
    /// </summary>
    Task<IReadOnlyList<QuarantineItemSummary>> ListQuarantineItemsAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Retrieves quarantine items that have expired based on their retention period.
    /// </summary>
    Task<IReadOnlyList<QuarantineItem>> GetExpiredQuarantineItemsAsync(DateTime asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Deletes a quarantine item by its identifier.
    /// </summary>
    Task<bool> DeleteQuarantineItemAsync(string quarantineId, CancellationToken ct = default);
}

/// <summary>
/// Summary projection of an undo checkpoint for listing purposes.
/// </summary>
/// <param name="CheckpointId">Unique identifier of the checkpoint.</param>
/// <param name="BatchId">The batch this checkpoint can undo.</param>
/// <param name="OperationCount">Number of inverse operations in the checkpoint.</param>
/// <param name="CreatedUtc">When the checkpoint was created.</param>
public sealed record CheckpointSummary(string CheckpointId, string BatchId, int OperationCount, DateTime CreatedUtc);

/// <summary>
/// Summary projection of a quarantine item for listing purposes.
/// </summary>
/// <param name="QuarantineId">Unique identifier of the quarantine item.</param>
/// <param name="OriginalPath">The original file path before quarantine.</param>
/// <param name="Reason">Why the item was quarantined.</param>
/// <param name="RetentionUntilUtc">When the item can be permanently deleted.</param>
public sealed record QuarantineItemSummary(string QuarantineId, string OriginalPath, string Reason, DateTime RetentionUntilUtc);
