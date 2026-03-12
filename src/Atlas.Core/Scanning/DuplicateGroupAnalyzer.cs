using Atlas.Core.Contracts;

namespace Atlas.Core.Scanning;

/// <summary>
/// A single signal that contributed to a duplicate group's risk or confidence assessment.
/// </summary>
public sealed record DuplicateEvidence(string Signal, string Detail);

/// <summary>
/// The outcome of duplicate group analysis: match confidence, cleanup confidence,
/// canonical rationale, and risk evidence.
/// </summary>
public sealed record DuplicateAnalysisResult(
    double MatchConfidence,
    double CleanupConfidence,
    string CanonicalReason,
    bool HasSensitiveMembers,
    bool HasSyncManagedMembers,
    bool HasProtectedMembers,
    SensitivityLevel MaxSensitivity,
    IReadOnlyList<DuplicateEvidence> Evidence);

/// <summary>
/// Bounded, deterministic duplicate group analyzer that evaluates evidence strength
/// and risk for a group of duplicate candidates.
/// </summary>
public static class DuplicateGroupAnalyzer
{
    private const double FullHashBaseConfidence = 0.999;
    private const double QuickHashOnlyConfidence = 0.85;
    private const double FingerprintAgreementBoost = 0.0005;

    private const double SensitivePenalty = 0.08;
    private const double SyncManagedPenalty = 0.04;
    private const double ProtectedMemberPenalty = 0.10;
    private const double CriticalSensitivityPenalty = 0.15;

    /// <summary>
    /// Analyzes a duplicate group given its member inventory items and hash verification level.
    /// </summary>
    /// <param name="members">The inventory items in the group.</param>
    /// <param name="isFullHashVerified">True when the group was confirmed via full-file SHA-256 hash.</param>
    /// <param name="canonical">The item selected as canonical by <see cref="DuplicateCanonicalSelector"/>.</param>
    public static DuplicateAnalysisResult Analyze(
        IReadOnlyList<FileInventoryItem> members,
        bool isFullHashVerified,
        FileInventoryItem canonical)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(canonical);

        if (members.Count < 2)
            throw new ArgumentException("A duplicate group must have at least two members.", nameof(members));

        var evidence = new List<DuplicateEvidence>();

        // 1. Match confidence based on hash verification level
        double matchConfidence;
        if (isFullHashVerified)
        {
            matchConfidence = FullHashBaseConfidence;
            evidence.Add(new DuplicateEvidence("FullHashMatch", "All members share identical SHA-256 full-file hash"));
        }
        else
        {
            matchConfidence = QuickHashOnlyConfidence;
            evidence.Add(new DuplicateEvidence("QuickHashOnly", "Members matched by partial hash only; full verification incomplete"));
        }

        // 2. Content fingerprint agreement boost
        var fingerprints = members
            .Select(static m => m.ContentFingerprint)
            .Where(static fp => !string.IsNullOrWhiteSpace(fp))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fingerprints.Count == 1 && members.All(m => !string.IsNullOrWhiteSpace(m.ContentFingerprint)))
        {
            matchConfidence = Math.Min(1.0, matchConfidence + FingerprintAgreementBoost);
            evidence.Add(new DuplicateEvidence("FingerprintAgreement", "All members share identical content header fingerprint"));
        }

        // 3. Size agreement (should always be true for well-formed groups, but verify)
        var distinctSizes = members.Select(static m => m.SizeBytes).Distinct().Count();
        if (distinctSizes == 1)
        {
            evidence.Add(new DuplicateEvidence("SizeMatch", $"All members are {members[0].SizeBytes:N0} bytes"));
        }

        // 4. Canonical selection rationale
        var canonicalReason = BuildCanonicalReason(canonical, members);

        // 5. Risk analysis
        var hasSensitive = members.Any(static m => m.Sensitivity > SensitivityLevel.Low);
        var hasSyncManaged = members.Any(static m => m.IsSyncManaged);
        var hasProtected = members.Any(static m => m.IsProtectedByUser);
        var maxSensitivity = members.Max(static m => m.Sensitivity);

        // 6. Cleanup confidence: start from match confidence, apply risk penalties
        var cleanupConfidence = matchConfidence;

        if (hasProtected)
        {
            cleanupConfidence -= ProtectedMemberPenalty;
            evidence.Add(new DuplicateEvidence("ProtectedMember", "Group contains user-protected file(s)"));
        }

        if (maxSensitivity == SensitivityLevel.Critical)
        {
            cleanupConfidence -= CriticalSensitivityPenalty;
            evidence.Add(new DuplicateEvidence("CriticalSensitivity", "Group contains file(s) with critical sensitivity"));
        }
        else if (hasSensitive)
        {
            cleanupConfidence -= SensitivePenalty;
            var sensitiveCount = members.Count(static m => m.Sensitivity > SensitivityLevel.Low);
            evidence.Add(new DuplicateEvidence("SensitiveMember",
                $"Group contains {sensitiveCount} file(s) above low sensitivity (max: {maxSensitivity})"));
        }

        if (hasSyncManaged)
        {
            cleanupConfidence -= SyncManagedPenalty;
            evidence.Add(new DuplicateEvidence("SyncManagedMember", "Group contains sync-managed file(s)"));
        }

        cleanupConfidence = Math.Max(0.0, cleanupConfidence);

        return new DuplicateAnalysisResult(
            MatchConfidence: Math.Round(matchConfidence, 4),
            CleanupConfidence: Math.Round(cleanupConfidence, 4),
            CanonicalReason: canonicalReason,
            HasSensitiveMembers: hasSensitive,
            HasSyncManagedMembers: hasSyncManaged,
            HasProtectedMembers: hasProtected,
            MaxSensitivity: maxSensitivity,
            Evidence: evidence);
    }

    private static string BuildCanonicalReason(FileInventoryItem canonical, IReadOnlyList<FileInventoryItem> members)
    {
        var reasons = new List<string>();

        if (canonical.IsProtectedByUser)
            reasons.Add("user-protected");

        if (canonical.IsSyncManaged)
            reasons.Add("sync-managed");

        if (canonical.Sensitivity >= SensitivityLevel.High)
            reasons.Add($"high sensitivity ({canonical.Sensitivity})");

        if (!string.IsNullOrWhiteSpace(canonical.ContentFingerprint))
            reasons.Add("has content fingerprint");

        // Check if canonical is in a preferred location
        var preferredMarkers = new[] { @"\documents\", @"\desktop\", @"\pictures\", @"\music\", @"\videos\", @"\projects\", @"\work\" };
        var lowPriorityMarkers = new[] { @"\downloads\", @"\temp\", @"\tmp\", @"\cache\", @"\archive\" };

        var isPreferred = preferredMarkers.Any(m => canonical.Path.Contains(m, StringComparison.OrdinalIgnoreCase));
        var isLowPriority = lowPriorityMarkers.Any(m => canonical.Path.Contains(m, StringComparison.OrdinalIgnoreCase));

        if (isPreferred)
            reasons.Add("preferred location");
        else if (!isLowPriority)
            reasons.Add("stable location");

        if (reasons.Count == 0)
            reasons.Add("highest composite safety score");

        return string.Join("; ", reasons);
    }
}
