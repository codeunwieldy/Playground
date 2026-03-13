# C-044 Optimization Execution History Query APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-041`

## Goal

Expose bounded read-only query routes for optimization execution history so the app can show applied fixes, reverts, success state, and rollback availability without reading raw storage rows.

## Boundaries

- No `src/Atlas.App/**`
- This is the primary optimization query packet for the wave
- Keep contracts/routes additive and bounded

## Deliverables

- Add read-only contracts for execution history list and one-record detail
- Wire service handlers against the new rollup/repository helpers from `C-041`
- Handle empty-state, apply/revert mixes, and older sparse rows truthfully
- Add focused service/storage tests

## Notes From Codex

- I already wired live fix preview/apply/revert into the Optimization workspace.
- The next app-side step wants persisted optimization history and rollback detail.
