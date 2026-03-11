using Atlas.Core.Contracts;

namespace Atlas.Core.Policies;

public sealed class PathSafetyClassifier
{
    public string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    public bool IsProtectedPath(PolicyProfile profile, string path)
    {
        var normalized = Normalize(path);
        return profile.ProtectedPaths.Any(candidate => IsSameOrChildPath(normalized, Normalize(candidate)));
    }

    public bool IsExcludedPath(PolicyProfile profile, string path)
    {
        var normalized = Normalize(path);
        return profile.ExcludedRoots.Any(candidate => IsSameOrChildPath(normalized, Normalize(candidate)));
    }

    public bool IsMutablePath(PolicyProfile profile, string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return profile.MutableRoots.Any(candidate => IsSameOrChildPath(normalized, Normalize(candidate)));
    }

    public bool IsSyncManaged(PolicyProfile profile, string path)
    {
        var normalized = Normalize(path);
        return profile.SyncFolderMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSameOrChildPath(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}