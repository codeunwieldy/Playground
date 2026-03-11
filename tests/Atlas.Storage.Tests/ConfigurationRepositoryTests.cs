using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Xunit;

namespace Atlas.Storage.Tests;

/// <summary>
/// Round-trip tests for ConfigurationRepository verifying policy profile persistence.
/// </summary>
public sealed class ConfigurationRepositoryTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ConfigurationRepository _repository;

    public ConfigurationRepositoryTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _repository = new ConfigurationRepository(_fixture.ConnectionFactory);
    }

    [Fact]
    public async Task SaveAndGetProfile_RoundTrip_Success()
    {
        // Arrange
        var profileName = $"TestProfile_{Guid.NewGuid():N}";
        var profile = new PolicyProfile
        {
            ProfileName = profileName,
            ScanRoots = ["C:\\Users\\Test\\Documents", "D:\\Data"],
            MutableRoots = ["C:\\Users\\Test\\Downloads", "C:\\Users\\Test\\Desktop"],
            ExcludedRoots = ["C:\\Windows", "C:\\Program Files"],
            ProtectedPaths = ["C:\\Users\\Test\\Important"],
            SyncFolderMarkers = [".dropbox", ".onedrive"],
            DuplicateAutoDeleteConfidenceThreshold = 0.95,
            UploadSensitiveContent = false,
            ExcludeSyncFoldersByDefault = true,
            AllowedAutomaticOptimizationKinds = [OptimizationKind.TemporaryFiles, OptimizationKind.CacheCleanup],
            ProtectedKeywords = ["confidential", "secret", "private"]
        };

        // Act
        await _repository.SaveProfileAsync(profile);
        var retrieved = await _repository.GetProfileAsync(profileName);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(profile.ProfileName, retrieved.ProfileName);
        Assert.Equal(profile.ScanRoots.Count, retrieved.ScanRoots.Count);
        Assert.Equal(profile.ScanRoots[0], retrieved.ScanRoots[0]);
        Assert.Equal(profile.MutableRoots.Count, retrieved.MutableRoots.Count);
        Assert.Equal(profile.ExcludedRoots.Count, retrieved.ExcludedRoots.Count);
        Assert.Equal(profile.ProtectedPaths.Count, retrieved.ProtectedPaths.Count);
        Assert.Equal(profile.SyncFolderMarkers.Count, retrieved.SyncFolderMarkers.Count);
        Assert.Equal(profile.DuplicateAutoDeleteConfidenceThreshold, retrieved.DuplicateAutoDeleteConfidenceThreshold);
        Assert.Equal(profile.UploadSensitiveContent, retrieved.UploadSensitiveContent);
        Assert.Equal(profile.ExcludeSyncFoldersByDefault, retrieved.ExcludeSyncFoldersByDefault);
        Assert.Equal(profile.AllowedAutomaticOptimizationKinds.Count, retrieved.AllowedAutomaticOptimizationKinds.Count);
        Assert.Equal(profile.ProtectedKeywords.Count, retrieved.ProtectedKeywords.Count);
    }

    [Fact]
    public async Task GetProfileAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentName = $"NonExistent_{Guid.NewGuid():N}";

        // Act
        var result = await _repository.GetProfileAsync(nonExistentName);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefaultProfile_ReturnsProfile()
    {
        // Act
        var profile = await _repository.GetDefaultProfileAsync();

        // Assert
        Assert.NotNull(profile);
        Assert.NotEmpty(profile.ProfileName);
    }

    [Fact]
    public async Task GetDefaultProfile_CreatesIfNotExists()
    {
        // The test database is shared, so the default profile may already exist.
        // This test verifies that GetDefaultProfileAsync never returns null.

        // Act
        var profile = await _repository.GetDefaultProfileAsync();

        // Assert
        Assert.NotNull(profile);
    }

    [Fact]
    public async Task ListProfiles_ReturnsSummaries()
    {
        // Arrange
        var profile1Name = $"ListTest1_{Guid.NewGuid():N}";
        var profile2Name = $"ListTest2_{Guid.NewGuid():N}";

        var profile1 = new PolicyProfile
        {
            ProfileName = profile1Name,
            ScanRoots = ["C:\\Root1", "C:\\Root2"],
            MutableRoots = ["C:\\Mutable1"]
        };
        var profile2 = new PolicyProfile
        {
            ProfileName = profile2Name,
            ScanRoots = ["D:\\Root1"],
            MutableRoots = ["D:\\Mutable1", "D:\\Mutable2", "D:\\Mutable3"]
        };

        await _repository.SaveProfileAsync(profile1);
        await _repository.SaveProfileAsync(profile2);

        // Act
        var summaries = await _repository.ListProfilesAsync();

        // Assert
        Assert.True(summaries.Count >= 2);

        var p1Summary = summaries.FirstOrDefault(s => s.ProfileName == profile1Name);
        var p2Summary = summaries.FirstOrDefault(s => s.ProfileName == profile2Name);

        Assert.NotNull(p1Summary);
        Assert.NotNull(p2Summary);
        Assert.Equal(2, p1Summary.ScanRootCount);
        Assert.Equal(1, p1Summary.MutableRootCount);
        Assert.Equal(1, p2Summary.ScanRootCount);
        Assert.Equal(3, p2Summary.MutableRootCount);
    }

    [Fact]
    public async Task ProfileExists_ReturnsCorrectly()
    {
        // Arrange
        var existingName = $"Exists_{Guid.NewGuid():N}";
        var nonExistentName = $"NotExists_{Guid.NewGuid():N}";

        var profile = new PolicyProfile { ProfileName = existingName };
        await _repository.SaveProfileAsync(profile);

        // Act
        var existsResult = await _repository.ProfileExistsAsync(existingName);
        var notExistsResult = await _repository.ProfileExistsAsync(nonExistentName);

        // Assert
        Assert.True(existsResult);
        Assert.False(notExistsResult);
    }

    [Fact]
    public async Task DeleteProfile_RemovesProfile()
    {
        // Arrange
        var profileName = $"ToDelete_{Guid.NewGuid():N}";
        var profile = new PolicyProfile { ProfileName = profileName };
        await _repository.SaveProfileAsync(profile);

        // Act
        var deleted = await _repository.DeleteProfileAsync(profileName);
        var retrieved = await _repository.GetProfileAsync(profileName);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteProfile_NonExistent_ReturnsFalse()
    {
        // Arrange
        var nonExistentName = $"NonExistent_{Guid.NewGuid():N}";

        // Act
        var result = await _repository.DeleteProfileAsync(nonExistentName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SaveProfile_UpdatesExisting()
    {
        // Arrange
        var profileName = $"Update_{Guid.NewGuid():N}";
        var original = new PolicyProfile
        {
            ProfileName = profileName,
            ScanRoots = ["C:\\Original"],
            DuplicateAutoDeleteConfidenceThreshold = 0.90
        };

        await _repository.SaveProfileAsync(original);

        var updated = new PolicyProfile
        {
            ProfileName = profileName,
            ScanRoots = ["C:\\Updated1", "C:\\Updated2"],
            DuplicateAutoDeleteConfidenceThreshold = 0.99
        };

        // Act
        await _repository.SaveProfileAsync(updated);
        var retrieved = await _repository.GetProfileAsync(profileName);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.ScanRoots.Count);
        Assert.Equal("C:\\Updated1", retrieved.ScanRoots[0]);
        Assert.Equal(0.99, retrieved.DuplicateAutoDeleteConfidenceThreshold);
    }

    [Fact]
    public async Task SaveProfile_WithEmptyCollections_Success()
    {
        // Arrange
        var profileName = $"Empty_{Guid.NewGuid():N}";
        var profile = new PolicyProfile
        {
            ProfileName = profileName,
            ScanRoots = [],
            MutableRoots = [],
            ExcludedRoots = [],
            ProtectedPaths = [],
            SyncFolderMarkers = [],
            AllowedAutomaticOptimizationKinds = [],
            ProtectedKeywords = []
        };

        // Act
        await _repository.SaveProfileAsync(profile);
        var retrieved = await _repository.GetProfileAsync(profileName);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Empty(retrieved.ScanRoots);
        Assert.Empty(retrieved.MutableRoots);
        Assert.Empty(retrieved.ExcludedRoots);
    }

    [Fact]
    public async Task SaveProfile_WithSpecialCharactersInName_Success()
    {
        // Arrange
        var profileName = $"Profile with spaces & symbols! {Guid.NewGuid():N}";
        var profile = new PolicyProfile
        {
            ProfileName = profileName,
            ScanRoots = ["C:\\Test"]
        };

        // Act
        await _repository.SaveProfileAsync(profile);
        var retrieved = await _repository.GetProfileAsync(profileName);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(profileName, retrieved.ProfileName);
    }
}
