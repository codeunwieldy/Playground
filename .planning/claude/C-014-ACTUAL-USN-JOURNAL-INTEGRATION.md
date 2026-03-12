# C-014 Actual USN Journal Integration Packet

## Owner

Claude Code in VS Code

## Priority

Queued after `C-013`

## Why This Is Next

`C-012` built the delta-source abstraction and a USN-capable seam, but the current `UsnJournalDeltaSource` only probes NTFS support and then falls back to full rescan behavior.

That was the right safety-first move for the first packet. Once `C-013` lands drift query APIs, the next big backend quality upgrade is to make the USN source real so Atlas can feed future drift and incremental-scan experiences from journal-backed change signals instead of only watcher/fallback behavior.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Keep the fallback chain intact: watcher and scheduled rescans must still work
- Keep the service-only boundary intact
- Prefer narrow additive changes over broad rewrites

## Read First

1. `src/Atlas.Core/Scanning/IDeltaSource.cs`
2. `src/Atlas.Core/Scanning/DeltaCapability.cs`
3. `src/Atlas.Core/Scanning/DeltaResult.cs`
4. `src/Atlas.Service/Services/DeltaSources/UsnJournalDeltaSource.cs`
5. `src/Atlas.Service/Services/DeltaSources/DeltaCapabilityDetector.cs`
6. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
7. `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
8. `.planning/claude/C-012-DELTA-SCANNING-AND-RESCAN-ORCHESTRATION.md`
9. `.planning/claude/C-013-SCAN-DIFF-AND-DRIFT-QUERY-APIS.md`

## Goal

Upgrade the current probe-only USN seam into a real journal-backed delta source for supported NTFS volumes, while preserving safe fallback when the environment does not support that path.

## Required Deliverables

### 1. Real USN reading path

Implement actual USN journal reading for supported NTFS volumes.

Requirements:

- bounded change collection
- clear handling for first-run baseline behavior
- deterministic `DeltaResult`
- no silent unsafe failure

### 2. Capability and fallback continuity

Keep the existing source-priority model intact:

- USN journal when truly supported
- watcher fallback when USN is not available
- scheduled rescan fallback when neither higher-fidelity source is usable

### 3. Safe error handling

Explicitly handle:

- missing or inaccessible journal
- unsupported volume types
- journal reset/invalid continuation state
- oversized change sets that should degrade to full rescan

### 4. State continuity

If you need checkpoint/continuation metadata for USN reading, keep it narrow and document it clearly.

Do not casually redesign the repository layer. Add the smallest persistence surface needed.

### 5. Tests

Add focused tests for:

- supported NTFS path produces journal-backed delta results
- unsupported path falls back cleanly
- overflow / invalid continuation forces bounded full-rescan behavior
- no regression of fallback ordering

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- what is now truly USN-backed
- what state is persisted, if any
- what still remains deferred
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- `UsnJournalDeltaSource` does real work on supported NTFS roots
- fallback behavior is preserved
- failure modes degrade safely
- tests exist and pass
- no UI files were touched

## Notes From Codex

- This packet is intentionally queued behind `C-013`.
- The UI does not need anything from this immediately, but future drift and incremental review UX will benefit from the improved signal quality.
- Keep the abstraction stable so the app does not care whether the backend got its drift from USN, watcher, or rescan.
