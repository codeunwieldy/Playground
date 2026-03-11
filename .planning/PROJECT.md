# Atlas File Intelligence

## What This Is

Atlas is a Windows 11-first native desktop application for AI-guided file management, storage hygiene, and safe PC optimization. It scans user-created and downloaded content, produces structured reorganization plans, validates those plans locally, and only executes reversible actions through a privileged Windows service. The product is aimed at consumers and prosumers who want a cleaner, faster PC without handing an AI unrestricted control over the machine.

## Core Value

Atlas must improve organization and performance without ever violating user trust, losing important data, or touching protected system areas.

## Requirements

### Validated

(None yet - the current repository is an internal brownfield scaffold, not a shipped product)

### Active

- [ ] Safe AI-authored file reorganization with deterministic local policy enforcement
- [ ] Quarantine-first duplicate cleanup and batch-level undo
- [ ] Deep file understanding beyond filenames for categorization and sensitivity detection
- [ ] Voice and text command surfaces for user-directed organization requests
- [ ] Guarded PC optimization for safe startup, temp, cache, and background-task improvements
- [ ] Compact local storage for plans, conversations, prompt traces, and action history
- [ ] Parallel delivery flow for Codex and Claude Code in VS Code with explicit contracts, inbox/outbox notes, and handoff documents

### Out of Scope

- Enterprise fleet management or remote domain administration - the first release is local-machine and user-scoped
- Direct permanent deletion by the model - all destructive cleanup must route through quarantine and retention
- Mutation of protected Windows, program, credential, or system-owned locations - violates the trust boundary
- Aggressive registry "tuning," driver tweaks, kernel tweaks, or security-product interference - too risky for the product promise
- macOS, Linux, mobile, or browser-only versions - this milestone is Windows-native first

## Context

The repository already contains a brownfield foundation:
- `src/Atlas.Core` defines the plan DSL, policy profile, pipe envelopes, and rollback primitives.
- `src/Atlas.Service` hosts a worker-service scaffold, scanner, executor skeleton, optimizer scaffold, and named-pipe server.
- `src/Atlas.AI` contains an OpenAI Responses client and a heuristic fallback planner.
- `src/Atlas.App` contains a WinUI 3 shell scaffold with the first Atlas visual language pass.
- `src/Atlas.Storage` bootstraps a SQLite schema for plans, checkpoints, conversations, prompt traces, and quarantine metadata.
- `tests/Atlas.Core.Tests` covers the current safety and rollback foundation.

The planning state did not exist before this pass, so `.planning/` is being initialized after the scaffold was created. This project should be treated as a brownfield startup with architecture already seeded but most product behavior still ahead.

Parallel execution is now expected to happen through Codex plus Claude Code in VS Code rather than another Codex instance. Codex retains ownership of WinUI, motion, visual system, and all UX/UI implementation because the repository-local design guidance and UI skill coverage live here. Claude is being routed toward structural, planning, backend, testing, eval, and documentation-heavy work through explicit markdown packets.

## Constraints

- **Platform**: Windows 11 23H2+ native desktop experience - file-system operations, Windows service work, and optimization tasks are Windows-specific
- **UI Stack**: WinUI 3 on .NET 8 - aligns with a native Windows look, packaging options, and the existing scaffold
- **Service Boundary**: Only the privileged service may mutate files or apply optimizations - the UI and model remain requesters, not executors
- **AI Contract**: The model may emit structured plans only - no free-form shell access or direct machine control
- **Deletion Safety**: "Delete" means quarantine-first with retention and restore paths - permanent purge is a later retention operation
- **Storage Budget**: History, plans, and conversations must stay compact - prefer compression, summarization, and retention windows
- **Parallel Collaboration**: Codex and Claude Code must be able to work in parallel - every major scope decision needs shared docs, explicit packets, and handoff hygiene
- **Privacy**: Sensitive content upload is opt-in and auditable - local preprocessing and redaction gates come before cloud reasoning

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| WinUI 3 desktop shell plus .NET worker service | Clean separation between native UX and privileged execution | - Pending |
| Unpackaged, x64-focused app workflow during development | Fits local CLI iteration and current scaffold | - Pending |
| MSI/WiX installer path instead of single-project MSIX | Product needs multiple executables and service installation | - Pending |
| Responses API as the primary planning interface | Best fit for structured plan generation, JSON outputs, and tool-style orchestration | - Pending |
| Realtime transcription for push-to-talk input | Low-latency transcript flow fits the voice command surface | - Pending |
| Strict operation DSL with local policy validation | Keeps AI inside a proposal-only boundary | - Pending |
| Quarantine-first deletion with retention windows | Preserves trust and enables low-cost restore | - Pending |
| VSS reserved for high-impact recovery checkpoints | Stronger rollback for destructive batches without overusing storage | - Pending |
| Sync-managed folders excluded by default | Reduces accidental cloud-desync and user surprise | - Pending |
| Optimization scope limited to curated safe actions | Avoids the unsafe reputation of generic "PC optimizer" software | - Pending |
| Codex owns UI/UX and Claude owns structural/planning support | Keeps the stronger design/tooling context with the UI implementation owner | - Pending |

---
*Last updated: 2026-03-11 after brownfield planning initialization*
