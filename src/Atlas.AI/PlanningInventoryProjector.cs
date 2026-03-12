using Atlas.Core.Contracts;

namespace Atlas.AI;

public sealed class PlanningInventoryProjector
{
    public IReadOnlyList<PlanningInventoryProjection> Project(IEnumerable<FileInventoryItem> inventory, int maxItems)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var normalizedMax = Math.Max(1, maxItems);
        var candidates = inventory
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Path))
            .Select(item => new RankedProjection(item, BuildProjection(item), Score(item)))
            .OrderByDescending(static ranked => ranked.Score)
            .ThenByDescending(static ranked => ranked.Item.LastModifiedUnixTimeSeconds)
            .ThenBy(static ranked => ranked.Item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count <= normalizedMax)
        {
            return candidates.Select(static ranked => ranked.Projection).ToList();
        }

        var selected = new List<PlanningInventoryProjection>(normalizedMax);
        var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ranked in candidates)
        {
            if (selected.Count >= normalizedMax)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(ranked.Projection.Category))
            {
                continue;
            }

            if (selected.Any(existing => string.Equals(existing.Category, ranked.Projection.Category, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(ranked.Projection);
            selectedPaths.Add(ranked.Item.Path);
        }

        foreach (var ranked in candidates)
        {
            if (selected.Count >= normalizedMax)
            {
                break;
            }

            if (selectedPaths.Add(ranked.Item.Path))
            {
                selected.Add(ranked.Projection);
            }
        }

        return selected;
    }

    private static PlanningInventoryProjection BuildProjection(FileInventoryItem item)
    {
        return new PlanningInventoryProjection
        {
            Path = item.Path,
            Name = item.Name,
            Extension = item.Extension,
            Category = item.Category,
            MimeType = item.MimeType,
            SizeBytes = item.SizeBytes,
            LastModifiedUnixTimeSeconds = item.LastModifiedUnixTimeSeconds,
            Sensitivity = item.Sensitivity,
            IsSyncManaged = item.IsSyncManaged,
            IsDuplicateCandidate = item.IsDuplicateCandidate,
            IsProtectedByUser = item.IsProtectedByUser,
            HasContentFingerprint = !string.IsNullOrWhiteSpace(item.ContentFingerprint)
        };
    }

    private static int Score(FileInventoryItem item)
    {
        var score = item.Sensitivity switch
        {
            SensitivityLevel.Critical => 1200,
            SensitivityLevel.High => 900,
            SensitivityLevel.Medium => 600,
            SensitivityLevel.Low => 200,
            _ => 100
        };

        if (item.IsProtectedByUser)
        {
            score += 400;
        }

        if (item.IsSyncManaged)
        {
            score += 220;
        }

        if (item.IsDuplicateCandidate)
        {
            score += 140;
        }

        if (!string.IsNullOrWhiteSpace(item.ContentFingerprint))
        {
            score += 90;
        }

        if (!string.IsNullOrWhiteSpace(item.MimeType))
        {
            score += 45;
        }

        if (!string.IsNullOrWhiteSpace(item.Category) &&
            !string.Equals(item.Category, "Other", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        return score;
    }

    private sealed record RankedProjection(FileInventoryItem Item, PlanningInventoryProjection Projection, int Score);
}

public sealed class PlanningInventoryProjection
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long LastModifiedUnixTimeSeconds { get; set; }
    public SensitivityLevel Sensitivity { get; set; }
    public bool IsSyncManaged { get; set; }
    public bool IsDuplicateCandidate { get; set; }
    public bool IsProtectedByUser { get; set; }
    public bool HasContentFingerprint { get; set; }
}
