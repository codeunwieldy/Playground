using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Core.Policies;
using Atlas.Core.Scanning;
using Atlas.Service.Services;
using Atlas.Service.Services.DeltaSources;
using Atlas.Storage;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for delta scanning and rescan orchestration (C-012).
/// </summary>
public sealed class DeltaScanningTests : IDisposable
{
    private readonly string _testRoot;
    private readonly DeltaTestDatabaseFixture _fixture;
    private readonly InventoryRepository _inventoryRepo;

    public DeltaScanningTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-delta-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _fixture = new DeltaTestDatabaseFixture();
        _inventoryRepo = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch { }
    }

    // ── Capability model tests ──────────────────────────────────────────────

    [Fact]
    public void DeltaCapability_UsnJournal_IsHighestPriority()
    {
        Assert.True(DeltaCapability.UsnJournal > DeltaCapability.Watcher);
        Assert.True(DeltaCapability.Watcher > DeltaCapability.ScheduledRescan);
        Assert.True(DeltaCapability.ScheduledRescan > DeltaCapability.None);
    }

    [Fact]
    public void DeltaResult_Defaults_AreReasonable()
    {
        var result = new DeltaResult();
        Assert.Equal(string.Empty, result.RootPath);
        Assert.Equal(DeltaCapability.None, result.Capability);
        Assert.False(result.HasChanges);
        Assert.Empty(result.ChangedPaths);
        Assert.False(result.RequiresFullRescan);
    }

    // ── USN journal source tests ────────────────────────────────────────────

    private static UsnJournalDeltaSource CreateUsnSource(
        IUsnJournalReader? reader = null,
        IUsnCheckpointRepository? checkpointRepo = null) =>
        new(
            reader ?? new NullUsnJournalReader(),
            checkpointRepo ?? new InMemoryUsnCheckpointRepository(),
            NullLogger<UsnJournalDeltaSource>.Instance);

    [Fact]
    public async Task UsnSource_NonExistentRoot_IsNotAvailable()
    {
        var source = CreateUsnSource();
        var available = await source.IsAvailableForRootAsync(@"Z:\nonexistent-test-path-abcdef");
        Assert.False(available);
    }

    [Fact]
    public async Task UsnSource_DetectChanges_WithUnavailableJournal_ReturnsFullRescan()
    {
        var source = CreateUsnSource();
        var result = await source.DetectChangesAsync(_testRoot);

        Assert.Equal(DeltaCapability.UsnJournal, result.Capability);
        Assert.True(result.HasChanges);
        Assert.True(result.RequiresFullRescan);
        Assert.Equal(_testRoot, result.RootPath);
    }

    [Fact]
    public void UsnSource_Capability_IsUsnJournal()
    {
        var source = CreateUsnSource();
        Assert.Equal(DeltaCapability.UsnJournal, source.Capability);
    }

    // ── Watcher source tests ────────────────────────────────────────────────

    [Fact]
    public async Task WatcherSource_NonExistentRoot_IsNotAvailable()
    {
        using var source = new FileSystemWatcherDeltaSource();
        var available = await source.IsAvailableForRootAsync(@"Z:\nonexistent-test-path-abcdef");
        Assert.False(available);
    }

    [Fact]
    public async Task WatcherSource_ExistingLocalRoot_IsAvailable()
    {
        using var source = new FileSystemWatcherDeltaSource();
        var available = await source.IsAvailableForRootAsync(_testRoot);
        Assert.True(available);
    }

    [Fact]
    public async Task WatcherSource_FirstDetection_RequiresFullRescan()
    {
        using var source = new FileSystemWatcherDeltaSource();
        var result = await source.DetectChangesAsync(_testRoot);

        Assert.Equal(DeltaCapability.Watcher, result.Capability);
        Assert.True(result.HasChanges);
        Assert.True(result.RequiresFullRescan);
        Assert.Contains("initial", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WatcherSource_SecondDetection_NoChangeIfQuiet()
    {
        using var source = new FileSystemWatcherDeltaSource();

        // First call: watcher starts, full rescan needed.
        await source.DetectChangesAsync(_testRoot);

        // Brief pause to let the watcher settle (no file changes happening).
        await Task.Delay(100);

        // Second call: watcher is running and nothing changed.
        var result = await source.DetectChangesAsync(_testRoot);

        Assert.Equal(DeltaCapability.Watcher, result.Capability);
        Assert.False(result.HasChanges);
        Assert.False(result.RequiresFullRescan);
    }

    [Fact]
    public async Task WatcherSource_DetectsFileCreation()
    {
        using var source = new FileSystemWatcherDeltaSource();

        // First call: start watcher.
        await source.DetectChangesAsync(_testRoot);
        await Task.Delay(100);

        // Create a file while watcher is running.
        var testFile = Path.Combine(_testRoot, "watcher-test.txt");
        await File.WriteAllTextAsync(testFile, "test content");
        await Task.Delay(500); // Allow watcher to fire.

        var result = await source.DetectChangesAsync(_testRoot);

        Assert.True(result.HasChanges);
        Assert.False(result.RequiresFullRescan);
        Assert.Contains(result.ChangedPaths, p => p.Contains("watcher-test.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WatcherSource_Capability_IsWatcher()
    {
        using var source = new FileSystemWatcherDeltaSource();
        Assert.Equal(DeltaCapability.Watcher, source.Capability);
    }

    // ── Scheduled rescan source tests ───────────────────────────────────────

    [Fact]
    public async Task ScheduledRescan_ExistingRoot_IsAvailable()
    {
        var source = new ScheduledRescanDeltaSource();
        var available = await source.IsAvailableForRootAsync(_testRoot);
        Assert.True(available);
    }

    [Fact]
    public async Task ScheduledRescan_NonExistentRoot_IsNotAvailable()
    {
        var source = new ScheduledRescanDeltaSource();
        var available = await source.IsAvailableForRootAsync(@"Z:\nonexistent-test-path-abcdef");
        Assert.False(available);
    }

    [Fact]
    public async Task ScheduledRescan_AlwaysReturnsFullRescan()
    {
        var source = new ScheduledRescanDeltaSource();
        var result = await source.DetectChangesAsync(_testRoot);

        Assert.Equal(DeltaCapability.ScheduledRescan, result.Capability);
        Assert.True(result.HasChanges);
        Assert.True(result.RequiresFullRescan);
    }

    [Fact]
    public void ScheduledRescan_Capability_IsScheduledRescan()
    {
        var source = new ScheduledRescanDeltaSource();
        Assert.Equal(DeltaCapability.ScheduledRescan, source.Capability);
    }

    // ── Capability detector tests ───────────────────────────────────────────

    [Fact]
    public async Task Detector_NoSources_ReturnsNull()
    {
        var detector = new DeltaCapabilityDetector([]);
        var best = await detector.DetectBestSourceAsync(_testRoot);
        Assert.Null(best);
    }

    [Fact]
    public async Task Detector_PrefersBestAvailableSource()
    {
        // All three sources should be available for a local temp dir.
        // USN source uses null reader (journal unavailable), so detector falls to watcher.
        var sources = new IDeltaSource[]
        {
            new ScheduledRescanDeltaSource(),
            new FileSystemWatcherDeltaSource(),
            CreateUsnSource()
        };

        var detector = new DeltaCapabilityDetector(sources);
        var best = await detector.DetectBestSourceAsync(_testRoot);

        Assert.NotNull(best);
        // On NTFS, USN should be preferred; on non-NTFS, watcher.
        Assert.True(best.Capability >= DeltaCapability.Watcher);
    }

    [Fact]
    public async Task Detector_FallsBackWhenHigherSourceUnavailable()
    {
        // Only provide scheduled rescan source.
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);
        var best = await detector.DetectBestSourceAsync(_testRoot);

        Assert.NotNull(best);
        Assert.Equal(DeltaCapability.ScheduledRescan, best.Capability);
    }

    [Fact]
    public async Task Detector_ProbeReport_IncludesAllAvailableCapabilities()
    {
        var sources = new IDeltaSource[]
        {
            new ScheduledRescanDeltaSource(),
            new FileSystemWatcherDeltaSource()
        };

        var detector = new DeltaCapabilityDetector(sources);
        var report = await detector.ProbeRootAsync(_testRoot);

        Assert.Equal(_testRoot, report.RootPath);
        Assert.True(report.BestCapability >= DeltaCapability.Watcher);
        Assert.Contains(DeltaCapability.ScheduledRescan, report.AvailableCapabilities);
    }

    [Fact]
    public async Task Detector_NonExistentRoot_ReturnsNone()
    {
        var sources = new IDeltaSource[]
        {
            new ScheduledRescanDeltaSource(),
            new FileSystemWatcherDeltaSource(),
            CreateUsnSource()
        };

        var detector = new DeltaCapabilityDetector(sources);
        var report = await detector.ProbeRootAsync(@"Z:\nonexistent-test-path-abcdef");

        Assert.Equal(DeltaCapability.None, report.BestCapability);
        Assert.Empty(report.AvailableCapabilities);
    }

    // ── Orchestration worker tests ──────────────────────────────────────────

    [Fact]
    public async Task Orchestration_DisabledByDefault_DoesNotRun()
    {
        var opts = Options.Create(new AtlasServiceOptions { EnableRescanOrchestration = false });
        var profile = new PolicyProfile { ScanRoots = [_testRoot] };
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);

        var worker = new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            opts, profile,
            new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);

        // ExecuteAsync should return immediately when disabled.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // No sessions should have been persisted.
        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Orchestration_Cycle_PersistsSession()
    {
        // Create a test file so the scanner has something to find.
        var testFile = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(testFile, "hello");

        var opts = Options.Create(new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0),
            MaxRootsPerCycle = 5
        });

        var profile = new PolicyProfile { ScanRoots = [_testRoot] };
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);

        var worker = new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            opts, profile,
            new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);

        // Run a single orchestration cycle directly (bypasses the timer loop).
        await worker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Single(sessions);
        Assert.True(sessions[0].FilesScanned > 0);
    }

    [Fact]
    public async Task Orchestration_RespectsRescanInterval()
    {
        var testFile = Path.Combine(_testRoot, "interval-test.txt");
        await File.WriteAllTextAsync(testFile, "hello");

        var opts = Options.Create(new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromHours(1), // Very long interval.
            MaxRootsPerCycle = 5
        });

        var profile = new PolicyProfile { ScanRoots = [_testRoot] };
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);

        var worker = new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            opts, profile,
            new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);

        // First cycle should rescan.
        await worker.RunOrchestrationCycleAsync(CancellationToken.None);
        // Second cycle should skip (interval not elapsed).
        await worker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Single(sessions);
    }

    [Fact]
    public async Task Orchestration_RespectsMaxRootsPerCycle()
    {
        // Create multiple roots.
        var roots = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var root = Path.Combine(_testRoot, $"root{i}");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "file.txt"), $"data{i}");
            roots.Add(root);
        }

        var opts = Options.Create(new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0),
            MaxRootsPerCycle = 2 // Only 2 roots per cycle.
        });

        var profile = new PolicyProfile { ScanRoots = roots };
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);

        var worker = new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            opts, profile,
            new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);

        await worker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Equal(2, sessions.Count);
    }

    [Fact]
    public async Task Orchestration_NoRoots_DoesNothing()
    {
        var opts = Options.Create(new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0)
        });

        var profile = new PolicyProfile(); // Empty ScanRoots and MutableRoots.
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);

        var worker = new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            opts, profile,
            new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);

        await worker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Orchestration_UnresolvableRoot_SkipsGracefully()
    {
        var opts = Options.Create(new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0)
        });

        // Use a non-existent root — no delta source will claim it.
        var profile = new PolicyProfile { ScanRoots = [@"Z:\nonexistent-test-path-abcdef"] };
        var detector = new DeltaCapabilityDetector([new ScheduledRescanDeltaSource()]);

        var worker = new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            opts, profile,
            new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);

        await worker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Empty(sessions);
    }

    // ── Service options tests ───────────────────────────────────────────────

    [Fact]
    public void ServiceOptions_RescanDefaults_AreReasonable()
    {
        var opts = new AtlasServiceOptions();
        Assert.False(opts.EnableRescanOrchestration);
        Assert.Equal(TimeSpan.FromMinutes(30), opts.RescanInterval);
        Assert.Equal(5, opts.MaxRootsPerCycle);
        Assert.Equal(TimeSpan.FromMinutes(5), opts.OrchestrationCooldown);
    }

    [Fact]
    public void ServiceOptions_MaxIncrementalPaths_HasReasonableDefault()
    {
        var opts = new AtlasServiceOptions();
        Assert.Equal(500, opts.MaxIncrementalPaths);
    }

    // ── Incremental composition tests (C-017) ──────────────────────────────

    private RescanOrchestrationWorker CreateWorker(
        IDeltaSource source, AtlasServiceOptions? opts = null, PolicyProfile? prof = null)
    {
        var o = Options.Create(opts ?? new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0),
            MaxRootsPerCycle = 5
        });
        var p = prof ?? new PolicyProfile { ScanRoots = [_testRoot] };
        var detector = new DeltaCapabilityDetector([source]);
        return new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            o, p, new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);
    }

    private RescanOrchestrationWorker CreateWorkerWithProfile(
        IDeltaSource source, PolicyProfile profile, AtlasServiceOptions? opts = null)
    {
        var o = Options.Create(opts ?? new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0),
            MaxRootsPerCycle = 5
        });
        var detector = new DeltaCapabilityDetector([source]);
        return new RescanOrchestrationWorker(
            NullLogger<RescanOrchestrationWorker>.Instance,
            o, profile, new FileScanner(new PathSafetyClassifier()),
            detector, _inventoryRepo);
    }

    [Fact]
    public async Task Orchestration_IncrementalComposition_WhenBoundedDelta()
    {
        // Create baseline files.
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "a.txt"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "b.txt"), "bbb");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "c.txt"), "ccc");

        // First cycle: full rescan creates baseline.
        var fullWorker = CreateWorker(new ScheduledRescanDeltaSource());
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var baselineSessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Single(baselineSessions);
        var baselineId = baselineSessions[0].SessionId;

        // Modify a file.
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "b.txt"), "bbb-modified");

        // Second cycle: incremental composition.
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [Path.Combine(_testRoot, "b.txt")],
            Reason = "1 changed path detected."
        }};
        var incWorker = CreateWorker(stub);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Equal(2, sessions.Count);

        var latest = sessions[0]; // Most recent first.
        Assert.Equal("IncrementalComposition", latest.BuildMode);
        Assert.Equal(baselineId, latest.BaselineSessionId);
        Assert.True(latest.IsTrusted);
        Assert.Contains("Watcher", latest.DeltaSource);
    }

    [Fact]
    public async Task Orchestration_IncrementalComposition_SetsCorrectFileCount()
    {
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "a.txt"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "b.txt"), "bbb");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "c.txt"), "ccc");

        // Baseline.
        var fullWorker = CreateWorker(new ScheduledRescanDeltaSource());
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Add a new file.
        var newFile = Path.Combine(_testRoot, "d.txt");
        await File.WriteAllTextAsync(newFile, "ddd");

        // Incremental with the new file as a changed path.
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [newFile],
            Reason = "1 new file."
        }};
        var incWorker = CreateWorker(stub);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var latest = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        Assert.Equal("IncrementalComposition", latest.BuildMode);
        Assert.Equal(4, latest.FilesScanned);
    }

    [Fact]
    public async Task Orchestration_IncrementalComposition_HandlesDeletedFiles()
    {
        var fileA = Path.Combine(_testRoot, "a.txt");
        var fileB = Path.Combine(_testRoot, "b.txt");
        await File.WriteAllTextAsync(fileA, "aaa");
        await File.WriteAllTextAsync(fileB, "bbb");

        // Baseline.
        var fullWorker = CreateWorker(new ScheduledRescanDeltaSource());
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Delete b.txt.
        File.Delete(fileB);

        // Incremental with b.txt as changed path (InspectFile returns null → removed).
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [fileB],
            Reason = "1 deleted file."
        }};
        var incWorker = CreateWorker(stub);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var latest = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        Assert.Equal("IncrementalComposition", latest.BuildMode);
        Assert.Equal(1, latest.FilesScanned);
    }

    [Fact]
    public async Task Orchestration_FallsBackToFullRescan_WhenNoBaseline()
    {
        var fileA = Path.Combine(_testRoot, "a.txt");
        await File.WriteAllTextAsync(fileA, "aaa");

        // No baseline exists. Attempt incremental.
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [fileA],
            Reason = "1 changed path."
        }};
        var incWorker = CreateWorker(stub);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Single(sessions);
        Assert.Equal("FullRescan", sessions[0].BuildMode);
        Assert.Contains("baseline", sessions[0].CompositionNote, StringComparison.OrdinalIgnoreCase);
        Assert.True(sessions[0].FilesScanned > 0);
    }

    [Fact]
    public async Task Orchestration_FallsBackToFullRescan_WhenDeltaExceedsMaxPaths()
    {
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "a.txt"), "aaa");

        // Create baseline.
        var fullWorker = CreateWorker(new ScheduledRescanDeltaSource());
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Attempt incremental with 3 paths but MaxIncrementalPaths=2.
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [
                Path.Combine(_testRoot, "x.txt"),
                Path.Combine(_testRoot, "y.txt"),
                Path.Combine(_testRoot, "z.txt")
            ],
            Reason = "3 changed paths."
        }};
        var incWorker = CreateWorker(stub, new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0),
            MaxRootsPerCycle = 5,
            MaxIncrementalPaths = 2
        });
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Equal(2, sessions.Count);
        var latest = sessions[0];
        Assert.Equal("FullRescan", latest.BuildMode);
        Assert.Contains("MaxIncrementalPaths", latest.CompositionNote);
    }

    // ── Untrusted session / degradation tests (C-018) ────────────────────

    [Fact]
    public async Task Orchestration_TrustedIncrementalSession_WhenAllPathsResolved()
    {
        // Create baseline files.
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "a.txt"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "b.txt"), "bbb");

        // Full rescan to create baseline.
        var fullWorker = CreateWorker(new ScheduledRescanDeltaSource());
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Modify a file.
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "b.txt"), "bbb-modified");

        // Incremental composition with all paths resolvable.
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [Path.Combine(_testRoot, "b.txt")],
            Reason = "1 changed path."
        }};
        var incWorker = CreateWorker(stub);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var latest = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        Assert.Equal("IncrementalComposition", latest.BuildMode);
        Assert.True(latest.IsTrusted);
        Assert.DoesNotContain("DEGRADED", latest.CompositionNote);
    }

    [Fact]
    public async Task Orchestration_DegradedSession_WhenSomePathsCannotBeInspected()
    {
        // Create baseline files — both scannable initially.
        var fileA = Path.Combine(_testRoot, "a.txt");
        var protectedDir = Path.Combine(_testRoot, "protected");
        Directory.CreateDirectory(protectedDir);
        var fileB = Path.Combine(protectedDir, "b.txt");
        await File.WriteAllTextAsync(fileA, "aaa");
        await File.WriteAllTextAsync(fileB, "bbb");

        // Full rescan baseline (no protection yet, so both files are scanned).
        var baseProfile = new PolicyProfile { ScanRoots = [_testRoot] };
        var fullWorker = CreateWorkerWithProfile(new ScheduledRescanDeltaSource(), baseProfile);
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var baseline = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        Assert.Equal(2, baseline.FilesScanned);

        // Now protect the directory containing b.txt.
        // InspectFile will return null for b.txt even though it exists on disk.
        var protectedProfile = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            ProtectedPaths = [protectedDir]
        };

        // Delta says both files changed. a.txt will resolve, b.txt will fail (protected).
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [fileA, fileB],
            Reason = "2 changed paths."
        }};
        var incWorker = CreateWorkerWithProfile(stub, protectedProfile);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var latest = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        Assert.Equal("IncrementalComposition", latest.BuildMode);
        Assert.False(latest.IsTrusted);
        Assert.Contains("DEGRADED", latest.CompositionNote);
        Assert.Contains("could not be refreshed", latest.CompositionNote);
        Assert.Contains("follow-up full rescan", latest.CompositionNote);
    }

    [Fact]
    public async Task Orchestration_DegradedNote_RoundTripsThrough_SessionDetailApi()
    {
        // Same setup as degraded test above.
        var fileA = Path.Combine(_testRoot, "a.txt");
        var protectedDir = Path.Combine(_testRoot, "protected");
        Directory.CreateDirectory(protectedDir);
        var fileB = Path.Combine(protectedDir, "b.txt");
        await File.WriteAllTextAsync(fileA, "aaa");
        await File.WriteAllTextAsync(fileB, "bbb");

        // Baseline.
        var baseProfile = new PolicyProfile { ScanRoots = [_testRoot] };
        var fullWorker = CreateWorkerWithProfile(new ScheduledRescanDeltaSource(), baseProfile);
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Protect and compose degraded session.
        var protectedProfile = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            ProtectedPaths = [protectedDir]
        };
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [fileA, fileB],
            Reason = "2 changed paths."
        }};
        var incWorker = CreateWorkerWithProfile(stub, protectedProfile);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Verify we can read the session back and provenance is intact.
        var latest = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        var detail = await _inventoryRepo.GetSessionAsync(latest.SessionId);
        Assert.NotNull(detail);
        Assert.False(detail.IsTrusted);
        Assert.Equal("IncrementalComposition", detail.BuildMode);
        Assert.Contains("DEGRADED", detail.CompositionNote);
        Assert.NotEmpty(detail.BaselineSessionId);
    }

    [Fact]
    public async Task Orchestration_ForcesFullRescan_WhenTooManyPathsFail()
    {
        // Create baseline with one file.
        var fileA = Path.Combine(_testRoot, "a.txt");
        await File.WriteAllTextAsync(fileA, "aaa");

        var fullWorker = CreateWorker(new ScheduledRescanDeltaSource());
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        // Create files in a protected directory — these exist on disk but
        // InspectFile returns null.
        var protectedDir = Path.Combine(_testRoot, "locked");
        Directory.CreateDirectory(protectedDir);
        var protectedFile1 = Path.Combine(protectedDir, "x.txt");
        var protectedFile2 = Path.Combine(protectedDir, "y.txt");
        var protectedFile3 = Path.Combine(protectedDir, "z.txt");
        await File.WriteAllTextAsync(protectedFile1, "xxx");
        await File.WriteAllTextAsync(protectedFile2, "yyy");
        await File.WriteAllTextAsync(protectedFile3, "zzz");

        // Delta is 4 paths: 1 resolvable (a.txt) + 3 protected (75% failure rate).
        // MaxDegradedRatio=0.5 → 75% > 50%, so it should force full rescan.
        var protectedProfile = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            ProtectedPaths = [protectedDir]
        };
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.Watcher,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [fileA, protectedFile1, protectedFile2, protectedFile3],
            Reason = "4 changed paths."
        }};
        var incWorker = CreateWorkerWithProfile(stub, protectedProfile, new AtlasServiceOptions
        {
            EnableRescanOrchestration = true,
            RescanInterval = TimeSpan.FromSeconds(0),
            MaxRootsPerCycle = 5,
            MaxDegradedRatio = 0.5
        });
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var sessions = await _inventoryRepo.ListSessionsAsync(10, 0);
        Assert.Equal(2, sessions.Count);
        var latest = sessions[0];
        Assert.Equal("FullRescan", latest.BuildMode);
        Assert.True(latest.IsTrusted);
        Assert.Contains("Degraded composition abandoned", latest.CompositionNote);
    }

    [Fact]
    public async Task Orchestration_BaselineLinkageTruthful_InDegradedCase()
    {
        // Baseline with two files.
        var fileA = Path.Combine(_testRoot, "a.txt");
        var protectedDir = Path.Combine(_testRoot, "protected");
        Directory.CreateDirectory(protectedDir);
        var fileB = Path.Combine(protectedDir, "b.txt");
        await File.WriteAllTextAsync(fileA, "aaa");
        await File.WriteAllTextAsync(fileB, "bbb");

        var baseProfile = new PolicyProfile { ScanRoots = [_testRoot] };
        var fullWorker = CreateWorkerWithProfile(new ScheduledRescanDeltaSource(), baseProfile);
        await fullWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var baseline = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];

        // Protect b.txt's directory and compose a degraded session.
        var protectedProfile = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            ProtectedPaths = [protectedDir]
        };
        var stub = new StubDeltaSource { NextResult = new DeltaResult
        {
            Capability = DeltaCapability.UsnJournal,
            HasChanges = true,
            RequiresFullRescan = false,
            ChangedPaths = [fileA, fileB],
            Reason = "2 changed paths."
        }};
        var incWorker = CreateWorkerWithProfile(stub, protectedProfile);
        await incWorker.RunOrchestrationCycleAsync(CancellationToken.None);

        var latest = (await _inventoryRepo.ListSessionsAsync(1, 0))[0];
        Assert.False(latest.IsTrusted);
        Assert.Equal(baseline.SessionId, latest.BaselineSessionId);
        Assert.Equal("UsnJournal", latest.DeltaSource);
    }

    [Fact]
    public void ServiceOptions_MaxDegradedRatio_HasReasonableDefault()
    {
        var opts = new AtlasServiceOptions();
        Assert.Equal(0.5, opts.MaxDegradedRatio);
    }

    // ── Trust-aware plan gating tests (C-019) ───────────────────────────

    private PlanExecutionService CreateExecutionService(IInventoryRepository inventoryRepo)
    {
        var opts = Options.Create(new AtlasServiceOptions
        {
            QuarantineFolderName = ".atlas-quarantine-test"
        });
        return new PlanExecutionService(
            new AtlasPolicyEngine(),
            new RollbackPlanner(),
            opts,
            inventoryRepo);
    }

    [Fact]
    public async Task Execution_LiveBlocked_WhenLatestSessionDegraded()
    {
        // Seed a degraded session.
        await _inventoryRepo.SaveSessionAsync(new ScanSession
        {
            Roots = [_testRoot],
            IsTrusted = false,
            BuildMode = "IncrementalComposition",
            CompositionNote = "DEGRADED: test scenario"
        });

        var service = CreateExecutionService(_inventoryRepo);
        var src = Path.Combine(_testRoot, "block-test.txt");
        await File.WriteAllTextAsync(src, "content");

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = new PolicyProfile { MutableRoots = [_testRoot], ScanRoots = [_testRoot] },
            Batch = new ExecutionBatch
            {
                PlanId = "plan-trust-block",
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.CreateDirectory,
                        DestinationPath = Path.Combine(_testRoot, "new-dir")
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Contains(response.Messages, m => m.Contains("Execution blocked") && m.Contains("IsTrusted=false"));
    }

    [Fact]
    public async Task Execution_PreviewAvailable_WhenLatestSessionDegraded()
    {
        // Seed a degraded session.
        await _inventoryRepo.SaveSessionAsync(new ScanSession
        {
            Roots = [_testRoot],
            IsTrusted = false,
            BuildMode = "IncrementalComposition",
            CompositionNote = "DEGRADED: test scenario"
        });

        var service = CreateExecutionService(_inventoryRepo);

        var request = new ExecutionRequest
        {
            Execute = false, // dry-run / preview
            PolicyProfile = new PolicyProfile { MutableRoots = [_testRoot], ScanRoots = [_testRoot] },
            Batch = new ExecutionBatch
            {
                PlanId = "plan-trust-preview",
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.CreateDirectory,
                        DestinationPath = Path.Combine(_testRoot, "preview-dir")
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Contains(response.Messages, m => m.Contains("Dry run"));
    }

    [Fact]
    public async Task Execution_LiveProceeds_WhenLatestSessionTrusted()
    {
        // Seed a trusted session.
        await _inventoryRepo.SaveSessionAsync(new ScanSession
        {
            Roots = [_testRoot],
            IsTrusted = true,
            BuildMode = "FullRescan"
        });

        var service = CreateExecutionService(_inventoryRepo);

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = new PolicyProfile { MutableRoots = [_testRoot], ScanRoots = [_testRoot] },
            Batch = new ExecutionBatch
            {
                PlanId = "plan-trust-ok",
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.CreateDirectory,
                        DestinationPath = Path.Combine(_testRoot, "trusted-dir")
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(Directory.Exists(Path.Combine(_testRoot, "trusted-dir")));
    }

    [Fact]
    public async Task Execution_BlockedReason_IsStableAndTruthful()
    {
        await _inventoryRepo.SaveSessionAsync(new ScanSession
        {
            Roots = [_testRoot],
            IsTrusted = false,
            BuildMode = "IncrementalComposition",
            CompositionNote = "DEGRADED: 1 path failed"
        });

        var service = CreateExecutionService(_inventoryRepo);

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = new PolicyProfile { MutableRoots = [_testRoot], ScanRoots = [_testRoot] },
            Batch = new ExecutionBatch
            {
                PlanId = "plan-reason",
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.CreateDirectory,
                        DestinationPath = Path.Combine(_testRoot, "reason-dir")
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Single(response.Messages);
        Assert.Contains("full rescan", response.Messages[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preview/dry-run remains available", response.Messages[0]);
    }

    [Fact]
    public async Task Execution_LiveProceeds_WhenNoSessionExists()
    {
        // No sessions seeded — empty inventory.
        var service = CreateExecutionService(_inventoryRepo);

        var request = new ExecutionRequest
        {
            Execute = true,
            PolicyProfile = new PolicyProfile { MutableRoots = [_testRoot], ScanRoots = [_testRoot] },
            Batch = new ExecutionBatch
            {
                PlanId = "plan-no-session",
                Operations =
                [
                    new PlanOperation
                    {
                        Kind = OperationKind.CreateDirectory,
                        DestinationPath = Path.Combine(_testRoot, "no-session-dir")
                    }
                ]
            }
        };

        var response = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(Directory.Exists(Path.Combine(_testRoot, "no-session-dir")));
    }
}

/// <summary>
/// Minimal in-process test database fixture reused across delta scanning tests.
/// </summary>
internal sealed class DeltaTestDatabaseFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public DeltaTestDatabaseFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_delta_test_{Guid.NewGuid():N}.db");
        var options = new StorageOptions
        {
            DataRoot = Path.GetDirectoryName(DatabasePath)!,
            DatabaseFileName = Path.GetFileName(DatabasePath)
        };
        var bootstrapper = new AtlasDatabaseBootstrapper(options);
        bootstrapper.InitializeAsync().GetAwaiter().GetResult();
        ConnectionFactory = new SqliteConnectionFactory(bootstrapper);
    }

    public void Dispose()
    {
        try { File.Delete(DatabasePath); } catch { }
    }
}

/// <summary>
/// Stub reader that simulates USN journal being inaccessible.
/// Used by existing tests that test the source's probe/fallback behavior.
/// </summary>
internal sealed class NullUsnJournalReader : IUsnJournalReader
{
    public UsnJournalInfo? QueryJournal(string volumeRoot) => null;

    public UsnJournalReadResult ReadChanges(
        string volumeRoot, long startUsn, ulong journalId,
        int maxChangedPaths, string rootPathFilter) =>
        new() { Success = false, ErrorReason = "Test stub: journal not available." };
}

/// <summary>
/// In-memory checkpoint repository for unit tests.
/// </summary>
internal sealed class InMemoryUsnCheckpointRepository : IUsnCheckpointRepository
{
    private readonly Dictionary<string, UsnCheckpoint> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task<UsnCheckpoint?> GetCheckpointAsync(string volumeId, CancellationToken ct = default)
    {
        _store.TryGetValue(volumeId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task SaveCheckpointAsync(UsnCheckpoint checkpoint, CancellationToken ct = default)
    {
        _store[checkpoint.VolumeId] = checkpoint;
        return Task.CompletedTask;
    }

    public Task DeleteCheckpointAsync(string volumeId, CancellationToken ct = default)
    {
        _store.Remove(volumeId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Configurable stub delta source for testing incremental composition.
/// Always reports as available for existing directories.
/// </summary>
internal sealed class StubDeltaSource : IDeltaSource
{
    public DeltaCapability Capability => NextResult.Capability;

    public DeltaResult NextResult { get; set; } = new()
    {
        Capability = DeltaCapability.Watcher,
        HasChanges = true,
        RequiresFullRescan = true,
        Reason = "Test stub default"
    };

    public Task<bool> IsAvailableForRootAsync(string rootPath, CancellationToken ct = default) =>
        Task.FromResult(Directory.Exists(rootPath));

    public Task<DeltaResult> DetectChangesAsync(string rootPath, CancellationToken ct = default) =>
        Task.FromResult(new DeltaResult
        {
            RootPath = rootPath,
            Capability = NextResult.Capability,
            HasChanges = NextResult.HasChanges,
            ChangedPaths = NextResult.ChangedPaths,
            RequiresFullRescan = NextResult.RequiresFullRescan,
            Reason = NextResult.Reason
        });
}
