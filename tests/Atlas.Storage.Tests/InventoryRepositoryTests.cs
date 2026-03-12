using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Tests for the inventory persistence layer (C-010).
/// </summary>
public sealed class InventoryRepositoryTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repo;

    public InventoryRepositoryTests()
    {
        _fixture = new TestDatabaseFixture();
        _repo = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    private static ScanSession CreateTestSession(int fileCount = 3, int volumeCount = 1, int rootCount = 1)
    {
        var session = new ScanSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            DuplicateGroupCount = 1,
            Roots = Enumerable.Range(0, rootCount).Select(i => $"C:\\Root{i}").ToList(),
            Volumes = Enumerable.Range(0, volumeCount).Select(i => new VolumeSnapshot
            {
                RootPath = $"{(char)('C' + i)}:\\",
                DriveFormat = "NTFS",
                DriveType = "Fixed",
                IsReady = true,
                TotalSizeBytes = 500_000_000_000L,
                FreeSpaceBytes = 200_000_000_000L
            }).ToList(),
            Files = Enumerable.Range(0, fileCount).Select(i => new FileInventoryItem
            {
                Path = $"C:\\Root0\\file{i}.txt",
                Name = $"file{i}.txt",
                Extension = ".txt",
                Category = "Documents",
                SizeBytes = 1024 * (i + 1),
                LastModifiedUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - i * 3600,
                Sensitivity = SensitivityLevel.Low,
                IsSyncManaged = i % 2 == 0,
                IsDuplicateCandidate = true
            }).ToList()
        };
        return session;
    }

    // ── Schema ──────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaBootstrap_CreatesInventoryTables()
    {
        // If we got here without exception, the schema is valid.
        // Verify by trying an empty query against each table.
        using var conn = _fixture.ConnectionFactory.CreateConnection();
        conn.Open();
        foreach (var table in new[] { "scan_sessions", "scan_session_roots", "scan_volumes", "file_snapshots" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(count >= 0, $"Table {table} should be queryable.");
        }
    }

    // ── Save and reload ─────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGetLatest_RoundTrip_Success()
    {
        var session = CreateTestSession();
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);
        Assert.Equal(session.SessionId, latest.SessionId);
        Assert.Equal(3, latest.FilesScanned);
        Assert.Equal(1, latest.DuplicateGroupCount);
        Assert.Equal(1, latest.RootCount);
        Assert.Equal(1, latest.VolumeCount);
    }

    [Fact]
    public async Task SaveSession_EmptyDatabase_GetLatestReturnsNull()
    {
        var latest = await _repo.GetLatestSessionAsync();
        Assert.Null(latest);
    }

    // ── File snapshots round-trip ───────────────────────────────────────────

    [Fact]
    public async Task GetFilesForSession_ReturnsAllFiles()
    {
        var session = CreateTestSession(fileCount: 5);
        await _repo.SaveSessionAsync(session);

        var files = await _repo.GetFilesForSessionAsync(session.SessionId);
        Assert.Equal(5, files.Count);
    }

    [Fact]
    public async Task GetFilesForSession_PreservesFileProperties()
    {
        var session = CreateTestSession(fileCount: 1);
        session.Files[0].Path = "C:\\Test\\important.pdf";
        session.Files[0].Name = "important.pdf";
        session.Files[0].Extension = ".pdf";
        session.Files[0].Category = "Documents";
        session.Files[0].SizeBytes = 42000;
        session.Files[0].Sensitivity = SensitivityLevel.High;
        session.Files[0].IsSyncManaged = true;
        session.Files[0].IsDuplicateCandidate = false;
        await _repo.SaveSessionAsync(session);

        var files = await _repo.GetFilesForSessionAsync(session.SessionId);
        Assert.Single(files);
        var file = files[0];
        Assert.Equal("C:\\Test\\important.pdf", file.Path);
        Assert.Equal("important.pdf", file.Name);
        Assert.Equal(".pdf", file.Extension);
        Assert.Equal("Documents", file.Category);
        Assert.Equal(42000, file.SizeBytes);
        Assert.Equal(SensitivityLevel.High, file.Sensitivity);
        Assert.True(file.IsSyncManaged);
        Assert.False(file.IsDuplicateCandidate);
    }

    [Fact]
    public async Task GetFilesForSession_RespectsLimitAndOffset()
    {
        var session = CreateTestSession(fileCount: 10);
        await _repo.SaveSessionAsync(session);

        var page1 = await _repo.GetFilesForSessionAsync(session.SessionId, limit: 3, offset: 0);
        var page2 = await _repo.GetFilesForSessionAsync(session.SessionId, limit: 3, offset: 3);
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.NotEqual(page1[0].Path, page2[0].Path);
    }

    [Fact]
    public async Task GetFileCountForSession_ReturnsCorrectCount()
    {
        var session = CreateTestSession(fileCount: 7);
        await _repo.SaveSessionAsync(session);

        var count = await _repo.GetFileCountForSessionAsync(session.SessionId);
        Assert.Equal(7, count);
    }

    [Fact]
    public async Task GetFileCountForSession_MissingSession_ReturnsZero()
    {
        var count = await _repo.GetFileCountForSessionAsync("nonexistent");
        Assert.Equal(0, count);
    }

    // ── Volumes round-trip ──────────────────────────────────────────────────

    [Fact]
    public async Task GetVolumesForSession_ReturnsAllVolumes()
    {
        var session = CreateTestSession(volumeCount: 3);
        await _repo.SaveSessionAsync(session);

        var volumes = await _repo.GetVolumesForSessionAsync(session.SessionId);
        Assert.Equal(3, volumes.Count);
    }

    [Fact]
    public async Task GetVolumesForSession_PreservesVolumeProperties()
    {
        var session = CreateTestSession(volumeCount: 1);
        session.Volumes[0].RootPath = "D:\\";
        session.Volumes[0].DriveFormat = "exFAT";
        session.Volumes[0].DriveType = "Removable";
        session.Volumes[0].IsReady = true;
        session.Volumes[0].TotalSizeBytes = 128_000_000_000L;
        session.Volumes[0].FreeSpaceBytes = 64_000_000_000L;
        await _repo.SaveSessionAsync(session);

        var volumes = await _repo.GetVolumesForSessionAsync(session.SessionId);
        Assert.Single(volumes);
        var vol = volumes[0];
        Assert.Equal("D:\\", vol.RootPath);
        Assert.Equal("exFAT", vol.DriveFormat);
        Assert.Equal("Removable", vol.DriveType);
        Assert.True(vol.IsReady);
        Assert.Equal(128_000_000_000L, vol.TotalSizeBytes);
        Assert.Equal(64_000_000_000L, vol.FreeSpaceBytes);
    }

    // ── Listing and ordering ────────────────────────────────────────────────

    [Fact]
    public async Task ListSessions_ReturnsDescendingTimeOrder()
    {
        var session1 = CreateTestSession();
        session1.CreatedUtc = DateTime.UtcNow.AddMinutes(-10);
        await _repo.SaveSessionAsync(session1);

        var session2 = CreateTestSession();
        session2.CreatedUtc = DateTime.UtcNow;
        await _repo.SaveSessionAsync(session2);

        var sessions = await _repo.ListSessionsAsync(10, 0);
        Assert.True(sessions.Count >= 2);
        Assert.Equal(session2.SessionId, sessions[0].SessionId);
        Assert.Equal(session1.SessionId, sessions[1].SessionId);
    }

    [Fact]
    public async Task ListSessions_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _repo.SaveSessionAsync(CreateTestSession());
        }

        var sessions = await _repo.ListSessionsAsync(3, 0);
        Assert.Equal(3, sessions.Count);
    }

    [Fact]
    public async Task ListSessions_EmptyDatabase_ReturnsEmptyList()
    {
        var sessions = await _repo.ListSessionsAsync();
        Assert.Empty(sessions);
    }

    // ── Multiple roots ──────────────────────────────────────────────────────

    [Fact]
    public async Task SaveSession_MultipleRoots_PreservedInSummary()
    {
        var session = CreateTestSession(rootCount: 3);
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);
        Assert.Equal(3, latest.RootCount);
    }
}
