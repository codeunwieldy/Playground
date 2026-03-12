namespace Atlas.Core.Scanning;

/// <summary>
/// The result of probing or querying a delta source for a given root.
/// </summary>
public sealed class DeltaResult
{
    /// <summary>The root path that was probed or queried.</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>The capability level that was actually available.</summary>
    public DeltaCapability Capability { get; init; }

    /// <summary>Whether the source detected any changes since the last observation.</summary>
    public bool HasChanges { get; init; }

    /// <summary>
    /// Paths known to have changed. Empty when <see cref="HasChanges"/> is false,
    /// or when the source only knows "something changed" without specific paths
    /// (in which case a full rescan of the root is needed).
    /// </summary>
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];

    /// <summary>Whether a full rescan of this root is recommended.</summary>
    public bool RequiresFullRescan { get; init; }

    /// <summary>Human-readable reason for the capability/fallback decision.</summary>
    public string Reason { get; init; } = string.Empty;
}
