namespace Atlas.Service.Services;

public sealed class AtlasServiceOptions
{
    public const string SectionName = "Atlas";

    public string PipeName { get; set; } = "AtlasFileIntelligenceV1";
    public int MaxFilesPerScan { get; set; } = 25000;
    public string QuarantineFolderName { get; set; } = ".atlas-quarantine";
    public bool EnableDryRunByDefault { get; set; } = true;
}