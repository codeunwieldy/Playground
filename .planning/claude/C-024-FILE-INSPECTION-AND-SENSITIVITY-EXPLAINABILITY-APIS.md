# C-024 File Inspection and Sensitivity Explainability APIs Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-023`

## Why This Is Next

Atlas now has much stronger scanner truth:

- `C-020` added bounded content sniffing, MIME truth, and content fingerprints
- `C-021` added reusable sensitivity scoring plus bounded evidence signals
- `C-022` and `C-023` made duplicate review more truthful and durable

But the shell still cannot ask the service a simple question like:

- "why did Atlas classify this file as sensitive?"
- "what MIME/category truth did Atlas derive for this file?"
- "what did a fresh bounded inspection of this path find right now?"

The evidence exists internally, but it is not yet available through a bounded service contract. Codex is taking the app-side memory/review lane in parallel, so this packet should expose file-understanding and sensitivity explainability without touching `src/Atlas.App/**`.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Keep changes additive and bounded
- Prefer read-only inspection contracts over broad redesign
- Do not leak raw file contents into the pipe contract
- Preserve conservative behavior when inspection fails or access is denied

## Read First

1. `src/Atlas.Core/Scanning/SensitivityScorer.cs`
2. `src/Atlas.Service/Services/FileScanner.cs`
3. `src/Atlas.Core/Contracts/DomainModels.cs`
4. `src/Atlas.Core/Contracts/PipeContracts.cs`
5. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
6. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
7. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
8. `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
9. `.planning/claude/C-021-SENSITIVITY-SCORING-AND-EVIDENCE.md`
10. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Expose bounded file inspection and sensitivity explainability through the service so Codex can later show trustworthy "why this file matters" detail without direct database access or app-side reinvention.

## Required Deliverables

### 1. Additive explainability contracts

Add narrow protobuf contracts for bounded file inspection / explainability. The contract should be able to answer at least:

- whether the file was found and inspectable
- normalized file identity (`path`, `name`, `extension`)
- category and MIME truth
- sensitivity level
- content-fingerprint presence, but not the raw fingerprint value unless you can justify it safely
- sync-managed / protected posture
- bounded sensitivity evidence signals

Keep the route read-only.

### 2. Service-side live inspection route

Use the existing scanner path so the service can inspect a specific file on demand through a bounded route such as:

- `inventory/inspect-file`

Reuse existing scanner and policy logic. Do not create a second classification implementation.

### 3. Conservative explainability behavior

Define clean outcomes for:

- missing file
- protected or excluded path
- access failure
- unsupported or low-signal files

The app should receive truthful posture instead of ambiguous success.

### 4. Optional persisted-session file detail if additive and cheap

If it fits cleanly, add one bounded read route for a stored session file row by session + path. This is optional. Do it only if it stays additive and does not slow the packet down.

### 5. Tests

Add focused tests for:

- inspectable file returns MIME/category/sensitivity data
- sensitivity evidence is bounded and deterministic
- protected/excluded paths fail closed
- missing file returns `Found = false`
- route result size stays bounded and payload-safe

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- new routes and contracts
- exact explainability fields now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas can inspect one file through the service and explain its sensitivity posture
- the new route is read-only and bounded
- scanner truth is reused instead of duplicated
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: additive pipe contracts and handler wiring
- Subagent B: scanner explainability shaping and failure semantics
- Subagent C: regression tests and payload-boundary review

## Notes From Codex

- I am wiring persisted duplicate-review memory into the existing shell right now.
- Keep this packet focused on explainability and bounded inspection, not UI and not prompt shaping.
- Optimize for a service response that the existing Memory / Plan Review surfaces can consume later without another backend redesign.
