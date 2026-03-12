# C-010 Inventory Persistence Foundations Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

`C-008` completed the first read-side memory bridge between the service and the app. The next structural gap is lower in the stack: Atlas still scans into an in-memory `ScanResponse`, but it does not persist scan sessions, volumes, roots, or file snapshots in a durable inventory model.

Phase 2 starts by fixing that foundation before we add NTFS USN journal work, watchers, or more advanced refresh orchestration.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Do not weaken the service-only mutation boundary
- Do not implement direct app-side database access
- Keep schema and contracts additive and version-friendly
- Do not jump into USN journal or watcher orchestration in this packet unless a tiny abstraction is needed to keep persistence extensible

## Read First

1. `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
2. `src/Atlas.Service/Services/FileScanner.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
6. `src/Atlas.Storage/Repositories/*.cs`
7. `.planning/ROADMAP.md`
8. `.planning/STATE.md`
9. `.planning/codebase/ARCHITECTURE.md`

## Goal

Persist scan results in a durable, queryable inventory foundation that captures:

- scan sessions
- scanned roots
- volume snapshots
- file snapshots

without changing app ownership or pretending Phase 2 delta scanning is already complete.

## Required Deliverables

### 1. Inventory schema foundation

Extend `AtlasDatabaseBootstrapper.cs` with additive tables for the inventory domain.

Good candidates:

- `scan_sessions`
- `scan_session_roots`
- `scan_volumes`
- `file_snapshots`

If you need a different naming scheme, keep it explicit and easy to query.

Minimum expectation:

- one durable scan session record
- one-to-many session roots
- one-to-many volume records
- one-to-many file snapshot records

### 2. Repository contracts and implementations

Add the smallest useful repository layer for inventory persistence.

Good targets:

- save full scan session
- get latest scan session
- list recent scan sessions
- get latest file snapshots for a session
- optional summary projections for volume and file counts

Keep this packet scoped to persistence and read-side retrieval, not full Phase 2 UX.

### 3. Service integration

Wire live scan persistence into the service path after successful scan completion.

Target:

- `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`

Behavior:

- persist only when the service actually produced a live scan
- log and degrade gracefully on persistence failure
- keep scanning behavior deterministic and non-destructive

### 4. Durable DTO or model shaping

If you need inventory-specific summary records or persistence models, add them in a way that is friendly to later query APIs.

Optimize for later work on:

- recent scan summaries
- session-to-volume drill-in
- session-to-file snapshot drill-in

### 5. Tests

Add focused tests for the inventory persistence layer.

Strong targets:

- schema bootstrap includes the new tables
- repository can save and reload a scan session
- file snapshots round-trip correctly
- recent sessions list in descending time order
- service-side scan persistence does not crash the request on repository failure

Use temporary SQLite databases and keep tests backend-only.

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- schema added
- repository contracts added
- what scan state is now persisted
- tests added and whether they pass
- what was intentionally deferred to the next packet

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Schema and repository design

- own table design
- own repository contracts
- own summary projections

### Subagent B: Repository implementation and service wiring

- own concrete repository code
- own service persistence integration after live scans

### Subagent C: Tests and verification

- own storage tests
- own integration tests for scan persistence
- own final build and test pass

## Success Criteria

This packet is successful when:

- Atlas persists live scan sessions durably
- volume and file snapshot data survive beyond process memory
- repository abstractions exist for later read-side inventory work
- tests exist and pass
- no UI files were touched

## Notes From Codex

- Codex is actively wiring the Atlas Memory workspace to the new history read APIs from `C-008`.
- The next likely Claude lane after this packet is delta scanning orchestration: USN journal, watcher fallback, scheduled bounded rescans.
- Please keep this packet foundational. I want a strong persisted inventory model first, then a separate packet for incremental scan mechanics.
