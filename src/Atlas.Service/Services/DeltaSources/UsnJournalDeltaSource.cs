using Atlas.Core.Scanning;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Atlas.Service.Services.DeltaSources;

/// <summary>
/// USN change journal delta source. Reads the NTFS change journal to detect
/// file changes since the last checkpoint. Falls back to RequiresFullRescan
/// when the journal is unavailable, reset, or changes exceed the bounded cap.
/// </summary>
public sealed class UsnJournalDeltaSource(
    IUsnJournalReader reader,
    IUsnCheckpointRepository checkpointRepo,
    ILogger<UsnJournalDeltaSource> logger) : IDeltaSource
{
    private const int MaxChangedPaths = 50_000;
    private const int MaxUnresolvedForRescan = 1_000;

    public DeltaCapability Capability => DeltaCapability.UsnJournal;

    public Task<bool> IsAvailableForRootAsync(string rootPath, CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(rootPath)) return Task.FromResult(false);

            var driveRoot = Path.GetPathRoot(rootPath);
            if (string.IsNullOrEmpty(driveRoot)) return Task.FromResult(false);

            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady) return Task.FromResult(false);

            var isNtfs = string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
            if (!isNtfs) return Task.FromResult(false);

            // Probe actual journal access to confirm availability.
            var journal = reader.QueryJournal(driveRoot);
            return Task.FromResult(journal is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task<DeltaResult> DetectChangesAsync(string rootPath, CancellationToken ct = default)
    {
        var volumeRoot = Path.GetPathRoot(rootPath) ?? rootPath;
        var volumeId = volumeRoot.TrimEnd('\\');

        // 1. Query current journal state.
        var journal = reader.QueryJournal(volumeRoot);
        if (journal is null)
        {
            logger.LogWarning("USN journal inaccessible for volume {Volume}.", volumeId);
            return FullRescan(rootPath, "USN journal inaccessible.");
        }

        // 2. Load checkpoint.
        var checkpoint = await checkpointRepo.GetCheckpointAsync(volumeId, ct);

        // 3. First-run baseline: no checkpoint exists.
        if (checkpoint is null)
        {
            logger.LogInformation(
                "No USN checkpoint for {Volume}. Establishing baseline at USN {Usn}.",
                volumeId, journal.NextUsn);
            await SaveCheckpoint(volumeId, journal.JournalId, journal.NextUsn, ct);
            return FullRescan(rootPath, "First-run baseline; establishing USN checkpoint.");
        }

        // 4. Journal ID changed (journal was deleted and recreated).
        if (checkpoint.JournalId != journal.JournalId)
        {
            logger.LogWarning(
                "USN journal ID changed for {Volume}. Old={Old}, New={New}. Resetting checkpoint.",
                volumeId, checkpoint.JournalId, journal.JournalId);
            await checkpointRepo.DeleteCheckpointAsync(volumeId, ct);
            await SaveCheckpoint(volumeId, journal.JournalId, journal.NextUsn, ct);
            return FullRescan(rootPath, "Journal ID changed; full rescan required.");
        }

        // 5. Checkpoint USN is before the journal's earliest record (journal wrapped).
        if (checkpoint.LastUsn < journal.FirstUsn)
        {
            logger.LogWarning(
                "USN checkpoint {Usn} for {Volume} is before journal start {First}. Journal wrapped.",
                checkpoint.LastUsn, volumeId, journal.FirstUsn);
            await SaveCheckpoint(volumeId, journal.JournalId, journal.NextUsn, ct);
            return FullRescan(rootPath, "USN checkpoint too old; journal entries lost.");
        }

        // 6. No changes: checkpoint is at the journal head.
        if (checkpoint.LastUsn >= journal.NextUsn)
        {
            return new DeltaResult
            {
                RootPath = rootPath,
                Capability = DeltaCapability.UsnJournal,
                HasChanges = false,
                Reason = "No changes since last checkpoint."
            };
        }

        // 7. Read changes bounded by MaxChangedPaths.
        var readResult = reader.ReadChanges(
            volumeRoot, checkpoint.LastUsn, journal.JournalId,
            MaxChangedPaths, rootPath);

        if (!readResult.Success)
        {
            logger.LogWarning("USN read failed for {Volume}: {Reason}.",
                volumeId, readResult.ErrorReason);
            return FullRescan(rootPath,
                $"USN read error: {readResult.ErrorReason}");
        }

        // 8. Overflow: too many changed paths.
        if (readResult.Overflowed)
        {
            logger.LogInformation(
                "USN change set overflowed for {Volume}. Full rescan needed.", volumeId);
            await SaveCheckpoint(volumeId, journal.JournalId, journal.NextUsn, ct);
            return FullRescan(rootPath,
                $"Change set exceeded {MaxChangedPaths} paths; full rescan.");
        }

        // 9. Too many unresolvable records.
        if (readResult.UnresolvedCount > MaxUnresolvedForRescan)
        {
            logger.LogInformation(
                "Too many unresolvable USN records ({Count}) for {Volume}.",
                readResult.UnresolvedCount, volumeId);
            await SaveCheckpoint(volumeId, journal.JournalId, journal.NextUsn, ct);
            return FullRescan(rootPath,
                $"Too many unresolvable records ({readResult.UnresolvedCount}); full rescan.");
        }

        // 10. Success: advance checkpoint.
        await SaveCheckpoint(volumeId, journal.JournalId, journal.NextUsn, ct);

        var changedPaths = readResult.ChangedPaths;
        if (changedPaths.Count == 0)
        {
            return new DeltaResult
            {
                RootPath = rootPath,
                Capability = DeltaCapability.UsnJournal,
                HasChanges = false,
                Reason = "Journal advanced but no changes under monitored root."
            };
        }

        return new DeltaResult
        {
            RootPath = rootPath,
            Capability = DeltaCapability.UsnJournal,
            HasChanges = true,
            ChangedPaths = changedPaths,
            RequiresFullRescan = false,
            Reason = $"{changedPaths.Count} changed path(s) detected via USN journal."
        };
    }

    private async Task SaveCheckpoint(
        string volumeId, ulong journalId, long usn, CancellationToken ct)
    {
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = volumeId,
            JournalId = journalId,
            LastUsn = usn,
            UpdatedUtc = DateTime.UtcNow
        }, ct);
    }

    private static DeltaResult FullRescan(string rootPath, string reason) => new()
    {
        RootPath = rootPath,
        Capability = DeltaCapability.UsnJournal,
        HasChanges = true,
        RequiresFullRescan = true,
        Reason = reason
    };
}
