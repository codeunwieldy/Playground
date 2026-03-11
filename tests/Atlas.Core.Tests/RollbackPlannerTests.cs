using Atlas.Core.Contracts;
using Atlas.Core.Planning;

namespace Atlas.Core.Tests;

public sealed class RollbackPlannerTests
{
    [Fact]
    public void BuildsInverseMoveAndRestoreOperationsInReverseOrder()
    {
        var batch = new ExecutionBatch
        {
            BatchId = "batch-01",
            PlanId = "plan-01",
            Operations =
            {
                new PlanOperation
                {
                    Kind = OperationKind.MovePath,
                    SourcePath = @"C:\Users\jscel\Documents\a.txt",
                    DestinationPath = @"C:\Users\jscel\Documents\Archive\a.txt"
                },
                new PlanOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = @"C:\Users\jscel\Documents\dup.txt",
                    DestinationPath = @"C:\.atlas-quarantine\20260311\dup.txt"
                }
            }
        };

        var planner = new RollbackPlanner();
        var checkpoint = planner.BuildCheckpoint(batch);

        Assert.Equal(2, checkpoint.InverseOperations.Count);
        Assert.Equal(OperationKind.RestoreFromQuarantine, checkpoint.InverseOperations[0].Kind);
        Assert.Equal(OperationKind.MovePath, checkpoint.InverseOperations[1].Kind);
        Assert.Equal(@"C:\Users\jscel\Documents\a.txt", checkpoint.InverseOperations[1].DestinationPath);
    }
}