# C-025 Duplicate Group Detail and Evidence APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-024`

## Why This Is Next

Atlas can now do two useful things:

- inspect a single file and explain its MIME/category/sensitivity posture
- list retained duplicate groups with confidence, canonical rationale, and risk flags

But the duplicate-review lane is still summary-grade. The shell cannot yet ask:

- "why does Atlas believe this duplicate group is trustworthy?"
- "which evidence signals pushed cleanup confidence down?"
- "what are the bounded member facts behind this group?"

The underlying analyzer already has bounded duplicate evidence in `DuplicateGroupAnalyzer`, but that evidence is not durable or queryable through a dedicated read path yet. Codex is taking the shell/review integration lane in parallel, so this packet should expose duplicate-group drill-in without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Preserve conservative duplicate posture over optimistic cleanup
- Do not redesign the whole duplicate model if one additive detail path is enough
- Do not leak raw file contents or unbounded member payloads

## Read First

1. `src/Atlas.Core/Scanning/DuplicateGroupAnalyzer.cs`
2. `src/Atlas.Core/Scanning/DuplicateCanonicalSelector.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
6. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
7. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
8. `src/Atlas.Service/Services/FileScanner.cs`
9. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
10. `.planning/claude/C-022-DUPLICATE-EVIDENCE-AND-CONFIDENCE.md`
11. `.planning/claude/C-023-PERSISTED-DUPLICATE-REVIEW-AND-QUERY-APIS.md`
12. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose a bounded duplicate-group detail path so Codex can later show "why this group is risky / why this canonical wins" without rebuilding duplicate analysis in the app.

## Required Deliverables

### 1. Additive duplicate evidence persistence

Persist bounded duplicate evidence for retained groups, such as:

- analyzer evidence signals from `DuplicateGroupAnalyzer`
- enough bounded member posture to support drill-in

Keep the schema additive and query-friendly. Avoid storing unbounded blobs if a normalized table is cleaner.

### 2. Repository evolution

Evolve the inventory repository additively so Atlas can:

- save duplicate evidence alongside retained duplicate groups
- read one duplicate group by `session + group id`
- read bounded evidence rows and bounded member detail for that group

Do not break existing duplicate summary callers.

### 3. Read-only duplicate detail route

Add a bounded pipe route for duplicate group drill-in, such as:

- `inventory/duplicate-detail`

The response should be enough for the app to explain:

- canonical file
- cleanup confidence vs match confidence
- canonical reason
- max sensitivity
- risk flags
- bounded duplicate evidence signals
- bounded member summaries or member posture

Keep it read-only.

### 4. Conservative empty/failure behavior

Define clean outcomes for:

- unknown session
- unknown group id
- group exists but has no retained evidence rows

The app should get truthful `Found = false` or empty bounded detail rather than ambiguous partial success.

### 5. Tests

Add focused tests for:

- duplicate evidence persistence round-trip
- duplicate detail route returns canonical/risk/evidence/member detail
- missing session or group returns `Found = false`
- bounded result sizes are enforced

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- schema/repository additions
- new route/contracts
- exact duplicate evidence now queryable
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can drill into one retained duplicate group through a bounded read route
- duplicate evidence survives persistence
- canonical/risk/evidence/member posture is available without UI-side reinvention
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: additive storage schema and repository detail read path
- Subagent B: pipe contracts and service route wiring
- Subagent C: tests and bounded-payload review

## Notes From Codex

- I am wiring `C-024` file inspection/detail into the current shell now.
- Keep this packet focused on duplicate drill-in, not UI, not prompt shaping, and not optimizer work.
- Optimize for a detail response the existing Memory / Plan Review surfaces can consume later without another backend redesign.
