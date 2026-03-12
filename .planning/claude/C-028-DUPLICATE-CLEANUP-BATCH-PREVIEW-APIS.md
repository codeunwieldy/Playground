# C-028 Duplicate Cleanup Batch Preview APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-027`

## Why This Is Next

Atlas can now answer the retained duplicate cleanup question for one group:

- is the group cleanup-eligible?
- what posture does Atlas recommend?
- what exact cleanup operations would Atlas preview for that one group?

But the backend still cannot answer the next higher-level review question the shell will need:

- "what would a bounded duplicate cleanup pass look like across the retained session?"
- "how many groups are previewable versus blocked?"
- "how many quarantine operations would Atlas stage before any execution is considered?"

Codex is wiring `C-027` into the current duplicate drill-in now, so this packet should stay backend-only and expose a bounded, read-only batch cleanup preview for retained duplicate groups without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Reuse `SafeDuplicateCleanupPlanner`, `DuplicateActionEvaluator`, and the single-group cleanup preview logic from `C-027`
- Keep the route read-only; do not execute cleanup in this packet
- Preserve conservative posture over optimistic automation
- Do not redesign duplicate persistence or plan execution broadly if one narrow route and helper shape are enough

## Read First

1. `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
2. `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs`
3. `src/Atlas.Core/Contracts/PipeContracts.cs`
4. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
5. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
6. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
7. `.planning/claude/C-026-DUPLICATE-ACTION-ELIGIBILITY-AND-REVIEW-APIS.md`
8. `.planning/claude/C-027-DUPLICATE-CLEANUP-PREVIEW-APIS.md`
9. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose a bounded retained-session duplicate cleanup preview so the shell can later review cleanup impact across multiple groups without rebuilding duplicate cleanup logic in the app.

## Required Deliverables

### 1. Additive batch cleanup preview contracts

Add narrow protobuf contracts for a retained duplicate batch cleanup preview. The request/response should be able to answer:

- whether the retained session was found
- how many duplicate groups were evaluated
- how many groups are previewable versus blocked
- bounded group-level preview summaries
- total previewable cleanup operation count
- the effective cleanup-confidence threshold used
- bounded reasons and notes for groups that stay blocked

Prefer one bounded aggregate response over broad contract churn.

### 2. Service-side deterministic batch preview route

Add a read-only route such as:

- `inventory/duplicate-cleanup-batch-preview`

The handler should:

- load retained duplicate groups for one session
- evaluate them with the existing cleanup planner/evaluator logic
- emit a bounded batch preview for only the top eligible/relevant groups
- avoid mutating files, creating checkpoints, or persisting execution batches

### 3. Conservative bounding behavior

Ensure the route behaves truthfully for:

- sessions with no duplicate groups
- mixed eligible and blocked groups
- sensitive, sync-managed, and protected groups
- low cleanup-confidence groups
- route-level limits on group count and per-group preview size

If a group should not emit operations, fail closed and return truthful blocked reasons.

### 4. Small shared-core / repository evolution only if needed

If one narrow additive helper is needed to assemble batch previews cleanly, keep it focused. Do not redesign duplicate persistence broadly if the current read model is already enough.

### 5. Tests

Add focused tests for:

- mixed retained groups produce truthful previewable and blocked counts
- limit enforcement keeps payloads bounded
- total operation count is truthful
- blocked groups do not emit misleading operation previews
- missing session returns `Found = false`

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new route/contracts
- exact batch preview fields now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can request a bounded retained-session duplicate cleanup batch preview
- the preview is deterministic and policy-aware
- previewable versus blocked groups are reported truthfully
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: additive contracts and pipe route wiring
- Subagent B: batch preview composition from existing per-group cleanup preview logic
- Subagent C: tests and bounded-payload review

## Notes From Codex

- I am wiring the single-group cleanup preview from `C-027` into the current Memory and Plan Review duplicate drill-in now.
- Keep this packet read-only and preview-oriented, not execution-oriented.
- Optimize for a response the shell can later consume as a session-level cleanup-impact review without another backend redesign.
