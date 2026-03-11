# C-006 Execution Hardening Packet

## Owner

Claude Code in VS Code

## Priority

Highest

## Why This Is Open Now

Codex has pushed the WinUI shell well past placeholder status:

- the app can now scan, draft plans, preview execution, preview undo, and show a real before/after review canvas
- the plan screen now explains current layout, proposed layout, rollback story, and risk framing
- the persistence layer is in place and tested

That means the biggest backend gap is no longer storage. It is execution safety.

Right now the service can perform plan operations, but the execution path is still too naive for the product we are building:

- operation ordering is simplistic
- dry runs do not perform a strong preflight
- partial failure behavior is not hardened
- quarantine metadata is incomplete and at least one field is wrong
- there are no focused execution-service tests

This packet should make the real service execution path safer and more durable without crossing into WinUI work.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, interaction design, shell layout, or visual polish
- Do not weaken policy enforcement to make tests easier
- Do not introduce destructive shortcuts like permanent delete fallbacks
- Prefer additive or backward-compatible contract changes if you truly need them
- If a change would materially alter UI-facing contracts, stop and report that instead of forcing it

## Read First

1. `src/Atlas.Service/Services/PlanExecutionService.cs`
2. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
6. `src/Atlas.Core/Planning/RollbackPlanner.cs`
7. `src/Atlas.Storage/Repositories/IPlanRepository.cs`
8. `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
9. `src/Atlas.Storage/Repositories/PlanRepository.cs`
10. `src/Atlas.Storage/Repositories/RecoveryRepository.cs`
11. `.planning/claude/C-005-PERSISTENCE-INTEGRATION.md`
12. `.planning/claude/C-004-INSTALLER-RECOVERY.md`

## Goal

Harden service-side execution so Atlas can:

- validate batches before mutation
- apply operations in a safer deterministic order
- stop cleanly on failure
- return rollback data for the work that actually happened
- keep quarantine metadata accurate enough for future restore and timeline UI
- do all of the above without touching UI code

## Required Deliverables

### 1. Execution preflight

Add a deterministic preflight layer for `PlanExecutionService` that runs for both dry-run and real execution.

Minimum expectations:

- reject operations with missing required paths
- verify source existence for move/rename/quarantine/restore actions
- reject destination collisions instead of failing halfway through a batch
- validate that parent folders are creatable for destination paths
- surface clear messages for blocked operations

If you need a small helper or internal result model, add it.

### 2. Safer operation ordering

Rework batch execution so operations happen in a deterministic order that reduces avoidable failure.

Preferred order:

1. `CreateDirectory`
2. `MovePath` / `RenamePath`
3. `DeleteToQuarantine`
4. `ApplyOptimizationFix`

If a different order is safer for a specific case, document it in the outbox.

Dry-run should reflect the same order and validation logic as real execution.

### 3. Partial-failure handling and rollback integrity

The service currently assumes success too optimistically.

Harden it so that:

- if execution fails midway, remaining operations do not continue
- the response includes meaningful failure messages
- the undo checkpoint only reflects operations that actually completed
- partial-success execution can still be rolled back from the returned checkpoint

Important:

- do not silently swallow real execution failures
- do not fabricate rollback steps for operations that never ran
- it is acceptable to return `Success = false` with a non-empty checkpoint if some work already occurred

### 4. Quarantine correctness

Fix the quarantine path and metadata handling.

At minimum, verify and correct:

- `QuarantineItem.PlanId` uses the real plan or batch context, not unrelated group metadata
- retention uses a stable configurable value if one is already available in scope; otherwise document the temporary default clearly
- destination/current path reporting stays accurate after quarantine moves
- restore metadata remains sufficient for later timeline and single-item restore work

If lightweight content hashing is easy and safe to add without scope blowup, include it. If not, document deferral.

### 5. Persistence touchpoints

Review `AtlasPipeServerWorker` and make sure persistence behavior matches the hardened execution flow.

Desired outcome:

- successful real executions persist the batch and checkpoint
- partial failures that already mutated state still persist enough recovery data to undo what happened
- dry-run behavior stays clear and intentional

If you decide not to persist dry runs, say why.

### 6. Tests

Add focused tests for the execution layer.

Good targets:

- dry-run preflight catches invalid paths and collisions
- create-before-move ordering
- quarantine metadata uses the correct plan context
- partial failure returns rollback only for completed operations
- undo can reverse a batch that actually executed in temp test directories

You can create a narrow `Atlas.Service.Tests` project if needed.

### 7. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files read
- files changed
- key execution risks fixed
- any remaining execution risks
- tests added and whether they pass
- any intentionally deferred pieces

## Suggested Subagent Split

If you use Claude subagents, this split should work well:

### Subagent A: Preflight and ordering

- own operation validation
- own ordering logic
- own dry-run parity

### Subagent B: Partial-failure and rollback

- own executed-operation tracking
- own checkpoint integrity
- own persistence adjustments in pipe/service flow

### Subagent C: Tests and verification

- own execution temp-directory fixtures
- own dry-run and partial-failure tests
- own final build/test pass

## Success Criteria

This packet is successful when:

- execution is validated before mutation
- batch ordering is deliberate and test-covered
- partial failures stop the batch and return accurate recovery data
- quarantine metadata is correct enough for restore/timeline work
- no UI files were touched

## Notes From Codex

- I now have a real plan review surface in the app, including current/proposed structure lanes. The user can inspect plans visually, but I still need the service to be much more trustworthy before we lean on real execution.
- Keep the app/service trust boundary intact. The app can preview and explain; the service must remain the only place that mutates files.
- After this packet, the next likely Claude lane will be either strict AI structured-output hardening or persisted history/read APIs for timeline surfaces.
