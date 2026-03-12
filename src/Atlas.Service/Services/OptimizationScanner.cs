using Atlas.Core.Contracts;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Atlas.Service.Services;

public sealed class OptimizationScanner
{
    private const long TempFindingThresholdBytes = 250L * 1024 * 1024;
    private const long CacheFindingThresholdBytes = 300L * 1024 * 1024;

    private static readonly string[] DuplicateArchiveExtensions =
    [
        ".zip",
        ".7z",
        ".rar",
        ".iso",
        ".msi",
        ".exe",
        ".cab"
    ];

    public Task<OptimizationResponse> ScanAsync(PolicyProfile profile, CancellationToken cancellationToken)
    {
        var findings = new List<OptimizationFinding>();
        findings.AddRange(InspectTempStorage(cancellationToken));
        findings.AddRange(InspectCacheStorage(cancellationToken));
        findings.AddRange(InspectDuplicateArchives(cancellationToken));

        if (OperatingSystem.IsWindows())
        {
            findings.AddRange(InspectStartupEntries(profile, cancellationToken));
        }

        findings.AddRange(InspectLowDiskPressure(cancellationToken));

        return Task.FromResult(new OptimizationResponse { Findings = findings });
    }

    private static IEnumerable<OptimizationFinding> InspectTempStorage(CancellationToken cancellationToken)
    {
        var tempPath = Path.GetTempPath();
        if (!Directory.Exists(tempPath))
        {
            yield break;
        }

        long tempBytes = 0;
        foreach (var file in Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                tempBytes += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore transient files.
            }
        }

        if (tempBytes > TempFindingThresholdBytes)
        {
            yield return new OptimizationFinding
            {
                Kind = OptimizationKind.TemporaryFiles,
                Target = tempPath,
                CanAutoFix = true,
                RequiresApproval = false,
                Evidence = $"Temporary storage uses {tempBytes / (1024 * 1024)} MB.",
                RollbackPlan = "No rollback needed for transient temp files."
            };
        }
    }

    internal static IEnumerable<OptimizationFinding> InspectCacheRoots(
        IEnumerable<string> cacheRoots,
        long thresholdBytes,
        CancellationToken cancellationToken)
    {
        foreach (var root in cacheRoots
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var totalBytes = MeasureDirectoryBytes(root, cancellationToken);
            if (totalBytes <= thresholdBytes)
            {
                continue;
            }

            yield return new OptimizationFinding
            {
                Kind = OptimizationKind.CacheCleanup,
                Target = root,
                CanAutoFix = true,
                RequiresApproval = false,
                Evidence = $"Cache storage uses {totalBytes / (1024 * 1024)} MB.",
                RollbackPlan = "Cache will repopulate naturally; no manual rollback should be required."
            };
        }
    }

    internal static IEnumerable<OptimizationFinding> InspectDuplicateArchivesInDirectory(
        string? directoryPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            yield break;
        }

        var candidates = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Where(path => DuplicateArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    return info.Exists && info.Length > 0 ? info : null;
                }
                catch
                {
                    return null;
                }
            })
            .Where(static info => info is not null)
            .Cast<FileInfo>()
            .ToList();

        foreach (var sizeGroup in candidates
                     .GroupBy(static info => info.Length)
                     .Where(static group => group.Count() > 1))
        {
            var hashGroups = sizeGroup
                .GroupBy(info => ComputeFileHash(info.FullName))
                .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);

            foreach (var hashGroup in hashGroups)
            {
                var ordered = hashGroup
                    .OrderBy(static info => info.FullName.Length)
                    .ThenBy(static info => info.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var canonical = ordered[0];
                var duplicates = ordered.Skip(1).ToList();
                var reclaimableBytes = duplicates.Sum(static info => info.Length);

                yield return new OptimizationFinding
                {
                    Kind = OptimizationKind.DuplicateArchives,
                    Target = canonical.FullName,
                    CanAutoFix = true,
                    RequiresApproval = true,
                    Evidence = $"Duplicate archive set contains {ordered.Count} matching files; about {reclaimableBytes / (1024 * 1024)} MB could be reclaimed.",
                    RollbackPlan = "Move duplicate archives to quarantine before permanent deletion."
                };
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<OptimizationFinding> InspectStartupEntries(PolicyProfile profile, CancellationToken cancellationToken)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key is null)
        {
            yield break;
        }

        foreach (var valueName in key.GetValueNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            yield return new OptimizationFinding
            {
                Kind = OptimizationKind.UserStartupEntry,
                Target = valueName,
                CanAutoFix = true,
                RequiresApproval = true,
                Evidence = $"Startup entry: {command}",
                RollbackPlan = "Re-add the Run registry entry if disabled."
            };
        }
    }

    private static IEnumerable<OptimizationFinding> InspectCacheStorage(CancellationToken cancellationToken)
    {
        return InspectCacheRoots(GetDefaultCacheRoots(), CacheFindingThresholdBytes, cancellationToken);
    }

    private static IEnumerable<OptimizationFinding> InspectDuplicateArchives(CancellationToken cancellationToken)
    {
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return InspectDuplicateArchivesInDirectory(downloadsPath, cancellationToken);
    }

    private static IEnumerable<OptimizationFinding> InspectLowDiskPressure(CancellationToken cancellationToken)
    {
        foreach (var drive in DriveInfo.GetDrives().Where(static drive => drive.IsReady))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var usage = 1d - ((double)drive.AvailableFreeSpace / drive.TotalSize);
            if (usage >= 0.9d)
            {
                yield return new OptimizationFinding
                {
                    Kind = OptimizationKind.LowDiskPressure,
                    Target = drive.RootDirectory.FullName,
                    CanAutoFix = false,
                    RequiresApproval = true,
                    Evidence = $"Drive usage is {(int)(usage * 100)}%.",
                    RollbackPlan = "Recommendation only."
                };
            }
        }
    }

    private static IEnumerable<string> GetDefaultCacheRoots()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            Path.Combine(localAppData, "Microsoft", "Windows", "INetCache"),
            Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
            Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")
        };
    }

    private static long MeasureDirectoryBytes(string root, CancellationToken cancellationToken)
    {
        long totalBytes = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                totalBytes += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore transient files.
            }
        }

        return totalBytes;
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return string.Empty;
        }
    }
}
