# Structure

## Top-Level Layout

- `src/` - runtime projects
- `tests/` - automated tests
- `installer/` - WiX assets
- `spec/` - lightweight product and handoff docs
- `.planning/` - GSD planning state and codebase map
- `evals/` - placeholder area for future eval assets

## Runtime Projects

### `src/Atlas.Core`

- `Contracts/DomainModels.cs` - core DSL, risk, inventory, checkpoint, and optimization models
- `Contracts/PipeContracts.cs` - named-pipe request/response contracts
- `Policies/` - path classification, default policy factory, and plan validation
- `Planning/RollbackPlanner.cs` - inverse operation generation

### `src/Atlas.AI`

- `AtlasPlanningClient.cs` - OpenAI Responses client and fallback planner
- `PromptCatalog.cs` - prompt strings
- `OpenAIOptions.cs` - configuration model

### `src/Atlas.Storage`

- `AtlasDatabaseBootstrapper.cs` - schema creation
- `AtlasJsonCompression.cs` - compressed artifact helpers
- `StorageOptions.cs` - storage configuration

### `src/Atlas.Service`

- `Program.cs` - DI and host bootstrap
- `Services/FileScanner.cs` - inventory and duplicate scan scaffold
- `Services/PlanExecutionService.cs` - executor and undo scaffold
- `Services/OptimizationScanner.cs` - optimization scan scaffold
- `Services/AtlasPipeServerWorker.cs` - pipe server host

### `src/Atlas.App`

- `MainWindow.xaml` - current top-level shell
- `Views/` - placeholder dashboard, plans, optimization, settings, and undo pages
- `Styles/AtlasTheme.xaml` - initial theme resources
- `Services/AtlasPipeClient.cs` - app-side service client scaffold

## Tests

- `tests/Atlas.Core.Tests/PolicyEngineTests.cs`
- `tests/Atlas.Core.Tests/RollbackPlannerTests.cs`

## Naming Patterns

- Projects use `Atlas.*`
- Most types use file-scoped namespaces
- Service helpers live under `Services/`
- Shared models are grouped by domain concern rather than one-type-per-file
