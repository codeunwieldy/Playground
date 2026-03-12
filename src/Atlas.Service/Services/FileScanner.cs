using System.Security.Cryptography;
using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.Service.Services;

public sealed class FileScanner(PathSafetyClassifier pathSafetyClassifier)
{
    private static readonly Dictionary<string, string> CategoryByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = "Documents",
        [".docx"] = "Documents",
        [".pdf"] = "Documents",
        [".txt"] = "Documents",
        [".md"] = "Documents",
        [".xls"] = "Spreadsheets",
        [".xlsx"] = "Spreadsheets",
        [".csv"] = "Spreadsheets",
        [".ppt"] = "Presentations",
        [".pptx"] = "Presentations",
        [".jpg"] = "Images",
        [".jpeg"] = "Images",
        [".png"] = "Images",
        [".gif"] = "Images",
        [".webp"] = "Images",
        [".mp4"] = "Video",
        [".mov"] = "Video",
        [".mkv"] = "Video",
        [".mp3"] = "Audio",
        [".wav"] = "Audio",
        [".zip"] = "Archives",
        [".7z"] = "Archives",
        [".rar"] = "Archives",
        [".exe"] = "Applications",
        [".msi"] = "Installers"
    };

    /// <summary>
    /// Returns a fresh snapshot of all ready drives.
    /// </summary>
    public static List<VolumeSnapshot> SnapshotVolumes()
    {
        var volumes = new List<VolumeSnapshot>();
        foreach (var drive in DriveInfo.GetDrives().Where(static d => d.IsReady))
        {
            volumes.Add(new VolumeSnapshot
            {
                RootPath = drive.RootDirectory.FullName,
                DriveFormat = drive.DriveFormat,
                DriveType = drive.DriveType.ToString(),
                IsReady = drive.IsReady,
                TotalSizeBytes = drive.TotalSize,
                FreeSpaceBytes = drive.AvailableFreeSpace
            });
        }
        return volumes;
    }

    /// <summary>
    /// Inspects a single file and returns its inventory item.
    /// Returns null if the file does not exist, is inaccessible, or is excluded/protected.
    /// </summary>
    public FileInventoryItem? InspectFile(PolicyProfile profile, string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            if (pathSafetyClassifier.IsProtectedPath(profile, filePath)) return null;
            if (pathSafetyClassifier.IsExcludedPath(profile, filePath)) return null;

            var info = new FileInfo(filePath);
            return new FileInventoryItem
            {
                Path = info.FullName,
                Name = info.Name,
                Extension = info.Extension,
                Category = ClassifyCategory(info.Extension),
                MimeType = info.Extension.Trim('.').ToLowerInvariant(),
                SizeBytes = info.Length,
                LastModifiedUnixTimeSeconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                Sensitivity = ClassifySensitivity(profile, info.FullName),
                IsSyncManaged = pathSafetyClassifier.IsSyncManaged(profile, info.FullName),
                IsProtectedByUser = false,
                IsDuplicateCandidate = info.Length > 0
            };
        }
        catch
        {
            return null;
        }
    }

    public Task<ScanResponse> ScanAsync(PolicyProfile profile, ScanRequest request, CancellationToken cancellationToken)
    {
        var response = new ScanResponse();
        var roots = request.Roots.Count > 0 ? request.Roots : profile.ScanRoots;
        var maxFiles = request.MaxFiles > 0 ? request.MaxFiles : 25000;

        response.Volumes.AddRange(SnapshotVolumes());

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested || response.Inventory.Count >= maxFiles)
            {
                break;
            }

            EnumerateRoot(profile, root, maxFiles, response, cancellationToken);
        }

        response.FilesScanned = response.Inventory.Count;
        response.Duplicates.AddRange(FindDuplicates(response.Inventory));
        return Task.FromResult(response);
    }

    private void EnumerateRoot(PolicyProfile profile, string root, int maxFiles, ScanResponse response, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || pathSafetyClassifier.IsExcludedPath(profile, root))
        {
            return;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Device | FileAttributes.Offline,
            ReturnSpecialDirectories = false
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            if (cancellationToken.IsCancellationRequested || response.Inventory.Count >= maxFiles)
            {
                break;
            }

            if (pathSafetyClassifier.IsProtectedPath(profile, file) || pathSafetyClassifier.IsExcludedPath(profile, file))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(file);
                var item = new FileInventoryItem
                {
                    Path = info.FullName,
                    Name = info.Name,
                    Extension = info.Extension,
                    Category = ClassifyCategory(info.Extension),
                    MimeType = info.Extension.Trim('.').ToLowerInvariant(),
                    SizeBytes = info.Length,
                    LastModifiedUnixTimeSeconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    Sensitivity = ClassifySensitivity(profile, info.FullName),
                    IsSyncManaged = pathSafetyClassifier.IsSyncManaged(profile, info.FullName),
                    IsProtectedByUser = false,
                    IsDuplicateCandidate = info.Length > 0
                };

                response.Inventory.Add(item);
            }
            catch
            {
                // Skip transient or access-limited files.
            }
        }
    }

    private static string ClassifyCategory(string extension) =>
        CategoryByExtension.TryGetValue(extension, out var category) ? category : "Other";

    private static SensitivityLevel ClassifySensitivity(PolicyProfile profile, string path)
    {
        if (profile.ProtectedKeywords.Any(keyword => path.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return SensitivityLevel.High;
        }

        if (path.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
        {
            return SensitivityLevel.Critical;
        }

        if (path.Contains("finance", StringComparison.OrdinalIgnoreCase) || path.Contains("legal", StringComparison.OrdinalIgnoreCase))
        {
            return SensitivityLevel.High;
        }

        return SensitivityLevel.Low;
    }

    private static IEnumerable<DuplicateGroup> FindDuplicates(IEnumerable<FileInventoryItem> inventory)
    {
        var bySize = inventory.Where(static item => item.SizeBytes > 0)
            .GroupBy(static item => item.SizeBytes)
            .Where(static group => group.Count() > 1);

        foreach (var sizeGroup in bySize)
        {
            var quickHashGroups = sizeGroup
                .GroupBy(item => ComputeQuickHash(item.Path))
                .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);

            foreach (var quickHashGroup in quickHashGroups)
            {
                var fullHashGroups = quickHashGroup
                    .GroupBy(item => ComputeFullHash(item.Path))
                    .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);

                foreach (var fullHashGroup in fullHashGroups)
                {
                    var canonical = fullHashGroup
                        .OrderByDescending(static item => item.Sensitivity)
                        .ThenBy(static item => item.Path.Length)
                        .First();

                    yield return new DuplicateGroup
                    {
                        CanonicalPath = canonical.Path,
                        Confidence = 0.995d,
                        Paths = fullHashGroup.Select(static item => item.Path).ToList()
                    };
                }
            }
        }
    }

    private static string ComputeQuickHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(64 * 1024, (int)stream.Length)];
            _ = stream.Read(buffer, 0, buffer.Length);
            var hash = SHA256.HashData(buffer);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ComputeFullHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }
}