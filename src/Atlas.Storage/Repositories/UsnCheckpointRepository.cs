namespace Atlas.Storage.Repositories;

public sealed class UsnCheckpointRepository(SqliteConnectionFactory connectionFactory)
    : IUsnCheckpointRepository
{
    public async Task<UsnCheckpoint?> GetCheckpointAsync(string volumeId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT volume_id, journal_id, last_usn, updated_utc
            FROM usn_checkpoints WHERE volume_id = @vid
            """;
        cmd.Parameters.AddWithValue("@vid", volumeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new UsnCheckpoint
            {
                VolumeId = reader.GetString(0),
                JournalId = (ulong)reader.GetInt64(1),
                LastUsn = reader.GetInt64(2),
                UpdatedUtc = DateTime.Parse(reader.GetString(3), null,
                    System.Globalization.DateTimeStyles.RoundtripKind)
            };
        }
        return null;
    }

    public async Task SaveCheckpointAsync(UsnCheckpoint checkpoint, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO usn_checkpoints
                (volume_id, journal_id, last_usn, updated_utc)
            VALUES (@vid, @jid, @usn, @updated)
            """;
        cmd.Parameters.AddWithValue("@vid", checkpoint.VolumeId);
        cmd.Parameters.AddWithValue("@jid", (long)checkpoint.JournalId);
        cmd.Parameters.AddWithValue("@usn", checkpoint.LastUsn);
        cmd.Parameters.AddWithValue("@updated", checkpoint.UpdatedUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteCheckpointAsync(string volumeId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM usn_checkpoints WHERE volume_id = @vid";
        cmd.Parameters.AddWithValue("@vid", volumeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
