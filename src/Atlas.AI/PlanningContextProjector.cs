using Atlas.Core.Contracts;

namespace Atlas.AI;

public sealed class PlanningContextProjector
{
    private readonly PlanningInventoryProjector inventoryProjector = new();

    public PlanningContextProjection Project(PlanRequest request, int maxInventoryItems)
    {
        ArgumentNullException.ThrowIfNull(request);

        var projectedInventory = inventoryProjector.Project(request.Scan.Inventory, maxInventoryItems).ToList();
        var inventoryByPath = request.Scan.Inventory
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new PlanningContextProjection
        {
            Inventory = projectedInventory,
            InventorySummary = BuildInventorySummary(request.Scan.Inventory, projectedInventory),
            Duplicates = BuildDuplicateProjection(
                request.Scan.Duplicates,
                inventoryByPath,
                request.PolicyProfile.DuplicateAutoDeleteConfidenceThreshold),
            VolumeSummary = BuildVolumeSummary(request.Scan.Volumes)
        };
    }

    private static PlanningInventorySummary BuildInventorySummary(
        IReadOnlyCollection<FileInventoryItem> inventory,
        IReadOnlyCollection<PlanningInventoryProjection> projectedInventory)
    {
        return new PlanningInventorySummary
        {
            TotalInventoryCount = inventory.Count,
            SelectedItemCount = projectedInventory.Count,
            DuplicateCandidateCount = inventory.Count(static item => item.IsDuplicateCandidate),
            SyncManagedCount = inventory.Count(static item => item.IsSyncManaged),
            ProtectedItemCount = inventory.Count(static item => item.IsProtectedByUser),
            ContentIdentifiedCount = inventory.Count(static item => !string.IsNullOrWhiteSpace(item.ContentFingerprint)),
            Sensitivity = BuildSensitivitySummary(inventory),
            TopCategories = BuildTopCounts(
                inventory.Select(static item => item.Category),
                maxItems: 6),
            TopMimeTypes = BuildTopCounts(
                inventory.Select(static item => item.MimeType),
                maxItems: 6)
        };
    }

    private static PlanningDuplicateProjection BuildDuplicateProjection(
        IEnumerable<DuplicateGroup> duplicates,
        IReadOnlyDictionary<string, FileInventoryItem> inventoryByPath,
        double confidenceThreshold)
    {
        var duplicateList = duplicates
            .Where(static group => group is not null)
            .ToList();

        var projectedGroups = duplicateList
            .Select(group => BuildDuplicateGroupProjection(group, inventoryByPath))
            .Where(static group => group is not null)
            .Cast<PlanningDuplicateGroupProjection>()
            .OrderByDescending(static group => group.HasProtectedMember)
            .ThenByDescending(static group => group.HasSensitiveMember)
            .ThenByDescending(static group => group.HasSyncManagedMember)
            .ThenByDescending(static group => group.Confidence)
            .ThenByDescending(static group => group.MatchConfidence)
            .ThenByDescending(static group => group.DuplicatePathCount)
            .Take(20)
            .ToList();

        return new PlanningDuplicateProjection
        {
            TotalDuplicateGroupCount = duplicateList.Count,
            HighConfidenceGroupCount = duplicateList.Count(group => group.Confidence >= confidenceThreshold),
            SelectedGroupCount = projectedGroups.Count,
            Groups = projectedGroups
        };
    }

    private static PlanningDuplicateGroupProjection? BuildDuplicateGroupProjection(
        DuplicateGroup group,
        IReadOnlyDictionary<string, FileInventoryItem> inventoryByPath)
    {
        if (string.IsNullOrWhiteSpace(group.CanonicalPath))
        {
            return null;
        }

        inventoryByPath.TryGetValue(group.CanonicalPath, out var canonical);

        var duplicateItems = group.Paths
            .Where(path => !string.Equals(path, group.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            .Select(path => inventoryByPath.TryGetValue(path, out var item) ? item : null)
            .Where(static item => item is not null)
            .Cast<FileInventoryItem>()
            .ToList();

        var maxSensitivity = group.MaxSensitivity != SensitivityLevel.Unknown
            ? group.MaxSensitivity
            : duplicateItems
                .Append(canonical)
                .Where(static item => item is not null)
                .Cast<FileInventoryItem>()
                .Select(static item => item.Sensitivity)
                .DefaultIfEmpty(SensitivityLevel.Low)
                .Max();

        var hasSensitiveMember = group.HasSensitiveMembers
            || (canonical?.Sensitivity ?? SensitivityLevel.Low) >= SensitivityLevel.High
            || duplicateItems.Any(static item => item.Sensitivity >= SensitivityLevel.High);
        var hasSyncManagedMember = group.HasSyncManagedMembers
            || (canonical?.IsSyncManaged ?? false)
            || duplicateItems.Any(static item => item.IsSyncManaged);
        var hasProtectedMember = group.HasProtectedMembers
            || (canonical?.IsProtectedByUser ?? false)
            || duplicateItems.Any(static item => item.IsProtectedByUser);

        return new PlanningDuplicateGroupProjection
        {
            GroupId = group.GroupId,
            CanonicalPath = group.CanonicalPath,
            CanonicalCategory = canonical?.Category ?? string.Empty,
            CanonicalMimeType = canonical?.MimeType ?? string.Empty,
            Confidence = group.Confidence,
            MatchConfidence = group.MatchConfidence > 0 ? group.MatchConfidence : group.Confidence,
            CanonicalReason = group.CanonicalReason,
            DuplicatePathCount = Math.Max(0, group.Paths.Count - 1),
            DuplicatePaths = group.Paths
                .Where(path => !string.Equals(path, group.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToList(),
            HasSensitiveMember = hasSensitiveMember,
            HasSyncManagedMember = hasSyncManagedMember,
            HasProtectedMember = hasProtectedMember,
            MaxSensitivity = maxSensitivity
        };
    }

    private static PlanningVolumeSummary BuildVolumeSummary(IEnumerable<VolumeSnapshot> volumes)
    {
        var volumeList = volumes.ToList();
        return new PlanningVolumeSummary
        {
            TotalVolumeCount = volumeList.Count,
            ReadyVolumeCount = volumeList.Count(static volume => volume.IsReady),
            LowFreeSpaceVolumeCount = volumeList.Count(static volume =>
                volume.TotalSizeBytes > 0 &&
                (double)volume.FreeSpaceBytes / volume.TotalSizeBytes <= 0.15d)
        };
    }

    private static PlanningSensitivitySummary BuildSensitivitySummary(IEnumerable<FileInventoryItem> inventory)
    {
        return new PlanningSensitivitySummary
        {
            Critical = inventory.Count(static item => item.Sensitivity == SensitivityLevel.Critical),
            High = inventory.Count(static item => item.Sensitivity == SensitivityLevel.High),
            Medium = inventory.Count(static item => item.Sensitivity == SensitivityLevel.Medium),
            Low = inventory.Count(static item => item.Sensitivity == SensitivityLevel.Low),
            Unknown = inventory.Count(static item => item.Sensitivity == SensitivityLevel.Unknown)
        };
    }

    private static List<PlanningNamedCount> BuildTopCounts(IEnumerable<string> values, int maxItems)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new PlanningNamedCount
            {
                Name = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }
}

public sealed class PlanningContextProjection
{
    public IReadOnlyList<PlanningInventoryProjection> Inventory { get; init; } = Array.Empty<PlanningInventoryProjection>();
    public PlanningInventorySummary InventorySummary { get; init; } = new();
    public PlanningDuplicateProjection Duplicates { get; init; } = new();
    public PlanningVolumeSummary VolumeSummary { get; init; } = new();
}

public sealed class PlanningInventorySummary
{
    public int TotalInventoryCount { get; init; }
    public int SelectedItemCount { get; init; }
    public int DuplicateCandidateCount { get; init; }
    public int SyncManagedCount { get; init; }
    public int ProtectedItemCount { get; init; }
    public int ContentIdentifiedCount { get; init; }
    public PlanningSensitivitySummary Sensitivity { get; init; } = new();
    public IReadOnlyList<PlanningNamedCount> TopCategories { get; init; } = Array.Empty<PlanningNamedCount>();
    public IReadOnlyList<PlanningNamedCount> TopMimeTypes { get; init; } = Array.Empty<PlanningNamedCount>();
}

public sealed class PlanningSensitivitySummary
{
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Unknown { get; init; }
}

public sealed class PlanningNamedCount
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class PlanningDuplicateProjection
{
    public int TotalDuplicateGroupCount { get; init; }
    public int HighConfidenceGroupCount { get; init; }
    public int SelectedGroupCount { get; init; }
    public IReadOnlyList<PlanningDuplicateGroupProjection> Groups { get; init; } = Array.Empty<PlanningDuplicateGroupProjection>();
}

public sealed class PlanningDuplicateGroupProjection
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
}

public sealed class PlanningVolumeSummary
{
    public int TotalVolumeCount { get; init; }
    public int ReadyVolumeCount { get; init; }
    public int LowFreeSpaceVolumeCount { get; init; }
}
