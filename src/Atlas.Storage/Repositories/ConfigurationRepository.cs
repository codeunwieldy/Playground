using System.Globalization;
using System.Text.Json;
using Atlas.Core.Contracts;
using Atlas.Core.Policies;
using Microsoft.Data.Sqlite;

namespace Atlas.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of the configuration repository for policy profiles.
/// </summary>
public sealed class ConfigurationRepository(SqliteConnectionFactory connectionFactory) : IConfigurationRepository
{
    private const string DefaultProfileName = "Windows 11 Consumer Default";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task SaveProfileAsync(PolicyProfile profile, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        var payload = AtlasJsonCompression.Compress(json);
        var createdUtc = DateTime.UtcNow.ToString("o");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO policy_profiles (profile_name, payload, created_utc)
            VALUES (@profile_name, @payload, @created_utc)
            """;
        command.Parameters.AddWithValue("@profile_name", profile.ProfileName);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@created_utc", createdUtc);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<PolicyProfile?> GetProfileAsync(string profileName, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM policy_profiles WHERE profile_name = @profile_name";
        command.Parameters.AddWithValue("@profile_name", profileName);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var payload = (byte[])reader["payload"];
        var json = AtlasJsonCompression.Decompress(payload);
        return JsonSerializer.Deserialize<PolicyProfile>(json, JsonOptions);
    }

    public async Task<PolicyProfile> GetDefaultProfileAsync(CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(DefaultProfileName, ct);
        if (profile is not null)
        {
            return profile;
        }

        // Create and save the default profile if it doesn't exist
        profile = PolicyProfileFactory.CreateDefault();
        await SaveProfileAsync(profile, ct);
        return profile;
    }

    public async Task<IReadOnlyList<PolicyProfileSummary>> ListProfilesAsync(CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT profile_name, payload, created_utc
            FROM policy_profiles
            ORDER BY profile_name
            """;

        var results = new List<PolicyProfileSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = (byte[])reader["payload"];
            var json = AtlasJsonCompression.Decompress(payload);
            var profile = JsonSerializer.Deserialize<PolicyProfile>(json, JsonOptions);

            results.Add(new PolicyProfileSummary(
                ProfileName: reader.GetString(0),
                ScanRootCount: profile?.ScanRoots.Count ?? 0,
                MutableRootCount: profile?.MutableRoots.Count ?? 0,
                CreatedUtc: DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind)
            ));
        }

        return results;
    }

    public async Task<bool> DeleteProfileAsync(string profileName, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM policy_profiles WHERE profile_name = @profile_name";
        command.Parameters.AddWithValue("@profile_name", profileName);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<bool> ProfileExistsAsync(string profileName, CancellationToken ct = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM policy_profiles WHERE profile_name = @profile_name";
        command.Parameters.AddWithValue("@profile_name", profileName);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct);
    }
}
