using Atlas.Core.Contracts;

namespace Atlas.Storage.Repositories;

/// <summary>
/// Repository for managing policy profiles and configuration settings.
/// </summary>
public interface IConfigurationRepository
{
    /// <summary>
    /// Persists a policy profile, creating or updating as needed.
    /// </summary>
    Task SaveProfileAsync(PolicyProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a policy profile by its name.
    /// </summary>
    Task<PolicyProfile?> GetProfileAsync(string profileName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the default policy profile.
    /// </summary>
    Task<PolicyProfile> GetDefaultProfileAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all available policy profiles.
    /// </summary>
    Task<IReadOnlyList<PolicyProfileSummary>> ListProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes a policy profile by its name.
    /// </summary>
    Task<bool> DeleteProfileAsync(string profileName, CancellationToken ct = default);

    /// <summary>
    /// Checks if a policy profile exists.
    /// </summary>
    Task<bool> ProfileExistsAsync(string profileName, CancellationToken ct = default);
}

/// <summary>
/// Summary projection of a policy profile for listing purposes.
/// </summary>
/// <param name="ProfileName">Name of the policy profile.</param>
/// <param name="ScanRootCount">Number of configured scan roots.</param>
/// <param name="MutableRootCount">Number of configured mutable roots.</param>
/// <param name="CreatedUtc">When the profile was created.</param>
public sealed record PolicyProfileSummary(string ProfileName, int ScanRootCount, int MutableRootCount, DateTime CreatedUtc);
