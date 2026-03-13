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

### C-032 Plan History Lineage and Source Filters

- Task ID: C-032
- Status: **done**
- Files read:
  - `.planning/claude/C-032-PLAN-HISTORY-LINEAGE-AND-SOURCE-FILTERS.md`
  - `.planning/claude/C-031-DUPLICATE-CLEANUP-PLAN-PROMOTION-AND-PERSISTENCE.md`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Storage/Repositories/IPlanRepository.cs`
  - `src/Atlas.Storage/Repositories/PlanRepository.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanPromotionTests.cs`
  - `.planning/claude/CLAUDE-OUTBOX.md`
  - `.planning/claude/CLAUDE-INBOX.md`
  - `spec/HANDOFF.md`
- Files modified:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added 2 additive column migrations on `plans` table: `source TEXT NOT NULL DEFAULT ''`, `source_session_id TEXT NOT NULL DEFAULT ''`
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `Source` (ProtoMember 10) and `SourceSessionId` (ProtoMember 11) to `PlanGraph`
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `Source` + `SourceSessionId` to `HistoryPlanSummary` (ProtoMember 7, 8), `HistoryPlanDetailResponse` (ProtoMember 4, 5), and `SourceFilter` to `HistoryListRequest` (ProtoMember 4)
  - `src/Atlas.Storage/Repositories/IPlanRepository.cs` — extended `PlanSummary` record with `Source` + `SourceSessionId`, added `ListPlansAsync` overload with optional `sourceFilter` parameter
  - `src/Atlas.Storage/Repositories/PlanRepository.cs` — persists `source` and `source_session_id` columns in `SavePlanAsync`, reads them in `ListPlansAsync`, implements filtered list query
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — wires lineage into `HandleHistorySnapshotAsync`, `HandleHistoryPlansAsync` (with source filter), `HandleHistoryPlanDetailAsync`, and sets `Source`/`SourceSessionId` on promoted plans in `HandlePromoteDuplicateCleanupPlanAsync`
- Files created:
  - `tests/Atlas.Service.Tests/PlanHistoryLineageTests.cs` — 8 tests for lineage metadata and source filtering
- Lineage fields added:
  - `PlanGraph.Source` — plan origin identifier (e.g. `"DuplicateCleanupPromotion"` or empty for normal plans)
  - `PlanGraph.SourceSessionId` — inventory session ID the plan was promoted from (empty for normal plans)
  - Schema: `plans.source`, `plans.source_session_id` columns with empty-string defaults for backward compatibility
- Query/filter behavior added:
  - `HistoryListRequest.SourceFilter` — when non-empty, `history/plans` route returns only plans matching the source
  - `HistoryPlanSummary` and `HistoryPlanDetailResponse` include `Source` + `SourceSessionId`
  - `history/snapshot` plan summaries also include lineage metadata
  - Older plans with no lineage metadata are readable with empty-string defaults
- Tests added and passing (8/8):
  - `PromotedPlan_PersistsLineageMetadata` — promoted plan has `Source="DuplicateCleanupPromotion"` and `SourceSessionId` set
  - `NormalPlan_HasEmptyLineage` — plan saved through normal flow has empty `Source`
  - `ListPlans_ReturnsLineageInSummary` — `ListPlansAsync` populates `Source` and `SourceSessionId`
  - `ListPlans_SourceFilter_ReturnOnlyMatchingPlans` — filter returns only promoted plans
  - `ListPlans_SourceFilter_EmptyResultsHandled` — filter for non-existent source returns empty list
  - `ListPlans_NoFilter_ReturnsAllPlans` — no filter returns both normal and promoted plans
  - `OlderPlans_ReadableWithEmptyLineage` — plans saved before lineage columns have empty-string defaults
  - `PlanDetail_SurfacesLineageTruth` — `GetPlanAsync` returns plan with lineage fields
- Full test suite: 167 tests, all passing (including 9 existing C-031 promotion tests)

### C-035 Conversation Compaction and Retention Summaries

- Task ID: C-035
- Status: **done**
- Files read:
  - `.planning/claude/C-035-CONVERSATION-COMPACTION-AND-RETENTION-SUMMARIES.md`
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs`
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
- Files created:
  - `src/Atlas.Service/Services/ConversationCompactionService.cs` — deterministic, local-first compaction service
  - `tests/Atlas.Service.Tests/ConversationCompactionTests.cs` — 9 tests
- Files changed:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added `conversation_summaries` table + index, additive `is_compacted` and `message_count` columns on `conversations`
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs` — added `GetCompactableCandidatesAsync`, `SaveSummaryAsync`, `MarkCompactedAsync`, `GetSummariesForConversationAsync` + new types `ConversationSummaryRecord`, `CompactableConversation`
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs` — implemented the four new methods, updated `SaveConversationAsync` to populate `message_count` and honor caller-supplied `CreatedUtc`
- Key findings:
  - Compaction is fully deterministic and local-first — no AI model calls required
  - Summary generation extracts first/last messages + keyword frequency analysis, bounded to 1000 chars
  - `is_compacted` flag prevents re-processing; idempotent across multiple runs
  - Original conversations are preserved — compaction only adds summary metadata
  - Older rows without `message_count`/`is_compacted` get default values (0) and remain readable
- Tests (9 total, all passing):
  - `CompactableCandidate_Selected` — old conversation with enough messages is found
  - `RecentConversation_NotSelected` — within retention window, skipped
  - `ShortConversation_NotSelected` — below message threshold, skipped
  - `AlreadyCompacted_NotSelected` — compacted conversation is skipped
  - `Compaction_GeneratesSummary` — summary saved with correct metadata
  - `Compaction_MarksConversationCompacted` — is_compacted=1 after compaction
  - `RepeatedCompaction_Idempotent` — second run finds nothing to compact
  - `Summary_TruncatedToBound` — long conversation produces summary <= 1000 chars
  - `BackwardCompatibility_OlderRowsReadable` — conversations without new columns still work

### C-034 Safe Optimization Fix Application and Rollback

- Task ID: C-034
- Status: **done**
- Files read:
  - `.planning/claude/C-034-SAFE-OPTIMIZATION-FIX-APPLICATION-AND-ROLLBACK.md`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `src/Atlas.Service/Services/OptimizationScanner.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
- Files created:
  - `src/Atlas.Service/Services/SafeOptimizationFixExecutor.cs` — applies/reverts optimization fixes with rollback states
  - `tests/Atlas.Service.Tests/SafeOptimizationFixTests.cs` — 20 tests
- Files changed:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `OptimizationRollbackState` (ProtoContract), `OptimizationFixResult`, `UndoCheckpoint.OptimizationRollbackStates` (ProtoMember 11)
  - `src/Atlas.Service/Services/PlanExecutionService.cs` — replaced inline ApplyOptimizationFix with SafeOptimizationFixExecutor delegation, added RevertOptimizationFix via stored rollback state, undo path uses checkpoint rollback states
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` — updated constructor to pass SafeOptimizationFixExecutor
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — updated constructor to pass SafeOptimizationFixExecutor
- Key findings:
  - Safe optimization fix kinds: TemporaryFiles (delete), CacheCleanup (delete), DuplicateArchives (quarantine), UserStartupEntry (registry disable)
  - Blocked kinds: ScheduledTask, BackgroundApplication, LowDiskPressure — conservative posture
  - TemporaryFiles/CacheCleanup are non-reversible (repopulate naturally); DuplicateArchives and UserStartupEntry are reversible
  - Windows registry access gated behind `OperatingSystem.IsWindows()` + `[SupportedOSPlatform("windows")]`
  - Rollback state stored as JSON in `OptimizationRollbackState.RollbackData`

### C-033 VSS Checkpoint Eligibility and Metadata Foundations

- Task ID: C-033
- Status: **done**
- Files read:
  - `.planning/claude/C-033-VSS-CHECKPOINT-ELIGIBILITY-AND-METADATA-FOUNDATIONS.md`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `src/Atlas.Core/Planning/RollbackPlanner.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
- Files created:
  - `src/Atlas.Service/Services/CheckpointEligibilityEvaluator.cs` — static deterministic evaluator
  - `tests/Atlas.Service.Tests/CheckpointEligibilityTests.cs` — 13 tests
- Files changed:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `UndoCheckpoint.CheckpointEligibility` (ProtoMember 7), `EligibilityReason` (8), `CoveredVolumes` (9), `VssSnapshotCreated` (10)
  - `src/Atlas.Service/Services/PlanExecutionService.cs` — evaluates checkpoint eligibility after preflight (both live and dry-run), populates metadata on UndoCheckpoint, adds VSS deferred note when Required
- Key findings:
  - `CheckpointRequirement` enum: `NotNeeded`, `Recommended`, `Required`
  - Required when: >= 5 destructive ops, cross-volume operations, or untrusted session
  - Recommended when: any destructive op exists (DeleteToQuarantine, MergeDuplicateGroup, unsafe ApplyOptimizationFix)
  - NotNeeded when: all operations are safe (CreateDirectory, MovePath, RenamePath, TemporaryFiles/CacheCleanup fixes)
  - No VSS snapshot actually created (deferred to a later packet) — only eligibility evaluation and metadata stored
  - All rules are deterministic and bounded, no randomness

### Full test suite after parallel wave: 649 tests, all passing
- Atlas.Core.Tests: 208
- Atlas.AI.Tests: 74
- Atlas.Storage.Tests: 158
- Atlas.Service.Tests: 209

### C-031 Duplicate Cleanup Plan Promotion and Persistence

- Task ID: C-031
- Status: **done**
- Files read:
  - `.planning/claude/C-031-DUPLICATE-CLEANUP-PLAN-PROMOTION-AND-PERSISTENCE.md`
  - `.planning/claude/C-030-DUPLICATE-CLEANUP-PLAN-MATERIALIZATION.md`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/Repositories/IPlanRepository.cs`
  - `src/Atlas.Storage/Repositories/PlanRepository.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanMaterializationTests.cs`
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanPreviewTests.cs`
  - `.planning/claude/CLAUDE-OUTBOX.md`
  - `.planning/claude/CLAUDE-INBOX.md`
  - `spec/HANDOFF.md`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `PromoteDuplicateCleanupPlanRequest` (SessionId, MaxGroups, MaxOperationsPerGroup), `PromoteDuplicateCleanupPlanResponse` (Found, Promoted, SavedPlanId, IsNewlyPromoted, IncludedGroupCount, BlockedGroupCount, TotalPlannedOperations, ConfidenceThresholdUsed, Rationale, RollbackPosture, Scope, Categories, DegradedReasons, SourceSessionId)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `inventory/duplicate-cleanup-plan-promote` route and `HandlePromoteDuplicateCleanupPlanAsync` handler; extracted shared `BuildPlanGraphFromCore` helper from C-030 materialization handler (C-030 behavior unchanged, all C-030 tests pass)
- Files created:
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanPromotionTests.cs` — 9 tests for plan promotion
- New route: `inventory/duplicate-cleanup-plan-promote` → `inventory/duplicate-cleanup-plan-promote-response`
- How saved retained-cleanup plans now appear in standard plan history:
  - Plans appear with `Scope="Duplicate Cleanup"` in `ListPlansAsync` / `history/plans`
  - `Rationale` stored in the `summary` column for list display
  - Full `PlanGraph` retrievable via `GetPlanAsync` / `history/plan-detail` using `SavedPlanId`
  - Each `PlanOperation` carries `GroupId` linking back to source duplicate group
  - `Categories = ["DuplicateCleanup"]` identifies the plan type for filtering
  - `RollbackStrategy` preserves quarantine-first posture
  - `RequiresReview = true` ensures human review before execution
- Tests added and passing (9/9):
  - `Promote_ValidSession_SavesPlanToRepository` — verifies plan persists and is retrievable
  - `Promote_DegradedSession_RefusesPromotion` — trust gate blocks degraded sessions
  - `Promote_AllBlocked_RefusesPromotion` — all-blocked groups refuse promotion
  - `Promote_MissingSession_ReturnsNotFound` — missing session returns Found=false
  - `Promote_SavedPlan_VisibleInPlanHistory` — promoted plan appears in ListPlansAsync
  - `Promote_RepeatedPromotion_DeterministicContent` — two promotions produce same content, different PlanIds
  - `Promote_BoundedLimits_Enforced` — group count respects MaxGroups limit
  - `Promote_LineageTruth_PreservedInSavedPlan` — saved plan has Rationale, RollbackStrategy, Categories, GroupIds
  - `Promote_NoDuplicates_RefusesPromotion` — no-duplicate session refuses promotion
- All 8 C-030 materialization tests still pass (no regressions from helper extraction)
- All 8 C-029 plan preview tests still pass

### C-030 Duplicate Cleanup Plan Materialization

- Task ID: C-030
- Status: **done**
- Files read:
  - `.planning/claude/C-030-DUPLICATE-CLEANUP-PLAN-MATERIALIZATION.md`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanPreviewTests.cs`
  - `.planning/claude/CLAUDE-OUTBOX.md`
  - `.planning/claude/CLAUDE-INBOX.md`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `MaterializeDuplicateCleanupPlanRequest` (SessionId, MaxGroups, MaxOperationsPerGroup), `MaterializeDuplicateCleanupPlanResponse` (Found, CanMaterialize, MaterializedPlanId, Plan, IncludedGroupCount, BlockedGroupCount, TotalPlannedOperations, ConfidenceThresholdUsed, Rationale, RollbackPosture, IncludedGroups, BlockedGroups, DegradedReasons)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `inventory/duplicate-cleanup-plan-materialize` route and `HandleMaterializeDuplicateCleanupPlanAsync` handler; refactored C-029 handler core logic into shared `BuildDuplicateCleanupPlanPreviewCoreAsync` helper and `DuplicateCleanupPlanPreviewCore` record
- Files created:
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanMaterializationTests.cs` — 8 tests for plan materialization
- New route: `inventory/duplicate-cleanup-plan-materialize` → `inventory/duplicate-cleanup-plan-materialize-response`
- Materialized review fields now available:
  - `Found` — whether the retained session exists
  - `CanMaterialize` — whether the session can be materialized (false if degraded, all groups blocked, or no duplicates)
  - `MaterializedPlanId` — stable GUID for this materialization
  - `Plan` — standard `PlanGraph` with Scope, Rationale, Categories, Operations, RiskSummary, EstimatedBenefit, RequiresReview, RollbackStrategy
  - `IncludedGroupCount` / `BlockedGroupCount` — group partition counts
  - `TotalPlannedOperations` — total operations across included groups
  - `ConfidenceThresholdUsed` — policy confidence threshold applied
  - `Rationale` / `RollbackPosture` — bounded text summaries
  - `IncludedGroups` / `BlockedGroups` — reuses C-029 types for group-level detail
  - `DegradedReasons` — list of reasons materialization is blocked or degraded
- Design rationale:
  - `PlanGraph` and the duplicate preview shape diverge (group-first vs flat ops, no risk envelope, string-typed fields). The materialization response carries *both* a standard `PlanGraph` (for `ApplyPlanState` rendering) and the group-level summary (for duplicate-specific review). This gives the app maximum flexibility without forcing one shape into the other.
  - Trust gate blocks materialization for degraded sessions (`IsTrusted=false`), consistent with C-019 trust-aware gating.
  - Core plan preview logic extracted into a shared helper to avoid C-029/C-030 duplication. C-029 handler is now a thin wrapper around the shared core. All 8 C-029 tests continue to pass.
- Tests added (8 total, all passing):
  - `Materialize_ValidSession_ProducesPlanGraph` — successful materialization returns CanMaterialize=true, non-empty PlanGraph, stable MaterializedPlanId
  - `Materialize_AllBlocked_CannotMaterialize` — all-blocked session returns CanMaterialize=false with degraded reasons
  - `Materialize_DegradedSession_CannotMaterialize` — untrusted session returns CanMaterialize=false with trust-related degraded reason
  - `Materialize_MissingSession_ReturnsNotFound` — missing session returns Found=false
  - `Materialize_NoDuplicates_CannotMaterialize` — session with zero duplicate groups returns CanMaterialize=false
  - `Materialize_BoundedLimits_Enforced` — MaxGroups limit is respected
  - `Materialize_RationaleAndRollback_CarryThrough` — rationale and rollback posture populated on both response and PlanGraph
  - `Materialize_PlanGraph_ContainsCorrectOperations` — PlanGraph operations match included groups exactly with correct GroupId, Kind, and Confidence

### C-029 Duplicate Cleanup Plan Preview APIs

- Task ID: C-029
- Status: **done**
- Files read:
  - `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
  - `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `.planning/claude/C-027-DUPLICATE-CLEANUP-PREVIEW-APIS.md`
  - `.planning/claude/C-028-DUPLICATE-CLEANUP-BATCH-PREVIEW-APIS.md`
  - `.planning/claude/CLAUDE-OUTBOX.md`
  - `tests/Atlas.Service.Tests/DuplicateCleanupBatchPreviewTests.cs`
  - `tests/Atlas.Service.Tests/DuplicateCleanupPreviewTests.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `DuplicateCleanupPlanPreviewRequest` (SessionId, MaxGroups, MaxOperationsPerGroup), `DuplicateCleanupPlanPreviewResponse` (Found, IncludedGroupCount, BlockedGroupCount, TotalPlannedOperations, ConfidenceThresholdUsed, Rationale, RollbackPosture, IncludedGroups, BlockedGroups), `PlanPreviewIncludedGroup` (GroupId, CanonicalPath, CleanupConfidence, OperationCount, Operations, ActionNotes), `PlanPreviewBlockedGroup` (GroupId, CanonicalPath, CleanupConfidence, RecommendedPosture, BlockedReasons)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `inventory/duplicate-cleanup-plan-preview` route and `HandleDuplicateCleanupPlanPreviewAsync` handler
- Files created:
  - `tests/Atlas.Service.Tests/DuplicateCleanupPlanPreviewTests.cs` — 8 integration tests for plan preview composition
- New route: `inventory/duplicate-cleanup-plan-preview` → `inventory/duplicate-cleanup-plan-preview-response`
- Plan-preview fields now available:
  - `Found` — whether the retained session exists
  - `IncludedGroupCount` — number of groups eligible and included in the plan
  - `BlockedGroupCount` — number of groups blocked by policy
  - `TotalPlannedOperations` — total cleanup operations across all included groups
  - `ConfidenceThresholdUsed` — policy confidence threshold applied
  - `Rationale` — bounded rationale summary for the plan
  - `RollbackPosture` — rollback-oriented notes for review surfaces
  - `IncludedGroups` — list of eligible groups with per-group operations, canonical path, confidence, and action notes
  - `BlockedGroups` — list of blocked groups with per-group blocked reasons, recommended posture, and canonical path
- Tests added (8 total, all passing):
  - `PlanPreview_EligibleGroups_ProduceBoundedPlan` — eligible groups produce bounded plan with operations
  - `PlanPreview_BlockedGroups_StayOutOfIncludedSet` — blocked groups are separated truthfully
  - `PlanPreview_MixedSession_TruthfulCounts` — included vs blocked counts match list lengths
  - `PlanPreview_MissingSession_ReturnsNotFound` — missing session returns Found=false
  - `PlanPreview_NoDuplicates_ReturnsEmptyPlan` — empty session returns zero counts
  - `PlanPreview_BoundedGroupLimit_Enforced` — MaxGroups limit is respected
  - `PlanPreview_RationaleAndRollback_ArePopulated` — rationale and rollback posture are non-empty
  - `PlanPreview_AllBlocked_NoIncludedGroups` — all-blocked session has zero included groups
- No UI files touched
- No new shared-core helpers needed; reused `SafeDuplicateCleanupPlanner`, `DuplicateActionEvaluator`, and `CleanupOperationPreview` directly

### C-026 Duplicate Action Eligibility and Review APIs

- Task ID: C-026
- Status: **done**
- Files read:
  - `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
  - `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
  - `src/Atlas.Core/Scanning/DuplicateCanonicalSelector.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `.planning/claude/C-022-DUPLICATE-EVIDENCE-AND-CONFIDENCE.md`
  - `.planning/claude/C-025-DUPLICATE-GROUP-DETAIL-AND-EVIDENCE-APIS.md`
  - `.planning/claude/CLAUDE-OUTBOX.md`
- Files modified:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `DuplicateActionPosture` enum (Keep, Review, QuarantineDuplicates)
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `DuplicateActionReviewRequest` (SessionId, GroupId) and `DuplicateActionReviewResponse` (Found, IsCleanupEligible, RequiresReview, RecommendedPosture, BlockedReasons, ActionNotes, GroupId, ConfidenceThresholdUsed)
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — added `GetFilesForPathsAsync` method
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — implemented `GetFilesForPathsAsync` (indexed point queries per path)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `inventory/duplicate-action-review` route and `HandleDuplicateActionReviewAsync` handler
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` — updated `TrustedInventoryStub` with new interface method
- Files created:
  - `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs` — stateless posture derivation from `SafeDuplicateCleanupPlanner` output
  - `tests/Atlas.Core.Tests/DuplicateActionEvaluatorTests.cs` — 9 unit tests for posture derivation
  - `tests/Atlas.Storage.Tests/DuplicateActionEligibilityTests.cs` — 5 integration tests for `GetFilesForPathsAsync`
- New route: `inventory/duplicate-action-review` → `inventory/duplicate-action-review-response`
- Decision-support fields now available:
  - `Found` — whether the retained group exists
  - `IsCleanupEligible` — whether the group passes all policy gates
  - `RequiresReview` — whether human review is needed before cleanup
  - `RecommendedPosture` — `Keep`, `Review`, or `QuarantineDuplicates`
  - `BlockedReasons` — bounded list of why cleanup is blocked (sensitive members, sync-managed, protected, low confidence, missing inventory)
  - `ActionNotes` — bounded list explaining the recommendation
  - `ConfidenceThresholdUsed` — the policy threshold applied
- Design: Reuses `SafeDuplicateCleanupPlanner.BuildOperations` as the sole rules engine. `DuplicateActionEvaluator` is a pure static result interpreter, not a second cleanup brain. The group-level `HasProtectedMembers` flag compensates for `file_snapshots` not persisting `IsProtectedByUser`.
- Tests: 14 new tests (9 core + 5 storage), all passing. Full suite: 560 tests, 0 failures.

### C-025 Duplicate Group Detail and Evidence APIs

- Task ID: C-025
- Status: **done**
- Files read:
  - `src/Atlas.Core/Scanning/DuplicateGroupAnalyzer.cs`
  - `src/Atlas.Core/Scanning/DuplicateCanonicalSelector.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `tests/Atlas.Storage.Tests/DuplicatePersistenceTests.cs`
  - `tests/Atlas.Storage.Tests/TestDatabaseFixture.cs`
  - `.planning/claude/C-025-DUPLICATE-GROUP-DETAIL-AND-EVIDENCE-APIS.md`
- Files modified:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `DuplicateEvidenceEntry` class (Signal, Detail) and `Evidence` list field (ProtoMember 11) on `DuplicateGroup`
  - `src/Atlas.Service/Services/FileScanner.cs` — populated `Evidence` from `DuplicateGroupAnalyzer.Analyze()` result on yielded `DuplicateGroup`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added `duplicate_group_evidence` table (session_id, group_id, signal, detail; PK on session_id + group_id + signal)
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — added `GetDuplicateGroupDetailAsync(sessionId, groupId)` method, `PersistedDuplicateGroupDetail` record (all group fields + evidence + members), `PersistedDuplicateEvidence` record
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — persists evidence rows in `SaveSessionAsync` transaction; implements `GetDuplicateGroupDetailAsync` with 3-query read (header → members → evidence)
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `DuplicateGroupDetailRequest`, `DuplicateGroupDetailResponse`, `DuplicateEvidenceSummary` contracts
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `inventory/duplicate-detail` route and `HandleDuplicateDetailAsync` handler
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` — added `GetDuplicateGroupDetailAsync` stub method to `TrustedInventoryStub`
- Files created:
  - `tests/Atlas.Storage.Tests/DuplicateDetailTests.cs` — 8 tests covering evidence round-trip, all group fields, member ordering, missing session/group, empty evidence, evidence bounded count, multiple groups isolation
- New route:
  - `inventory/duplicate-detail` → `inventory/duplicate-detail-response` — bounded read-only drill-in for one retained duplicate group
- New contracts:
  - `DuplicateGroupDetailRequest` (SessionId, GroupId)
  - `DuplicateGroupDetailResponse` (Found, GroupId, CanonicalPath, MatchConfidence, CleanupConfidence, CanonicalReason, MaxSensitivity, HasSensitiveMembers, HasSyncManagedMembers, HasProtectedMembers, MemberPaths, MemberCount, Evidence)
  - `DuplicateEvidenceSummary` (Signal, Detail)
- Schema addition:
  - `duplicate_group_evidence` table: normalized per-signal rows keyed on (session_id, group_id, signal); bounded to ≤7 rows per group from the analyzer's fixed rule set
- Duplicate evidence now queryable:
  - `FullHashMatch` / `QuickHashOnly` — hash verification level
  - `FingerprintAgreement` — all members share identical content header fingerprint
  - `SizeMatch` — all members same size
  - `ProtectedMember` — group contains user-protected files
  - `CriticalSensitivity` / `SensitiveMember` — group contains sensitive files
  - `SyncManagedMember` — group contains sync-managed files
- Tests: 8 tests in `DuplicateDetailTests.cs`, all passing:
  - Evidence round-trip persistence
  - All group fields returned correctly
  - Member paths ordered by path
  - Missing session returns null
  - Missing group ID returns null
  - Empty evidence list returns empty
  - Evidence count bounded (≤ 10)
  - Multiple groups — only requested group returned
- Full test suite: **546 tests** (199 Core + 74 AI + 153 Storage + 120 Service), **0 failures**
- No `src/Atlas.App/**` files were touched
- Risks or questions: None. The evidence was already computed by `DuplicateGroupAnalyzer` but discarded; this packet persists it through the existing scan transaction and exposes it through a bounded read route.

### C-024 File Inspection and Sensitivity Explainability APIs

- Task ID: C-024
- Status: **done**
- Files read:
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Core/Scanning/SensitivityScorer.cs`
  - `src/Atlas.Core/Scanning/ContentSniffer.cs`
  - `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Service/Services/OptimizationScanner.cs`
  - `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
  - `.planning/claude/C-021-SENSITIVITY-SCORING-AND-EVIDENCE.md`
  - `.planning/claude/C-024-FILE-INSPECTION-AND-SENSITIVITY-EXPLAINABILITY-APIS.md`
- Files modified:
  - `tests/Atlas.Service.Tests/FileInspectionTests.cs` — fixed `ExcludedPaths` → `ExcludedRoots` property bug, added 4 payload-boundedness and contract-safety tests (total: 15 tests, all passing)
- Files already in place (from prior scaffolding):
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — `FileInspectionRequest`, `FileInspectionResponse`, `SensitivityEvidenceSummary`, `SessionFileDetailRequest`, `SessionFileDetailResponse` contracts
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — `inventory/inspect-file` and `inventory/file-detail` routes + handler implementations
  - `src/Atlas.Service/Services/FileScanner.cs` — `InspectFileDetailed` method with outcome discrimination
- New routes:
  - `inventory/inspect-file` → `inventory/inspect-file-response` — live on-demand file inspection (read-only)
  - `inventory/file-detail` → `inventory/file-detail-response` — persisted session file lookup by session ID + path (read-only)
- Explainability fields now available through `FileInspectionResponse`:
  - `Found` (bool) — whether the file was found and inspectable
  - `Outcome` (string) — one of: `Inspected`, `Missing`, `Protected`, `Excluded`, `AccessDenied`
  - `Path`, `Name`, `Extension` — normalized file identity
  - `Category`, `MimeType` — MIME/category truth (sniff-first, extension fallback)
  - `ContentSniffSucceeded` (bool) — whether magic-byte detection worked
  - `HasContentFingerprint` (bool) — whether a header fingerprint was produced
  - `SizeBytes`, `LastModifiedUnixTimeSeconds` — file metadata
  - `Sensitivity` (enum) — `Unknown`, `Low`, `Medium`, `High`, `Critical`
  - `SensitivityEvidence` (list of `SensitivityEvidenceSummary`) — each with `Signal` and `Detail`
  - `IsSyncManaged`, `IsDuplicateCandidate` — posture flags
- Tests: 15 tests in `tests/Atlas.Service.Tests/FileInspectionTests.cs`, all passing:
  - Inspectable file returns identity, MIME/category, sniff status
  - Content sniff success and failure paths
  - Sensitivity evidence with multiple rules (bounded count asserted)
  - Critical, high, and low sensitivity levels
  - Missing file → `Found = false`
  - Protected path → `Protected` outcome
  - Excluded path → `Excluded` outcome
  - Empty path → `Missing` outcome
  - Size/time population
  - All string fields non-null on success
  - Duplicate candidate posture flag
- No `src/Atlas.App/**` files were touched
- Risks or questions: None. The contracts, routes, and handlers were already scaffolded in prior packets; this session verified correctness, fixed a test bug, and completed the test coverage required by the packet.

### C-023 Persisted Duplicate Review and Query APIs

- Task ID: C-023
- Status: **done**
- Files read:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
  - `.planning/claude/C-022-DUPLICATE-EVIDENCE-AND-CONFIDENCE.md`
- Files created:
  - `tests/Atlas.Storage.Tests/DuplicatePersistenceTests.cs` — 14 tests covering round-trip, confidence, risk flags, member paths, pagination, ordering, session isolation, and empty-session behavior
- Files modified:
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added `duplicate_groups` table (normalized header with all evidence fields), `duplicate_group_members` table (member paths per group), and session index
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs` — added `DuplicateGroups` property to `ScanSession`, added `GetDuplicateGroupsForSessionAsync` and `GetDuplicateGroupCountForSessionAsync` methods, added `PersistedDuplicateGroup` record
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs` — implemented duplicate group persistence in `SaveSessionAsync` transaction and two new read methods
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `SessionDuplicateListRequest`, `SessionDuplicateListResponse`, and `DuplicateGroupSummary` contracts
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `inventory/session-duplicates` route and handler; wired `DuplicateGroups` into `HandleScanAsync` session save
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs` — wired `DuplicateGroups` into `RunFullRescanAsync` session save

#### Schema design

Normalized two-table design:

| Table | Columns | PK |
|---|---|---|
| `duplicate_groups` | session_id, group_id, canonical_path, match_confidence, cleanup_confidence, canonical_reason, max_sensitivity, has_sensitive_members, has_sync_managed_members, has_protected_members | (session_id, group_id) |
| `duplicate_group_members` | session_id, group_id, member_path | (session_id, group_id, member_path) |

Index: `idx_duplicate_groups_session` on `duplicate_groups(session_id)`.

#### Duplicate fields now persisted and queryable

| Field | Type | Description |
|---|---|---|
| `group_id` | TEXT | Unique group identifier |
| `canonical_path` | TEXT | Path selected as canonical |
| `match_confidence` | REAL | Hash-based match confidence (from C-022 analyzer) |
| `cleanup_confidence` | REAL | Risk-adjusted cleanup confidence |
| `canonical_reason` | TEXT | Human-readable rationale for canonical selection |
| `max_sensitivity` | INTEGER | Highest sensitivity level in the group |
| `has_sensitive_members` | INTEGER | Group contains sensitive files |
| `has_sync_managed_members` | INTEGER | Group contains sync-managed files |
| `has_protected_members` | INTEGER | Group contains user-protected files |
| `member_path` (members table) | TEXT | Individual member paths |

#### Read-side API

- Route: `inventory/session-duplicates` → `inventory/session-duplicates-response`
- Request: `SessionDuplicateListRequest` (SessionId, Limit, Offset)
- Response: `SessionDuplicateListResponse` (Found, TotalCount, Groups)
- DTO: `DuplicateGroupSummary` with all evidence fields + member paths + member count
- Pagination bounded by `Math.Clamp(1, 500)`
- Results ordered by cleanup confidence descending (highest-confidence groups first)
- Missing session returns `Found=false`

#### Persistence continuity

- **Manual scans** (`HandleScanAsync`): duplicate groups from `ScanResponse.Duplicates` are now included in session save
- **Orchestration full rescans** (`RunFullRescanAsync`): duplicate groups from rescan response are now included in session save
- **Incremental composition** (`TryIncrementalCompositionAsync`): carries `DuplicateGroupCount` from baseline (no live groups — this is correct since groups are not re-computed during composition)

#### Test results

- 14 new tests in `DuplicatePersistenceTests`, all passing
- 519 total tests across all projects (141 Storage + 105 Service + 199 Core + 74 AI), 0 failures
- No UI files touched

---

### C-022 Duplicate Evidence and Confidence

- Task ID: C-022
- Status: **done**
- Files read:
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Scanning/DuplicateCanonicalSelector.cs`
  - `src/Atlas.Core/Scanning/ContentSniffer.cs`
  - `src/Atlas.Core/Scanning/SensitivityScorer.cs`
  - `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
  - `tests/Atlas.Core.Tests/DuplicateCanonicalSelectorTests.cs`
  - `tests/Atlas.Core.Tests/SensitivityScorerTests.cs`
  - `tests/Atlas.Service.Tests/FileScannerTests.cs`
  - `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
  - `.planning/claude/C-021-SENSITIVITY-SCORING-AND-EVIDENCE.md`
- Files created:
  - `src/Atlas.Core/Scanning/DuplicateGroupAnalyzer.cs` — reusable static analyzer with evidence-friendly result model; computes match confidence (hash-based), cleanup confidence (risk-adjusted), canonical rationale, and risk flags
  - `tests/Atlas.Core.Tests/DuplicateGroupAnalyzerTests.cs` — 23 tests covering all confidence, evidence, and canonical-reason behavior
- Files modified:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added 6 additive fields to `DuplicateGroup`: `CanonicalReason`, `HasSensitiveMembers`, `HasSyncManagedMembers`, `HasProtectedMembers`, `MaxSensitivity`, `MatchConfidence`
  - `src/Atlas.Service/Services/FileScanner.cs` — replaced hardcoded `Confidence = 0.995d` with `DuplicateGroupAnalyzer.Analyze()` integration; populates all new evidence fields on `DuplicateGroup`

#### Confidence model

| Evidence Level | MatchConfidence | Notes |
|---|---|---|
| Full SHA-256 hash match | 0.999 base | Boosted to 0.9995 when content fingerprints also agree |
| Quick hash only (partial) | 0.85 base | For future use; current pipeline always verifies full hash |

#### Cleanup confidence risk penalties

| Risk Factor | Penalty | Effect |
|---|---|---|
| Protected member in group | -0.10 | Strongly conservative when user has explicitly protected a file |
| Critical sensitivity member | -0.15 | Very conservative with credential/key material |
| High/Medium sensitivity member | -0.08 | Conservative with sensitive documents |
| Sync-managed member | -0.04 | Moderate caution with cloud-synced files |

Penalties stack. CleanupConfidence is clamped to [0.0, MatchConfidence].

#### Canonical reason explanation

The analyzer explains why the canonical file was chosen using signals: user-protected, sync-managed, high sensitivity, has content fingerprint, preferred location, stable location, or fallback to highest composite safety score.

#### Risk data now available on DuplicateGroup

- `MatchConfidence` — pure hash-based match strength (unaffected by risk)
- `Confidence` — risk-adjusted cleanup confidence (formerly hardcoded 0.995)
- `CanonicalReason` — human-readable explanation of canonical selection
- `HasSensitiveMembers`, `HasSyncManagedMembers`, `HasProtectedMembers` — boolean risk flags
- `MaxSensitivity` — highest sensitivity level in the group

#### Test results

- 23 new tests in `DuplicateGroupAnalyzerTests`, all passing
- 505 total tests across all projects, 0 failures
- No UI files touched

---

### C-021 Sensitivity Scoring and Evidence

- Task ID: C-021
- Status: **done**
- Files read:
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Scanning/ContentSniffer.cs`
  - `src/Atlas.Core/Policies/PolicyProfileFactory.cs`
  - `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
  - `tests/Atlas.Service.Tests/ContentSniffingTests.cs`
  - `tests/Atlas.Service.Tests/FileScannerTests.cs`
  - `tests/Atlas.Core.Tests/DuplicateCanonicalSelectorTests.cs`
  - `.planning/claude/C-021-SENSITIVITY-SCORING-AND-EVIDENCE.md`
- Files created:
  - `src/Atlas.Core/Scanning/SensitivityScorer.cs` — reusable static scorer with evidence-friendly result model; combines path, filename, extension, category, and MIME signals
  - `tests/Atlas.Core.Tests/SensitivityScorerTests.cs` — 45 tests covering all classification rules
- Files modified:
  - `src/Atlas.Service/Services/FileScanner.cs` — replaced inline `ClassifySensitivity` with `SensitivityScorer.Classify`; extracted `category` and `mimeType` into local variables in `ClassifyFile` so they flow into the scorer

#### Sensitivity signals used

| Priority | Signal | Level | Source |
|----------|--------|-------|--------|
| 1 | CriticalExtension | Critical | `.kdbx`, `.kdb`, `.pfx`, `.p12`, `.pem`, `.key`, `.jks`, `.keystore`, `.ppk` |
| 2a | PolicyKeyword | High | `profile.ProtectedKeywords` (user-configurable; defaults: passport, tax, medical, contract, payroll, bank, identity, recovery, password) |
| 2b | SensitivePathSegment | High | Built-in: finance, legal, medical, payroll, identity, personnel, insurance, accounting, confidential |
| 2c | SensitiveFilenameTerm | High | Built-in: w-2, w2, 1099, ssn, paystub, pay-stub, pay_stub, bank-statement, bank_statement, tax-return, tax_return, nda, deed, mortgage |
| 3a | DocumentCategory | Medium | Category in {Documents, Spreadsheets, Presentations} (from C-020 content sniffing or extension fallback) |
| 3b | ArchiveCategory | Medium | Category == "Archives" |
| 3c | DatabaseExtension | Medium | `.db`, `.sqlite`, `.sqlite3`, `.mdb`, `.accdb` |
| 4 | (default) | Low | No evidence matched |

All rules are evaluated and all matching evidence is collected. The maximum severity level wins.

#### Evidence model

```csharp
public sealed record SensitivityEvidence(string Signal, string Detail);
public sealed record SensitivityResult(SensitivityLevel Level, IReadOnlyList<SensitivityEvidence> Evidence);
```

`FileScanner` consumes only `result.Level` for `FileInventoryItem.Sensitivity`. The full `SensitivityResult` (with evidence) is available for future explainability or diagnostic endpoints without any contract changes.

#### What is now content-aware vs still heuristic

- **Content-aware**: The `category` parameter passed to the scorer comes from `ContentSniffer.Sniff()` when available (e.g., a `.dat` file containing PDF magic bytes gets category "Documents" → Medium, not Low). This means C-020's content truth directly strengthens sensitivity decisions.
- **Still heuristic**: Path segments, filename terms, and extension matching are string-based heuristics. They are intentionally conservative (over-classify rather than under-classify). The evidence trail makes it clear why a file was promoted.

#### What changed from the old `ClassifySensitivity`

- Fixed priority bug: `.kdbx`/`.pfx` now correctly classified as Critical regardless of keyword matches
- Added 7 more Critical extensions (`.kdb`, `.p12`, `.pem`, `.key`, `.jks`, `.keystore`, `.ppk`)
- Added built-in path segment matching (9 terms) as a safety net beyond user-configured keywords
- Added filename term matching (14 terms) for specific document patterns like W-2, 1099, SSN
- Added Medium tier: documents, spreadsheets, presentations, archives, and database files are no longer Low
- Added evidence collection for all signals
- Content-sniffed category now influences sensitivity decisions

#### Tests added (45 total, all passing)

| Category | Count | Tests |
|----------|-------|-------|
| Critical extensions | 9 | Theory across all 9 extensions |
| High from policy keywords | 3 | passport, tax, bank in path/filename |
| High from path segments | 7 | Theory across finance, legal, medical, insurance, personnel, accounting, confidential |
| High from filename terms | 9 | Theory across w-2, 1099, ssn, nda, mortgage, paystub, bank-statement, tax-return, deed |
| Medium from category | 5 | Documents, Spreadsheets, Presentations, Archives, database extension |
| Low default | 4 | image, audio, video, unknown |
| Priority and evidence | 4 | Critical beats High, High beats Medium, evidence accumulates, empty keywords uses built-in rules |
| Content-aware | 2 | Unknown ext with Document category → Medium, txt ext with Images category → Low |
| Edge cases | 2 | Null category, empty path |

Total test count: 176 Core tests passed, 101 Service tests passed, 0 failed.

### C-020 Content Sniffing and MIME Detection

- Task ID: C-020
- Status: **done**
- Files read:
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Policies/PolicyProfile.cs`
  - `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
  - `.planning/claude/C-020-CONTENT-SNIFFING-AND-MIME-DETECTION.md`
- Files created:
  - `src/Atlas.Core/Scanning/ContentSniffer.cs` — static utility with bounded header-based content sniffing for 10 file families, ZIP sub-sniffing for Office Open XML, and SHA-256 header fingerprint (first 8KB)
  - `tests/Atlas.Service.Tests/ContentSniffingTests.cs` — 16 tests covering all format detection, fallback, mismatch, fingerprint, and integration scenarios
- Files modified:
  - `src/Atlas.Service/Services/FileScanner.cs` — extracted shared `ClassifyFile` method used by both `InspectFile` and `EnumerateRoot`; wired `ContentSniffer.Sniff` with extension-based fallback

#### Content sniffing behavior

`ContentSniffer.Sniff(filePath)` reads at most 68 bytes from the file header to detect file type via magic bytes, then computes a SHA-256 fingerprint of the first 8KB. Returns `null` when the file cannot be opened, is empty, or doesn't match any known signature.

**Signature table:**

| Family | Magic bytes | MIME | Category |
|--------|------------|------|----------|
| PDF | `%PDF` (0x25504446) | `application/pdf` | Documents |
| PNG | `\x89PNG\r\n\x1A\n` | `image/png` | Images |
| JPEG | `\xFF\xD8\xFF` | `image/jpeg` | Images |
| GIF | `GIF87a` or `GIF89a` | `image/gif` | Images |
| WebP | `RIFF....WEBP` | `image/webp` | Images |
| WAV | `RIFF....WAVE` | `audio/wav` | Audio |
| ZIP | `PK\x03\x04` | `application/zip` | Archives |
| ZIP (Office) | `PK\x03\x04` + `word/`/`xl/`/`ppt/` filename | Office MIME types | Documents/Spreadsheets/Presentations |
| MP3 (ID3) | `ID3` | `audio/mpeg` | Audio |
| MP3 (sync) | `\xFF\xFB`/`\xFF\xFA`/`\xFF\xF3`/`\xFF\xF2` | `audio/mpeg` | Audio |
| MP4/MOV | bytes 4-7 = `ftyp` | `video/mp4` | Video |

#### Mismatch policy

Content signal always wins over extension when sniffing succeeds. A PNG header in a `.txt` file will be classified as `image/png` / Images. When sniffing returns null (unknown content), the extension-based fallback is used.

#### FileScanner deduplication

Previously, `InspectFile` and `EnumerateRoot` duplicated inline classification logic. Both now call a shared `ClassifyFile(PolicyProfile, FileInfo)` method that:
1. Calls `ContentSniffer.Sniff(info.FullName)`
2. If non-null: uses sniffed MIME, category, and header fingerprint
3. If null: falls back to extension-based category + extension string as MIME + empty fingerprint

#### Tests added (16 total, all passing)

| Test | Validates |
|---|---|
| `Sniff_DetectsPdf` | PDF magic → `application/pdf` / Documents |
| `Sniff_DetectsPng` | PNG magic → `image/png` / Images |
| `Sniff_DetectsJpeg` | JPEG magic → `image/jpeg` / Images |
| `Sniff_DetectsGif` | GIF89a magic → `image/gif` / Images |
| `Sniff_DetectsWebP` | RIFF+WEBP magic → `image/webp` / Images |
| `Sniff_DetectsZip` | PK magic with generic filename → `application/zip` / Archives |
| `Sniff_DetectsMp3WithId3` | ID3 magic → `audio/mpeg` / Audio |
| `Sniff_DetectsWav` | RIFF+WAVE magic → `audio/wav` / Audio |
| `Sniff_DetectsMp4` | ftyp box → `video/mp4` / Video |
| `Sniff_ReturnsNull_ForUnknownContent` | Unknown content → null |
| `Scanner_FallsBackToExtension_WhenSniffFails` | `.txt` with plain text → extension fallback |
| `Scanner_ContentWins_WhenExtensionMismatches` | PNG header in `.txt` → sniff wins |
| `Sniff_ReturnsNull_ForEmptyFile` | Empty file → null |
| `Sniff_PopulatesFingerprint_ForKnownFile` | SHA-256 hex fingerprint, 64 chars |
| `Sniff_Fingerprint_BoundedToFirstEightKb` | Files differing after 8KB have identical fingerprints |
| `InspectFile_UsesSniffer` | PDF via InspectFile → correct MIME and fingerprint |
| `ScanAsync_UsesSniffer` | PNG via full scan → correct MIME and fingerprint |

Total test count: 101 passed, 0 failed.

### C-019 Trust-Aware Plan Gating

- Task ID: C-019
- Status: **done**
- Files read:
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
  - `.planning/claude/C-018-UNTRUSTED-SESSION-STATES-AND-PARTIAL-DEGRADATION.md`
- Files changed:
  - `src/Atlas.Service/Services/PlanExecutionService.cs` — added `IInventoryRepository` constructor dependency; added trust gate at top of `ExecuteAsync` that blocks live execution when the latest inventory session has `IsTrusted=false`, while allowing preview/dry-run to proceed normally
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added inventory trust check in `HandlePlanAsync` after policy validation; when latest session is degraded, sets `RequiresReview=true`, adds a blocked reason explaining the degradation, and elevates `ApprovalRequirement` to `Review`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — added 5 C-019 tests covering all trust gating scenarios
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` — updated constructor call to pass `TrustedInventoryStub`; added `TrustedInventoryStub` class so existing tests remain unaffected by the new dependency

#### Planning-time behavior for degraded retained scans

When `HandlePlanAsync` runs and the latest inventory session has `IsTrusted=false`:
1. `response.Plan.RequiresReview` is set to `true`
2. A blocked reason is appended to `RiskSummary.BlockedReasons`: "Retained inventory session is degraded (IsTrusted=false). Plan accuracy may be affected. A full rescan is recommended before execution."
3. `RiskSummary.ApprovalRequirement` is elevated to at least `ApprovalRequirement.Review`
4. If the trust check itself throws, the error is logged as a warning and plan generation continues (fail-open for read, conservative for the user)

When the latest session is trusted or no session exists, behavior is completely unchanged.

#### Execution-time behavior for degraded retained scans

When `PlanExecutionService.ExecuteAsync` runs:
- If `request.Execute == true` (live execution) and latest session has `IsTrusted=false`: returns `ExecutionResponse { Success = false }` with message explaining the block and recommending a full rescan
- If `request.Execute == false` (preview/dry-run): proceeds normally regardless of trust state
- If no session exists or session is trusted: proceeds normally (existing behavior preserved)

#### Additive contract changes

None. All gating flows through existing surfaces:
- `PlanGraph.RequiresReview` (existing bool)
- `RiskEnvelope.BlockedReasons` (existing list)
- `RiskEnvelope.ApprovalRequirement` (existing enum)
- `ExecutionResponse.Success` and `ExecutionResponse.Messages` (existing fields)

The only structural change is adding `IInventoryRepository` as a constructor parameter to `PlanExecutionService`, which is resolved automatically via DI.

#### Tests added (all pass)

| Test | Validates |
|---|---|
| `Execution_LiveBlocked_WhenLatestSessionDegraded` | Live execution returns `Success=false` with explanation when `IsTrusted=false` |
| `Execution_PreviewAvailable_WhenLatestSessionDegraded` | Dry-run succeeds normally even with degraded session |
| `Execution_LiveProceeds_WhenLatestSessionTrusted` | Live execution succeeds when session is trusted |
| `Execution_BlockedReason_IsStableAndTruthful` | Blocked message contains "full rescan" and "Preview/dry-run remains available" |
| `Execution_LiveProceeds_WhenNoSessionExists` | Live execution succeeds when no inventory session exists |

Total test count: 82 passed, 0 failed.

### C-018 Untrusted Session States and Partial Degradation

- Task ID: C-018
- Status: **done**
- Files read:
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs`
  - `src/Atlas.Service/Services/FileScanner.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `src/Atlas.Core/Scanning/DeltaResult.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Core/Policies/PathSafetyClassifier.cs`
  - `.planning/claude/C-015-INCREMENTAL-SESSION-COMPOSITION.md`
  - `.planning/claude/C-016-INCREMENTAL-PROVENANCE-QUERY-APIS.md`
  - `.planning/claude/C-017-INCREMENTAL-COMPOSITION-ACTIVATION.md`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
- Files changed:
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs` — added `MaxDegradedRatio` option (default 0.5) controlling the threshold above which a degraded composition is abandoned in favor of a full rescan
  - `src/Atlas.Service/Services/RescanOrchestrationWorker.cs` — upgraded `TryIncrementalCompositionAsync` to distinguish three inspection outcomes (updated, removed, failed) and to emit `IsTrusted=false` degraded sessions, abandon composition when failure ratio exceeds threshold, and populate precise degradation notes
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — added 6 C-018 tests and a `CreateWorkerWithProfile` helper
- Cases that now emit `IsTrusted=false`:
  - Incremental composition where some delta paths could not be refreshed (file exists on disk but `InspectFile` returned null due to I/O error, protection, exclusion, or permissions) **and** the failure ratio is ≤ `MaxDegradedRatio`
  - Baseline entries are retained for failed paths; `CompositionNote` includes `DEGRADED:` prefix with path count, cause, and follow-up guidance
- Cases that still force full rescan (`IsTrusted=true`):
  - `RequiresFullRescan=true` from delta source
  - Delta path count exceeds `MaxIncrementalPaths`
  - No baseline session found
  - Baseline session has 0 files
  - Delta reported changes but no specific paths
  - Composition throws an exception (catch block)
  - **New**: Failure ratio exceeds `MaxDegradedRatio` (too many paths failed → abandon degraded session, fall back to full rescan)
- Tests added (6 new, all passing):
  1. `Orchestration_TrustedIncrementalSession_WhenAllPathsResolved` — complete composition stays `IsTrusted=true`
  2. `Orchestration_DegradedSession_WhenSomePathsCannotBeInspected` — partial failures produce `IsTrusted=false` with `DEGRADED` note
  3. `Orchestration_DegradedNote_RoundTripsThrough_SessionDetailApi` — degraded provenance round-trips through snapshot/session/detail APIs
  4. `Orchestration_ForcesFullRescan_WhenTooManyPathsFail` — high failure ratio forces full rescan instead of degraded persistence
  5. `Orchestration_BaselineLinkageTruthful_InDegradedCase` — baseline ID and delta source remain correct in degraded sessions
  6. `ServiceOptions_MaxDegradedRatio_HasReasonableDefault` — config default is 0.5
- Full test suite: **77 tests, all passing** (Atlas.Service.Tests)
- No UI files touched
- No schema or pipe contract changes required (existing `IsTrusted` and `CompositionNote` fields carry the new semantics)

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

### C-027 Duplicate Cleanup Preview APIs

- Task ID: C-027
- Status: **done**
- Files read:
  - `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
  - `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `src/Atlas.Storage/Repositories/InventoryRepository.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `.planning/claude/C-025-DUPLICATE-GROUP-DETAIL-AND-EVIDENCE-APIS.md`
  - `.planning/claude/C-026-DUPLICATE-ACTION-ELIGIBILITY-AND-REVIEW-APIS.md`
  - `.planning/claude/C-027-DUPLICATE-CLEANUP-PREVIEW-APIS.md`
  - `tests/Atlas.Storage.Tests/DuplicateDetailTests.cs`
  - `tests/Atlas.Storage.Tests/DuplicateActionEligibilityTests.cs`
  - `tests/Atlas.Core.Tests/SafeDuplicateCleanupPlannerTests.cs`
  - `tests/Atlas.Core.Tests/DuplicateActionEvaluatorTests.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `DuplicateCleanupPreviewRequest` (SessionId, GroupId), `DuplicateCleanupPreviewResponse` (Found, IsPreviewAvailable, RecommendedPosture, CanonicalPath, Operations, BlockedReasons, ActionNotes, GroupId, ConfidenceThresholdUsed, OperationCount), `CleanupOperationPreview` (SourcePath, Kind, Description, Confidence, Sensitivity)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added route `inventory/duplicate-cleanup-preview` -> `inventory/duplicate-cleanup-preview-response` with `HandleDuplicateCleanupPreviewAsync` handler and `MaxCleanupPreviewOperations = 50` constant
- New files:
  - `tests/Atlas.Service.Tests/DuplicateCleanupPreviewTests.cs` — 7 tests + `CleanupPreviewTestFixture`
- New route: `inventory/duplicate-cleanup-preview`
- New contracts:
  - `DuplicateCleanupPreviewRequest` — SessionId, GroupId
  - `DuplicateCleanupPreviewResponse` — Found, IsPreviewAvailable, RecommendedPosture, CanonicalPath, Operations (List\<CleanupOperationPreview\>), BlockedReasons, ActionNotes, GroupId, ConfidenceThresholdUsed, OperationCount
  - `CleanupOperationPreview` — SourcePath, Kind, Description, Confidence, Sensitivity
- Cleanup preview fields now available:
  - `IsPreviewAvailable` — whether preview operations were produced
  - `RecommendedPosture` — carried through from evaluator (Keep, Review, QuarantineDuplicates)
  - `CanonicalPath` — the path Atlas would keep
  - `Operations` — bounded list of quarantine previews (SourcePath, Kind, Description, Confidence, Sensitivity)
  - `OperationCount` — total operation count (may exceed bounded list size)
  - `BlockedReasons` — why preview is unavailable when blocked
  - `ActionNotes` — supplementary notes when preview is available
  - `ConfidenceThresholdUsed` — the policy threshold used for evaluation
- Tests: 7 tests, all passing
  - `CleanupPreview_EligibleGroup_ReturnsBoundedOperations` — eligible group produces quarantine operations
  - `CleanupPreview_ReviewGroup_ReturnsNoPreview` — sensitive group returns no preview with blocked reasons
  - `CleanupPreview_LowConfidence_ReturnsNoPreview` — low-confidence group returns Keep posture
  - `CleanupPreview_MissingSession_ReturnsNotFound` — unknown session returns null
  - `CleanupPreview_MissingGroup_ReturnsNotFound` — unknown group returns null
  - `CleanupPreview_CanonicalPathTruthful` — canonical path excluded from operations
  - `CleanupPreview_OperationsBounded` — large group capped at 50 operations
- Full test suite: 567 tests, all passing, 0 regressions
- No new repository methods or storage tables were needed
- Reused `SafeDuplicateCleanupPlanner` and `DuplicateActionEvaluator` as-is
- No `src/Atlas.App/**` files were touched

### C-028 Duplicate Cleanup Batch Preview APIs

- Task ID: C-028
- Status: **done**
- Files read:
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Planning/SafeDuplicateCleanupPlanner.cs`
  - `src/Atlas.Core/Planning/DuplicateActionEvaluator.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/Repositories/IInventoryRepository.cs`
  - `.planning/claude/C-028-DUPLICATE-CLEANUP-BATCH-PREVIEW-APIS.md`
  - `tests/Atlas.Service.Tests/DuplicateCleanupPreviewTests.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `DuplicateCleanupBatchPreviewRequest` (SessionId, MaxGroups, MaxOperationsPerGroup), `DuplicateCleanupBatchPreviewResponse` (Found, GroupsEvaluated, GroupsPreviewable, GroupsBlocked, TotalOperationCount, ConfidenceThresholdUsed, Groups), `BatchGroupPreviewSummary` (GroupId, CanonicalPath, IsPreviewable, RecommendedPosture, OperationCount, BlockedReasons, ActionNotes, CleanupConfidence)
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added route `inventory/duplicate-cleanup-batch-preview` -> `inventory/duplicate-cleanup-batch-preview-response` with `HandleDuplicateCleanupBatchPreviewAsync` handler and `MaxBatchPreviewGroups = 50`, `MaxBatchPreviewOpsPerGroup = 20` constants
- New files:
  - `tests/Atlas.Service.Tests/DuplicateCleanupBatchPreviewTests.cs` — 7 tests + `BatchPreviewTestFixture`
- New route: `inventory/duplicate-cleanup-batch-preview`
- New contracts:
  - `DuplicateCleanupBatchPreviewRequest` — SessionId, MaxGroups (default 50), MaxOperationsPerGroup (default 20)
  - `DuplicateCleanupBatchPreviewResponse` — Found, GroupsEvaluated, GroupsPreviewable, GroupsBlocked, TotalOperationCount, ConfidenceThresholdUsed, Groups (List\<BatchGroupPreviewSummary\>)
  - `BatchGroupPreviewSummary` — GroupId, CanonicalPath, IsPreviewable, RecommendedPosture, OperationCount, BlockedReasons, ActionNotes, CleanupConfidence
- Batch preview fields now available:
  - `GroupsEvaluated` — how many retained duplicate groups were examined
  - `GroupsPreviewable` — how many are cleanup-eligible
  - `GroupsBlocked` — how many are blocked by policy/sensitivity/confidence
  - `TotalOperationCount` — total quarantine operations across all previewable groups
  - `ConfidenceThresholdUsed` — the policy threshold used for evaluation
  - `Groups` — bounded per-group summaries with posture, operation count, blocked reasons, and action notes
- Tests: 7 tests, all passing
  - `BatchPreview_MixedGroups_TruthfulCounts` — mixed eligible/blocked groups produce correct counts
  - `BatchPreview_LimitEnforced_RestrictsGroupCount` — MaxGroups caps evaluation scope
  - `BatchPreview_TotalOperationCount_IsTruthful` — total ops equals sum of previewable group ops
  - `BatchPreview_BlockedGroups_ZeroOperations` — blocked groups carry blocked reasons
  - `BatchPreview_MissingSession_ReturnsNotFound` — unknown session returns Found=false
  - `BatchPreview_NoDuplicateGroups_ReturnsZeroCounts` — session with no duplicates returns zero counts
  - `BatchPreview_ReportsConfidenceThreshold` — threshold is reported in response
- Full test suite: 292 tests, all passing, 0 regressions
- No new repository methods or storage tables were needed
- Reused `SafeDuplicateCleanupPlanner` and `DuplicateActionEvaluator` per-group as-is
- No `src/Atlas.App/**` files were touched

### C-036 Actual VSS Snapshot Orchestration and Persistence

- Task ID: C-036
- Status: **done**
- Files read:
  - `.planning/claude/C-036-ACTUAL-VSS-SNAPSHOT-ORCHESTRATION-AND-PERSISTENCE.md`
  - `src/Atlas.Service/Services/CheckpointEligibilityEvaluator.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `src/Atlas.Service/Program.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `tests/Atlas.Service.Tests/CheckpointEligibilityTests.cs`
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs`
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs`
- Files modified:
  - `src/Atlas.Service/Services/VssSnapshotOrchestrator.cs` — **created**: VSS orchestration service with `TryCreateSnapshots(CheckpointRequirement, List<string>, bool)`, `VssSnapshotStatus` enum (NotNeeded/Success/Unavailable/Failed/PartialCoverage/Skipped), `VssSnapshotResult`, `VssSnapshotReference`; invokes `vssadmin create shadow /for=<volume>` on Windows, parses snapshot ID from stdout via regex, fails closed on non-Windows/empty volumes/process failures
  - `src/Atlas.Service/Services/PlanExecutionService.cs` — added `VssSnapshotOrchestrator` constructor param; after `CheckpointEligibilityEvaluator.Evaluate()`, calls `TryCreateSnapshots()`; Required + VSS failure → blocks live execution with error; `PopulateCheckpointEligibility` now takes `VssSnapshotResult` and populates `VssSnapshotCreated`, `VssSnapshotReferences`, and truthful notes
  - `src/Atlas.Service/Program.cs` — registered `VssSnapshotOrchestrator` as singleton
  - `tests/Atlas.Service.Tests/VssSnapshotOrchestrationTests.cs` — **created**: 8 tests covering not-needed/recommended/required eligibility, dry-run skipping, non-Windows unavailable, empty volumes, preview mode, and truthful note population
  - `tests/Atlas.Service.Tests/CheckpointEligibilityTests.cs` — updated `ExecutionService_RequiredEligibility_AddsNote` → `ExecutionService_RequiredEligibility_BlocksWhenVssFails` to reflect C-036 fail-closed semantics (Required + VSS failure in non-elevated env → execution blocked)
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` — added `NoOpOptimizationRepository` stub; added `VssSnapshotOrchestrator` + `IOptimizationRepository` to `PlanExecutionService` constructor calls
  - `tests/Atlas.Service.Tests/DeltaScanningTests.cs` — added `VssSnapshotOrchestrator` + `NoOpOptimizationRepository` to `CreateExecutionService` helper
- Key findings:
  - Fail-closed semantics correctly block execution when checkpoint is Required and VSS creation fails
  - Non-Windows environments and dry-run/preview modes return early without attempting VSS
  - Snapshot ID parsed from vssadmin stdout via `Shadow Copy ID: \{([^}]+)\}` regex
- Risks: VSS requires elevated privileges; all tests verify behavior in non-elevated environment
- No `src/Atlas.App/**` files were touched

### C-037 Optimization Execution History and Rollback Details

- Task ID: C-037
- Status: **done**
- Files read:
  - `.planning/claude/C-037-OPTIMIZATION-EXECUTION-HISTORY-AND-ROLLBACK-DETAILS.md`
  - `src/Atlas.Service/Services/SafeOptimizationFixExecutor.cs`
  - `src/Atlas.Service/Services/PlanExecutionService.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
  - `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
  - `src/Atlas.Storage/Repositories/OptimizationRepository.cs`
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `OptimizationExecutionAction` enum (Applied=0, Reverted=1, Failed=2) and `OptimizationExecutionRecord` class with 10 ProtoMembers (RecordId, PlanId, FixKind, Target, Action, Success, IsReversible, RollbackNote, Message, CreatedUtc)
  - `src/Atlas.Storage/AtlasDatabaseBootstrapper.cs` — added `optimization_execution_history` table with columns (record_id, plan_id, fix_kind, target, action, success, is_reversible, rollback_note, message, created_utc) and two indexes (ix_optexec_plan, ix_optexec_target)
  - `src/Atlas.Storage/Repositories/IOptimizationRepository.cs` — added `SaveExecutionRecordAsync`, `GetExecutionHistoryAsync`, `GetExecutionHistoryForTargetAsync`
  - `src/Atlas.Storage/Repositories/OptimizationRepository.cs` — implemented all three new methods plus `ReadExecutionRecordsAsync` helper
  - `src/Atlas.Service/Services/PlanExecutionService.cs` — added `IOptimizationRepository` constructor param; after each `ApplyOptimizationFix`/`RevertOptimizationFix`, persists `OptimizationExecutionRecord` via best-effort `PersistExecutionRecord()` helper (silently catches failures); `RevertOptimizationFix` now takes `planId` parameter
  - `tests/Atlas.Service.Tests/OptimizationExecutionHistoryTests.cs` — **created**: 9 tests covering save/retrieve round-trip, plan filter, target filter, action enum values, failed action persistence, auto-generated record ID, default CreatedUtc, rollback note persistence, empty history
- Key findings:
  - Execution history is best-effort (persistence failures don't block plan execution)
  - Records are durable and queryable by plan ID or target path
  - `CreatedUtc` defaults to `DateTime.UtcNow` when not explicitly set
- No `src/Atlas.App/**` files were touched

### C-038 Conversation Compaction Worker and Retention Orchestration

- Task ID: C-038
- Status: **done**
- Files read:
  - `.planning/claude/C-038-CONVERSATION-COMPACTION-WORKER-AND-RETENTION-ORCHESTRATION.md`
  - `src/Atlas.Service/Services/ConversationCompactionService.cs`
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs`
  - `src/Atlas.Service/Program.cs`
- Files modified:
  - `src/Atlas.Service/Services/AtlasServiceOptions.cs` — added `EnableConversationCompaction` (default false), `CompactionInterval` (default 6h), `CompactionRetentionWindow` (default 7d), `CompactionMinMessages` (default 20), `CompactionMaxCandidatesPerCycle` (default 100)
  - `src/Atlas.Service/Services/ConversationCompactionService.cs` — `CompactAsync` signature changed to add `int maxCandidates = 100` parameter, passed as `limit:` to repository
  - `src/Atlas.Service/Services/ConversationCompactionWorker.cs` — **created**: `BackgroundService` that periodically runs `CompactAsync()` with configured interval; checks `EnableConversationCompaction` each cycle (hot-reload safe); catches all exceptions per cycle (never crashes the worker); logs compacted count only when > 0
  - `src/Atlas.Service/Program.cs` — registered `ConversationCompactionService` as singleton and `ConversationCompactionWorker` as hosted service
  - `tests/Atlas.Service.Tests/ConversationCompactionWorkerTests.cs` — **created**: 12 tests covering disabled compaction, no candidates, recent-only conversations, below-min-messages, bounded max candidates, limit passed correctly, summaries persisted with limit, zero limit, configured options passthrough, no-op behavior, cancellation support
- Key findings:
  - Worker is hot-reload safe: checks `EnableConversationCompaction` each cycle
  - `CompactionMaxCandidatesPerCycle` bounds work per cycle to prevent unbounded batch sizes
  - All options configurable via `AtlasServiceOptions` (IOptions pattern)
- No `src/Atlas.App/**` files were touched

### C-039 Conversation Summary Query APIs

- Task ID: C-039
- Status: **done**
- Files read:
  - `.planning/claude/C-039-CONVERSATION-SUMMARY-QUERY-APIS.md`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs`
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `ConversationSummaryListRequest` (Limit, Offset), `ConversationSummaryListResponse` (Summaries, TotalCount), `ConversationSummaryDetailRequest` (ConversationId), `ConversationSummaryDetailResponse` (Found, Summary), `ConversationSummaryDto` (ConversationId, Summary, MessageCount, CreatedUtc, CompactedUtc)
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs` — added `ListSummariesAsync(int limit, int offset)` and `GetSummaryCountAsync()`
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs` — implemented both new methods with SQLite queries against `conversation_summaries` table
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added routes `"conversation/summaries"` and `"conversation/summary-detail"` with handlers `HandleConversationSummariesAsync` and `HandleConversationSummaryDetailAsync`
  - `tests/Atlas.Service.Tests/ConversationSummaryQueryTests.cs` — **created**: 7 tests covering list with summaries, empty list, pagination offset/limit, detail found, detail not found, total count accuracy, default limit/offset
- Key findings:
  - Read-only pipe routes — no mutations, pure query APIs
  - Pagination via Limit/Offset with TotalCount for client-side paging
  - Detail endpoint returns `Found=false` for unknown conversation IDs
- No `src/Atlas.App/**` files were touched

### Combined Test Results (C-036 through C-039)

- Full test suite: **246 tests, all passing**, 0 failures, 0 regressions
- New tests added: 36 (8 + 9 + 12 + 7)
- Build: 0 warnings, 0 errors
- Constructor compatibility: Fixed 4 existing test files that referenced old `PlanExecutionService` 5-param constructor (now 7 params with `VssSnapshotOrchestrator` + `IOptimizationRepository`); added `NoOpOptimizationRepository` stub for tests that don't exercise optimization history

### C-040 VSS Checkpoint Detail Query APIs

- Task ID: C-040
- Status: **done**
- Files read:
  - `.planning/claude/C-040-VSS-CHECKPOINT-DETAIL-QUERY-APIS.md`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Storage/Repositories/IRecoveryRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
- Files modified:
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `HandleCheckpointDetailAsync` handler method (route `checkpoint/detail` was already wired at line 101). Maps `UndoCheckpoint` → `CheckpointDetailResponse` with counts for inverse operations, quarantine items, and optimization rollback states.
- Files created:
  - `tests/Atlas.Service.Tests/CheckpointDetailQueryTests.cs` — 5 tests: FoundCheckpoint_ReturnsFullDetail, MissingCheckpoint_ReturnsNotFound, OlderCheckpoint_WithoutVssMetadata_ReturnsCleanDefaults, VssSnapshotReferences_AreBounded, OptimizationRollbackState_CountIsExposed
- Key findings:
  - Route was already present in dispatch switch; only the handler body was missing
  - Response exposes counts (not full lists) for inverse operations, quarantine items, and optimization rollback states to keep wire size bounded
  - Older checkpoints without VSS metadata return clean defaults (empty string eligibility, empty lists, false for VssSnapshotCreated)
- No `src/Atlas.App/**` files were touched

### C-041 Optimization Execution History Rollups

- Task ID: C-041
- Status: **done**
- Files read:
  - `.planning/claude/C-041-OPTIMIZATION-EXECUTION-HISTORY-ROLLUPS.md`
  - `src/Atlas.Storage/Repositories/IOptimizationRepository.cs`
  - `src/Atlas.Storage/Repositories/OptimizationRepository.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/DomainModels.cs` — added `OptimizationExecutionRollup` ProtoContract (Kind, TotalCount, AppliedCount, RevertedCount, FailedCount, ReversibleCount, MostRecentUtc) and `OptimizationExecutionSummary` ProtoContract (RecordId, Kind, Target, Action, Success, IsReversible, HasRollbackData, CreatedUtc)
  - `src/Atlas.Storage/Repositories/IOptimizationRepository.cs` — added 3 methods: `GetExecutionRollupsAsync`, `GetRecentExecutionSummariesAsync`, `GetExecutionRecordAsync`
  - `src/Atlas.Storage/Repositories/OptimizationRepository.cs` — implemented all 3 methods with SQL GROUP BY aggregation for rollups, paginated summary query, and single record lookup
  - `tests/Atlas.Service.Tests/PlanExecutionServiceTests.cs` — added 3 stub implementations to `NoOpOptimizationRepository` for new interface methods
- Files created:
  - `tests/Atlas.Service.Tests/OptimizationExecutionRollupTests.cs` — 6 tests: EmptyHistory_ProducesEmptyRollups, SingleKind_AllApplied_ProducesCorrectRollup, MixedApplyAndRevert_GroupedCorrectly, MultipleKinds_ProduceSeparateRollups, ExecutionSummary_MapsFieldsCorrectly, Ordering_MostRecentFirst
- Key findings:
  - Rollups use in-memory LINQ grouping (matching test helper pattern) since the pipe route is not yet wired (repo/service-side only per spec)
  - Stayed out of PipeContracts.cs and AtlasPipeServerWorker.cs as spec required (parallel-safe)
- No `src/Atlas.App/**` files were touched

### C-042 Safe Optimization Fix Request APIs

- Task ID: C-042
- Status: **done**
- Files read:
  - `.planning/claude/C-042-SAFE-OPTIMIZATION-FIX-REQUEST-APIS.md`
  - `src/Atlas.Service/Services/SafeOptimizationFixExecutor.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Core/Contracts/DomainModels.cs`
- Files modified:
  - `src/Atlas.Service/Services/SafeOptimizationFixExecutor.cs` — added public `UnsafeKinds` static property exposing `BlockedKinds` for use by request handlers
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added 6 contracts: `OptimizationFixPreviewRequest/Response`, `OptimizationFixApplyRequest/Response`, `OptimizationFixRevertRequest/Response` with ProtoContract/ProtoMember attributes
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added `SafeOptimizationFixExecutor` to constructor DI; added 3 routes (`optimization/fix-preview`, `optimization/fix-apply`, `optimization/fix-revert`); added `HandleOptimizationFixPreviewAsync`, `HandleOptimizationFixApplyAsync`, `HandleOptimizationFixRevertAsync` handlers
- Files created:
  - `tests/Atlas.Service.Tests/SafeOptimizationFixRequestTests.cs` — 10 tests: SafeKind_IsAccepted (4 theory cases), UnsafeKind_IsRejected (4 theory cases), Preview_ForSafeKind_ReturnsEligible, Preview_ForUnsafeKind_ReturnsNotSafe, Preview_ForRecommendationOnly_ReturnsCannotAutoFix, Preview_MissingFinding_ReturnsNotFound, ApplyResponse_TracksRollbackState, ApplyResponse_UnsafeKind_Fails, RevertResponse_RecordsRevertId, RevertResponse_MissingRecord_Fails
- Key findings:
  - Preview checks both `CanAutoFix` and `UnsafeKinds` for eligibility
  - Apply persists `OptimizationExecutionRecord` with action tracking (Applied/Failed)
  - Revert handler validates rollback data exists before recording revert; records a new execution record with `Reverted` action
  - Safe kinds: TemporaryFiles, CacheCleanup, DuplicateArchives, UserStartupEntry. Unsafe: ScheduledTask, BackgroundApplication, LowDiskPressure, Unknown
- No `src/Atlas.App/**` files were touched

### C-043 Conversation Summary Snapshot Integration

- Task ID: C-043
- Status: **done**
- Files read:
  - `.planning/claude/C-043-CONVERSATION-SUMMARY-SNAPSHOT-INTEGRATION.md`
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs`
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs`
  - `src/Atlas.Core/Contracts/PipeContracts.cs`
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs`
- Files modified:
  - `src/Atlas.Core/Contracts/PipeContracts.cs` — added `ConversationSummarySnapshotRequest` (empty) and `ConversationSummarySnapshotResponse` (TotalSummaryCount, CompactedConversationCount, NonCompactedConversationCount, CompactedSummaryCount, RetainedSummaryCount, MostRecentSummaryUtc)
  - `src/Atlas.Storage/Repositories/IConversationRepository.cs` — added 3 methods: `GetSummaryCompactionCountsAsync`, `GetConversationCompactionCountsAsync`, `GetMostRecentSummaryUtcAsync`
  - `src/Atlas.Storage/Repositories/ConversationRepository.cs` — implemented all 3 methods with SQL `SUM(CASE WHEN...)` aggregation for compaction counting and `MAX(created_utc)` for most recent timestamp
  - `src/Atlas.Service/Services/AtlasPipeServerWorker.cs` — added route `conversation/summary-snapshot` and `HandleConversationSummarySnapshotAsync` handler that aggregates all counts
- Files created:
  - `tests/Atlas.Service.Tests/ConversationSummarySnapshotTests.cs` — 5 tests: EmptyState_ProducesZeroCounts, MixedCompactionState_SumsCorrectly, AllCompacted_ZeroRetained, NoneCompacted_AllRetained, MostRecentSummaryUtc_RoundTrips
- Key findings:
  - Snapshot is a parameterless aggregation endpoint — no pagination needed
  - Compaction counts split into summary-level (compacted vs retained) and conversation-level (compacted vs non-compacted)
  - `MostRecentSummaryUtc` returns empty string when no summaries exist
- No `src/Atlas.App/**` files were touched

### Combined Test Results (C-040 through C-043)

- Full test suite: **278 tests, all passing**, 0 failures, 0 regressions
- New tests added: 26 (5 + 6 + 10 + 5)
- Build: 0 warnings, 0 errors
- Constructor compatibility: Extended `NoOpOptimizationRepository` with 3 additional stub methods for C-041 interface additions