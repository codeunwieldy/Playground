using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

public sealed class DuplicatePersistenceTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repository;

    public DuplicatePersistenceTests()
    {
        _fixture = new TestDatabaseFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Round-trip: save and read duplicate groups ────────────────────────

    [Fact]
    public async Task SaveSession_PersistsDuplicateGroups_RoundTrip()
    {
        var session = CreateSessionWithDuplicates();
        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);

        Assert.Single(groups);
        var g = groups[0];
        Assert.Equal("group-1", g.GroupId);
        Assert.Equal(@"C:\Documents\file.pdf", g.CanonicalPath);
        Assert.Equal(2, g.MemberPaths.Count);
    }

    [Fact]
    public async Task SaveSession_PreservesConfidenceValues()
    {
        var session = CreateSessionWithDuplicates();
        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);

        var g = Assert.Single(groups);
        Assert.Equal(0.999, g.MatchConfidence, precision: 3);
        Assert.Equal(0.919, g.CleanupConfidence, precision: 3);
    }

    [Fact]
    public async Task SaveSession_PreservesCanonicalReason()
    {
        var session = CreateSessionWithDuplicates();
        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);

        var g = Assert.Single(groups);
        Assert.Equal("preferred location; high sensitivity (High)", g.CanonicalReason);
    }

    [Fact]
    public async Task SaveSession_PreservesRiskFlags()
    {
        var session = CreateSessionWithDuplicates();
        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);

        var g = Assert.Single(groups);
        Assert.True(g.HasSensitiveMembers);
        Assert.True(g.HasSyncManagedMembers);
        Assert.False(g.HasProtectedMembers);
        Assert.Equal(SensitivityLevel.High, g.MaxSensitivity);
    }

    [Fact]
    public async Task SaveSession_PreservesMemberPaths()
    {
        var session = CreateSessionWithDuplicates();
        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);

        var g = Assert.Single(groups);
        Assert.Contains(@"C:\Documents\file.pdf", g.MemberPaths);
        Assert.Contains(@"C:\Downloads\file (1).pdf", g.MemberPaths);
    }

    // ── Multiple groups ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveSession_MultipleDuplicateGroups_AllPersisted()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            CreateDuplicateGroup("g-1", @"C:\A\file1.pdf", [@"C:\A\file1.pdf", @"C:\B\file1.pdf"], 0.999, 0.999),
            CreateDuplicateGroup("g-2", @"C:\A\file2.xlsx", [@"C:\A\file2.xlsx", @"C:\B\file2.xlsx", @"C:\C\file2.xlsx"], 0.999, 0.85),
        ];
        session.DuplicateGroupCount = 2;

        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);
        Assert.Equal(2, groups.Count);

        var g1 = groups.First(g => g.GroupId == "g-1");
        var g2 = groups.First(g => g.GroupId == "g-2");
        Assert.Equal(2, g1.MemberPaths.Count);
        Assert.Equal(3, g2.MemberPaths.Count);
    }

    // ── Pagination ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroups_Pagination_RespectsLimitAndOffset()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            CreateDuplicateGroup("g-1", @"C:\A\file1.pdf", [@"C:\A\file1.pdf", @"C:\B\file1.pdf"], 0.999, 0.999),
            CreateDuplicateGroup("g-2", @"C:\A\file2.pdf", [@"C:\A\file2.pdf", @"C:\B\file2.pdf"], 0.999, 0.919),
            CreateDuplicateGroup("g-3", @"C:\A\file3.pdf", [@"C:\A\file3.pdf", @"C:\B\file3.pdf"], 0.999, 0.80),
        ];
        session.DuplicateGroupCount = 3;

        await _repository.SaveSessionAsync(session);

        var page1 = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId, limit: 2, offset: 0);
        var page2 = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId, limit: 2, offset: 2);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
    }

    // ── Count ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupCount_ReturnsCorrectCount()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            CreateDuplicateGroup("g-1", @"C:\A\file1.pdf", [@"C:\A\file1.pdf", @"C:\B\file1.pdf"], 0.999, 0.999),
            CreateDuplicateGroup("g-2", @"C:\A\file2.pdf", [@"C:\A\file2.pdf", @"C:\B\file2.pdf"], 0.999, 0.919),
        ];
        session.DuplicateGroupCount = 2;

        await _repository.SaveSessionAsync(session);

        var count = await _repository.GetDuplicateGroupCountForSessionAsync(session.SessionId);
        Assert.Equal(2, count);
    }

    // ── Empty session ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroups_EmptySession_ReturnsEmpty()
    {
        var session = CreateSessionWithFiles();
        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);
        Assert.Empty(groups);
    }

    [Fact]
    public async Task GetDuplicateGroupCount_EmptySession_ReturnsZero()
    {
        var session = CreateSessionWithFiles();
        await _repository.SaveSessionAsync(session);

        var count = await _repository.GetDuplicateGroupCountForSessionAsync(session.SessionId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetDuplicateGroups_NonExistentSession_ReturnsEmpty()
    {
        var groups = await _repository.GetDuplicateGroupsForSessionAsync("nonexistent");
        Assert.Empty(groups);
    }

    // ── Ordering: descending cleanup confidence ──────────────────────────

    [Fact]
    public async Task GetDuplicateGroups_OrderedByCleanupConfidenceDescending()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            CreateDuplicateGroup("g-low", @"C:\A\risky.pdf", [@"C:\A\risky.pdf", @"C:\B\risky.pdf"], 0.999, 0.5),
            CreateDuplicateGroup("g-high", @"C:\A\safe.pdf", [@"C:\A\safe.pdf", @"C:\B\safe.pdf"], 0.999, 0.999),
            CreateDuplicateGroup("g-mid", @"C:\A\mid.pdf", [@"C:\A\mid.pdf", @"C:\B\mid.pdf"], 0.999, 0.8),
        ];
        session.DuplicateGroupCount = 3;

        await _repository.SaveSessionAsync(session);

        var groups = await _repository.GetDuplicateGroupsForSessionAsync(session.SessionId);
        Assert.Equal(3, groups.Count);
        Assert.Equal("g-high", groups[0].GroupId);
        Assert.Equal("g-mid", groups[1].GroupId);
        Assert.Equal("g-low", groups[2].GroupId);
    }

    // ── Session isolation ────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroups_IsolatedBySession()
    {
        var session1 = CreateSessionWithFiles();
        session1.DuplicateGroups =
        [
            CreateDuplicateGroup("g-1", @"C:\A\file.pdf", [@"C:\A\file.pdf", @"C:\B\file.pdf"], 0.999, 0.999),
        ];
        session1.DuplicateGroupCount = 1;

        var session2 = CreateSessionWithFiles();
        session2.DuplicateGroups =
        [
            CreateDuplicateGroup("g-2", @"C:\X\data.xlsx", [@"C:\X\data.xlsx", @"C:\Y\data.xlsx"], 0.999, 0.85),
        ];
        session2.DuplicateGroupCount = 1;

        await _repository.SaveSessionAsync(session1);
        await _repository.SaveSessionAsync(session2);

        var groups1 = await _repository.GetDuplicateGroupsForSessionAsync(session1.SessionId);
        var groups2 = await _repository.GetDuplicateGroupsForSessionAsync(session2.SessionId);

        Assert.Single(groups1);
        Assert.Single(groups2);
        Assert.Equal("g-1", groups1[0].GroupId);
        Assert.Equal("g-2", groups2[0].GroupId);
    }

    // ── DuplicateGroupCount on session summary still works ───────────────

    [Fact]
    public async Task SessionSummary_StillReturnsDuplicateGroupCount()
    {
        var session = CreateSessionWithDuplicates();
        await _repository.SaveSessionAsync(session);

        var summary = await _repository.GetSessionAsync(session.SessionId);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.DuplicateGroupCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ScanSession CreateSessionWithDuplicates()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            new DuplicateGroup
            {
                GroupId = "group-1",
                CanonicalPath = @"C:\Documents\file.pdf",
                MatchConfidence = 0.999,
                Confidence = 0.919,
                CanonicalReason = "preferred location; high sensitivity (High)",
                MaxSensitivity = SensitivityLevel.High,
                HasSensitiveMembers = true,
                HasSyncManagedMembers = true,
                HasProtectedMembers = false,
                Paths = [@"C:\Documents\file.pdf", @"C:\Downloads\file (1).pdf"]
            }
        ];
        session.DuplicateGroupCount = 1;
        return session;
    }

    private static ScanSession CreateSessionWithFiles()
    {
        return new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Documents\file.pdf",
                    Name = "file.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 1024,
                    LastModifiedUnixTimeSeconds = 100
                }
            ]
        };
    }

    private static DuplicateGroup CreateDuplicateGroup(
        string groupId, string canonicalPath, List<string> paths, double matchConfidence, double cleanupConfidence)
    {
        return new DuplicateGroup
        {
            GroupId = groupId,
            CanonicalPath = canonicalPath,
            MatchConfidence = matchConfidence,
            Confidence = cleanupConfidence,
            CanonicalReason = "test",
            MaxSensitivity = SensitivityLevel.Low,
            HasSensitiveMembers = false,
            HasSyncManagedMembers = false,
            HasProtectedMembers = false,
            Paths = paths
        };
    }
}
