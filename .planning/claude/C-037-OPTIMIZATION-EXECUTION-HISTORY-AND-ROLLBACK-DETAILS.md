# C-037 Optimization Execution History and Rollback Details Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-034`

## Why This Is Next

Atlas can now apply safe optimization fixes and retain rollback states inside checkpoints, but optimization execution is still under-explained as a durable backend story. The next gap is execution history truth: what was applied, what was reversible, and what actually happened.

This packet should deepen optimization execution history without touching `src/Atlas.App/**`.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Avoid `src/Atlas.Core/Contracts/PipeContracts.cs`
- Avoid conversation-compaction files
- Prefer optimization, execution, recovery, and storage files
- Do not widen the safe fix list in this packet

## Read First

1. `.planning/claude/C-034-SAFE-OPTIMIZATION-FIX-APPLICATION-AND-ROLLBACK.md`
2. `src/Atlas.Service/Services/SafeOptimizationFixExecutor.cs`
3. `src/Atlas.Service/Services/PlanExecutionService.cs`
4. `src/Atlas.Core/Contracts/DomainModels.cs`
5. `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
6. `src/Atlas.Storage/Repositories/OptimizationRepository.cs`

## Goal

Persist a truthful backend history of optimization fix execution and rollback posture so later query APIs and UI work do not have to infer execution outcomes from generic findings alone.

## Required Deliverables

### 1. Execution history model

Add additive execution-history support for safe optimization fixes, including:

- what fix kind ran
- target
- whether it succeeded
- whether it was reversible
- what rollback state or note was retained

### 2. Service integration

Integrate that history into live execution and undo paths so Atlas retains truthful optimization outcome history after:

- apply
- revert
- partial failure

### 3. Conservative behavior

Handle truthfully:

- already-clean targets
- already-disabled startup entries
- non-reversible cleanup kinds
- failed reverts
- partial batches mixing reversible and non-reversible fixes

### 4. Tests

Add focused tests for:

- successful execution history capture
- rollback detail capture
- non-reversible fix truth
- repeated apply/revert behavior
- partial failure semantics

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- history model added
- rollback detail behavior
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- optimization fix execution has durable backend history truth
- rollback posture is retained honestly
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: storage/history model
- Subagent B: service integration
- Subagent C: tests and safety review

## Notes From Codex

- I am surfacing optimization posture in the shell; this packet should make the backend story as explicit as the UI.
- Keep the scope on history truth, not new fix kinds.
