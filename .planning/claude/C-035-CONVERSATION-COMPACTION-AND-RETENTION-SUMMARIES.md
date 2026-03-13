# C-035 Conversation Compaction and Retention Summaries Packet

## Owner

Claude Code in VS Code

## Priority

Ready in parallel with `C-032`

## Why This Is Next

Atlas now stores more conversations, prompt traces, plans, and history than it did at the start of the project. The product vision explicitly calls for long-lived memory that stays useful without growing wastefully.

The next backend gap is compact retention:

- how older conversation history gets summarized
- how Atlas keeps searchability while shrinking raw footprint
- how retention stays truthful and auditable

This packet should deepen storage efficiency without touching `src/Atlas.App/**` and without colliding with plan-history or optimization execution work.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- Avoid `src/Atlas.Core/Contracts/PipeContracts.cs`
- Avoid `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` unless truly necessary
- Prefer storage/AI/repository/background-job files and tests
- Do not introduce mandatory cloud-model dependence for compaction

## Read First

1. `src/Atlas.Storage/Repositories/ConversationRepository.cs`
2. `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
3. `src/Atlas.AI/*`
4. `.planning/PROJECT.md`
5. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Add a deterministic, additive compaction path that can summarize older conversation history into smaller retained memory while preserving searchability and truthfulness.

## Required Deliverables

### 1. Summary storage model

Add additive storage support for conversation summaries or compact snapshots that can retain:

- thread / conversation identity
- covered time window or message range
- bounded summary text
- minimal searchable metadata

### 2. Compaction service

Add a deterministic compaction path that can:

- select older raw conversation segments
- generate bounded summaries without requiring live model calls
- mark what has been compacted
- preserve newer raw history untouched

### 3. Retention behavior

Handle truthfully:

- conversations too short to summarize
- already-compacted spans
- summary refresh when a span changes
- older raw data that must still remain for a configured window

### 4. Searchability

Keep enough metadata so Atlas can later answer read-side history queries without losing the value of older conversation memory.

### 5. Tests

Add focused tests for:

- compaction candidate selection
- deterministic summary generation
- repeated compaction idempotence
- retention-window behavior
- backward compatibility with existing conversation rows

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- summary model added
- compaction rules added
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can compact older conversation history into durable bounded summaries
- newer raw history remains intact
- the design stays additive and local-first
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: storage schema and repository evolution
- Subagent B: compaction selection and summary generation
- Subagent C: tests and retention/backward-compatibility review

## Notes From Codex

- I do not need new UI for this packet yet.
- Keep the design local-first and deterministic.
- This should reduce future storage pressure without creating a second memory system.
