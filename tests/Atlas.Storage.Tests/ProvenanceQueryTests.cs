using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Tests for the incremental provenance query layer (C-016).
/// Validates that provenance metadata (trigger, build mode, delta source,
/// baseline linkage, trust, and composition note) round-trips through the
/// inventory repository and maps correctly to the pipe contract DTOs.
/// </summary>
public sealed class ProvenanceQueryTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repo;

    public ProvenanceQueryTests()
    {
        _fixture = new TestDatabaseFixture();
        _repo = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    private static ScanSession CreateTestSession(
        string trigger = "Manual",
        string buildMode = "FullRescan",
        string deltaSource = "",
        string baselineSessionId = "",
        bool isTrusted = true,
        string compositionNote = "",
        int fileCount = 2)
    {
        return new ScanSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            DuplicateGroupCount = 1,
            Roots = ["C:\\Root0"],
            Volumes =
            [
                new VolumeSnapshot
                {
                    RootPath = "C:\\",
                    DriveFormat = "NTFS",
                    DriveType = "Fixed",
                    IsReady = true,
                    TotalSizeBytes = 500_000_000_000L,
                    FreeSpaceBytes = 200_000_000_000L
                }
            ],
            Files = Enumerable.Range(0, fileCount).Select(i => new FileInventoryItem
            {
                Path = $"C:\\Root0\\file{i}.txt",
                Name = $"file{i}.txt",
                Extension = ".txt",
                Category = "Documents",
                SizeBytes = 1024 * (i + 1),
                LastModifiedUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - i * 3600,
                Sensitivity = SensitivityLevel.Low,
                IsSyncManaged = false,
                IsDuplicateCandidate = false
            }).ToList(),
            Trigger = trigger,
            BuildMode = buildMode,
            DeltaSource = deltaSource,
            BaselineSessionId = baselineSessionId,
            IsTrusted = isTrusted,
            CompositionNote = compositionNote
        };
    }

    // ── Snapshot response includes provenance ────────────────────────────────

    [Fact]
    public async Task Snapshot_IncludesProvenance_ForLatestSession()
    {
        var session = CreateTestSession(
            trigger: "Orchestration",
            buildMode: "IncrementalComposition",
            deltaSource: "UsnJournal",
            baselineSessionId: "abc123",
            isTrusted: true,
            compositionNote: "Composed from USN delta");
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);

        // Mirror the handler projection to InventorySnapshotResponse
        var response = new InventorySnapshotResponse
        {
            HasSession = true,
            SessionId = latest.SessionId,
            FilesScanned = latest.FilesScanned,
            CreatedUtc = latest.CreatedUtc.ToString("o"),
            Trigger = latest.Trigger,
            BuildMode = latest.BuildMode,
            DeltaSource = latest.DeltaSource,
            BaselineSessionId = latest.BaselineSessionId,
            IsTrusted = latest.IsTrusted,
            CompositionNote = latest.CompositionNote
        };

        Assert.True(response.HasSession);
        Assert.Equal("Orchestration", response.Trigger);
        Assert.Equal("IncrementalComposition", response.BuildMode);
        Assert.Equal("UsnJournal", response.DeltaSource);
        Assert.Equal("abc123", response.BaselineSessionId);
        Assert.True(response.IsTrusted);
        Assert.Equal("Composed from USN delta", response.CompositionNote);
    }

    // ── Session list returns provenance summary data ────────────────────────

    [Fact]
    public async Task SessionList_ReturnsProvenanceSummary()
    {
        var manual = CreateTestSession(trigger: "Manual", buildMode: "FullRescan");
        manual.CreatedUtc = DateTime.UtcNow.AddMinutes(-10);
        await _repo.SaveSessionAsync(manual);

        var orchestrated = CreateTestSession(trigger: "Orchestration", buildMode: "IncrementalComposition", deltaSource: "Watcher");
        orchestrated.CreatedUtc = DateTime.UtcNow;
        await _repo.SaveSessionAsync(orchestrated);

        var sessions = await _repo.ListSessionsAsync(10, 0);
        Assert.True(sessions.Count >= 2);

        // Most recent first
        var dto0 = new InventorySessionSummary
        {
            SessionId = sessions[0].SessionId,
            Trigger = sessions[0].Trigger,
            BuildMode = sessions[0].BuildMode,
            DeltaSource = sessions[0].DeltaSource
        };
        var dto1 = new InventorySessionSummary
        {
            SessionId = sessions[1].SessionId,
            Trigger = sessions[1].Trigger,
            BuildMode = sessions[1].BuildMode,
            DeltaSource = sessions[1].DeltaSource
        };

        Assert.Equal("Orchestration", dto0.Trigger);
        Assert.Equal("IncrementalComposition", dto0.BuildMode);
        Assert.Equal("Watcher", dto0.DeltaSource);

        Assert.Equal("Manual", dto1.Trigger);
        Assert.Equal("FullRescan", dto1.BuildMode);
    }

    // ── Session detail returns baseline lineage ─────────────────────────────

    [Fact]
    public async Task SessionDetail_ReturnsBaselineLineage_WhenCompositionUsedOne()
    {
        var baseline = CreateTestSession(trigger: "Manual", buildMode: "FullRescan");
        await _repo.SaveSessionAsync(baseline);

        var composed = CreateTestSession(
            trigger: "Orchestration",
            buildMode: "IncrementalComposition",
            deltaSource: "UsnJournal",
            baselineSessionId: baseline.SessionId,
            compositionNote: "Composed from baseline");
        await _repo.SaveSessionAsync(composed);

        var detail = await _repo.GetSessionAsync(composed.SessionId);
        Assert.NotNull(detail);
        Assert.Equal(baseline.SessionId, detail.BaselineSessionId);
        Assert.Equal("IncrementalComposition", detail.BuildMode);
        Assert.Equal("Composed from baseline", detail.CompositionNote);

        // Mirror handler projection to InventorySessionDetailResponse
        var response = new InventorySessionDetailResponse
        {
            Found = true,
            SessionId = detail.SessionId,
            BaselineSessionId = detail.BaselineSessionId,
            BuildMode = detail.BuildMode,
            Trigger = detail.Trigger,
            DeltaSource = detail.DeltaSource,
            IsTrusted = detail.IsTrusted,
            CompositionNote = detail.CompositionNote
        };

        Assert.Equal(baseline.SessionId, response.BaselineSessionId);
        Assert.Equal("IncrementalComposition", response.BuildMode);
    }

    // ── Full-rescan sessions report clear non-incremental provenance ────────

    [Fact]
    public async Task FullRescanSession_ReportsClearNonIncrementalProvenance()
    {
        var session = CreateTestSession(
            trigger: "Manual",
            buildMode: "FullRescan",
            deltaSource: "",
            baselineSessionId: "",
            isTrusted: true);
        await _repo.SaveSessionAsync(session);

        var loaded = await _repo.GetSessionAsync(session.SessionId);
        Assert.NotNull(loaded);
        Assert.Equal("Manual", loaded.Trigger);
        Assert.Equal("FullRescan", loaded.BuildMode);
        Assert.Equal(string.Empty, loaded.DeltaSource);
        Assert.Equal(string.Empty, loaded.BaselineSessionId);
        Assert.True(loaded.IsTrusted);
        Assert.Equal(string.Empty, loaded.CompositionNote);
    }

    // ── Missing or empty sessions return clean typed responses ───────────────

    [Fact]
    public async Task MissingSession_ReturnsNull()
    {
        var result = await _repo.GetSessionAsync("nonexistent_provenance");
        Assert.Null(result);
    }

    [Fact]
    public async Task EmptyDatabase_SnapshotReturnsNull()
    {
        var latest = await _repo.GetLatestSessionAsync();
        Assert.Null(latest);

        // Mirror handler logic
        var response = latest is null
            ? new InventorySnapshotResponse { HasSession = false }
            : new InventorySnapshotResponse { HasSession = true };
        Assert.False(response.HasSession);
    }

    [Fact]
    public async Task EmptyDatabase_SessionList_ReturnsEmpty()
    {
        var sessions = await _repo.ListSessionsAsync();
        Assert.Empty(sessions);
    }

    // ── Untrusted session provenance round-trips ────────────────────────────

    [Fact]
    public async Task UntrustedSession_ProvenanceRoundTrips()
    {
        var session = CreateTestSession(
            trigger: "Orchestration",
            buildMode: "IncrementalComposition",
            deltaSource: "UsnJournal",
            baselineSessionId: "staleBaseline",
            isTrusted: false,
            compositionNote: "Delta window exceeded; session may be incomplete");
        await _repo.SaveSessionAsync(session);

        var loaded = await _repo.GetSessionAsync(session.SessionId);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsTrusted);
        Assert.Equal("Delta window exceeded; session may be incomplete", loaded.CompositionNote);
    }

    // ── Default provenance for legacy-style sessions ────────────────────────

    [Fact]
    public async Task DefaultProvenance_ManualFullRescan()
    {
        // A session created with no explicit provenance should get defaults
        var session = new ScanSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            DuplicateGroupCount = 0,
            Roots = ["C:\\Test"],
            Volumes = [],
            Files = []
        };
        await _repo.SaveSessionAsync(session);

        var loaded = await _repo.GetSessionAsync(session.SessionId);
        Assert.NotNull(loaded);
        Assert.Equal("Manual", loaded.Trigger);
        Assert.Equal("FullRescan", loaded.BuildMode);
        Assert.Equal(string.Empty, loaded.DeltaSource);
        Assert.Equal(string.Empty, loaded.BaselineSessionId);
        Assert.True(loaded.IsTrusted);
        Assert.Equal(string.Empty, loaded.CompositionNote);
    }

    // ── Provenance fields on InventorySessionSummary DTO ────────────────────

    [Fact]
    public async Task InventorySessionSummaryDto_MapsAllProvenanceFields()
    {
        var session = CreateTestSession(
            trigger: "Orchestration",
            buildMode: "IncrementalComposition",
            deltaSource: "ScheduledRescan",
            baselineSessionId: "base123",
            isTrusted: true,
            compositionNote: "Scheduled delta merge");
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);

        var dto = new InventorySessionSummary
        {
            SessionId = latest.SessionId,
            FilesScanned = latest.FilesScanned,
            DuplicateGroupCount = latest.DuplicateGroupCount,
            RootCount = latest.RootCount,
            VolumeCount = latest.VolumeCount,
            CreatedUtc = latest.CreatedUtc.ToString("o"),
            Trigger = latest.Trigger,
            BuildMode = latest.BuildMode,
            DeltaSource = latest.DeltaSource,
            BaselineSessionId = latest.BaselineSessionId,
            IsTrusted = latest.IsTrusted,
            CompositionNote = latest.CompositionNote
        };

        Assert.Equal("Orchestration", dto.Trigger);
        Assert.Equal("IncrementalComposition", dto.BuildMode);
        Assert.Equal("ScheduledRescan", dto.DeltaSource);
        Assert.Equal("base123", dto.BaselineSessionId);
        Assert.True(dto.IsTrusted);
        Assert.Equal("Scheduled delta merge", dto.CompositionNote);
    }
}
