namespace Atlas.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DataRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Atlas",
        "FileIntelligence");

    public string DatabaseFileName { get; set; } = "atlas.db";
    public int QuarantineRetentionDays { get; set; } = 30;
}