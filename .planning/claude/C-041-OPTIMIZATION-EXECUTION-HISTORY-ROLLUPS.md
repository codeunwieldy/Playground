# C-041 Optimization Execution History Rollups Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-037`

## Why This Is Next

Atlas now persists optimization execution records, but there is no summary layer on top of that storage yet. Before exposing new query routes, we need a bounded rollup/read model so the history stays compact and app-ready.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Do not edit `src/Atlas.Core/Contracts/PipeContracts.cs`
- Do not edit `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
- Stay inside storage/repository/service summarization files

## Read First

1. `.planning/claude/C-034-SAFE-OPTIMIZATION-FIX-APPLICATION-AND-ROLLBACK.md`
2. `.planning/claude/C-037-OPTIMIZATION-EXECUTION-HISTORY-AND-ROLLBACK-DETAILS.md`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Storage/Repositories/*.cs`
5. `src/Atlas.Service/Services/PlanExecutionService.cs`

## Goal

Create bounded rollup and repository read helpers for optimization execution history so later query routes can stay narrow and predictable.

## Required Deliverables

### 1. Repository additions

Add narrow read methods for optimization execution history, such as:

- recent execution list
- grouped or rolled-up execution summaries by optimization kind
- one execution detail by id if needed for later APIs

### 2. Summarization model

Introduce additive summary shapes that answer:

- what kind ran
- target
- apply vs revert posture
- created time
- whether rollback data exists
- whether the record was successful

### 3. Conservative behavior

Handle truthfully:

- empty history
- mixed apply and revert records
- older rows with sparse rollback data
- bounded list sizes

### 4. Tests

Add focused tests for:

- rollup behavior
- empty-state behavior
- apply/revert grouping
- ordering and bounds

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- repository methods or summary types added
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- optimization execution history has compact read helpers and rollups
- no pipe contracts or UI files were touched
- tests exist and pass

## Notes From Codex

- I want a future Optimization workspace to show trusted fix history and rollback posture without reading raw execution rows.
- Keep this packet app-ready but route-free.
