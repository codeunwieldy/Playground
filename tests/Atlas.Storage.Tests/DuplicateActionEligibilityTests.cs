using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

public sealed class DuplicateActionEligibilityTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repository;

    public DuplicateActionEligibilityTests()
    {
        _fixture = new TestDatabaseFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetFilesForPaths_ReturnsMatchingFiles()
    {
        var session = CreateSessionWithFiles();
        await _repository.SaveSessionAsync(session);

        var result = await _repository.GetFilesForPathsAsync(
            session.SessionId,
            [@"C:\Documents\report.pdf", @"C:\Downloads\report-copy.pdf"]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.Path == @"C:\Documents\report.pdf");
        Assert.Contains(result, f => f.Path == @"C:\Downloads\report-copy.pdf");
    }

    [Fact]
    public async Task GetFilesForPaths_MissingPathsOmitted()
    {
        var session = CreateSessionWithFiles();
        await _repository.SaveSessionAsync(session);

        var result = await _repository.GetFilesForPathsAsync(
            session.SessionId,
            [@"C:\Documents\report.pdf", @"C:\Nonexistent\missing.txt"]);

        Assert.Single(result);
        Assert.Equal(@"C:\Documents\report.pdf", result[0].Path);
    }

    [Fact]
    public async Task GetFilesForPaths_EmptyPaths_ReturnsEmpty()
    {
        var session = CreateSessionWithFiles();
        await _repository.SaveSessionAsync(session);

        var result = await _repository.GetFilesForPathsAsync(session.SessionId, []);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilesForPaths_SessionIsolation()
    {
        var session1 = CreateSessionWithFiles();
        var session2 = new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Other\data.xlsx", Name = "data.xlsx",
                    Extension = ".xlsx", Category = "Documents",
                    SizeBytes = 512, LastModifiedUnixTimeSeconds = 200
                }
            ]
        };
        await _repository.SaveSessionAsync(session1);
        await _repository.SaveSessionAsync(session2);

        var result = await _repository.GetFilesForPathsAsync(
            session1.SessionId, [@"C:\Other\data.xlsx"]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilesForPaths_PreservesFieldValues()
    {
        var session = new ScanSession
        {
            Roots = [@"C:\"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Finance\tax.pdf", Name = "tax.pdf",
                    Extension = ".pdf", Category = "Documents",
                    SizeBytes = 4096, LastModifiedUnixTimeSeconds = 300,
                    Sensitivity = SensitivityLevel.High,
                    IsSyncManaged = true, IsDuplicateCandidate = true
                }
            ]
        };
        await _repository.SaveSessionAsync(session);

        var result = await _repository.GetFilesForPathsAsync(
            session.SessionId, [@"C:\Finance\tax.pdf"]);

        var file = Assert.Single(result);
        Assert.Equal(SensitivityLevel.High, file.Sensitivity);
        Assert.True(file.IsSyncManaged);
        Assert.True(file.IsDuplicateCandidate);
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
                    Path = @"C:\Documents\report.pdf", Name = "report.pdf",
                    Extension = ".pdf", Category = "Documents",
                    SizeBytes = 2048, LastModifiedUnixTimeSeconds = 100
                },
                new FileInventoryItem
                {
                    Path = @"C:\Downloads\report-copy.pdf", Name = "report-copy.pdf",
                    Extension = ".pdf", Category = "Documents",
                    SizeBytes = 2048, LastModifiedUnixTimeSeconds = 100
                }
            ]
        };
    }
}
