using Atlas.Core.Contracts;
using Atlas.Core.Planning;

namespace Atlas.Core.Tests;

public sealed class DuplicateActionEvaluatorTests
{
    [Fact]
    public void Evaluate_EligibleGroup_ReturnsQuarantineDuplicates()
    {
        var planResult = new DuplicateCleanupPlanResult { ActionableGroups = 1, ConsideredGroups = 1 };
        planResult.Operations.Add(new PlanOperation
        {
            Kind = OperationKind.DeleteToQuarantine,
            SourcePath = @"C:\Downloads\report-copy.pdf"
        });

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: false,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.True(result.IsCleanupEligible);
        Assert.False(result.RequiresReview);
        Assert.Equal(DuplicateActionPosture.QuarantineDuplicates, result.RecommendedPosture);
        Assert.Empty(result.BlockedReasons);
        Assert.NotEmpty(result.ActionNotes);
    }

    [Fact]
    public void Evaluate_SensitiveMembers_ReturnsReview()
    {
        var planResult = new DuplicateCleanupPlanResult { ConsideredGroups = 1, SkippedSensitive = 2 };

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: false,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.False(result.IsCleanupEligible);
        Assert.True(result.RequiresReview);
        Assert.Equal(DuplicateActionPosture.Review, result.RecommendedPosture);
        Assert.Contains(result.BlockedReasons, r => r.Contains("elevated sensitivity"));
    }

    [Fact]
    public void Evaluate_SyncManagedMembers_ReturnsReview()
    {
        var planResult = new DuplicateCleanupPlanResult { ConsideredGroups = 1, SkippedSyncManaged = 1 };

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: false,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.False(result.IsCleanupEligible);
        Assert.True(result.RequiresReview);
        Assert.Equal(DuplicateActionPosture.Review, result.RecommendedPosture);
        Assert.Contains(result.BlockedReasons, r => r.Contains("sync-managed"));
    }

    [Fact]
    public void Evaluate_ProtectedMembers_ReturnsReview()
    {
        var planResult = new DuplicateCleanupPlanResult { ConsideredGroups = 1 };

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: true,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.False(result.IsCleanupEligible);
        Assert.True(result.RequiresReview);
        Assert.Equal(DuplicateActionPosture.Review, result.RecommendedPosture);
        Assert.Contains(result.BlockedReasons, r => r.Contains("protected by user policy"));
    }

    [Fact]
    public void Evaluate_LowConfidence_ReturnsKeep()
    {
        var planResult = new DuplicateCleanupPlanResult();

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: false,
            groupConfidence: 0.80, confidenceThreshold: 0.985);

        Assert.False(result.IsCleanupEligible);
        Assert.False(result.RequiresReview);
        Assert.Equal(DuplicateActionPosture.Keep, result.RecommendedPosture);
        Assert.Contains(result.BlockedReasons, r => r.Contains("below policy threshold"));
    }

    [Fact]
    public void Evaluate_AllMembersBlocked_ReturnsKeep()
    {
        var planResult = new DuplicateCleanupPlanResult { ConsideredGroups = 1, SkippedMissingInventory = 3 };

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: false,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.False(result.IsCleanupEligible);
        Assert.False(result.RequiresReview);
        Assert.Equal(DuplicateActionPosture.Keep, result.RecommendedPosture);
        Assert.Contains(result.BlockedReasons, r => r.Contains("missing from inventory"));
    }

    [Fact]
    public void Evaluate_BlockedReasons_AreBounded()
    {
        var planResult = new DuplicateCleanupPlanResult
        {
            ConsideredGroups = 1,
            SkippedSensitive = 5,
            SkippedSyncManaged = 3,
            SkippedProtectedByUser = 2
        };

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: true,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.InRange(result.BlockedReasons.Count, 1, DuplicateActionEvaluator.MaxBlockedReasons);
    }

    [Fact]
    public void Evaluate_ActionNotes_AreBounded()
    {
        var planResult = new DuplicateCleanupPlanResult { ActionableGroups = 1, ConsideredGroups = 1 };
        planResult.Operations.Add(new PlanOperation());

        var result = DuplicateActionEvaluator.Evaluate(
            planResult, hasProtectedMembers: false,
            groupConfidence: 0.995, confidenceThreshold: 0.985);

        Assert.InRange(result.ActionNotes.Count, 1, DuplicateActionEvaluator.MaxActionNotes);
    }

    [Fact]
    public void Evaluate_NullPlanResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DuplicateActionEvaluator.Evaluate(null!, false, 0.995, 0.985));
    }
}
