# Conventions

## C# Style

- File-scoped namespaces are used throughout the runtime projects
- Modern C# features such as primary constructors and collection expressions are already in use
- Shared domain models are centralized in large files like `src/Atlas.Core/Contracts/DomainModels.cs`
- Most service classes are `sealed`

## Dependency Injection

- The service uses `Host.CreateApplicationBuilder` and constructor-injected services
- Options binding is used for storage, service, and OpenAI settings in `src/Atlas.Service/Program.cs`
- The app is not yet using a full MVVM binding pattern

## Safety Patterns

- Policy evaluation is separated from execution
- Path normalization/classification is isolated in `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
- Rollback generation is separated from executor logic
- The fallback planner still stays inside the same operation DSL as the remote planner

## UI Conventions

- The current WinUI shell favors `Border`, `Grid`, `StackPanel`, and simple page composition
- Theme tokens live in `src/Atlas.App/Styles/AtlasTheme.xaml`
- The visual direction already leans toward Fluent materials with teal/copper accents and rounded cards

## Testing Conventions

- Tests currently focus on safety-critical core logic first
- xUnit is the active test framework
- Assertions are direct and behavior-oriented rather than snapshot-based

## Areas Without Strong Conventions Yet

- Repository abstractions and persistence patterns
- View-model structure and data-binding strategy for the app
- Logging, telemetry, and tracing standards
- Eval corpus format and prompt-trace review workflow
