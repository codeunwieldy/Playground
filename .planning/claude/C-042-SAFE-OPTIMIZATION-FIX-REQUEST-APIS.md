# C-042 Safe Optimization Fix Request APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-034` and easier if `C-041` has already landed

## Why This Is Next

Atlas can analyze optimization pressure and the backend can apply safe fixes internally, but the app still has no first-class request path to preview or trigger those fixes outside a plan batch. The next backend gap is a constrained optimization-fix request surface.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- If `C-040` is in progress, do not claim this packet at the same time because both may need `PipeContracts.cs` and `AtlasPipeServerWorker.cs`
- Stay inside optimization execution and service request files
- Keep the surface tightly constrained to the safe optimization lane

## Read First

1. `.planning/claude/C-034-SAFE-OPTIMIZATION-FIX-APPLICATION-AND-ROLLBACK.md`
2. `.planning/claude/C-037-OPTIMIZATION-EXECUTION-HISTORY-AND-ROLLBACK-DETAILS.md`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Service/Services/PlanExecutionService.cs`
6. `src/Atlas.Service/Services/OptimizationScanner.cs`

## Goal

Expose a tightly scoped preview/apply request path for safe optimization findings so the app can later move from analysis-only into trusted fix execution.

## Required Deliverables

### 1. Additive contracts

Add narrow request/response contracts for:

- optimization fix preview
- optimization fix apply
- optionally optimization fix revert if it fits cleanly

Keep the payload bounded and identifier-driven.

### 2. Service handling

Implement handlers that:

- only accept curated safe optimization kinds
- preserve rollback-state persistence
- reject unsupported or recommendation-only findings cleanly

### 3. Conservative behavior

Handle truthfully:

- missing findings
- unsupported optimization kinds
- apply failures
- no-rollback-available states

### 4. Tests

Add focused tests for:

- safe kind acceptance
- unsupported kind rejection
- preview vs apply behavior
- rollback-state persistence after apply

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- contracts/routes added
- safety constraints enforced
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas exposes a constrained request surface for safe optimization fixes
- unsupported fixes are rejected clearly
- rollback truth is preserved
- no UI files were touched
- tests exist and pass

## Notes From Codex

- I want to evolve the Optimization workspace from analysis-only into trusted fix control without broadening the optimizer into unsafe tweak territory.
- Keep the route narrow and deterministic.
