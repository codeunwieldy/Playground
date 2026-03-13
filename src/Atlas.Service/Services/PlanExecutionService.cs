using System.Security.Cryptography;
using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Core.Policies;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Options;

namespace Atlas.Service.Services;

public sealed class PlanExecutionService(
    AtlasPolicyEngine policyEngine,
    RollbackPlanner rollbackPlanner,
    IOptions<AtlasServiceOptions> serviceOptions,
    IInventoryRepository inventoryRepository,
    SafeOptimizationFixExecutor optimizationFixExecutor,
    VssSnapshotOrchestrator vssSnapshotOrchestrator,
    IOptimizationRepository optimizationRepository)
{
    private static readonly int[] OperationOrder =
    [
        (int)OperationKind.CreateDirectory,
        (int)OperationKind.MovePath,
        (int)OperationKind.RenamePath,
        (int)OperationKind.RestoreFromQuarantine,
        (int)OperationKind.DeleteToQuarantine,
        (int)OperationKind.MergeDuplicateGroup,
        (int)OperationKind.ApplyOptimizationFix,
        (int)OperationKind.RevertOptimizationFix
    ];

    public async Task<ExecutionResponse> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        // Determine trust posture from the latest inventory session.
        var latestSession = await inventoryRepository.GetLatestSessionAsync(cancellationToken);
        var isTrustedSession = latestSession is null || latestSession.IsTrusted;

        // Trust gate: block live execution when the latest inventory session is degraded.
        // Preview/dry-run always remains available.
        if (request.Execute && !isTrustedSession)
        {
            return new ExecutionResponse
            {
                Success = false,
                Messages =
                [
                    "Execution blocked: retained inventory session is degraded (IsTrusted=false). " +
                    "Preview/dry-run remains available. " +
                    "Run a full rescan to restore trusted state before executing."
                ]
            };
        }

        var validation = policyEngine.ValidatePlan(request.PolicyProfile, new PlanGraph
        {
            PlanId = request.Batch.PlanId,
            Operations = request.Batch.Operations
        });

        if (!validation.IsAllowed)
        {
            return new ExecutionResponse
            {
                Success = false,
                Messages = validation.Decisions
                    .Where(static decision => !decision.Decision.IsAllowed)
                    .SelectMany(static decision => decision.Decision.RiskEnvelope.BlockedReasons)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        // Preflight: validate all operations before any mutation.
        var preflightErrors = RunPreflight(request.Batch.Operations);
        if (preflightErrors.Count > 0)
        {
            return new ExecutionResponse
            {
                Success = false,
                Messages = preflightErrors
            };
        }

        // Evaluate checkpoint eligibility (deterministic, always runs including dry-run).
        var eligibility = CheckpointEligibilityEvaluator.Evaluate(request.Batch, isTrustedSession);

        // Attempt VSS snapshot creation for eligible batches (C-036).
        var vssResult = vssSnapshotOrchestrator.TryCreateSnapshots(
            eligibility.Requirement,
            eligibility.CoveredVolumes,
            !request.Execute);

        // Required checkpoint + VSS failure → block live execution.
        if (eligibility.Requirement == CheckpointRequirement.Required
            && request.Execute
            && !vssResult.Success
            && vssResult.Status != VssSnapshotStatus.NotNeeded
            && vssResult.Status != VssSnapshotStatus.Skipped)
        {
            return new ExecutionResponse
            {
                Success = false,
                Messages =
                [
                    $"Execution blocked: VSS checkpoint was required but creation failed. {vssResult.Message}"
                ]
            };
        }

        // Sort operations into safe deterministic order.
        var ordered = OrderOperations(request.Batch.Operations);

        var quarantineItems = new List<QuarantineItem>();
        var optimizationRollbackStates = new List<OptimizationRollbackState>();
        var messages = new List<string>();
        var completedOperations = new List<PlanOperation>();

        foreach (var operation in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ExecuteOperation(operation, request.Execute, request.Batch.PlanId, quarantineItems, optimizationRollbackStates, messages);
                completedOperations.Add(operation);
            }
            catch (Exception ex)
            {
                messages.Add($"Operation {operation.Kind} failed for {operation.SourcePath}: {ex.Message}");

                // Build checkpoint from only the operations that actually completed.
                var partialBatch = new ExecutionBatch
                {
                    BatchId = request.Batch.BatchId,
                    PlanId = request.Batch.PlanId,
                    TouchedVolumes = request.Batch.TouchedVolumes,
                    RequiresCheckpoint = request.Batch.RequiresCheckpoint,
                    IsDryRun = request.Batch.IsDryRun,
                    Operations = completedOperations,
                    EstimatedImpact = request.Batch.EstimatedImpact
                };

                var partialCheckpoint = rollbackPlanner.BuildCheckpoint(partialBatch, quarantineItems);
                partialCheckpoint.OptimizationRollbackStates.AddRange(optimizationRollbackStates);
                partialCheckpoint.Notes.Add($"Partial failure: {completedOperations.Count} of {ordered.Count} operations completed before error.");
                PopulateCheckpointEligibility(partialCheckpoint, eligibility, request.Execute, vssResult);

                return new ExecutionResponse
                {
                    Success = false,
                    Messages = messages,
                    UndoCheckpoint = partialCheckpoint
                };
            }
        }

        var checkpoint = rollbackPlanner.BuildCheckpoint(request.Batch, quarantineItems);
        checkpoint.OptimizationRollbackStates.AddRange(optimizationRollbackStates);
        PopulateCheckpointEligibility(checkpoint, eligibility, request.Execute, vssResult);
        return new ExecutionResponse
        {
            Success = true,
            Messages = messages,
            UndoCheckpoint = checkpoint
        };
    }

    public Task<UndoResponse> UndoAsync(UndoRequest request, CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        if (!request.Execute)
        {
            messages.Add("Undo dry run complete.");
            return Task.FromResult(new UndoResponse { Success = true, Messages = messages });
        }

        foreach (var inverse in request.Checkpoint.InverseOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (inverse.Kind)
            {
                case OperationKind.MovePath:
                case OperationKind.RenamePath:
                case OperationKind.RestoreFromQuarantine:
                    ExecuteMoveLikeOperation(inverse.SourcePath, inverse.DestinationPath);
                    messages.Add(inverse.Description);
                    break;
                case OperationKind.DeleteToQuarantine:
                    DeletePath(inverse.SourcePath);
                    messages.Add(inverse.Description);
                    break;
                case OperationKind.RevertOptimizationFix:
                    RevertOptimizationFromCheckpoint(inverse, request.Checkpoint.OptimizationRollbackStates, messages);
                    break;
            }
        }

        return Task.FromResult(new UndoResponse { Success = true, Messages = messages });
    }

    /// <summary>
    /// Validates all operations before any mutation occurs.
    /// Returns an empty list if all operations pass preflight.
    /// </summary>
    internal static List<string> RunPreflight(List<PlanOperation> operations)
    {
        var errors = new List<string>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations)
        {
            // Check required paths by operation kind.
            switch (operation.Kind)
            {
                case OperationKind.CreateDirectory:
                    if (string.IsNullOrWhiteSpace(operation.DestinationPath))
                    {
                        errors.Add($"CreateDirectory requires a destination path (operation {operation.OperationId}).");
                    }
                    break;

                case OperationKind.MovePath:
                case OperationKind.RenamePath:
                    if (string.IsNullOrWhiteSpace(operation.SourcePath))
                    {
                        errors.Add($"{operation.Kind} requires a source path (operation {operation.OperationId}).");
                    }
                    if (string.IsNullOrWhiteSpace(operation.DestinationPath))
                    {
                        errors.Add($"{operation.Kind} requires a destination path (operation {operation.OperationId}).");
                    }
                    break;

                case OperationKind.DeleteToQuarantine:
                    if (string.IsNullOrWhiteSpace(operation.SourcePath))
                    {
                        errors.Add($"DeleteToQuarantine requires a source path (operation {operation.OperationId}).");
                    }
                    break;

                case OperationKind.RestoreFromQuarantine:
                    if (string.IsNullOrWhiteSpace(operation.SourcePath))
                    {
                        errors.Add($"RestoreFromQuarantine requires a source path (operation {operation.OperationId}).");
                    }
                    if (string.IsNullOrWhiteSpace(operation.DestinationPath))
                    {
                        errors.Add($"RestoreFromQuarantine requires a destination path (operation {operation.OperationId}).");
                    }
                    break;

                case OperationKind.ApplyOptimizationFix:
                case OperationKind.RevertOptimizationFix:
                    if (string.IsNullOrWhiteSpace(operation.SourcePath))
                    {
                        errors.Add($"{operation.Kind} requires a source path (operation {operation.OperationId}).");
                    }
                    break;
            }

            // Check source existence for operations that read from source.
            if (operation.Kind is OperationKind.MovePath or OperationKind.RenamePath
                or OperationKind.DeleteToQuarantine or OperationKind.RestoreFromQuarantine)
            {
                if (!string.IsNullOrWhiteSpace(operation.SourcePath)
                    && !File.Exists(operation.SourcePath) && !Directory.Exists(operation.SourcePath))
                {
                    errors.Add($"Source path does not exist: {operation.SourcePath} (operation {operation.OperationId}).");
                }
            }

            // Check destination collisions within the batch.
            if (!string.IsNullOrWhiteSpace(operation.DestinationPath)
                && operation.Kind is OperationKind.MovePath or OperationKind.RenamePath or OperationKind.RestoreFromQuarantine)
            {
                if (!destinations.Add(operation.DestinationPath))
                {
                    errors.Add($"Destination collision within batch: {operation.DestinationPath} (operation {operation.OperationId}).");
                }

                if (File.Exists(operation.DestinationPath) || Directory.Exists(operation.DestinationPath))
                {
                    errors.Add($"Destination already exists: {operation.DestinationPath} (operation {operation.OperationId}).");
                }
            }

            // Validate parent directories are creatable for destination paths.
            if (!string.IsNullOrWhiteSpace(operation.DestinationPath)
                && operation.Kind is OperationKind.MovePath or OperationKind.RenamePath
                    or OperationKind.RestoreFromQuarantine or OperationKind.CreateDirectory)
            {
                var parentDir = Path.GetDirectoryName(operation.DestinationPath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    var pathRoot = Path.GetPathRoot(parentDir);
                    if (!string.IsNullOrWhiteSpace(pathRoot) && !Directory.Exists(pathRoot))
                    {
                        errors.Add($"Drive root does not exist for destination: {operation.DestinationPath} (operation {operation.OperationId}).");
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Orders operations into a safe deterministic sequence:
    /// CreateDirectory first, then Move/Rename, then DeleteToQuarantine, then optimizations.
    /// Preserves relative order within each kind.
    /// </summary>
    internal static List<PlanOperation> OrderOperations(List<PlanOperation> operations)
    {
        return operations
            .Select((op, index) => (op, index))
            .OrderBy(pair =>
            {
                var kindIndex = Array.IndexOf(OperationOrder, (int)pair.op.Kind);
                return kindIndex < 0 ? OperationOrder.Length : kindIndex;
            })
            .ThenBy(pair => pair.index)
            .Select(pair => pair.op)
            .ToList();
    }

    /// <summary>
    /// Populates checkpoint eligibility and VSS snapshot metadata on the given UndoCheckpoint.
    /// </summary>
    private static void PopulateCheckpointEligibility(UndoCheckpoint checkpoint, CheckpointEligibilityResult eligibility, bool isLiveExecution, VssSnapshotResult vssResult)
    {
        checkpoint.CheckpointEligibility = eligibility.Requirement.ToString();
        checkpoint.EligibilityReason = string.Join(" ", eligibility.Reasons);
        checkpoint.CoveredVolumes = eligibility.CoveredVolumes;

        // Populate VSS snapshot truth (C-036).
        checkpoint.VssSnapshotCreated = vssResult.Success && vssResult.References.Count > 0;
        checkpoint.VssSnapshotReferences = vssResult.References
            .Select(r => $"{r.Volume}|{r.SnapshotId}|{r.CreatedUtc:o}")
            .ToList();

        if (vssResult.Status == VssSnapshotStatus.Success)
        {
            checkpoint.Notes.Add($"VSS snapshot(s) created for {vssResult.References.Count} volume(s).");
        }
        else if (vssResult.Status == VssSnapshotStatus.PartialCoverage)
        {
            checkpoint.Notes.Add($"Partial VSS coverage: {vssResult.Message}");
        }
        else if (vssResult.Status == VssSnapshotStatus.Unavailable)
        {
            checkpoint.Notes.Add($"VSS unavailable: {vssResult.Message}");
        }
        else if (vssResult.Status == VssSnapshotStatus.Failed && isLiveExecution)
        {
            checkpoint.Notes.Add($"VSS snapshot creation failed: {vssResult.Message}");
        }
    }

    private void ExecuteOperation(PlanOperation operation, bool execute, string planId, List<QuarantineItem> quarantineItems, List<OptimizationRollbackState> optimizationRollbackStates, List<string> messages)
    {
        if (!execute)
        {
            messages.Add($"Dry run: {operation.Kind} :: {operation.SourcePath} -> {operation.DestinationPath}");
            return;
        }

        switch (operation.Kind)
        {
            case OperationKind.CreateDirectory:
                Directory.CreateDirectory(operation.DestinationPath);
                messages.Add($"Created directory {operation.DestinationPath}");
                break;
            case OperationKind.MovePath:
            case OperationKind.RenamePath:
                ExecuteMoveLikeOperation(operation.SourcePath, operation.DestinationPath);
                messages.Add($"Moved {operation.SourcePath} to {operation.DestinationPath}");
                break;
            case OperationKind.DeleteToQuarantine:
                var quarantineItem = MoveToQuarantine(operation, planId);
                quarantineItems.Add(quarantineItem);
                operation.DestinationPath = quarantineItem.CurrentPath;
                messages.Add($"Moved {operation.SourcePath} to quarantine {quarantineItem.CurrentPath}");
                break;
            case OperationKind.RestoreFromQuarantine:
                ExecuteMoveLikeOperation(operation.SourcePath, operation.DestinationPath);
                messages.Add($"Restored {operation.DestinationPath} from quarantine.");
                break;
            case OperationKind.ApplyOptimizationFix:
                var fixResult = optimizationFixExecutor.Apply(operation, planId);
                messages.Add(fixResult.Message);
                if (fixResult.RollbackState is not null)
                {
                    optimizationRollbackStates.Add(fixResult.RollbackState);
                }
                PersistExecutionRecord(new OptimizationExecutionRecord
                {
                    PlanId = planId,
                    FixKind = operation.OptimizationKind,
                    Target = operation.SourcePath,
                    Action = fixResult.Success ? OptimizationExecutionAction.Applied : OptimizationExecutionAction.Failed,
                    Success = fixResult.Success,
                    IsReversible = fixResult.RollbackState?.IsReversible ?? false,
                    RollbackNote = fixResult.RollbackState?.Description ?? string.Empty,
                    Message = fixResult.Message,
                    CreatedUtc = DateTime.UtcNow
                });
                if (!fixResult.Success)
                {
                    throw new InvalidOperationException(fixResult.Message);
                }
                break;
            case OperationKind.RevertOptimizationFix:
                RevertOptimizationFix(operation, planId, optimizationRollbackStates, messages);
                break;
        }
    }

    private static void ExecuteMoveLikeOperation(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(sourcePath, destinationPath, overwrite: false);
            return;
        }

        if (Directory.Exists(sourcePath))
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Directory.Move(sourcePath, destinationPath);
        }
    }

    private QuarantineItem MoveToQuarantine(PlanOperation operation, string planId)
    {
        var quarantineRoot = BuildQuarantineRoot(operation.SourcePath);
        Directory.CreateDirectory(quarantineRoot);

        try
        {
            File.SetAttributes(quarantineRoot, FileAttributes.Hidden);
        }
        catch
        {
            // Best effort only.
        }

        var targetName = $"{Guid.NewGuid():N}_{Path.GetFileName(operation.SourcePath)}";
        var quarantinePath = Path.Combine(quarantineRoot, targetName);

        // Compute content hash before move for files (lightweight SHA-256).
        var contentHash = string.Empty;
        if (File.Exists(operation.SourcePath))
        {
            try
            {
                using var stream = File.OpenRead(operation.SourcePath);
                var hashBytes = SHA256.HashData(stream);
                contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                // Best effort - locked files or permission issues skip hashing.
            }
        }

        ExecuteMoveLikeOperation(operation.SourcePath, quarantinePath);

        return new QuarantineItem
        {
            OriginalPath = operation.SourcePath,
            CurrentPath = quarantinePath,
            PlanId = planId,
            Reason = operation.Description,
            RetentionUntilUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
            ContentHash = contentHash
        };
    }

    private string BuildQuarantineRoot(string sourcePath)
    {
        var driveRoot = Path.GetPathRoot(sourcePath) ?? sourcePath;
        return Path.Combine(driveRoot, serviceOptions.Value.QuarantineFolderName, DateTime.UtcNow.ToString("yyyyMMdd"));
    }

    private static void DeletePath(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
        }
        else if (Directory.Exists(sourcePath))
        {
            Directory.Delete(sourcePath, recursive: true);
        }
    }

    private void RevertOptimizationFix(PlanOperation operation, string planId, List<OptimizationRollbackState> rollbackStates, List<string> messages)
    {
        // Look for a matching rollback state from a previous apply in this batch.
        var matchingState = rollbackStates.FirstOrDefault(s =>
            s.Kind == operation.OptimizationKind &&
            string.Equals(s.Target, operation.SourcePath, StringComparison.OrdinalIgnoreCase));

        if (matchingState is null)
        {
            messages.Add($"No rollback state found for optimization revert on {operation.SourcePath}.");
            PersistExecutionRecord(new OptimizationExecutionRecord
            {
                PlanId = planId,
                FixKind = operation.OptimizationKind,
                Target = operation.SourcePath,
                Action = OptimizationExecutionAction.Failed,
                Success = false,
                Message = $"No rollback state found for optimization revert on {operation.SourcePath}.",
                CreatedUtc = DateTime.UtcNow
            });
            return;
        }

        var revertResult = optimizationFixExecutor.Revert(matchingState);
        messages.Add(revertResult.Message);
        PersistExecutionRecord(new OptimizationExecutionRecord
        {
            PlanId = planId,
            FixKind = matchingState.Kind,
            Target = matchingState.Target,
            Action = revertResult.Success ? OptimizationExecutionAction.Reverted : OptimizationExecutionAction.Failed,
            Success = revertResult.Success,
            IsReversible = matchingState.IsReversible,
            RollbackNote = matchingState.Description,
            Message = revertResult.Message,
            CreatedUtc = DateTime.UtcNow
        });
    }

    private void RevertOptimizationFromCheckpoint(InverseOperation inverse, List<OptimizationRollbackState> checkpointRollbackStates, List<string> messages)
    {
        // Find matching rollback state from the checkpoint.
        var matchingState = checkpointRollbackStates.FirstOrDefault(s =>
            string.Equals(s.Target, inverse.SourcePath, StringComparison.OrdinalIgnoreCase));

        if (matchingState is null)
        {
            messages.Add($"No stored rollback state for optimization revert on {inverse.SourcePath}.");
            PersistExecutionRecord(new OptimizationExecutionRecord
            {
                FixKind = OptimizationKind.Unknown,
                Target = inverse.SourcePath,
                Action = OptimizationExecutionAction.Failed,
                Success = false,
                Message = $"No stored rollback state for optimization revert on {inverse.SourcePath}.",
                CreatedUtc = DateTime.UtcNow
            });
            return;
        }

        var revertResult = optimizationFixExecutor.Revert(matchingState);
        messages.Add(revertResult.Message);
        PersistExecutionRecord(new OptimizationExecutionRecord
        {
            FixKind = matchingState.Kind,
            Target = matchingState.Target,
            Action = revertResult.Success ? OptimizationExecutionAction.Reverted : OptimizationExecutionAction.Failed,
            Success = revertResult.Success,
            IsReversible = matchingState.IsReversible,
            RollbackNote = matchingState.Description,
            Message = revertResult.Message,
            CreatedUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Best-effort persistence of an optimization execution record (C-037).
    /// Failures are silently ignored to avoid blocking the execution pipeline.
    /// </summary>
    private void PersistExecutionRecord(OptimizationExecutionRecord record)
    {
        try
        {
            optimizationRepository.SaveExecutionRecordAsync(record).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort — execution history must never block the pipeline.
        }
    }
}
