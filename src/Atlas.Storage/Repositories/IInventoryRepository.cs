using Atlas.Core.Contracts;

namespace Atlas.Storage.Repositories;

/// <summary>
/// Repository for persisting and querying scan session inventory.
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// Persists a complete scan session including roots, volumes, and file snapshots.
    /// </summary>
    Task<string> SaveSessionAsync(ScanSession session, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a scan session summary by ID.
    /// </summary>
    Task<ScanSessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent scan session summary.
    /// </summary>
    Task<ScanSessionSummary?> GetLatestSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists recent scan session summaries in descending time order.
    /// </summary>
    Task<IReadOnlyList<ScanSessionSummary>> ListSessionsAsync(int limit = 20, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Retrieves root paths for a given scan session.
    /// </summary>
    Task<IReadOnlyList<string>> GetRootsForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves volume snapshots for a given scan session.
    /// </summary>
    Task<IReadOnlyList<VolumeSnapshot>> GetVolumesForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves file snapshots for a given scan session with pagination.
    /// </summary>
    Task<IReadOnlyList<FileInventoryItem>> GetFilesForSessionAsync(string sessionId, int limit = 1000, int offset = 0, CancellationToken ct = default);

    /// <summary>
    /// Returns the total file count for a given scan session.
    /// </summary>
    Task<int> GetFileCountForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Compares two scan sessions and returns a diff summary with counts of added, removed, and changed files.
    /// </summary>
    Task<SessionDiffSummary> DiffSessionsAsync(string olderSessionId, string newerSessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns bounded changed file rows between two sessions, ordered by path.
    /// </summary>
    Task<IReadOnlyList<SessionDiffFile>> GetDiffFilesAsync(string olderSessionId, string newerSessionId, int limit = 200, int offset = 0, CancellationToken ct = default);
}

/// <summary>
/// Represents a persisted scan session with its associated data.
/// </summary>
public sealed class ScanSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> Roots { get; set; } = new();
    public List<VolumeSnapshot> Volumes { get; set; } = new();
    public List<FileInventoryItem> Files { get; set; } = new();
    public int DuplicateGroupCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // ── Provenance (C-016) ──────────────────────────────────────────────────
    public string Trigger { get; set; } = "Manual";
    public string BuildMode { get; set; } = "FullRescan";
    public string DeltaSource { get; set; } = string.Empty;
    public string BaselineSessionId { get; set; } = string.Empty;
    public bool IsTrusted { get; set; } = true;
    public string CompositionNote { get; set; } = string.Empty;
}

/// <summary>
/// Summary projection of a scan session for listing.
/// </summary>
/// <param name="SessionId">Unique identifier of the session.</param>
/// <param name="FilesScanned">Number of files captured in this session.</param>
/// <param name="DuplicateGroupCount">Number of duplicate groups found.</param>
/// <param name="RootCount">Number of roots scanned.</param>
/// <param name="VolumeCount">Number of volumes captured.</param>
/// <param name="CreatedUtc">When the session was recorded.</param>
public sealed record ScanSessionSummary(
    string SessionId,
    int FilesScanned,
    int DuplicateGroupCount,
    int RootCount,
    int VolumeCount,
    DateTime CreatedUtc,
    string Trigger = "Manual",
    string BuildMode = "FullRescan",
    string DeltaSource = "",
    string BaselineSessionId = "",
    bool IsTrusted = true,
    string CompositionNote = "");

/// <summary>
/// Summary of differences between two scan sessions.
/// </summary>
public sealed record SessionDiffSummary(
    string OlderSessionId,
    string NewerSessionId,
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    int UnchangedCount);

/// <summary>
/// A single file-level diff entry between two scan sessions.
/// </summary>
/// <param name="Path">The file path.</param>
/// <param name="ChangeKind">One of: Added, Removed, Changed.</param>
/// <param name="OlderSizeBytes">Size in the older session (null if Added).</param>
/// <param name="NewerSizeBytes">Size in the newer session (null if Removed).</param>
/// <param name="OlderLastModifiedUnix">Last modified in the older session (null if Added).</param>
/// <param name="NewerLastModifiedUnix">Last modified in the newer session (null if Removed).</param>
public sealed record SessionDiffFile(
    string Path,
    string ChangeKind,
    long? OlderSizeBytes,
    long? NewerSizeBytes,
    long? OlderLastModifiedUnix,
    long? NewerLastModifiedUnix);
