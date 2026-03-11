# C-001 Phase 1 Safety Audit Packet
## Atlas File Intelligence - Policy Engine Safety Review

**Auditor:** Subagent A - Safety and Policy Analyst
**Date:** 2026-03-11
**Scope:** `Atlas.Core.Policies` and `Atlas.Core.Tests`

---

## Executive Summary

This audit identifies safety edge cases and gaps in the Atlas policy engine's path blocking, mutable root enforcement, sync folder detection, and cross-volume/rename handling. The current test suite covers only **3 policy engine tests**, leaving significant attack surface untested.

**Risk Level:** MEDIUM-HIGH - The policy engine is the last line of defense before destructive file operations.

---

## 1. Blocked-Path Edge Cases

### 1.1 Path Normalization Exception Swallowing

**File:** `src/Atlas.Core/Policies/PathSafetyClassifier.cs:14-23`

```csharp
try
{
    return Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
catch
{
    return path.Trim();  // DANGEROUS: falls back to raw input
}
```

**Issue:** When `Path.GetFullPath()` throws (e.g., invalid characters, too-long path), the method falls back to `path.Trim()` without any sanitization. This could allow malformed paths to bypass protection checks.

**Attack Vector:** A path like `C:\Windows\System32\<invalid>` could fail normalization but the raw string might still match protected paths inconsistently.

**Recommendation:** Fail closed - return empty string or throw when normalization fails for paths that will be used in mutable operations.

### 1.2 No UNC Path Handling

**File:** `src/Atlas.Core/Policies/PathSafetyClassifier.cs:54-68`

```csharp
public static bool IsSameOrChildPath(string candidate, string parent)
{
    // ...
    return candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
```

**Issue:** UNC paths (`\\server\share\folder`) are not explicitly handled. The path separator logic may behave unexpectedly with UNC paths, and network locations are not in the default protected paths.

**Attack Vectors:**
- `\\localhost\C$\Windows` maps to `C:\Windows` but won't be detected as protected
- Network shares could be used to bypass mutable root restrictions

**Uncertainty:** Need to verify `Path.GetFullPath()` behavior with UNC paths that map to local drives.

### 1.3 Symbolic Links and Junction Points

**File:** `src/Atlas.Core/Policies/AtlasPolicyEngine.cs:12-13`

```csharp
var source = _pathSafetyClassifier.Normalize(operation.SourcePath);
var destination = _pathSafetyClassifier.Normalize(operation.DestinationPath);
```

**Issue:** `Path.GetFullPath()` does NOT resolve symbolic links or junction points. A symlink at `C:\Users\jscel\Documents\MyLink` pointing to `C:\Windows\System32` would be treated as a safe mutable path.

**Attack Vector:** User creates junction point from Documents to System32, operations on the junction bypass protected path checks.

**Recommendation:** Add `FileSystemInfo.ResolveLinkTarget()` or equivalent resolution before path safety checks.

### 1.4 Path Traversal Not Blocked

**File:** `src/Atlas.Core/Policies/PathSafetyClassifier.cs:16`

```csharp
return Path.GetFullPath(path)
```

**Issue:** While `Path.GetFullPath()` canonicalizes `..` sequences, there's no explicit validation that the input path doesn't contain traversal attempts. The audit trail may log the original malicious path while the operation targets a different location.

**Example:** Input `C:\Users\jscel\Documents\..\..\..\Windows\notepad.exe` is normalized but the original path in operation records is misleading.

### 1.5 Missing Protected Path Categories

**File:** `src/Atlas.Core/Policies/PolicyProfileFactory.cs:37-47`

```csharp
var protectedPaths = new[]
{
    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    Environment.SystemDirectory
}
```

**Missing Protected Locations:**
- `C:\$Recycle.Bin` - System recycle bin
- `C:\System Volume Information` - System restore data
- `C:\Recovery` - Windows recovery environment
- `C:\Boot` - Boot configuration
- `%LOCALAPPDATA%\Microsoft` - Critical user app data
- `%APPDATA%\Microsoft\Windows` - Start menu, taskbar configs
- EFI System Partition (if mounted)

---

## 2. Mutable-Root Gaps

### 2.1 Hardcoded Downloads Path

**File:** `src/Atlas.Core/Policies/PolicyProfileFactory.cs:31`

```csharp
Path.Combine(userProfile, "Downloads")
```

**Issue:** Windows allows users to relocate the Downloads folder. The shell folder location should be queried via `SHGetKnownFolderPath` or the registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders`).

**Impact:** Users with relocated Downloads folder could have operations blocked unexpectedly, or worse, the actual Downloads location may be outside mutable roots.

### 2.2 No Mutable Root Existence Validation

**File:** `src/Atlas.Core/Policies/PolicyProfileFactory.cs:24-35`

```csharp
var mutableRoots = new[]
{
    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    // ...
}
.Where(static path => !string.IsNullOrWhiteSpace(path))
```

**Issue:** Only checks for non-empty strings, not whether directories actually exist. Non-existent mutable roots could lead to:
- Operations failing at execution time after passing policy checks
- Confusion in policy validation results

### 2.3 OneDrive Folder Redirection Not Handled

**File:** `src/Atlas.Core/Policies/PolicyProfileFactory.cs:26-30`

```csharp
Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
```

**Issue:** When OneDrive is configured with "Known Folder Move", these special folders point to OneDrive locations. This creates a conflict:
1. The paths are in MutableRoots (allowed)
2. The paths contain "OneDrive" marker (sync-managed, blocked by default)

**Current Behavior:** Operations would be blocked due to sync folder exclusion overriding mutable root status.

**Uncertainty:** Is this the intended behavior? Need clarification on policy precedence.

### 2.4 Profile Tampering Vector

**File:** `src/Atlas.Core/Contracts/DomainModels.cs:47-60`

```csharp
public sealed class PolicyProfile
{
    [ProtoMember(3)] public List<string> MutableRoots { get; set; } = new();
    [ProtoMember(5)] public List<string> ProtectedPaths { get; set; } = new();
```

**Issue:** PolicyProfile is a mutable data class serialized via ProtoBuf. If profiles are persisted and loaded without validation, a corrupted or maliciously crafted profile could:
- Add `C:\Windows` to MutableRoots
- Remove system paths from ProtectedPaths
- Set `DuplicateAutoDeleteConfidenceThreshold = 0.0`

**Recommendation:** Add profile validation on load, ensure protected paths cannot be removed, cap mutable roots to known safe locations.

---

## 3. Sync-Folder Review Cases

### 3.1 Fragile String Matching for Sync Detection

**File:** `src/Atlas.Core/Policies/PathSafetyClassifier.cs:48-52`

```csharp
public bool IsSyncManaged(PolicyProfile profile, string path)
{
    var normalized = Normalize(path);
    return profile.SyncFolderMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
```

**Issue:** `Contains()` matching is overly broad:
- `C:\Users\jscel\Documents\MyOneDriveProject\file.txt` matches "OneDrive"
- `C:\Dropbox Exports\archive.zip` matches "Dropbox"
- `C:\Users\jscel\Desktop\sync-notes.txt` matches ".sync"

**False Positives:** Any path containing sync keywords anywhere gets flagged.

**Recommendation:** Match sync markers as path segments only:
```csharp
// Should match: C:\Users\jscel\OneDrive\file.txt
// Should NOT match: C:\OneDriveBackups\file.txt
```

### 3.2 Missing iCloud Path Variations

**File:** `src/Atlas.Core/Policies/PolicyProfileFactory.cs:66`

```csharp
"iCloudDrive",
```

**Issue:** iCloud uses different folder names depending on configuration:
- `iCloud Drive` (with space)
- `iCloudDrive` (no space, current marker)
- `com~apple~CloudDocs` (internal name on some systems)

**Impact:** iCloud-synced files may not be detected on all configurations.

### 3.3 Generic ".sync" Marker

**File:** `src/Atlas.Core/Policies/PolicyProfileFactory.cs:69`

```csharp
".sync"
```

**Issue:** Too generic - many applications create `.sync` files or folders unrelated to cloud sync:
- Version control systems
- Development tools
- Database sync files

**Recommendation:** Remove or make more specific (e.g., `Synology.sync` or remove entirely if SynologyDrive marker is sufficient).

### 3.4 Nested Sync Folder Behavior

**Issue:** No tests or documentation for nested sync scenarios:
- OneDrive containing a Dropbox subfolder
- Shared folders within sync roots
- Selective sync folders (files-on-demand vs. always available)

**Uncertainty:** How should policy behave when operation source is in OneDrive and destination is in Dropbox?

---

## 4. Cross-Volume and Rename Edge Cases

### 4.1 UNC Path Root Comparison

**File:** `src/Atlas.Core/Policies/AtlasPolicyEngine.cs:105-113`

```csharp
private static bool IsCrossVolumeMove(string source, string destination)
{
    // ...
    return !string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
}
```

**Issue:** `Path.GetPathRoot()` returns `\\server\share` for UNC paths, not the full path. Two different shares on the same server would have different roots and trigger cross-volume approval, but two shares with same name on different servers would appear as same volume.

**Example:**
- `\\server1\share` vs `\\server2\share` - same root `\\server1\share` after normalization? (needs testing)

### 4.2 Subst/Mount Point Blindness

**Issue:** Drives created via `SUBST` or mount points that map volumes to folders are not detected. A move from `X:\file.txt` to `C:\Users\jscel\Documents\file.txt` would be flagged as cross-volume even if X: is a subst of Documents.

**Impact:** False positive approval requirements for same-volume operations.

### 4.3 Network Drive Disconnection

**Issue:** No handling for network drives that become unavailable during operation planning vs. execution. Path.GetPathRoot succeeds on disconnected mapped drives, but operations would fail.

**Recommendation:** Add availability check for network paths before execution.

### 4.4 Root Folder Rename Logic

**File:** `src/Atlas.Core/Policies/AtlasPolicyEngine.cs:115-128`

```csharp
private static bool IsRootFolderRename(string source, string destination)
{
    var sourceParent = Path.GetDirectoryName(source);
    var destinationParent = Path.GetDirectoryName(destination);
    return string.IsNullOrWhiteSpace(sourceParent)
        || string.IsNullOrWhiteSpace(destinationParent)
        || string.Equals(sourceParent, Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase)
        || string.Equals(destinationParent, Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
}
```

**Issue:** This flags ANY rename where either source or destination is at drive root, not just renames of folders that ARE at the root.

**Example:** Renaming `C:\Users` to `C:\OldUsers` should require approval (correct), but so would renaming `C:\X\file.txt` to `C:\file.txt` even though we're just moving down (possibly over-cautious).

---

## 5. Test Expansion Recommendations

### 5.1 Current Test Coverage (CRITICAL GAPS)

**File:** `tests/Atlas.Core.Tests/PolicyEngineTests.cs`

**Current tests:** Only 3 tests exist:
1. `BlocksProtectedSystemPaths` - basic Windows path blocking
2. `RequiresApprovalForSyncManagedOperations` - OneDrive detection
3. `AllowsSafeDuplicateQuarantineInsideMutableRoots` - happy path duplicate delete

### 5.2 Required Test Additions

#### Blocked-Path Tests
```
[ ] Test_BlocksUNCPathsToSystemShares (\\localhost\C$\Windows)
[ ] Test_BlocksJunctionPointsToProtectedPaths
[ ] Test_BlocksSymlinksToProtectedPaths
[ ] Test_NormalizationFailure_FailsClosed
[ ] Test_PathTraversal_Blocked (../../Windows)
[ ] Test_PathsWithInvalidCharacters_Handled
[ ] Test_LongPaths_Over260Characters
[ ] Test_AlternateDataStreams_Blocked (file.txt:hidden)
[ ] Test_DevicePaths_Blocked (\\.\PhysicalDrive0)
```

#### Mutable-Root Tests
```
[ ] Test_RelocatedDownloadsFolder_Detected
[ ] Test_NonExistentMutableRoot_Handled
[ ] Test_OneDriveKnownFolderMove_PolicyPrecedence
[ ] Test_OperationOutsideAllMutableRoots_Blocked
[ ] Test_EmptyMutableRoots_AllOperationsBlocked
[ ] Test_ProfileWithSystemPathInMutableRoots_Rejected
```

#### Sync-Folder Tests
```
[ ] Test_OneDrivePath_ExactMatch_Detected
[ ] Test_OneDriveSubstring_InProjectName_NotDetected
[ ] Test_DropboxPath_Detected
[ ] Test_iCloudDrive_AllVariants_Detected
[ ] Test_GoogleDrive_Detected
[ ] Test_NestedSyncFolders_BothDetected
[ ] Test_SyncFolderWhenExcludeDisabled_RequiresApproval
[ ] Test_GenericSyncMarker_FalsePositives
```

#### Cross-Volume Tests
```
[ ] Test_CrossVolume_LocalToLocal_RequiresApproval
[ ] Test_CrossVolume_LocalToNetwork_RequiresApproval
[ ] Test_CrossVolume_NetworkToNetwork_SameServer
[ ] Test_CrossVolume_SubstDrive_SameVolume
[ ] Test_CrossVolume_MountPoint_SameVolume
```

#### Rename Tests
```
[ ] Test_RootFolderRename_RequiresApproval
[ ] Test_NestedFolderRename_NoApprovalRequired
[ ] Test_RenameToRootLevel_RequiresApproval
[ ] Test_RenameFromRootLevel_RequiresApproval
```

#### Policy Profile Tests
```
[ ] Test_ProfileValidation_RejectsEmptyProtectedPaths
[ ] Test_ProfileValidation_RejectsZeroConfidenceThreshold
[ ] Test_ProfileDeserialization_MalformedData_FailsSafe
```

#### Rollback Planner Tests
```
[ ] Test_Rollback_PartiallyCompletedBatch
[ ] Test_Rollback_CrossVolumeOperation
[ ] Test_Rollback_DeletedSourceFolder
[ ] Test_Rollback_PermissionDenied
```

### 5.3 Test File Organization Recommendation

```
tests/Atlas.Core.Tests/
  Policies/
    PathSafetyClassifierTests.cs      (NEW - path normalization, blocking)
    PolicyEngineBlockingTests.cs      (expanded from current)
    PolicyEngineMutableRootTests.cs   (NEW)
    PolicyEngineSyncFolderTests.cs    (NEW)
    PolicyEngineCrossVolumeTests.cs   (NEW)
    PolicyProfileValidationTests.cs  (NEW)
  Planning/
    RollbackPlannerTests.cs          (expand existing)
    RollbackPlannerEdgeCaseTests.cs  (NEW)
```

---

## Appendix A: Risk Matrix

| Issue | Likelihood | Impact | Risk Score |
|-------|------------|--------|------------|
| Symlink bypass of protected paths | Medium | Critical | HIGH |
| UNC path to local system bypass | Low | Critical | MEDIUM |
| Sync folder false positives | High | Low | MEDIUM |
| Profile tampering | Low | Critical | MEDIUM |
| Mutable root gaps | Medium | Medium | MEDIUM |
| Path normalization fallback | Low | High | MEDIUM |
| Network drive disconnection | Medium | Low | LOW |

---

## Appendix B: Files Reviewed

- `src/Atlas.Core/Policies/AtlasPolicyEngine.cs` (162 lines)
- `src/Atlas.Core/Policies/PathSafetyClassifier.cs` (69 lines)
- `src/Atlas.Core/Policies/PolicyProfileFactory.cs` (89 lines)
- `src/Atlas.Core/Planning/RollbackPlanner.cs` (109 lines)
- `src/Atlas.Core/Contracts/DomainModels.cs` (194 lines)
- `src/Atlas.Core/Contracts/PipeContracts.cs` (108 lines)
- `tests/Atlas.Core.Tests/PolicyEngineTests.cs` (65 lines)
- `tests/Atlas.Core.Tests/RollbackPlannerTests.cs` (40 lines)

---

*End of Safety Audit Report*
