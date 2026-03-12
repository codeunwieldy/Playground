# C-011 Inventory Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

`C-010` completed the durable scan inventory foundation:

- scan sessions
- scanned roots
- volume snapshots
- file snapshots

But the app still cannot query that stored inventory through the Windows service. Codex is now preparing the dashboard and shell session to consume persisted scan memory, and the missing piece is the read-only pipe contract layer for inventory queries.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Do not weaken the service-only boundary
- These APIs are read-only; do not add app-side direct database access
- Keep contracts additive and version-friendly
- Do not implement USN journal, watcher orchestration, or delta scanning logic in this packet

## Read First

1. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
2. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
3. `src/Atlas.Core/Contracts/PipeContracts.cs`
4. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
5. `src/Atlas.App/Services/AtlasShellSession.cs`
6. `src/Atlas.App/Views/DashboardPage.xaml`
7. `.planning/claude/C-010-INVENTORY-PERSISTENCE-FOUNDATIONS.md`
8. `.planning/claude/CLAUDE-INBOX.md`

## Goal

Expose persisted inventory back to the app through safe read-only service APIs so Codex can query:

- latest scan session summary
- recent scan sessions
- session volume snapshots
- session file snapshots

without the app ever touching SQLite directly.

## Required Deliverables

### 1. Pipe contracts for read-side inventory

Add additive request/response contracts in `PipeContracts.cs` for inventory queries.

Keep them compact and app-friendly.

Good candidates:

- inventory snapshot request/response
- recent session list request/response
- session detail request/response
- session files list request/response

You do not need to expose every repository method if a smaller set gives the app a strong first inventory surface.

### 2. Service handlers

Add read-only inventory routes to `AtlasPipeServerWorker`.

Desired behavior:

- query repositories only
- no mutation side effects
- bounded result sizes
- graceful empty responses
- clear missing-session handling

Prefer a small number of high-value routes over many narrow ones if that keeps the API easier to evolve.

### 3. DTO shaping

Do not send giant payloads by default if the dashboard only needs summaries.

Provide summary/detail shaping that matches UI needs:

- scan session summaries for dashboards and memory workspaces
- volume summaries for a selected session
- paginated file snapshot rows for deeper drill-in later
- useful timestamps
- enough identifiers to support later detail navigation

If you add summary DTOs, keep them deterministic and protobuf-serializable.

### 4. Repository gaps

Fill any missing repository read methods only if required by the new service routes.

Keep this scoped:

- do not redesign repository interfaces casually
- do not rewrite working inventory persistence logic
- if a needed method is missing, add the smallest useful abstraction and document it

### 5. Tests

Add focused tests for the inventory query layer.

Good targets:

- route returns recent sessions in descending time order
- route returns latest session summary cleanly
- route returns volume summaries for a session
- route returns paginated file snapshot summaries
- empty database returns clean empty responses
- missing session detail returns a clear missing response

Use temporary SQLite databases and avoid live service hosting where possible.

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- new message types/contracts added
- what inventory can now be queried
- tests added and whether they pass
- any intentionally deferred detail endpoints

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Contracts and DTOs

- own new protobuf request/response types
- own summary/detail DTO shaping

### Subagent B: Service handlers and repository read path

- own `AtlasPipeServerWorker` routes
- own minimal repository gap fills

### Subagent C: Tests and verification

- own read-side integration tests
- own empty-state and ordering tests
- own final build/test pass

## Success Criteria

This packet is successful when:

- the app can query persisted scan history only through the service
- stored scan sessions, volumes, and file snapshots are available through bounded read APIs
- contracts are additive and read-only
- tests exist and pass
- no UI files were touched

## Notes From Codex

- Codex is actively upgrading the dashboard and shell session to consume persisted scan memory next.
- Strong first DTO fields would be `session_id`, `timestamp`, `files_scanned`, `duplicate_group_count`, `root_count`, `volume_count`, and for detail calls `root_path`, `drive_type`, `size_bytes`, `sensitivity`, and `is_sync_managed`.
- If one compact inventory snapshot route plus a few detail/list routes covers the need, prefer that over a large set of narrow endpoints.
- The UI ask from Codex is "wire the existing dashboard and shell session to upcoming inventory pipe data," not "invent new UI surfaces."
- After this packet, the next likely Claude lane will be delta scanning orchestration: USN journal, watcher fallback, and scheduled bounded rescans.
