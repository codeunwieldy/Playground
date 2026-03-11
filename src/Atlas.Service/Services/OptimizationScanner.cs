using Atlas.Core.Contracts;
using Microsoft.Win32;

namespace Atlas.Service.Services;

public sealed class OptimizationScanner
{
    public Task<OptimizationResponse> ScanAsync(PolicyProfile profile, CancellationToken cancellationToken)
    {
        var findings = new List<OptimizationFinding>();
        findings.AddRange(InspectTempStorage(cancellationToken));
        findings.AddRange(InspectStartupEntries(profile, cancellationToken));
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

        if (tempBytes > 250L * 1024 * 1024)
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
}