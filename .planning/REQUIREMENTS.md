# Requirements: Atlas File Intelligence

**Defined:** 2026-03-11
**Core Value:** Atlas must improve organization and performance without ever violating user trust, losing important data, or touching protected system areas.

## v1 Requirements

### Safety and Trust

- [ ] **SAFE-01**: The AI layer can only propose structured operations from an approved DSL; it cannot mutate the file system directly
- [ ] **SAFE-02**: Atlas blocks operations targeting protected system, program, credential, and app-critical paths
- [ ] **SAFE-03**: Atlas requires explicit review or approval for high-sensitivity, cross-volume, sync-folder, or destructive operations
- [ ] **SAFE-04**: Atlas excludes sync-managed folders by default unless the user explicitly opts in
- [ ] **SAFE-05**: Every destructive or structural action has a defined rollback strategy before execution starts
- [ ] **SAFE-06**: Atlas records an audit trail for plans, approvals, executions, and undo activity

### Scanning and Inventory

- [ ] **SCAN-01**: Atlas scans all mounted drives and records volume metadata, capacity, and readiness
- [ ] **SCAN-02**: Atlas supports incremental rescans on NTFS volumes using the USN change journal, with fallback rescans elsewhere
- [ ] **SCAN-03**: Atlas identifies file type using more than the filename, including extension, signature, metadata, and extractable content signals
- [ ] **SCAN-04**: Atlas classifies file sensitivity using path heuristics, special locations, keywords, and content-derived signals
- [ ] **SCAN-05**: Atlas groups exact duplicates safely using tiered hashing and deterministic canonical-file selection

### Planning and Explainability

- [ ] **PLAN-01**: A user can request a full organization plan from typed input, preset workflows, or later voice input
- [ ] **PLAN-02**: Atlas generates user-readable rationales for category placement, duplicate handling, and optimization recommendations
- [ ] **PLAN-03**: Atlas locally risk-scores every proposed operation before it can be shown or executed
- [ ] **PLAN-04**: Atlas shows a before-and-after reorganization preview, including tree changes and operation summaries
- [ ] **PLAN-05**: Atlas can run a second-pass risk review for high-impact or highly destructive batches

### Execution and Recovery

- [ ] **EXEC-01**: Atlas executes only approved create, move, rename, quarantine, restore, and optimization operations
- [ ] **EXEC-02**: Atlas moves duplicate or deleted content into quarantine instead of purging it immediately
- [ ] **EXEC-03**: Atlas can undo an execution batch using inverse operations and stored checkpoint metadata
- [ ] **EXEC-04**: Atlas can restore individual quarantined files without rolling back an entire batch
- [ ] **EXEC-05**: Atlas can create recovery checkpoints for qualifying destructive batches on supported NTFS volumes

### Native UX and Controls

- [ ] **UX-01**: Atlas provides a native WinUI shell with dashboard, plan review, undo timeline, optimization center, conversation history, and settings
- [ ] **UX-02**: Atlas uses a premium, animated, accessible visual system with reduced-motion support
- [ ] **UX-03**: A user can inspect why a file was categorized, protected, marked sensitive, or quarantined
- [ ] **UX-04**: Atlas stores and surfaces searchable conversations, plans, and action history
- [ ] **UX-05**: A user can manage policy settings such as roots, exclusions, sensitivity rules, and retention windows

### Voice and Commands

- [ ] **VOICE-01**: Atlas supports push-to-talk voice capture with live transcript preview
- [ ] **VOICE-02**: Atlas parses voice requests into the same guarded intent pipeline used by typed commands
- [ ] **VOICE-03**: Atlas requires confirmation before a voice-originated action can trigger planning or execution

### Optimization

- [ ] **OPTI-01**: Atlas scans for safe PC optimization opportunities across temp files, cache bloat, startup entries, low disk pressure, and non-essential background activity
- [ ] **OPTI-02**: Atlas auto-fixes only curated low-risk optimization classes approved by policy
- [ ] **OPTI-03**: Atlas treats risky or system-level optimizations as recommendations, not automatic actions
- [ ] **OPTI-04**: Atlas provides evidence and rollback guidance for every optimization finding
- [ ] **OPTI-05**: Atlas never disables Windows security, drivers, or core Microsoft services as part of optimization

### Data, Storage, and Auditing

- [ ] **DATA-01**: Atlas stores plans, execution batches, checkpoints, conversations, policies, prompt traces, and quarantine metadata locally in SQLite
- [ ] **DATA-02**: Atlas compresses large structured artifacts and applies retention policies to minimize disk usage
- [ ] **DATA-03**: Atlas protects secrets and sensitive local settings using Windows-native secure storage mechanisms
- [ ] **DATA-04**: Atlas maintains evaluation assets that cover prompt injection, unsafe deletion requests, sync-folder hazards, and rollback failures

### Delivery and Operations

- [ ] **OPS-01**: The solution can be progressed in parallel by Codex and Claude Code in VS Code using shared planning, decisions, inbox/outbox notes, and handoff artifacts
- [ ] **OPS-02**: The product ships with an installer that can deploy the app, the service, dependencies, and recovery prerequisites
- [ ] **OPS-03**: Atlas can run at consumer scale without exhausting memory on large personal file sets
- [ ] **OPS-04**: Atlas includes automated and manual verification paths for safety, rollback, performance, and UX acceptance

## v2 Requirements

### Advanced Scope

- **ADV-01**: Atlas supports optional local/offline model routing for privacy-sensitive deployments
- **ADV-02**: Atlas supports workspace templates for different organization styles and user personas
- **ADV-03**: Atlas learns long-term user preferences from accepted and rejected plans
- **ADV-04**: Atlas supports multi-user profile isolation on shared PCs
- **ADV-05**: Atlas supports advanced near-duplicate semantic clustering for photos and documents

## Out of Scope

| Feature | Reason |
|---------|--------|
| Remote fleet optimization and enterprise policy push | Not aligned with the first local consumer/prosumer milestone |
| Fully autonomous background cleanup without review thresholds | Too risky for trust and reversibility |
| Direct shell, PowerShell, or arbitrary command execution by the model | Violates the safety boundary |
| Permanent shredding as a first-class AI action | Quarantine-first deletion is the product promise |
| Support for non-Windows operating systems | Native Windows experience is the primary differentiator |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| SAFE-01 | Phase 1 | Pending |
| SAFE-02 | Phase 1 | Pending |
| SAFE-03 | Phase 5 | Pending |
| SAFE-04 | Phase 1 | Pending |
| SAFE-05 | Phase 7 | Pending |
| SAFE-06 | Phase 11 | Pending |
| SCAN-01 | Phase 2 | Pending |
| SCAN-02 | Phase 2 | Pending |
| SCAN-03 | Phase 3 | Pending |
| SCAN-04 | Phase 4 | Pending |
| SCAN-05 | Phase 4 | Pending |
| PLAN-01 | Phase 5 | Pending |
| PLAN-02 | Phase 6 | Pending |
| PLAN-03 | Phase 5 | Pending |
| PLAN-04 | Phase 6 | Pending |
| PLAN-05 | Phase 5 | Pending |
| EXEC-01 | Phase 7 | Pending |
| EXEC-02 | Phase 7 | Pending |
| EXEC-03 | Phase 7 | Pending |
| EXEC-04 | Phase 7 | Pending |
| EXEC-05 | Phase 8 | Pending |
| UX-01 | Phase 6 | Pending |
| UX-02 | Phase 6 | Pending |
| UX-03 | Phase 6 | Pending |
| UX-04 | Phase 11 | Pending |
| UX-05 | Phase 6 | Pending |
| VOICE-01 | Phase 10 | Pending |
| VOICE-02 | Phase 10 | Pending |
| VOICE-03 | Phase 10 | Pending |
| OPTI-01 | Phase 9 | Pending |
| OPTI-02 | Phase 9 | Pending |
| OPTI-03 | Phase 9 | Pending |
| OPTI-04 | Phase 9 | Pending |
| OPTI-05 | Phase 9 | Pending |
| DATA-01 | Phase 7 | Pending |
| DATA-02 | Phase 11 | Pending |
| DATA-03 | Phase 11 | Pending |
| DATA-04 | Phase 12 | Pending |
| OPS-01 | Phase 11 | Pending |
| OPS-02 | Phase 12 | Pending |
| OPS-03 | Phase 12 | Pending |
| OPS-04 | Phase 12 | Pending |

**Coverage:**
- v1 requirements: 42 total
- Mapped to phases: 42
- Unmapped: 0

---
*Requirements defined: 2026-03-11*
*Last updated: 2026-03-11 after brownfield planning initialization*
