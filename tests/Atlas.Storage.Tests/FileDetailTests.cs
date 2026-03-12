using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

public sealed class FileDetailTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly InventoryRepository _repository;

    public FileDetailTests()
    {
        _fixture = new TestDatabaseFixture();
        _repository = new InventoryRepository(_fixture.ConnectionFactory);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetFileForSession_ExistingFile_ReturnsItem()
    {
        var session = CreateSession();
        await _repository.SaveSessionAsync(session);

        var file = await _repository.GetFileForSessionAsync(session.SessionId, @"C:\Documents\report.pdf");

        Assert.NotNull(file);
        Assert.Equal(@"C:\Documents\report.pdf", file!.Path);
        Assert.Equal("report.pdf", file.Name);
        Assert.Equal(".pdf", file.Extension);
        Assert.Equal("Documents", file.Category);
        Assert.Equal(2048L, file.SizeBytes);
        Assert.Equal(SensitivityLevel.Medium, file.Sensitivity);
    }

    [Fact]
    public async Task GetFileForSession_MissingFile_ReturnsNull()
    {
        var session = CreateSession();
        await _repository.SaveSessionAsync(session);

        var file = await _repository.GetFileForSessionAsync(session.SessionId, @"C:\Nonexistent\file.txt");

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileForSession_WrongSession_ReturnsNull()
    {
        var session = CreateSession();
        await _repository.SaveSessionAsync(session);

        var file = await _repository.GetFileForSessionAsync("nonexistent-session", @"C:\Documents\report.pdf");

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileForSession_PreservesSyncAndDuplicateFlags()
    {
        var session = CreateSession();
        session.Files[0].IsSyncManaged = true;
        session.Files[0].IsDuplicateCandidate = true;
        await _repository.SaveSessionAsync(session);

        var file = await _repository.GetFileForSessionAsync(session.SessionId, @"C:\Documents\report.pdf");

        Assert.NotNull(file);
        Assert.True(file!.IsSyncManaged);
        Assert.True(file.IsDuplicateCandidate);
    }

    private static ScanSession CreateSession()
    {
        return new ScanSession
        {
            Roots = [@"C:\Documents"],
            Files =
            [
                new FileInventoryItem
                {
                    Path = @"C:\Documents\report.pdf",
                    Name = "report.pdf",
                    Extension = ".pdf",
                    Category = "Documents",
                    SizeBytes = 2048,
                    LastModifiedUnixTimeSeconds = 1000,
                    Sensitivity = SensitivityLevel.Medium,
                    IsSyncManaged = false,
                    IsDuplicateCandidate = false
                }
            ]
        };
    }
}
