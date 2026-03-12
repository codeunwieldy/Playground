using Atlas.Core.Contracts;

namespace Atlas.AI;

public sealed class PlanningPromptPayloadBuilder
{
    private readonly PlanningContextProjector contextProjector = new();

    public PlanningPromptPayload Build(PlanRequest request, int maxInventoryItems)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = contextProjector.Project(request, maxInventoryItems);
        var redactor = new SensitivePathRedactor(request);

        return new PlanningPromptPayload
        {
            UserIntent = request.UserIntent,
            PolicyProfile = request.PolicyProfile,
            Volumes = request.Scan.Volumes,
            VolumeProjection = context.VolumeSummary,
            DuplicateProjection = BuildDuplicateProjection(context.Duplicates, redactor),
            InventoryProjection = new PlanningInventoryProjectionEnvelope
            {
                Strategy = "priority-and-diversity sample biased toward sensitive, duplicate, sync-managed, protected, and content-identified files",
                Summary = context.InventorySummary
            },
            Inventory = context.Inventory.Select(redactor.RedactInventoryItem).ToList()
        };
    }

    private static PlanningPromptDuplicateProjection BuildDuplicateProjection(
        PlanningDuplicateProjection projection,
        SensitivePathRedactor redactor)
    {
        return new PlanningPromptDuplicateProjection
        {
            TotalDuplicateGroupCount = projection.TotalDuplicateGroupCount,
            HighConfidenceGroupCount = projection.HighConfidenceGroupCount,
            SelectedGroupCount = projection.SelectedGroupCount,
            Groups = projection.Groups.Select(group => new PlanningPromptDuplicateGroup
            {
                GroupId = group.GroupId,
                CanonicalPath = redactor.RedactPath(group.CanonicalPath),
                CanonicalCategory = group.CanonicalCategory,
                CanonicalMimeType = group.CanonicalMimeType,
                Confidence = group.Confidence,
                MatchConfidence = group.MatchConfidence,
                CanonicalReason = group.CanonicalReason,
                DuplicatePathCount = group.DuplicatePathCount,
                DuplicatePaths = group.DuplicatePaths.Select(redactor.RedactPath).ToList(),
                HasSensitiveMember = group.HasSensitiveMember,
                HasSyncManagedMember = group.HasSyncManagedMember,
                HasProtectedMember = group.HasProtectedMember,
                MaxSensitivity = group.MaxSensitivity,
                ContainsRedactedPaths = group.DuplicatePaths.Any(redactor.WillRedactPath)
                    || redactor.WillRedactPath(group.CanonicalPath)
            }).ToList()
        };
    }

    private sealed class SensitivePathRedactor
    {
        private readonly bool uploadSensitiveContent;
        private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileInventoryItem> inventoryByPath;
        private int aliasIndex;

        public SensitivePathRedactor(PlanRequest request)
        {
            uploadSensitiveContent = request.PolicyProfile.UploadSensitiveContent;
            inventoryByPath = request.Scan.Inventory
                .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Path))
                .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public PlanningPromptInventoryItem RedactInventoryItem(PlanningInventoryProjection item)
        {
            var pathRedacted = WillRedact(item.Sensitivity);

            return new PlanningPromptInventoryItem
            {
                ReferenceId = pathRedacted ? GetAlias(item.Path) : string.Empty,
                Path = pathRedacted ? GetAlias(item.Path) : item.Path,
                Name = pathRedacted ? BuildRedactedName(item.Extension) : item.Name,
                Extension = item.Extension,
                Category = item.Category,
                MimeType = item.MimeType,
                SizeBytes = item.SizeBytes,
                LastModifiedUnixTimeSeconds = item.LastModifiedUnixTimeSeconds,
                Sensitivity = item.Sensitivity,
                IsSyncManaged = item.IsSyncManaged,
                IsDuplicateCandidate = item.IsDuplicateCandidate,
                IsProtectedByUser = item.IsProtectedByUser,
                HasContentFingerprint = item.HasContentFingerprint,
                PathRedacted = pathRedacted
            };
        }

        public string RedactPath(string path)
        {
            return WillRedactPath(path) ? GetAlias(path) : path;
        }

        public bool WillRedactPath(string path)
        {
            if (uploadSensitiveContent || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!inventoryByPath.TryGetValue(path, out var item))
            {
                return false;
            }

            return WillRedact(item.Sensitivity);
        }

        private bool WillRedact(SensitivityLevel sensitivity) =>
            !uploadSensitiveContent && sensitivity is SensitivityLevel.High or SensitivityLevel.Critical;

        private string GetAlias(string path)
        {
            if (aliases.TryGetValue(path, out var alias))
            {
                return alias;
            }

            alias = $"sensitive-item-{++aliasIndex:D3}";
            aliases[path] = alias;
            return alias;
        }

        private static string BuildRedactedName(string extension) =>
            string.IsNullOrWhiteSpace(extension) ? "[redacted]" : $"[redacted]{extension}";
    }
}

public sealed class PlanningPromptPayload
{
    public string UserIntent { get; init; } = string.Empty;
    public PolicyProfile PolicyProfile { get; init; } = new();
    public IReadOnlyList<VolumeSnapshot> Volumes { get; init; } = Array.Empty<VolumeSnapshot>();
    public PlanningVolumeSummary VolumeProjection { get; init; } = new();
    public PlanningPromptDuplicateProjection DuplicateProjection { get; init; } = new();
    public PlanningInventoryProjectionEnvelope InventoryProjection { get; init; } = new();
    public IReadOnlyList<PlanningPromptInventoryItem> Inventory { get; init; } = Array.Empty<PlanningPromptInventoryItem>();
}

public sealed class PlanningInventoryProjectionEnvelope
{
    public string Strategy { get; init; } = string.Empty;
    public PlanningInventorySummary Summary { get; init; } = new();
}

public sealed class PlanningPromptInventoryItem
{
    public string ReferenceId { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public long LastModifiedUnixTimeSeconds { get; init; }
    public SensitivityLevel Sensitivity { get; init; }
    public bool IsSyncManaged { get; init; }
    public bool IsDuplicateCandidate { get; init; }
    public bool IsProtectedByUser { get; init; }
    public bool HasContentFingerprint { get; init; }
    public bool PathRedacted { get; init; }
}

public sealed class PlanningPromptDuplicateProjection
{
    public int TotalDuplicateGroupCount { get; init; }
    public int HighConfidenceGroupCount { get; init; }
    public int SelectedGroupCount { get; init; }
    public IReadOnlyList<PlanningPromptDuplicateGroup> Groups { get; init; } = Array.Empty<PlanningPromptDuplicateGroup>();
}

public sealed class PlanningPromptDuplicateGroup
{
    public string GroupId { get; init; } = string.Empty;
    public string CanonicalPath { get; init; } = string.Empty;
    public string CanonicalCategory { get; init; } = string.Empty;
    public string CanonicalMimeType { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public double MatchConfidence { get; init; }
    public string CanonicalReason { get; init; } = string.Empty;
    public int DuplicatePathCount { get; init; }
    public IReadOnlyList<string> DuplicatePaths { get; init; } = Array.Empty<string>();
    public bool HasSensitiveMember { get; init; }
    public bool HasSyncManagedMember { get; init; }
    public bool HasProtectedMember { get; init; }
    public SensitivityLevel MaxSensitivity { get; init; }
    public bool ContainsRedactedPaths { get; init; }
}
