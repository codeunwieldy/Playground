using Atlas.Core.Contracts;

namespace Atlas.Core.Planning;

public static class DuplicateActionEvaluator
{
    public const int MaxBlockedReasons = 10;
    public const int MaxActionNotes = 10;

    public static DuplicateActionReviewResult Evaluate(
        DuplicateCleanupPlanResult planResult,
        bool hasProtectedMembers,
        double groupConfidence,
        double confidenceThreshold)
    {
        ArgumentNullException.ThrowIfNull(planResult);

        var blockedReasons = new List<string>();
        var actionNotes = new List<string>();
        DuplicateActionPosture posture;
        bool isEligible;
        bool requiresReview;

        // Branch 1: Confidence too low — the planner silently skips groups below
        // threshold (ConsideredGroups will be 0), so we check first.
        if (groupConfidence < confidenceThreshold)
        {
            posture = DuplicateActionPosture.Keep;
            isEligible = false;
            requiresReview = false;
            blockedReasons.Add(
                $"Group cleanup confidence ({groupConfidence:F3}) is below policy threshold ({confidenceThreshold:F3}).");
            actionNotes.Add("Confidence is too low for automated cleanup. No action recommended.");
        }
        // Branch 2: Risky members detected by planner OR protected flag from group metadata.
        // Note: file_snapshots does not persist IsProtectedByUser, so the planner may
        // not detect protected members from DB-loaded items. The group-level flag from
        // duplicate_groups.has_protected_members compensates.
        else if (planResult.HasSkippedRiskyCandidates || hasProtectedMembers)
        {
            posture = DuplicateActionPosture.Review;
            isEligible = false;
            requiresReview = true;

            if (planResult.SkippedSensitive > 0)
                blockedReasons.Add($"{planResult.SkippedSensitive} member(s) have elevated sensitivity.");
            if (planResult.SkippedSyncManaged > 0)
                blockedReasons.Add($"{planResult.SkippedSyncManaged} member(s) are sync-managed.");
            if (planResult.SkippedProtectedByUser > 0 || hasProtectedMembers)
                blockedReasons.Add("Member(s) are protected by user policy.");

            actionNotes.Add("Human review is required before cleanup can proceed.");
        }
        // Branch 3: Planner produced actionable operations with no risky skips.
        else if (planResult.ActionableGroups > 0)
        {
            posture = DuplicateActionPosture.QuarantineDuplicates;
            isEligible = true;
            requiresReview = false;
            actionNotes.Add(
                $"Group is eligible for cleanup. {planResult.Operations.Count} duplicate(s) can be quarantined.");
            actionNotes.Add("Atlas would keep the canonical copy and quarantine non-canonical members.");
        }
        // Branch 4: No actionable ops, no risky skips — all blocked for other reasons.
        else
        {
            posture = DuplicateActionPosture.Keep;
            isEligible = false;
            requiresReview = false;

            if (planResult.SkippedMissingInventory > 0)
                blockedReasons.Add($"{planResult.SkippedMissingInventory} member(s) are missing from inventory.");
            if (blockedReasons.Count == 0)
                blockedReasons.Add("No eligible members for cleanup.");

            actionNotes.Add("All non-canonical members are blocked. No cleanup action available.");
        }

        return new DuplicateActionReviewResult(
            IsCleanupEligible: isEligible,
            RequiresReview: requiresReview,
            RecommendedPosture: posture,
            BlockedReasons: blockedReasons.Take(MaxBlockedReasons).ToList(),
            ActionNotes: actionNotes.Take(MaxActionNotes).ToList());
    }
}

public sealed record DuplicateActionReviewResult(
    bool IsCleanupEligible,
    bool RequiresReview,
    DuplicateActionPosture RecommendedPosture,
    IReadOnlyList<string> BlockedReasons,
    IReadOnlyList<string> ActionNotes);
