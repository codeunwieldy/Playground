# C-023 Persisted Duplicate Review and Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-022`

## Why This Is Next

`C-022` made live duplicate groups much more truthful:

- duplicate groups now carry cleanup confidence and match confidence separately
- canonical choice has a human-readable rationale
- risk flags now capture sensitive, sync-managed, and protected membership

But that truth still drops on the floor after a scan session is persisted:

- inventory persistence only stores `duplicate_group_count`
- retained scan history cannot surface actual duplicate review groups
- Codex can use the new fields in the live shell today, but not in persisted scan memory yet

Codex is taking the app/planning/review integration lane in parallel, so this packet should make duplicate review durable and queryable without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Preserve conservative duplicate behavior over optimistic cleanup
- Do not redesign the existing inventory/session APIs more broadly than needed

## Read First

1. `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
2. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
3. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
4. `src/Atlas.Core/Contracts/DomainModels.cs`
5. `src/Atlas.Core/Contracts/PipeContracts.cs`
6. `src/Atlas.Service/Services/FileScanner.cs`
7. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
8. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
9. `.planning/claude/C-022-DUPLICATE-EVIDENCE-AND-CONFIDENCE.md`
10. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make retained scan sessions carry duplicate review truth, and expose that truth through bounded read-side APIs so Codex can feed it into existing memory/review surfaces later.

## Required Deliverables

### 1. Additive duplicate persistence

Persist duplicate groups for a scan session, including bounded additive evidence fields such as:

- group id
- canonical path
- cleanup confidence
- match confidence
- canonical reason
- max sensitivity
- sensitive / sync-managed / protected flags
- bounded member paths or a normalized member table

Prefer a normalized design that keeps query/read costs reasonable.

### 2. Inventory repository evolution

Evolve the inventory repository additively so Atlas can:

- save duplicate groups as part of session persistence
- read duplicate groups for a session
- optionally page or bound duplicate-group reads if needed

Do not break existing inventory callers.

### 3. Service-side persistence continuity

Ensure duplicate groups are persisted from:

- live full scans
- persisted rescans / orchestration paths
- incremental composition paths when a session is saved

The goal is that a retained session can later answer “what duplicate groups did Atlas see?” instead of only “how many?”

### 4. Read-only duplicate query API

Add a bounded pipe route for duplicate review tied to a session, such as:

- list duplicate groups for a session
- optionally include a bounded member sample per group

Keep it read-only and app-ready.

### 5. Tests

Add focused tests for:

- schema/bootstrap evolution
- session save + read round-trip for duplicate groups
- preserving canonical reason / confidence / risk flags
- route behavior for empty sessions and bounded results
- rescans persisting duplicate groups through existing save paths

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- schema or repository changes made
- exact duplicate fields now persisted and queryable
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- retained scan sessions can answer more than duplicate count
- duplicate review evidence survives persistence
- a bounded read-side API exists for session duplicate groups
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: storage schema and repository evolution
- Subagent B: service persistence and pipe route wiring
- Subagent C: regression tests and bounded DTO review

## Notes From Codex

- I am wiring live duplicate evidence into planning projection and the existing shell now.
- Please keep this packet focused on persisted duplicate review truth, not UI, not prompt styling, and not optimizer work.
- Optimize for an additive read model that the existing Memory / Plan Review surfaces can consume later without another backend redesign.
