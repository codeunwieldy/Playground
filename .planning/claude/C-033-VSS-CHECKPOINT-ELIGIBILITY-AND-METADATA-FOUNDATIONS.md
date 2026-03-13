# C-033 VSS Checkpoint Eligibility and Metadata Foundations Packet

## Owner

Claude Code in VS Code

## Priority

Ready in parallel with `C-032`

## Why This Is Next

Atlas has strong undo, quarantine, and execution-hardening foundations, but the heavyweight rollback side of the product is still shallow. The next backend gap is not full VSS orchestration yet. It is the policy and metadata foundation that decides:

- when Atlas should require a volume-level checkpoint
- what checkpoint coverage a batch is supposed to have
- what metadata must persist so later rollback and audit flows can explain that decision truthfully

This packet should deepen recovery truth without touching `src/Atlas.App/**` and without colliding with plan-history work.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Avoid `src/Atlas.Core/Contracts/PipeContracts.cs`
- Avoid `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` unless a tiny additive change is truly unavoidable
- Prefer service/core/storage files related to execution, undo, checkpointing, and persistence
- Do not implement actual VSS snapshot creation in this packet unless the seam is trivial and low-risk

## Read First

1. `.planning/claude/C-004-INSTALLER-RECOVERY.md`
2. `src/Atlas.Core/Contracts/DomainModels.cs`
3. `src/Atlas.Service/Services/PlanExecutionService.cs`
4. `src/Atlas.Storage/Repositories/IPlanRepository.cs`
5. `src/Atlas.Storage/Repositories/*`
6. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Create the recovery-side truth Atlas needs before real VSS orchestration: deterministic checkpoint eligibility, bounded recovery metadata, and persistence that later packets can reuse.

## Required Deliverables

### 1. Checkpoint eligibility evaluator

Add a deterministic evaluator that can answer whether a batch:

- does not need a checkpoint
- should strongly recommend one
- requires one before execution should continue

Use factors already present in Atlas, such as:

- destructive operation count
- touched roots / library roots
- cross-volume movement
- retained trust posture
- quarantined versus non-quarantined destructive behavior

Keep the rules bounded, explainable, and testable.

### 2. Recovery metadata model

Persist enough additive metadata so an undo/checkpoint record can later answer:

- what Atlas decided about checkpoint eligibility
- why it made that decision
- what volumes / roots the decision covered
- whether a later packet actually created a snapshot or only evaluated eligibility

Prefer additive fields over broad model churn.

### 3. Execution integration seam

Integrate the evaluator into execution preflight or batch-shaping logic so Atlas can retain truthful checkpoint posture even before real VSS creation exists.

The result should make later VSS packets cleaner, not harder.

### 4. Conservative behavior

Handle truthfully:

- small reversible batches that do not need a checkpoint
- large destructive batches that should require one
- untrusted/degraded retained sessions
- mixed batches where only some operations are destructive
- older persisted records with no new checkpoint metadata yet

### 5. Tests

Add focused tests for:

- eligibility threshold behavior
- library-root / destructive-batch triggers
- mixed safe versus destructive batches
- persistence / repository backward compatibility
- execution preflight retaining the new metadata truthfully

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- eligibility rules added
- metadata fields added
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can deterministically say whether a batch needs a heavyweight checkpoint
- that decision is persisted truthfully and additively
- later VSS creation work has a clean seam to plug into
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: eligibility rules and execution-preflight seam
- Subagent B: persistence/model evolution
- Subagent C: tests and backward-compatibility review

## Notes From Codex

- I am not changing the recovery UI contract in this packet.
- I want this to stay backend-only and parallel-safe with plan-history work.
- Optimize for a seam that makes later actual VSS orchestration easier.
