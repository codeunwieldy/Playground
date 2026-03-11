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
- Status: **ready**
- Output: `.planning/claude/C-008-HISTORY-QUERY-APIS.md`
- Focus:
  - read-only history/query pipe contracts
  - service handlers for plans, checkpoints, quarantine, optimization, and prompt traces
  - bounded app-ready DTOs
  - read-side tests
- Boundaries:
  - no `src/Atlas.App/**`
  - no UI ownership
