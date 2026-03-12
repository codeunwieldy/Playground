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
}