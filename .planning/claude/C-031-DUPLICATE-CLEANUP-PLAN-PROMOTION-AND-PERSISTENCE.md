# C-031 Duplicate Cleanup Plan Promotion and Persistence Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-030`

## Why This Is Next

Atlas can now materialize a retained duplicate cleanup plan into a standard `PlanGraph`, which means Codex can feed it into the existing plan-review surfaces without inventing a second UI path.

But that materialized plan is still effectively ephemeral. The next product gap is:

- "can Atlas intentionally promote that retained duplicate cleanup plan into the standard saved plan history?"
- "can later execution, undo, and history flows see that retained duplicate cleanup plan as a first-class plan instead of only an in-memory review payload?"

`C-030` should stay read-only. This packet should add the first explicit backend step that saves a materialized retained duplicate cleanup plan into the existing plan-history vocabulary without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Keep `C-030` read-only; do not quietly add write-side persistence to the materialization route
- Reuse the `C-030` materialization core; do not build a second retained cleanup planner
- Prefer one narrow explicit save/promote step over broad contract churn
- Do not execute filesystem mutations in this packet
- Preserve conservative trust/policy behavior over optimistic automation

## Read First

1. `.planning/claude/C-030-DUPLICATE-CLEANUP-PLAN-MATERIALIZATION.md`
2. `src/Atlas.Core/Contracts/PipeContracts.cs`
3. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
4. `src/Atlas.Storage/Repositories/IPlanRepository.cs`
5. `src/Atlas.Storage/Repositories/PlanRepository.cs`
6. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose an explicit backend route that promotes a materialized retained duplicate cleanup plan into Atlas' standard saved-plan history so downstream review, execution, undo, and history surfaces can reason about it as a first-class plan.

## Required Deliverables

### 1. Additive promotion contracts

Add narrow protobuf contracts for a write-side promotion route, for example:

- `inventory/duplicate-cleanup-plan-promote`

The request should be able to identify:

- retained session
- bounded group/operation limits
- any narrow options truly required for promotion

The response should be able to answer:

- whether promotion succeeded
- the saved plan id
- whether the plan was newly saved or already represented
- bounded degraded / blocked reasons when promotion cannot happen

### 2. Explicit save/promote route

Implement a deterministic route that:

- starts from the shared `C-030` materialization core
- refuses promotion when the retained session is degraded or untrusted
- persists the resulting `PlanGraph` via the existing plan repository
- avoids execution and avoids any filesystem mutation

### 3. Lineage truth

Make sure the promoted plan preserves truthful origin, such as:

- retained duplicate cleanup source
- rationale
- rollback posture
- any bounded degradation or blocked notes that matter to later review/history flows

If the current `PlanGraph` already has a suitable place for this truth, reuse it narrowly.

### 4. Conservative failure behavior

Handle truthfully:

- missing retained session
- no duplicate groups
- all groups blocked
- degraded or untrusted retained session
- bounded max-group / max-operation limits
- repeated promotion requests for materially identical retained cleanup plans

### 5. Tests

Add focused tests for:

- successful promotion of a materializable retained duplicate cleanup plan
- degraded retained sessions refusing promotion
- all-blocked plans refusing promotion
- promoted plans being readable from the existing plan repository/history path
- repeated promotion behavior staying deterministic

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new route/contracts
- how saved retained-cleanup plans now appear in standard plan history
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can explicitly promote a retained duplicate cleanup plan into standard saved-plan history
- the route is bounded and policy-aware
- `C-030` remains read-only
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: promotion contracts and route wiring
- Subagent B: repository persistence and lineage truth
- Subagent C: tests and repeated-promotion behavior

## Notes From Codex

- I am wiring the `C-030` materialized plan into the existing shell and plan-review state now.
- The next clean backend seam is an explicit "save/promote this retained cleanup plan" step so plan history and later execution flows stop treating it as only an in-memory review payload.
- Keep the packet backend-only and additive.
