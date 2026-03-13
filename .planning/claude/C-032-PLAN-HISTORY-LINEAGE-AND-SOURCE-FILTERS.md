# C-032 Plan History Lineage and Source Filters Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-031`

## Why This Is Next

Atlas can now:

- materialize a retained duplicate cleanup plan (`C-030`)
- explicitly promote that retained cleanup plan into standard saved-plan history (`C-031`)

Codex is wiring the app so users can promote retained cleanup plans from Memory and Plan Review.

The next backend gap is no longer plan creation. It is plan history truth:

- "which saved plans came from retained duplicate cleanup promotion versus normal planning?"
- "can Atlas query or filter those plans without forcing the app to infer lineage from titles or rationale text?"
- "can plan history preserve the source session and origin cleanly?"

This packet should deepen plan-history metadata and bounded query behavior without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Keep changes additive and bounded
- Reuse existing plan repository/history routes where possible
- Do not rebuild a second plan history surface
- Do not introduce execution or mutation behavior in this packet

## Read First

1. `.planning/claude/C-031-DUPLICATE-CLEANUP-PLAN-PROMOTION-AND-PERSISTENCE.md`
2. `src/Atlas.Core/Contracts/PipeContracts.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Storage/Repositories/IPlanRepository.cs`
5. `src/Atlas.Storage/Repositories/PlanRepository.cs`
6. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
7. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose truthful lineage metadata and bounded source-aware query behavior for saved plans, especially plans promoted from retained duplicate cleanup flows.

## Required Deliverables

### 1. Plan lineage truth

Persist and expose enough metadata so saved plans can distinguish:

- normal planner-originated plans
- retained duplicate cleanup promoted plans
- any source session linkage that already exists from `C-031`

Prefer narrowly additive metadata over broad domain churn.

### 2. History query improvements

Add the narrowest useful read-side support for:

- source-aware summaries in existing history plan responses
- optional bounded filtering by plan source / lineage

If filtering belongs on the existing history list request, evolve it additively instead of inventing a parallel history route unless a new route is truly cleaner.

### 3. Detail-query truth

Ensure plan-detail retrieval can surface lineage/source metadata cleanly enough for the app to explain:

- how the plan was created
- whether it came from retained duplicate cleanup promotion
- which retained scan session it came from, when available

### 4. Conservative behavior

Handle truthfully:

- older saved plans with no lineage metadata yet
- newly promoted retained cleanup plans
- mixed plan history with multiple plan sources
- filters that return no results

### 5. Tests

Add focused tests for:

- promoted retained cleanup plans persisting lineage metadata
- list/history queries surfacing the lineage/source correctly
- optional source filter behavior
- older plans remaining readable without new metadata

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- lineage fields added
- query/filter behavior added
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can distinguish promoted retained-cleanup plans in saved plan history
- history queries expose lineage/source truth cleanly
- older plan history still works
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: repository/schema lineage metadata
- Subagent B: history contract/query evolution
- Subagent C: tests and backward-compatibility review

## Notes From Codex

- I am making retained cleanup plan promotion an explicit user action in the app now.
- The next clean backend seam is history truth, not more hidden plan generation.
- Keep this packet backend-only and additive.
