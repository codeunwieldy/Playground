using Atlas.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of the inventory repository.
/// </summary>
public sealed class InventoryRepository(SqliteConnectionFactory connectionFactory) : IInventoryRepository
{
    public async Task<string> SaveSessionAsync(ScanSession session, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(session.SessionId))
        {
            session.SessionId = Guid.NewGuid().ToString("N");
        }

        var createdUtc = session.CreatedUtc.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            // Session header
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT OR REPLACE INTO scan_sessions (session_id, files_scanned, duplicate_group_count, created_utc, trigger, build_mode, delta_source, baseline_session_id, is_trusted, composition_note)
                    VALUES (@sid, @files, @dupes, @created, @trigger, @buildMode, @deltaSource, @baselineSid, @trusted, @note)
                    """;
                cmd.Parameters.AddWithValue("@sid", session.SessionId);
                cmd.Parameters.AddWithValue("@files", session.Files.Count);
                cmd.Parameters.AddWithValue("@dupes", session.DuplicateGroupCount);
                cmd.Parameters.AddWithValue("@created", createdUtc);
                cmd.Parameters.AddWithValue("@trigger", session.Trigger);
                cmd.Parameters.AddWithValue("@buildMode", session.BuildMode);
                cmd.Parameters.AddWithValue("@deltaSource", session.DeltaSource);
                cmd.Parameters.AddWithValue("@baselineSid", session.BaselineSessionId);
                cmd.Parameters.AddWithValue("@trusted", session.IsTrusted ? 1 : 0);
                cmd.Parameters.AddWithValue("@note", session.CompositionNote);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Roots
            foreach (var root in session.Roots)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT OR REPLACE INTO scan_session_roots (session_id, root_path)
                    VALUES (@sid, @root)
                    """;
                cmd.Parameters.AddWithValue("@sid", session.SessionId);
                cmd.Parameters.AddWithValue("@root", root);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Volumes
            foreach (var vol in session.Volumes)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT OR REPLACE INTO scan_volumes (session_id, root_path, drive_format, drive_type, is_ready, total_size_bytes, free_space_bytes)
                    VALUES (@sid, @root, @fmt, @type, @ready, @total, @free)
                    """;
                cmd.Parameters.AddWithValue("@sid", session.SessionId);
                cmd.Parameters.AddWithValue("@root", vol.RootPath);
                cmd.Parameters.AddWithValue("@fmt", vol.DriveFormat);
                cmd.Parameters.AddWithValue("@type", vol.DriveType);
                cmd.Parameters.AddWithValue("@ready", vol.IsReady ? 1 : 0);
                cmd.Parameters.AddWithValue("@total", vol.TotalSizeBytes);
                cmd.Parameters.AddWithValue("@free", vol.FreeSpaceBytes);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // File snapshots
            foreach (var file in session.Files)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT OR REPLACE INTO file_snapshots (session_id, path, name, extension, category, size_bytes, last_modified_unix, sensitivity, is_sync_managed, is_duplicate_candidate)
                    VALUES (@sid, @path, @name, @ext, @cat, @size, @modified, @sens, @sync, @dupe)
                    """;
                cmd.Parameters.AddWithValue("@sid", session.SessionId);
                cmd.Parameters.AddWithValue("@path", file.Path);
                cmd.Parameters.AddWithValue("@name", file.Name);
                cmd.Parameters.AddWithValue("@ext", file.Extension);
                cmd.Parameters.AddWithValue("@cat", file.Category);
                cmd.Parameters.AddWithValue("@size", file.SizeBytes);
                cmd.Parameters.AddWithValue("@modified", file.LastModifiedUnixTimeSeconds);
                cmd.Parameters.AddWithValue("@sens", (int)file.Sensitivity);
                cmd.Parameters.AddWithValue("@sync", file.IsSyncManaged ? 1 : 0);
                cmd.Parameters.AddWithValue("@dupe", file.IsDuplicateCandidate ? 1 : 0);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Duplicate groups (C-023)
            foreach (var group in session.DuplicateGroups)
            {
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = """
                        INSERT OR REPLACE INTO duplicate_groups (session_id, group_id, canonical_path, match_confidence, cleanup_confidence, canonical_reason, max_sensitivity, has_sensitive_members, has_sync_managed_members, has_protected_members)
                        VALUES (@sid, @gid, @canon, @matchConf, @cleanConf, @reason, @maxSens, @hasSens, @hasSync, @hasProt)
                        """;
                    cmd.Parameters.AddWithValue("@sid", session.SessionId);
                    cmd.Parameters.AddWithValue("@gid", group.GroupId);
                    cmd.Parameters.AddWithValue("@canon", group.CanonicalPath);
                    cmd.Parameters.AddWithValue("@matchConf", group.MatchConfidence);
                    cmd.Parameters.AddWithValue("@cleanConf", group.Confidence);
                    cmd.Parameters.AddWithValue("@reason", group.CanonicalReason);
                    cmd.Parameters.AddWithValue("@maxSens", (int)group.MaxSensitivity);
                    cmd.Parameters.AddWithValue("@hasSens", group.HasSensitiveMembers ? 1 : 0);
                    cmd.Parameters.AddWithValue("@hasSync", group.HasSyncManagedMembers ? 1 : 0);
                    cmd.Parameters.AddWithValue("@hasProt", group.HasProtectedMembers ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                foreach (var memberPath in group.Paths)
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = """
                        INSERT OR REPLACE INTO duplicate_group_members (session_id, group_id, member_path)
                        VALUES (@sid, @gid, @path)
                        """;
                    cmd.Parameters.AddWithValue("@sid", session.SessionId);
                    cmd.Parameters.AddWithValue("@gid", group.GroupId);
                    cmd.Parameters.AddWithValue("@path", memberPath);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // Evidence (C-025)
                foreach (var evidence in group.Evidence)
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = """
                        INSERT OR REPLACE INTO duplicate_group_evidence (session_id, group_id, signal, detail)
                        VALUES (@sid, @gid, @signal, @detail)
                        """;
                    cmd.Parameters.AddWithValue("@sid", session.SessionId);
                    cmd.Parameters.AddWithValue("@gid", group.GroupId);
                    cmd.Parameters.AddWithValue("@signal", evidence.Signal);
                    cmd.Parameters.AddWithValue("@detail", evidence.Detail);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return session.SessionId;
    }

    public async Task<ScanSessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                s.session_id,
                s.files_scanned,
                s.duplicate_group_count,
                s.created_utc,
                (SELECT COUNT(*) FROM scan_session_roots r WHERE r.session_id = s.session_id) AS root_count,
                (SELECT COUNT(*) FROM scan_volumes v WHERE v.session_id = s.session_id) AS volume_count,
                s.trigger,
                s.build_mode,
                s.delta_source,
                s.baseline_session_id,
                s.is_trusted,
                s.composition_note
            FROM scan_sessions s
            WHERE s.session_id = @sid
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new ScanSessionSummary(
                SessionId: reader.GetString(0),
                FilesScanned: reader.GetInt32(1),
                DuplicateGroupCount: reader.GetInt32(2),
                RootCount: reader.GetInt32(4),
                VolumeCount: reader.GetInt32(5),
                CreatedUtc: DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Trigger: reader.GetString(6),
                BuildMode: reader.GetString(7),
                DeltaSource: reader.GetString(8),
                BaselineSessionId: reader.GetString(9),
                IsTrusted: reader.GetInt32(10) != 0,
                CompositionNote: reader.GetString(11));
        }

        return null;
    }

    public async Task<ScanSessionSummary?> GetLatestSessionAsync(CancellationToken ct = default)
    {
        var sessions = await ListSessionsAsync(1, 0, ct);
        return sessions.Count > 0 ? sessions[0] : null;
    }

    public async Task<IReadOnlyList<ScanSessionSummary>> ListSessionsAsync(int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                s.session_id,
                s.files_scanned,
                s.duplicate_group_count,
                s.created_utc,
                (SELECT COUNT(*) FROM scan_session_roots r WHERE r.session_id = s.session_id) AS root_count,
                (SELECT COUNT(*) FROM scan_volumes v WHERE v.session_id = s.session_id) AS volume_count,
                s.trigger,
                s.build_mode,
                s.delta_source,
                s.baseline_session_id,
                s.is_trusted,
                s.composition_note
            FROM scan_sessions s
            ORDER BY s.created_utc DESC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var results = new List<ScanSessionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ScanSessionSummary(
                SessionId: reader.GetString(0),
                FilesScanned: reader.GetInt32(1),
                DuplicateGroupCount: reader.GetInt32(2),
                RootCount: reader.GetInt32(4),
                VolumeCount: reader.GetInt32(5),
                CreatedUtc: DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Trigger: reader.GetString(6),
                BuildMode: reader.GetString(7),
                DeltaSource: reader.GetString(8),
                BaselineSessionId: reader.GetString(9),
                IsTrusted: reader.GetInt32(10) != 0,
                CompositionNote: reader.GetString(11)));
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetRootsForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT root_path FROM scan_session_roots WHERE session_id = @sid ORDER BY root_path";
        cmd.Parameters.AddWithValue("@sid", sessionId);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task<IReadOnlyList<VolumeSnapshot>> GetVolumesForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT root_path, drive_format, drive_type, is_ready, total_size_bytes, free_space_bytes
            FROM scan_volumes
            WHERE session_id = @sid
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);

        var results = new List<VolumeSnapshot>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new VolumeSnapshot
            {
                RootPath = reader.GetString(0),
                DriveFormat = reader.GetString(1),
                DriveType = reader.GetString(2),
                IsReady = reader.GetInt32(3) != 0,
                TotalSizeBytes = reader.GetInt64(4),
                FreeSpaceBytes = reader.GetInt64(5)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<FileInventoryItem>> GetFilesForSessionAsync(string sessionId, int limit = 1000, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT path, name, extension, category, size_bytes, last_modified_unix, sensitivity, is_sync_managed, is_duplicate_candidate
            FROM file_snapshots
            WHERE session_id = @sid
            ORDER BY path
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var results = new List<FileInventoryItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FileInventoryItem
            {
                Path = reader.GetString(0),
                Name = reader.GetString(1),
                Extension = reader.GetString(2),
                Category = reader.GetString(3),
                SizeBytes = reader.GetInt64(4),
                LastModifiedUnixTimeSeconds = reader.GetInt64(5),
                Sensitivity = (SensitivityLevel)reader.GetInt32(6),
                IsSyncManaged = reader.GetInt32(7) != 0,
                IsDuplicateCandidate = reader.GetInt32(8) != 0
            });
        }

        return results;
    }

    public async Task<int> GetFileCountForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM file_snapshots WHERE session_id = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<SessionDiffSummary> DiffSessionsAsync(string olderSessionId, string newerSessionId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        // Use a single query with LEFT JOINs to compute counts in one pass.
        // Added: in newer but not older
        // Removed: in older but not newer
        // Changed: in both but size_bytes or last_modified_unix differ
        // Unchanged: in both with identical size_bytes and last_modified_unix
        // SQLite does not support FULL OUTER JOIN. Emulate with LEFT JOIN + anti-join via UNION ALL.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                SUM(CASE WHEN change_kind = 'Added' THEN 1 ELSE 0 END),
                SUM(CASE WHEN change_kind = 'Removed' THEN 1 ELSE 0 END),
                SUM(CASE WHEN change_kind = 'Changed' THEN 1 ELSE 0 END),
                SUM(CASE WHEN change_kind = 'Unchanged' THEN 1 ELSE 0 END)
            FROM (
                SELECT
                    CASE
                        WHEN o.path IS NULL THEN 'Added'
                        WHEN o.size_bytes != n.size_bytes OR o.last_modified_unix != n.last_modified_unix THEN 'Changed'
                        ELSE 'Unchanged'
                    END AS change_kind
                FROM (SELECT path, size_bytes, last_modified_unix FROM file_snapshots WHERE session_id = @newer) n
                LEFT JOIN (SELECT path, size_bytes, last_modified_unix FROM file_snapshots WHERE session_id = @older) o ON n.path = o.path

                UNION ALL

                SELECT 'Removed' AS change_kind
                FROM (SELECT path FROM file_snapshots WHERE session_id = @older) o2
                LEFT JOIN (SELECT path FROM file_snapshots WHERE session_id = @newer) n2 ON o2.path = n2.path
                WHERE n2.path IS NULL
            )
            """;
        cmd.Parameters.AddWithValue("@older", olderSessionId);
        cmd.Parameters.AddWithValue("@newer", newerSessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new SessionDiffSummary(
                OlderSessionId: olderSessionId,
                NewerSessionId: newerSessionId,
                AddedCount: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                RemovedCount: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                ChangedCount: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                UnchangedCount: reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
        }

        return new SessionDiffSummary(olderSessionId, newerSessionId, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<SessionDiffFile>> GetDiffFilesAsync(string olderSessionId, string newerSessionId, int limit = 200, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        // SQLite doesn't support FULL OUTER JOIN natively - emulate with UNION.
        // Return only non-unchanged rows (Added, Removed, Changed), ordered by path.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT path, change_kind, older_size, newer_size, older_modified, newer_modified
            FROM (
                SELECT
                    n.path AS path,
                    CASE
                        WHEN o.path IS NULL THEN 'Added'
                        WHEN o.size_bytes != n.size_bytes OR o.last_modified_unix != n.last_modified_unix THEN 'Changed'
                        ELSE 'Unchanged'
                    END AS change_kind,
                    o.size_bytes AS older_size,
                    n.size_bytes AS newer_size,
                    o.last_modified_unix AS older_modified,
                    n.last_modified_unix AS newer_modified
                FROM (
                    SELECT path, size_bytes, last_modified_unix FROM file_snapshots WHERE session_id = @newer
                ) n
                LEFT JOIN (
                    SELECT path, size_bytes, last_modified_unix FROM file_snapshots WHERE session_id = @older
                ) o ON n.path = o.path

                UNION ALL

                SELECT
                    o.path AS path,
                    'Removed' AS change_kind,
                    o.size_bytes AS older_size,
                    NULL AS newer_size,
                    o.last_modified_unix AS older_modified,
                    NULL AS newer_modified
                FROM (
                    SELECT path, size_bytes, last_modified_unix FROM file_snapshots WHERE session_id = @older
                ) o
                LEFT JOIN (
                    SELECT path FROM file_snapshots WHERE session_id = @newer
                ) n ON o.path = n.path
                WHERE n.path IS NULL
            )
            WHERE change_kind != 'Unchanged'
            ORDER BY path
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@older", olderSessionId);
        cmd.Parameters.AddWithValue("@newer", newerSessionId);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var results = new List<SessionDiffFile>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SessionDiffFile(
                Path: reader.GetString(0),
                ChangeKind: reader.GetString(1),
                OlderSizeBytes: reader.IsDBNull(2) ? null : reader.GetInt64(2),
                NewerSizeBytes: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                OlderLastModifiedUnix: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                NewerLastModifiedUnix: reader.IsDBNull(5) ? null : reader.GetInt64(5)));
        }

        return results;
    }

    public async Task<IReadOnlyList<PersistedDuplicateGroup>> GetDuplicateGroupsForSessionAsync(string sessionId, int limit = 200, int offset = 0, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        // Read group headers
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT group_id, canonical_path, match_confidence, cleanup_confidence, canonical_reason, max_sensitivity, has_sensitive_members, has_sync_managed_members, has_protected_members
            FROM duplicate_groups
            WHERE session_id = @sid
            ORDER BY cleanup_confidence DESC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var groups = new List<(string GroupId, string CanonicalPath, double MatchConfidence, double CleanupConfidence, string CanonicalReason, SensitivityLevel MaxSensitivity, bool HasSensitive, bool HasSync, bool HasProtected)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                groups.Add((
                    GroupId: reader.GetString(0),
                    CanonicalPath: reader.GetString(1),
                    MatchConfidence: reader.GetDouble(2),
                    CleanupConfidence: reader.GetDouble(3),
                    CanonicalReason: reader.GetString(4),
                    MaxSensitivity: (SensitivityLevel)reader.GetInt32(5),
                    HasSensitive: reader.GetInt32(6) != 0,
                    HasSync: reader.GetInt32(7) != 0,
                    HasProtected: reader.GetInt32(8) != 0));
            }
        }

        if (groups.Count == 0)
            return [];

        // Read member paths for each group
        var results = new List<PersistedDuplicateGroup>(groups.Count);
        foreach (var g in groups)
        {
            await using var memberCmd = connection.CreateCommand();
            memberCmd.CommandText = """
                SELECT member_path FROM duplicate_group_members
                WHERE session_id = @sid AND group_id = @gid
                ORDER BY member_path
                """;
            memberCmd.Parameters.AddWithValue("@sid", sessionId);
            memberCmd.Parameters.AddWithValue("@gid", g.GroupId);

            var members = new List<string>();
            await using var memberReader = await memberCmd.ExecuteReaderAsync(ct);
            while (await memberReader.ReadAsync(ct))
            {
                members.Add(memberReader.GetString(0));
            }

            results.Add(new PersistedDuplicateGroup(
                GroupId: g.GroupId,
                CanonicalPath: g.CanonicalPath,
                MatchConfidence: g.MatchConfidence,
                CleanupConfidence: g.CleanupConfidence,
                CanonicalReason: g.CanonicalReason,
                MaxSensitivity: g.MaxSensitivity,
                HasSensitiveMembers: g.HasSensitive,
                HasSyncManagedMembers: g.HasSync,
                HasProtectedMembers: g.HasProtected,
                MemberPaths: members));
        }

        return results;
    }

    public async Task<int> GetDuplicateGroupCountForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM duplicate_groups WHERE session_id = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<FileInventoryItem?> GetFileForSessionAsync(string sessionId, string filePath, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT path, name, extension, category, size_bytes, last_modified_unix, sensitivity, is_sync_managed, is_duplicate_candidate
            FROM file_snapshots
            WHERE session_id = @sid AND path = @path
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@path", filePath);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new FileInventoryItem
            {
                Path = reader.GetString(0),
                Name = reader.GetString(1),
                Extension = reader.GetString(2),
                Category = reader.GetString(3),
                SizeBytes = reader.GetInt64(4),
                LastModifiedUnixTimeSeconds = reader.GetInt64(5),
                Sensitivity = (SensitivityLevel)reader.GetInt32(6),
                IsSyncManaged = reader.GetInt32(7) != 0,
                IsDuplicateCandidate = reader.GetInt32(8) != 0
            };
        }

        return null;
    }

    // ── Duplicate group detail (C-025) ──────────────────────────────────────

    public async Task<PersistedDuplicateGroupDetail?> GetDuplicateGroupDetailAsync(string sessionId, string groupId, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        // 1. Read group header
        await using var headerCmd = connection.CreateCommand();
        headerCmd.CommandText = """
            SELECT canonical_path, match_confidence, cleanup_confidence, canonical_reason, max_sensitivity, has_sensitive_members, has_sync_managed_members, has_protected_members
            FROM duplicate_groups
            WHERE session_id = @sid AND group_id = @gid
            """;
        headerCmd.Parameters.AddWithValue("@sid", sessionId);
        headerCmd.Parameters.AddWithValue("@gid", groupId);

        string canonicalPath;
        double matchConfidence, cleanupConfidence;
        string canonicalReason;
        SensitivityLevel maxSensitivity;
        bool hasSensitive, hasSync, hasProtected;

        await using (var reader = await headerCmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return null;

            canonicalPath = reader.GetString(0);
            matchConfidence = reader.GetDouble(1);
            cleanupConfidence = reader.GetDouble(2);
            canonicalReason = reader.GetString(3);
            maxSensitivity = (SensitivityLevel)reader.GetInt32(4);
            hasSensitive = reader.GetInt32(5) != 0;
            hasSync = reader.GetInt32(6) != 0;
            hasProtected = reader.GetInt32(7) != 0;
        }

        // 2. Read member paths
        await using var memberCmd = connection.CreateCommand();
        memberCmd.CommandText = """
            SELECT member_path FROM duplicate_group_members
            WHERE session_id = @sid AND group_id = @gid
            ORDER BY member_path
            """;
        memberCmd.Parameters.AddWithValue("@sid", sessionId);
        memberCmd.Parameters.AddWithValue("@gid", groupId);

        var members = new List<string>();
        await using (var memberReader = await memberCmd.ExecuteReaderAsync(ct))
        {
            while (await memberReader.ReadAsync(ct))
                members.Add(memberReader.GetString(0));
        }

        // 3. Read evidence
        await using var evidenceCmd = connection.CreateCommand();
        evidenceCmd.CommandText = """
            SELECT signal, detail FROM duplicate_group_evidence
            WHERE session_id = @sid AND group_id = @gid
            ORDER BY signal
            """;
        evidenceCmd.Parameters.AddWithValue("@sid", sessionId);
        evidenceCmd.Parameters.AddWithValue("@gid", groupId);

        var evidence = new List<PersistedDuplicateEvidence>();
        await using (var evidenceReader = await evidenceCmd.ExecuteReaderAsync(ct))
        {
            while (await evidenceReader.ReadAsync(ct))
                evidence.Add(new PersistedDuplicateEvidence(evidenceReader.GetString(0), evidenceReader.GetString(1)));
        }

        return new PersistedDuplicateGroupDetail(
            GroupId: groupId,
            CanonicalPath: canonicalPath,
            MatchConfidence: matchConfidence,
            CleanupConfidence: cleanupConfidence,
            CanonicalReason: canonicalReason,
            MaxSensitivity: maxSensitivity,
            HasSensitiveMembers: hasSensitive,
            HasSyncManagedMembers: hasSync,
            HasProtectedMembers: hasProtected,
            MemberPaths: members,
            Evidence: evidence);
    }

    // ── File lookup by paths (C-026) ────────────────────────────────────────

    public async Task<IReadOnlyList<FileInventoryItem>> GetFilesForPathsAsync(
        string sessionId, IEnumerable<string> paths, CancellationToken ct = default)
    {
        var pathList = paths?.ToList() ?? [];
        if (pathList.Count == 0)
            return [];

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        var results = new List<FileInventoryItem>(pathList.Count);
        foreach (var path in pathList)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT path, name, extension, category, size_bytes, last_modified_unix, sensitivity, is_sync_managed, is_duplicate_candidate
                FROM file_snapshots
                WHERE session_id = @sid AND path = @path
                """;
            cmd.Parameters.AddWithValue("@sid", sessionId);
            cmd.Parameters.AddWithValue("@path", path);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                results.Add(new FileInventoryItem
                {
                    Path = reader.GetString(0),
                    Name = reader.GetString(1),
                    Extension = reader.GetString(2),
                    Category = reader.GetString(3),
                    SizeBytes = reader.GetInt64(4),
                    LastModifiedUnixTimeSeconds = reader.GetInt64(5),
                    Sensitivity = (SensitivityLevel)reader.GetInt32(6),
                    IsSyncManaged = reader.GetInt32(7) != 0,
                    IsDuplicateCandidate = reader.GetInt32(8) != 0
                });
            }
        }

        return results;
    }
}
