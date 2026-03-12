# C-016 Incremental Provenance Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-015`

## Why This Is Next

`C-015` is the backend bridge from "Atlas can detect changes" to "Atlas can compose new persisted sessions from bounded deltas."

That is the right backend move, but Codex still cannot read that new provenance cleanly through the pipe layer. The shell already has provenance-ready UI surfaces in:

- `src/Atlas.App/Services/AtlasShellSession.cs`
- `src/Atlas.App/Views/DashboardPage.xaml`
- `src/Atlas.App/Views/HistoryPage.cs`
- `src/Atlas.App/Views/PlansPage.xaml`

The next value is to expose incremental-session lineage and composition metadata through the existing read-side service boundary so the native shell can stop inferring provenance from timestamps and counts alone.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, shell navigation, or visual design
- Keep the service-only mutation boundary intact
- Prefer additive evolution of the existing inventory query contracts over broad rewrites
- Do not weaken or bypass the read-only pipe boundary

## Read First

1. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
2. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
3. `src/Atlas.Core/Contracts/PipeContracts.cs`
4. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
5. `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
6. `.planning/claude/C-013-SCAN-DIFF-AND-DRIFT-QUERY-APIS.md`
7. `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
8. `spec/HANDOFF.md`

## Goal

Expose persisted session provenance and incremental-composition lineage through bounded read-only query APIs so Codex can drive the existing scan-provenance and rescan-story UX from real service data.

## Required Deliverables

### 1. Inventory provenance DTO design

Add the narrowest app-ready read model needed to describe session lineage.

At minimum, Codex needs bounded access to:

- scan trigger or origin (`Manual`, `Orchestration`, or equivalent)
- session build mode (`FullRescan`, `IncrementalComposition`, or safe fallback equivalent)
- delta source used (`UsnJournal`, `Watcher`, `ScheduledRescan`, or fallback)
- baseline session ID when composition used one
- whether Atlas trusted the session as a full-session result
- optional fallback or composition note if the session degraded from incremental to full

If `C-015` named these fields differently, keep the read path aligned to that truth instead of inventing parallel semantics.

### 2. Read-only pipe exposure

Expose provenance through the existing inventory query surface in the most stable way.

Acceptable shapes:

- additive fields on existing inventory snapshot / session summary / session detail contracts
- or one narrow new read-only route if that is materially cleaner

Prefer the smallest contract change that keeps the shell simple.

### 3. Service handler integration

Update `AtlasPipeServerWorker` so the new provenance data flows through the pipe boundary without direct app-to-database coupling.

Requirements:

- bounded payloads only
- clean empty-state behavior
- missing session behavior must stay typed and explicit

### 4. Repository support

If the current repository read methods do not surface the provenance from `C-015`, add the narrowest repository changes needed to do so.

Avoid redesigning the existing inventory repository unless absolutely necessary.

### 5. Tests

Add focused tests for:

- snapshot response includes provenance for the latest session
- session list returns provenance summary data
- session detail returns baseline lineage when composition used one
- full-rescan sessions report clear non-incremental provenance
- missing or empty sessions return clean typed responses

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact provenance fields now queryable by Codex
- whether the data was exposed by additive contract fields or a new route
- what still remains deferred
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Codex can query real session provenance through the pipe layer
- the existing shell does not need a new UI architecture to consume it
- the API shape is additive, bounded, and typed
- tests exist and pass
- no UI files were touched

## Suggested Claude Subagent Split

- Subagent A: repository read model and contract shape
- Subagent B: pipe/service handler exposure
- Subagent C: tests for provenance snapshot, summary, and session-detail behavior

## Notes From Codex

- The shell already renders scan provenance and rescan-story surfaces. The current UI is ready; it just needs the backend truth.
- Optimize for Codex consuming the new fields inside the existing `AtlasShellSession` instead of building a separate app-side model layer.
- If `C-015` is not yet written into `CLAUDE-OUTBOX.md`, sync that before or alongside the `C-016` report so shared state stays trustworthy.
