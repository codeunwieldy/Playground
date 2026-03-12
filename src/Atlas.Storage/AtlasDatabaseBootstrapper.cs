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

        // Additive schema migrations — safe to re-run on existing databases.
        foreach (var migration in AdditiveColumnMigrations)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = migration;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column"))
            {
                // Column already exists — safe to ignore.
            }
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
        """,

        // ── Inventory persistence (C-010) ───────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS scan_sessions (
            session_id TEXT PRIMARY KEY,
            files_scanned INTEGER NOT NULL,
            duplicate_group_count INTEGER NOT NULL,
            created_utc TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS scan_session_roots (
            session_id TEXT NOT NULL,
            root_path TEXT NOT NULL,
            PRIMARY KEY (session_id, root_path)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS scan_volumes (
            session_id TEXT NOT NULL,
            root_path TEXT NOT NULL,
            drive_format TEXT NOT NULL,
            drive_type TEXT NOT NULL,
            is_ready INTEGER NOT NULL,
            total_size_bytes INTEGER NOT NULL,
            free_space_bytes INTEGER NOT NULL,
            PRIMARY KEY (session_id, root_path)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS file_snapshots (
            session_id TEXT NOT NULL,
            path TEXT NOT NULL,
            name TEXT NOT NULL,
            extension TEXT NOT NULL,
            category TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            last_modified_unix INTEGER NOT NULL,
            sensitivity INTEGER NOT NULL,
            is_sync_managed INTEGER NOT NULL,
            is_duplicate_candidate INTEGER NOT NULL,
            PRIMARY KEY (session_id, path)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS idx_file_snapshots_session ON file_snapshots (session_id);
        """,

        // ── Duplicate group persistence (C-023) ─────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS duplicate_groups (
            session_id TEXT NOT NULL,
            group_id TEXT NOT NULL,
            canonical_path TEXT NOT NULL,
            match_confidence REAL NOT NULL,
            cleanup_confidence REAL NOT NULL,
            canonical_reason TEXT NOT NULL DEFAULT '',
            max_sensitivity INTEGER NOT NULL DEFAULT 0,
            has_sensitive_members INTEGER NOT NULL DEFAULT 0,
            has_sync_managed_members INTEGER NOT NULL DEFAULT 0,
            has_protected_members INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (session_id, group_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS duplicate_group_members (
            session_id TEXT NOT NULL,
            group_id TEXT NOT NULL,
            member_path TEXT NOT NULL,
            PRIMARY KEY (session_id, group_id, member_path)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS idx_duplicate_groups_session ON duplicate_groups (session_id);
        """,

        // ── Duplicate group evidence persistence (C-025) ────────────────────
        """
        CREATE TABLE IF NOT EXISTS duplicate_group_evidence (
            session_id TEXT NOT NULL,
            group_id TEXT NOT NULL,
            signal TEXT NOT NULL,
            detail TEXT NOT NULL,
            PRIMARY KEY (session_id, group_id, signal)
        );
        """,

        // ── USN checkpoint persistence (C-014) ──────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS usn_checkpoints (
            volume_id TEXT PRIMARY KEY,
            journal_id INTEGER NOT NULL,
            last_usn INTEGER NOT NULL,
            updated_utc TEXT NOT NULL
        );
        """
    ];

    // ── Additive column migrations (C-016) ──────────────────────────────────
    // Each ALTER TABLE is run individually; duplicate-column errors are caught
    // and silently ignored so the bootstrapper is idempotent.
    private static IReadOnlyList<string> AdditiveColumnMigrations =>
    [
        "ALTER TABLE scan_sessions ADD COLUMN trigger TEXT NOT NULL DEFAULT 'Manual';",
        "ALTER TABLE scan_sessions ADD COLUMN build_mode TEXT NOT NULL DEFAULT 'FullRescan';",
        "ALTER TABLE scan_sessions ADD COLUMN delta_source TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE scan_sessions ADD COLUMN baseline_session_id TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE scan_sessions ADD COLUMN is_trusted INTEGER NOT NULL DEFAULT 1;",
        "ALTER TABLE scan_sessions ADD COLUMN composition_note TEXT NOT NULL DEFAULT '';"
    ];
}