using Atlas.Core.Contracts;

namespace Atlas.Core.Planning;

public sealed class SafeDuplicateCleanupPlanner
{
    public DuplicateCleanupPlanResult BuildOperations(
        IEnumerable<DuplicateGroup> duplicateGroups,
        IEnumerable<FileInventoryItem> inventory,
        double confidenceThreshold,
        int maxGroups = int.MaxValue,
        int maxOperationsPerGroup = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(duplicateGroups);
        ArgumentNullException.ThrowIfNull(inventory);

        var inventoryByPath = inventory
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new DuplicateCleanupPlanResult();

        foreach (var duplicate in duplicateGroups
                     .Where(group => group is not null && group.Confidence >= confidenceThreshold)
                     .Take(maxGroups))
        {
            result.ConsideredGroups++;

            var createdForGroup = 0;
            foreach (var duplicatePath in duplicate.Paths
                         .Where(path => !string.Equals(path, duplicate.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                         .Take(maxOperationsPerGroup))
            {
                if (!inventoryByPath.TryGetValue(duplicatePath, out var item))
                {
                    result.SkippedMissingInventory++;
                    continue;
                }

                if (item.IsProtectedByUser)
                {
                    result.SkippedProtectedByUser++;
                    continue;
                }

                if (item.IsSyncManaged)
                {
                    result.SkippedSyncManaged++;
                    continue;
                }

                if (item.Sensitivity != SensitivityLevel.Low)
                {
                    result.SkippedSensitive++;
                    continue;
                }

                result.Operations.Add(new PlanOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = item.Path,
                    Description = BuildDescription(item, duplicate.CanonicalPath),
                    Confidence = duplicate.Confidence,
                    MarksSafeDuplicate = true,
                    Sensitivity = item.Sensitivity,
                    GroupId = duplicate.GroupId
                });

                createdForGroup++;
            }

            if (createdForGroup > 0)
            {
                result.ActionableGroups++;
            }
        }

        return result;
    }

    private static string BuildDescription(FileInventoryItem item, string canonicalPath)
    {
        var category = string.IsNullOrWhiteSpace(item.Category) ? "file" : item.Category.ToLowerInvariant();
        var canonicalName = Path.GetFileName(canonicalPath);

        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            return $"Stage a low-risk duplicate {category} for quarantine after review.";
        }

        return $"Stage a low-risk duplicate {category} for quarantine while keeping '{canonicalName}' as the canonical copy.";
    }
}

public sealed class DuplicateCleanupPlanResult
{
    public List<PlanOperation> Operations { get; } = new();
    public int ConsideredGroups { get; set; }
    public int ActionableGroups { get; set; }
    public int SkippedSensitive { get; set; }
    public int SkippedSyncManaged { get; set; }
    public int SkippedProtectedByUser { get; set; }
    public int SkippedMissingInventory { get; set; }

    public bool HasSkippedRiskyCandidates =>
        SkippedSensitive > 0 || SkippedSyncManaged > 0 || SkippedProtectedByUser > 0;
}
