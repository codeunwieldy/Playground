# C-026 Duplicate Action Eligibility and Review APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-025`

## Why This Is Next

Atlas can now do two important duplicate-review things:

- list retained duplicate groups with confidence, canonical rationale, and risk flags
- drill into one retained group with bounded evidence and member posture

But the backend still cannot answer the decision question that matters most before cleanup:

- "is this retained group actually eligible for cleanup under the current policy?"
- "what is blocking Atlas from auto-staging this duplicate group?"
- "what would Atlas recommend right now: keep, review, or quarantine duplicates?"

Codex is wiring the new duplicate drill-in into the existing shell now, so this packet should add a deterministic, read-only policy-evaluation lane for retained duplicate groups without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Reuse existing policy, duplicate, and safety logic instead of inventing a separate cleanup brain
- Keep the route read-only; do not execute cleanup in this packet
- Preserve conservative cleanup posture over optimistic automation

## Read First

1. `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
2. `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
3. `src/Atlas.Core/Scanning/DuplicateCanonicalSelector.cs`
4. `src/Atlas.Core/Contracts/DomainModels.cs`
5. `src/Atlas.Core/Contracts/PipeContracts.cs`
6. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
7. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
8. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
9. `src/Atlas.Service/Services/PlanExecutionService.cs`
10. `.planning/claude/C-022-DUPLICATE-EVIDENCE-AND-CONFIDENCE.md`
11. `.planning/claude/C-025-DUPLICATE-GROUP-DETAIL-AND-EVIDENCE-APIS.md`
12. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose a bounded decision-support route for one retained duplicate group so the shell can explain not just what the group is, but whether Atlas would act on it under the current policy and why.

## Required Deliverables

### 1. Additive duplicate action review contracts

Add narrow protobuf contracts for retained duplicate decision support. The response should be able to answer:

- whether the retained group was found
- whether the group is cleanup-eligible under the current policy
- whether review is required
- recommended posture such as `Keep`, `Review`, or `QuarantineDuplicates`
- bounded blocked reasons / gating reasons
- bounded action notes that explain why Atlas would or would not stage cleanup

Prefer one bounded response over a broad redesign.

### 2. Service-side deterministic evaluation

Add a read-only route such as:

- `inventory/duplicate-action-review`

The handler should:

- load one retained duplicate group
- evaluate it against the current policy and the existing safe duplicate cleanup logic
- return a deterministic recommendation without mutating files or creating batches

Do not build a second cleanup rules engine if the existing planner/policy logic can be reused or extracted cleanly.

### 3. Conservative policy behavior

Ensure the route reacts truthfully to:

- sensitive members
- sync-managed members
- protected members
- low cleanup confidence
- missing session or missing group

The response should fail closed and explain why.

### 4. Clean repository / service evolution

If the evaluation needs one narrow additive repository helper or one shared-core extraction, keep it additive and small. Do not redesign the inventory repository broadly if one helper is enough.

### 5. Tests

Add focused tests for:

- cleanup-eligible retained group returns actionable posture
- sensitive / sync-managed / protected groups force review or block eligibility
- low-confidence groups stay non-actionable
- missing session / missing group returns `Found = false`
- blocked reasons and action notes stay bounded

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new route/contracts
- exact decision-support fields now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can ask for a bounded action review of one retained duplicate group
- the recommendation is deterministic and policy-aware
- blocked reasons are truthful and bounded
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: additive contracts and pipe route wiring
- Subagent B: deterministic policy evaluation using existing duplicate cleanup logic
- Subagent C: tests and bounded-reason review

## Notes From Codex

- I am wiring `C-025` duplicate detail into the current Memory and Plan Review surfaces now.
- Keep this packet read-only and decision-oriented, not execution-oriented.
- Optimize for a response the existing shell can consume later without another backend redesign.
