# C-020 Content Sniffing and MIME Detection Packet

## Owner

Claude Code in VS Code

## Priority

Ready after `C-019`

## Why This Is Next

Atlas now has real retained-inventory trust, drift, provenance, and trust-aware plan gating. The next backend depth gap is that file understanding is still too shallow:

- `FileScanner` still classifies category almost entirely from extension
- `MimeType` currently falls back to the extension string
- `ContentFingerprint` is mostly unused
- duplicate and sensitivity intelligence still lack richer file-type evidence

Codex is taking a separate shared-core duplicate-intelligence lane, so this packet should push the scanner and file-understanding pipeline forward without overlapping that work.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, XAML, navigation, or visual design
- Do not redesign duplicate canonical ranking in this packet unless a tiny scanner-side seam is required
- Keep changes additive and bounded
- Preserve conservative fallback behavior over optimistic detection

## Read First

1. `src/Atlas.Service/Services/FileScanner.cs`
2. `src/Atlas.Core/Contracts/DomainModels.cs`
3. `src/Atlas.Core/Contracts/PipeContracts.cs`
4. `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
5. `src/Atlas.Storage/Repositories/InventoryRepository.cs`
6. `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
7. `tests/Atlas.Service.Tests/`
8. `.planning/claude/CLAUDE-OUTBOX.md`

## Goal

Make Atlas understand files by lightweight content evidence instead of extension alone, while keeping scanning bounded and safe.

## Required Deliverables

### 1. Bounded file-signature sniffing

Add a small, safe content-sniffing layer for common file families Atlas already cares about most:

- PDF
- ZIP container / Office document containers where practical
- PNG / JPEG / GIF / WebP
- MP3 / WAV
- MP4 / MOV if a bounded signature approach is practical

Requirements:

- header-only or otherwise tightly bounded reads
- fail closed to the current extension-based fallback
- do not introduce heavy parsing or full-file reads for classification

### 2. Truthful MIME and category population

Upgrade scan-time population so Atlas can emit a better `MimeType` and, when appropriate, a better `Category` based on sniffed content plus extension fallback.

Keep behavior conservative:

- if content and extension disagree, prefer the more trustworthy bounded content signal
- if content cannot be determined safely, preserve extension fallback

### 3. Content fingerprint population

Use the existing `ContentFingerprint` field in a bounded, truthful way when it adds value.

Acceptable directions:

- a lightweight header fingerprint for scanned files
- a stable content-derived fingerprint for supported formats

Do not turn normal scan classification into a full-file hashing pass for every file.

### 4. Scanner integration

Wire the new detection into:

- full scans
- single-file inspection used by incremental composition

Make sure the same file-understanding path is used in both places where practical.

### 5. Tests

Add focused tests for:

- common signature detection success cases
- extension fallback when sniffing is inconclusive
- content/extension mismatch behaving conservatively
- single-file inspection and full scan both using the same understanding path
- `ContentFingerprint` population rules staying bounded and stable

### 6. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- task status
- files read
- files changed
- exact file families supported
- fallback behavior when sniffing fails
- how `ContentFingerprint` is now populated
- tests added and whether they pass

## Success Criteria

This packet is successful when:

- Atlas no longer relies on extension-only typing for the most common important file families
- `MimeType` becomes meaningfully better than the current extension echo
- the work is reused by both full scan and incremental inspection
- the scanner remains bounded and safe
- no UI files were touched
- tests exist and pass

## Suggested Claude Subagent Split

- Subagent A: signature detection and MIME/category mapping
- Subagent B: scanner/incremental inspection integration
- Subagent C: tests and bounded `ContentFingerprint` behavior

## Notes From Codex

- I am moving duplicate canonical selection into shared core code in parallel.
- Please keep this packet focused on file understanding and scanner truthfulness, not duplicate plan UX or ranking semantics.
- Optimize for an additive seam that future sensitivity and duplicate packets can build on.
