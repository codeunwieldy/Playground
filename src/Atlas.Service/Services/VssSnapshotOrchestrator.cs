using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Atlas.Service.Services;

/// <summary>
/// Orchestrates real VSS (Volume Shadow Copy) snapshot creation for eligible execution batches.
/// Invokes vssadmin on Windows and fails closed on other platforms or when VSS is unavailable.
/// </summary>
public sealed class VssSnapshotOrchestrator
{
    private static readonly TimeSpan VssTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Attempts to create VSS snapshots for the given volumes.
    /// Returns a result indicating success, unavailability, or failure per volume.
    /// </summary>
    /// <param name="requirement">The checkpoint requirement (Required, Recommended, or NotNeeded).</param>
    /// <param name="volumes">Volume roots to snapshot (e.g., "C:\").</param>
    /// <param name="isDryRun">If true, no snapshots are created.</param>
    /// <returns>A result per the attempt.</returns>
    public VssSnapshotResult TryCreateSnapshots(
        CheckpointRequirement requirement,
        List<string> volumes,
        bool isDryRun)
    {
        if (requirement == CheckpointRequirement.NotNeeded)
        {
            return new VssSnapshotResult
            {
                Status = VssSnapshotStatus.NotNeeded,
                Message = "No checkpoint needed; VSS snapshot creation skipped."
            };
        }

        if (isDryRun)
        {
            return new VssSnapshotResult
            {
                Status = VssSnapshotStatus.Skipped,
                Message = "Dry-run mode; VSS snapshot creation skipped."
            };
        }

        if (!OperatingSystem.IsWindows())
        {
            return new VssSnapshotResult
            {
                Status = VssSnapshotStatus.Unavailable,
                Message = "VSS snapshots are only available on Windows."
            };
        }

        if (volumes.Count == 0)
        {
            return new VssSnapshotResult
            {
                Status = VssSnapshotStatus.Unavailable,
                Message = "No volumes specified for snapshot creation."
            };
        }

        return CreateSnapshotsWindows(requirement, volumes);
    }

    [SupportedOSPlatform("windows")]
    private static VssSnapshotResult CreateSnapshotsWindows(
        CheckpointRequirement requirement,
        List<string> volumes)
    {
        var references = new List<VssSnapshotReference>();
        var failures = new List<string>();

        foreach (var volume in volumes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var volumeLetter = ExtractVolumeLetter(volume);
            if (string.IsNullOrEmpty(volumeLetter))
            {
                failures.Add($"Cannot determine volume letter from '{volume}'.");
                continue;
            }

            try
            {
                var (exitCode, stdout, stderr) = RunVssAdmin($"create shadow /for={volumeLetter}:\\");

                if (exitCode == 0)
                {
                    var snapshotId = ParseSnapshotId(stdout);
                    references.Add(new VssSnapshotReference
                    {
                        SnapshotId = snapshotId ?? $"unknown-{Guid.NewGuid():N}",
                        Volume = volume,
                        CreatedUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    var errorDetail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                    failures.Add($"VSS creation failed for {volume} (exit code {exitCode}): {Truncate(errorDetail, 200)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"VSS creation exception for {volume}: {ex.Message}");
            }
        }

        // Determine overall status.
        if (references.Count == volumes.Count)
        {
            return new VssSnapshotResult
            {
                Status = VssSnapshotStatus.Success,
                Success = true,
                References = references,
                Message = $"VSS snapshots created for {references.Count} volume(s)."
            };
        }

        if (references.Count > 0)
        {
            return new VssSnapshotResult
            {
                Status = VssSnapshotStatus.PartialCoverage,
                Success = requirement != CheckpointRequirement.Required,
                References = references,
                Message = $"Partial VSS coverage: {references.Count}/{volumes.Count} volumes. Failures: {string.Join("; ", failures)}"
            };
        }

        // Complete failure.
        return new VssSnapshotResult
        {
            Status = VssSnapshotStatus.Failed,
            Success = false,
            Message = $"VSS snapshot creation failed for all volumes. {string.Join("; ", failures)}"
        };
    }

    [SupportedOSPlatform("windows")]
    private static (int exitCode, string stdout, string stderr) RunVssAdmin(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "vssadmin",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)VssTimeout.TotalMilliseconds))
        {
            try { process.Kill(); } catch { /* best effort */ }
            return (-1, "", "VSS operation timed out.");
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static string? ParseSnapshotId(string stdout)
    {
        // vssadmin output typically contains a line like:
        // Shadow Copy ID: {abcdef12-3456-7890-abcd-ef1234567890}
        var match = Regex.Match(stdout, @"Shadow Copy ID:\s*\{?([0-9a-fA-F-]+)\}?", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractVolumeLetter(string volume)
    {
        if (string.IsNullOrWhiteSpace(volume)) return null;
        var trimmed = volume.TrimEnd('\\', '/');
        if (trimmed.Length >= 1 && char.IsLetter(trimmed[0]))
        {
            return trimmed[0].ToString().ToUpperInvariant();
        }
        return null;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }
}

/// <summary>
/// Status of a VSS snapshot creation attempt.
/// </summary>
public enum VssSnapshotStatus
{
    /// <summary>Checkpoint was not needed.</summary>
    NotNeeded = 0,

    /// <summary>All requested snapshots were created successfully.</summary>
    Success = 1,

    /// <summary>VSS is not available on this platform or for the requested volumes.</summary>
    Unavailable = 2,

    /// <summary>Snapshot creation failed for all volumes.</summary>
    Failed = 3,

    /// <summary>Some but not all volumes were successfully snapshotted.</summary>
    PartialCoverage = 4,

    /// <summary>Snapshot creation was skipped (dry-run).</summary>
    Skipped = 5
}

/// <summary>
/// Result of attempting VSS snapshot creation for an execution batch.
/// </summary>
public sealed class VssSnapshotResult
{
    public VssSnapshotStatus Status { get; init; }
    public bool Success { get; init; }
    public List<VssSnapshotReference> References { get; init; } = new();
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// A reference to a created VSS snapshot for one volume.
/// </summary>
public sealed class VssSnapshotReference
{
    public string SnapshotId { get; init; } = string.Empty;
    public string Volume { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
}
