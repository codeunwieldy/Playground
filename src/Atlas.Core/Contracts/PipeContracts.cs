using ProtoBuf;

namespace Atlas.Core.Contracts;

[ProtoContract]
public sealed class PipeEnvelope
{
    [ProtoMember(1)] public int ProtocolVersion { get; set; } = 1;
    [ProtoMember(2)] public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(3)] public string MessageType { get; set; } = string.Empty;
    [ProtoMember(4)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[ProtoContract]
public sealed class ScanRequest
{
    [ProtoMember(1)] public bool FullScan { get; set; } = true;
    [ProtoMember(2)] public List<string> Roots { get; set; } = new();
    [ProtoMember(3)] public int MaxFiles { get; set; } = 25000;
}

[ProtoContract]
public sealed class ScanResponse
{
    [ProtoMember(1)] public List<VolumeSnapshot> Volumes { get; set; } = new();
    [ProtoMember(2)] public List<FileInventoryItem> Inventory { get; set; } = new();
    [ProtoMember(3)] public List<DuplicateGroup> Duplicates { get; set; } = new();
    [ProtoMember(4)] public long FilesScanned { get; set; }
}

[ProtoContract]
public sealed class PlanRequest
{
    [ProtoMember(1)] public string UserIntent { get; set; } = string.Empty;
    [ProtoMember(2)] public ScanResponse Scan { get; set; } = new();
    [ProtoMember(3)] public PolicyProfile PolicyProfile { get; set; } = new();
}

[ProtoContract]
public sealed class PlanResponse
{
    [ProtoMember(1)] public PlanGraph Plan { get; set; } = new();
    [ProtoMember(2)] public string Summary { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class ExecutionRequest
{
    [ProtoMember(1)] public ExecutionBatch Batch { get; set; } = new();
    [ProtoMember(2)] public PolicyProfile PolicyProfile { get; set; } = new();
    [ProtoMember(3)] public bool Execute { get; set; }
}

[ProtoContract]
public sealed class ExecutionResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public List<string> Messages { get; set; } = new();
    [ProtoMember(3)] public UndoCheckpoint UndoCheckpoint { get; set; } = new();
}

[ProtoContract]
public sealed class UndoRequest
{
    [ProtoMember(1)] public UndoCheckpoint Checkpoint { get; set; } = new();
    [ProtoMember(2)] public bool Execute { get; set; }
}

[ProtoContract]
public sealed class UndoResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public List<string> Messages { get; set; } = new();
}

[ProtoContract]
public sealed class OptimizationRequest
{
    [ProtoMember(1)] public bool IncludeRecommendationsOnly { get; set; } = true;
    [ProtoMember(2)] public PolicyProfile PolicyProfile { get; set; } = new();
}

[ProtoContract]
public sealed class OptimizationResponse
{
    [ProtoMember(1)] public List<OptimizationFinding> Findings { get; set; } = new();
}

[ProtoContract]
public sealed class VoiceIntentRequest
{
    [ProtoMember(1)] public string Transcript { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class VoiceIntentResponse
{
    [ProtoMember(1)] public string ParsedIntent { get; set; } = string.Empty;
    [ProtoMember(2)] public bool NeedsConfirmation { get; set; } = true;
}

[ProtoContract]
public sealed class ProgressEvent
{
    [ProtoMember(1)] public string Stage { get; set; } = string.Empty;
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public double Progress { get; set; }
}

// ── History read-side contracts (C-008) ─────────────────────────────────────

[ProtoContract]
public sealed class HistorySnapshotRequest
{
    [ProtoMember(1)] public int Limit { get; set; } = 10;
}

[ProtoContract]
public sealed class HistorySnapshotResponse
{
    [ProtoMember(1)] public List<HistoryPlanSummary> RecentPlans { get; set; } = new();
    [ProtoMember(2)] public List<HistoryCheckpointSummary> RecentCheckpoints { get; set; } = new();
    [ProtoMember(3)] public List<HistoryQuarantineSummary> RecentQuarantine { get; set; } = new();
    [ProtoMember(4)] public List<HistoryFindingSummary> RecentFindings { get; set; } = new();
    [ProtoMember(5)] public List<HistoryTraceSummary> RecentTraces { get; set; } = new();
}

[ProtoContract]
public sealed class HistoryListRequest
{
    [ProtoMember(1)] public int Limit { get; set; } = 50;
    [ProtoMember(2)] public int Offset { get; set; }
    [ProtoMember(3)] public string Stage { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class HistoryPlanListResponse
{
    [ProtoMember(1)] public List<HistoryPlanSummary> Plans { get; set; } = new();
}

[ProtoContract]
public sealed class HistoryPlanDetailRequest
{
    [ProtoMember(1)] public string PlanId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class HistoryPlanDetailResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public PlanGraph Plan { get; set; } = new();
    [ProtoMember(3)] public List<ExecutionBatch> Batches { get; set; } = new();
}

[ProtoContract]
public sealed class HistoryCheckpointListResponse
{
    [ProtoMember(1)] public List<HistoryCheckpointSummary> Checkpoints { get; set; } = new();
}

[ProtoContract]
public sealed class HistoryQuarantineListResponse
{
    [ProtoMember(1)] public List<HistoryQuarantineSummary> Items { get; set; } = new();
}

[ProtoContract]
public sealed class HistoryFindingListResponse
{
    [ProtoMember(1)] public List<HistoryFindingSummary> Findings { get; set; } = new();
}

[ProtoContract]
public sealed class HistoryTraceListResponse
{
    [ProtoMember(1)] public List<HistoryTraceSummary> Traces { get; set; } = new();
}

// ── History summary DTOs ────────────────────────────────────────────────────

[ProtoContract]
public sealed class HistoryPlanSummary
{
    [ProtoMember(1)] public string PlanId { get; set; } = string.Empty;
    [ProtoMember(2)] public string Scope { get; set; } = string.Empty;
    [ProtoMember(3)] public string Summary { get; set; } = string.Empty;
    [ProtoMember(4)] public int OperationCount { get; set; }
    [ProtoMember(5)] public bool RequiresReview { get; set; }
    [ProtoMember(6)] public string CreatedUtc { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class HistoryCheckpointSummary
{
    [ProtoMember(1)] public string CheckpointId { get; set; } = string.Empty;
    [ProtoMember(2)] public string BatchId { get; set; } = string.Empty;
    [ProtoMember(3)] public int OperationCount { get; set; }
    [ProtoMember(4)] public int QuarantineCount { get; set; }
    [ProtoMember(5)] public string CreatedUtc { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class HistoryQuarantineSummary
{
    [ProtoMember(1)] public string QuarantineId { get; set; } = string.Empty;
    [ProtoMember(2)] public string OriginalPath { get; set; } = string.Empty;
    [ProtoMember(3)] public string Reason { get; set; } = string.Empty;
    [ProtoMember(4)] public string RetentionUntilUtc { get; set; } = string.Empty;
    [ProtoMember(5)] public string PlanId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class HistoryFindingSummary
{
    [ProtoMember(1)] public string FindingId { get; set; } = string.Empty;
    [ProtoMember(2)] public OptimizationKind Kind { get; set; }
    [ProtoMember(3)] public string Target { get; set; } = string.Empty;
    [ProtoMember(4)] public bool CanAutoFix { get; set; }
    [ProtoMember(5)] public string CreatedUtc { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class HistoryTraceSummary
{
    [ProtoMember(1)] public string TraceId { get; set; } = string.Empty;
    [ProtoMember(2)] public string Stage { get; set; } = string.Empty;
    [ProtoMember(3)] public string CreatedUtc { get; set; } = string.Empty;
}

// ── Inventory read-side contracts (C-011) ────────────────────────────────────

[ProtoContract]
public sealed class InventorySnapshotRequest
{
    [ProtoMember(1)] public int Limit { get; set; } = 1;
}

[ProtoContract]
public sealed class InventorySnapshotResponse
{
    [ProtoMember(1)] public bool HasSession { get; set; }
    [ProtoMember(2)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(3)] public int FilesScanned { get; set; }
    [ProtoMember(4)] public int DuplicateGroupCount { get; set; }
    [ProtoMember(5)] public int RootCount { get; set; }
    [ProtoMember(6)] public int VolumeCount { get; set; }
    [ProtoMember(7)] public string CreatedUtc { get; set; } = string.Empty;

    // ── Provenance (C-016) ──────────────────────────────────────────────────
    [ProtoMember(8)] public string Trigger { get; set; } = string.Empty;
    [ProtoMember(9)] public string BuildMode { get; set; } = string.Empty;
    [ProtoMember(10)] public string DeltaSource { get; set; } = string.Empty;
    [ProtoMember(11)] public string BaselineSessionId { get; set; } = string.Empty;
    [ProtoMember(12)] public bool IsTrusted { get; set; }
    [ProtoMember(13)] public string CompositionNote { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class InventorySessionListRequest
{
    [ProtoMember(1)] public int Limit { get; set; } = 20;
    [ProtoMember(2)] public int Offset { get; set; }
}

[ProtoContract]
public sealed class InventorySessionListResponse
{
    [ProtoMember(1)] public List<InventorySessionSummary> Sessions { get; set; } = new();
}

[ProtoContract]
public sealed class InventorySessionDetailRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class InventorySessionDetailResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(3)] public int FilesScanned { get; set; }
    [ProtoMember(4)] public int DuplicateGroupCount { get; set; }
    [ProtoMember(5)] public string CreatedUtc { get; set; } = string.Empty;
    [ProtoMember(6)] public List<string> Roots { get; set; } = new();
    [ProtoMember(7)] public List<InventoryVolumeSummary> Volumes { get; set; } = new();

    // ── Provenance (C-016) ──────────────────────────────────────────────────
    [ProtoMember(8)] public string Trigger { get; set; } = string.Empty;
    [ProtoMember(9)] public string BuildMode { get; set; } = string.Empty;
    [ProtoMember(10)] public string DeltaSource { get; set; } = string.Empty;
    [ProtoMember(11)] public string BaselineSessionId { get; set; } = string.Empty;
    [ProtoMember(12)] public bool IsTrusted { get; set; }
    [ProtoMember(13)] public string CompositionNote { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class InventoryVolumeListRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class InventoryVolumeListResponse
{
    [ProtoMember(1)] public List<InventoryVolumeSummary> Volumes { get; set; } = new();
}

[ProtoContract]
public sealed class InventoryFileListRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public int Limit { get; set; } = 200;
    [ProtoMember(3)] public int Offset { get; set; }
}

[ProtoContract]
public sealed class InventoryFileListResponse
{
    [ProtoMember(1)] public List<InventoryFileSummary> Files { get; set; } = new();
    [ProtoMember(2)] public int TotalCount { get; set; }
}

// ── Inventory summary DTOs ──────────────────────────────────────────────────

[ProtoContract]
public sealed class InventorySessionSummary
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public int FilesScanned { get; set; }
    [ProtoMember(3)] public int DuplicateGroupCount { get; set; }
    [ProtoMember(4)] public int RootCount { get; set; }
    [ProtoMember(5)] public int VolumeCount { get; set; }
    [ProtoMember(6)] public string CreatedUtc { get; set; } = string.Empty;

    // ── Provenance (C-016) ──────────────────────────────────────────────────
    [ProtoMember(7)] public string Trigger { get; set; } = string.Empty;
    [ProtoMember(8)] public string BuildMode { get; set; } = string.Empty;
    [ProtoMember(9)] public string DeltaSource { get; set; } = string.Empty;
    [ProtoMember(10)] public string BaselineSessionId { get; set; } = string.Empty;
    [ProtoMember(11)] public bool IsTrusted { get; set; }
    [ProtoMember(12)] public string CompositionNote { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class InventoryVolumeSummary
{
    [ProtoMember(1)] public string RootPath { get; set; } = string.Empty;
    [ProtoMember(2)] public string DriveFormat { get; set; } = string.Empty;
    [ProtoMember(3)] public string DriveType { get; set; } = string.Empty;
    [ProtoMember(4)] public bool IsReady { get; set; }
    [ProtoMember(5)] public long TotalSizeBytes { get; set; }
    [ProtoMember(6)] public long FreeSpaceBytes { get; set; }
}

[ProtoContract]
public sealed class InventoryFileSummary
{
    [ProtoMember(1)] public string Path { get; set; } = string.Empty;
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public string Extension { get; set; } = string.Empty;
    [ProtoMember(4)] public string Category { get; set; } = string.Empty;
    [ProtoMember(5)] public long SizeBytes { get; set; }
    [ProtoMember(6)] public long LastModifiedUnixTimeSeconds { get; set; }
    [ProtoMember(7)] public SensitivityLevel Sensitivity { get; set; }
    [ProtoMember(8)] public bool IsSyncManaged { get; set; }
    [ProtoMember(9)] public bool IsDuplicateCandidate { get; set; }
}

// ── Scan drift / diff contracts (C-013) ─────────────────────────────────────

[ProtoContract]
public sealed class DriftSnapshotRequest { }

// ── Session duplicate review contracts (C-023) ──────────────────────────────

[ProtoContract]
public sealed class SessionDuplicateListRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public int Limit { get; set; } = 200;
    [ProtoMember(3)] public int Offset { get; set; }
}

[ProtoContract]
public sealed class SessionDuplicateListResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public List<DuplicateGroupSummary> Groups { get; set; } = new();
    [ProtoMember(3)] public int TotalCount { get; set; }
}

[ProtoContract]
public sealed class DuplicateGroupSummary
{
    [ProtoMember(1)] public string GroupId { get; set; } = string.Empty;
    [ProtoMember(2)] public string CanonicalPath { get; set; } = string.Empty;
    [ProtoMember(3)] public double MatchConfidence { get; set; }
    [ProtoMember(4)] public double CleanupConfidence { get; set; }
    [ProtoMember(5)] public string CanonicalReason { get; set; } = string.Empty;
    [ProtoMember(6)] public SensitivityLevel MaxSensitivity { get; set; }
    [ProtoMember(7)] public bool HasSensitiveMembers { get; set; }
    [ProtoMember(8)] public bool HasSyncManagedMembers { get; set; }
    [ProtoMember(9)] public bool HasProtectedMembers { get; set; }
    [ProtoMember(10)] public List<string> MemberPaths { get; set; } = new();
    [ProtoMember(11)] public int MemberCount { get; set; }
}

// ── File inspection and explainability contracts (C-024) ─────────────────────

[ProtoContract]
public sealed class FileInspectionRequest
{
    [ProtoMember(1)] public string FilePath { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class FileInspectionResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public string Outcome { get; set; } = string.Empty;

    // Identity
    [ProtoMember(3)] public string Path { get; set; } = string.Empty;
    [ProtoMember(4)] public string Name { get; set; } = string.Empty;
    [ProtoMember(5)] public string Extension { get; set; } = string.Empty;

    // Type truth
    [ProtoMember(6)] public string Category { get; set; } = string.Empty;
    [ProtoMember(7)] public string MimeType { get; set; } = string.Empty;
    [ProtoMember(8)] public bool ContentSniffSucceeded { get; set; }
    [ProtoMember(9)] public bool HasContentFingerprint { get; set; }

    // Size/time
    [ProtoMember(10)] public long SizeBytes { get; set; }
    [ProtoMember(11)] public long LastModifiedUnixTimeSeconds { get; set; }

    // Sensitivity
    [ProtoMember(12)] public SensitivityLevel Sensitivity { get; set; }
    [ProtoMember(13)] public List<SensitivityEvidenceSummary> SensitivityEvidence { get; set; } = new();

    // Posture
    [ProtoMember(14)] public bool IsSyncManaged { get; set; }
    [ProtoMember(15)] public bool IsDuplicateCandidate { get; set; }
}

[ProtoContract]
public sealed class SensitivityEvidenceSummary
{
    [ProtoMember(1)] public string Signal { get; set; } = string.Empty;
    [ProtoMember(2)] public string Detail { get; set; } = string.Empty;
}

// ── Persisted session file detail contract (C-024) ───────────────────────────

[ProtoContract]
public sealed class SessionFileDetailRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public string FilePath { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class SessionFileDetailResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public string Path { get; set; } = string.Empty;
    [ProtoMember(3)] public string Name { get; set; } = string.Empty;
    [ProtoMember(4)] public string Extension { get; set; } = string.Empty;
    [ProtoMember(5)] public string Category { get; set; } = string.Empty;
    [ProtoMember(6)] public long SizeBytes { get; set; }
    [ProtoMember(7)] public long LastModifiedUnixTimeSeconds { get; set; }
    [ProtoMember(8)] public SensitivityLevel Sensitivity { get; set; }
    [ProtoMember(9)] public bool IsSyncManaged { get; set; }
    [ProtoMember(10)] public bool IsDuplicateCandidate { get; set; }
}

[ProtoContract]
public sealed class DriftSnapshotResponse
{
    [ProtoMember(1)] public bool HasBaseline { get; set; }
    [ProtoMember(2)] public string OlderSessionId { get; set; } = string.Empty;
    [ProtoMember(3)] public string NewerSessionId { get; set; } = string.Empty;
    [ProtoMember(4)] public int AddedCount { get; set; }
    [ProtoMember(5)] public int RemovedCount { get; set; }
    [ProtoMember(6)] public int ChangedCount { get; set; }
    [ProtoMember(7)] public int UnchangedCount { get; set; }
    [ProtoMember(8)] public string OlderCreatedUtc { get; set; } = string.Empty;
    [ProtoMember(9)] public string NewerCreatedUtc { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class SessionDiffRequest
{
    [ProtoMember(1)] public string OlderSessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public string NewerSessionId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class SessionDiffResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public string OlderSessionId { get; set; } = string.Empty;
    [ProtoMember(3)] public string NewerSessionId { get; set; } = string.Empty;
    [ProtoMember(4)] public int AddedCount { get; set; }
    [ProtoMember(5)] public int RemovedCount { get; set; }
    [ProtoMember(6)] public int ChangedCount { get; set; }
    [ProtoMember(7)] public int UnchangedCount { get; set; }
}

[ProtoContract]
public sealed class SessionDiffFilesRequest
{
    [ProtoMember(1)] public string OlderSessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public string NewerSessionId { get; set; } = string.Empty;
    [ProtoMember(3)] public int Limit { get; set; } = 200;
    [ProtoMember(4)] public int Offset { get; set; }
}

[ProtoContract]
public sealed class SessionDiffFilesResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public List<DiffFileSummary> Files { get; set; } = new();
}

[ProtoContract]
public sealed class DiffFileSummary
{
    [ProtoMember(1)] public string Path { get; set; } = string.Empty;
    [ProtoMember(2)] public string ChangeKind { get; set; } = string.Empty;
    [ProtoMember(3)] public long OlderSizeBytes { get; set; }
    [ProtoMember(4)] public long NewerSizeBytes { get; set; }
    [ProtoMember(5)] public long OlderLastModifiedUnix { get; set; }
    [ProtoMember(6)] public long NewerLastModifiedUnix { get; set; }
}

// ── Duplicate group detail contracts (C-025) ────────────────────────────────

[ProtoContract]
public sealed class DuplicateGroupDetailRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public string GroupId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class DuplicateGroupDetailResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public string GroupId { get; set; } = string.Empty;
    [ProtoMember(3)] public string CanonicalPath { get; set; } = string.Empty;
    [ProtoMember(4)] public double MatchConfidence { get; set; }
    [ProtoMember(5)] public double CleanupConfidence { get; set; }
    [ProtoMember(6)] public string CanonicalReason { get; set; } = string.Empty;
    [ProtoMember(7)] public SensitivityLevel MaxSensitivity { get; set; }
    [ProtoMember(8)] public bool HasSensitiveMembers { get; set; }
    [ProtoMember(9)] public bool HasSyncManagedMembers { get; set; }
    [ProtoMember(10)] public bool HasProtectedMembers { get; set; }
    [ProtoMember(11)] public List<string> MemberPaths { get; set; } = new();
    [ProtoMember(12)] public int MemberCount { get; set; }
    [ProtoMember(13)] public List<DuplicateEvidenceSummary> Evidence { get; set; } = new();
}

[ProtoContract]
public sealed class DuplicateEvidenceSummary
{
    [ProtoMember(1)] public string Signal { get; set; } = string.Empty;
    [ProtoMember(2)] public string Detail { get; set; } = string.Empty;
}

// ── Duplicate action eligibility review contracts (C-026) ────────────────────

[ProtoContract]
public sealed class DuplicateActionReviewRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public string GroupId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class DuplicateActionReviewResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public bool IsCleanupEligible { get; set; }
    [ProtoMember(3)] public bool RequiresReview { get; set; }
    [ProtoMember(4)] public DuplicateActionPosture RecommendedPosture { get; set; }
    [ProtoMember(5)] public List<string> BlockedReasons { get; set; } = new();
    [ProtoMember(6)] public List<string> ActionNotes { get; set; } = new();
    [ProtoMember(7)] public string GroupId { get; set; } = string.Empty;
    [ProtoMember(8)] public double ConfidenceThresholdUsed { get; set; }
}

// ── Duplicate cleanup preview contracts (C-027) ──────────────────────────────

[ProtoContract]
public sealed class DuplicateCleanupPreviewRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public string GroupId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class DuplicateCleanupPreviewResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public bool IsPreviewAvailable { get; set; }
    [ProtoMember(3)] public DuplicateActionPosture RecommendedPosture { get; set; }
    [ProtoMember(4)] public string CanonicalPath { get; set; } = string.Empty;
    [ProtoMember(5)] public List<CleanupOperationPreview> Operations { get; set; } = new();
    [ProtoMember(6)] public List<string> BlockedReasons { get; set; } = new();
    [ProtoMember(7)] public List<string> ActionNotes { get; set; } = new();
    [ProtoMember(8)] public string GroupId { get; set; } = string.Empty;
    [ProtoMember(9)] public double ConfidenceThresholdUsed { get; set; }
    [ProtoMember(10)] public int OperationCount { get; set; }
}

[ProtoContract]
public sealed class CleanupOperationPreview
{
    [ProtoMember(1)] public string SourcePath { get; set; } = string.Empty;
    [ProtoMember(2)] public string Kind { get; set; } = string.Empty;
    [ProtoMember(3)] public string Description { get; set; } = string.Empty;
    [ProtoMember(4)] public double Confidence { get; set; }
    [ProtoMember(5)] public string Sensitivity { get; set; } = string.Empty;
}

// ── Duplicate cleanup batch preview contracts (C-028) ─────────────────────

[ProtoContract]
public sealed class DuplicateCleanupBatchPreviewRequest
{
    [ProtoMember(1)] public string SessionId { get; set; } = string.Empty;
    [ProtoMember(2)] public int MaxGroups { get; set; } = 50;
    [ProtoMember(3)] public int MaxOperationsPerGroup { get; set; } = 20;
}

[ProtoContract]
public sealed class DuplicateCleanupBatchPreviewResponse
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public int GroupsEvaluated { get; set; }
    [ProtoMember(3)] public int GroupsPreviewable { get; set; }
    [ProtoMember(4)] public int GroupsBlocked { get; set; }
    [ProtoMember(5)] public int TotalOperationCount { get; set; }
    [ProtoMember(6)] public double ConfidenceThresholdUsed { get; set; }
    [ProtoMember(7)] public List<BatchGroupPreviewSummary> Groups { get; set; } = new();
}

[ProtoContract]
public sealed class BatchGroupPreviewSummary
{
    [ProtoMember(1)] public string GroupId { get; set; } = string.Empty;
    [ProtoMember(2)] public string CanonicalPath { get; set; } = string.Empty;
    [ProtoMember(3)] public bool IsPreviewable { get; set; }
    [ProtoMember(4)] public DuplicateActionPosture RecommendedPosture { get; set; }
    [ProtoMember(5)] public int OperationCount { get; set; }
    [ProtoMember(6)] public List<string> BlockedReasons { get; set; } = new();
    [ProtoMember(7)] public List<string> ActionNotes { get; set; } = new();
    [ProtoMember(8)] public double CleanupConfidence { get; set; }
}