# C-022 Duplicate Evidence and Confidence Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-021`

## Why This Is Next

Atlas now has stronger file truth:

- `C-020` added bounded content sniffing, MIME/category truth, and header fingerprints
- `C-021` added stronger sensitivity scoring with evidence-friendly internal results
- Codex has already moved duplicate canonical selection and safe duplicate cleanup planning into reusable core code

The next backend gap is duplicate *analysis* itself is still too shallow:

- duplicate confidence is still effectively a fixed value
- `DuplicateGroup` carries almost no explanation for why Atlas chose the canonical file
- risky duplicate groups with sensitive, sync-managed, or protected members are not expressed clearly enough for later review surfaces

Codex is taking optimization-system work in parallel, so this packet should deepen duplicate intelligence without overlapping UI or optimization code.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Do not redesign the optimization subsystem in this packet
- Keep changes additive and bounded
- Preserve conservative duplicate cleanup behavior over optimistic cleanup

## Read First

1. `src/Atlas.Service/Services/FileScanner.cs`
2. `src/Atlas.Core/Contracts/DomainModels.cs`
3. `src/Atlas.Core/Scanning/DuplicateCanonicalSelector.cs`
4. `src/Atlas.Core/Scanning/ContentSniffer.cs`
5. `src/Atlas.Core/Scanning/SensitivityScorer.cs`
6. `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
7. `tests/Atlas.Core.Tests/DuplicateCanonicalSelectorTests.cs`
8. `tests/Atlas.Core.Tests/SensitivityScorerTests.cs`
9. `tests/Atlas.Service.Tests/FileScannerTests.cs`
10. `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
11. `.planning/claude/C-021-SENSITIVITY-SCORING-AND-EVIDENCE.md`
12. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas duplicate analysis more truthful and explainable by using richer evidence and confidence scoring, not just fixed duplicate confidence.

## Required Deliverables

### 1. Duplicate analysis seam

Introduce a reusable duplicate-analysis helper or model that can evaluate a duplicate group using bounded existing signals such as:

- quick hash / full hash outcome
- file size agreement
- content fingerprint availability and agreement where useful
- sensitivity distribution
- sync-managed membership
- protected membership
- canonical-selection rationale

### 2. Better confidence scoring

Replace or refine the current hardcoded duplicate confidence behavior so Atlas can distinguish, for example:

- strong exact duplicates confirmed by full hash
- less certain candidates where evidence is weaker or partial
- risky duplicate groups where cleanup should stay conservative even if the content match is strong

Keep the confidence model bounded and deterministic.

### 3. Evidence-friendly result

If possible with narrow additive changes, let Atlas carry forward a little more duplicate explanation, such as:

- why the canonical file was chosen
- whether the group contains sensitive members
- whether the group contains sync-managed or protected members

Prefer additive contract evolution only if it is genuinely small and useful.

### 4. Scanner integration

Use the new duplicate analysis in `FileScanner` so scan responses benefit immediately.

### 5. Tests

Add focused tests for:

- exact-duplicate confidence behavior
- canonical-selection explanation
- sensitive-member and sync/protected-member risk marking
- low-risk vs higher-risk duplicate groups
- conservative behavior when evidence is incomplete

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact duplicate confidence logic used
- what explanation/risk data is now available
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- duplicate confidence is no longer a fixed placeholder
- Atlas can explain duplicate risk/canonical choice more truthfully
- duplicate analysis is reusable and bounded
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: duplicate evidence/confidence model
- Subagent B: scanner integration and additive contract changes if needed
- Subagent C: tests and regression coverage

## Notes From Codex

- I am moving the optimization subsystem forward in parallel.
- Please keep this packet focused on duplicate intelligence, not optimization, not prompt shaping, and not UI review surfaces.
- Optimize for additive evidence that later plan review and shell work can consume cleanly.
