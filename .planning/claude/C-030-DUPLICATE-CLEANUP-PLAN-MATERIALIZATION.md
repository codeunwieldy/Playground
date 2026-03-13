# C-030 Duplicate Cleanup Plan Materialization Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-029`

## Why This Is Next

Atlas can now answer a retained-session duplicate cleanup plan preview with:

- included vs blocked groups
- bounded grouped operations
- rationale
- rollback posture

But the backend still cannot answer the next product question cleanly:

- "can Atlas materialize that deterministic retained duplicate cleanup plan into a standard plan-review shape the rest of the product already understands?"
- "can the app reuse existing plan review and execution-preview surfaces without rebuilding duplicate plan logic itself?"

Codex is wiring `C-029` into the current Memory and Plan Review surfaces now. This packet should stay backend-only and expose one deterministic materialization step that turns the retained duplicate cleanup plan preview into an app-ready review payload without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Reuse the duplicate cleanup plan preview from `C-029`; do not build a second cleanup brain
- Prefer a deterministic read-only materialization step before any execution-oriented route
- Preserve conservative posture over optimistic automation

## Read First

1. `.planning/claude/C-029-DUPLICATE-CLEANUP-PLAN-PREVIEW-APIS.md`
2. `src/Atlas.Core/Contracts/PipeContracts.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
5. `src/Atlas.Service/Services/PlanExecutionService.cs`
6. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose a deterministic backend route that materializes the retained duplicate cleanup plan preview into an app-ready review payload that can feed existing plan-oriented surfaces without app-side duplicate-plan synthesis.

## Required Deliverables

### 1. Additive materialization contracts

Add narrow protobuf contracts for a retained duplicate cleanup plan materialization route. The response should be able to answer:

- whether a retained session cleanup plan could be materialized
- a stable plan identifier or review identifier
- grouped operations in a standard review-friendly shape
- a bounded summary, rationale, and rollback posture
- blocked or degraded reasons when materialization is not possible

### 2. Read-only materialization route

Add a deterministic read-only route such as:

- `inventory/duplicate-cleanup-plan-materialize`

The handler should:

- start from the `C-029` retained cleanup plan preview
- materialize only the bounded included groups
- preserve rationale, rollback posture, and blocked-reason truth
- avoid file mutation and avoid execution in this packet

### 3. Reuse existing review vocabulary where practical

If Atlas already has a stable plan/review DTO shape that can be reused narrowly, prefer that over inventing a second long-lived plan surface. If reuse would create broad churn, add the narrowest compatible materialization response possible.

### 4. Conservative failure behavior

Ensure truthful handling for:

- missing retained session
- no duplicate groups
- all groups blocked
- mixed included and blocked groups
- bounded max group and operation limits
- degraded retained-session trust if that should stop materialization

### 5. Tests

Add focused tests for:

- successful materialization from a valid retained cleanup plan preview
- blocked-only retained sessions producing no materialized review payload
- bounded limits remaining enforced
- rationale / rollback posture carrying through
- empty or missing session behavior

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new route/contracts
- exact materialized review fields now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can request a deterministic retained duplicate cleanup plan materialization payload
- the payload is bounded and policy-aware
- it is read-only
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: contracts and route wiring
- Subagent B: deterministic materialization logic from `C-029`
- Subagent C: tests and bounded-failure review

## Notes From Codex

- I am wiring `C-029` into the current Memory and Plan Review surfaces now.
- The next clean backend seam is a review payload the existing plan-oriented UI can consume without rebuilding duplicate plan logic in the app.
- Keep this packet read-only and additive.
