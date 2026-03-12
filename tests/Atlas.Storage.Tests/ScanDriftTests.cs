using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Tests for scan drift / session diffing (C-013).
/// Validates that repository diff methods produce correct added/removed/changed/unchanged
/// counts and bounded diff file rows.
/// </summary>
public sealed class ScanDriftTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repo;

    public ScanDriftTests()
    {
        _fixture = new TestDatabaseFixture();
        _repo = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    private static ScanSession CreateSession(params (string path, long size, long modified)[] files)
    {
        return new ScanSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            DuplicateGroupCount = 0,
            Roots = new List<string> { "C:\\Root" },
            Volumes = new List<VolumeSnapshot>
            {
                new() { RootPath = "C:\\", DriveFormat = "NTFS", DriveType = "Fixed", IsReady = true, TotalSizeBytes = 500_000_000_000L, FreeSpaceBytes = 200_000_000_000L }
            },
            Files = files.Select(f => new FileInventoryItem
            {
                Path = f.path,
                Name = System.IO.Path.GetFileName(f.path),
                Extension = System.IO.Path.GetExtension(f.path),
                Category = "Documents",
                SizeBytes = f.size,
                LastModifiedUnixTimeSeconds = f.modified,
                Sensitivity = SensitivityLevel.Low,
                IsSyncManaged = false,
                IsDuplicateCandidate = false
            }).ToList()
        };
    }

    // ── DiffSessionsAsync: counts ───────────────────────────────────────────

    [Fact]
    public async Task Diff_IdenticalSessions_AllUnchanged()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\b.txt", 200, 2000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\b.txt", 200, 2000));
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(2, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_FileAdded_CountsCorrectly()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\b.txt", 200, 2000));
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_FileRemoved_CountsCorrectly()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\b.txt", 200, 2000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_FileChanged_SizeChanged()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 999, 1000));
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.ChangedCount);
        Assert.Equal(0, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_FileChanged_ModifiedTimeChanged()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 9999));
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.ChangedCount);
        Assert.Equal(0, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_MixedChanges_CountsCorrectly()
    {
        var older = CreateSession(
            ("C:\\Root\\unchanged.txt", 100, 1000),
            ("C:\\Root\\changed.txt", 200, 2000),
            ("C:\\Root\\removed.txt", 300, 3000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\unchanged.txt", 100, 1000),
            ("C:\\Root\\changed.txt", 250, 2000),
            ("C:\\Root\\added.txt", 400, 4000));
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.ChangedCount);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_EmptySessions_AllZeros()
    {
        var older = CreateSession();
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession();
        await _repo.SaveSessionAsync(newer);

        var diff = await _repo.DiffSessionsAsync(older.SessionId, newer.SessionId);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(0, diff.UnchangedCount);
    }

    [Fact]
    public async Task Diff_MissingSessions_ReturnsAllZeros()
    {
        var diff = await _repo.DiffSessionsAsync("nonexistent1", "nonexistent2");
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(0, diff.UnchangedCount);
    }

    // ── GetDiffFilesAsync: file-level rows ──────────────────────────────────

    [Fact]
    public async Task DiffFiles_ReturnsAddedRemovedChanged_NotUnchanged()
    {
        var older = CreateSession(
            ("C:\\Root\\unchanged.txt", 100, 1000),
            ("C:\\Root\\changed.txt", 200, 2000),
            ("C:\\Root\\removed.txt", 300, 3000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\unchanged.txt", 100, 1000),
            ("C:\\Root\\changed.txt", 250, 2000),
            ("C:\\Root\\added.txt", 400, 4000));
        await _repo.SaveSessionAsync(newer);

        var files = await _repo.GetDiffFilesAsync(older.SessionId, newer.SessionId);
        Assert.Equal(3, files.Count);

        var added = files.Single(f => f.ChangeKind == "Added");
        Assert.Equal("C:\\Root\\added.txt", added.Path);
        Assert.Null(added.OlderSizeBytes);
        Assert.Equal(400, added.NewerSizeBytes);

        var removed = files.Single(f => f.ChangeKind == "Removed");
        Assert.Equal("C:\\Root\\removed.txt", removed.Path);
        Assert.Equal(300, removed.OlderSizeBytes);
        Assert.Null(removed.NewerSizeBytes);

        var changed = files.Single(f => f.ChangeKind == "Changed");
        Assert.Equal("C:\\Root\\changed.txt", changed.Path);
        Assert.Equal(200, changed.OlderSizeBytes);
        Assert.Equal(250, changed.NewerSizeBytes);
    }

    [Fact]
    public async Task DiffFiles_OrderedByPath()
    {
        var older = CreateSession(
            ("C:\\Root\\z.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\m.txt", 100, 1000),
            ("C:\\Root\\z.txt", 100, 1000));
        await _repo.SaveSessionAsync(newer);

        var files = await _repo.GetDiffFilesAsync(older.SessionId, newer.SessionId);
        Assert.Equal(2, files.Count); // a.txt and m.txt added; z.txt unchanged
        Assert.Equal("C:\\Root\\a.txt", files[0].Path);
        Assert.Equal("C:\\Root\\m.txt", files[1].Path);
    }

    [Fact]
    public async Task DiffFiles_RespectsLimitAndOffset()
    {
        var older = CreateSession();
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        // Create newer with 10 added files
        var newFiles = Enumerable.Range(0, 10)
            .Select(i => ($"C:\\Root\\file{i:D2}.txt", (long)(i * 100 + 100), (long)(i * 1000 + 1000)))
            .ToArray();
        var newer = CreateSession(newFiles);
        await _repo.SaveSessionAsync(newer);

        var page1 = await _repo.GetDiffFilesAsync(older.SessionId, newer.SessionId, 3, 0);
        var page2 = await _repo.GetDiffFilesAsync(older.SessionId, newer.SessionId, 3, 3);
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.NotEqual(page1[0].Path, page2[0].Path);
    }

    [Fact]
    public async Task DiffFiles_IdenticalSessions_ReturnsEmpty()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        await _repo.SaveSessionAsync(newer);

        var files = await _repo.GetDiffFilesAsync(older.SessionId, newer.SessionId);
        Assert.Empty(files);
    }

    [Fact]
    public async Task DiffFiles_MissingSessions_ReturnsEmpty()
    {
        var files = await _repo.GetDiffFilesAsync("nonexistent1", "nonexistent2");
        Assert.Empty(files);
    }

    // ── Drift snapshot (latest-vs-previous) ─────────────────────────────────

    [Fact]
    public async Task DriftSnapshot_FewerThanTwoSessions_NoBaseline()
    {
        // Zero sessions
        var sessions0 = await _repo.ListSessionsAsync(2, 0);
        Assert.True(sessions0.Count < 2);

        // One session
        var session = CreateSession(("C:\\Root\\a.txt", 100, 1000));
        await _repo.SaveSessionAsync(session);

        var sessions1 = await _repo.ListSessionsAsync(2, 0);
        Assert.True(sessions1.Count < 2);
    }

    [Fact]
    public async Task DriftSnapshot_TwoSessions_ProducesDiff()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\b.txt", 200, 2000));
        await _repo.SaveSessionAsync(newer);

        var sessions = await _repo.ListSessionsAsync(2, 0);
        Assert.Equal(2, sessions.Count);
        var diff = await _repo.DiffSessionsAsync(sessions[1].SessionId, sessions[0].SessionId);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.UnchangedCount);
    }

    // ── Contract DTO mapping ────────────────────────────────────────────────

    [Fact]
    public async Task DiffFileSummary_MapsFromSessionDiffFile()
    {
        var older = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 200, 2000));
        await _repo.SaveSessionAsync(newer);

        var files = await _repo.GetDiffFilesAsync(older.SessionId, newer.SessionId);
        Assert.Single(files);
        var f = files[0];

        var dto = new DiffFileSummary
        {
            Path = f.Path,
            ChangeKind = f.ChangeKind,
            OlderSizeBytes = f.OlderSizeBytes ?? 0,
            NewerSizeBytes = f.NewerSizeBytes ?? 0,
            OlderLastModifiedUnix = f.OlderLastModifiedUnix ?? 0,
            NewerLastModifiedUnix = f.NewerLastModifiedUnix ?? 0
        };

        Assert.Equal("C:\\Root\\a.txt", dto.Path);
        Assert.Equal("Changed", dto.ChangeKind);
        Assert.Equal(100, dto.OlderSizeBytes);
        Assert.Equal(200, dto.NewerSizeBytes);
        Assert.Equal(1000, dto.OlderLastModifiedUnix);
        Assert.Equal(2000, dto.NewerLastModifiedUnix);
    }

    [Fact]
    public async Task DriftSnapshotResponse_MapsFromDiffSummary()
    {
        var older = CreateSession(("C:\\Root\\a.txt", 100, 1000));
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
        await _repo.SaveSessionAsync(older);

        var newer = CreateSession(
            ("C:\\Root\\a.txt", 100, 1000),
            ("C:\\Root\\b.txt", 200, 2000));
        await _repo.SaveSessionAsync(newer);

        var sessions = await _repo.ListSessionsAsync(2, 0);
        var diff = await _repo.DiffSessionsAsync(sessions[1].SessionId, sessions[0].SessionId);

        var response = new DriftSnapshotResponse
        {
            HasBaseline = true,
            OlderSessionId = diff.OlderSessionId,
            NewerSessionId = diff.NewerSessionId,
            AddedCount = diff.AddedCount,
            RemovedCount = diff.RemovedCount,
            ChangedCount = diff.ChangedCount,
            UnchangedCount = diff.UnchangedCount,
            OlderCreatedUtc = sessions[1].CreatedUtc.ToString("o"),
            NewerCreatedUtc = sessions[0].CreatedUtc.ToString("o")
        };

        Assert.True(response.HasBaseline);
        Assert.Equal(1, response.AddedCount);
        Assert.Equal(0, response.RemovedCount);
        Assert.Equal(1, response.UnchangedCount);
        Assert.False(string.IsNullOrEmpty(response.OlderCreatedUtc));
        Assert.False(string.IsNullOrEmpty(response.NewerCreatedUtc));
    }

    [Fact]
    public async Task SessionDiffResponse_MissingSession_ReturnsNotFound()
    {
        var loaded = await _repo.GetSessionAsync("nonexistent");
        var response = loaded is null
            ? new SessionDiffResponse { Found = false }
            : new SessionDiffResponse { Found = true };
        Assert.False(response.Found);
    }
}
