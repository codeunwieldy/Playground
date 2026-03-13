using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Atlas.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Services;

/// <summary>
/// Applies safe, deterministic optimization fixes and captures rollback state.
/// Handles TemporaryFiles, CacheCleanup, DuplicateArchives, and UserStartupEntry.
/// Blocks unsupported kinds (ScheduledTask, BackgroundApplication, LowDiskPressure, Unknown).
/// </summary>
public sealed class SafeOptimizationFixExecutor(IOptions<AtlasServiceOptions> serviceOptions)
{
    private static readonly HashSet<OptimizationKind> BlockedKinds =
    [
        OptimizationKind.ScheduledTask,
        OptimizationKind.BackgroundApplication,
        OptimizationKind.LowDiskPressure,
        OptimizationKind.Unknown
    ];

    /// <summary>
    /// The set of optimization kinds not supported for automatic application.
    /// </summary>
    public static HashSet<OptimizationKind> UnsafeKinds => BlockedKinds;

    /// <summary>
    /// Applies the optimization fix for the given operation, returning a result with rollback state.
    /// </summary>
    public OptimizationFixResult Apply(PlanOperation operation, string planId)
    {
        if (BlockedKinds.Contains(operation.OptimizationKind))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = $"Optimization kind '{operation.OptimizationKind}' is not supported for automatic application."
            };
        }

        return operation.OptimizationKind switch
        {
            OptimizationKind.TemporaryFiles => ApplyFileCleanup(operation, isCache: false),
            OptimizationKind.CacheCleanup => ApplyFileCleanup(operation, isCache: true),
            OptimizationKind.DuplicateArchives => ApplyDuplicateArchiveQuarantine(operation, planId),
            OptimizationKind.UserStartupEntry => ApplyUserStartupEntry(operation),
            _ => new OptimizationFixResult
            {
                Success = false,
                Message = $"Optimization kind '{operation.OptimizationKind}' is not supported for automatic application."
            }
        };
    }

    /// <summary>
    /// Reverts a previously applied optimization fix using stored rollback state.
    /// </summary>
    public OptimizationFixResult Revert(OptimizationRollbackState rollbackState)
    {
        if (!rollbackState.IsReversible)
        {
            return new OptimizationFixResult
            {
                Success = true,
                Message = rollbackState.Description
            };
        }

        return rollbackState.Kind switch
        {
            OptimizationKind.UserStartupEntry => RevertUserStartupEntry(rollbackState),
            OptimizationKind.DuplicateArchives => RevertDuplicateArchiveQuarantine(rollbackState),
            _ => new OptimizationFixResult
            {
                Success = false,
                Message = $"No revert handler for optimization kind '{rollbackState.Kind}'."
            }
        };
    }

    #region TemporaryFiles / CacheCleanup

    private static OptimizationFixResult ApplyFileCleanup(PlanOperation operation, bool isCache)
    {
        var kindLabel = isCache ? "cache" : "temporary";
        var kind = isCache ? OptimizationKind.CacheCleanup : OptimizationKind.TemporaryFiles;
        var targetDir = operation.SourcePath;

        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = $"Target {kindLabel} directory does not exist: {targetDir}"
            };
        }

        var files = Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories).ToList();

        if (files.Count == 0)
        {
            return new OptimizationFixResult
            {
                Success = true,
                Message = $"Target {kindLabel} directory is already clean: {targetDir}",
                RollbackState = new OptimizationRollbackState
                {
                    Kind = kind,
                    Target = targetDir,
                    IsReversible = false,
                    Description = $"No-op: {kindLabel} directory was already empty."
                }
            };
        }

        var deletedPaths = new List<string>();
        var failedPaths = new List<string>();

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                deletedPaths.Add(file);
            }
            catch
            {
                failedPaths.Add(file);
            }
        }

        // Clean up empty subdirectories after file deletion.
        CleanEmptySubdirectories(targetDir);

        var rollbackData = JsonSerializer.Serialize(new FileCleanupRollbackData
        {
            DeletedPaths = deletedPaths,
            FailedPaths = failedPaths
        });

        var allDeleted = failedPaths.Count == 0;
        var message = allDeleted
            ? $"Cleared {deletedPaths.Count} {kindLabel} files under {targetDir}."
            : $"Partially cleared {kindLabel} files under {targetDir}: {deletedPaths.Count} deleted, {failedPaths.Count} locked/failed.";

        return new OptimizationFixResult
        {
            Success = true,
            Message = message,
            RollbackState = new OptimizationRollbackState
            {
                Kind = kind,
                Target = targetDir,
                IsReversible = false,
                RollbackData = rollbackData,
                Description = $"Not reversible: {kindLabel} files will repopulate naturally."
            }
        };
    }

    private static void CleanEmptySubdirectories(string rootDir)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(static d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // Skip locked/in-use directories.
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }

    #endregion

    #region DuplicateArchives

    private OptimizationFixResult ApplyDuplicateArchiveQuarantine(PlanOperation operation, string planId)
    {
        var targetPath = operation.SourcePath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "DuplicateArchives fix requires a source path."
            };
        }

        if (!File.Exists(targetPath))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = $"Duplicate archive file does not exist: {targetPath}"
            };
        }

        var quarantineRoot = BuildQuarantineRoot(targetPath);
        Directory.CreateDirectory(quarantineRoot);
        TryHideDirectory(quarantineRoot);

        var targetName = $"{Guid.NewGuid():N}_{Path.GetFileName(targetPath)}";
        var quarantinePath = Path.Combine(quarantineRoot, targetName);

        // Compute content hash before move.
        var contentHash = ComputeFileHash(targetPath);

        var directory = Path.GetDirectoryName(quarantinePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(targetPath, quarantinePath, overwrite: false);

        var rollbackData = JsonSerializer.Serialize(new DuplicateArchiveRollbackData
        {
            OriginalPath = targetPath,
            QuarantinePath = quarantinePath,
            ContentHash = contentHash,
            PlanId = planId
        });

        return new OptimizationFixResult
        {
            Success = true,
            Message = $"Quarantined duplicate archive {targetPath} to {quarantinePath}.",
            RollbackState = new OptimizationRollbackState
            {
                Kind = OptimizationKind.DuplicateArchives,
                Target = targetPath,
                IsReversible = true,
                RollbackData = rollbackData,
                Description = $"Restore {targetPath} from quarantine at {quarantinePath}."
            }
        };
    }

    private static OptimizationFixResult RevertDuplicateArchiveQuarantine(OptimizationRollbackState rollbackState)
    {
        DuplicateArchiveRollbackData? data;
        try
        {
            data = JsonSerializer.Deserialize<DuplicateArchiveRollbackData>(rollbackState.RollbackData);
        }
        catch
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "Failed to deserialize duplicate archive rollback data."
            };
        }

        if (data is null || string.IsNullOrWhiteSpace(data.QuarantinePath) || string.IsNullOrWhiteSpace(data.OriginalPath))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "Rollback data is incomplete for duplicate archive revert."
            };
        }

        if (!File.Exists(data.QuarantinePath))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = $"Quarantined file no longer exists: {data.QuarantinePath}"
            };
        }

        var parentDir = Path.GetDirectoryName(data.OriginalPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        File.Move(data.QuarantinePath, data.OriginalPath, overwrite: false);

        return new OptimizationFixResult
        {
            Success = true,
            Message = $"Restored duplicate archive {data.OriginalPath} from quarantine."
        };
    }

    private string BuildQuarantineRoot(string sourcePath)
    {
        var driveRoot = Path.GetPathRoot(sourcePath) ?? sourcePath;
        return Path.Combine(driveRoot, serviceOptions.Value.QuarantineFolderName, DateTime.UtcNow.ToString("yyyyMMdd"));
    }

    private static void TryHideDirectory(string directoryPath)
    {
        try
        {
            File.SetAttributes(directoryPath, FileAttributes.Hidden);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hashBytes = SHA256.HashData(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion

    #region UserStartupEntry

    private static OptimizationFixResult ApplyUserStartupEntry(PlanOperation operation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "UserStartupEntry optimization is only supported on Windows."
            };
        }

        return ApplyUserStartupEntryWindows(operation);
    }

    [SupportedOSPlatform("windows")]
    private static OptimizationFixResult ApplyUserStartupEntryWindows(PlanOperation operation)
    {
        var valueName = operation.SourcePath;

        if (string.IsNullOrWhiteSpace(valueName))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "UserStartupEntry fix requires a registry value name in SourcePath."
            };
        }

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        if (key is null)
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "Cannot open HKCU Run registry key."
            };
        }

        var existingValue = key.GetValue(valueName)?.ToString();

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return new OptimizationFixResult
            {
                Success = true,
                Message = $"Startup entry '{valueName}' does not exist or is already removed.",
                RollbackState = new OptimizationRollbackState
                {
                    Kind = OptimizationKind.UserStartupEntry,
                    Target = valueName,
                    IsReversible = false,
                    Description = $"No-op: startup entry '{valueName}' was already absent."
                }
            };
        }

        // Save value, then delete.
        var rollbackData = JsonSerializer.Serialize(new StartupEntryRollbackData
        {
            ValueName = valueName,
            OriginalCommand = existingValue
        });

        key.DeleteValue(valueName, throwOnMissingValue: false);

        return new OptimizationFixResult
        {
            Success = true,
            Message = $"Disabled startup entry '{valueName}' (was: {existingValue}).",
            RollbackState = new OptimizationRollbackState
            {
                Kind = OptimizationKind.UserStartupEntry,
                Target = valueName,
                IsReversible = true,
                RollbackData = rollbackData,
                Description = $"Re-add startup entry '{valueName}' with command: {existingValue}"
            }
        };
    }

    private static OptimizationFixResult RevertUserStartupEntry(OptimizationRollbackState rollbackState)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "UserStartupEntry revert is only supported on Windows."
            };
        }

        return RevertUserStartupEntryWindows(rollbackState);
    }

    [SupportedOSPlatform("windows")]
    private static OptimizationFixResult RevertUserStartupEntryWindows(OptimizationRollbackState rollbackState)
    {
        StartupEntryRollbackData? data;
        try
        {
            data = JsonSerializer.Deserialize<StartupEntryRollbackData>(rollbackState.RollbackData);
        }
        catch
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "Failed to deserialize startup entry rollback data."
            };
        }

        if (data is null || string.IsNullOrWhiteSpace(data.ValueName) || string.IsNullOrWhiteSpace(data.OriginalCommand))
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "Rollback data is incomplete for startup entry revert."
            };
        }

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        if (key is null)
        {
            return new OptimizationFixResult
            {
                Success = false,
                Message = "Cannot open HKCU Run registry key for revert."
            };
        }

        key.SetValue(data.ValueName, data.OriginalCommand);

        return new OptimizationFixResult
        {
            Success = true,
            Message = $"Restored startup entry '{data.ValueName}' with command: {data.OriginalCommand}"
        };
    }

    #endregion

    #region Rollback Data Models

    internal sealed class FileCleanupRollbackData
    {
        public List<string> DeletedPaths { get; set; } = new();
        public List<string> FailedPaths { get; set; } = new();
    }

    internal sealed class DuplicateArchiveRollbackData
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinePath { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public string PlanId { get; set; } = string.Empty;
    }

    internal sealed class StartupEntryRollbackData
    {
        public string ValueName { get; set; } = string.Empty;
        public string OriginalCommand { get; set; } = string.Empty;
    }

    #endregion
}
