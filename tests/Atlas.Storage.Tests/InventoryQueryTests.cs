using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Tests for the read-side inventory query layer (C-011).
/// Validates that inventory repository methods return data in the shape
/// the inventory pipe handlers rely on: bounded, descending-time, and with
/// correct field mapping for the Inventory* DTOs.
/// </summary>
public sealed class InventoryQueryTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repo;

    public InventoryQueryTests()
    {
        _fixture = new TestDatabaseFixture();
        _repo = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    private static ScanSession CreateTestSession(int fileCount = 3, int volumeCount = 1, int rootCount = 1)
    {
        return new ScanSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            DuplicateGroupCount = 2,
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
    }

    // ── Snapshot (latest session) ───────────────────────────────────────────

    [Fact]
    public async Task GetLatestSession_EmptyDatabase_ReturnsNull()
    {
        var latest = await _repo.GetLatestSessionAsync();
        Assert.Null(latest);
    }

    [Fact]
    public async Task GetLatestSession_ReturnsNewestSession()
    {
        var older = CreateTestSession();
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-10);
        await _repo.SaveSessionAsync(older);

        var newer = CreateTestSession();
        newer.CreatedUtc = DateTime.UtcNow;
        await _repo.SaveSessionAsync(newer);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);
        Assert.Equal(newer.SessionId, latest.SessionId);
    }

    [Fact]
    public async Task GetLatestSession_MapsAllSummaryFields()
    {
        var session = CreateTestSession(fileCount: 5, volumeCount: 2, rootCount: 3);
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);
        Assert.Equal(session.SessionId, latest.SessionId);
        Assert.Equal(5, latest.FilesScanned);
        Assert.Equal(2, latest.DuplicateGroupCount);
        Assert.Equal(3, latest.RootCount);
        Assert.Equal(2, latest.VolumeCount);
        Assert.True(latest.CreatedUtc > DateTime.MinValue);
    }

    // ── GetSessionAsync (by ID) ─────────────────────────────────────────────

    [Fact]
    public async Task GetSession_ExistingSession_ReturnsSummary()
    {
        var session = CreateTestSession(fileCount: 4, volumeCount: 2, rootCount: 2);
        await _repo.SaveSessionAsync(session);

        var loaded = await _repo.GetSessionAsync(session.SessionId);
        Assert.NotNull(loaded);
        Assert.Equal(session.SessionId, loaded.SessionId);
        Assert.Equal(4, loaded.FilesScanned);
        Assert.Equal(2, loaded.RootCount);
        Assert.Equal(2, loaded.VolumeCount);
    }

    [Fact]
    public async Task GetSession_MissingSession_ReturnsNull()
    {
        var loaded = await _repo.GetSessionAsync("nonexistent");
        Assert.Null(loaded);
    }

    // ── Session list ────────────────────────────────────────────────────────

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
    public async Task ListSessions_RespectsLimitAndOffset()
    {
        for (var i = 0; i < 5; i++)
        {
            var s = CreateTestSession();
            s.CreatedUtc = DateTime.UtcNow.AddMinutes(-i);
            await _repo.SaveSessionAsync(s);
        }

        var page1 = await _repo.ListSessionsAsync(2, 0);
        var page2 = await _repo.ListSessionsAsync(2, 2);
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.NotEqual(page1[0].SessionId, page2[0].SessionId);
    }

    [Fact]
    public async Task ListSessions_EmptyDatabase_ReturnsEmptyList()
    {
        var sessions = await _repo.ListSessionsAsync();
        Assert.Empty(sessions);
    }

    // ── GetRootsForSession ──────────────────────────────────────────────────

    [Fact]
    public async Task GetRootsForSession_ReturnsAllRoots()
    {
        var session = CreateTestSession(rootCount: 3);
        await _repo.SaveSessionAsync(session);

        var roots = await _repo.GetRootsForSessionAsync(session.SessionId);
        Assert.Equal(3, roots.Count);
    }

    [Fact]
    public async Task GetRootsForSession_MissingSession_ReturnsEmptyList()
    {
        var roots = await _repo.GetRootsForSessionAsync("nonexistent");
        Assert.Empty(roots);
    }

    [Fact]
    public async Task GetRootsForSession_RootsAreSortedByPath()
    {
        var session = CreateTestSession(rootCount: 0);
        session.Roots = new List<string> { "D:\\Beta", "C:\\Alpha", "E:\\Gamma" };
        await _repo.SaveSessionAsync(session);

        var roots = await _repo.GetRootsForSessionAsync(session.SessionId);
        Assert.Equal(3, roots.Count);
        Assert.Equal("C:\\Alpha", roots[0]);
        Assert.Equal("D:\\Beta", roots[1]);
        Assert.Equal("E:\\Gamma", roots[2]);
    }

    // ── Volume snapshots ────────────────────────────────────────────────────

    [Fact]
    public async Task GetVolumesForSession_ReturnsVolumes()
    {
        var session = CreateTestSession(volumeCount: 2);
        await _repo.SaveSessionAsync(session);

        var volumes = await _repo.GetVolumesForSessionAsync(session.SessionId);
        Assert.Equal(2, volumes.Count);
    }

    [Fact]
    public async Task GetVolumesForSession_MissingSession_ReturnsEmptyList()
    {
        var volumes = await _repo.GetVolumesForSessionAsync("nonexistent");
        Assert.Empty(volumes);
    }

    [Fact]
    public async Task GetVolumesForSession_MapsToDtoShape()
    {
        var session = CreateTestSession(volumeCount: 1);
        session.Volumes[0].RootPath = "D:\\";
        session.Volumes[0].DriveFormat = "exFAT";
        session.Volumes[0].DriveType = "Removable";
        session.Volumes[0].TotalSizeBytes = 128_000_000_000L;
        session.Volumes[0].FreeSpaceBytes = 64_000_000_000L;
        await _repo.SaveSessionAsync(session);

        var volumes = await _repo.GetVolumesForSessionAsync(session.SessionId);
        Assert.Single(volumes);
        var vol = volumes[0];

        // Mirror the handler projection to InventoryVolumeSummary
        var dto = new InventoryVolumeSummary
        {
            RootPath = vol.RootPath,
            DriveFormat = vol.DriveFormat,
            DriveType = vol.DriveType,
            IsReady = vol.IsReady,
            TotalSizeBytes = vol.TotalSizeBytes,
            FreeSpaceBytes = vol.FreeSpaceBytes
        };

        Assert.Equal("D:\\", dto.RootPath);
        Assert.Equal("exFAT", dto.DriveFormat);
        Assert.Equal("Removable", dto.DriveType);
        Assert.Equal(128_000_000_000L, dto.TotalSizeBytes);
        Assert.Equal(64_000_000_000L, dto.FreeSpaceBytes);
    }

    // ── File snapshots ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetFilesForSession_ReturnsPaginatedFiles()
    {
        var session = CreateTestSession(fileCount: 10);
        await _repo.SaveSessionAsync(session);

        var page1 = await _repo.GetFilesForSessionAsync(session.SessionId, 3, 0);
        var page2 = await _repo.GetFilesForSessionAsync(session.SessionId, 3, 3);
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.NotEqual(page1[0].Path, page2[0].Path);
    }

    [Fact]
    public async Task GetFilesForSession_MapsToFileSummaryDto()
    {
        var session = CreateTestSession(fileCount: 1);
        session.Files[0].Path = "C:\\Test\\report.pdf";
        session.Files[0].Name = "report.pdf";
        session.Files[0].Extension = ".pdf";
        session.Files[0].Category = "Documents";
        session.Files[0].SizeBytes = 42000;
        session.Files[0].Sensitivity = SensitivityLevel.High;
        session.Files[0].IsSyncManaged = true;
        session.Files[0].IsDuplicateCandidate = false;
        await _repo.SaveSessionAsync(session);

        var files = await _repo.GetFilesForSessionAsync(session.SessionId);
        Assert.Single(files);
        var f = files[0];

        // Mirror the handler projection to InventoryFileSummary
        var dto = new InventoryFileSummary
        {
            Path = f.Path,
            Name = f.Name,
            Extension = f.Extension,
            Category = f.Category,
            SizeBytes = f.SizeBytes,
            LastModifiedUnixTimeSeconds = f.LastModifiedUnixTimeSeconds,
            Sensitivity = f.Sensitivity,
            IsSyncManaged = f.IsSyncManaged,
            IsDuplicateCandidate = f.IsDuplicateCandidate
        };

        Assert.Equal("C:\\Test\\report.pdf", dto.Path);
        Assert.Equal("report.pdf", dto.Name);
        Assert.Equal(".pdf", dto.Extension);
        Assert.Equal("Documents", dto.Category);
        Assert.Equal(42000, dto.SizeBytes);
        Assert.Equal(SensitivityLevel.High, dto.Sensitivity);
        Assert.True(dto.IsSyncManaged);
        Assert.False(dto.IsDuplicateCandidate);
    }

    [Fact]
    public async Task GetFilesForSession_MissingSession_ReturnsEmptyList()
    {
        var files = await _repo.GetFilesForSessionAsync("nonexistent");
        Assert.Empty(files);
    }

    [Fact]
    public async Task GetFileCount_MatchesPaginationTotal()
    {
        var session = CreateTestSession(fileCount: 7);
        await _repo.SaveSessionAsync(session);

        var count = await _repo.GetFileCountForSessionAsync(session.SessionId);
        var allFiles = await _repo.GetFilesForSessionAsync(session.SessionId, 1000, 0);
        Assert.Equal(7, count);
        Assert.Equal(count, allFiles.Count);
    }

    // ── DTO mapping (mirrors handler projection) ────────────────────────────

    [Fact]
    public async Task InventorySessionSummary_MapsFromRepoSummary()
    {
        var session = CreateTestSession(fileCount: 5, volumeCount: 2, rootCount: 3);
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);

        // Mirror the handler projection to InventorySessionSummary
        var dto = new InventorySessionSummary
        {
            SessionId = latest.SessionId,
            FilesScanned = latest.FilesScanned,
            DuplicateGroupCount = latest.DuplicateGroupCount,
            RootCount = latest.RootCount,
            VolumeCount = latest.VolumeCount,
            CreatedUtc = latest.CreatedUtc.ToString("o")
        };

        Assert.Equal(session.SessionId, dto.SessionId);
        Assert.Equal(5, dto.FilesScanned);
        Assert.Equal(2, dto.DuplicateGroupCount);
        Assert.Equal(3, dto.RootCount);
        Assert.Equal(2, dto.VolumeCount);
        Assert.False(string.IsNullOrEmpty(dto.CreatedUtc));
    }

    [Fact]
    public async Task InventorySnapshotResponse_EmptyDatabase_HasSessionIsFalse()
    {
        var latest = await _repo.GetLatestSessionAsync();

        // Mirror the handler logic for InventorySnapshotResponse
        var response = latest is null
            ? new InventorySnapshotResponse { HasSession = false }
            : new InventorySnapshotResponse { HasSession = true, SessionId = latest.SessionId };

        Assert.False(response.HasSession);
        Assert.Equal(string.Empty, response.SessionId);
    }

    [Fact]
    public async Task InventorySnapshotResponse_WithSession_HasSessionIsTrue()
    {
        var session = CreateTestSession(fileCount: 3, volumeCount: 1, rootCount: 2);
        await _repo.SaveSessionAsync(session);

        var latest = await _repo.GetLatestSessionAsync();
        Assert.NotNull(latest);

        var response = new InventorySnapshotResponse
        {
            HasSession = true,
            SessionId = latest.SessionId,
            FilesScanned = latest.FilesScanned,
            DuplicateGroupCount = latest.DuplicateGroupCount,
            RootCount = latest.RootCount,
            VolumeCount = latest.VolumeCount,
            CreatedUtc = latest.CreatedUtc.ToString("o")
        };

        Assert.True(response.HasSession);
        Assert.Equal(session.SessionId, response.SessionId);
        Assert.Equal(3, response.FilesScanned);
        Assert.Equal(2, response.RootCount);
    }

    [Fact]
    public async Task SessionDetail_MissingSession_ReturnsNotFound()
    {
        var loaded = await _repo.GetSessionAsync("nonexistent");

        // Mirror the handler logic for InventorySessionDetailResponse
        var response = loaded is null
            ? new InventorySessionDetailResponse { Found = false }
            : new InventorySessionDetailResponse { Found = true };

        Assert.False(response.Found);
    }

    [Fact]
    public async Task SessionDetail_ExistingSession_ReturnsRootsAndVolumes()
    {
        var session = CreateTestSession(fileCount: 2, volumeCount: 2, rootCount: 3);
        await _repo.SaveSessionAsync(session);

        var loaded = await _repo.GetSessionAsync(session.SessionId);
        Assert.NotNull(loaded);

        var roots = await _repo.GetRootsForSessionAsync(session.SessionId);
        var volumes = await _repo.GetVolumesForSessionAsync(session.SessionId);

        var response = new InventorySessionDetailResponse
        {
            Found = true,
            SessionId = loaded.SessionId,
            FilesScanned = loaded.FilesScanned,
            DuplicateGroupCount = loaded.DuplicateGroupCount,
            CreatedUtc = loaded.CreatedUtc.ToString("o"),
            Roots = roots.ToList(),
            Volumes = volumes.Select(v => new InventoryVolumeSummary
            {
                RootPath = v.RootPath,
                DriveFormat = v.DriveFormat,
                DriveType = v.DriveType,
                IsReady = v.IsReady,
                TotalSizeBytes = v.TotalSizeBytes,
                FreeSpaceBytes = v.FreeSpaceBytes
            }).ToList()
        };

        Assert.True(response.Found);
        Assert.Equal(3, response.Roots.Count);
        Assert.Equal(2, response.Volumes.Count);
        Assert.Equal(2, response.FilesScanned);
    }
}
