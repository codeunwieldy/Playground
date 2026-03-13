# C-039 Conversation Summary Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-035`

## Why This Is Next

Atlas can now retain compact conversation summaries, but the app still has no clean read path for them. The next backend gap is bounded query support so Codex can surface compacted conversation memory without inferring it from raw conversation rows.

This packet should expose conversation-summary read APIs without touching `src/Atlas.App/**`.

## Parallel-Safety Boundaries

- Do not edit `src/Atlas.App/**`
- This is the only packet in the current wave that should touch `PipeContracts.cs` or `AtlasPipeServerWorker.cs`
- Avoid VSS and optimization execution files
- Keep the route/read surface additive and bounded

## Read First

1. `.planning/claude/C-035-CONVERSATION-COMPACTION-AND-RETENTION-SUMMARIES.md`
2. `src/Atlas.Storage/Repositories/IConversationRepository.cs`
3. `src/Atlas.Storage/Repositories/ConversationRepository.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`

## Goal

Expose bounded read-side APIs for compacted conversation summaries so the app can later show compact memory lanes without loading full raw conversations.

## Required Deliverables

### 1. Additive contracts

Add narrow protobuf contracts for:

- listing conversation summaries
- reading summaries for one conversation
- optionally surfacing compaction status counts if that fits existing history surfaces cleanly

### 2. Service routes

Implement read-only service handlers that:

- return bounded summary rows
- handle empty-state cleanly
- preserve older raw conversations that have no summaries yet

### 3. Conservative behavior

Handle truthfully:

- conversations with no summaries
- mixed raw and compacted history
- pagination / limit bounds
- missing conversation ids

### 4. Tests

Add focused tests for:

- summary list behavior
- one-conversation summary retrieval
- empty-state behavior
- older conversations with no summaries
- bounds / limit handling

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- contracts/routes added
- empty-state and backward-compatibility behavior
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- compacted conversation summaries are queryable through bounded read routes
- older uncompacted conversations still work cleanly
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: repository read-shape review
- Subagent B: pipe contracts and route wiring
- Subagent C: tests and backward-compatibility review

## Notes From Codex

- I want to surface conversation memory in the app later without inventing a second memory model.
- Keep this route additive, read-only, and bounded.
