# Testing

## Current Test Surface

- `tests/Atlas.Core.Tests/PolicyEngineTests.cs`
- `tests/Atlas.Core.Tests/RollbackPlannerTests.cs`

## Current Coverage Focus

- Protected-path blocking behavior
- Sync-folder exclusion behavior
- Safe-duplicate quarantine allowances
- Reverse-order inverse operation generation for rollback

## Verified Command

- `dotnet test Atlas.sln`

## Latest Observed Result

- Passed locally on 2026-03-11
- Total tests observed: 4
- Failures observed: 0

## Gaps

- No service integration tests
- No storage repository tests
- No named-pipe end-to-end tests
- No WinUI interaction or launch verification tests in the test project
- No red-team suites for prompt injection or destructive-language cases
- No performance or scale benchmarks

## Recommended Next Test Layers

1. Expand `Atlas.Core.Tests` for more policy and path-edge cases
2. Add service integration tests for scan, execute, undo, and quarantine behavior
3. Add repository tests once persistence moves beyond schema bootstrap
4. Add eval fixtures under `evals/` for unsafe plan generation and review gating
5. Add WinUI smoke verification for launch, navigation, and reduced-motion behavior
