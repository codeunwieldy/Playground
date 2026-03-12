namespace Atlas.Core.Scanning;

/// <summary>
/// Abstraction for detecting filesystem changes on a given root path.
/// Implementations may use USN journals, FileSystemWatcher, or scheduled rescans.
/// </summary>
public interface IDeltaSource
{
    /// <summary>The capability level this source provides.</summary>
    DeltaCapability Capability { get; }

    /// <summary>
    /// Probes whether this delta source can observe the given root path.
    /// Returns false if the root's filesystem, permissions, or volume type
    /// are incompatible with this source.
    /// </summary>
    Task<bool> IsAvailableForRootAsync(string rootPath, CancellationToken ct = default);

    /// <summary>
    /// Queries the delta source for changes since the last observation.
    /// The result indicates whether changes were detected and whether a
    /// full rescan is needed.
    /// </summary>
    Task<DeltaResult> DetectChangesAsync(string rootPath, CancellationToken ct = default);
}
