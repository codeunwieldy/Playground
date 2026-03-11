using Atlas.Storage.Repositories;

namespace Atlas.Storage.Tests;

/// <summary>
/// Test fixture that creates and manages an isolated SQLite database for repository tests.
/// </summary>
public sealed class TestDatabaseFixture : IDisposable
{
    public string DatabasePath { get; }
    public AtlasDatabaseBootstrapper Bootstrapper { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }

    public TestDatabaseFixture()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"atlas_test_{Guid.NewGuid():N}.db");
        var options = new StorageOptions
        {
            DataRoot = Path.GetDirectoryName(DatabasePath)!,
            DatabaseFileName = Path.GetFileName(DatabasePath)
        };
        Bootstrapper = new AtlasDatabaseBootstrapper(options);
        Bootstrapper.InitializeAsync().GetAwaiter().GetResult();
        ConnectionFactory = new SqliteConnectionFactory(Bootstrapper);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(DatabasePath);
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}
