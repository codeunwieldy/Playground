using System.Security.Cryptography;
using Atlas.Core.Contracts;
using Atlas.Core.Policies;
using Atlas.Core.Scanning;

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
            return ClassifyFile(profile, info);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Inspects a single file with full explainability, distinguishing failure reasons.
    /// </summary>
    public DetailedInspectionResult InspectFileDetailed(PolicyProfile profile, string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new DetailedInspectionResult { Outcome = "Missing" };

            if (pathSafetyClassifier.IsProtectedPath(profile, filePath))
                return new DetailedInspectionResult { Outcome = "Protected" };

            if (pathSafetyClassifier.IsExcludedPath(profile, filePath))
                return new DetailedInspectionResult { Outcome = "Excluded" };

            var info = new FileInfo(filePath);
            var sniff = ContentSniffer.Sniff(info.FullName);
            var category = sniff?.Category ?? ClassifyCategory(info.Extension);
            var mimeType = sniff?.MimeType ?? info.Extension.Trim('.').ToLowerInvariant();
            var sensitivity = SensitivityScorer.Classify(
                profile, info.FullName, info.Name, info.Extension, category, mimeType);

            var item = new FileInventoryItem
            {
                Path = info.FullName,
                Name = info.Name,
                Extension = info.Extension,
                Category = category,
                MimeType = mimeType,
                ContentFingerprint = sniff?.HeaderFingerprint ?? string.Empty,
                SizeBytes = info.Length,
                LastModifiedUnixTimeSeconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                Sensitivity = sensitivity.Level,
                IsSyncManaged = pathSafetyClassifier.IsSyncManaged(profile, info.FullName),
                IsProtectedByUser = false,
                IsDuplicateCandidate = info.Length > 0
            };

            return new DetailedInspectionResult
            {
                Outcome = "Inspected",
                Item = item,
                Sensitivity = sensitivity,
                ContentSniffSucceeded = sniff is not null,
                HasContentFingerprint = !string.IsNullOrEmpty(sniff?.HeaderFingerprint)
            };
        }
        catch
        {
            return new DetailedInspectionResult { Outcome = "AccessDenied" };
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
                var item = ClassifyFile(profile, info);
                response.Inventory.Add(item);
            }
            catch
            {
                // Skip transient or access-limited files.
            }
        }
    }

    private FileInventoryItem ClassifyFile(PolicyProfile profile, FileInfo info)
    {
        var sniff = ContentSniffer.Sniff(info.FullName);
        var category = sniff?.Category ?? ClassifyCategory(info.Extension);
        var mimeType = sniff?.MimeType ?? info.Extension.Trim('.').ToLowerInvariant();
        var sensitivity = SensitivityScorer.Classify(
            profile, info.FullName, info.Name, info.Extension, category, mimeType);

        return new FileInventoryItem
        {
            Path = info.FullName,
            Name = info.Name,
            Extension = info.Extension,
            Category = category,
            MimeType = mimeType,
            ContentFingerprint = sniff?.HeaderFingerprint ?? string.Empty,
            SizeBytes = info.Length,
            LastModifiedUnixTimeSeconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            Sensitivity = sensitivity.Level,
            IsSyncManaged = pathSafetyClassifier.IsSyncManaged(profile, info.FullName),
            IsProtectedByUser = false,
            IsDuplicateCandidate = info.Length > 0
        };
    }

    private static string ClassifyCategory(string extension) =>
        CategoryByExtension.TryGetValue(extension, out var category) ? category : "Other";

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
                    var duplicateCandidates = fullHashGroup.ToList();
                    var canonical = DuplicateCanonicalSelector.SelectCanonical(duplicateCandidates);
                    var analysis = DuplicateGroupAnalyzer.Analyze(duplicateCandidates, isFullHashVerified: true, canonical);

                    yield return new DuplicateGroup
                    {
                        CanonicalPath = canonical.Path,
                        Confidence = analysis.CleanupConfidence,
                        MatchConfidence = analysis.MatchConfidence,
                        CanonicalReason = analysis.CanonicalReason,
                        HasSensitiveMembers = analysis.HasSensitiveMembers,
                        HasSyncManagedMembers = analysis.HasSyncManagedMembers,
                        HasProtectedMembers = analysis.HasProtectedMembers,
                        MaxSensitivity = analysis.MaxSensitivity,
                        Paths = duplicateCandidates.Select(static item => item.Path).ToList(),
                        Evidence = analysis.Evidence.Select(static e => new DuplicateEvidenceEntry { Signal = e.Signal, Detail = e.Detail }).ToList()
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

/// <summary>
/// Detailed inspection result with explainability and failure distinction.
/// </summary>
public sealed class DetailedInspectionResult
{
    public required string Outcome { get; init; }
    public FileInventoryItem? Item { get; init; }
    public SensitivityResult? Sensitivity { get; init; }
    public bool ContentSniffSucceeded { get; init; }
    public bool HasContentFingerprint { get; init; }
}
