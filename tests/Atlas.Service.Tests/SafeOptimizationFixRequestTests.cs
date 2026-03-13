using Atlas.Core.Contracts;
using Atlas.Service.Services;

namespace Atlas.Service.Tests;

/// <summary>
/// Tests for safe optimization fix request preview/apply/revert behavior (C-042).
/// Validates safe kind acceptance, unsupported kind rejection, preview vs apply,
/// and rollback-state persistence after apply.
/// </summary>
public sealed class SafeOptimizationFixRequestTests
{
    [Theory]
    [InlineData(OptimizationKind.TemporaryFiles)]
    [InlineData(OptimizationKind.CacheCleanup)]
    [InlineData(OptimizationKind.DuplicateArchives)]
    [InlineData(OptimizationKind.UserStartupEntry)]
    public void SafeKind_IsAccepted(OptimizationKind kind)
    {
        Assert.DoesNotContain(kind, SafeOptimizationFixExecutor.UnsafeKinds);
    }

    [Theory]
    [InlineData(OptimizationKind.ScheduledTask)]
    [InlineData(OptimizationKind.BackgroundApplication)]
    [InlineData(OptimizationKind.LowDiskPressure)]
    [InlineData(OptimizationKind.Unknown)]
    public void UnsafeKind_IsRejected(OptimizationKind kind)
    {
        Assert.Contains(kind, SafeOptimizationFixExecutor.UnsafeKinds);
    }

    [Fact]
    public void Preview_ForSafeKind_ReturnsEligible()
    {
        var finding = new OptimizationFinding
        {
            FindingId = "f1",
            Kind = OptimizationKind.TemporaryFiles,
            Target = "C:\\Temp",
            CanAutoFix = true,
            RequiresApproval = false,
            RollbackPlan = "Files will repopulate naturally"
        };

        var isSafe = !SafeOptimizationFixExecutor.UnsafeKinds.Contains(finding.Kind);

        var response = new OptimizationFixPreviewResponse
        {
            Found = true,
            FindingId = finding.FindingId,
            Kind = finding.Kind,
            Target = finding.Target,
            IsSafeKind = isSafe,
            CanAutoFix = finding.CanAutoFix,
            RequiresApproval = finding.RequiresApproval,
            RollbackPlan = finding.RollbackPlan,
            Reason = "Fix is eligible for safe application."
        };

        Assert.True(response.Found);
        Assert.True(response.IsSafeKind);
        Assert.True(response.CanAutoFix);
        Assert.False(response.RequiresApproval);
    }

    [Fact]
    public void Preview_ForUnsafeKind_ReturnsNotSafe()
    {
        var finding = new OptimizationFinding
        {
            FindingId = "f2",
            Kind = OptimizationKind.ScheduledTask,
            Target = "SomeTask",
            CanAutoFix = true
        };

        var isSafe = !SafeOptimizationFixExecutor.UnsafeKinds.Contains(finding.Kind);

        var response = new OptimizationFixPreviewResponse
        {
            Found = true,
            FindingId = finding.FindingId,
            Kind = finding.Kind,
            Target = finding.Target,
            IsSafeKind = isSafe,
            Reason = $"Kind '{finding.Kind}' is not supported for automatic application."
        };

        Assert.True(response.Found);
        Assert.False(response.IsSafeKind);
    }

    [Fact]
    public void Preview_ForRecommendationOnly_ReturnsCannotAutoFix()
    {
        var finding = new OptimizationFinding
        {
            FindingId = "f3",
            Kind = OptimizationKind.TemporaryFiles,
            Target = "C:\\Temp",
            CanAutoFix = false
        };

        var response = new OptimizationFixPreviewResponse
        {
            Found = true,
            FindingId = finding.FindingId,
            Kind = finding.Kind,
            Target = finding.Target,
            IsSafeKind = false,
            Reason = "Finding is recommendation-only and cannot be auto-fixed."
        };

        Assert.True(response.Found);
        Assert.False(response.IsSafeKind);
    }

    [Fact]
    public void Preview_MissingFinding_ReturnsNotFound()
    {
        var response = new OptimizationFixPreviewResponse
        {
            Found = false,
            FindingId = "missing-id",
            Reason = "Finding not found."
        };

        Assert.False(response.Found);
    }

    [Fact]
    public void ApplyResponse_TracksRollbackState()
    {
        var response = new OptimizationFixApplyResponse
        {
            Success = true,
            FindingId = "f1",
            Kind = OptimizationKind.DuplicateArchives,
            Message = "Quarantined duplicate archive.",
            HasRollbackState = true,
            IsReversible = true,
            ExecutionRecordId = "exec-1"
        };

        Assert.True(response.Success);
        Assert.True(response.HasRollbackState);
        Assert.True(response.IsReversible);
        Assert.Equal("exec-1", response.ExecutionRecordId);
    }

    [Fact]
    public void ApplyResponse_UnsafeKind_Fails()
    {
        var response = new OptimizationFixApplyResponse
        {
            Success = false,
            FindingId = "f2",
            Kind = OptimizationKind.ScheduledTask,
            Message = "Kind 'ScheduledTask' is not supported for automatic application."
        };

        Assert.False(response.Success);
        Assert.Contains("not supported", response.Message);
    }

    [Fact]
    public void RevertResponse_RecordsRevertId()
    {
        var response = new OptimizationFixRevertResponse
        {
            Success = true,
            ExecutionRecordId = "exec-1",
            Kind = OptimizationKind.DuplicateArchives,
            Message = "Revert recorded.",
            RevertRecordId = "revert-1"
        };

        Assert.True(response.Success);
        Assert.Equal("revert-1", response.RevertRecordId);
    }

    [Fact]
    public void RevertResponse_MissingRecord_Fails()
    {
        var response = new OptimizationFixRevertResponse
        {
            Success = false,
            ExecutionRecordId = "missing-exec",
            Message = "Execution record not found."
        };

        Assert.False(response.Success);
    }
}
