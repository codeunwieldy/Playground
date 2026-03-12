# C-021 Sensitivity Scoring and Evidence Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-020`

## Why This Is Next

`C-020` made Atlas better at understanding what a file *is* through bounded content sniffing, MIME detection, and header fingerprints. The next backend depth gap is Atlas still deciding how sensitive a file is almost entirely from path keywords and a few extensions.

That leaves a big part of the product vision underpowered:

- Atlas should recognize likely financial, legal, identity, credential, and recovery material more truthfully
- Atlas should be more conservative around files whose type or name signals user importance
- later plan generation, duplicate cleanup, and review UX need stronger sensitivity truth to build on

Codex is taking a separate AI/planning projection lane in parallel, so this packet should deepen scanner-side sensitivity intelligence without overlapping UI or prompt-shaping work.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Do not redesign planning prompts or app-side review UX in this packet
- Keep changes bounded and additive
- Preserve conservative behavior over optimistic cleanup

## Read First

1. `src/Atlas.Service/Services/FileScanner.cs`
2. `src/Atlas.Core/Contracts/DomainModels.cs`
3. `src/Atlas.Core/Scanning/ContentSniffer.cs`
4. `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
5. `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
6. `tests/Atlas.Service.Tests/ContentSniffingTests.cs`
7. `tests/Atlas.Service.Tests/FileScannerTests.cs`
8. `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
9. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas assign file sensitivity more truthfully by combining path, filename, extension, MIME/category, and bounded content-derived file type signals.

## Required Deliverables

### 1. Bounded sensitivity scorer

Extract or introduce a sensitivity scoring/classification seam that Atlas can reuse during:

- full scans
- single-file inspection during incremental composition

It should combine multiple bounded signals such as:

- protected keywords already in policy
- filename terms that imply identity, tax, finance, payroll, contracts, medical, credentials, keys, recovery, etc.
- high-risk extensions and MIME types
- content-sniffed file family where useful
- location/context signals where still appropriate

### 2. Conservative sensitivity rules

Improve classification beyond the current mostly path-based heuristics.

Target outcomes:

- credential/key material remains `Critical`
- likely identity / finance / legal / medical / recovery files become at least `High`
- generic media and routine downloads do not get over-promoted without evidence
- content/type truth from `C-020` can strengthen a decision when the extension/path is weak

### 3. Evidence-friendly structure

If possible without broad contract churn, keep the implementation structured so Atlas can later explain *why* a file was scored as sensitive.

That can mean:

- a small internal evidence model
- an enum/flag structure
- a classifier result type used by `FileScanner`

Do not broaden public contracts just to expose full evidence yet unless a truly tiny additive change is unavoidable.

### 4. Scanner integration

Make sure both:

- `InspectFile`
- full scan enumeration

use the same sensitivity path.

### 5. Tests

Add focused tests for at least:

- credential/key material being `Critical`
- finance/legal/identity-style documents becoming `High`
- MIME/type-aware cases where content truth strengthens the result
- low-risk media/plain files staying low
- full scan and single-file inspection sharing the same sensitivity logic

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact sensitivity signals used
- what still remains heuristic vs content-aware
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas no longer relies mostly on path keywords for sensitivity
- scanner sensitivity uses the richer file-understanding truth from `C-020`
- the implementation is reusable for both full scan and incremental inspection
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: sensitivity scoring model and evidence structure
- Subagent B: scanner integration and conservative rule tuning
- Subagent C: tests and regression coverage

## Notes From Codex

- I am improving the AI-side inventory projection in parallel so the planning layer can consume richer inventory truth.
- Please keep this packet focused on scanner-side sensitivity truth, not prompt shaping or shell review UX.
- Optimize for a bounded, explainable internal classifier that future duplicate and risk packets can build on.
