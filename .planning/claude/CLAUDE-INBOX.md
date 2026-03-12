# Claude Inbox

Use this file as the active assignment queue for Claude Code.

## Rules

- Claim work by noting the task ID in `.planning/claude/CLAUDE-OUTBOX.md`
- Do not claim UI/UX tasks unless the packet explicitly says so
- If a task seems blocked by Codex-owned UI work, report that instead of forcing it

## Ready Now

### C-001 Phase 1 Safety Audit Packet
- Status: **complete** (audit + 121 tests implemented)
- Output: `.planning/claude/C-001-SAFETY-AUDIT.md`
- Implementation: `tests/Atlas.Core.Tests/Policies/*.cs`

### C-002 Repository and Retention Packet
- Status: **complete** (plan + 5 interfaces implemented)
- Output: `.planning/claude/C-002-STORAGE-PLAN.md`
- Implementation: `src/Atlas.Storage/Repositories/*.cs`

### C-003 AI Contract and Eval Packet
- Status: **complete** (plan + 31 fixtures implemented)
- Output: `.planning/claude/C-003-AI-CONTRACTS.md`
- Implementation: `evals/fixtures/*.json`

### C-004 Installer and Recovery Research Packet
- Status: **complete** (research document produced)
- Output: `.planning/claude/C-004-INSTALLER-RECOVERY.md`
- Key deliverables:
  - MSI/WiX packaging requirements and gaps
  - Service registration requirements
  - VSS checkpoint eligibility rules (6 criteria)
  - 8 recovery failure modes with mitigations
  - Deployment checklist

## Waiting

### C-005 Persistence Integration Packet
- Status: **complete** (5 repository implementations + service integration + 42 tests)
- Output: `src/Atlas.Storage/Repositories/*.cs` (concrete implementations)
- Service integration: `src/Atlas.Service/Program.cs`, `AtlasPipeServerWorker.cs`
- Tests: `tests/Atlas.Storage.Tests/` (42 tests, all passing)
- Deferred: `IConversationRepository` (FTS5 complexity, later packet)

### C-009 Installer and Service Registration Packet
- Status: **complete** (Windows Service hosting + WiX installer)
- Implementation:
  - `src/Atlas.Service/Atlas.Service.csproj` - Added WindowsServices package
  - `src/Atlas.Service/Program.cs` - Added AddWindowsService()
  - `installer/Product.wxs` - ServiceInstall, ServiceControl, MajorUpgrade, shortcuts
- Note: App build blocked by missing `AtlasStructureGroupCard` type (Codex-owned)

### C-006 Execution Hardening Packet
- Status: **complete** (hardened execution + 27 tests)
- Output: `.planning/claude/C-006-EXECUTION-HARDENING.md`
- Implementation:
  - `src/Atlas.Service/Services/PlanExecutionService.cs` (preflight, ordering, partial-failure, quarantine fix)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` (partial-failure persistence)
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` (27 tests, all passing)

### C-007 Strict AI Pipeline Packet
- Status: **complete** (strict schemas + semantic validation + trace persistence + ConversationRepository + 57 tests)
- Output: `.planning/claude/CLAUDE-OUTBOX.md` (C-007 section)
- Implementation:
  - `src/Atlas.AI/ResponseSchemas.cs` - Strict JSON schemas for plan and voice
  - `src/Atlas.AI/PlanSemanticValidator.cs` - 6-rule semantic validation layer
  - `src/Atlas.AI/AtlasPlanningClient.cs` - Strict schemas, validation, OpenAIOptions wiring
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs` - 10 methods with FTS5
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` - Prompt-trace persistence
  - `tests/Atlas.AI.Tests/` - 57 tests covering schemas, validation, client, and wiring

## Open Next

### C-008 Persisted History and Query APIs Packet
- Status: **complete** (read-only history/query routes, contracts, and tests landed)
- Output: `.planning/claude/C-008-HISTORY-QUERY-APIS.md`
- Codex clarification: `.planning/claude/C-008-CODEX-TARGET.md`
- Focus:
  - read-only history/query pipe contracts
  - service handlers for plans, checkpoints, quarantine, optimization, and prompt traces
  - bounded app-ready DTOs
  - read-side tests
- Codex target:
  - wire the existing `src/Atlas.App/Views/HistoryPage.cs` and `src/Atlas.App/Services/AtlasShellSession.cs`
  - do not assume new page files or a separate view-model layer
  - optimize for a stable UX shape fed by service-backed summaries
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-010 Inventory Persistence Foundations Packet
- Status: **complete** (inventory schema, repository, scan persistence, and tests landed)
- Output: `.planning/claude/C-010-INVENTORY-PERSISTENCE-FOUNDATIONS.md`
- Focus:
  - persisted inventory schema and repository layer
  - scan-session storage for volumes, roots, and file snapshots
  - service-side persistence after live scans
  - storage and read-side tests
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not implement USN journal or watcher orchestration yet unless required for the persistence design

### C-011 Inventory Query APIs Packet
- Status: **complete** (read-only inventory routes, contracts, and tests landed)
- Output: `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
- Focus:
  - read-only inventory/query pipe contracts
  - service handlers for recent scan sessions, latest session summary, session volumes, and session file snapshots
  - bounded app-ready DTOs
  - read-side tests
- Codex target:
  - wire the existing dashboard and shell session to persisted scan memory
  - do not assume new page files or a separate view-model layer
  - optimize for a stable inventory UX fed by service-backed summaries
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - no USN journal or watcher implementation in this packet

### C-012 Delta Scanning and Rescan Orchestration Packet
- Status: **complete** (delta-source seam, bounded rescan worker, and tests landed)
- Output: `.planning/claude/C-012-DELTA-SCANNING-AND-RESCAN-ORCHESTRATION.md`
- Focus:
  - incremental scan orchestration foundations
  - USN-first design with safe watcher/scheduled-rescan fallback
  - bounded service-side rescans that persist new scan sessions
  - service and storage tests for fallback/scheduling behavior
- Codex target:
  - keep the existing dashboard and memory workspace shape stable
  - deliver backend change-detection foundations that can later feed inventory drift and “what changed” UX
  - do not assume or create new UI work
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not redesign existing repositories unless the orchestration truly needs a minimal additive change

### C-013 Scan Diff and Drift Query APIs Packet
- Status: **complete** (session diffing, drift APIs, contracts, and tests landed)
- Output: `.planning/claude/C-013-SCAN-DIFF-AND-DRIFT-QUERY-APIS.md`
- Focus:
  - persisted session diffing between scan sessions
  - read-only drift/query pipe contracts for latest-vs-previous and explicit session comparisons
  - bounded changed/added/removed summary DTOs for app consumption
  - storage/service tests for diff semantics and empty-state handling
- Codex target:
  - feed the existing dashboard and `Atlas Memory` continuity surfaces
  - unlock "what changed" UX without requiring new UI architecture
  - do not assume or create new page files
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - no direct app/database access

### C-014 Actual USN Journal Integration Packet
- Status: **complete** (real USN journal reading, checkpoint persistence, bounded detection, and tests landed)
- Output: `.planning/claude/C-014-ACTUAL-USN-JOURNAL-INTEGRATION.md`
- Focus:
  - replace the current USN probe-only seam with real journal reading on supported NTFS volumes
  - emit bounded changed-path results through the existing delta-source abstraction
  - keep safe fallback to watcher and scheduled rescans
- Codex target:
  - no immediate UI dependency
  - strengthens the backend accuracy and efficiency behind future drift UX
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not remove fallback behavior while upgrading USN support

### C-015 Incremental Session Composition Packet
- Status: **complete** (reported complete; outbox sync may still need to be written)
- Output: `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
- Focus:
  - turn delta results into trustworthy persisted inventory sessions
  - compose new sessions from a baseline plus bounded add/remove/change sets
  - persist minimal provenance metadata for how a session was built
  - preserve safe full-rescan fallback when incremental composition is not trustworthy
- Codex target:
  - feed a future native scan-intelligence UX with session provenance and rescan-story detail
  - keep the existing dashboard, memory workspace, and drift-review architecture stable
  - do not assume or create new UI files
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not replace the existing persisted session model if an additive evolution is enough

### C-016 Incremental Provenance Query APIs Packet
- Status: **complete** (provenance fields added to existing inventory query contracts)
- Output: `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
- Focus:
  - expose incremental-session provenance and lineage through read-only inventory query APIs
  - surface build mode, delta source, trigger/origin, trust level, and baseline linkage for app consumption
  - keep the read path additive and bounded so the existing shell can consume it directly
- Codex target:
  - feed the existing scan provenance and rescan-story surfaces in the dashboard, plans workspace, and `Atlas Memory`
  - do not require a new page architecture or separate app-side model layer
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not redesign the inventory contracts broadly if additive fields or one narrow route is enough

### C-017 Incremental Composition Activation Packet
- Status: **complete** (incremental composition activated in orchestration, 6 tests, all passing)
- Output: `.planning/claude/C-017-INCREMENTAL-COMPOSITION-ACTIVATION.md`
- Focus:
  - activate real incremental-composition sessions in orchestration when bounded delta input is safe
  - populate trust, fallback, baseline, and composition-note provenance meaningfully
  - preserve conservative fallback to full rescan when incremental composition is incomplete or unsafe
- Codex target:
  - feed the existing provenance and rescan-story UI with truthful incremental vs full-rescan behavior
  - avoid any app-side contract churn beyond the additive fields already shipped in `C-016`
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - keep the current inventory read surface stable unless a narrowly additive backend change is unavoidable

### C-018 Untrusted Session States and Partial Degradation Packet
- Status: **complete** (degraded retained sessions, trust semantics, and tests landed)
- Output: `.planning/claude/C-018-UNTRUSTED-SESSION-STATES-AND-PARTIAL-DEGRADATION.md`
- Focus:
  - introduce honest `IsTrusted=false` behavior for degraded retained sessions
  - define when Atlas persists a degraded session versus forcing a full rescan
  - populate precise degradation notes without changing the app contract surface
- Codex target:
  - feed the existing scan-trust and provenance UX in the dashboard, plans workspace, and `Atlas Memory`
  - keep the current shell architecture stable
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - preserve conservative fallback behavior over optimistic partial state

### C-019 Trust-Aware Plan Gating Packet
- Status: **complete** (trust-aware planning/execution gating and tests landed)
- Output: `.planning/claude/C-019-TRUST-AWARE-PLAN-GATING.md`
- Focus:
  - make service-side planning and execution react to degraded retained scan trust
  - elevate review or block execution when Atlas lacks a trusted retained inventory basis
  - prefer existing risk and execution response surfaces over broad contract churn
- Codex target:
  - feed the current plan review, command deck, and trust UX with truthful service-side gating
  - keep the shell architecture stable
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - preserve preview availability while keeping risky execution conservative

### C-020 Content Sniffing and MIME Detection Packet
- Status: **complete** (bounded content sniffing, MIME/category truth, and tests landed)
- Output: `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
- Focus:
  - bounded content-based file understanding for common file families
  - truthful MIME/category population beyond extension-only typing
  - bounded `ContentFingerprint` population and reuse in full scan + single-file inspection
  - service-side tests for sniffing and fallback behavior
- Codex target:
  - keep Codex free to deepen duplicate intelligence and shell behavior without overlapping the scanner internals
  - provide richer file-understanding truth that later sensitivity and duplicate packets can consume
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not redesign duplicate canonical ranking in this packet unless a tiny scanner-side seam is unavoidable

### C-021 Sensitivity Scoring and Evidence Packet
- Status: **complete** (sensitivity scorer, evidence model, and tests landed)
- Output: `.planning/claude/C-021-SENSITIVITY-SCORING-AND-EVIDENCE.md`
- Focus:
  - bounded sensitivity scoring that uses path, filename, extension, MIME/category, and content-aware file truth
  - reusable scanner-side sensitivity classification for both full scan and single-file inspection
  - evidence-friendly internal structure for future explainability without broad contract churn
  - service-side tests for conservative sensitivity behavior
- Codex target:
  - let Codex improve AI prompt projection and shell reasoning against stronger sensitivity truth
  - avoid overlap with prompt shaping or app-side UX work
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
  - do not redesign planning prompts or public app contracts broadly in this packet

### C-022 Duplicate Evidence and Confidence Packet
- Status: **complete** (duplicate analyzer, evidence fields, confidence scoring, and 23 tests landed)
- Output: `.planning/claude/C-022-DUPLICATE-EVIDENCE-AND-CONFIDENCE.md`
- Focus:
  - bounded duplicate analysis and confidence truth
  - canonical rationale and duplicate risk flags
  - scanner integration and tests
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-023 Persisted Duplicate Review and Query APIs Packet
- Status: **complete** (schema, repository, pipe route, service wiring, and 14 tests landed)
- Output: `.planning/claude/C-023-PERSISTED-DUPLICATE-REVIEW-AND-QUERY-APIS.md`
- Focus:
  - persist duplicate review truth with confidence, canonical rationale, and risk flags
  - expose bounded retained-session duplicate queries for app consumption
  - keep duplicate review durable across full scans and retained sessions
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-024 File Inspection and Sensitivity Explainability APIs Packet
- Status: **complete** (contracts, routes, handlers verified; 15 tests passing)
- Output: `.planning/claude/C-024-FILE-INSPECTION-AND-SENSITIVITY-EXPLAINABILITY-APIS.md`
- Focus:
  - bounded live file inspection through the service
  - additive explainability contracts for MIME/category truth and sensitivity evidence
  - conservative outcomes for missing, excluded, or protected paths
  - service and contract tests
- Codex target:
  - let the existing shell gain trustworthy "why this file matters" drill-in later without UI architecture churn
  - keep current Memory / Plan Review surfaces stable while backend explainability grows
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-025 Duplicate Group Detail and Evidence APIs Packet
- Status: **complete** (schema, evidence persistence, detail route, contracts, and 8 tests landed)
- Output: `.planning/claude/C-025-DUPLICATE-GROUP-DETAIL-AND-EVIDENCE-APIS.md`
- Focus:
  - additive duplicate evidence persistence and drill-in detail
  - bounded read-only route for one retained duplicate group
  - canonical, confidence, risk-flag, evidence, and member posture detail
  - repository and service tests
- Codex target:
  - let the current shell evolve from duplicate summary cards into trustworthy duplicate-group explanation without app-side reinvention
  - keep existing Memory / Plan Review surfaces stable while backend duplicate drill-in grows
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-026 Duplicate Action Eligibility and Review APIs Packet
- Status: **complete** (read-only retained duplicate action review route, evaluator, and 14 tests landed)
- Output: `.planning/claude/C-026-DUPLICATE-ACTION-ELIGIBILITY-AND-REVIEW-APIS.md`
- Focus:
  - read-only retained duplicate action review under current policy
  - deterministic cleanup eligibility, review requirement, and recommended posture
  - bounded blocked reasons and action notes for one retained duplicate group
  - service and contract tests
- Codex target:
  - let the current shell evolve from duplicate explanation into duplicate decision support without app-side rule reinvention
  - keep existing Memory / Plan Review surfaces stable while backend duplicate review grows
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-027 Duplicate Cleanup Preview APIs Packet
- Status: **complete** (read-only cleanup preview route, contracts, and 7 tests landed)
- Output: `.planning/claude/C-027-DUPLICATE-CLEANUP-PREVIEW-APIS.md`
- Focus:
  - read-only retained duplicate cleanup preview for one group
  - deterministic operation preview using the existing cleanup planner
  - bounded blocked reasons, action notes, and keep/quarantine path truth
  - service and contract tests
- Codex target:
  - let the current shell evolve from duplicate decision support into concrete cleanup preview without app-side cleanup logic
  - keep existing Memory / Plan Review surfaces stable while backend duplicate preview grows
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-028 Duplicate Cleanup Batch Preview APIs Packet
- Status: **complete** (read-only batch cleanup preview route, contracts, and 7 tests landed)
- Output: `.planning/claude/C-028-DUPLICATE-CLEANUP-BATCH-PREVIEW-APIS.md`
- Focus:
  - read-only retained-session duplicate cleanup batch preview
  - deterministic aggregate preview using the existing single-group preview logic
  - bounded eligible-versus-blocked group summaries and total operation counts
  - service and contract tests
- Codex target:
  - let the current shell evolve from one-group duplicate drill-in into later session-level cleanup impact review without app-side cleanup logic
  - keep existing Memory / Plan Review surfaces stable while backend duplicate preview grows
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership

### C-029 Duplicate Cleanup Plan Preview APIs Packet
- Status: **ready**
- Output: `.planning/claude/C-029-DUPLICATE-CLEANUP-PLAN-PREVIEW-APIS.md`
- Focus:
  - read-only retained-session duplicate cleanup plan preview
  - deterministic included-vs-blocked duplicate group selection
  - bounded grouped operations, rationale, and rollback-oriented notes for review surfaces
  - service and contract tests
- Codex target:
  - let the current shell evolve from cleanup impact review into a future duplicate cleanup plan-review step without app-side cleanup logic
  - keep existing Memory / Plan Review surfaces stable while backend duplicate planning grows
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
