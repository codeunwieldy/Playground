namespace Atlas.Storage.Repositories;

/// <summary>
/// Persists per-volume USN journal read checkpoints so the delta source
/// can resume from the last-known position between service restarts.
/// </summary>
public interface IUsnCheckpointRepository
{
    Task<UsnCheckpoint?> GetCheckpointAsync(string volumeId, CancellationToken ct = default);
    Task SaveCheckpointAsync(UsnCheckpoint checkpoint, CancellationToken ct = default);
    Task DeleteCheckpointAsync(string volumeId, CancellationToken ct = default);
}

/// <summary>
/// Per-volume USN journal checkpoint.
/// </summary>
public sealed class UsnCheckpoint
{
    public string VolumeId { get; init; } = string.Empty;
    public ulong JournalId { get; init; }
    public long LastUsn { get; init; }
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}
