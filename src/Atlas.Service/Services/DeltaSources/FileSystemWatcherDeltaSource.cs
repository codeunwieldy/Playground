using Atlas.Core.Scanning;

namespace Atlas.Service.Services.DeltaSources;

/// <summary>
/// FileSystemWatcher-based delta source. Available on most local volumes.
/// Tracks whether any changes were observed since the last check.
/// </summary>
public sealed class FileSystemWatcherDeltaSource : IDeltaSource, IDisposable
{
    private readonly Dictionary<string, WatcherState> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public DeltaCapability Capability => DeltaCapability.Watcher;

    public Task<bool> IsAvailableForRootAsync(string rootPath, CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(rootPath)) return Task.FromResult(false);

            var driveRoot = Path.GetPathRoot(rootPath);
            if (string.IsNullOrEmpty(driveRoot)) return Task.FromResult(false);

            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady) return Task.FromResult(false);

            // FileSystemWatcher works on local fixed and removable drives.
            // It does not work reliably on network shares.
            var isLocal = drive.DriveType is DriveType.Fixed or DriveType.Removable;
            return Task.FromResult(isLocal);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<DeltaResult> DetectChangesAsync(string rootPath, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(rootPath, out var state))
            {
                // First call: start a watcher and report that a full rescan is needed.
                state = StartWatcher(rootPath);
                if (state is null)
                {
                    return Task.FromResult(new DeltaResult
                    {
                        RootPath = rootPath,
                        Capability = DeltaCapability.Watcher,
                        HasChanges = true,
                        RequiresFullRescan = true,
                        Reason = "Failed to start FileSystemWatcher; full rescan recommended."
                    });
                }

                _watchers[rootPath] = state;
                return Task.FromResult(new DeltaResult
                {
                    RootPath = rootPath,
                    Capability = DeltaCapability.Watcher,
                    HasChanges = true,
                    RequiresFullRescan = true,
                    Reason = "Watcher just started; initial full rescan needed."
                });
            }

            // Check for changes since last read.
            var changedPaths = state.DrainChangedPaths();
            var hasOverflow = state.DrainOverflow();

            return Task.FromResult(new DeltaResult
            {
                RootPath = rootPath,
                Capability = DeltaCapability.Watcher,
                HasChanges = changedPaths.Count > 0 || hasOverflow,
                ChangedPaths = hasOverflow ? [] : changedPaths,
                RequiresFullRescan = hasOverflow,
                Reason = hasOverflow
                    ? "Watcher buffer overflowed; full rescan recommended."
                    : changedPaths.Count > 0
                        ? $"{changedPaths.Count} changed path(s) detected."
                        : "No changes detected."
            });
        }
    }

    private static WatcherState? StartWatcher(string rootPath)
    {
        try
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024 // 64 KB
            };

            var state = new WatcherState(watcher);
            watcher.Changed += (_, e) => state.RecordChange(e.FullPath);
            watcher.Created += (_, e) => state.RecordChange(e.FullPath);
            watcher.Deleted += (_, e) => state.RecordChange(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                state.RecordChange(e.OldFullPath);
                state.RecordChange(e.FullPath);
            };
            watcher.Error += (_, _) => state.RecordOverflow();
            watcher.EnableRaisingEvents = true;
            return state;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var state in _watchers.Values)
            {
                state.Dispose();
            }
            _watchers.Clear();
        }
    }

    private sealed class WatcherState(FileSystemWatcher watcher) : IDisposable
    {
        private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _stateLock = new();
        private bool _overflow;

        public void RecordChange(string path)
        {
            lock (_stateLock)
            {
                if (_changedPaths.Count < 10_000)
                {
                    _changedPaths.Add(path);
                }
                else
                {
                    _overflow = true;
                }
            }
        }

        public void RecordOverflow()
        {
            lock (_stateLock) { _overflow = true; }
        }

        public List<string> DrainChangedPaths()
        {
            lock (_stateLock)
            {
                var result = new List<string>(_changedPaths);
                _changedPaths.Clear();
                return result;
            }
        }

        public bool DrainOverflow()
        {
            lock (_stateLock)
            {
                var was = _overflow;
                _overflow = false;
                return was;
            }
        }

        public void Dispose() => watcher.Dispose();
    }
}
