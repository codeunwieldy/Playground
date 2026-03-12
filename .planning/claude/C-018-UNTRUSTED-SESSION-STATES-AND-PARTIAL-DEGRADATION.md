# C-018 Untrusted Session States and Partial Degradation Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-017`

## Why This Is Next

`C-017` successfully activated incremental composition, but Claude's outbox is still clear about one remaining honesty gap:

- `IsTrusted` stays `true`
- degraded or partial session states are not yet meaningfully represented
- `CompositionNote` explains fallback, but Atlas cannot yet distinguish a fully trusted retained session from a conservative degraded one

Codex is now surfacing scan trust as a first-class UI concern in the dashboard, plan review, and `Atlas Memory`. This packet makes that trust posture truthful instead of theoretical.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Preserve the service-only mutation boundary
- Keep conservative fallback behavior intact
- Do not break the additive provenance fields already shipped in `C-016`

## Read First

1. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
2. `src/Atlas.Service/Services/FileScanner.cs`
3. `src/Atlas.Core/Scanning/DeltaResult.cs`
4. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
5. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
6. `src/Atlas.Core/Contracts/PipeContracts.cs`
7. `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
8. `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
9. `.planning/claude/C-017-INCREMENTAL-COMPOSITION-ACTIVATION.md`
10. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas represent degraded retained inventory sessions honestly when orchestration cannot claim a fully trustworthy result, while keeping destructive planning and execution on the conservative side.

## Required Deliverables

### 1. Real untrusted session semantics

Introduce meaningful `IsTrusted=false` behavior for persisted sessions when Atlas retains a usable but degraded result.

Examples of acceptable triggers:

- bounded incremental composition where some changed paths could not be refreshed
- retained session composed from incomplete delta evidence
- degraded recovery path where Atlas intentionally preserves useful state but cannot honestly call it complete

If a scenario is too unsafe for a degraded retained session, keep the existing full-rescan fallback.

### 2. Conservative degradation model

Define exactly when Atlas:

- persists a trusted session
- persists an untrusted/degraded session
- refuses degraded persistence and falls back to a full rescan instead

The rule should favor correctness over cleverness.

### 3. Provenance and notes

Populate `CompositionNote` with precise, operator-readable explanations for degraded states.

At minimum, notes should tell Codex and the user:

- what degraded
- why Atlas still retained the session
- what follow-up would restore full trust

### 4. Read-path continuity

Ensure the existing inventory query APIs continue to round-trip:

- `BuildMode`
- `DeltaSource`
- `BaselineSessionId`
- `IsTrusted`
- `CompositionNote`

No UI contract rewrite should be required.

### 5. Tests

Add focused tests for:

- trusted incremental session remains trusted when composition is complete
- degraded retained session persists with `IsTrusted=false`
- degraded note round-trips through snapshot/session/session-detail APIs
- scenarios that are too unsafe still force full rescan instead of partial persistence
- baseline linkage and delta source remain truthful in degraded cases

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact cases that now emit `IsTrusted=false`
- exact cases that still force full rescan
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can now emit real degraded/untrusted retained sessions when safe
- fully trusted incremental sessions remain supported
- unsafe scenarios still fall back conservatively
- existing inventory query APIs keep working without UI-side contract churn
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: orchestration and degradation decision model
- Subagent B: persistence/query continuity for degraded provenance
- Subagent C: tests for trusted vs degraded vs forced-full-rescan cases

## Notes From Codex

- The shell is now explicitly surfacing trust posture, execution stance, and degradation notes.
- Optimize for truthful state, not optimistic state.
- Prefer additive backend behavior over schema churn.
