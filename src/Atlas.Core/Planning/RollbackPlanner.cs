using Atlas.Core.Contracts;

namespace Atlas.Core.Planning;

public sealed class RollbackPlanner
{
    public UndoCheckpoint BuildCheckpoint(ExecutionBatch batch, IReadOnlyCollection<QuarantineItem>? quarantineItems = null)
    {
        var checkpoint = new UndoCheckpoint
        {
            BatchId = batch.BatchId,
            Notes = new List<string>
            {
                $"Created from plan {batch.PlanId}",
                batch.RequiresCheckpoint ? "VSS checkpoint requested before execution." : "Inverse operations only."
            }
        };

        foreach (var operation in Enumerable.Reverse(batch.Operations))
        {
            checkpoint.InverseOperations.AddRange(CreateInverseOperations(operation));
        }

        if (quarantineItems is not null)
        {
            checkpoint.QuarantineItems.AddRange(quarantineItems);
        }

        return checkpoint;
    }

    private static IEnumerable<InverseOperation> CreateInverseOperations(PlanOperation operation)
    {
        switch (operation.Kind)
        {
            case OperationKind.CreateDirectory:
                yield return new InverseOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = operation.DestinationPath,
                    Description = $"Delete created directory {operation.DestinationPath} during rollback."
                };
                break;
            case OperationKind.MovePath:
                yield return new InverseOperation
                {
                    Kind = OperationKind.MovePath,
                    SourcePath = operation.DestinationPath,
                    DestinationPath = operation.SourcePath,
                    Description = $"Move {operation.DestinationPath} back to {operation.SourcePath}."
                };
                break;
            case OperationKind.RenamePath:
                yield return new InverseOperation
                {
                    Kind = OperationKind.RenamePath,
                    SourcePath = operation.DestinationPath,
                    DestinationPath = operation.SourcePath,
                    Description = $"Rename {operation.DestinationPath} back to {operation.SourcePath}."
                };
                break;
            case OperationKind.DeleteToQuarantine:
                yield return new InverseOperation
                {
                    Kind = OperationKind.RestoreFromQuarantine,
                    SourcePath = operation.DestinationPath,
                    DestinationPath = operation.SourcePath,
                    Description = $"Restore {operation.SourcePath} from quarantine."
                };
                break;
            case OperationKind.RestoreFromQuarantine:
                yield return new InverseOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = operation.DestinationPath,
                    DestinationPath = operation.SourcePath,
                    Description = $"Return restored path {operation.DestinationPath} to quarantine."
                };
                break;
            case OperationKind.ApplyOptimizationFix:
                yield return new InverseOperation
                {
                    Kind = OperationKind.RevertOptimizationFix,
                    SourcePath = operation.SourcePath,
                    DestinationPath = operation.DestinationPath,
                    Description = $"Revert optimization change for {operation.SourcePath}."
                };
                break;
            case OperationKind.RevertOptimizationFix:
                yield return new InverseOperation
                {
                    Kind = OperationKind.ApplyOptimizationFix,
                    SourcePath = operation.SourcePath,
                    DestinationPath = operation.DestinationPath,
                    Description = $"Reapply optimization change for {operation.SourcePath}."
                };
                break;
            case OperationKind.MergeDuplicateGroup:
                yield return new InverseOperation
                {
                    Kind = OperationKind.RestoreFromQuarantine,
                    SourcePath = operation.DestinationPath,
                    DestinationPath = operation.SourcePath,
                    Description = "Restore duplicate-group members from checkpoint artifacts."
                };
                break;
        }
    }
}