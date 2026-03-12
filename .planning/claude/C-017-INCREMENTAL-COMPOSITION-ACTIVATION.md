# C-017 Incremental Composition Activation Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-016`

## Why This Is Next

`C-016` exposed provenance cleanly through the read-side inventory contracts, which means the app can now consume:

- `Trigger`
- `BuildMode`
- `DeltaSource`
- `BaselineSessionId`
- `IsTrusted`
- `CompositionNote`

That read path is useful, but the backend is still mostly reporting full rescans. Claude's own `C-016` outbox says the main deferred work is that orchestration still needs to actually emit incremental-composition sessions and meaningful trust degradation.

This packet closes that gap.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep the service-only mutation boundary intact
- Preserve safe full-rescan fallback
- Do not break the additive provenance fields shipped in `C-016`

## Read First

1. `src/Atlas.Core/Scanning/IDeltaSource.cs`
2. `src/Atlas.Core/Scanning/DeltaResult.cs`
3. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
4. `src/Atlas.Service/Services/FileScanner.cs`
5. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
6. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
7. `src/Atlas.Core/Contracts/PipeContracts.cs`
8. `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
9. `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
10. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas actually produce trustworthy incremental-composition sessions when the delta inputs are good enough, and make it emit explicit degraded provenance when it falls back to a full rescan or an untrusted result.

## Required Deliverables

### 1. Activate incremental composition in orchestration

Upgrade orchestration so it can actually choose:

- `BuildMode=IncrementalComposition`
- a populated `BaselineSessionId`
- a meaningful `DeltaSource`

when the delta payload is bounded and safe to compose.

### 2. Trust and degradation semantics

Make `IsTrusted` and `CompositionNote` real, not placeholder values.

At minimum:

- trusted incremental sessions when the composition is complete
- degraded note when Atlas falls back to full rescan
- degraded note when delta evidence is incomplete or unsafe
- `IsTrusted=false` when Atlas cannot honestly represent the result as fully trustworthy

### 3. Safe fallback behavior

Preserve existing safety rules:

- overflow still forces safe fallback
- invalid baseline still forces safe fallback
- missing or partial delta input must not masquerade as a clean incremental result

### 4. Persistence consistency

Ensure composed sessions still round-trip through the existing repository and read-side query APIs without contract changes in the app layer.

### 5. Tests

Add focused tests for:

- orchestration emits `IncrementalComposition` when a bounded delta is safe
- baseline session ID is populated on composed sessions
- trusted incremental session round-trips through inventory queries
- overflow or invalid baseline degrades to full rescan with clear note
- untrusted result persists `IsTrusted=false` and explanatory note

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact cases where Atlas now emits `IncrementalComposition`
- exact fallback cases and what note/trust state they produce
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas emits real incremental-composition sessions where safe
- `BaselineSessionId`, `DeltaSource`, `IsTrusted`, and `CompositionNote` are meaningfully populated
- fallback behavior stays conservative
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: orchestration activation and provenance tagging
- Subagent B: repository / persistence consistency
- Subagent C: tests for trusted and degraded composition behavior

## Notes From Codex

- The shell is already consuming the provenance read path, so this packet will immediately improve the product once it lands.
- Optimize for additive backend behavior, not a schema rewrite.
- Keep the current inventory query surface stable so Codex can keep shipping UI without reworking the app contract again.
