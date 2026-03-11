# Claude Outbox

Use this file to report status back to Codex.

## Update Format

For each update, include:
- Task ID
- Status: `in_progress`, `blocked`, or `done`
- Files read
- Files changed
- Key findings
- Risks or questions

## Current Updates

### C-001 Phase 1 Safety Audit
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11
- Output: `.planning/claude/C-001-SAFETY-AUDIT.md` (431 lines)
- Files read:
  - `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
  - `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
  - `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
  - `tests/Atlas.Core.Tests/*.cs`
- Key findings:
  - 5 blocked-path edge cases (UNC bypass, symlinks, missing protected paths)
  - 4 mutable-root gaps (hardcoded Downloads, OneDrive KFM conflict)
  - 4 sync-folder issues (overly broad Contains() matching)
  - 4 cross-volume edge cases (SUBST drives, mount points)
  - Only 3 tests exist; recommends 35+ new tests
- Risks: Path normalization silently falls back on exception; profile tampering possible

### C-002 Repository and Retention Packet
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11
- Output: `.planning/claude/C-002-STORAGE-PLAN.md` (639 lines)
- Files read:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `src/Atlas.Storage/AtlasJsonCompression.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `.planning/codebase/ARCHITECTURE.md`
- Key findings:
  - Proposes 5 repositories: Plan, Recovery, Conversation, Configuration, Optimization
  - Maps all 9 existing tables to repository ownership
  - Defines retention job framework with configurable policies
  - Enhances FTS5 search with unified search schema and triggers
- Risks: No migration tooling exists; cascade delete behavior needs spec

### C-003 AI Contract and Eval Packet
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11
- Output: `.planning/claude/C-003-AI-CONTRACTS.md`
- Files read:
  - `src/Atlas.AI/AtlasPlanningClient.cs`
  - `src/Atlas.AI/PromptCatalog.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `.planning/REQUIREMENTS.md`
  - `evals/red-team-cases.md`
- Key findings:
  - Loose JSON schema at AtlasPlanningClient.cs:56-72 needs strict typing
  - Defines 6 eval categories with 30+ test cases
  - Documents 15 prompt-injection attack vectors
  - Provides destructive-language detection rules and escalation logic
  - Proposes prompt-trace capture schema for SAFE-06/DATA-01 compliance
- Risks: Voice-originated commands need extra confirmation gates

---

## Implementation Updates (2026-03-11)

### C-001-IMPL: Safety Test Implementation
- Status: `done`
- Files created:
  - `tests/Atlas.Core.Tests/Policies/PathSafetyClassifierTests.cs` (32 tests)
  - `tests/Atlas.Core.Tests/Policies/PolicyEngineSyncFolderTests.cs` (24 tests)
  - `tests/Atlas.Core.Tests/Policies/PolicyEngineCrossVolumeTests.cs` (20 tests)
  - `tests/Atlas.Core.Tests/Policies/PolicyEngineMutableRootTests.cs` (26 tests)
- Total: **102 new tests** (all passing)
- Coverage includes:
  - UNC path handling and admin shares
  - Path traversal detection
  - Device paths and alternate data streams
  - Sync folder exact match vs false positives
  - Cross-volume move detection
  - Mutable root enforcement
  - Protected path override

### C-002-IMPL: Repository Interface Implementation
- Status: `done`
- Files created:
  - `src/Atlas.Storage/Repositories/IPlanRepository.cs`
  - `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs`
  - `src/Atlas.Storage/Repositories/IConfigurationRepository.cs`
  - `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
- Includes summary record types for efficient list projections
- Includes FTS search types with rank/snippet support
- Project builds with 0 warnings, 0 errors

### C-003-IMPL: Eval Fixture Implementation
- Status: `done`
- Files created:
  - `evals/fixtures/organization-evals.json` (7 test cases)
  - `evals/fixtures/sensitivity-evals.json` (8 test cases)
  - `evals/fixtures/prompt-injection-evals.json` (8 test cases)
  - `evals/fixtures/sync-folder-evals.json` (8 test cases)
- Updated `evals/README.md` with fixture documentation
- Total: **31 eval fixtures** across 4 categories

---

## C-007 Strict AI Pipeline (2026-03-11)

### C-007: Strict AI Pipeline Implementation
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11
- Claimed from: CLAUDE-INBOX.md

#### Scope
- Strict structured-output definitions for planning and voice parsing
- Post-parse semantic validation
- Prompt-trace persistence
- ConversationRepository completion
- OpenAIOptions runtime wiring
- AI-layer tests (Atlas.AI.Tests project)

#### Hard Boundaries
- No `src/Atlas.App/**` edits
- No UI ownership

---

## What Codex Should Read Next
1. Review C-007 strict AI pipeline implementation
2. Review C-006 execution hardening changes in `PlanExecutionService.cs`
3. Fix `AtlasStructureGroupCard` missing type in `AtlasShellSession.cs:93,95`
4. Review persistence implementation in `src/Atlas.Storage/Repositories/`
5. Test installer build with: `dotnet build installer/`

---

## C-007 Strict AI Pipeline (2026-03-11)

### C-007: Strict AI Pipeline Implementation
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11

#### Files Read
- `src/Atlas.AI/AtlasPlanningClient.cs`
- `src/Atlas.AI/OpenAIOptions.cs`
- `src/Atlas.AI/PromptCatalog.cs`
- `src/Atlas.AI/Atlas.AI.csproj`
- `src/Atlas.Core/Contracts/DomainModels.cs`
- `src/Atlas.Core/Contracts/PipeContracts.cs`
- `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
- `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
- `src/Atlas.Storage/AtlasJsonCompression.cs`
- `src/Atlas.Storage/Repositories/IConversationRepository.cs`
- `src/Atlas.Storage/Repositories/PlanRepository.cs`
- `src/Atlas.Storage/Repositories/SqliteConnectionFactory.cs`
- `src/Atlas.Service/Program.cs`
- `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
- `.planning/claude/C-003-AI-CONTRACTS.md`

#### Files Created
- `src/Atlas.AI/ResponseSchemas.cs` - Strict JSON schema definitions for plan and voice responses
- `src/Atlas.AI/PlanSemanticValidator.cs` - Post-parse semantic validation with 6 rule categories
- `src/Atlas.Storage/Repositories/ConversationRepository.cs` - Full conversation + prompt trace repository (10 methods)
- `tests/Atlas.AI.Tests/Atlas.AI.Tests.csproj` - New test project
- `tests/Atlas.AI.Tests/PlanSemanticValidatorTests.cs` - 32 semantic validation tests
- `tests/Atlas.AI.Tests/ResponseSchemasTests.cs` - 12 schema structure tests
- `tests/Atlas.AI.Tests/PlanningClientTests.cs` - 13 planning client integration tests

#### Files Modified
- `src/Atlas.AI/AtlasPlanningClient.cs` - Replaced loose schema with strict, added semantic validation, constructor changed to accept IOptions<OpenAIOptions> and IConversationRepository, added JsonStringEnumConverter
- `src/Atlas.AI/Atlas.AI.csproj` - Added Microsoft.Extensions.Options (8.0.2), added Atlas.Storage project reference
- `src/Atlas.Service/Program.cs` - Registered IConversationRepository in DI
- `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` - Added IConversationRepository to constructor, added prompt-trace persistence for plan and voice handlers
- `Atlas.sln` - Added Atlas.AI.Tests project

#### What Strictness Was Added

**1. Strict Structured-Output Schemas (ResponseSchemas.cs)**
- `PlanResponseSchema`: Full JSON schema with typed `plan` object, `operations` array with enum-constrained `kind` (8 values), `sensitivity` (5 values), `optimization_kind` (8 values), confidence bounded to [0.0, 1.0], `risk_summary` with bounded scores and `approval_requirement` enum (3 values), `additionalProperties: false` at all levels
- `VoiceIntentSchema`: Typed `parsed_intent` (string) and `needs_confirmation` (boolean), `additionalProperties: false`
- Previously the `plan` field was `type = "object"` (any shape accepted)

**2. Post-Parse Semantic Validation (PlanSemanticValidator.cs)**
- Rule 1: Path requirements by operation kind (MovePath needs source+dest, CreateDir needs dest, etc.)
- Rule 2: Protected path detection (Windows, Program Files, ProgramData, AppData, $Recycle.Bin, .git, .ssh)
- Rule 3: Confidence and risk scores bounded to [0.0, 1.0]
- Rule 4: Review escalation enforced when High/Critical sensitivity present
- Rule 5: DeleteToQuarantine with MarksSafeDuplicate=true must have GroupId
- Rule 6: Rollback strategy required when destructive/move operations exist

**3. Prompt-Trace Persistence**
- Planning traces captured: user intent, profile name, summary, plan ID, operation count
- Voice-intent traces captured: transcript, parsed intent, confirmation status
- Both stored via IConversationRepository.SavePromptTraceAsync with stage tags ("planning", "voice_intent")
- Trace persistence errors are logged but don't crash the service (graceful degradation)

**4. ConversationRepository (10 methods, full FTS)**
- SaveConversation + GetConversation + ListConversations + SearchConversations + DeleteConversation + GetExpiredConversationIds
- SavePromptTrace + GetPromptTrace + ListPromptTraces + DeletePromptTrace
- FTS5 full-text search with snippet highlighting and rank scoring
- Brotli compression via AtlasJsonCompression for all payloads

**5. Runtime Wiring**
- OpenAIResponsesPlanningClient now uses IOptions<OpenAIOptions> for API key, model, base URL, and max inventory items
- Environment variable fallback preserved for backward compatibility
- HttpClient BaseAddress set from OpenAIOptions.BaseUrl
- IConversationRepository registered in DI and injected into pipe worker

#### What Invalid Outputs Now Fail Closed
- Plans with untyped/missing `plan` object → fallback
- Plans targeting Windows, Program Files, ProgramData, AppData, $Recycle.Bin, .git, .ssh → fallback
- Plans with out-of-range confidence (< 0.0 or > 1.0) → fallback
- Plans with High/Critical sensitivity but no review escalation → fallback
- Plans with destructive ops but no rollback strategy → fallback
- Safe-duplicate quarantine without GroupId → fallback
- Operations missing required paths (no source for move, no dest for create) → fallback
- Malformed JSON responses → fallback
- Empty model output → fallback

#### Tests Added (57 total, all passing)

| Category | Count | Tests |
|----------|-------|-------|
| Semantic validation - path requirements | 7 | Missing source/dest for Move, Rename, Delete, Restore, CreateDir, MergeDup |
| Semantic validation - protected paths | 9 | Windows, Program Files, Program Files (x86), ProgramData, AppData, $Recycle.Bin, .git, .ssh, safe path negative |
| Semantic validation - confidence/risk | 5 | Out-of-range negative, over 1.0, double over, risk score range, valid bounds |
| Semantic validation - review escalation | 2 | High sensitivity without review fails, Critical with review passes |
| Semantic validation - duplicate rules | 2 | Safe duplicate missing GroupId fails, with GroupId passes |
| Semantic validation - rollback | 3 | Delete without rollback fails, move without rollback fails, create-only without rollback passes |
| Semantic validation - valid plans | 3 | Basic valid, with move, with duplicate quarantine |
| Semantic validation - edge cases | 2 | Null plan, multiple simultaneous violations |
| Schema structure | 12 | Serialization, top-level fields, operations array, kind enum (8 values), confidence bounds, risk bounds, additionalProperties disabled, voice intent fields, sensitivity enum, approval enum |
| Planning client - fallback | 5 | No API key plan, no API key voice, invalid JSON, semantic fail, empty output |
| Planning client - valid flow | 3 | Valid plan parsed, valid voice parsed, invalid voice JSON falls back |
| Planning client - options wiring | 2 | Configured model sent in request, API key in Authorization header |

#### Build Status
- **Build**: 0 errors, 4 pre-existing CA1416 warnings (in OptimizationScanner.cs)
- **Core Tests**: 121 passed
- **Storage Tests**: 42 passed
- **Service Tests**: 27 passed
- **AI Tests**: 57 passed (new)
- **Total**: 247 tests passing

#### No UI Files Touched
- Zero changes in `src/Atlas.App/**`

#### Intentionally Deferred
- Prompt-trace persistence inside `OpenAIResponsesPlanningClient` itself (traces are stored at the service layer instead, which is the correct trust boundary location)
- Full conversation FTS triggers for automatic re-indexing on update (FTS index is built on save; updates would need delete+re-insert)
- Prompt trace retention/cleanup job (tables are populated; retention policy can be added in a future packet)
- `IConversationRepository` not yet available for direct trace storage inside `OpenAIResponsesPlanningClient` when running without service context (e.g. testing with a standalone client)

---

## C-006 Execution Hardening (2026-03-11)

### C-006: Execution Hardening Implementation
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11

#### Files Read
- `src/Atlas.Service/Services/PlanExecutionService.cs`
- `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
- `src/Atlas.Service/Services/AtlasServiceOptions.cs`
- `src/Atlas.Core/Contracts/DomainModels.cs`
- `src/Atlas.Core/Contracts/PipeContracts.cs`
- `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
- `src/Atlas.Core/Planning/RollbackPlanner.cs`
- `src/Atlas.Storage/Repositories/IPlanRepository.cs`
- `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
- `src/Atlas.Storage/Repositories/PlanRepository.cs`
- `src/Atlas.Storage/Repositories/RecoveryRepository.cs`
- `.planning/claude/C-005-PERSISTENCE-INTEGRATION.md`
- `.planning/claude/C-004-INSTALLER-RECOVERY.md`

#### Files Modified
- `src/Atlas.Service/Services/PlanExecutionService.cs` - Complete execution hardening
- `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` - Partial-failure persistence
- `src/Atlas.Service/Atlas.Service.csproj` - Added InternalsVisibleTo for test project

#### Files Created
- `tests/Atlas.Service.Tests/Atlas.Service.Tests.csproj` - New test project
- `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` - 27 execution-layer tests
- Solution updated: `Atlas.sln` now includes `Atlas.Service.Tests`

#### Key Execution Risks Fixed

**1. Execution Preflight (new)**
- Rejects operations with missing required paths (source/destination per operation kind)
- Verifies source existence for move/rename/quarantine/restore before any mutation
- Rejects destination collisions within the batch (two ops targeting same path)
- Rejects destinations that already exist on disk
- Validates drive roots exist for destination paths
- Runs identically for both dry-run and real execution
- Surfaces clear per-operation error messages with operation ID

**2. Safer Operation Ordering (new)**
- Deterministic execution order: CreateDirectory → Move/Rename → RestoreFromQuarantine → DeleteToQuarantine → MergeDuplicateGroup → ApplyOptimizationFix → RevertOptimizationFix
- Preserves relative order within each operation kind (stable sort)
- Dry-run output reflects the same ordering as real execution

**3. Partial-Failure Handling (new)**
- If any operation throws, remaining operations are skipped immediately
- Failure message includes the specific operation and exception
- Checkpoint is built from only the operations that actually completed
- Partial-failure response returns `Success = false` with a non-empty checkpoint
- Checkpoint notes include partial-failure metadata (completed count vs total)
- No rollback steps fabricated for operations that never ran

**4. Quarantine Metadata Correctness (bug fix)**
- **Fixed**: `QuarantineItem.PlanId` now uses the batch's `PlanId` instead of `operation.GroupId` (was using duplicate group ID, not plan context)
- **Added**: SHA-256 content hashing for quarantined files (best-effort, skips locked/inaccessible files)
- Retention: 30-day default from `DateTimeOffset.UtcNow` (stable, configurable via future config)
- Original path and quarantine path both tracked accurately

**5. Persistence Touchpoints (fix)**
- `HandleExecutionAsync` now persists recovery data even on partial failure when real mutations occurred
- Condition: `executionRequest.Execute && response.UndoCheckpoint.InverseOperations.Count > 0`
- Dry-run: Not persisted (intentional - no mutations to undo)

#### Tests Added (27 total, all passing)

| Category | Count | Tests |
|----------|-------|-------|
| Preflight validation | 10 | Missing source/dest paths for each op type, non-existent source, destination collision, destination exists, valid ops pass |
| Operation ordering | 5 | Create before move, move before quarantine, quarantine before optimization, relative order preserved, full sequence |
| Dry-run parity | 2 | Preflight runs on dry-run, dry-run output reflects ordering |
| Quarantine metadata | 4 | PlanId correctness, content hash present, path tracking, retention window |
| Partial failure | 2 | Stops batch and returns completed ops only, no fabricated rollback |
| End-to-end | 4 | Create+move, undo reversal, quarantine+restore roundtrip |

#### Build Status
- **Build**: 0 errors, 4 pre-existing CA1416 warnings (in OptimizationScanner.cs)
- **Core Tests**: 121 passed
- **Storage Tests**: 42 passed
- **Service Tests**: 27 passed (new)
- **Total**: 190 tests passing

#### No UI Files Touched
- Zero changes in `src/Atlas.App/**`

#### Intentionally Deferred
- Content hashing for directories (only files hashed; directories would require recursive hashing which could be slow for large trees)
- Configurable retention period (currently 30 days hardcoded; `AtlasServiceOptions` could add a `QuarantineRetentionDays` property in a future packet)
- Race condition between preflight and execution (TOCTOU: a file could vanish after preflight passes but before the operation runs; the partial-failure handler covers this case safely)

---

## C-009 Installer and Service Registration (2026-03-11)

### C-009: Windows Service Hosting and Installer Integration
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11

#### Files Modified

**`src/Atlas.Service/Atlas.Service.csproj`**
- Added `Microsoft.Extensions.Hosting.WindowsServices` Version 8.0.1
- Added `RuntimeIdentifier` = `win-x64` for proper service deployment

**`src/Atlas.Service/Program.cs`**
- Added `builder.Services.AddWindowsService()` with service name `AtlasFileIntelligence`

**`installer/Product.wxs`**
- Added `MajorUpgrade` element for clean upgrade handling
- Added `ServiceInstall` element:
  - Service name: `AtlasFileIntelligence`
  - Display name: `Atlas File Intelligence`
  - Start type: `auto` (starts with Windows)
  - Account: `NT AUTHORITY\LocalService` (principle of least privilege)
- Added `ServiceControl` for start/stop lifecycle
- Added Start Menu shortcut for Atlas.App
- Added appsettings.json to installer components
- Changed file sources to Release configuration

#### Service Capabilities After Install
- Runs as Windows Service under LocalService account
- Starts automatically with Windows
- Stopped cleanly during upgrade/uninstall
- Named pipe communication unchanged

#### Cleanup
- Removed duplicate `SqliteConnectionFactory.cs` from `src/Atlas.Storage/` (kept one in `Repositories/`)

#### Build Status
- **Service Build**: 0 errors, 4 pre-existing CA1416 warnings
- **Core Tests**: 121 passed
- **Storage Tests**: 42 passed
- **App Build**: 2 errors (Codex-owned - missing `AtlasStructureGroupCard` type)

#### Notes for Codex
- The App project has build errors due to missing `AtlasStructureGroupCard` type at lines 93 and 95 of `AtlasShellSession.cs`
- Service is ready to run as Windows Service - test with: `sc.exe create AtlasFileIntelligence binPath="path\to\Atlas.Service.exe"`
- Installer needs Release builds to work: `dotnet publish -c Release`

---

## C-005 Persistence Integration (2026-03-11)

### C-005: Persistence Integration Implementation
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11

#### Files Created (Repository Implementations)
- `src/Atlas.Storage/Repositories/SqliteConnectionFactory.cs` - Connection factory
- `src/Atlas.Storage/Repositories/PlanRepository.cs` - Plans and batches
- `src/Atlas.Storage/Repositories/RecoveryRepository.cs` - Checkpoints and quarantine
- `src/Atlas.Storage/Repositories/OptimizationRepository.cs` - Optimization findings
- `src/Atlas.Storage/Repositories/ConfigurationRepository.cs` - Policy profiles

#### Files Modified (Service Integration)
- `src/Atlas.Service/Program.cs` - Added DI registrations for all repositories
- `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` - Added persistence at handler points:
  - `HandlePlanAsync`: Saves plan after creation
  - `HandleExecutionAsync`: Saves batch, checkpoint, quarantine items after execution
  - `HandleOptimizeAsync`: Saves optimization findings after scan

#### Test Project Created
- `tests/Atlas.Storage.Tests/Atlas.Storage.Tests.csproj`
- `tests/Atlas.Storage.Tests/TestDatabaseFixture.cs`
- `tests/Atlas.Storage.Tests/PlanRepositoryTests.cs` (8 tests)
- `tests/Atlas.Storage.Tests/RecoveryRepositoryTests.cs` (14 tests)
- `tests/Atlas.Storage.Tests/OptimizationRepositoryTests.cs` (10 tests)
- `tests/Atlas.Storage.Tests/ConfigurationRepositoryTests.cs` (10 tests)
- **Total: 42 tests, all passing**

#### Schema Changes
- None - existing schema in AtlasDatabaseBootstrapper was sufficient

#### Implementation Notes
- All repositories use `AtlasJsonCompression` for payload storage
- JSON serialization with `System.Text.Json`
- ISO8601 date formatting for SQLite compatibility
- Parameterized queries throughout (SQL injection safe)
- Persistence errors are logged but don't crash the service (graceful degradation)
- `IConversationRepository` deferred to later packet (FTS5 complexity)

#### Build Status
- **Build**: 0 errors, 4 pre-existing CA1416 warnings
- **Core Tests**: 121 passed
- **Storage Tests**: 42 passed
- **Total**: 163 tests passing

---

## Research Updates (2026-03-11)

### C-004: Installer and Recovery Research
- Status: `done`
- Started: 2026-03-11
- Completed: 2026-03-11
- Output: `.planning/claude/C-004-INSTALLER-RECOVERY.md`
- Files read:
  - `installer/Bundle.wxs`
  - `installer/Product.wxs`
  - `src/Atlas.Service/Program.cs`
  - `src/Atlas.Service/Services/AtlasStartupWorker.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Atlas.Service.csproj`
- Key findings:
  - Current installer only copies executables - no service registration
  - Missing: ServiceInstall, ServiceControl, MajorUpgrade elements
  - Missing: runtime dependency files (DLLs, assets, configs)
  - Missing: .NET 8 and Windows App SDK prerequisite detection
  - Service needs `Microsoft.Extensions.Hosting.WindowsServices` package
  - Recommend virtual account (`NT SERVICE\AtlasFileIntelligence`)
  - VSS eligibility rules defined (6 criteria)
  - 8 recovery failure modes analyzed with mitigations
- Risks: Service will not function without proper WiX ServiceInstall element
- Immediate actions required:
  1. Add WindowsService support to Atlas.Service
  2. Add ServiceInstall to WiX Product.wxs
  3. Harvest all deployment files from publish output
  4. Add MajorUpgrade element
