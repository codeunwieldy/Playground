# Integrations

## External AI Integration

- `src/Atlas.AI/AtlasPlanningClient.cs` calls the OpenAI Responses API via `POST /v1/responses`
- Authentication is expected through `OPENAI_API_KEY` or `OPENAI_APIKEY`
- Model selection currently defaults to `gpt-5` unless `ATLAS_OPENAI_MODEL` is set
- There is a deterministic fallback planner when no API key is configured

## Windows Platform Integrations

- Drive enumeration uses `System.IO.DriveInfo` in `src/Atlas.Service/Services/FileScanner.cs`
- Registry startup inspection uses `Microsoft.Win32.Registry` in `src/Atlas.Service/Services/OptimizationScanner.cs`
- WinUI and Windows App SDK integration is defined in `src/Atlas.App/Atlas.App.csproj`

## Internal Service Integration

- The solution is designed around named-pipe IPC, with contracts defined in `src/Atlas.Core/Contracts/PipeContracts.cs`
- The service host is registered in `src/Atlas.Service/Program.cs`
- The current app-side pipe client lives in `src/Atlas.App/Services/AtlasPipeClient.cs`

## Storage Integration

- SQLite schema bootstrapping lives in `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
- Large structured payload compression helpers live in `src/Atlas.Storage/AtlasJsonCompression.cs`

## Installer and Deployment Integration

- WiX bundle and product stubs exist in `installer/Bundle.wxs` and `installer/Product.wxs`
- Service installation logic is not fully wired yet

## Planned Integrations Not Yet Implemented

- Realtime voice/transcription pipeline
- USN journal scanning for incremental NTFS updates
- VSS checkpoint orchestration for destructive batches
- Secure local secret protection beyond environment variables
- Formal eval harnesses and red-team corpus loaders
