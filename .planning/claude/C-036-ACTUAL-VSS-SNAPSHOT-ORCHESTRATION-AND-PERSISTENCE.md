# C-036 Actual VSS Snapshot Orchestration and Persistence Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-033`

## Why This Is Next

Atlas can now decide when a checkpoint is required or recommended, but it still does not actually create heavyweight snapshot coverage. The next backend gap is activating real VSS orchestration so required checkpoints stop being advisory-only.

This packet should turn the `C-033` eligibility foundation into truthful persisted snapshot coverage without touching `src/Atlas.App/**`.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Avoid `src/Atlas.Core/Contracts/PipeContracts.cs`
- Avoid conversation-compaction files
- Prefer execution, recovery, service, and repository files
- Do not build a fake snapshot system; either create a real seam or fail closed

## Read First

1. `.planning/claude/C-033-VSS-CHECKPOINT-ELIGIBILITY-AND-METADATA-FOUNDATIONS.md`
2. `.planning/claude/C-004-INSTALLER-RECOVERY.md`
3. `src/Atlas.Service/Services/CheckpointEligibilityEvaluator.cs`
4. `src/Atlas.Service/Services/PlanExecutionService.cs`
5. `src/Atlas.Core/Contracts/DomainModels.cs`
6. `src/Atlas.Storage/Repositories/*`

## Goal

Create real service-side VSS snapshot orchestration for eligible batches, and persist truthful snapshot references so later undo and audit flows can reason about heavyweight rollback coverage.

## Required Deliverables

### 1. Snapshot orchestration seam

Add a bounded orchestration layer that can:

- attempt VSS creation for batches marked `Required`
- optionally create for `Recommended` when a conservative rule says yes
- fail closed when snapshot creation is unavailable

### 2. Persistence truth

Populate the existing checkpoint fields truthfully:

- `VssSnapshotCreated`
- `VssSnapshotReferences`
- checkpoint notes explaining success, fallback, or failure

### 3. Conservative execution behavior

Handle truthfully:

- successful snapshot creation
- VSS unavailable on the target volume
- partial volume coverage
- snapshot creation failure
- dry-run behavior versus live execution

### 4. Tests

Add focused tests for:

- required checkpoint batches creating snapshot refs
- failures blocking live execution when coverage is required
- recommended checkpoints behaving conservatively
- metadata persistence on success and failure

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- snapshot orchestration seam added
- success/failure behavior
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- required checkpoint batches can create real snapshot coverage or fail closed
- snapshot references persist truthfully
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: VSS orchestration seam
- Subagent B: execution/persistence integration
- Subagent C: tests and failure-mode review

## Notes From Codex

- I am integrating checkpoint posture into the Undo and Plan Review UX now.
- The next clean backend step is real coverage, not more eligibility vocabulary.
