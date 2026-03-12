# C-015 Incremental Session Composition Packet

## Owner

Claude Code in VS Code

## Priority

Queued after `C-014`

## Why This Is Next

`C-014` upgrades the USN journal path so Atlas can detect real changed paths on supported NTFS volumes.

That is necessary, but not sufficient. Atlas still persists scan sessions as full snapshots. If the service only knows "these paths changed" but still has to rebuild every session through a full root walk, we do not get the main product win from incremental scanning.

This packet is the first real step that turns delta detection into persisted inventory efficiency:

- compose a new persisted session from a prior baseline plus bounded changes
- remove deleted paths safely
- preserve full-session semantics for drift review and later planning
- keep full-rescan fallback when the delta window is too risky or incomplete

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Keep the service-only mutation boundary intact
- Preserve safe fallback to full rescan
- Prefer narrow additive schema/repository changes over broad rewrites

## Read First

1. `src/Atlas.Core/Scanning/IDeltaSource.cs`
2. `src/Atlas.Core/Scanning/DeltaResult.cs`
3. `src/Atlas.Service/Services/DeltaSources/UsnJournalDeltaSource.cs`
4. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
5. `src/Atlas.Service/Services/FileScanner.cs`
6. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
7. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
8. `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
9. `tests/Atlas.Storage.Tests/ScanDriftTests.cs`
10. `.planning/claude/C-012-DELTA-SCANNING-AND-RESCAN-ORCHESTRATION.md`
11. `.planning/claude/C-014-ACTUAL-USN-JOURNAL-INTEGRATION.md`

## Goal

Allow Atlas to persist a new complete inventory session from a prior baseline plus bounded delta changes, instead of forcing every orchestration cycle to behave like a full root walk.

## Required Deliverables

### 1. Incremental session composition model

Add the narrowest repository/service surface needed to build a new scan session from:

- a baseline stored session
- changed or newly discovered file rows
- removed paths
- refreshed root and volume posture where needed

The output still needs to behave like a complete session for later diffing, history, and planning.

### 2. Safe deletion handling

When a path is removed from disk, the new session must stop carrying it forward.

Requirements:

- removed paths must not silently linger in the composed session
- missing baseline rows must degrade safely
- partial composition must never masquerade as a trustworthy full session if Atlas cannot prove completeness

### 3. Bounded fallback rules

Add explicit rules that force a full rescan when incremental composition would be unsafe, such as:

- oversized changed-path sets
- invalid or missing baseline
- incomplete delta payload
- source overflow or journal reset
- roots/volumes whose shape changed too much to trust a bounded merge

### 4. Provenance metadata

Persist enough metadata on composed sessions to explain how Atlas got them.

Keep it narrow and app-ready. At minimum, Codex will benefit from knowing:

- manual full scan vs orchestration scan
- full rescan vs incremental composition
- delta source used (`UsnJournal`, `Watcher`, `ScheduledRescan`, or fallback)
- baseline session ID when composition used one

If schema changes are required, keep them minimal and documented.

### 5. Service integration

Update orchestration so it can choose between:

- full rescan
- incremental composition from delta results

Do not weaken existing bounds on cadence, cycle count, or fallback behavior.

### 6. Tests

Add focused tests for:

- compose baseline + added files
- compose baseline + changed files
- compose baseline + removed files
- mixed add/remove/change composition
- invalid baseline forces safe full rescan
- overflow or excessive delta count forces safe full rescan
- provenance metadata persists correctly

### 7. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- whether composed sessions are now full-session trustworthy
- what provenance metadata is persisted
- what still remains deferred
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can build a new persisted session from a baseline plus bounded deltas
- removed paths are handled correctly
- unsafe cases degrade to full rescan
- provenance is persisted clearly enough for future UI use
- tests exist and pass
- no UI files were touched

## Suggested Claude Subagent Split

- Subagent A: repository/schema/provenance changes
- Subagent B: orchestration and incremental-session composition logic
- Subagent C: tests for composition correctness, fallback triggers, and provenance

## Notes From Codex

- This packet is the bridge between "Atlas can detect changes" and "Atlas can actually benefit from them."
- Codex will use the resulting provenance metadata to build a native scan-intelligence and rescan-story UX in the shell.
- Keep the persisted session contract stable for existing drift/history routes; additive evolution is preferred over replacing the session model.
