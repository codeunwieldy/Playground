# Stack

## Primary Languages

- C# for all runtime projects under `src/`
- XAML for the WinUI shell under `src/Atlas.App/`
- XML for installer assets under `installer/`
- Markdown for specs, planning, and handoff documents under `spec/` and `.planning/`
- PowerShell for local command execution in the Codex environment

## Solution Shape

- `Atlas.sln` is the top-level .NET solution
- Shared build defaults live in `Directory.Build.props`
- Runtime projects live under `src/`
- Tests currently live under `tests/`

## Runtime Stack

- `.NET 8` is the baseline runtime across the solution
- `WinUI 3` is used for the native desktop client in `src/Atlas.App/Atlas.App.csproj`
- `Microsoft.WindowsAppSDK 1.8.260101001` is referenced by the app project
- `.NET Worker Service` is used for the privileged background process in `src/Atlas.Service/Atlas.Service.csproj`

## Core Libraries

- `protobuf-net` for pipe-envelope serialization in `src/Atlas.Core/Contracts/`
- `Microsoft.Data.Sqlite` for local storage bootstrap in `src/Atlas.Storage/`
- `Microsoft.Extensions.Hosting` and options/configuration in `src/Atlas.Service/Program.cs`
- `xUnit` for tests in `tests/Atlas.Core.Tests/`

## Platform Configuration

- The app targets `net8.0-windows10.0.19041.0`
- The app is currently configured as unpackaged, self-contained, and x64-focused in `src/Atlas.App/Atlas.App.csproj`
- The service currently targets plain `net8.0`

## Configuration Sources

- `src/Atlas.Service/appsettings.json`
- `src/Atlas.Service/appsettings.Development.json`
- Environment variables such as `OPENAI_API_KEY` and `ATLAS_OPENAI_MODEL`

## Missing or Planned Stack Pieces

- No repository layer exists yet above the SQLite bootstrapper
- No official VSS integration package or COM interop layer is present yet
- No realtime voice client library is wired into the app yet
- No CI pipeline or packaging automation is present in the repo yet
