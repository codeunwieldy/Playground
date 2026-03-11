# C-004 Installer and Recovery Research

**Researcher:** Claude Code (CL-04 Workstream)
**Date:** 2026-03-11
**Status:** Research Complete

---

## Executive Summary

This document analyzes the current installer scaffold, service deployment requirements, VSS checkpoint eligibility, and recovery failure modes for Atlas File Intelligence. The current installer is minimal - it copies executables but does not handle service registration, runtime dependencies, permissions, or upgrade scenarios.

**Risk Level:** HIGH - The service cannot function as designed without proper installation, registration, and runtime configuration.

---

## 1. MSI/WiX Packaging Requirements

### 1.1 Current State Analysis

**File:** `installer/Bundle.wxs`
- WiX v4 syntax (using `http://wixtoolset.org/schemas/v4/wxs` namespace)
- Standard bootstrapper with hyperlink license theme
- Single MSI package in chain
- UpgradeCode: `7e00df84-aeba-4d8d-a9ba-fc10633aad8a`

**File:** `installer/Product.wxs`
- Per-machine installation (Scope="perMachine")
- Installs to `ProgramFiles6432Folder\Atlas File Intelligence`
- Only 2 components defined:
  - `AtlasAppExecutable` - WinUI app exe only
  - `AtlasServiceExecutable` - Service exe only

### 1.2 Critical Gaps

| Gap | Impact | Priority |
|-----|--------|----------|
| No service registration | Service won't run without `sc.exe create` or ServiceInstall element | Critical |
| No dependency files included | Missing DLLs, runtime assets, appsettings.json | Critical |
| No runtime prerequisite checks | .NET 8 runtime may not be present | High |
| No Windows App SDK check | WinUI app requires WindowsAppSDK | High |
| No upgrade/downgrade handling | MajorUpgrade element missing | High |
| No data directory creation | `%LOCALAPPDATA%\Atlas` not created | Medium |
| No quarantine folder setup | Quarantine storage location not established | Medium |
| No start menu shortcuts | No shortcuts for app launch | Medium |
| No firewall rules | Named pipe may need local firewall exception | Low |

### 1.3 Required WiX Elements

#### Service Registration
```xml
<!-- Add to AtlasServiceExecutable component -->
<ServiceInstall
    Id="AtlasServiceInstall"
    Name="AtlasFileIntelligence"
    DisplayName="Atlas File Intelligence"
    Description="Background service for AI-guided file organization and safe PC optimization"
    Type="ownProcess"
    Start="auto"
    Account="LocalSystem"
    ErrorControl="normal"
    Arguments="--service" />

<ServiceControl
    Id="AtlasServiceControl"
    Name="AtlasFileIntelligence"
    Start="install"
    Stop="both"
    Remove="uninstall"
    Wait="yes" />
```

#### Major Upgrade Handling
```xml
<MajorUpgrade
    Schedule="afterInstallInitialize"
    DowngradeErrorMessage="A newer version of Atlas is already installed."
    AllowSameVersionUpgrades="yes" />
```

#### Runtime Prerequisites (in Bundle.wxs)
```xml
<Chain>
    <!-- .NET 8 Runtime Check -->
    <ExePackage
        Id="DotNet8Runtime"
        DisplayName=".NET 8 Runtime"
        SourceFile="path\to\dotnet-runtime-8.0.x-win-x64.exe"
        DetectCondition="DOTNET8_INSTALLED"
        InstallCondition="NOT DOTNET8_INSTALLED"
        Permanent="yes" />

    <!-- Windows App SDK Check -->
    <MsixPackage
        Id="WindowsAppSDK"
        SourceFile="path\to\Microsoft.WindowsAppRuntime.x.x.msix"
        DetectCondition="WINDOWSAPPSDK_INSTALLED" />

    <MsiPackage SourceFile="bin\Atlas.Installer.msi" />
</Chain>
```

### 1.4 File Harvest Requirements

The MSI must include all runtime files. Current approach only copies `.exe` files.

**Atlas.App Required Files:**
- `Atlas.App.exe`
- `Atlas.App.dll`
- `Atlas.Core.dll`
- `Atlas.AI.dll`
- `Atlas.Storage.dll`
- `Microsoft.WindowsAppSDK.*.dll` (multiple)
- `WinRT.Runtime.dll`
- `Microsoft.Windows.SDK.NET.dll`
- `*.pri` resources
- `Assets/` folder
- `appsettings.json`

**Atlas.Service Required Files:**
- `Atlas.Service.exe`
- `Atlas.Service.dll`
- `Atlas.Core.dll`
- `Atlas.AI.dll`
- `Atlas.Storage.dll`
- `Microsoft.Data.Sqlite.dll`
- `SQLitePCLRaw.*.dll` (multiple)
- `e_sqlite3.dll` (native)
- `protobuf-net.dll`
- `appsettings.json`
- `appsettings.Development.json` (optional)

**Recommendation:** Use WiX HeatWave or `heat.exe` to harvest published output directories.

---

## 2. Service Registration and Startup

### 2.1 Current Service Architecture

**File:** `src/Atlas.Service/Program.cs`
- Uses `Microsoft.Extensions.Hosting` Worker Service template
- Registers `AtlasStartupWorker` (IHostedService) for initialization
- Registers `AtlasPipeServerWorker` (BackgroundService) for pipe server
- No Windows Service-specific configuration

### 2.2 Windows Service Mode Requirements

The current `Host.CreateApplicationBuilder(args)` does not enable Windows Service mode.

**Required Change:**
```csharp
var builder = Host.CreateApplicationBuilder(args);

// Add Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AtlasFileIntelligence";
});
```

**Package Required:**
```xml
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.1" />
```

### 2.3 Service Account Considerations

| Account | Pros | Cons | Recommendation |
|---------|------|------|----------------|
| LocalSystem | Full access | Over-privileged | Not recommended |
| NetworkService | Reduced privileges | No user profile access | Possible |
| LocalService | Minimal privileges | No network, limited file access | Not viable |
| Virtual Account | Per-service identity | More complex setup | Recommended |

**Recommendation:** Use Virtual Account (`NT SERVICE\AtlasFileIntelligence`) for principle of least privilege while maintaining necessary file system access.

### 2.4 Service Startup Dependencies

The service should start after:
- Windows Event Log (for logging)
- Workstation (for network mapped drives, if accessed)

```xml
<ServiceDependency Id="EventLog" />
```

### 2.5 Recovery Actions

Configure service recovery for automatic restart:

```xml
<ServiceConfig
    ServiceName="AtlasFileIntelligence"
    OnInstall="yes"
    OnUninstall="yes"
    DelayedAutoStart="yes">
    <Failure Action="restart" Delay="60" />
    <Failure Action="restart" Delay="120" />
    <Failure Action="restart" Delay="300" />
</ServiceConfig>
```

---

## 3. VSS Checkpoint Eligibility

### 3.1 VSS Overview

Volume Shadow Copy Service (VSS) creates point-in-time snapshots of NTFS volumes. Atlas uses VSS for heavyweight rollback of destructive batches.

### 3.2 Eligibility Rules

An execution batch qualifies for a pre-execution VSS checkpoint when:

| Rule | Condition | Rationale |
|------|-----------|-----------|
| **VSS-E01** | `batch.Operations.Count >= 50` | Bulk operations need recovery |
| **VSS-E02** | Any operation has `sensitivity >= High` | High-value data protection |
| **VSS-E03** | `batch.Operations.Any(o => o.Kind == DeleteToQuarantine && !o.MarksSafeDuplicate)` | Non-duplicate deletes |
| **VSS-E04** | `batch.TouchedVolumes.Count > 1` | Cross-volume complexity |
| **VSS-E05** | User-configured `alwaysCheckpoint: true` | User preference |
| **VSS-E06** | Batch contains `ApplyOptimizationFix` operations | System changes |

### 3.3 VSS Capability Detection

Before attempting VSS operations, Atlas must verify:

```csharp
public sealed class VssCapabilityChecker
{
    public VssCapability Check(string volumePath)
    {
        // 1. Is volume NTFS?
        var driveInfo = new DriveInfo(Path.GetPathRoot(volumePath));
        if (driveInfo.DriveFormat != "NTFS")
            return VssCapability.NotSupported("Volume is not NTFS");

        // 2. Is VSS service running?
        using var sc = new ServiceController("VSS");
        if (sc.Status != ServiceControllerStatus.Running)
            return VssCapability.NotAvailable("VSS service not running");

        // 3. Sufficient disk space? (VSS needs ~10% or 320MB minimum)
        var freeSpacePercent = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize;
        if (freeSpacePercent < 0.10 && driveInfo.AvailableFreeSpace < 335544320)
            return VssCapability.LowSpace("Insufficient disk space for snapshot");

        // 4. Is user/service account allowed to create snapshots?
        // Requires backup/restore privileges or administrator

        return VssCapability.Available();
    }
}
```

### 3.4 VSS API Integration Options

| Approach | Complexity | Reliability | Recommendation |
|----------|------------|-------------|----------------|
| `wmic shadowcopy` CLI | Low | Medium | Development only |
| `vssadmin` CLI | Low | Medium | Development only |
| AlphaVSS NuGet package | Medium | High | **Recommended** |
| Direct COM Interop | High | High | Only if AlphaVSS insufficient |

**AlphaVSS Example:**
```csharp
using Alphaleonis.Win32.Vss;

public async Task<string> CreateSnapshotAsync(string volumePath, CancellationToken ct)
{
    using var vss = VssBackupComponents.CreateInstance();
    vss.InitializeForBackup(null);
    vss.SetBackupState(false, true, VssBackupType.Full, false);

    using var async = vss.GatherWriterMetadata();
    await Task.Factory.FromAsync(async.BeginWait, async.EndWait, null);

    var snapshotSetId = vss.StartSnapshotSet();
    var snapshotId = vss.AddToSnapshotSet(volumePath);

    vss.SetContext(VssSnapshotContext.Backup);
    vss.PrepareForBackup();

    using var asyncBackup = vss.DoSnapshotSet();
    await Task.Factory.FromAsync(asyncBackup.BeginWait, asyncBackup.EndWait, null);

    var props = vss.GetSnapshotProperties(snapshotId);
    return props.SnapshotDeviceObject; // Returns snapshot path
}
```

### 3.5 Snapshot Storage Guardrails

| Guardrail | Limit | Action When Exceeded |
|-----------|-------|---------------------|
| Max snapshots per volume | 64 (VSS limit) | Delete oldest Atlas snapshot |
| Max age | 7 days | Automatic cleanup |
| Max storage per volume | 10% or 10GB | Delete oldest |
| Pre-batch check | 1 snapshot minimum available | Warn user, allow proceed |

---

## 4. Recovery Failure Modes

### 4.1 Failure Mode Analysis

| ID | Failure Mode | Cause | Impact | Mitigation |
|----|--------------|-------|--------|------------|
| **RF-01** | VSS snapshot creation fails | Disk full, VSS disabled, permissions | No pre-execution checkpoint | Warn user, allow proceed without checkpoint |
| **RF-02** | VSS restore fails | Snapshot deleted, corruption | Cannot rollback via VSS | Fall back to inverse operations |
| **RF-03** | Inverse operation fails | File locked, moved, permissions | Partial rollback | Log failures, continue with remaining ops |
| **RF-04** | Quarantine restore fails | Quarantine folder deleted | Data loss | Keep quarantine metadata for audit |
| **RF-05** | Service crash during execution | Unhandled exception, OOM | Partial execution state | Resume from last checkpoint on restart |
| **RF-06** | App-Service IPC failure | Pipe disconnect, timeout | Lost progress visibility | Reconnect, query state from service |
| **RF-07** | Database corruption | Power loss, disk error | Lost history/checkpoints | SQLite WAL + periodic backups |
| **RF-08** | Cross-volume operation failure | Network disconnect, drive removal | Partially completed batch | Atomic batching per volume |

### 4.2 Recovery Strategy by Failure Mode

#### RF-01/RF-02: VSS Failures
```
If VSS unavailable:
  1. Log warning to prompt_traces
  2. Show user notification: "Recovery checkpoint unavailable for this operation"
  3. Require explicit confirmation for batch with RequiresCheckpoint=true
  4. Proceed with inverse-operation-only recovery path
```

#### RF-03: Inverse Operation Failures
```
For each failed inverse operation:
  1. Log to undo_checkpoint.Notes with error details
  2. Mark operation as "partial_failure" in batch status
  3. Continue with remaining inverse operations
  4. Present failure report to user with manual remediation steps
```

#### RF-05: Service Crash During Execution
```
On service startup:
  1. Query execution_batches for status="running"
  2. For each incomplete batch:
     a. Determine last successfully completed operation
     b. If <=50% complete: mark failed, generate rollback plan
     c. If >50% complete: offer resume or rollback
  3. Log recovery action to audit trail
```

### 4.3 Recovery Observability

Required metrics/logging for recovery debugging:

| Metric | Purpose |
|--------|---------|
| `atlas.recovery.vss_create_success` | VSS snapshot creation rate |
| `atlas.recovery.vss_create_failure` | VSS failures with reason |
| `atlas.recovery.inverse_op_success` | Successful rollback operations |
| `atlas.recovery.inverse_op_failure` | Failed rollback operations |
| `atlas.recovery.batch_resume_count` | Resumed incomplete batches |
| `atlas.recovery.quarantine_restore_success` | Successful quarantine restores |

---

## 5. Deployment Checklist

### 5.1 Pre-Release Checklist

#### Build Configuration
- [ ] Set build configuration to Release
- [ ] Enable self-contained deployment for app
- [ ] Target x64 only (per PROJECT.md constraints)
- [ ] Set version numbers consistently across all projects
- [ ] Strip debug symbols or generate PDBs separately

#### Installer Verification
- [ ] MSI installs to correct Program Files location
- [ ] Service registers and starts automatically
- [ ] App shortcut appears in Start Menu
- [ ] Uninstall removes all files and service
- [ ] Upgrade preserves user data in %LOCALAPPDATA%\Atlas
- [ ] Downgrade blocked with clear error message

#### Runtime Prerequisites
- [ ] .NET 8 runtime detection works
- [ ] Windows App SDK detection works
- [ ] Prerequisites install silently when missing

#### Service Operation
- [ ] Service starts with Windows
- [ ] Service recovers from crashes (3 restart attempts)
- [ ] Service accepts named pipe connections
- [ ] Service initializes database on first run
- [ ] Service logs to Windows Event Log

#### Security
- [ ] Service runs as virtual account (not LocalSystem)
- [ ] App communicates only via named pipe (no network)
- [ ] Database location protected by user ACLs
- [ ] Quarantine folder protected by elevated ACLs

### 5.2 Smoke Test Sequence

```
1. Clean install on clean Windows 11 VM
2. Verify service running: Get-Service AtlasFileIntelligence
3. Launch app, verify connection to service
4. Run scan on small folder (~100 files)
5. Generate plan and verify policy validation
6. Execute plan (dry run first, then real)
7. Verify undo capability
8. Uninstall and verify clean removal
9. Reinstall and verify data preserved (if applicable)
```

---

## 6. Recommendations

### Immediate Actions (Required for Phase 1)

1. **Add WindowsService support to Atlas.Service**
   - File: `src/Atlas.Service/Program.cs`
   - Add `Microsoft.Extensions.Hosting.WindowsServices` package
   - Configure service name

2. **Add ServiceInstall to WiX**
   - File: `installer/Product.wxs`
   - Add ServiceInstall, ServiceControl elements
   - Configure virtual account

3. **Harvest all deployment files**
   - Use `dotnet publish` output
   - Include all DLLs, assets, configs

4. **Add MajorUpgrade element**
   - Prevent downgrade
   - Handle upgrade cleanly

### Phase 2 Actions (Before VSS Work)

1. **Add AlphaVSS NuGet package**
2. **Implement VssCapabilityChecker**
3. **Implement VSS eligibility rules**
4. **Add VSS error handling and fallback**

### Phase 3 Actions (Before Release)

1. **Add runtime prerequisite checks to Bundle**
2. **Code signing for executables and installer**
3. **Add telemetry/crash reporting hooks**
4. **Create repair/recovery tool**

---

## Appendix A: File References

| File | Lines | Purpose |
|------|-------|---------|
| `installer/Bundle.wxs` | 1-14 | EXE bootstrapper definition |
| `installer/Product.wxs` | 1-28 | MSI package definition |
| `src/Atlas.Service/Program.cs` | 1-42 | Service host configuration |
| `src/Atlas.Service/Services/AtlasStartupWorker.cs` | 1-23 | Service initialization |
| `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` | 1-127 | Named pipe server |
| `src/Atlas.Service/Atlas.Service.csproj` | 1-17 | Service project config |

---

## Appendix B: Uncertainties

| Uncertainty | Impact | Resolution Path |
|-------------|--------|-----------------|
| Virtual account file system permissions | May need explicit ACL grants | Test during development |
| AlphaVSS .NET 8 compatibility | May need source compilation | Verify with test project |
| Bundle.wxs prerequisite chain order | May cause install failures | Test on clean VM |
| WinUI app elevated launch | Service communication from elevated app | Test both scenarios |

---

*End of Installer and Recovery Research*
