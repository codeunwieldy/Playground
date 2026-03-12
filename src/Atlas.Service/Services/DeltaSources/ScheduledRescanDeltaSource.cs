using Atlas.Core.Scanning;

namespace Atlas.Service.Services.DeltaSources;

/// <summary>
/// Fallback delta source that always reports changes and requests a full rescan.
/// Used when no better change detection mechanism is available for a root.
/// The orchestration layer controls timing/cadence externally.
/// </summary>
public sealed class ScheduledRescanDeltaSource : IDeltaSource
{
    public DeltaCapability Capability => DeltaCapability.ScheduledRescan;

    public Task<bool> IsAvailableForRootAsync(string rootPath, CancellationToken ct = default)
    {
        // Scheduled rescan is always available if the root exists.
        return Task.FromResult(Directory.Exists(rootPath));
    }

    public Task<DeltaResult> DetectChangesAsync(string rootPath, CancellationToken ct = default)
    {
        return Task.FromResult(new DeltaResult
        {
            RootPath = rootPath,
            Capability = DeltaCapability.ScheduledRescan,
            HasChanges = true,
            RequiresFullRescan = true,
            Reason = "Scheduled rescan fallback; full rescan of root."
        });
    }
}
