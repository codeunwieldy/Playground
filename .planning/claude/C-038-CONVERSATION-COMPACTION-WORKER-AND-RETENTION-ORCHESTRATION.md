# C-038 Conversation Compaction Worker and Retention Orchestration Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-035`

## Why This Is Next

Atlas can compact conversations deterministically now, but the feature is still just a service class. The next backend gap is orchestration: when compaction runs, how retention windows are enforced, and how the service keeps storage under control without manual intervention.

This packet should make conversation compaction operational without touching `src/Atlas.App/**`.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Avoid `src/Atlas.Core/Contracts/PipeContracts.cs`
- Avoid plan-history and VSS files
- Prefer service startup/options/background-worker files plus conversation repository files
- Do not require UI interaction for compaction to function

## Read First

1. `.planning/claude/C-035-CONVERSATION-COMPACTION-AND-RETENTION-SUMMARIES.md`
2. `src/Atlas.Service/Program.cs`
3. `src/Atlas.Service/Services/AtlasServiceOptions.cs`
4. `src/Atlas.Service/Services/ConversationCompactionService.cs`
5. `src/Atlas.Storage/Repositories/ConversationRepository.cs`

## Goal

Operationalize deterministic conversation compaction through a bounded background workflow and explicit service options.

## Required Deliverables

### 1. Service options

Add additive service options for:

- enabling/disabling compaction
- compaction cadence
- retention window
- minimum age / message count thresholds if needed

### 2. Background worker

Add a bounded worker that:

- periodically looks for compactable candidates
- runs deterministic compaction
- respects retention windows and cooldowns
- degrades safely on repository or compaction errors

### 3. Conservative behavior

Handle truthfully:

- no candidates
- repeated runs with nothing new to compact
- large candidate sets needing bounded work per cycle
- repository failures

### 4. Tests

Add focused tests for:

- worker cadence / candidate processing
- no-op cycles
- bounded processing limits
- disabled worker behavior
- error resilience

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- options added
- worker behavior
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- conversation compaction can run operationally in the service
- the cadence and retention behavior are configurable
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: options and startup wiring
- Subagent B: background worker implementation
- Subagent C: tests and resilience review

## Notes From Codex

- I do not need UI changes for compaction orchestration yet.
- Keep this local-first and bounded.
