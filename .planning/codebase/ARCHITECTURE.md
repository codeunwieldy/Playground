# Architecture

## Process Model

The codebase is organized as a multi-process Windows desktop product:

- `Atlas.App` is the native WinUI shell
- `Atlas.Service` is the privileged worker/service boundary
- `Atlas.Core` is the shared contracts and policy layer
- `Atlas.AI` is the remote-planning client and fallback heuristics layer
- `Atlas.Storage` is the local database bootstrap and artifact helper layer

## Current Control Flow

1. The app is expected to request scans, plans, execution, undo, and optimization work through named pipes
2. The service owns scanners, executors, optimization logic, and later recovery orchestration
3. Shared contracts travel through protobuf envelopes from `src/Atlas.Core/Contracts/PipeContracts.cs`
4. Policy validation happens in `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
5. Rollback checkpoints are derived by `src/Atlas.Core/Planning/RollbackPlanner.cs`

## Architectural Strengths

- The AI boundary is intentionally separated from execution
- Shared contracts are isolated in a clean core project
- Storage is already split from business logic
- The app and service separation fits the product risk model

## Architectural Gaps

- The app shell is mostly static and not yet bound to live data
- Named-pipe contracts exist, but the end-to-end UI-to-service workflow is still skeletal
- The scanner is still shallow and mostly extension/path based
- Persistence is only bootstrapped at the schema level
- Recovery orchestration is inverse-op based today; VSS is still planned

## Risk Boundaries

- The hard trust boundary currently depends on `AtlasPolicyEngine` plus disciplined use of the service
- Direct model execution does not exist in code today, which is good
- Some schema validation in the AI layer is still broad and should be tightened before advanced execution work

## Important Entry Points

- `src/Atlas.Service/Program.cs`
- `src/Atlas.App/App.xaml`
- `src/Atlas.App/MainWindow.xaml`
- `src/Atlas.AI/AtlasPlanningClient.cs`
- `src/Atlas.Core/Policies/AtlasPolicyEngine.cs`
