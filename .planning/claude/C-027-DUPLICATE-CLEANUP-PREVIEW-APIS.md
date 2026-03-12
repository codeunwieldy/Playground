# C-027 Duplicate Cleanup Preview APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-026`

## Why This Is Next

Atlas can now answer a retained duplicate decision question for one group:

- is this group cleanup-eligible?
- does it need review?
- should Atlas keep, review, or quarantine duplicates?

But the backend still cannot answer the next operational question the shell needs:

- "what would Atlas actually do for this one retained group if cleanup were previewed right now?"
- "which paths would be kept versus quarantined?"
- "how many operations would this create under the current policy?"

Codex is wiring `C-026` into the existing duplicate drill-in now, so this packet should stay backend-only and expose a bounded, read-only cleanup preview for one retained duplicate group without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Reuse `SafeDuplicateCleanupPlanner` and existing duplicate/policy logic instead of inventing a second cleanup planner
- Keep the route read-only; do not execute cleanup in this packet
- Preserve conservative posture over optimistic automation
- Do not redesign plan execution or duplicate persistence broadly if one narrow route and helper shape are enough

## Read First

1. `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
2. `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
6. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
7. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
8. `.planning/claude/C-025-DUPLICATE-GROUP-DETAIL-AND-EVIDENCE-APIS.md`
9. `.planning/claude/C-026-DUPLICATE-ACTION-ELIGIBILITY-AND-REVIEW-APIS.md`
10. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose a bounded preview route for one retained duplicate group so the shell can show the exact cleanup shape Atlas would propose under the current policy without rebuilding duplicate cleanup logic in the app.

## Required Deliverables

### 1. Additive duplicate cleanup preview contracts

Add narrow protobuf contracts for a retained duplicate cleanup preview. The response should be able to answer:

- whether the group was found
- whether cleanup preview is available under the current policy
- the recommended posture carried through from `C-026`
- the canonical path Atlas would keep
- bounded operation previews for the group
- bounded blocked reasons and action notes when preview cannot be produced

Prefer one bounded response over broad plan/execution contract churn.

### 2. Service-side deterministic preview route

Add a read-only route such as:

- `inventory/duplicate-cleanup-preview`

The handler should:

- load one retained duplicate group
- reuse the existing cleanup planner / evaluator
- emit a deterministic preview of the cleanup shape for that group only
- avoid mutating files, creating checkpoints, or persisting execution batches

### 3. Conservative preview behavior

Ensure the route reacts truthfully to:

- sensitive members
- sync-managed members
- protected members
- low cleanup confidence
- missing session or missing group
- groups that should stay `Keep` or `Review`

If Atlas should not produce cleanup operations, fail closed and explain why with bounded reasons.

### 4. Small shared-core / repository evolution only if needed

If a narrow helper is needed to turn retained duplicate data into planner input, keep it additive and focused. Do not redesign the duplicate model broadly if one small shared helper is enough.

### 5. Tests

Add focused tests for:

- cleanup-eligible retained group returns bounded operation previews
- review-heavy or blocked groups return no actionable preview
- missing session / missing group returns `Found = false`
- canonical keep path is truthful
- bounded result sizes are enforced

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new route/contracts
- exact cleanup-preview fields now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can request a bounded cleanup preview for one retained duplicate group
- the preview is deterministic and policy-aware
- blocked groups do not emit misleading operations
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: additive contracts and pipe route wiring
- Subagent B: cleanup preview composition from existing duplicate cleanup logic
- Subagent C: tests and bounded-payload review

## Notes From Codex

- I am wiring `C-026` decision support into the current duplicate drill-in now.
- Keep this packet read-only and preview-oriented, not execution-oriented.
- Optimize for a response the existing Memory / Plan Review surfaces can consume without another backend redesign.
