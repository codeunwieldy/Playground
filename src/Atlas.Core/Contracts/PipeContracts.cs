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