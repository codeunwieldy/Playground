using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

public sealed class DuplicateDetailTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repository;

    public DuplicateDetailTests()
    {
        _fixture = new TestDatabaseFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    // ── Evidence round-trip ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_EvidenceRoundTrip()
    {
        var session = CreateSessionWithEvidenceDuplicates();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-ev");

        Assert.NotNull(detail);
        Assert.NotEmpty(detail!.Evidence);
        Assert.Contains(detail.Evidence, e => e.Signal == "FullHashMatch");
        Assert.Contains(detail.Evidence, e => e.Signal == "SizeMatch");
    }

    // ── All group fields returned ───────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_ReturnsAllFields()
    {
        var session = CreateSessionWithEvidenceDuplicates();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-ev");

        Assert.NotNull(detail);
        Assert.Equal("group-ev", detail!.GroupId);
        Assert.Equal(@"C:\Documents\file.pdf", detail.CanonicalPath);
        Assert.Equal(0.999, detail.MatchConfidence, precision: 3);
        Assert.Equal(0.919, detail.CleanupConfidence, precision: 3);
        Assert.Equal("preferred location; high sensitivity (High)", detail.CanonicalReason);
        Assert.Equal(SensitivityLevel.High, detail.MaxSensitivity);
        Assert.True(detail.HasSensitiveMembers);
        Assert.True(detail.HasSyncManagedMembers);
        Assert.False(detail.HasProtectedMembers);
    }

    // ── Bounded member paths ordered by path ────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_MemberPathsOrderedByPath()
    {
        var session = CreateSessionWithEvidenceDuplicates();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-ev");

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.MemberPaths.Count);
        // Ordered by path: C:\Documents comes before C:\Downloads
        Assert.Equal(@"C:\Documents\file.pdf", detail.MemberPaths[0]);
        Assert.Equal(@"C:\Downloads\file (1).pdf", detail.MemberPaths[1]);
    }

    // ── Missing session returns null ────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_MissingSession_ReturnsNull()
    {
        var detail = await _repository.GetDuplicateGroupDetailAsync("nonexistent", "any-group");

        Assert.Null(detail);
    }

    // ── Missing group ID returns null ───────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_MissingGroupId_ReturnsNull()
    {
        var session = CreateSessionWithEvidenceDuplicates();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "nonexistent-group");

        Assert.Null(detail);
    }

    // ── Group with empty evidence ───────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_NoEvidence_ReturnsEmptyEvidenceList()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            new DuplicateGroup
            {
                GroupId = "group-no-ev",
                CanonicalPath = @"C:\Documents\file.pdf",
                MatchConfidence = 0.999,
                Confidence = 0.999,
                CanonicalReason = "test",
                MaxSensitivity = SensitivityLevel.Low,
                Paths = [@"C:\Documents\file.pdf", @"C:\Downloads\file.pdf"],
                Evidence = []
            }
        ];
        session.DuplicateGroupCount = 1;
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-no-ev");

        Assert.NotNull(detail);
        Assert.Empty(detail!.Evidence);
    }

    // ── Evidence count bounded ──────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_EvidenceCountBounded()
    {
        var session = CreateSessionWithEvidenceDuplicates();
        await _repository.SaveSessionAsync(session);

        var detail = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-ev");

        Assert.NotNull(detail);
        Assert.InRange(detail!.Evidence.Count, 1, 10);
    }

    // ── Multiple groups — only requested group returned ─────────────────────

    [Fact]
    public async Task GetDuplicateGroupDetail_MultipleGroups_ReturnsOnlyRequested()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            new DuplicateGroup
            {
                GroupId = "group-a",
                CanonicalPath = @"C:\A\file.pdf",
                MatchConfidence = 0.999,
                Confidence = 0.999,
                CanonicalReason = "reason-a",
                MaxSensitivity = SensitivityLevel.Low,
                Paths = [@"C:\A\file.pdf", @"C:\B\file.pdf"],
                Evidence = [new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "detail-a" }]
            },
            new DuplicateGroup
            {
                GroupId = "group-b",
                CanonicalPath = @"C:\X\data.xlsx",
                MatchConfidence = 0.85,
                Confidence = 0.80,
                CanonicalReason = "reason-b",
                MaxSensitivity = SensitivityLevel.Medium,
                Paths = [@"C:\X\data.xlsx", @"C:\Y\data.xlsx"],
                Evidence = [new DuplicateEvidenceEntry { Signal = "QuickHashOnly", Detail = "detail-b" }]
            }
        ];
        session.DuplicateGroupCount = 2;
        await _repository.SaveSessionAsync(session);

        var detailA = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-a");
        var detailB = await _repository.GetDuplicateGroupDetailAsync(session.SessionId, "group-b");

        Assert.NotNull(detailA);
        Assert.Equal("group-a", detailA!.GroupId);
        Assert.Equal("reason-a", detailA.CanonicalReason);
        Assert.Contains(detailA.Evidence, e => e.Signal == "FullHashMatch");

        Assert.NotNull(detailB);
        Assert.Equal("group-b", detailB!.GroupId);
        Assert.Equal("reason-b", detailB.CanonicalReason);
        Assert.Contains(detailB.Evidence, e => e.Signal == "QuickHashOnly");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ScanSession CreateSessionWithEvidenceDuplicates()
    {
        var session = CreateSessionWithFiles();
        session.DuplicateGroups =
        [
            new DuplicateGroup
            {
                GroupId = "group-ev",
                CanonicalPath = @"C:\Documents\file.pdf",
                MatchConfidence = 0.999,
                Confidence = 0.919,
                CanonicalReason = "preferred location; high sensitivity (High)",
                MaxSensitivity = SensitivityLevel.High,
                HasSensitiveMembers = true,
                HasSyncManagedMembers = true,
                HasProtectedMembers = false,
                Paths = [@"C:\Documents\file.pdf", @"C:\Downloads\file (1).pdf"],
                Evidence =
                [
                    new DuplicateEvidenceEntry { Signal = "FullHashMatch", Detail = "All members share identical SHA-256 full-file hash" },
                    new DuplicateEvidenceEntry { Signal = "SizeMatch", Detail = "All members are 1,024 bytes" },
                    new DuplicateEvidenceEntry { Signal = "SensitiveMember", Detail = "Group contains 1 file(s) above low sensitivity (max: High)" }
                ]
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
}
