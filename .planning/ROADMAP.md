# Roadmap: Atlas File Intelligence

## Overview

Atlas already has a brownfield scaffold in place, but the product itself is still pre-release. This roadmap turns the current foundation into a full Windows-native system that can scan and understand user files, ask AI for guarded reorganization plans, preview and explain those plans, execute only validated actions, recover from mistakes, and improve safe areas of overall PC performance.

Parallel delivery is now split between Codex and Claude Code in VS Code. Codex owns all UI/UX, WinUI, visual system, animation, and interaction implementation. Claude should be pointed at structural, planning, backend, testing, eval, and documentation-heavy work through explicit markdown packets.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions if new safety or platform work appears

- [ ] **Phase 1: Safety Kernel and Runtime Hardening** - Lock the trust boundary, policy defaults, app/service runtime assumptions, and protected-path enforcement
- [ ] **Phase 2: Inventory Graph and Delta Scanning** - Build reliable whole-machine inventory and incremental change tracking
- [ ] **Phase 3: Deep File Understanding** - Improve file-type understanding beyond names and extensions
- [ ] **Phase 4: Sensitivity and Duplicate Intelligence** - Detect sensitive content and safe duplicates with deterministic ranking
- [ ] **Phase 5: Plan DSL and AI Risk Orchestration** - Harden the planning contract, AI prompts, validation, and review logic
- [ ] **Phase 6: Review Canvas and Premium WinUI Shell** - Build the main user-facing experience for plan preview, explanation, and policy control
- [ ] **Phase 7: Executor, Quarantine, Undo, and Persistence** - Turn validated plans into durable, reversible machine actions
- [ ] **Phase 8: Recovery Checkpoints and VSS Orchestration** - Add heavy-duty rollback support for high-impact batches
- [ ] **Phase 9: Optimization Scanner and Fix Engine** - Expand guarded PC optimization beyond file organization
- [ ] **Phase 10: Voice and Command Center** - Add push-to-talk, transcript parsing, and unified command intake
- [ ] **Phase 11: Audit, Storage Lifecycle, and Parallel Delivery Ops** - Strengthen history, retention, searchable memory, and Codex-plus-Claude collaboration
- [ ] **Phase 12: Installer, Scale, Red-Team, and Beta Readiness** - Ship deployment, performance, and safety hardening for real users

## Phase Details

### Phase 1: Safety Kernel and Runtime Hardening
**Goal:** Lock the non-negotiable safety boundary before adding more power to the system
**Depends on:** Nothing (brownfield scaffold already exists)
**Requirements**: [SAFE-01, SAFE-02, SAFE-04]
**Success Criteria** (what must be TRUE):
  1. Protected and mutable path rules reflect the actual Atlas trust boundary
  2. The app and service packaging/runtime assumptions are documented and testable
  3. Brownfield foundation gaps are clearly separated from accepted v1 scope
**Plans:** 3 plans

Plans:
- [ ] 01-01: Audit and harden policy-profile defaults, path normalization, and protected-root handling
- [ ] 01-02: Lock runtime and deployment assumptions for WinUI, service hosting, x64 targeting, and installer direction
- [ ] 01-03: Expand core safety tests and seed red-team fixtures for blocked-path and sync-folder scenarios

### Phase 2: Inventory Graph and Delta Scanning
**Goal:** Build a trustworthy machine inventory that can scale beyond one-off full scans
**Depends on:** Phase 1
**Requirements**: [SCAN-01, SCAN-02]
**Success Criteria** (what must be TRUE):
  1. Atlas records all mounted drives and user-approved scan roots consistently
  2. NTFS volumes can be rescanned incrementally instead of only through full traversal
  3. Inventory results can be persisted and refreshed without losing source metadata
**Plans:** 3 plans

Plans:
- [ ] 02-01: Introduce persisted inventory tables and repository contracts for volumes, roots, and file snapshots
- [ ] 02-02: Add NTFS USN journal scanning with fallback watchers and scheduled rescans
- [ ] 02-03: Build inventory refresh orchestration, progress reporting, and bounded-memory scan batching

### Phase 3: Deep File Understanding
**Goal:** Understand what files are by content and metadata, not only by names
**Depends on:** Phase 2
**Requirements**: [SCAN-03]
**Success Criteria** (what must be TRUE):
  1. File-type detection combines extension, signature, and metadata rather than extension alone
  2. Atlas can extract classification hints from common document, media, and archive types
  3. Unknown files degrade gracefully into conservative categories instead of unsafe guesses
**Plans:** 3 plans

Plans:
- [ ] 03-01: Add content sniffing, MIME/signature detection, and parser abstraction points
- [ ] 03-02: Add media, archive, and office-document metadata extraction
- [ ] 03-03: Define category taxonomy and confidence scoring for mixed-signal file classification

### Phase 4: Sensitivity and Duplicate Intelligence
**Goal:** Distinguish protected/sensitive files from safe cleanup candidates with deterministic heuristics
**Depends on:** Phase 3
**Requirements**: [SCAN-04, SCAN-05]
**Success Criteria** (what must be TRUE):
  1. Atlas can explain why a file is marked sensitive or protected
  2. Atlas can identify exact duplicates with deterministic canonical selection
  3. Duplicate cleanup remains conservative when signals conflict or sensitivity is elevated
**Plans:** 3 plans

Plans:
- [ ] 04-01: Build sensitivity scoring from location, keywords, ACLs, and extracted content hints
- [ ] 04-02: Implement duplicate grouping, canonical ranking, and safe-delete thresholds
- [ ] 04-03: Add explainability objects that connect classification evidence to user-visible reasons

### Phase 5: Plan DSL and AI Risk Orchestration
**Goal:** Turn file intelligence into guarded AI planning with deterministic review gates
**Depends on:** Phase 4
**Requirements**: [SAFE-03, PLAN-01, PLAN-03, PLAN-05]
**Success Criteria** (what must be TRUE):
  1. The planner emits only valid Atlas DSL operations
  2. Every plan is risk-scored, policy-checked, and review-gated before execution
  3. High-risk batches can receive a second independent AI or rules-based risk pass
**Plans:** 4 plans

Plans:
- [ ] 05-01: Refine plan schema, operation semantics, and validation error reporting
- [ ] 05-02: Replace loose planner parsing with strict structured-output handling and trace capture
- [ ] 05-03: Add independent risk-review stage and destructive-batch escalation rules
- [ ] 05-04: Build prompt packs and eval fixtures for organization, sensitivity, duplicates, and unsafe requests

### Phase 6: Review Canvas and Premium WinUI Shell
**Goal:** Deliver the premium Windows UX for planning, inspection, explanation, and approval
**Depends on:** Phase 5
**Requirements**: [PLAN-02, PLAN-04, UX-01, UX-02, UX-03, UX-05]
**Success Criteria** (what must be TRUE):
  1. Users can review a plan visually before running it
  2. The shell feels native, premium, animated, and accessible
  3. Policy and explanation surfaces make safety decisions legible instead of opaque
**Plans:** 4 plans

Plans:
- [ ] 06-01: Build the main navigation shell, adaptive layout, and premium visual system
- [ ] 06-02: Implement plan review canvas with before/after tree diff, risk badges, and rationale panels
- [ ] 06-03: Build settings and policy studio for roots, exclusions, retention, and sensitivity controls
- [ ] 06-04: Add loading states, staged motion, reduced-motion handling, and keyboard-first accessibility polish

### Phase 7: Executor, Quarantine, Undo, and Persistence
**Goal:** Safely execute validated plans and recover from them using durable local state
**Depends on:** Phase 6
**Requirements**: [SAFE-05, EXEC-01, EXEC-02, EXEC-03, EXEC-04, DATA-01]
**Success Criteria** (what must be TRUE):
  1. Validated plan batches can be executed deterministically by the service
  2. Deletes route to quarantine with file-level restore support
  3. Batch-level undo works from persisted checkpoints and journals
**Plans:** 4 plans

Plans:
- [ ] 07-01: Add repositories for plans, batches, checkpoints, quarantine items, and conversations
- [ ] 07-02: Harden executor ordering, error handling, and partial-failure behavior
- [ ] 07-03: Implement quarantine lifecycle and single-item restore flows
- [ ] 07-04: Implement persisted undo timeline and batch rollback orchestration

### Phase 8: Recovery Checkpoints and VSS Orchestration
**Goal:** Add heavyweight rollback support for destructive batches and recovery edge cases
**Depends on:** Phase 7
**Requirements**: [EXEC-05]
**Success Criteria** (what must be TRUE):
  1. Atlas can detect when a batch qualifies for a pre-execution recovery checkpoint
  2. NTFS checkpoint behavior is explicit, auditable, and bounded
  3. Recovery flows degrade safely when the environment cannot support VSS
**Plans:** 3 plans

Plans:
- [ ] 08-01: Design VSS checkpoint eligibility rules, capability detection, and storage guardrails
- [ ] 08-02: Implement VSS orchestration and snapshot reference persistence for supported cases
- [ ] 08-03: Build recovery fallback logic and UI messaging for unsupported or failed checkpoint scenarios

### Phase 9: Optimization Scanner and Fix Engine
**Goal:** Expand Atlas into a guarded PC optimization product without becoming unsafe optimizerware
**Depends on:** Phase 7
**Requirements**: [OPTI-01, OPTI-02, OPTI-03, OPTI-04, OPTI-05]
**Success Criteria** (what must be TRUE):
  1. Atlas can surface safe optimization opportunities with evidence
  2. Only curated low-risk fixes can auto-run
  3. Riskier system-level tweaks stay in recommendation-only mode
**Plans:** 3 plans

Plans:
- [ ] 09-01: Expand scanners for startup, scheduled tasks, cache bloat, disk pressure, and background activity
- [ ] 09-02: Implement reversible fix modules only for approved optimization classes
- [ ] 09-03: Build optimization review UX with evidence, expected impact, and rollback guidance

### Phase 10: Voice and Command Center
**Goal:** Let users talk to Atlas or type commands through one guarded intent pipeline
**Depends on:** Phase 6
**Requirements**: [VOICE-01, VOICE-02, VOICE-03]
**Success Criteria** (what must be TRUE):
  1. Push-to-talk transcript capture works reliably
  2. Voice and text requests flow through the same intent, planning, and confirmation logic
  3. Misheard destructive commands cannot skip confirmation
**Plans:** 3 plans

Plans:
- [ ] 10-01: Implement push-to-talk capture, transcript streaming, and microphone/session state
- [ ] 10-02: Add voice-intent parsing and confirmation UX shared with typed commands
- [ ] 10-03: Add safety tests for transcription ambiguity, destructive phrasing, and confirmation loops

### Phase 11: Audit, Storage Lifecycle, and Parallel Delivery Ops
**Goal:** Make Atlas explainable over time and easy to advance in parallel across Codex and Claude Code
**Depends on:** Phase 7
**Requirements**: [SAFE-06, UX-04, DATA-02, DATA-03, OPS-01]
**Success Criteria** (what must be TRUE):
  1. Conversations, plans, and actions are searchable and retention-managed
  2. Secrets and sensitive local settings are protected
  3. Codex and Claude Code can work in parallel without drifting contracts
**Plans:** 3 plans

Plans:
- [ ] 11-01: Add compressed artifact storage, summarization, retention jobs, and search indexes
- [ ] 11-02: Protect secrets/settings and formalize trace, approval, and audit models
- [ ] 11-03: Build handoff protocols, Claude packets, branch ownership rules, and eval ownership for Codex-plus-Claude collaboration

### Phase 12: Installer, Scale, Red-Team, and Beta Readiness
**Goal:** Turn the product into something that can be installed, evaluated, and stress-tested safely
**Depends on:** Phases 8, 9, 10, and 11
**Requirements**: [DATA-04, OPS-02, OPS-03, OPS-04]
**Success Criteria** (what must be TRUE):
  1. Atlas can be installed and upgraded with app/service/runtime dependencies accounted for
  2. Large personal file sets do not blow up memory or leave the system in a fragile state
  3. Red-team, rollback, performance, and UX acceptance criteria are all exercised before beta
**Plans:** 4 plans

Plans:
- [ ] 12-01: Complete WiX/MSI packaging, service registration, prerequisite checks, and repair flows
- [ ] 12-02: Run scale tests for large inventories, rescans, and rollback storage growth
- [ ] 12-03: Build red-team suites for prompt injection, unsafe plan requests, sync-folder hazards, and recovery failures
- [ ] 12-04: Execute beta readiness review, launch verification, and release documentation

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10 -> 11 -> 12

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Safety Kernel and Runtime Hardening | 0/3 | In progress | - |
| 2. Inventory Graph and Delta Scanning | 0/3 | Not started | - |
| 3. Deep File Understanding | 0/3 | Not started | - |
| 4. Sensitivity and Duplicate Intelligence | 0/3 | Not started | - |
| 5. Plan DSL and AI Risk Orchestration | 0/4 | Not started | - |
| 6. Review Canvas and Premium WinUI Shell | 0/4 | Not started | - |
| 7. Executor, Quarantine, Undo, and Persistence | 0/4 | Not started | - |
| 8. Recovery Checkpoints and VSS Orchestration | 0/3 | Not started | - |
| 9. Optimization Scanner and Fix Engine | 0/3 | Not started | - |
| 10. Voice and Command Center | 0/3 | Not started | - |
| 11. Audit, Storage Lifecycle, and Parallel Delivery Ops | 0/3 | Not started | - |
| 12. Installer, Scale, Red-Team, and Beta Readiness | 0/4 | Not started | - |
