# C-045 VSS Restore Request and Fallback APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-036` and `C-040`

## Goal

Expose a constrained restore-oriented request surface for VSS-backed checkpoints so Atlas can later explain and trigger deeper recovery paths when inverse ops and quarantine are not enough.

## Boundaries

- No `src/Atlas.App/**`
- If another packet is touching `PipeContracts.cs` or `AtlasPipeServerWorker.cs`, serialize this one behind it
- Preserve fail-closed behavior for missing or degraded snapshot coverage

## Deliverables

- Add narrow contracts for VSS restore preview and VSS restore request
- Keep the surface checkpoint-driven and bounded
- Return truthful fallback posture when VSS cannot be used
- Add tests for missing snapshots, degraded coverage, and restore-preview behavior

## Notes From Codex

- The Undo workspace can now show checkpoint detail, but it still cannot move beyond explanation into deeper snapshot-backed recovery.
- Keep this conservative and recovery-first.
