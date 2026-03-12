using Atlas.Core.Scanning;

namespace Atlas.Service.Services.DeltaSources;

/// <summary>
/// Probes a root path against registered <see cref="IDeltaSource"/> implementations
/// and returns the best available source (highest <see cref="DeltaCapability"/>).
/// </summary>
public sealed class DeltaCapabilityDetector(IEnumerable<IDeltaSource> sources)
{
    private readonly IReadOnlyList<IDeltaSource> _sources = sources
        .OrderByDescending(static s => s.Capability)
        .ToList();

    /// <summary>
    /// Probes the given root and returns the best available delta source.
    /// Returns null if no source can observe the root.
    /// </summary>
    public async Task<IDeltaSource?> DetectBestSourceAsync(string rootPath, CancellationToken ct = default)
    {
        foreach (var source in _sources)
        {
            if (await source.IsAvailableForRootAsync(rootPath, ct))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// Probes the given root and returns a summary of all source availability.
    /// Useful for diagnostics and logging.
    /// </summary>
    public async Task<DeltaCapabilityReport> ProbeRootAsync(string rootPath, CancellationToken ct = default)
    {
        IDeltaSource? best = null;
        var available = new List<DeltaCapability>();

        foreach (var source in _sources)
        {
            if (await source.IsAvailableForRootAsync(rootPath, ct))
            {
                available.Add(source.Capability);
                best ??= source;
            }
        }

        return new DeltaCapabilityReport
        {
            RootPath = rootPath,
            BestCapability = best?.Capability ?? DeltaCapability.None,
            AvailableCapabilities = available
        };
    }
}

/// <summary>
/// Summary of delta-source availability for a given root.
/// </summary>
public sealed class DeltaCapabilityReport
{
    public string RootPath { get; init; } = string.Empty;
    public DeltaCapability BestCapability { get; init; }
    public IReadOnlyList<DeltaCapability> AvailableCapabilities { get; init; } = [];
}
