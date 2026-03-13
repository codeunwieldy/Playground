# C-034 Safe Optimization Fix Application and Rollback Packet

## Owner

Claude Code in VS Code

## Priority

Ready in parallel with `C-032`

## Why This Is Next

Atlas already has meaningful optimization scanning and a decent finding vocabulary, but the safe-fix side is still thinner than the scan side. The next backend gap is deterministic fix application and rollback truth for the optimization kinds Atlas already treats as low-risk.

This packet should deepen the optimization execution path without touching `src/Atlas.App/**` and without colliding with plan-history lineage work.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Avoid `src/Atlas.Core/Contracts/PipeContracts.cs` unless absolutely required
- Avoid plan-history repository/query work
- Prefer optimization/service/execution files and tests
- Reuse existing undo / recovery structures where possible instead of inventing a second rollback system

## Read First

1. `src/Atlas.Service/Services/OptimizationScanner.cs`
2. `src/Atlas.Service/Services/PlanExecutionService.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Storage/Repositories/*`
5. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas able to apply and revert the current safe optimization classes deterministically and conservatively, using the existing service-owned execution boundary.

## Required Deliverables

### 1. Safe optimization fix executor

Implement deterministic application support for the current safe kinds Atlas already understands, such as:

- `TemporaryFiles`
- `CacheCleanup`
- `DuplicateArchives`
- `UserStartupEntry`

If one of these kinds should remain recommendation-only after deeper inspection, keep it conservative and explain why.

### 2. Rollback truth

Make sure fix application retains enough rollback truth so Atlas can later:

- revert what it changed
- explain what was removed / disabled
- avoid treating non-reversible cleanup as reversible

Reuse existing undo/checkpoint vocabulary if it fits.

### 3. Bounded execution behavior

Handle truthfully:

- missing targets
- already-clean / already-disabled cases
- partially applied fix sets
- user-level versus machine-level boundaries
- safe failure without widening privilege surface

### 4. Conservative safety posture

Do not add support for:

- registry “tuning” hacks
- Microsoft services
- drivers
- unsupported startup sources
- anything that breaks the earlier product safety stance

### 5. Tests

Add focused tests for:

- successful safe fix application
- idempotent repeated application
- reversible fix behavior
- partial-failure handling
- unsupported or blocked target behavior

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- safe fix kinds implemented
- rollback semantics added
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can deterministically apply the current safe optimization fixes through the service layer
- rollback truth is retained honestly
- unsupported or risky classes stay blocked
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: fix executor implementation
- Subagent B: rollback / undo persistence integration
- Subagent C: tests and conservative safety review

## Notes From Codex

- I am keeping the optimization UI stable; this packet should make the backend execution story catch up.
- Stay inside the existing safety posture rather than expanding the optimizer aggressively.
- Backend-only and additive is the right shape here.
