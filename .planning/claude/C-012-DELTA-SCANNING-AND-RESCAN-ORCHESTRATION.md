# C-012 Delta Scanning and Rescan Orchestration Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

`C-010` and `C-011` completed the durable inventory foundation:

- full scan-session persistence
- stored roots, volumes, and file snapshots
- read-only inventory query APIs

That means Atlas can now remember scans, but it still behaves like a full-rescan-first product. The next backend step is to lay the groundwork for incremental inventory refresh so Atlas can eventually answer "what changed?" without depending on a full walk of every mutable root each time.

Codex is already wiring the dashboard and memory workspace to persisted scan history. This packet should move the backend toward delta-aware behavior without taking UI ownership.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Do not weaken the service-only boundary
- Do not introduce direct app-side access to SQLite, filesystem watchers, or USN state
- Keep the design Windows-first but fail safe when a delta source is unavailable
- Avoid large speculative rewrites; prefer additive service/runtime foundations

## Read First

1. `src/Atlas.Service/Services/FileScanner.cs`
2. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
3. `src/Atlas.Service/Services/AtlasStartupWorker.cs`
4. `src/Atlas.Service/Services/AtlasServiceOptions.cs`
5. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
6. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
7. `src/Atlas.Core/Contracts/PipeContracts.cs`
8. `.planning/claude/C-010-INVENTORY-PERSISTENCE-FOUNDATIONS.md`
9. `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
10. `.planning/claude/CLAUDE-INBOX.md`

## Goal

Add service-side delta scanning foundations so Atlas can:

- track whether incremental observation is available for a mutable root or volume
- fall back safely when USN or watcher signals are unavailable
- schedule bounded rescans instead of blindly relying on repeated full scans
- persist the resulting scan sessions through the existing inventory repository

This packet does not need to deliver the full end-state UX. It should deliver the backend foundation Codex can build on next.

## Required Deliverables

### 1. Delta-source abstraction

Introduce a small additive abstraction for incremental scan change detection.

Good outcomes:

- clear interface for obtaining change signals or availability status
- explicit distinction between:
  - USN-capable
  - watcher-capable fallback
  - scheduled-rescan-only fallback
- deterministic result model that the service can reason about

Keep this small and testable. Do not bury orchestration rules inside ad hoc conditionals if a lightweight abstraction makes the behavior easier to verify.

### 2. Windows-first fallback strategy

Atlas is a Windows 11 desktop product, so the design should prefer USN/change-journal-friendly paths where supported. But it must fail safe.

Required behavior:

- if a root/volume is not suitable for the preferred delta path, Atlas should degrade gracefully
- fallback must never broaden mutation scope
- unsupported volumes or roots should remain observable via bounded rescans
- do not block the entire service because one root lacks incremental support

If actual USN integration is too large for this packet, build the orchestration seam and capability model cleanly, then implement the safe fallback path completely.

### 3. Bounded rescan orchestration

Add a service-side orchestration path for bounded rescans.

Examples of useful behavior:

- remember the most recent mutable roots that were scanned
- schedule or trigger a lightweight follow-up scan path
- avoid unbounded or tight-loop rescans
- make cadence/configuration visible through service options

This does not need to become a full always-on daemon in one packet. It does need a credible, testable orchestration foundation.

### 4. Persistence continuity

Reuse the existing inventory persistence path.

Needed outcome:

- delta-triggered or fallback rescans still persist normal scan sessions
- no special one-off storage format that bypasses the current repository layer
- if you need tiny additive metadata to support orchestration, keep it narrow and explain why

### 5. Service/runtime integration

Wire the new orchestration into the service in a way that is safe to run locally.

Good targets:

- options/config registration
- startup worker or dedicated background worker integration
- clean logging around capability detection and fallback mode
- graceful shutdown behavior

### 6. Tests

Add focused backend tests.

Good targets:

- unsupported delta source falls back to scheduled rescan
- fallback remains bounded and does not spin endlessly
- persisted inventory sessions are still written after an orchestrated rescan
- capability detection is deterministic for mocked roots/volumes
- orchestration respects configured cadence/bounds

Prefer unit/integration tests that avoid live OS-only dependencies where possible.

### 7. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- new abstractions/options/workers added
- what "delta-ready" means after this packet
- what still remains deferred
- tests added and whether they pass

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Capability model and abstractions

- own delta-source contracts
- own capability/fallback result types
- own service option additions

### Subagent B: Orchestration worker/runtime

- own startup/background integration
- own bounded rescan scheduling
- own persistence continuity through existing scan repository path

### Subagent C: Tests and verification

- own fallback and scheduling tests
- own persistence-through-orchestration tests
- own build/test pass

## Success Criteria

This packet is successful when:

- Atlas has a clear backend seam for incremental scan observation
- unsupported roots/volumes degrade to safe bounded rescans
- service-side orchestration exists and is test-covered
- persisted scan sessions continue to flow through the current repository model
- no UI files were touched

## Notes From Codex

- Codex is actively using the new persisted inventory query APIs in the dashboard and memory workspace now.
- The next UI value after this packet is likely "scan drift" and "what changed since the last scan", so structure your abstractions so later diff/query APIs are easy to add.
- Do not burn time inventing a full UI contract in this packet.
- Prefer explicit capability states and bounded scheduling over clever implicit behavior.
- If full USN integration is too large for one packet, ship the capability seam plus safe watcher/scheduled-rescan fallback rather than overreaching.
