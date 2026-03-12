# C-013 Scan Diff and Drift Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

`C-011` gave Atlas read-only inventory session APIs.

`C-012` gave Atlas the backend seam for incremental observation and bounded rescans.

Codex is already surfacing scan continuity in the WinUI shell by comparing stored session summaries. The next backend step is to expose proper "what changed" data through the service so the UI can move from coarse trend signals to real drift evidence without touching SQLite directly.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Keep all new APIs read-only
- Do not weaken the service-only boundary
- Do not add direct app-side database or filesystem access
- Keep result sizes bounded and DTOs app-friendly

## Read First

1. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
2. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
3. `src/Atlas.Core/Contracts/PipeContracts.cs`
4. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
5. `src/Atlas.Core/Scanning/*`
6. `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
7. `.planning/claude/C-012-DELTA-SCANNING-AND-RESCAN-ORCHESTRATION.md`
8. `src/Atlas.App/Services/AtlasShellSession.cs`
9. `src/Atlas.App/Views/HistoryPage.cs`
10. `.planning/claude/CLAUDE-INBOX.md`

## Goal

Expose persisted scan drift through safe service APIs so Codex can query:

- latest-vs-previous scan drift summary
- explicit session-vs-session drift summary
- bounded changed file rows for review

without adding any app-side storage knowledge.

## Required Deliverables

### 1. Repository diff support

Add the minimum repository read support needed to compare scan sessions.

Good scope:

- compare two session IDs
- derive added / removed / changed counts
- optionally return bounded changed file rows

Keep this narrow. Do not redesign the persistence model.

### 2. Pipe contracts for scan drift

Add additive protobuf contracts in `PipeContracts.cs`.

Good candidates:

- `inventory/drift-snapshot`
- `inventory/session-diff`
- optional paged `inventory/session-diff-files`

DTOs should be compact, deterministic, and app-ready.

### 3. Service handlers

Add read-only handlers in `AtlasPipeServerWorker`.

Desired behavior:

- graceful empty-state handling when fewer than two sessions exist
- explicit missing-session response
- bounded file-level diff rows
- no mutation side effects

### 4. Diff semantics

Be explicit about what counts as:

- added
- removed
- changed
- unchanged

At minimum, changed should account for path-stable rows whose meaningful file metadata changed.

If you need to choose a first-pass definition, document it clearly in the outbox.

### 5. Tests

Add focused storage/service tests.

Good targets:

- latest-vs-previous snapshot works when two sessions exist
- fewer than two sessions returns a clean empty/no-baseline response
- explicit session diff works with missing session IDs
- added / removed / changed counts are correct
- changed file rows are bounded and ordered deterministically

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- new routes/contracts added
- how diff semantics are defined
- what drift the app can now query
- tests added and whether they pass
- anything intentionally deferred

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Repository diffing

- own minimal session-comparison repository additions
- own diff semantics

### Subagent B: Contracts and service handlers

- own protobuf request/response types
- own read-only pipe routes

### Subagent C: Tests and verification

- own diff correctness tests
- own empty-state and bounds tests
- own final build/test pass

## Success Criteria

This packet is successful when:

- Atlas can query persisted drift through the service only
- latest-vs-previous and explicit session diff flows are available
- bounded changed-file rows can be returned for drill-in
- contracts are additive and read-only
- tests exist and pass
- no UI files were touched

## Notes From Codex

- Codex already has continuity signals in the dashboard and `Atlas Memory`.
- The immediate UI ask after this packet is not a new workspace. It is richer drift evidence inside the existing shell.
- If a compact drift snapshot plus one explicit session-diff route covers the need, prefer that over too many narrow endpoints.
- Keep DTOs shaped for review: counts, timestamps, session IDs, and bounded changed-row summaries matter more than raw storage detail.
