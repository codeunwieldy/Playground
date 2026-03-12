# C-019 Trust-Aware Plan Gating Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-018`

## Why This Is Next

`C-018` made degraded retained scan sessions real:

- `IsTrusted=false` now round-trips through the existing inventory read path
- `CompositionNote` now carries truthful degradation notes
- Atlas can distinguish trusted retained sessions from degraded ones

The next backend gap is that planning and execution still need to *respect* that truth, not just expose it. Codex is already surfacing scan trust in the UI, so this packet should close the service-side safety loop.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep the service-only mutation boundary intact
- Prefer additive evolution over broad contract churn
- Preserve conservative behavior over optimistic behavior

## Read First

1. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
2. `src/Atlas.Service/Services/PlanExecutionService.cs`
3. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Core/Contracts/DomainModels.cs`
6. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
7. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
8. `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
9. `.planning/claude/C-017-INCREMENTAL-COMPOSITION-ACTIVATION.md`
10. `.planning/claude/C-018-UNTRUSTED-SESSION-STATES-AND-PARTIAL-DEGRADATION.md`
11. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas planning and execution trust-aware, so degraded retained scan state can raise service-side review or blocking behavior instead of being treated like a clean trusted baseline.

## Required Deliverables

### 1. Planning-time trust awareness

When Atlas drafts a plan against degraded retained inventory, make that fact visible through the existing plan risk surface whenever possible.

Preferred outcomes:

- add a blocked reason or explicit review reason tied to degraded retained scan trust
- elevate approval posture when the retained inventory is degraded
- keep the explanation operator-readable and specific

If a minimal additive contract change is truly required to carry scan/session lineage into plan generation, keep it narrow and document it clearly.

### 2. Execution-time trust gating

Strengthen execution safety so Atlas does not treat degraded retained scan posture as equivalent to a trusted retained baseline.

At minimum:

- dry-run / preview should remain available
- destructive execution should stay blocked or explicitly gated when the plan depends on degraded retained scan trust
- service responses should explain why the batch is being held

### 3. Truthful linkage

Ensure Atlas can connect the plan/execution decision to the relevant retained scan trust state in a conservative way.

If the current plan pipeline lacks enough lineage, add the smallest safe bridge needed rather than guessing.

### 4. Tests

Add focused tests for:

- plan generation against trusted retained scan state
- plan generation against degraded retained scan state
- execution preview still available when retained scan trust is degraded
- live execution blocked or elevated when retained scan trust is degraded
- user-facing blocked or review reasons are stable and truthful

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact planning-time behavior for degraded retained scans
- exact execution-time behavior for degraded retained scans
- any additive contract changes and why they were necessary
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- service-side planning or execution behavior now reacts to degraded retained scan trust
- preview remains available, but risky execution does not silently proceed on degraded inventory
- existing UI can consume the behavior through current risk / message surfaces, or only through a very narrow additive contract change
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: plan-risk integration and blocked/review semantics
- Subagent B: execution-time trust gate
- Subagent C: tests and any minimal contract bridge

## Notes From Codex

- The shell already surfaces trust posture, scan provenance, and degradation notes.
- The next most valuable backend improvement is to make the service enforce that truth, not just expose it.
- Optimize for the smallest safe bridge between retained scan trust and plan/execution behavior.
