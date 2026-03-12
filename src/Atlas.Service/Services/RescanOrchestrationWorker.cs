using Atlas.Core.Contracts;
using Atlas.Core.Policies;
using Atlas.Core.Scanning;
using Atlas.Service.Services.DeltaSources;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Services;

/// <summary>
/// Background worker that orchestrates bounded incremental rescans.
/// Picks the best available delta source per root, detects changes,
/// and triggers rescans that persist through the existing inventory repository.
/// </summary>
public sealed class RescanOrchestrationWorker(
    ILogger<RescanOrchestrationWorker> logger,
    IOptions<AtlasServiceOptions> options,
    PolicyProfile profile,
    FileScanner fileScanner,
    DeltaCapabilityDetector capabilityDetector,
    IInventoryRepository inventoryRepository) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastRescanUtc = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        if (!opts.EnableRescanOrchestration)
        {
            logger.LogInformation("Rescan orchestration is disabled.");
            return;
        }

        logger.LogInformation(
            "Rescan orchestration started. Interval={Interval}, MaxRoots={MaxRoots}, Cooldown={Cooldown}.",
            opts.RescanInterval, opts.MaxRootsPerCycle, opts.OrchestrationCooldown);

        // Initial delay to let startup finish before first cycle.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOrchestrationCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rescan orchestration cycle failed.");
            }

            await Task.Delay(opts.OrchestrationCooldown, stoppingToken);
        }

        logger.LogInformation("Rescan orchestration stopped.");
    }

    internal async Task RunOrchestrationCycleAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var roots = profile.ScanRoots
            .Concat(profile.MutableRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roots.Count == 0)
        {
            logger.LogDebug("No roots configured for rescan orchestration.");
            return;
        }

        var now = DateTime.UtcNow;
        var rescanCount = 0;

        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested || rescanCount >= opts.MaxRootsPerCycle)
                break;

            // Skip roots that were rescanned recently.
            if (_lastRescanUtc.TryGetValue(root, out var lastRescan)
                && now - lastRescan < opts.RescanInterval)
            {
                logger.LogDebug("Skipping root {Root}: last rescan {Ago} ago.", root, now - lastRescan);
                continue;
            }

            var source = await capabilityDetector.DetectBestSourceAsync(root, ct);
            if (source is null)
            {
                logger.LogWarning("No delta source available for root {Root}. Skipping.", root);
                continue;
            }

            var result = await source.DetectChangesAsync(root, ct);
            logger.LogInformation(
                "Delta detection for {Root}: capability={Capability}, hasChanges={HasChanges}, fullRescan={FullRescan}, reason={Reason}.",
                root, result.Capability, result.HasChanges, result.RequiresFullRescan, result.Reason);

            if (!result.HasChanges)
            {
                logger.LogDebug("No changes for root {Root}. Skipping rescan.", root);
                _lastRescanUtc[root] = now;
                continue;
            }

            // Trigger a rescan and persist the session.
            await RunRescanForRootAsync(root, result, ct);
            _lastRescanUtc[root] = now;
            rescanCount++;
        }

        logger.LogInformation("Orchestration cycle complete. Rescanned {Count} root(s).", rescanCount);
    }

    private async Task RunRescanForRootAsync(string root, DeltaResult deltaResult, CancellationToken ct)
    {
        var opts = options.Value;

        // Check if incremental composition is eligible.
        if (!deltaResult.RequiresFullRescan
            && deltaResult.ChangedPaths.Count > 0
            && deltaResult.ChangedPaths.Count <= opts.MaxIncrementalPaths)
        {
            logger.LogInformation(
                "Attempting incremental composition for root {Root}. DeltaPaths={Count}, Capability={Cap}.",
                root, deltaResult.ChangedPaths.Count, deltaResult.Capability);

            try
            {
                var composed = await TryIncrementalCompositionAsync(root, deltaResult, ct);
                if (composed) return;
                // TryIncrementalCompositionAsync already called RunFullRescanAsync on failure.
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Incremental composition failed for root {Root}. Falling back to full rescan.", root);
            }
        }

        // Determine the fallback note.
        string note;
        if (deltaResult.RequiresFullRescan)
            note = $"Delta source requires full rescan: {deltaResult.Reason}";
        else if (deltaResult.ChangedPaths.Count > opts.MaxIncrementalPaths)
            note = $"Delta path count ({deltaResult.ChangedPaths.Count}) exceeds MaxIncrementalPaths ({opts.MaxIncrementalPaths}); full rescan required.";
        else if (deltaResult.ChangedPaths.Count == 0)
            note = "Delta reported changes but no specific paths; full rescan.";
        else
            note = "Full rescan fallback.";

        await RunFullRescanAsync(root, deltaResult, note, ct);
    }

    private async Task<bool> TryIncrementalCompositionAsync(
        string root, DeltaResult deltaResult, CancellationToken ct)
    {
        // 1. Find baseline session for this root.
        var baseline = await FindBaselineSessionForRootAsync(root, ct);
        if (baseline is null)
        {
            logger.LogInformation("No baseline session for root {Root}. Falling back to full rescan.", root);
            await RunFullRescanAsync(root, deltaResult,
                "No baseline session found for this root; full rescan required.", ct);
            return false;
        }

        // 2. Load baseline files.
        var fileCount = await inventoryRepository.GetFileCountForSessionAsync(baseline.SessionId, ct);
        if (fileCount == 0)
        {
            logger.LogInformation("Baseline session {Sid} has no files. Falling back to full rescan.",
                baseline.SessionId);
            await RunFullRescanAsync(root, deltaResult,
                $"Baseline session {baseline.SessionId} has no files; full rescan required.", ct);
            return false;
        }

        var baselineFiles = await inventoryRepository.GetFilesForSessionAsync(
            baseline.SessionId, limit: fileCount, offset: 0, ct);

        // 3. Build mutable dictionary from baseline.
        var composed = new Dictionary<string, FileInventoryItem>(
            baselineFiles.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var file in baselineFiles)
            composed[file.Path] = file;

        // 4. Apply delta: re-inspect each changed path.
        var updatedCount = 0;
        var removedCount = 0;
        foreach (var changedPath in deltaResult.ChangedPaths)
        {
            var inspected = fileScanner.InspectFile(profile, changedPath);
            if (inspected is not null)
            {
                composed[inspected.Path] = inspected;
                updatedCount++;
            }
            else
            {
                if (composed.Remove(changedPath))
                    removedCount++;
            }
        }

        // 5. Fresh volume snapshots.
        var volumes = FileScanner.SnapshotVolumes();

        // 6. Persist composed session.
        var session = new ScanSession
        {
            Roots = [root],
            Volumes = volumes,
            Files = composed.Values.ToList(),
            DuplicateGroupCount = baseline.DuplicateGroupCount,
            Trigger = "Orchestration",
            BuildMode = "IncrementalComposition",
            DeltaSource = deltaResult.Capability.ToString(),
            BaselineSessionId = baseline.SessionId,
            IsTrusted = true,
            CompositionNote = $"Composed from baseline {baseline.SessionId}: " +
                $"{deltaResult.ChangedPaths.Count} delta paths, " +
                $"{updatedCount} updated, {removedCount} removed. " +
                $"DuplicateGroupCount carried from baseline."
        };

        var sessionId = await inventoryRepository.SaveSessionAsync(session, ct);
        logger.LogInformation(
            "Incremental composition for root {Root} persisted as session {SessionId}. " +
            "Baseline={Baseline}, DeltaPaths={Delta}, Composed={Total}.",
            root, sessionId, baseline.SessionId, deltaResult.ChangedPaths.Count, composed.Count);
        return true;
    }

    private async Task<ScanSessionSummary?> FindBaselineSessionForRootAsync(
        string root, CancellationToken ct)
    {
        var recentSessions = await inventoryRepository.ListSessionsAsync(limit: 10, offset: 0, ct);
        foreach (var session in recentSessions)
        {
            var roots = await inventoryRepository.GetRootsForSessionAsync(session.SessionId, ct);
            if (roots.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase)))
                return session;
        }
        return null;
    }

    private async Task RunFullRescanAsync(
        string root, DeltaResult deltaResult, string compositionNote, CancellationToken ct)
    {
        logger.LogInformation("Starting full rescan for root {Root}. Note: {Note}", root, compositionNote);

        var scanRequest = new ScanRequest
        {
            FullScan = true,
            Roots = [root],
            MaxFiles = options.Value.MaxFilesPerScan
        };

        var response = await fileScanner.ScanAsync(profile, scanRequest, ct);

        try
        {
            var session = new ScanSession
            {
                Roots = [root],
                Volumes = response.Volumes,
                Files = response.Inventory,
                DuplicateGroupCount = response.Duplicates.Count,
                Trigger = "Orchestration",
                BuildMode = "FullRescan",
                DeltaSource = deltaResult.Capability.ToString(),
                IsTrusted = true,
                CompositionNote = compositionNote
            };
            var sessionId = await inventoryRepository.SaveSessionAsync(session, ct);
            logger.LogInformation(
                "Full rescan for root {Root} persisted as session {SessionId}. Files={Files}, Duplicates={Dupes}.",
                root, sessionId, response.FilesScanned, response.Duplicates.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist full rescan session for root {Root}.", root);
        }
    }
}
