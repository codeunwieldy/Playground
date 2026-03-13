# C-040 VSS Checkpoint Detail Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-036`

## Why This Is Next

Atlas now creates real VSS snapshots when checkpoint policy requires them, but the app still only sees coarse checkpoint posture. The next backend gap is a bounded read path for checkpoint/VSS detail so Codex can show actual snapshot coverage instead of inference.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- This is the only packet in this wave that should touch `src/Atlas.Core/Contracts/PipeContracts.cs` or `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
- Avoid optimization execution history files
- Keep the route/read surface additive and bounded

## Read First

1. `.planning/claude/C-033-VSS-CHECKPOINT-ELIGIBILITY-AND-METADATA-FOUNDATIONS.md`
2. `.planning/claude/C-036-ACTUAL-VSS-SNAPSHOT-ORCHESTRATION-AND-PERSISTENCE.md`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`

## Goal

Expose bounded read-side checkpoint detail for VSS-backed recovery so the app can show actual snapshot coverage, eligibility, and restore depth.

## Required Deliverables

### 1. Additive contracts

Add narrow protobuf contracts for:

- listing richer checkpoint detail for recent checkpoints, or
- reading one checkpoint detail by checkpoint id

Include only bounded fields that already exist in domain truth, such as:

- checkpoint eligibility
- eligibility reason
- covered volumes
- whether VSS was actually created
- retained snapshot references
- optimization rollback-state count

### 2. Service routes

Implement read-only handlers that:

- return checkpoint detail without re-running execution
- handle missing checkpoints cleanly
- preserve older checkpoints that predate VSS orchestration

### 3. Conservative behavior

Handle truthfully:

- checkpoints with no VSS coverage
- checkpoints where VSS was required but not created
- older checkpoints that only have inverse ops and quarantine
- bounded snapshot-reference output

### 4. Tests

Add focused tests for:

- found vs missing checkpoint detail
- older checkpoint backward compatibility
- bounded VSS reference output
- optimization rollback-state exposure

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- contracts/routes added
- backward-compatibility behavior
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- the app can query truthful VSS/checkpoint detail through a bounded read route
- older checkpoints still deserialize cleanly
- no UI files were touched
- tests exist and pass

## Notes From Codex

- I want to feed the Undo workspace with actual VSS coverage, not guessed posture.
- Keep this additive and read-only.
