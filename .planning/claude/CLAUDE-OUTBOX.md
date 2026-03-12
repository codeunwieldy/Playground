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

### C-017 Incremental Composition Activation

- Task ID: C-017
- Status: **done**
- Files read:
  - `src/Atlas.Core/Scanning/IDeltaSource.cs`
  - `src/Atlas.Core/Scanning/DeltaResult.cs`
  - `src/Atlas.Core/Scanning/DeltaCapability.cs`
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `src/Atlas.Service/Services/DeltaSources/DeltaCapabilityDetector.cs`
  - `src/Atlas.Service/Services/DeltaSources/UsnJournalDeltaSource.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
  - `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
  - `.planning/claude/C-017-INCREMENTAL-COMPOSITION-ACTIVATION.md`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
- Files changed:
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs` — added `MaxIncrementalPaths` safety cap (default 500)
  - `src/Atlas.Service/Services/FileScanner.cs` — added `SnapshotVolumes()` (extracted drive enumeration to reusable static method) and `InspectFile()` (single-file inspection with policy/safety classification)
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs` — replaced `RunRescanForRootAsync` with incremental composition decision tree: `TryIncrementalCompositionAsync`, `FindBaselineSessionForRootAsync`, `RunFullRescanAsync`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — added `StubDeltaSource` test helper and 6 new tests

#### When Atlas now emits `IncrementalComposition`

Atlas emits `BuildMode=IncrementalComposition` when ALL of:
1. `deltaResult.RequiresFullRescan == false`
2. `deltaResult.ChangedPaths.Count > 0`
3. `deltaResult.ChangedPaths.Count <= MaxIncrementalPaths` (default 500)
4. A baseline session exists for the root with file count > 0

Composition logic: loads baseline files into a dictionary, re-inspects each changed path (upsert if exists, remove if deleted), gets fresh volume snapshots, persists as a complete session.

#### Fallback cases and provenance

| Scenario | BuildMode | IsTrusted | CompositionNote |
|---|---|---|---|
| Bounded delta + valid baseline | `IncrementalComposition` | `true` | Composition details (baseline ID, delta count, updated/removed counts) |
| No baseline session exists | `FullRescan` | `true` | "No baseline session found for this root; full rescan required." |
| Baseline has 0 files | `FullRescan` | `true` | "Baseline session {id} has no files; full rescan required." |
| Delta count > MaxIncrementalPaths | `FullRescan` | `true` | "Delta path count (N) exceeds MaxIncrementalPaths (M); full rescan required." |
| RequiresFullRescan=true | `FullRescan` | `true` | "Delta source requires full rescan: {reason}" |
| No specific changed paths | `FullRescan` | `true` | "Delta reported changes but no specific paths; full rescan." |
| Composition throws exception | `FullRescan` | `true` | "Full rescan fallback." |

`IsTrusted` stays `true` across all cases — either a complete incremental result is produced, or the system falls back entirely to a full rescan. Partial results are never persisted.

#### Tests added (6 new, all passing)

1. `ServiceOptions_MaxIncrementalPaths_HasReasonableDefault` — verifies default of 500
2. `Orchestration_IncrementalComposition_WhenBoundedDelta` — baseline + incremental → `IncrementalComposition`, `BaselineSessionId` populated
3. `Orchestration_IncrementalComposition_SetsCorrectFileCount` — 3 baseline + 1 added = 4 files
4. `Orchestration_IncrementalComposition_HandlesDeletedFiles` — 2 baseline, delete 1 = 1 file
5. `Orchestration_FallsBackToFullRescan_WhenNoBaseline` — no prior session → `FullRescan` with baseline note
6. `Orchestration_FallsBackToFullRescan_WhenDeltaExceedsMaxPaths` — 3 paths with cap=2 → `FullRescan` with overflow note

All 33 service tests and 127 storage tests pass. No UI files touched.

### C-016 Incremental Provenance Query APIs

- Task ID: C-016
- Status: **done**
- Files read:
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
  - `.planning/claude/C-013-SCAN-DIFF-AND-DRIFT-QUERY-APIS.md`
  - `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
  - `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
  - `.planning/claude/CLAUDE-INBOX.md`
  - `spec/HANDOFF.md`
  - `tests/Atlas.Storage.Tests/InventoryQueryTests.cs`
  - `tests/Atlas.Storage.Tests/InventoryRepositoryTests.cs`
  - `tests/Atlas.Storage.Tests/TestDatabaseFixture.cs`
  - `src/Atlas.Core/Scanning/DeltaCapability.cs`
  - `src/Atlas.Core/Scanning/DeltaResult.cs`
- Files changed:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added idempotent additive column migrations for 6 provenance columns on `scan_sessions` (trigger, build_mode, delta_source, baseline_session_id, is_trusted, composition_note); bootstrapper now handles duplicate-column errors safely for existing databases
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — added 6 provenance fields to `ScanSession` model (Trigger, BuildMode, DeltaSource, BaselineSessionId, IsTrusted, CompositionNote); extended `ScanSessionSummary` record with matching optional parameters
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — updated `SaveSessionAsync` INSERT to persist provenance columns; updated `GetSessionAsync` and `ListSessionsAsync` SELECT queries to read provenance columns and map them to `ScanSessionSummary`
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added 6 provenance fields (ProtoMember 8-13) to `InventorySnapshotResponse`, `InventorySessionDetailResponse`, and `InventorySessionSummary` (ProtoMember 7-12)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — updated 3 handlers (`HandleInventorySnapshotAsync`, `HandleInventorySessionsAsync`, `HandleInventorySessionDetailAsync`) to flow provenance through pipe; updated `HandleScanAsync` to set explicit Manual/FullRescan provenance on pipe-triggered sessions
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs` — updated `RunRescanForRootAsync` to accept `DeltaResult` and tag orchestration sessions with Trigger=Orchestration, BuildMode=FullRescan, and DeltaSource from the detected capability
  - `tests/Atlas.Storage.Tests/ProvenanceQueryTests.cs` — new file with 10 focused provenance tests

#### Exact provenance fields now queryable by Codex

| Field | Type | Description | Default |
|---|---|---|---|
| `Trigger` | string | `Manual` or `Orchestration` | `Manual` |
| `BuildMode` | string | `FullRescan` or `IncrementalComposition` | `FullRescan` |
| `DeltaSource` | string | `UsnJournal`, `Watcher`, `ScheduledRescan`, or empty | `""` |
| `BaselineSessionId` | string | ID of the baseline session used during composition, or empty | `""` |
| `IsTrusted` | bool | Whether Atlas trusts this as a complete session result | `true` |
| `CompositionNote` | string | Freeform note about composition/fallback/degradation | `""` |

#### Exposure approach

Provenance was exposed via **additive fields on existing contracts**. No new pipe routes were needed. The 3 existing inventory read routes now carry provenance:

- `inventory/snapshot` → `InventorySnapshotResponse` (fields 8-13)
- `inventory/sessions` → `InventorySessionSummary` (fields 7-12)
- `inventory/session-detail` → `InventorySessionDetailResponse` (fields 8-13)

All fields use safe protobuf-additive numbering. Older clients that don't read the new fields will silently ignore them.

#### What still remains deferred

- **Actual incremental session composition**: The orchestration worker currently always does full rescans and tags sessions accordingly. When C-015's incremental composition logic is fully integrated, sessions will start appearing with `BuildMode=IncrementalComposition` and populated `BaselineSessionId`/`DeltaSource`/`CompositionNote` fields. The read path is ready for that today.
- **Composition trust scoring**: `IsTrusted` is always `true` for now. When the orchestrator gains fallback-to-full-rescan degradation paths, untrusted sessions with explanatory `CompositionNote` will appear.

#### Tests added

10 new tests in `tests/Atlas.Storage.Tests/ProvenanceQueryTests.cs`:

1. `Snapshot_IncludesProvenance_ForLatestSession` — verifies all 6 provenance fields on latest snapshot
2. `SessionList_ReturnsProvenanceSummary` — verifies provenance on session list items
3. `SessionDetail_ReturnsBaselineLineage_WhenCompositionUsedOne` — verifies baseline linkage round-trip
4. `FullRescanSession_ReportsClearNonIncrementalProvenance` — verifies clean non-incremental defaults
5. `MissingSession_ReturnsNull` — typed missing-session behavior
6. `EmptyDatabase_SnapshotReturnsNull` — clean empty-state behavior
7. `EmptyDatabase_SessionList_ReturnsEmpty` — clean empty-state behavior
8. `UntrustedSession_ProvenanceRoundTrips` — untrusted session with composition note
9. `DefaultProvenance_ManualFullRescan` — legacy-style sessions get correct defaults
10. `InventorySessionSummaryDto_MapsAllProvenanceFields` — full DTO mapping verification

All 370 tests pass (121 Core + 57 AI + 127 Storage + 65 Service).

### C-014 Actual USN Journal Integration

- Task ID: C-014
- Status: **done**
- Files read:
  - `src/Atlas.Core/Scanning/IDeltaSource.cs`
  - `src/Atlas.Core/Scanning/DeltaCapability.cs`
  - `src/Atlas.Core/Scanning/DeltaResult.cs`
  - `src/Atlas.Service/Services/DeltaSources/UsnJournalDeltaSource.cs` (original stub)
  - `src/Atlas.Service/Services/DeltaSources/DeltaCapabilityDetector.cs`
  - `src/Atlas.Service/Services/DeltaSources/FileSystemWatcherDeltaSource.cs`
  - `src/Atlas.Service/Services/DeltaSources/ScheduledRescanDeltaSource.cs`
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `src/Atlas.Service/Program.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
  - `.planning/claude/C-012-DELTA-SCANNING-AND-RESCAN-ORCHESTRATION.md`
  - `.planning/claude/C-013-SCAN-DIFF-AND-DRIFT-QUERY-APIS.md`
  - `.planning/claude/C-014-ACTUAL-USN-JOURNAL-INTEGRATION.md`
  - `.planning/claude/CLAUDE-INBOX.md`
- Files changed:
  - `src/Atlas.Service/Services/DeltaSources/UsnJournalDeltaSource.cs` — replaced stub with real journal-backed implementation; fixed namespace from `Atlas.Core.Scanning` to `Atlas.Service.Services.DeltaSources`; added constructor dependencies on `IUsnJournalReader`, `IUsnCheckpointRepository`, and `ILogger`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added `usn_checkpoints` table to schema
  - `src/Atlas.Service/Program.cs` — registered `IUsnJournalReader`/`UsnJournalReader` and `IUsnCheckpointRepository`/`UsnCheckpointRepository`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — updated existing tests with test stubs for new constructor; added `NullUsnJournalReader`, `InMemoryUsnCheckpointRepository`, and `MockUsnJournalReader` test helpers
- Files created:
  - `src/Atlas.Service/Services/DeltaSources/Interop/UsnJournalInterop.cs` — P/Invoke declarations for `CreateFileW`, `DeviceIoControl` (2 overloads), `OpenFileById`, `GetFinalPathNameByHandleW`; native structs (`USN_JOURNAL_DATA_V1`, `READ_USN_JOURNAL_DATA_V0`, `FILE_ID_DESCRIPTOR`); IOCTL constants and USN_RECORD_V2 field offset documentation
  - `src/Atlas.Service/Services/DeltaSources/UsnJournalReader.cs` — `IUsnJournalReader` interface + `UsnJournalReader` implementation with volume handle management, bounded record reading loop (64KB buffer, 200K record cap), path resolution via `OpenFileById`/`GetFinalPathNameByHandle` with parent-directory cache, root-path filtering, and overflow detection
  - `src/Atlas.Storage/Repositories/IUsnCheckpointRepository.cs` — interface + `UsnCheckpoint` model for per-volume journal checkpoints
  - `src/Atlas.Storage/Repositories/UsnCheckpointRepository.cs` — SQLite-backed CRUD implementation
  - `tests/Atlas.Service.Tests/UsnJournalDeltaSourceTests.cs` — 11 focused tests with mocked reader
- What is now truly USN-backed:
  - `UsnJournalDeltaSource.IsAvailableForRootAsync` now probes actual journal access (NTFS check + `QueryJournal`), not just filesystem type
  - `UsnJournalDeltaSource.DetectChangesAsync` reads the USN change journal on supported NTFS volumes, returns bounded `ChangedPaths` when under 50K, and degrades safely to `RequiresFullRescan` on overflow, unresolvable records, journal reset, or read failure
  - Per-volume checkpoints are persisted in SQLite so the service resumes from the last-read USN between restarts
- What state is persisted:
  - `usn_checkpoints` table: `volume_id` (PK), `journal_id`, `last_usn`, `updated_utc`
  - One row per monitored volume; updated on each successful detection cycle
- What still remains deferred:
  - `RescanOrchestrationWorker` still runs full rescans even when `ChangedPaths` are available (incremental partial-rescan optimization is a future packet)
  - No Watcher-to-USN promotion at runtime (if USN becomes available mid-session)
  - No UI consumption of USN-specific signals
- Tests added and whether they pass:
  - 11 new tests in `UsnJournalDeltaSourceTests`: first-run baseline, journal ID change, journal wrap, no changes, changes under cap, overflow, unresolvable records, read failure (checkpoint not advanced), changes filtered by root, journal unavailable, fallback chain integration — **all 11 pass**
  - All 65 service tests pass (including updated existing delta scanning tests)
  - All 117 storage tests pass
  - Full solution builds with 0 errors
- No UI files were touched

### C-013 Scan Diff and Drift Query APIs

- Task ID: C-013
- Status: **done**
- Files read:
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `tests/Atlas.Storage.Tests/InventoryQueryTests.cs`
  - `tests/Atlas.Storage.Tests/InventoryRepositoryTests.cs`
  - `tests/Atlas.Storage.Tests/TestDatabaseFixture.cs`
  - `.planning/claude/C-013-SCAN-DIFF-AND-DRIFT-QUERY-APIS.md`
  - `.planning/claude/CLAUDE-INBOX.md`
  - `spec/HANDOFF.md`
- Files changed:
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — added `DiffSessionsAsync`, `GetDiffFilesAsync` interface methods + `SessionDiffSummary` and `SessionDiffFile` record types
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — implemented `DiffSessionsAsync` and `GetDiffFilesAsync` with SQLite-compatible UNION ALL diff queries
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added 3 request/response contract pairs and 1 summary DTO type for scan drift
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added 3 read-only drift route handlers + 3 routes in `RouteAsync`
  - `Atlas.sln` — added missing `Atlas.Storage.Tests` project reference
- Files created:
  - `tests/Atlas.Storage.Tests/ScanDriftTests.cs` — 18 new tests
- New message types added:
  - `inventory/drift-snapshot` → `inventory/drift-snapshot-response` (latest-vs-previous drift summary)
  - `inventory/session-diff` → `inventory/session-diff-response` (explicit session-vs-session diff with counts)
  - `inventory/session-diff-files` → `inventory/session-diff-files-response` (bounded changed file rows for drill-in)
- New contracts:
  - Requests: `DriftSnapshotRequest`, `SessionDiffRequest`, `SessionDiffFilesRequest`
  - Responses: `DriftSnapshotResponse`, `SessionDiffResponse`, `SessionDiffFilesResponse`
  - DTOs: `DiffFileSummary`
- New repository methods:
  - `IInventoryRepository.DiffSessionsAsync(olderSessionId, newerSessionId)` — returns `SessionDiffSummary` with added/removed/changed/unchanged counts
  - `IInventoryRepository.GetDiffFilesAsync(olderSessionId, newerSessionId, limit, offset)` — returns bounded `SessionDiffFile` rows ordered by path

#### Diff semantics (first-pass definition)

- **Added**: path exists in newer session but not in older session
- **Removed**: path exists in older session but not in newer session
- **Changed**: path exists in both sessions but `size_bytes` or `last_modified_unix` differ
- **Unchanged**: same path with identical `size_bytes` and `last_modified_unix`

This definition is path-stable: a file that moved from one location to another appears as Removed + Added (no rename tracking). This is intentional for safety and simplicity.

#### What drift the app can now query

- **Drift snapshot** (`inventory/drift-snapshot`): zero-argument request that auto-selects the two most recent scan sessions. Returns `HasBaseline=false` when fewer than two sessions exist. Otherwise returns added/removed/changed/unchanged counts plus both session IDs and timestamps.
- **Explicit session diff** (`inventory/session-diff`): compare any two session IDs. Returns `Found=false` if either session is missing. Otherwise returns full diff counts.
- **Diff file rows** (`inventory/session-diff-files`): bounded, paginated (limit: 1-500) changed file rows for review. Returns ChangeKind (Added/Removed/Changed) and size+timestamp for both sessions. Ordered deterministically by path.

#### Handler design notes

- All handlers are read-only, no mutations
- Drift snapshot fetches the two most recent sessions via `ListSessionsAsync(2, 0)` — no client-side session ID management needed
- Explicit diff and diff-files handlers validate both session IDs exist before computing diff
- File-level diff rows bounded by `Math.Clamp` (1-500)
- Empty databases and missing sessions return clean typed responses (`HasBaseline=false`, `Found=false`, empty lists)
- Nullable older/newer fields in `SessionDiffFile` are mapped to `0` in the protobuf DTO for transport safety

#### Tests added (18 new tests, all passing)

| Category | Count | Tests |
|----------|-------|-------|
| Diff counts - identical sessions | 1 | All unchanged |
| Diff counts - added file | 1 | Added count correct |
| Diff counts - removed file | 1 | Removed count correct |
| Diff counts - changed (size) | 1 | Changed when size differs |
| Diff counts - changed (modified) | 1 | Changed when last_modified differs |
| Diff counts - mixed | 1 | Added + removed + changed + unchanged all correct |
| Diff counts - empty sessions | 1 | Two empty sessions → all zeros |
| Diff counts - missing sessions | 1 | Nonexistent session IDs → all zeros |
| Diff files - added/removed/changed rows | 1 | Returns correct rows, excludes unchanged |
| Diff files - ordering | 1 | Ordered by path deterministically |
| Diff files - pagination | 1 | Respects limit and offset |
| Diff files - identical sessions | 1 | Returns empty (no changed rows) |
| Diff files - missing sessions | 1 | Returns empty |
| Drift snapshot - fewer than 2 sessions | 1 | No baseline available |
| Drift snapshot - two sessions | 1 | Produces correct diff |
| DTO mapping - DiffFileSummary | 1 | Maps correctly from SessionDiffFile |
| DTO mapping - DriftSnapshotResponse | 1 | Maps correctly from diff summary + sessions |
| Handler logic - missing session response | 1 | Returns Found=false |

#### Build status
- **Build**: 0 errors, 0 warnings
- **Core Tests**: 121 passed
- **AI Tests**: 57 passed
- **Storage Tests**: 117 passed (99 existing + 18 new)
- **Service Tests**: 54 passed
- **Total**: 349 tests passing

#### No UI files touched
- Zero changes in `src/Atlas.App/**`

#### Intentionally deferred
- Rename/move tracking across sessions (current diff uses path identity only — moved files show as Removed + Added)
- Content-hash-based diffing (would need fingerprint column in file_snapshots; path + size + modified is sufficient for first pass)
- Total diff file count endpoint (would need a COUNT query variant of the diff; pagination is available via bounded limit+offset)
- Drift retention/cleanup (old sessions and their diff data grow without pruning)
- Async bulk diff for very large sessions (current SQLite query is synchronous per call; may need streaming for 100K+ file sessions)

---

### C-012 Delta Scanning and Rescan Orchestration

- Task ID: C-012
- Status: **done**
- Files read:
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/AtlasStartupWorker.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `src/Atlas.Service/Program.cs`
  - `.planning/claude/C-010-INVENTORY-PERSISTENCE-FOUNDATIONS.md`
  - `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
  - `.planning/claude/C-012-DELTA-SCANNING-AND-RESCAN-ORCHESTRATION.md`
  - `.planning/claude/CLAUDE-INBOX.md`
- Files created:
  - `src/Atlas.Core/Scanning/DeltaCapability.cs` — capability enum (None, ScheduledRescan, Watcher, UsnJournal)
  - `src/Atlas.Core/Scanning/DeltaResult.cs` — result model for delta detection
  - `src/Atlas.Core/Scanning/IDeltaSource.cs` — delta source interface (Capability, IsAvailableForRootAsync, DetectChangesAsync)
  - `src/Atlas.Service/Services/DeltaSources/UsnJournalDeltaSource.cs` — USN journal seam (probes NTFS, defers actual reading)
  - `src/Atlas.Service/Services/DeltaSources/FileSystemWatcherDeltaSource.cs` — watcher fallback with overflow handling
  - `src/Atlas.Service/Services/DeltaSources/ScheduledRescanDeltaSource.cs` — always-full-rescan fallback
  - `src/Atlas.Service/Services/DeltaSources/DeltaCapabilityDetector.cs` — probes all sources, returns best available
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs` — bounded background worker for incremental rescans
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — 27 new tests
- Files modified:
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs` — added EnableRescanOrchestration, RescanInterval, MaxRootsPerCycle, OrchestrationCooldown
  - `src/Atlas.Service/Program.cs` — registered IDeltaSource implementations, DeltaCapabilityDetector, RescanOrchestrationWorker

#### New abstractions added

**1. Delta-source capability model (Atlas.Core.Scanning)**
- `DeltaCapability` enum: None < ScheduledRescan < Watcher < UsnJournal (ordered by preference)
- `DeltaResult`: root path, capability used, hasChanges, changedPaths, requiresFullRescan, reason
- `IDeltaSource` interface: Capability property, IsAvailableForRootAsync, DetectChangesAsync

**2. Delta source implementations (Atlas.Service.Services.DeltaSources)**
- `UsnJournalDeltaSource`: probes NTFS volumes, currently defers USN reading (returns RequiresFullRescan=true). The seam is ready for actual USN journal integration.
- `FileSystemWatcherDeltaSource`: starts FileSystemWatcher per root, tracks changed paths, handles buffer overflow gracefully. 64KB buffer, 10K path cap before overflow.
- `ScheduledRescanDeltaSource`: always available for existing roots, always returns RequiresFullRescan=true. Pure fallback.
- `DeltaCapabilityDetector`: probes all registered sources in descending priority order, returns best available. Also provides ProbeRootAsync for diagnostics.

**3. Rescan orchestration (Atlas.Service.Services.RescanOrchestrationWorker)**
- BackgroundService that runs on a configurable cooldown cycle
- Disabled by default (EnableRescanOrchestration = false)
- Per-cycle behavior: iterates configured roots, detects best delta source, checks rescan interval, triggers bounded rescans
- Respects MaxRootsPerCycle to prevent unbounded work
- Persists scan sessions through the existing IInventoryRepository.SaveSessionAsync path
- 10-second startup delay to let the database initialize
- Graceful shutdown on cancellation

**4. Service options additions**
- `EnableRescanOrchestration` (default: false) — must be explicitly enabled
- `RescanInterval` (default: 30 min) — minimum time between rescans of the same root
- `MaxRootsPerCycle` (default: 5) — cap on roots scanned per orchestration cycle
- `OrchestrationCooldown` (default: 5 min) — delay between cycles

#### What "delta-ready" means after this packet
- Atlas has a clear backend seam (IDeltaSource) for incremental scan observation
- Three delta sources are registered and probed in priority order
- USN journal probe identifies NTFS-capable volumes; actual reading is deferred
- FileSystemWatcher provides near-realtime change detection on local drives
- Unsupported roots degrade to safe bounded rescans via ScheduledRescanDeltaSource
- All rescans persist normal scan sessions through the existing inventory repository
- Orchestration is bounded by interval, per-cycle root cap, and cooldown
- No UI files were touched; no service boundary was weakened

#### What still remains deferred
- Actual USN journal reading (requires P/Invoke for DeviceIoControl; the seam and probe are ready)
- Delta-aware partial rescans (only rescanning changed paths instead of full root walk)
- Session diffing between scan sessions (comparing file_snapshots across two sessions)
- "What changed" query APIs for the app to consume
- Watcher lifecycle management for root additions/removals at runtime
- Retention/cleanup for orchestration-generated scan sessions
- Config exposure via appsettings.json (options are wired but no JSON examples added)

#### Tests added (27 new tests, all passing)

| Category | Count | Tests |
|----------|-------|-------|
| Capability model | 2 | Priority ordering (USN > Watcher > Scheduled > None), DeltaResult defaults |
| USN journal source | 3 | Non-existent root unavailable, detect returns full rescan, capability value |
| Watcher source | 5 | Non-existent root unavailable, existing root available, first detection needs full rescan, quiet second detection no changes, file creation detected |
| Scheduled rescan source | 4 | Existing root available, non-existent unavailable, always full rescan, capability value |
| Capability detector | 5 | No sources returns null, prefers best available, falls back to scheduled, probe report completeness, non-existent root returns none |
| Orchestration worker | 7 | Disabled by default no-op, cycle persists session, interval respected, max roots per cycle bounded, empty roots no-op, unresolvable root skipped, options defaults |

#### Build status
- **Build**: 0 errors, 0 warnings (excluding 4 pre-existing CA1416 in OptimizationScanner.cs)
- **Core Tests**: 121 passed
- **AI Tests**: 57 passed
- **Service Tests**: 54 passed (27 existing + 27 new)
- **Storage Tests**: 99 passed
- **Total**: 331 tests passing

#### No UI files touched
- Zero changes in `src/Atlas.App/**`

---

### C-011 Inventory Query APIs

- Task ID: C-011
- Status: **done**
- Files read:
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Program.cs`
  - `tests/Atlas.Storage.Tests/InventoryRepositoryTests.cs`
  - `tests/Atlas.Storage.Tests/HistoryQueryTests.cs`
  - `.planning/claude/C-011-INVENTORY-QUERY-APIS.md`
  - `.planning/claude/CLAUDE-INBOX.md`
  - `spec/HANDOFF.md`
- Files changed:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added 5 request/response contract pairs and 3 summary DTO types
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — added `GetSessionAsync` and `GetRootsForSessionAsync` interface methods
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — implemented `GetSessionAsync` and `GetRootsForSessionAsync`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added 5 read-only inventory route handlers + 5 routes in `RouteAsync`
  - `tests/Atlas.Storage.Tests/InventoryQueryTests.cs` — new file, 23 tests
- New message types added:
  - `inventory/snapshot` → `inventory/snapshot-response` (latest session summary for dashboard)
  - `inventory/sessions` → `inventory/sessions-response` (paginated session list, descending time order)
  - `inventory/session-detail` → `inventory/session-detail-response` (session with roots and volume detail)
  - `inventory/volumes` → `inventory/volumes-response` (volume snapshots for a session)
  - `inventory/files` → `inventory/files-response` (paginated file snapshots with total count)
- New contracts:
  - Requests: `InventorySnapshotRequest`, `InventorySessionListRequest`, `InventorySessionDetailRequest`, `InventoryVolumeListRequest`, `InventoryFileListRequest`
  - Responses: `InventorySnapshotResponse`, `InventorySessionListResponse`, `InventorySessionDetailResponse`, `InventoryVolumeListResponse`, `InventoryFileListResponse`
  - DTOs: `InventorySessionSummary`, `InventoryVolumeSummary`, `InventoryFileSummary`
- What inventory can now be queried:
  - Latest scan session summary (session_id, files_scanned, duplicate_group_count, root_count, volume_count, created_utc)
  - Recent scan sessions with pagination
  - Session detail with root paths and volume summaries
  - Volume snapshots for a given session (root_path, drive_format, drive_type, is_ready, total_size_bytes, free_space_bytes)
  - Paginated file snapshots for a session (path, name, extension, category, size_bytes, last_modified, sensitivity, sync, duplicate flags) with total count
  - All of the above through bounded read-only pipe contracts
- Repository gap-fills:
  - `GetSessionAsync(sessionId)` — direct session lookup by ID (avoids listing all sessions to find one)
  - `GetRootsForSessionAsync(sessionId)` — returns root paths for a session, sorted by path
- Tests: 23 new tests in `InventoryQueryTests.cs`, all passing. Cumulative: 99 storage tests, 304 total
- Design notes:
  - All handlers are read-only, no mutations
  - Result sizes bounded by `Math.Clamp` (sessions: 1-200, files: 1-1000)
  - Empty databases return clean empty responses (`HasSession = false` or empty lists)
  - Missing session detail returns `Found = false`
  - Timestamps serialized as ISO-8601 strings for safe protobuf transport
  - `InventorySnapshotResponse` uses a boolean `HasSession` flag for clean empty-state handling
  - File list response includes `TotalCount` for pagination support
- Intentionally deferred:
  - Session deletion and retention policies
  - Full-text search across file snapshots
  - Delta scanning queries (diffing file_snapshots between sessions)
  - File snapshot detail endpoint (full FileInventoryItem with content fingerprint and mime type)
  - These can be added as narrow follow-up packets when needed
- No `src/Atlas.App/**` files were touched

---

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

---

## C-008 Persisted History and Query APIs

- Task ID: C-008
- Status: **done**
- Files read:
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/Repositories/IPlanRepository.cs`
  - `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
  - `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs`
  - `src/Atlas.Storage/Repositories/PlanRepository.cs`
  - `.planning/claude/C-008-HISTORY-QUERY-APIS.md`
  - `.planning/claude/C-008-CODEX-TARGET.md`
  - `.planning/claude/CLAUDE-INBOX.md`
- Files changed:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added 7 request/response contract pairs and 5 summary DTO types
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added 7 read-only route handlers + 7 routes in `RouteAsync`
  - `tests/Atlas.Storage.Tests/HistoryQueryTests.cs` — new file, 20 tests
- New message types added:
  - `history/snapshot` → `history/snapshot-response` (compact all-domain snapshot)
  - `history/plans` → `history/plans-response` (paginated plan summaries)
  - `history/plan-detail` → `history/plan-detail-response` (full plan graph + linked batches)
  - `history/checkpoints` → `history/checkpoints-response` (paginated checkpoint summaries)
  - `history/quarantine` → `history/quarantine-response` (paginated quarantine summaries)
  - `history/findings` → `history/findings-response` (paginated finding summaries)
  - `history/traces` → `history/traces-response` (paginated prompt-trace summaries, filterable by stage)
- New contracts:
  - Requests: `HistorySnapshotRequest`, `HistoryListRequest`, `HistoryPlanDetailRequest`
  - Responses: `HistorySnapshotResponse`, `HistoryPlanListResponse`, `HistoryPlanDetailResponse`, `HistoryCheckpointListResponse`, `HistoryQuarantineListResponse`, `HistoryFindingListResponse`, `HistoryTraceListResponse`
  - DTOs: `HistoryPlanSummary`, `HistoryCheckpointSummary`, `HistoryQuarantineSummary`, `HistoryFindingSummary`, `HistoryTraceSummary`
- What history can now be queried:
  - Recent plans (scope, summary, created timestamp)
  - Plan detail (full PlanGraph + all batches for that plan)
  - Recent undo checkpoints (batch link, operation count, created timestamp)
  - Recent quarantine items (original path, reason, retention deadline)
  - Recent optimization findings (kind, target, auto-fix eligibility)
  - Recent prompt traces (stage, created timestamp; filterable by stage)
  - All of the above in a single compact snapshot request
- Tests: 20 new tests in `HistoryQueryTests.cs`, all passing. Cumulative: 62 storage tests, 27 service tests
- Design notes:
  - All handlers are read-only, no mutations
  - Result sizes bounded by `Math.Clamp` (snapshot: 1-50, list: 1-200)
  - Empty databases return clean empty lists
  - Missing plan detail returns `Found = false`
  - Timestamps serialized as ISO-8601 strings for safe protobuf transport
  - No repository interface changes needed — existing `List*` methods were sufficient
  - `HistoryListRequest` shared across all paginated routes (limit, offset, optional stage filter)
- Intentionally deferred:
  - Checkpoint detail endpoint (full UndoCheckpoint with inverse operations)
  - Quarantine detail endpoint (full QuarantineItem with content hash)
  - Finding detail endpoint (full OptimizationFinding with evidence + rollback plan)
  - Prompt trace detail endpoint (full PromptTrace with prompt/response payloads)
  - These can be added as narrow follow-up packets if Codex needs drill-in from the history workspace
- No `src/Atlas.App/**` files were touched

---

## C-010 Inventory Persistence Foundations

- Task ID: C-010
- Status: **done**
- Files read:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/Repositories/IPlanRepository.cs`
  - `src/Atlas.Storage/Repositories/SqliteConnectionFactory.cs`
  - `src/Atlas.Storage/AtlasJsonCompression.cs`
  - `src/Atlas.Service/Program.cs`
  - `.planning/claude/C-010-INVENTORY-PERSISTENCE-FOUNDATIONS.md`
  - `.planning/claude/CLAUDE-INBOX.md`
- Files changed:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added 4 tables + 1 index for inventory domain
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — new file, interface + ScanSession model + ScanSessionSummary record
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — new file, full SQLite implementation
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added IInventoryRepository constructor param, scan persistence in HandleScanAsync
  - `src/Atlas.Service/Program.cs` — registered IInventoryRepository DI binding
  - `tests/Atlas.Storage.Tests/InventoryRepositoryTests.cs` — new file, 14 tests
- Schema added:
  - `scan_sessions` — session header (session_id PK, files_scanned, duplicate_group_count, created_utc)
  - `scan_session_roots` — one-to-many roots per session (composite PK: session_id + root_path)
  - `scan_volumes` — one-to-many volumes per session (composite PK: session_id + root_path)
  - `file_snapshots` — individual file rows per session (composite PK: session_id + path)
  - `idx_file_snapshots_session` — index on session_id for efficient session queries
- Repository contracts added:
  - `IInventoryRepository.SaveSessionAsync` — bulk-inserts session header, roots, volumes, and file rows in a single transaction
  - `IInventoryRepository.GetLatestSessionAsync` — returns most recent session summary
  - `IInventoryRepository.ListSessionsAsync` — paginated session summaries in descending time order, includes root and volume counts
  - `IInventoryRepository.GetVolumesForSessionAsync` — returns volume snapshots for a session
  - `IInventoryRepository.GetFilesForSessionAsync` — paginated file snapshots for a session
  - `IInventoryRepository.GetFileCountForSessionAsync` — file count for a session
- What scan state is now persisted:
  - Every live scan through the pipe server persists a full scan session
  - Session header with file count and duplicate group count
  - All scanned root paths
  - All volume snapshots (root path, format, type, capacity, free space)
  - All file inventory items as individual queryable rows (path, name, extension, category, size, last modified, sensitivity, sync/duplicate flags)
  - Persistence failure degrades gracefully — scan response is still returned to the app
- Tests: 14 new tests in `InventoryRepositoryTests.cs`, all passing. Cumulative: 76 storage tests, 27 service tests
- Design notes:
  - File snapshots stored as individual rows (not compressed blobs) to support later delta scanning queries
  - All writes happen in a single SQLite transaction for atomicity
  - `ScanSessionSummary` includes derived counts (root_count, volume_count) via correlated subqueries
  - File snapshots are ordered by path for deterministic pagination
- Intentionally deferred:
  - USN journal integration and watcher orchestration (separate packet per Codex instruction)
  - Delta scanning logic (diffing file_snapshots between sessions)
  - Inventory read-side pipe contracts for the app (analogous to C-008 history routes)
  - Session deletion and retention policies
  - Duplicate group persistence (currently only the count is stored, not the full group data)
- No `src/Atlas.App/**` files were touched
