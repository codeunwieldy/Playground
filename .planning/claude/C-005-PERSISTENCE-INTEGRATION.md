# C-005 Persistence Integration Packet

## Owner

Claude Code in VS Code

## Priority

High

## Why This Is Open Now

Codex has moved the WinUI shell from a static prototype into a functional client surface:

- `src/Atlas.App/Services/AtlasShellSession.cs` now drives scan, plan, optimization, execution-preview, undo-preview, and policy state
- `src/Atlas.App/MainWindow.xaml.cs` now routes command input into that session
- the app pages now reflect live or preview state instead of placeholder copy

That means the next backend-heavy slice is persistence. The service already has the right IPC shape, and Claude already created repository interfaces. What is still missing is the real storage implementation and service-side writes so plans, batches, checkpoints, quarantine items, and optimization findings stop being session-only.

## Hard Boundaries

- Do not edit `src/Atlas.App/**`
- Do not take ownership of WinUI, shell interaction, visual design, or XAML
- Do not remove or weaken existing safety validation
- Do not rewrite the storage schema casually; only make schema changes that are necessary to satisfy the repository contracts, and document them in the outbox

## Read First

1. `src/Atlas.App/Services/AtlasShellSession.cs`
2. `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
3. `src/Atlas.Storage/AtlasJsonCompression.cs`
4. `src/Atlas.Storage/Repositories/IPlanRepository.cs`
5. `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
6. `src/Atlas.Storage/Repositories/IConversationRepository.cs`
7. `src/Atlas.Storage/Repositories/IConfigurationRepository.cs`
8. `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
9. `src/Atlas.Service/Program.cs`
10. `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
11. `src/Atlas.Service/Services/PlanExecutionService.cs`
12. `src/Atlas.Service/Services/OptimizationScanner.cs`
13. `.planning/claude/C-002-STORAGE-PLAN.md`

## Goal

Implement the first real persistence layer for Atlas so the service can store:

- generated plans
- execution batches
- undo checkpoints
- quarantine items
- optimization findings
- policy profiles

and expose that through the repository interfaces Claude already defined.

## Required Deliverables

### 1. Concrete repository implementations

Create concrete SQLite-backed implementations under `src/Atlas.Storage/Repositories/` for:

- `IPlanRepository`
- `IRecoveryRepository`
- `IOptimizationRepository`
- `IConfigurationRepository`

If `IConversationRepository` is easy to finish without blowing scope, include it. If not, leave it for a later packet and say so clearly.

Use:

- the existing `AtlasDatabaseBootstrapper`
- the existing `AtlasJsonCompression`
- deterministic JSON serialization

Keep list queries lightweight. Use summary projections where the interfaces already ask for them.

### 2. Storage plumbing

Add the minimal reusable plumbing the repositories need, for example:

- a connection factory or shared helper for opening SQLite connections
- serializer helpers if needed
- small SQL helpers only if they reduce duplication

Do not introduce a heavyweight ORM.

### 3. Service integration

Wire the repositories into `src/Atlas.Service/Program.cs` and then integrate writes at the right points:

- save plans when a live `plan/request` succeeds
- save optimization findings when a live `optimize/request` succeeds
- save execution batches and checkpoints when execution or execution-preview runs
- save quarantine items produced by execution

Keep the current pipe behavior intact.

If a clean service seam is missing, add one. Prefer small composable changes over broad rewrites.

### 4. Tests

Add repository-focused tests for round-tripping the stored contracts.

Good targets:

- save/list/get for plans
- save/get batch by plan
- save/get/list checkpoints
- save/get/list quarantine items
- save/list/get optimization findings
- default profile persistence and retrieval

If you create a new test project, keep it narrow and focused on storage/service persistence.

### 5. Reporting

Update `.planning/claude/CLAUDE-OUTBOX.md` with:

- exact task status
- files changed
- schema changes, if any
- tests added and whether they pass
- any intentionally deferred pieces

## Suggested Subagent Split

If you use Claude subagents, this split should map well:

### Subagent A: Repository implementation

- own SQLite writes/reads
- own compression/serialization use
- own summary query shape

### Subagent B: Service integration

- own DI registration
- own repository invocation points
- own non-UI orchestration changes

### Subagent C: Tests and verification

- own repository roundtrip tests
- own schema/setup helpers for tests
- own final build/test pass

## Success Criteria

This packet is successful when:

- the new repositories compile
- the service is wired to them
- persistence happens for the major live flows
- tests exist for the repository layer
- no WinUI or UI files were touched

## Notes From Codex

- The WinUI shell now has a deliberate `live service` vs `preview mode` split. That is intentional. Do not try to move execution into the app.
- I will keep owning the user-facing history surfaces and polish. What I need from Claude is real stored data coming from the service side so those views can become persistent next.
