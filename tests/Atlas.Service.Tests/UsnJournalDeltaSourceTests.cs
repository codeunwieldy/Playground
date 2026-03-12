using Atlas.Core.Scanning;
using Atlas.Service.Services.DeltaSources;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Service.Tests;

/// <summary>
/// Focused tests for UsnJournalDeltaSource with mocked reader (C-014).
/// All tests exercise decision branches without requiring admin privileges or NTFS.
/// </summary>
public sealed class UsnJournalDeltaSourceTests
{
    private const string TestRoot = @"C:\TestRoot";
    private const string VolumeId = @"C:";

    private static UsnJournalDeltaSource CreateSource(
        IUsnJournalReader reader,
        IUsnCheckpointRepository? checkpointRepo = null) =>
        new(
            reader,
            checkpointRepo ?? new InMemoryUsnCheckpointRepository(),
            NullLogger<UsnJournalDeltaSource>.Instance);

    // ── First-run baseline ──────────────────────────────────────────────

    [Fact]
    public async Task FirstRun_NoCheckpoint_EstablishesBaselineAndReturnsFullRescan()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 1000 }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        var source = CreateSource(reader, checkpointRepo);

        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);
        Assert.True(result.HasChanges);
        Assert.Equal(DeltaCapability.UsnJournal, result.Capability);
        Assert.Contains("baseline", result.Reason, StringComparison.OrdinalIgnoreCase);

        // Checkpoint should be saved at NextUsn.
        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.NotNull(checkpoint);
        Assert.Equal(1000L, checkpoint.LastUsn);
        Assert.Equal(1UL, checkpoint.JournalId);
    }

    // ── Journal ID changed ──────────────────────────────────────────────

    [Fact]
    public async Task JournalIdChanged_ResetsCheckpointAndReturnsFullRescan()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 2, FirstUsn = 0, NextUsn = 5000 }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        // Pre-seed a checkpoint with the old journal ID.
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 500
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);
        Assert.Contains("Journal ID changed", result.Reason);

        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.NotNull(checkpoint);
        Assert.Equal(2UL, checkpoint.JournalId);
        Assert.Equal(5000L, checkpoint.LastUsn);
    }

    // ── Journal wrapped ─────────────────────────────────────────────────

    [Fact]
    public async Task CheckpointBeforeFirstUsn_JournalWrapped_ReturnsFullRescan()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 2000, NextUsn = 5000 }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 500
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);
        Assert.Contains("too old", result.Reason, StringComparison.OrdinalIgnoreCase);

        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(5000L, checkpoint!.LastUsn);
    }

    // ── No changes ──────────────────────────────────────────────────────

    [Fact]
    public async Task NoChangesSinceCheckpoint_ReturnsNoChanges()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 1000 }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 1000
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.False(result.HasChanges);
        Assert.False(result.RequiresFullRescan);

        // Checkpoint should not be updated.
        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(1000L, checkpoint!.LastUsn);
    }

    // ── Changes detected under cap ──────────────────────────────────────

    [Fact]
    public async Task ChangesDetected_UnderCap_ReturnsPaths()
    {
        var changedPaths = Enumerable.Range(1, 50)
            .Select(i => $@"{TestRoot}\file{i}.txt")
            .ToList();

        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 2000 },
            ReadResult = new UsnJournalReadResult
            {
                Success = true,
                NextUsn = 2000,
                ChangedPaths = changedPaths,
                UnresolvedCount = 0,
                Overflowed = false
            }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 1000
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.HasChanges);
        Assert.False(result.RequiresFullRescan);
        Assert.Equal(50, result.ChangedPaths.Count);

        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(2000L, checkpoint!.LastUsn);
    }

    // ── Overflow (exceeds cap) ──────────────────────────────────────────

    [Fact]
    public async Task ChangesDetected_OverCap_ReturnsFullRescan()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 2000 },
            ReadResult = new UsnJournalReadResult
            {
                Success = true,
                NextUsn = 2000,
                ChangedPaths = [],
                UnresolvedCount = 0,
                Overflowed = true
            }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 1000
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);

        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(2000L, checkpoint!.LastUsn);
    }

    // ── Too many unresolvable ───────────────────────────────────────────

    [Fact]
    public async Task TooManyUnresolvable_ReturnsFullRescan()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 2000 },
            ReadResult = new UsnJournalReadResult
            {
                Success = true,
                NextUsn = 2000,
                ChangedPaths = [@$"{TestRoot}\file.txt"],
                UnresolvedCount = 2000,
                Overflowed = false
            }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 1000
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);
        Assert.Contains("unresolvable", result.Reason, StringComparison.OrdinalIgnoreCase);

        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(2000L, checkpoint!.LastUsn);
    }

    // ── Read failure ────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFails_ReturnsFullRescan_DoesNotAdvanceCheckpoint()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 2000 },
            ReadResult = new UsnJournalReadResult
            {
                Success = false,
                ErrorReason = "Simulated Win32 error"
            }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 1000
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);

        // Checkpoint should NOT be advanced on read failure.
        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(1000L, checkpoint!.LastUsn);
    }

    // ── Changes on volume but none under root ───────────────────────────

    [Fact]
    public async Task ChangesOnVolume_NoneUnderRoot_ReturnsNoChanges()
    {
        var reader = new MockUsnJournalReader
        {
            JournalInfo = new UsnJournalInfo { JournalId = 1, FirstUsn = 0, NextUsn = 2000 },
            ReadResult = new UsnJournalReadResult
            {
                Success = true,
                NextUsn = 2000,
                ChangedPaths = [], // All filtered out by root prefix.
                UnresolvedCount = 0,
                Overflowed = false
            }
        };
        var checkpointRepo = new InMemoryUsnCheckpointRepository();
        await checkpointRepo.SaveCheckpointAsync(new UsnCheckpoint
        {
            VolumeId = VolumeId, JournalId = 1, LastUsn = 1000
        });

        var source = CreateSource(reader, checkpointRepo);
        var result = await source.DetectChangesAsync(TestRoot);

        Assert.False(result.HasChanges);
        Assert.False(result.RequiresFullRescan);
        Assert.Contains("no changes under", result.Reason, StringComparison.OrdinalIgnoreCase);

        // Checkpoint should still be advanced.
        var checkpoint = await checkpointRepo.GetCheckpointAsync(VolumeId);
        Assert.Equal(2000L, checkpoint!.LastUsn);
    }

    // ── Journal unavailable ─────────────────────────────────────────────

    [Fact]
    public async Task JournalUnavailable_DetectChanges_ReturnsFullRescan()
    {
        var reader = new MockUsnJournalReader { JournalInfo = null };
        var source = CreateSource(reader);

        var result = await source.DetectChangesAsync(TestRoot);

        Assert.True(result.RequiresFullRescan);
        Assert.Contains("inaccessible", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Fallback chain integration ──────────────────────────────────────

    [Fact]
    public async Task FallbackChain_UsnUnavailable_DetectorPicksWatcher()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"atlas-usn-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var usnSource = CreateSource(new NullUsnJournalReader());
            using var watcherSource = new FileSystemWatcherDeltaSource();
            var scheduledSource = new ScheduledRescanDeltaSource();

            var detector = new DeltaCapabilityDetector(new IDeltaSource[]
            {
                scheduledSource, watcherSource, usnSource
            });

            var best = await detector.DetectBestSourceAsync(tempRoot);
            Assert.NotNull(best);
            // USN is unavailable (null reader), so watcher should be selected.
            Assert.Equal(DeltaCapability.Watcher, best.Capability);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}

/// <summary>
/// Configurable mock USN journal reader for unit tests.
/// </summary>
internal sealed class MockUsnJournalReader : IUsnJournalReader
{
    public UsnJournalInfo? JournalInfo { get; set; }
    public UsnJournalReadResult? ReadResult { get; set; }

    public UsnJournalInfo? QueryJournal(string volumeRoot) => JournalInfo;

    public UsnJournalReadResult ReadChanges(
        string volumeRoot, long startUsn, ulong journalId,
        int maxChangedPaths, string rootPathFilter) =>
        ReadResult ?? new UsnJournalReadResult
        {
            Success = false,
            ErrorReason = "No read result configured."
        };
}
