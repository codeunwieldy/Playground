using Atlas.Core.Contracts;

namespace Atlas.Core.Policies;

public static class PolicyProfileFactory
{
    private static readonly OptimizationKind[] SafeOptimizationKinds =
    [
        OptimizationKind.TemporaryFiles,
        OptimizationKind.CacheCleanup,
        OptimizationKind.DuplicateArchives,
        OptimizationKind.UserStartupEntry
    ];

    public static PolicyProfile CreateDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var scanRoots = DriveInfo.GetDrives()
            .Where(static drive => drive.IsReady)
            .Select(static drive => drive.RootDirectory.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mutableRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(userProfile, "Downloads")
        }
        .Where(static path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.SystemDirectory
        }
        .Where(static path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        return new PolicyProfile
        {
            ProfileName = "Windows 11 Consumer Default",
            ScanRoots = scanRoots,
            MutableRoots = mutableRoots,
            ProtectedPaths = protectedPaths,
            ExcludedRoots = new List<string>
            {
                Path.Combine(userProfile, ".nuget"),
                Path.Combine(userProfile, "AppData"),
                Path.Combine(userProfile, "OneDrive")
            },
            SyncFolderMarkers = new List<string>
            {
                "OneDrive",
                "Dropbox",
                "Google Drive",
                "iCloudDrive",
                "SynologyDrive",
                ".dropbox",
                ".sync"
            },
            DuplicateAutoDeleteConfidenceThreshold = 0.985d,
            UploadSensitiveContent = false,
            ExcludeSyncFoldersByDefault = true,
            AllowedAutomaticOptimizationKinds = SafeOptimizationKinds.ToList(),
            ProtectedKeywords = new List<string>
            {
                "passport",
                "tax",
                "medical",
                "contract",
                "payroll",
                "bank",
                "identity",
                "recovery",
                "password"
            }
        };
    }
}