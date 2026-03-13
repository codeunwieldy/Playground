# C-047 Conversation Summary Filters and Drill-In Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-039` and `C-043`

## Goal

Deepen the conversation-memory lane with bounded filter and drill-in support so Atlas can later move beyond top summaries into conversation-specific memory windows.

## Boundaries

- No `src/Atlas.App/**`
- If another packet is touching `PipeContracts.cs` or `AtlasPipeServerWorker.cs`, serialize accordingly
- Keep the route additive and bounded

## Deliverables

- Add bounded filter support for conversation summary queries
- Support drill-in by conversation id and possibly compaction posture
- Preserve clean behavior for zero-summary and mixed-history cases
- Add focused tests for filters, bounds, and empty-state truth

## Notes From Codex

- I already surfaced compact conversation memory in the Memory workspace.
- The next app-side step wants drill-in, not raw conversation dumping.
