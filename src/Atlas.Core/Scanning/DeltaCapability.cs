namespace Atlas.Core.Scanning;

/// <summary>
/// Describes how a mutable root can be observed for changes.
/// Ordered by preference: USN is best, then Watcher, then ScheduledRescan.
/// </summary>
public enum DeltaCapability
{
    /// <summary>No change detection available. Root cannot be monitored.</summary>
    None = 0,

    /// <summary>Scheduled full or partial rescan at a configured cadence.</summary>
    ScheduledRescan = 1,

    /// <summary>FileSystemWatcher-based near-realtime change detection.</summary>
    Watcher = 2,

    /// <summary>NTFS USN change journal for efficient incremental queries.</summary>
    UsnJournal = 3
}
