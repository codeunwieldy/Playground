namespace Atlas.Service.Services;

public sealed class AtlasServiceOptions
{
    public const string SectionName = "Atlas";

    public string PipeName { get; set; } = "AtlasFileIntelligenceV1";
    public int MaxFilesPerScan { get; set; } = 25000;
    public string QuarantineFolderName { get; set; } = ".atlas-quarantine";
    public bool EnableDryRunByDefault { get; set; } = true;

    /// <summary>Whether background delta scanning and rescan orchestration is enabled.</summary>
    public bool EnableRescanOrchestration { get; set; }

    /// <summary>Minimum interval between scheduled rescans of the same root.</summary>
    public TimeSpan RescanInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum number of roots to rescan per orchestration cycle.</summary>
    public int MaxRootsPerCycle { get; set; } = 5;

    /// <summary>Delay between orchestration cycles to prevent tight loops.</summary>
    public TimeSpan OrchestrationCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum changed paths for incremental composition. Beyond this, fall back to full rescan.</summary>
    public int MaxIncrementalPaths { get; set; } = 500;

    /// <summary>
    /// Maximum fraction of delta paths that can fail inspection before the composed session
    /// is considered too unsafe to retain. Above this threshold, Atlas falls back to a full rescan
    /// instead of persisting a degraded session. Range: 0.0 (no tolerance) to 1.0 (always retain).
    /// Default: 0.5 (50%).
    /// </summary>
    public double MaxDegradedRatio { get; set; } = 0.5;

    // ── Conversation compaction (C-038) ─────────────────────────────────

    /// <summary>Whether background conversation compaction is enabled.</summary>
    public bool EnableConversationCompaction { get; set; }

    /// <summary>Interval between compaction cycles.</summary>
    public TimeSpan CompactionInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Conversations older than this window are eligible for compaction.</summary>
    public TimeSpan CompactionRetentionWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Minimum message count for a conversation to be compactable.</summary>
    public int CompactionMinMessages { get; set; } = 10;

    /// <summary>Maximum candidates to process per compaction cycle.</summary>
    public int CompactionMaxCandidatesPerCycle { get; set; } = 50;
}