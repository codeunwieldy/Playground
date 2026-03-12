# C-029 Duplicate Cleanup Plan Preview APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-028`

## Why This Is Next

Atlas can now answer retained duplicate cleanup at two levels:

- one-group decision support and cleanup preview
- bounded session-level batch preview with previewable vs blocked counts

But the backend still cannot answer the next review question the shell will eventually need:

- "what exact bounded duplicate-cleanup plan would Atlas put in front of the user for this retained session?"
- "which groups would actually be included in that plan under current policy?"
- "what grouped operations, rationale, and rollback posture would Atlas surface before execution?"

Codex is wiring `C-028` into the current duplicate review surfaces now, so this packet should stay backend-only and expose a deterministic, read-only duplicate cleanup plan preview for retained sessions without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Reuse `SafeDuplicateCleanupPlanner`, `DuplicateActionEvaluator`, and the batch preview logic from `C-028`
- Keep the route read-only; do not execute cleanup in this packet
- Preserve conservative posture over optimistic automation
- Do not redesign the main AI planner broadly if one narrow deterministic duplicate-plan preview is enough

## Read First

1. `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
2. `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
6. `.planning/claude/C-027-DUPLICATE-CLEANUP-PREVIEW-APIS.md`
7. `.planning/claude/C-028-DUPLICATE-CLEANUP-BATCH-PREVIEW-APIS.md`
8. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose a bounded deterministic duplicate-cleanup plan preview so the shell can later present one retained-session cleanup plan for review without rebuilding duplicate cleanup logic in the app or relying on a free-form model step.

## Required Deliverables

### 1. Additive duplicate cleanup plan preview contracts

Add narrow protobuf contracts for a retained-session duplicate cleanup plan preview. The response should be able to answer:

- whether the retained session was found
- how many groups were included in the preview plan
- total planned cleanup operation count
- grouped included vs blocked duplicate groups
- bounded rationale / posture summary
- rollback-oriented notes suitable for app review surfaces

Prefer a bounded deterministic plan-preview response over broad churn to the existing general planning contracts unless reuse is truly cleaner.

### 2. Service-side deterministic plan-preview route

Add a read-only route such as:

- `inventory/duplicate-cleanup-plan-preview`

The handler should:

- start from retained duplicate groups in one session
- include only groups that survive current policy and cleanup-confidence thresholds
- carry forward truthful blocked groups and reasons
- emit a bounded plan-shaped preview without mutating files or creating execution batches

### 3. Conservative inclusion rules

Ensure the route behaves truthfully for:

- sessions with no duplicate groups
- sessions where all groups are blocked
- mixed eligible and blocked groups
- sensitive, sync-managed, and protected groups
- bounded maximum group count and operation count

If Atlas should not stage a duplicate cleanup plan, fail closed and explain why.

### 4. Small shared-core evolution only if needed

If one narrow shared helper is needed to convert retained duplicate preview data into a deterministic plan shape, keep it additive and focused. Do not build a second duplicate cleanup brain.

### 5. Tests

Add focused tests for:

- eligible retained groups produce a bounded duplicate cleanup plan preview
- blocked groups stay out of the included operation set
- mixed sessions report included vs blocked groups truthfully
- empty / missing session returns `Found = false` or equivalent empty preview semantics
- bounded payload limits are enforced

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new route/contracts
- exact duplicate-plan preview fields now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can request a deterministic retained-session duplicate cleanup plan preview
- the preview is policy-aware and bounded
- included vs blocked duplicate groups are truthful
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: additive contracts and pipe route wiring
- Subagent B: deterministic plan-preview composition from existing duplicate cleanup logic
- Subagent C: tests and bounded-payload review

## Notes From Codex

- I am wiring the `C-028` batch preview into the current Memory and Plan Review duplicate surfaces now.
- Keep this packet read-only and review-oriented, not execution-oriented.
- Optimize for a response the shell can later consume as a real duplicate cleanup review step without another backend redesign.
