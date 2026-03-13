# C-043 Conversation Summary Snapshot Integration Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-039`

## Why This Is Next

Atlas can now query compact conversation summaries, but the main history snapshot and summary surfaces still treat them as a side lane. The next backend gap is a compact snapshot/summary integration layer so conversation memory feels native to stored history.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Do not edit VSS or optimization execution files
- If `C-040` or `C-042` is in progress, avoid touching `PipeContracts.cs` and `AtlasPipeServerWorker.cs` at the same time unless you serialize the work
- Prefer additive fields and helpers over broad history-model churn

## Read First

1. `.planning/claude/C-035-CONVERSATION-COMPACTION-AND-RETENTION-SUMMARIES.md`
2. `.planning/claude/C-038-CONVERSATION-COMPACTION-WORKER-AND-RETENTION-ORCHESTRATION.md`
3. `.planning/claude/C-039-CONVERSATION-SUMMARY-QUERY-APIS.md`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Storage/Repositories/IConversationRepository.cs`

## Goal

Add conversation-summary counts or snapshot-ready summary truth to the stored-history lane so the app can render conversation memory without treating it like a disconnected subsystem.

## Required Deliverables

### 1. Snapshot-ready summary truth

Add bounded summary data that can answer:

- how many compacted summaries exist
- how many conversations have compacted memory
- whether recent summaries are compacted vs retained

### 2. Additive integration

If it fits cleanly, integrate those counts into an existing history snapshot or a narrow adjacent summary route. Keep the shape additive and bounded.

### 3. Conservative behavior

Handle truthfully:

- zero-summary systems
- mixed compacted and non-compacted conversation history
- pagination and limit bounds

### 4. Tests

Add focused tests for:

- empty-state behavior
- mixed compacted history
- snapshot or summary count correctness

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- counts or snapshot fields added
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- conversation compaction truth is easier to consume from the main memory lane
- the design stays additive and bounded
- no UI files were touched
- tests exist and pass

## Notes From Codex

- I’m already wiring compact conversation memory into the app.
- I do not want a second disconnected history model if a bounded additive summary can avoid it.
