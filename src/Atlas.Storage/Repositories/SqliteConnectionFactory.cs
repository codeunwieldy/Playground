using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// Factory for creating SQLite connections using the bootstrapper's database path.
/// </summary>
public sealed class SqliteConnectionFactory(AtlasDatabaseBootstrapper bootstrapper)
{
    public SqliteConnection CreateConnection() => new($"Data Source={bootstrapper.DatabasePath}");
}
