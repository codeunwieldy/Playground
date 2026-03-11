using Microsoft.Data.Sqlite;

namespace Atlas.Storage;

public sealed class AtlasDatabaseBootstrapper(StorageOptions options)
{
    public string DatabasePath => Path.Combine(options.DataRoot, options.DatabaseFileName);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.DataRoot);

        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);

        foreach (var statement in SchemaStatements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<string> SchemaStatements =>
    [
        """
        CREATE TABLE IF NOT EXISTS plans (
            plan_id TEXT PRIMARY KEY,
            scope TEXT NOT NULL,
            summary TEXT NOT NULL,
            payload BLOB NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS execution_batches (
            batch_id TEXT PRIMARY KEY,
            plan_id TEXT NOT NULL,
            payload BLOB NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS undo_checkpoints (
            checkpoint_id TEXT PRIMARY KEY,
            batch_id TEXT NOT NULL,
            payload BLOB NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS optimization_findings (
            finding_id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            target TEXT NOT NULL,
            payload BLOB NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS conversations (
            conversation_id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            summary TEXT NOT NULL,
            payload BLOB NOT NULL,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NULL
        );
        """,
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS conversation_fts USING fts5(conversation_id, summary, content);
        """,
        """
        CREATE TABLE IF NOT EXISTS prompt_traces (
            trace_id TEXT PRIMARY KEY,
            stage TEXT NOT NULL,
            prompt_payload BLOB NOT NULL,
            response_payload BLOB NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS policy_profiles (
            profile_name TEXT PRIMARY KEY,
            payload BLOB NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS quarantine_items (
            quarantine_id TEXT PRIMARY KEY,
            original_path TEXT NOT NULL,
            current_path TEXT NOT NULL,
            plan_id TEXT NOT NULL,
            retention_until_utc TEXT NOT NULL,
            payload BLOB NOT NULL
        );
        """
    ];
}