using ProtoBuf;

namespace Atlas.Core.Contracts;

public enum OperationKind
{
    Unknown = 0,
    CreateDirectory = 1,
    MovePath = 2,
    RenamePath = 3,
    DeleteToQuarantine = 4,
    RestoreFromQuarantine = 5,
    MergeDuplicateGroup = 6,
    ApplyOptimizationFix = 7,
    RevertOptimizationFix = 8
}

public enum SensitivityLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum ApprovalRequirement
{
    None = 0,
    Review = 1,
    ExplicitApproval = 2
}

public enum OptimizationKind
{
    Unknown = 0,
    UserStartupEntry = 1,
    ScheduledTask = 2,
    TemporaryFiles = 3,
    CacheCleanup = 4,
    DuplicateArchives = 5,
    BackgroundApplication = 6,
    LowDiskPressure = 7
}

public enum DuplicateActionPosture
{
    Keep = 0,
    Review = 1,
    QuarantineDuplicates = 2
}

[ProtoContract]
public sealed class PolicyProfile
{
    [ProtoMember(1)] public string ProfileName { get; set; } = "Default";
    [ProtoMember(2)] public List<string> ScanRoots { get; set; } = new();
    [ProtoMember(3)] public List<string> MutableRoots { get; set; } = new();
    [ProtoMember(4)] public List<string> ExcludedRoots { get; set; } = new();
    [ProtoMember(5)] public List<string> ProtectedPaths { get; set; } = new();
    [ProtoMember(6)] public List<string> SyncFolderMarkers { get; set; } = new();
    [ProtoMember(7)] public double DuplicateAutoDeleteConfidenceThreshold { get; set; } = 0.98d;
    [ProtoMember(8)] public bool UploadSensitiveContent { get; set; }
    [ProtoMember(9)] public bool ExcludeSyncFoldersByDefault { get; set; } = true;
    [ProtoMember(10)] public List<OptimizationKind> AllowedAutomaticOptimizationKinds { get; set; } = new();
    [ProtoMember(11)] public List<string> ProtectedKeywords { get; set; } = new();
}

[ProtoContract]
public sealed class VolumeSnapshot
{
    [ProtoMember(1)] public string RootPath { get; set; } = string.Empty;
    [ProtoMember(2)] public string DriveFormat { get; set; } = string.Empty;
    [ProtoMember(3)] public string DriveType { get; set; } = string.Empty;
    [ProtoMember(4)] public bool IsReady { get; set; }
    [ProtoMember(5)] public long TotalSizeBytes { get; set; }
    [ProtoMember(6)] public long FreeSpaceBytes { get; set; }
}

[ProtoContract]
public sealed class FileInventoryItem
{
    [ProtoMember(1)] public string Path { get; set; } = string.Empty;
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public string Extension { get; set; } = string.Empty;
    [ProtoMember(4)] public string Category { get; set; } = string.Empty;
    [ProtoMember(5)] public string MimeType { get; set; } = string.Empty;
    [ProtoMember(6)] public long SizeBytes { get; set; }
    [ProtoMember(7)] public long LastModifiedUnixTimeSeconds { get; set; }
    [ProtoMember(8)] public SensitivityLevel Sensitivity { get; set; }
    [ProtoMember(9)] public string ContentFingerprint { get; set; } = string.Empty;
    [ProtoMember(10)] public bool IsSyncManaged { get; set; }
    [ProtoMember(11)] public bool IsDuplicateCandidate { get; set; }
    [ProtoMember(12)] public bool IsProtectedByUser { get; set; }
}

[ProtoContract]
public sealed class DuplicateGroup
{
    [ProtoMember(1)] public string GroupId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public List<string> Paths { get; set; } = new();
    [ProtoMember(3)] public string CanonicalPath { get; set; } = string.Empty;
    [ProtoMember(4)] public double Confidence { get; set; }
    [ProtoMember(5)] public string CanonicalReason { get; set; } = string.Empty;
    [ProtoMember(6)] public bool HasSensitiveMembers { get; set; }
    [ProtoMember(7)] public bool HasSyncManagedMembers { get; set; }
    [ProtoMember(8)] public bool HasProtectedMembers { get; set; }
    [ProtoMember(9)] public SensitivityLevel MaxSensitivity { get; set; }
    [ProtoMember(10)] public double MatchConfidence { get; set; }
    [ProtoMember(11)] public List<DuplicateEvidenceEntry> Evidence { get; set; } = new();
}

[ProtoContract]
public sealed class DuplicateEvidenceEntry
{
    [ProtoMember(1)] public string Signal { get; set; } = string.Empty;
    [ProtoMember(2)] public string Detail { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class RiskEnvelope
{
    [ProtoMember(1)] public double SensitivityScore { get; set; }
    [ProtoMember(2)] public double SystemScore { get; set; }
    [ProtoMember(3)] public double SyncRisk { get; set; }
    [ProtoMember(4)] public double ReversibilityScore { get; set; }
    [ProtoMember(5)] public double Confidence { get; set; }
    [ProtoMember(6)] public ApprovalRequirement ApprovalRequirement { get; set; }
    [ProtoMember(7)] public List<string> BlockedReasons { get; set; } = new();
}

[ProtoContract]
public sealed class PlanOperation
{
    [ProtoMember(1)] public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public OperationKind Kind { get; set; }
    [ProtoMember(3)] public string SourcePath { get; set; } = string.Empty;
    [ProtoMember(4)] public string DestinationPath { get; set; } = string.Empty;
    [ProtoMember(5)] public string Description { get; set; } = string.Empty;
    [ProtoMember(6)] public double Confidence { get; set; }
    [ProtoMember(7)] public bool MarksSafeDuplicate { get; set; }
    [ProtoMember(8)] public SensitivityLevel Sensitivity { get; set; }
    [ProtoMember(9)] public string GroupId { get; set; } = string.Empty;
    [ProtoMember(10)] public OptimizationKind OptimizationKind { get; set; }
}

[ProtoContract]
public sealed class PlanGraph
{
    [ProtoMember(1)] public string PlanId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public string Scope { get; set; } = string.Empty;
    [ProtoMember(3)] public string Rationale { get; set; } = string.Empty;
    [ProtoMember(4)] public List<string> Categories { get; set; } = new();
    [ProtoMember(5)] public List<PlanOperation> Operations { get; set; } = new();
    [ProtoMember(6)] public RiskEnvelope RiskSummary { get; set; } = new();
    [ProtoMember(7)] public string EstimatedBenefit { get; set; } = string.Empty;
    [ProtoMember(8)] public bool RequiresReview { get; set; }
    [ProtoMember(9)] public string RollbackStrategy { get; set; } = string.Empty;

    // ── Lineage (C-032) ────────────────────────────────────────────────────
    [ProtoMember(10)] public string Source { get; set; } = string.Empty;
    [ProtoMember(11)] public string SourceSessionId { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class ExecutionBatch
{
    [ProtoMember(1)] public string BatchId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public string PlanId { get; set; } = string.Empty;
    [ProtoMember(3)] public List<string> TouchedVolumes { get; set; } = new();
    [ProtoMember(4)] public bool RequiresCheckpoint { get; set; }
    [ProtoMember(5)] public bool IsDryRun { get; set; }
    [ProtoMember(6)] public List<PlanOperation> Operations { get; set; } = new();
    [ProtoMember(7)] public string EstimatedImpact { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class InverseOperation
{
    [ProtoMember(1)] public string SourcePath { get; set; } = string.Empty;
    [ProtoMember(2)] public string DestinationPath { get; set; } = string.Empty;
    [ProtoMember(3)] public OperationKind Kind { get; set; }
    [ProtoMember(4)] public string Description { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class QuarantineItem
{
    [ProtoMember(1)] public string QuarantineId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public string OriginalPath { get; set; } = string.Empty;
    [ProtoMember(3)] public string CurrentPath { get; set; } = string.Empty;
    [ProtoMember(4)] public string PlanId { get; set; } = string.Empty;
    [ProtoMember(5)] public string Reason { get; set; } = string.Empty;
    [ProtoMember(6)] public long RetentionUntilUnixTimeSeconds { get; set; }
    [ProtoMember(7)] public string ContentHash { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class UndoCheckpoint
{
    [ProtoMember(1)] public string CheckpointId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public string BatchId { get; set; } = string.Empty;
    [ProtoMember(3)] public List<InverseOperation> InverseOperations { get; set; } = new();
    [ProtoMember(4)] public List<QuarantineItem> QuarantineItems { get; set; } = new();
    [ProtoMember(5)] public List<string> Notes { get; set; } = new();
    [ProtoMember(6)] public List<string> VssSnapshotReferences { get; set; } = new();

    // ── Checkpoint eligibility metadata (C-033) ─────────────────────────────
    [ProtoMember(7)] public string CheckpointEligibility { get; set; } = string.Empty;
    [ProtoMember(8)] public string EligibilityReason { get; set; } = string.Empty;
    [ProtoMember(9)] public List<string> CoveredVolumes { get; set; } = new();
    [ProtoMember(10)] public bool VssSnapshotCreated { get; set; }

    // ── Optimization rollback states (C-034) ────────────────────────────────
    [ProtoMember(11)] public List<OptimizationRollbackState> OptimizationRollbackStates { get; set; } = new();
}

[ProtoContract]
public sealed class OptimizationRollbackState
{
    [ProtoMember(1)] public OptimizationKind Kind { get; set; }
    [ProtoMember(2)] public string Target { get; set; } = string.Empty;
    [ProtoMember(3)] public bool IsReversible { get; set; }
    [ProtoMember(4)] public string RollbackData { get; set; } = string.Empty;
    [ProtoMember(5)] public string Description { get; set; } = string.Empty;
}

public sealed class OptimizationFixResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OptimizationRollbackState? RollbackState { get; set; }
}

/// <summary>
/// The action performed on an optimization fix.
/// </summary>
public enum OptimizationExecutionAction
{
    Applied = 0,
    Reverted = 1,
    Failed = 2
}

/// <summary>
/// A durable record of an optimization fix execution outcome.
/// </summary>
[ProtoContract]
public sealed class OptimizationExecutionRecord
{
    [ProtoMember(1)] public string RecordId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public string PlanId { get; set; } = string.Empty;
    [ProtoMember(3)] public OptimizationKind FixKind { get; set; }
    [ProtoMember(4)] public string Target { get; set; } = string.Empty;
    [ProtoMember(5)] public OptimizationExecutionAction Action { get; set; }
    [ProtoMember(6)] public bool Success { get; set; }
    [ProtoMember(7)] public bool IsReversible { get; set; }
    [ProtoMember(8)] public string RollbackNote { get; set; } = string.Empty;
    [ProtoMember(9)] public string Message { get; set; } = string.Empty;
    [ProtoMember(10)] public DateTime CreatedUtc { get; set; }
}

[ProtoContract]
public sealed class OptimizationFinding
{
    [ProtoMember(1)] public string FindingId { get; set; } = Guid.NewGuid().ToString("N");
    [ProtoMember(2)] public OptimizationKind Kind { get; set; }
    [ProtoMember(3)] public string Target { get; set; } = string.Empty;
    [ProtoMember(4)] public string Evidence { get; set; } = string.Empty;
    [ProtoMember(5)] public bool CanAutoFix { get; set; }
    [ProtoMember(6)] public bool RequiresApproval { get; set; }
    [ProtoMember(7)] public string RollbackPlan { get; set; } = string.Empty;
}

// ── Optimization execution rollup (C-041) ─────────────────────────────────

/// <summary>
/// A bounded rollup summary of optimization execution history grouped by kind.
/// </summary>
[ProtoContract]
public sealed class OptimizationExecutionRollup
{
    [ProtoMember(1)] public OptimizationKind Kind { get; set; }
    [ProtoMember(2)] public int TotalCount { get; set; }
    [ProtoMember(3)] public int AppliedCount { get; set; }
    [ProtoMember(4)] public int RevertedCount { get; set; }
    [ProtoMember(5)] public int FailedCount { get; set; }
    [ProtoMember(6)] public int ReversibleCount { get; set; }
    [ProtoMember(7)] public string MostRecentUtc { get; set; } = string.Empty;
}

/// <summary>
/// A compact summary projection of one execution record for listing.
/// </summary>
[ProtoContract]
public sealed class OptimizationExecutionSummary
{
    [ProtoMember(1)] public string RecordId { get; set; } = string.Empty;
    [ProtoMember(2)] public OptimizationKind Kind { get; set; }
    [ProtoMember(3)] public string Target { get; set; } = string.Empty;
    [ProtoMember(4)] public OptimizationExecutionAction Action { get; set; }
    [ProtoMember(5)] public bool Success { get; set; }
    [ProtoMember(6)] public bool IsReversible { get; set; }
    [ProtoMember(7)] public bool HasRollbackData { get; set; }
    [ProtoMember(8)] public string CreatedUtc { get; set; } = string.Empty;
}