using Atlas.Core.Contracts;

namespace Atlas.Core.Scanning;

public static class DuplicateCanonicalSelector
{
    private static readonly string[] PreferredLocationMarkers =
    [
        @"\documents\",
        @"\desktop\",
        @"\pictures\",
        @"\music\",
        @"\videos\",
        @"\projects\",
        @"\work\"
    ];

    private static readonly string[] LowPriorityLocationMarkers =
    [
        @"\downloads\",
        @"\temp\",
        @"\tmp\",
        @"\cache\",
        @"\archive\",
        @"\archives\",
        @"\duplicates\",
        @"\duplicate\",
        @"\trash\",
        @"\recycle",
        @"\.atlas-quarantine\"
    ];

    public static FileInventoryItem SelectCanonical(IEnumerable<FileInventoryItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var materialized = candidates
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Path))
            .ToList();

        if (materialized.Count == 0)
        {
            throw new ArgumentException("At least one duplicate candidate is required.", nameof(candidates));
        }

        return materialized
            .OrderByDescending(GetSafetyScore)
            .ThenByDescending(static item => item.LastModifiedUnixTimeSeconds)
            .ThenBy(static item => item.Path.Length)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int GetSafetyScore(FileInventoryItem item)
    {
        var score = 0;

        if (item.IsProtectedByUser)
        {
            score += 1000;
        }

        if (item.IsSyncManaged)
        {
            score += 120;
        }

        score += item.Sensitivity switch
        {
            SensitivityLevel.Critical => 500,
            SensitivityLevel.High => 375,
            SensitivityLevel.Medium => 225,
            SensitivityLevel.Low => 100,
            _ => 0
        };

        score += GetMetadataScore(item);
        score += GetLocationScore(item.Path);

        return score;
    }

    private static int GetMetadataScore(FileInventoryItem item)
    {
        var score = 0;

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

        if (!string.IsNullOrWhiteSpace(item.Extension))
        {
            score += 10;
        }

        return score;
    }

    private static int GetLocationScore(string path)
    {
        var score = 0;

        foreach (var marker in PreferredLocationMarkers)
        {
            if (path.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                score += 120;
            }
        }

        foreach (var marker in LowPriorityLocationMarkers)
        {
            if (path.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                score -= 90;
            }
        }

        return score;
    }
}
