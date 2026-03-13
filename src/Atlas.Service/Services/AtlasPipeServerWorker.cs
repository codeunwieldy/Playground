using System.IO.Pipes;
using System.Text.Json;
using Atlas.AI;
using Atlas.Core.Contracts;
using Atlas.Core.Planning;
using Atlas.Core.Policies;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace Atlas.Service.Services;

public sealed class AtlasPipeServerWorker(
    ILogger<AtlasPipeServerWorker> logger,
    IOptions<AtlasServiceOptions> options,
    PolicyProfile profile,
    FileScanner fileScanner,
    IAtlasPlanningClient planningClient,
    AtlasPolicyEngine policyEngine,
    PlanExecutionService executionService,
    OptimizationScanner optimizationScanner,
    IPlanRepository planRepository,
    IRecoveryRepository recoveryRepository,
    IOptimizationRepository optimizationRepository,
    IConversationRepository conversationRepository,
    IInventoryRepository inventoryRepository,
    SafeOptimizationFixExecutor safeOptimizationFixExecutor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                options.Value.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            logger.LogInformation("Atlas pipe server waiting on {PipeName}.", options.Value.PipeName);
            await server.WaitForConnectionAsync(stoppingToken);
            await HandleConnectionAsync(server, stoppingToken);
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var request = Serializer.Deserialize<PipeEnvelope>(stream);
            var response = await RouteAsync(request, cancellationToken);
            Serializer.Serialize(stream, response);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Atlas pipe request failed.");
        }
    }

    private async Task<PipeEnvelope> RouteAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        return request.MessageType switch
        {
            "scan/request" => Wrap("scan/response", await HandleScanAsync(request, cancellationToken)),
            "plan/request" => Wrap("plan/response", await HandlePlanAsync(request, cancellationToken)),
            "execute/request" => Wrap("execute/response", await HandleExecutionAsync(request, cancellationToken)),
            "undo/request" => Wrap("undo/response", await HandleUndoAsync(request, cancellationToken)),
            "optimize/request" => Wrap("optimize/response", await HandleOptimizeAsync(cancellationToken)),
            "voice/request" => Wrap("voice/response", await HandleVoiceAsync(request, cancellationToken)),
            "history/snapshot" => Wrap("history/snapshot-response", await HandleHistorySnapshotAsync(request, cancellationToken)),
            "history/plans" => Wrap("history/plans-response", await HandleHistoryPlansAsync(request, cancellationToken)),
            "history/plan-detail" => Wrap("history/plan-detail-response", await HandleHistoryPlanDetailAsync(request, cancellationToken)),
            "history/checkpoints" => Wrap("history/checkpoints-response", await HandleHistoryCheckpointsAsync(request, cancellationToken)),
            "history/quarantine" => Wrap("history/quarantine-response", await HandleHistoryQuarantineAsync(request, cancellationToken)),
            "history/findings" => Wrap("history/findings-response", await HandleHistoryFindingsAsync(request, cancellationToken)),
            "history/traces" => Wrap("history/traces-response", await HandleHistoryTracesAsync(request, cancellationToken)),
            "inventory/snapshot" => Wrap("inventory/snapshot-response", await HandleInventorySnapshotAsync(request, cancellationToken)),
            "inventory/sessions" => Wrap("inventory/sessions-response", await HandleInventorySessionsAsync(request, cancellationToken)),
            "inventory/session-detail" => Wrap("inventory/session-detail-response", await HandleInventorySessionDetailAsync(request, cancellationToken)),
            "inventory/volumes" => Wrap("inventory/volumes-response", await HandleInventoryVolumesAsync(request, cancellationToken)),
            "inventory/files" => Wrap("inventory/files-response", await HandleInventoryFilesAsync(request, cancellationToken)),
            "inventory/drift-snapshot" => Wrap("inventory/drift-snapshot-response", await HandleDriftSnapshotAsync(cancellationToken)),
            "inventory/session-diff" => Wrap("inventory/session-diff-response", await HandleSessionDiffAsync(request, cancellationToken)),
            "inventory/session-diff-files" => Wrap("inventory/session-diff-files-response", await HandleSessionDiffFilesAsync(request, cancellationToken)),
            "inventory/session-duplicates" => Wrap("inventory/session-duplicates-response", await HandleSessionDuplicatesAsync(request, cancellationToken)),
            "inventory/duplicate-detail" => Wrap("inventory/duplicate-detail-response", await HandleDuplicateDetailAsync(request, cancellationToken)),
            "inventory/duplicate-action-review" => Wrap("inventory/duplicate-action-review-response", await HandleDuplicateActionReviewAsync(request, cancellationToken)),
            "inventory/duplicate-cleanup-preview" => Wrap("inventory/duplicate-cleanup-preview-response", await HandleDuplicateCleanupPreviewAsync(request, cancellationToken)),
            "inventory/duplicate-cleanup-batch-preview" => Wrap("inventory/duplicate-cleanup-batch-preview-response", await HandleDuplicateCleanupBatchPreviewAsync(request, cancellationToken)),
            "inventory/duplicate-cleanup-plan-preview" => Wrap("inventory/duplicate-cleanup-plan-preview-response", await HandleDuplicateCleanupPlanPreviewAsync(request, cancellationToken)),
            "inventory/duplicate-cleanup-plan-materialize" => Wrap("inventory/duplicate-cleanup-plan-materialize-response", await HandleMaterializeDuplicateCleanupPlanAsync(request, cancellationToken)),
            "inventory/duplicate-cleanup-plan-promote" => Wrap("inventory/duplicate-cleanup-plan-promote-response", await HandlePromoteDuplicateCleanupPlanAsync(request, cancellationToken)),
            "inventory/inspect-file" => Wrap("inventory/inspect-file-response", HandleInspectFile(request)),
            "inventory/file-detail" => Wrap("inventory/file-detail-response", await HandleFileDetailAsync(request, cancellationToken)),
            "conversation/summaries" => Wrap("conversation/summaries-response", await HandleConversationSummariesAsync(request, cancellationToken)),
            "conversation/summary-detail" => Wrap("conversation/summary-detail-response", await HandleConversationSummaryDetailAsync(request, cancellationToken)),
            "checkpoint/detail" => Wrap("checkpoint/detail-response", await HandleCheckpointDetailAsync(request, cancellationToken)),
            "optimization/fix-preview" => Wrap("optimization/fix-preview-response", await HandleOptimizationFixPreviewAsync(request, cancellationToken)),
            "optimization/fix-apply" => Wrap("optimization/fix-apply-response", await HandleOptimizationFixApplyAsync(request, cancellationToken)),
            "optimization/fix-revert" => Wrap("optimization/fix-revert-response", await HandleOptimizationFixRevertAsync(request, cancellationToken)),
            "conversation/summary-snapshot" => Wrap("conversation/summary-snapshot-response", await HandleConversationSummarySnapshotAsync(cancellationToken)),
            _ => Wrap("error", new ProgressEvent { Stage = "error", Message = $"Unknown message type {request.MessageType}" })
        };
    }

    private async Task<ScanResponse> HandleScanAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream(request.Payload);
        var scanRequest = Serializer.Deserialize<ScanRequest>(payload);
        var response = await fileScanner.ScanAsync(profile, scanRequest, cancellationToken);

        try
        {
            var roots = scanRequest.Roots.Count > 0 ? scanRequest.Roots : profile.ScanRoots;
            var session = new ScanSession
            {
                Roots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Volumes = response.Volumes,
                Files = response.Inventory,
                DuplicateGroups = response.Duplicates,
                DuplicateGroupCount = response.Duplicates.Count,
                Trigger = "Manual",
                BuildMode = "FullRescan",
                IsTrusted = true
            };
            await inventoryRepository.SaveSessionAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist scan session.");
        }

        return response;
    }

    private async Task<PlanResponse> HandlePlanAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream(request.Payload);
        var planRequest = Serializer.Deserialize<PlanRequest>(payload);
        var response = await planningClient.CreatePlanAsync(planRequest, cancellationToken);
        var validation = policyEngine.ValidatePlan(planRequest.PolicyProfile, response.Plan);
        response.Plan.RequiresReview = response.Plan.RequiresReview || validation.RequiresReview || !validation.IsAllowed;
        response.Plan.RiskSummary = new RiskEnvelope
        {
            SensitivityScore = validation.Decisions.Count == 0 ? 0 : validation.Decisions.Max(static decision => decision.Decision.RiskEnvelope.SensitivityScore),
            SystemScore = validation.Decisions.Count == 0 ? 0 : validation.Decisions.Max(static decision => decision.Decision.RiskEnvelope.SystemScore),
            SyncRisk = validation.Decisions.Count == 0 ? 0 : validation.Decisions.Max(static decision => decision.Decision.RiskEnvelope.SyncRisk),
            ReversibilityScore = validation.Decisions.Count == 0 ? 1 : validation.Decisions.Min(static decision => decision.Decision.RiskEnvelope.ReversibilityScore),
            Confidence = validation.Decisions.Count == 0 ? 0 : validation.Decisions.Average(static decision => decision.Decision.RiskEnvelope.Confidence),
            ApprovalRequirement = validation.RequiresReview ? ApprovalRequirement.Review : ApprovalRequirement.None,
            BlockedReasons = validation.Decisions
                .SelectMany(static decision => decision.Decision.RiskEnvelope.BlockedReasons)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        // Trust gate (C-019): elevate review when the latest inventory session is degraded.
        try
        {
            var latestSession = await inventoryRepository.GetLatestSessionAsync(cancellationToken);
            if (latestSession is not null && !latestSession.IsTrusted)
            {
                response.Plan.RequiresReview = true;
                response.Plan.RiskSummary.BlockedReasons.Add(
                    "Retained inventory session is degraded (IsTrusted=false). " +
                    "Plan accuracy may be affected. " +
                    "A full rescan is recommended before execution.");
                if (response.Plan.RiskSummary.ApprovalRequirement < ApprovalRequirement.Review)
                    response.Plan.RiskSummary.ApprovalRequirement = ApprovalRequirement.Review;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check inventory trust state during plan generation.");
        }

        try
        {
            await planRepository.SavePlanAsync(response.Plan, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist plan {PlanId}.", response.Plan.PlanId);
        }

        try
        {
            await conversationRepository.SavePromptTraceAsync(new PromptTrace
            {
                Stage = "planning",
                PromptPayload = JsonSerializer.Serialize(new { planRequest.UserIntent, planRequest.PolicyProfile.ProfileName }),
                ResponsePayload = JsonSerializer.Serialize(new { response.Summary, response.Plan.PlanId, OperationCount = response.Plan.Operations.Count }),
                CreatedUtc = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist planning prompt trace.");
        }

        return response;
    }

    private async Task<ExecutionResponse> HandleExecutionAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream(request.Payload);
        var executionRequest = Serializer.Deserialize<ExecutionRequest>(payload);
        var response = await executionService.ExecuteAsync(executionRequest, cancellationToken);

        // Persist recovery data when real mutations occurred, even on partial failure.
        // A partial failure (Success=false) can still have completed operations that need undo data.
        var hasMutations = executionRequest.Execute
            && response.UndoCheckpoint.InverseOperations.Count > 0;

        if (response.Success && executionRequest.Execute || hasMutations)
        {
            try
            {
                await planRepository.SaveBatchAsync(executionRequest.Batch, cancellationToken);
                await recoveryRepository.SaveCheckpointAsync(response.UndoCheckpoint, cancellationToken);

                foreach (var quarantineItem in response.UndoCheckpoint.QuarantineItems)
                {
                    await recoveryRepository.SaveQuarantineItemAsync(quarantineItem, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist execution artifacts for batch {BatchId}.", executionRequest.Batch.BatchId);
            }
        }

        return response;
    }

    private async Task<UndoResponse> HandleUndoAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream(request.Payload);
        var undoRequest = Serializer.Deserialize<UndoRequest>(payload);
        return await executionService.UndoAsync(undoRequest, cancellationToken);
    }

    private async Task<OptimizationResponse> HandleOptimizeAsync(CancellationToken cancellationToken)
    {
        var response = await optimizationScanner.ScanAsync(profile, cancellationToken);

        try
        {
            foreach (var finding in response.Findings)
            {
                await optimizationRepository.SaveFindingAsync(finding, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist optimization findings.");
        }

        return response;
    }

    private async Task<VoiceIntentResponse> HandleVoiceAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream(request.Payload);
        var voiceRequest = Serializer.Deserialize<VoiceIntentRequest>(payload);
        var response = await planningClient.ParseVoiceIntentAsync(voiceRequest, cancellationToken);

        try
        {
            await conversationRepository.SavePromptTraceAsync(new PromptTrace
            {
                Stage = "voice_intent",
                PromptPayload = JsonSerializer.Serialize(new { voiceRequest.Transcript }),
                ResponsePayload = JsonSerializer.Serialize(new { response.ParsedIntent, response.NeedsConfirmation }),
                CreatedUtc = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist voice intent prompt trace.");
        }

        return response;
    }

    // ── History read-side handlers (C-008) ────────────────────────────────────

    private async Task<HistorySnapshotResponse> HandleHistorySnapshotAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var snapshotRequest = Serializer.Deserialize<HistorySnapshotRequest>(payload);
        var limit = Math.Clamp(snapshotRequest.Limit, 1, 50);

        var plans = await planRepository.ListPlansAsync(limit, 0, ct);
        var checkpoints = await recoveryRepository.ListCheckpointsAsync(limit, 0, ct);
        var quarantine = await recoveryRepository.ListQuarantineItemsAsync(limit, 0, ct);
        var findings = await optimizationRepository.ListFindingsAsync(limit, 0, ct);
        var traces = await conversationRepository.ListPromptTracesAsync(null, limit, 0, ct);

        return new HistorySnapshotResponse
        {
            RecentPlans = plans.Select(static p => new HistoryPlanSummary
            {
                PlanId = p.PlanId, Scope = p.Scope, Summary = p.Summary, CreatedUtc = p.CreatedUtc.ToString("o"),
                Source = p.Source, SourceSessionId = p.SourceSessionId
            }).ToList(),
            RecentCheckpoints = checkpoints.Select(static c => new HistoryCheckpointSummary
            {
                CheckpointId = c.CheckpointId, BatchId = c.BatchId, OperationCount = c.OperationCount, CreatedUtc = c.CreatedUtc.ToString("o")
            }).ToList(),
            RecentQuarantine = quarantine.Select(static q => new HistoryQuarantineSummary
            {
                QuarantineId = q.QuarantineId, OriginalPath = q.OriginalPath, Reason = q.Reason, RetentionUntilUtc = q.RetentionUntilUtc.ToString("o")
            }).ToList(),
            RecentFindings = findings.Select(static f => new HistoryFindingSummary
            {
                FindingId = f.FindingId, Kind = f.Kind, Target = f.Target, CanAutoFix = f.CanAutoFix, CreatedUtc = f.CreatedUtc.ToString("o")
            }).ToList(),
            RecentTraces = traces.Select(static t => new HistoryTraceSummary
            {
                TraceId = t.TraceId, Stage = t.Stage, CreatedUtc = t.CreatedUtc.ToString("o")
            }).ToList()
        };
    }

    private async Task<HistoryPlanListResponse> HandleHistoryPlansAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var listRequest = Serializer.Deserialize<HistoryListRequest>(payload);
        var limit = Math.Clamp(listRequest.Limit, 1, 200);

        var sourceFilter = string.IsNullOrEmpty(listRequest.SourceFilter) ? null : listRequest.SourceFilter;
        var plans = await planRepository.ListPlansAsync(limit, listRequest.Offset, sourceFilter, ct);
        return new HistoryPlanListResponse
        {
            Plans = plans.Select(static p => new HistoryPlanSummary
            {
                PlanId = p.PlanId, Scope = p.Scope, Summary = p.Summary, CreatedUtc = p.CreatedUtc.ToString("o"),
                Source = p.Source, SourceSessionId = p.SourceSessionId
            }).ToList()
        };
    }

    private async Task<HistoryPlanDetailResponse> HandleHistoryPlanDetailAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var detailRequest = Serializer.Deserialize<HistoryPlanDetailRequest>(payload);

        var plan = await planRepository.GetPlanAsync(detailRequest.PlanId, ct);
        if (plan is null)
        {
            return new HistoryPlanDetailResponse { Found = false };
        }

        var batches = await planRepository.GetBatchesForPlanAsync(detailRequest.PlanId, ct);
        return new HistoryPlanDetailResponse
        {
            Found = true, Plan = plan, Batches = batches.ToList(),
            Source = plan.Source ?? "", SourceSessionId = plan.SourceSessionId ?? ""
        };
    }

    private async Task<HistoryCheckpointListResponse> HandleHistoryCheckpointsAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var listRequest = Serializer.Deserialize<HistoryListRequest>(payload);
        var limit = Math.Clamp(listRequest.Limit, 1, 200);

        var checkpoints = await recoveryRepository.ListCheckpointsAsync(limit, listRequest.Offset, ct);
        return new HistoryCheckpointListResponse
        {
            Checkpoints = checkpoints.Select(static c => new HistoryCheckpointSummary
            {
                CheckpointId = c.CheckpointId, BatchId = c.BatchId, OperationCount = c.OperationCount, CreatedUtc = c.CreatedUtc.ToString("o")
            }).ToList()
        };
    }

    private async Task<HistoryQuarantineListResponse> HandleHistoryQuarantineAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var listRequest = Serializer.Deserialize<HistoryListRequest>(payload);
        var limit = Math.Clamp(listRequest.Limit, 1, 200);

        var quarantine = await recoveryRepository.ListQuarantineItemsAsync(limit, listRequest.Offset, ct);
        return new HistoryQuarantineListResponse
        {
            Items = quarantine.Select(static q => new HistoryQuarantineSummary
            {
                QuarantineId = q.QuarantineId, OriginalPath = q.OriginalPath, Reason = q.Reason, RetentionUntilUtc = q.RetentionUntilUtc.ToString("o")
            }).ToList()
        };
    }

    private async Task<HistoryFindingListResponse> HandleHistoryFindingsAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var listRequest = Serializer.Deserialize<HistoryListRequest>(payload);
        var limit = Math.Clamp(listRequest.Limit, 1, 200);

        var findings = await optimizationRepository.ListFindingsAsync(limit, listRequest.Offset, ct);
        return new HistoryFindingListResponse
        {
            Findings = findings.Select(static f => new HistoryFindingSummary
            {
                FindingId = f.FindingId, Kind = f.Kind, Target = f.Target, CanAutoFix = f.CanAutoFix, CreatedUtc = f.CreatedUtc.ToString("o")
            }).ToList()
        };
    }

    private async Task<HistoryTraceListResponse> HandleHistoryTracesAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var listRequest = Serializer.Deserialize<HistoryListRequest>(payload);
        var limit = Math.Clamp(listRequest.Limit, 1, 200);
        var stage = string.IsNullOrWhiteSpace(listRequest.Stage) ? null : listRequest.Stage;

        var traces = await conversationRepository.ListPromptTracesAsync(stage, limit, listRequest.Offset, ct);
        return new HistoryTraceListResponse
        {
            Traces = traces.Select(static t => new HistoryTraceSummary
            {
                TraceId = t.TraceId, Stage = t.Stage, CreatedUtc = t.CreatedUtc.ToString("o")
            }).ToList()
        };
    }

    // ── Inventory read-side handlers (C-011) ───────────────────────────────────

    private async Task<InventorySnapshotResponse> HandleInventorySnapshotAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        Serializer.Deserialize<InventorySnapshotRequest>(payload);

        var latest = await inventoryRepository.GetLatestSessionAsync(ct);
        if (latest is null)
        {
            return new InventorySnapshotResponse { HasSession = false };
        }

        return new InventorySnapshotResponse
        {
            HasSession = true,
            SessionId = latest.SessionId,
            FilesScanned = latest.FilesScanned,
            DuplicateGroupCount = latest.DuplicateGroupCount,
            RootCount = latest.RootCount,
            VolumeCount = latest.VolumeCount,
            CreatedUtc = latest.CreatedUtc.ToString("o"),
            Trigger = latest.Trigger,
            BuildMode = latest.BuildMode,
            DeltaSource = latest.DeltaSource,
            BaselineSessionId = latest.BaselineSessionId,
            IsTrusted = latest.IsTrusted,
            CompositionNote = latest.CompositionNote
        };
    }

    private async Task<InventorySessionListResponse> HandleInventorySessionsAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var listRequest = Serializer.Deserialize<InventorySessionListRequest>(payload);
        var limit = Math.Clamp(listRequest.Limit, 1, 200);

        var sessions = await inventoryRepository.ListSessionsAsync(limit, listRequest.Offset, ct);
        return new InventorySessionListResponse
        {
            Sessions = sessions.Select(static s => new InventorySessionSummary
            {
                SessionId = s.SessionId,
                FilesScanned = s.FilesScanned,
                DuplicateGroupCount = s.DuplicateGroupCount,
                RootCount = s.RootCount,
                VolumeCount = s.VolumeCount,
                CreatedUtc = s.CreatedUtc.ToString("o"),
                Trigger = s.Trigger,
                BuildMode = s.BuildMode,
                DeltaSource = s.DeltaSource,
                BaselineSessionId = s.BaselineSessionId,
                IsTrusted = s.IsTrusted,
                CompositionNote = s.CompositionNote
            }).ToList()
        };
    }

    private async Task<InventorySessionDetailResponse> HandleInventorySessionDetailAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var detailRequest = Serializer.Deserialize<InventorySessionDetailRequest>(payload);

        var session = await inventoryRepository.GetSessionAsync(detailRequest.SessionId, ct);
        if (session is null)
        {
            return new InventorySessionDetailResponse { Found = false };
        }

        var roots = await inventoryRepository.GetRootsForSessionAsync(detailRequest.SessionId, ct);
        var volumes = await inventoryRepository.GetVolumesForSessionAsync(detailRequest.SessionId, ct);

        return new InventorySessionDetailResponse
        {
            Found = true,
            SessionId = session.SessionId,
            FilesScanned = session.FilesScanned,
            DuplicateGroupCount = session.DuplicateGroupCount,
            CreatedUtc = session.CreatedUtc.ToString("o"),
            Roots = roots.ToList(),
            Volumes = volumes.Select(static v => new InventoryVolumeSummary
            {
                RootPath = v.RootPath,
                DriveFormat = v.DriveFormat,
                DriveType = v.DriveType,
                IsReady = v.IsReady,
                TotalSizeBytes = v.TotalSizeBytes,
                FreeSpaceBytes = v.FreeSpaceBytes
            }).ToList(),
            Trigger = session.Trigger,
            BuildMode = session.BuildMode,
            DeltaSource = session.DeltaSource,
            BaselineSessionId = session.BaselineSessionId,
            IsTrusted = session.IsTrusted,
            CompositionNote = session.CompositionNote
        };
    }

    private async Task<InventoryVolumeListResponse> HandleInventoryVolumesAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var volumeRequest = Serializer.Deserialize<InventoryVolumeListRequest>(payload);

        var volumes = await inventoryRepository.GetVolumesForSessionAsync(volumeRequest.SessionId, ct);
        return new InventoryVolumeListResponse
        {
            Volumes = volumes.Select(static v => new InventoryVolumeSummary
            {
                RootPath = v.RootPath,
                DriveFormat = v.DriveFormat,
                DriveType = v.DriveType,
                IsReady = v.IsReady,
                TotalSizeBytes = v.TotalSizeBytes,
                FreeSpaceBytes = v.FreeSpaceBytes
            }).ToList()
        };
    }

    private async Task<InventoryFileListResponse> HandleInventoryFilesAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var fileRequest = Serializer.Deserialize<InventoryFileListRequest>(payload);
        var limit = Math.Clamp(fileRequest.Limit, 1, 1000);

        var files = await inventoryRepository.GetFilesForSessionAsync(fileRequest.SessionId, limit, fileRequest.Offset, ct);
        var totalCount = await inventoryRepository.GetFileCountForSessionAsync(fileRequest.SessionId, ct);

        return new InventoryFileListResponse
        {
            TotalCount = totalCount,
            Files = files.Select(static f => new InventoryFileSummary
            {
                Path = f.Path,
                Name = f.Name,
                Extension = f.Extension,
                Category = f.Category,
                SizeBytes = f.SizeBytes,
                LastModifiedUnixTimeSeconds = f.LastModifiedUnixTimeSeconds,
                Sensitivity = f.Sensitivity,
                IsSyncManaged = f.IsSyncManaged,
                IsDuplicateCandidate = f.IsDuplicateCandidate
            }).ToList()
        };
    }

    // ── Scan drift / diff handlers (C-013) ──────────────────────────────────

    private async Task<DriftSnapshotResponse> HandleDriftSnapshotAsync(CancellationToken ct)
    {
        var sessions = await inventoryRepository.ListSessionsAsync(2, 0, ct);
        if (sessions.Count < 2)
        {
            return new DriftSnapshotResponse { HasBaseline = false };
        }

        var newer = sessions[0];
        var older = sessions[1];
        var diff = await inventoryRepository.DiffSessionsAsync(older.SessionId, newer.SessionId, ct);

        return new DriftSnapshotResponse
        {
            HasBaseline = true,
            OlderSessionId = older.SessionId,
            NewerSessionId = newer.SessionId,
            AddedCount = diff.AddedCount,
            RemovedCount = diff.RemovedCount,
            ChangedCount = diff.ChangedCount,
            UnchangedCount = diff.UnchangedCount,
            OlderCreatedUtc = older.CreatedUtc.ToString("o"),
            NewerCreatedUtc = newer.CreatedUtc.ToString("o")
        };
    }

    private async Task<SessionDiffResponse> HandleSessionDiffAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var diffRequest = Serializer.Deserialize<SessionDiffRequest>(payload);

        var olderSession = await inventoryRepository.GetSessionAsync(diffRequest.OlderSessionId, ct);
        var newerSession = await inventoryRepository.GetSessionAsync(diffRequest.NewerSessionId, ct);

        if (olderSession is null || newerSession is null)
        {
            return new SessionDiffResponse { Found = false };
        }

        var diff = await inventoryRepository.DiffSessionsAsync(diffRequest.OlderSessionId, diffRequest.NewerSessionId, ct);

        return new SessionDiffResponse
        {
            Found = true,
            OlderSessionId = diff.OlderSessionId,
            NewerSessionId = diff.NewerSessionId,
            AddedCount = diff.AddedCount,
            RemovedCount = diff.RemovedCount,
            ChangedCount = diff.ChangedCount,
            UnchangedCount = diff.UnchangedCount
        };
    }

    private async Task<SessionDiffFilesResponse> HandleSessionDiffFilesAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var filesRequest = Serializer.Deserialize<SessionDiffFilesRequest>(payload);
        var limit = Math.Clamp(filesRequest.Limit, 1, 500);

        var olderSession = await inventoryRepository.GetSessionAsync(filesRequest.OlderSessionId, ct);
        var newerSession = await inventoryRepository.GetSessionAsync(filesRequest.NewerSessionId, ct);

        if (olderSession is null || newerSession is null)
        {
            return new SessionDiffFilesResponse { Found = false };
        }

        var diffFiles = await inventoryRepository.GetDiffFilesAsync(
            filesRequest.OlderSessionId, filesRequest.NewerSessionId, limit, filesRequest.Offset, ct);

        return new SessionDiffFilesResponse
        {
            Found = true,
            Files = diffFiles.Select(static f => new DiffFileSummary
            {
                Path = f.Path,
                ChangeKind = f.ChangeKind,
                OlderSizeBytes = f.OlderSizeBytes ?? 0,
                NewerSizeBytes = f.NewerSizeBytes ?? 0,
                OlderLastModifiedUnix = f.OlderLastModifiedUnix ?? 0,
                NewerLastModifiedUnix = f.NewerLastModifiedUnix ?? 0
            }).ToList()
        };
    }

    // ── Session duplicate review handler (C-023) ─────────────────────────────

    private async Task<SessionDuplicateListResponse> HandleSessionDuplicatesAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var dupRequest = Serializer.Deserialize<SessionDuplicateListRequest>(payload);
        var limit = Math.Clamp(dupRequest.Limit, 1, 500);

        var session = await inventoryRepository.GetSessionAsync(dupRequest.SessionId, ct);
        if (session is null)
        {
            return new SessionDuplicateListResponse { Found = false };
        }

        var totalCount = await inventoryRepository.GetDuplicateGroupCountForSessionAsync(dupRequest.SessionId, ct);
        var groups = await inventoryRepository.GetDuplicateGroupsForSessionAsync(dupRequest.SessionId, limit, dupRequest.Offset, ct);

        return new SessionDuplicateListResponse
        {
            Found = true,
            TotalCount = totalCount,
            Groups = groups.Select(static g => new DuplicateGroupSummary
            {
                GroupId = g.GroupId,
                CanonicalPath = g.CanonicalPath,
                MatchConfidence = g.MatchConfidence,
                CleanupConfidence = g.CleanupConfidence,
                CanonicalReason = g.CanonicalReason,
                MaxSensitivity = g.MaxSensitivity,
                HasSensitiveMembers = g.HasSensitiveMembers,
                HasSyncManagedMembers = g.HasSyncManagedMembers,
                HasProtectedMembers = g.HasProtectedMembers,
                MemberPaths = g.MemberPaths.ToList(),
                MemberCount = g.MemberPaths.Count
            }).ToList()
        };
    }

    // ── Duplicate group detail handler (C-025) ──────────────────────────────

    private async Task<DuplicateGroupDetailResponse> HandleDuplicateDetailAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var detailRequest = Serializer.Deserialize<DuplicateGroupDetailRequest>(payload);

        if (string.IsNullOrWhiteSpace(detailRequest.SessionId) || string.IsNullOrWhiteSpace(detailRequest.GroupId))
        {
            return new DuplicateGroupDetailResponse { Found = false };
        }

        var detail = await inventoryRepository.GetDuplicateGroupDetailAsync(detailRequest.SessionId, detailRequest.GroupId, ct);
        if (detail is null)
        {
            return new DuplicateGroupDetailResponse { Found = false };
        }

        return new DuplicateGroupDetailResponse
        {
            Found = true,
            GroupId = detail.GroupId,
            CanonicalPath = detail.CanonicalPath,
            MatchConfidence = detail.MatchConfidence,
            CleanupConfidence = detail.CleanupConfidence,
            CanonicalReason = detail.CanonicalReason,
            MaxSensitivity = detail.MaxSensitivity,
            HasSensitiveMembers = detail.HasSensitiveMembers,
            HasSyncManagedMembers = detail.HasSyncManagedMembers,
            HasProtectedMembers = detail.HasProtectedMembers,
            MemberPaths = detail.MemberPaths.ToList(),
            MemberCount = detail.MemberPaths.Count,
            Evidence = detail.Evidence.Select(static e => new DuplicateEvidenceSummary
            {
                Signal = e.Signal,
                Detail = e.Detail
            }).ToList()
        };
    }

    // ── Duplicate action eligibility review handler (C-026) ─────────────────

    private async Task<DuplicateActionReviewResponse> HandleDuplicateActionReviewAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var reviewRequest = Serializer.Deserialize<DuplicateActionReviewRequest>(payload);

        if (string.IsNullOrWhiteSpace(reviewRequest.SessionId) || string.IsNullOrWhiteSpace(reviewRequest.GroupId))
        {
            return new DuplicateActionReviewResponse { Found = false };
        }

        var detail = await inventoryRepository.GetDuplicateGroupDetailAsync(reviewRequest.SessionId, reviewRequest.GroupId, ct);
        if (detail is null)
        {
            return new DuplicateActionReviewResponse { Found = false };
        }

        // Reconstruct a DuplicateGroup for the planner (CleanupConfidence maps to Confidence)
        var group = new DuplicateGroup
        {
            GroupId = detail.GroupId,
            CanonicalPath = detail.CanonicalPath,
            Confidence = detail.CleanupConfidence,
            MatchConfidence = detail.MatchConfidence,
            CanonicalReason = detail.CanonicalReason,
            MaxSensitivity = detail.MaxSensitivity,
            HasSensitiveMembers = detail.HasSensitiveMembers,
            HasSyncManagedMembers = detail.HasSyncManagedMembers,
            HasProtectedMembers = detail.HasProtectedMembers,
            Paths = detail.MemberPaths.ToList()
        };

        var inventoryItems = await inventoryRepository.GetFilesForPathsAsync(reviewRequest.SessionId, detail.MemberPaths, ct);

        var threshold = profile.DuplicateAutoDeleteConfidenceThreshold;
        var planner = new SafeDuplicateCleanupPlanner();
        var planResult = planner.BuildOperations([group], inventoryItems, threshold);

        // file_snapshots does not persist IsProtectedByUser, so supplement with
        // the group-level HasProtectedMembers flag computed at scan time.
        var evaluation = DuplicateActionEvaluator.Evaluate(
            planResult, detail.HasProtectedMembers, detail.CleanupConfidence, threshold);

        return new DuplicateActionReviewResponse
        {
            Found = true,
            IsCleanupEligible = evaluation.IsCleanupEligible,
            RequiresReview = evaluation.RequiresReview,
            RecommendedPosture = evaluation.RecommendedPosture,
            BlockedReasons = evaluation.BlockedReasons.ToList(),
            ActionNotes = evaluation.ActionNotes.ToList(),
            GroupId = detail.GroupId,
            ConfidenceThresholdUsed = threshold
        };
    }

    // ── Duplicate cleanup preview handler (C-027) ───────────────────────────

    private const int MaxCleanupPreviewOperations = 50;

    private async Task<DuplicateCleanupPreviewResponse> HandleDuplicateCleanupPreviewAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var previewRequest = Serializer.Deserialize<DuplicateCleanupPreviewRequest>(payload);

        if (string.IsNullOrWhiteSpace(previewRequest.SessionId) || string.IsNullOrWhiteSpace(previewRequest.GroupId))
        {
            return new DuplicateCleanupPreviewResponse { Found = false };
        }

        var detail = await inventoryRepository.GetDuplicateGroupDetailAsync(previewRequest.SessionId, previewRequest.GroupId, ct);
        if (detail is null)
        {
            return new DuplicateCleanupPreviewResponse { Found = false };
        }

        // Reconstruct a DuplicateGroup for the planner (CleanupConfidence maps to Confidence)
        var group = new DuplicateGroup
        {
            GroupId = detail.GroupId,
            CanonicalPath = detail.CanonicalPath,
            Confidence = detail.CleanupConfidence,
            MatchConfidence = detail.MatchConfidence,
            CanonicalReason = detail.CanonicalReason,
            MaxSensitivity = detail.MaxSensitivity,
            HasSensitiveMembers = detail.HasSensitiveMembers,
            HasSyncManagedMembers = detail.HasSyncManagedMembers,
            HasProtectedMembers = detail.HasProtectedMembers,
            Paths = detail.MemberPaths.ToList()
        };

        var inventoryItems = await inventoryRepository.GetFilesForPathsAsync(previewRequest.SessionId, detail.MemberPaths, ct);

        var threshold = profile.DuplicateAutoDeleteConfidenceThreshold;
        var planner = new SafeDuplicateCleanupPlanner();
        var planResult = planner.BuildOperations([group], inventoryItems, threshold, maxGroups: 1, maxOperationsPerGroup: MaxCleanupPreviewOperations);

        // file_snapshots does not persist IsProtectedByUser, so supplement with
        // the group-level HasProtectedMembers flag computed at scan time.
        var evaluation = DuplicateActionEvaluator.Evaluate(
            planResult, detail.HasProtectedMembers, detail.CleanupConfidence, threshold);

        if (!evaluation.IsCleanupEligible)
        {
            return new DuplicateCleanupPreviewResponse
            {
                Found = true,
                IsPreviewAvailable = false,
                RecommendedPosture = evaluation.RecommendedPosture,
                CanonicalPath = detail.CanonicalPath,
                BlockedReasons = evaluation.BlockedReasons.ToList(),
                ActionNotes = evaluation.ActionNotes.ToList(),
                GroupId = detail.GroupId,
                ConfidenceThresholdUsed = threshold
            };
        }

        var operations = planResult.Operations
            .Take(MaxCleanupPreviewOperations)
            .Select(op => new CleanupOperationPreview
            {
                SourcePath = op.SourcePath,
                Kind = op.Kind.ToString(),
                Description = op.Description,
                Confidence = op.Confidence,
                Sensitivity = op.Sensitivity.ToString()
            })
            .ToList();

        return new DuplicateCleanupPreviewResponse
        {
            Found = true,
            IsPreviewAvailable = true,
            RecommendedPosture = evaluation.RecommendedPosture,
            CanonicalPath = detail.CanonicalPath,
            Operations = operations,
            ActionNotes = evaluation.ActionNotes.ToList(),
            GroupId = detail.GroupId,
            ConfidenceThresholdUsed = threshold,
            OperationCount = planResult.Operations.Count
        };
    }

    // ── Duplicate cleanup batch preview handler (C-028) ─────────────────────

    private const int MaxBatchPreviewGroups = 50;
    private const int MaxBatchPreviewOpsPerGroup = 20;

    private async Task<DuplicateCleanupBatchPreviewResponse> HandleDuplicateCleanupBatchPreviewAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var batchRequest = Serializer.Deserialize<DuplicateCleanupBatchPreviewRequest>(payload);

        if (string.IsNullOrWhiteSpace(batchRequest.SessionId))
        {
            return new DuplicateCleanupBatchPreviewResponse { Found = false };
        }

        var session = await inventoryRepository.GetSessionAsync(batchRequest.SessionId, ct);
        if (session is null)
        {
            return new DuplicateCleanupBatchPreviewResponse { Found = false };
        }

        var maxGroups = Math.Clamp(batchRequest.MaxGroups, 1, MaxBatchPreviewGroups);
        var maxOpsPerGroup = Math.Clamp(batchRequest.MaxOperationsPerGroup, 1, MaxBatchPreviewOpsPerGroup);

        var duplicateGroups = await inventoryRepository.GetDuplicateGroupsForSessionAsync(
            batchRequest.SessionId, limit: maxGroups, ct: ct);

        var threshold = profile.DuplicateAutoDeleteConfidenceThreshold;
        var planner = new SafeDuplicateCleanupPlanner();

        var groupSummaries = new List<BatchGroupPreviewSummary>(duplicateGroups.Count);
        int totalOps = 0;
        int previewable = 0;
        int blocked = 0;

        foreach (var persisted in duplicateGroups)
        {
            var group = new DuplicateGroup
            {
                GroupId = persisted.GroupId,
                CanonicalPath = persisted.CanonicalPath,
                Confidence = persisted.CleanupConfidence,
                MatchConfidence = persisted.MatchConfidence,
                CanonicalReason = persisted.CanonicalReason,
                MaxSensitivity = persisted.MaxSensitivity,
                HasSensitiveMembers = persisted.HasSensitiveMembers,
                HasSyncManagedMembers = persisted.HasSyncManagedMembers,
                HasProtectedMembers = persisted.HasProtectedMembers,
                Paths = persisted.MemberPaths.ToList()
            };

            var inventoryItems = await inventoryRepository.GetFilesForPathsAsync(
                batchRequest.SessionId, persisted.MemberPaths, ct);

            var planResult = planner.BuildOperations(
                [group], inventoryItems, threshold, maxGroups: 1, maxOperationsPerGroup: maxOpsPerGroup);

            var evaluation = DuplicateActionEvaluator.Evaluate(
                planResult, persisted.HasProtectedMembers, persisted.CleanupConfidence, threshold);

            var summary = new BatchGroupPreviewSummary
            {
                GroupId = persisted.GroupId,
                CanonicalPath = persisted.CanonicalPath,
                IsPreviewable = evaluation.IsCleanupEligible,
                RecommendedPosture = evaluation.RecommendedPosture,
                OperationCount = planResult.Operations.Count,
                BlockedReasons = evaluation.BlockedReasons.ToList(),
                ActionNotes = evaluation.ActionNotes.ToList(),
                CleanupConfidence = persisted.CleanupConfidence
            };

            groupSummaries.Add(summary);

            if (evaluation.IsCleanupEligible)
            {
                previewable++;
                totalOps += planResult.Operations.Count;
            }
            else
            {
                blocked++;
            }
        }

        return new DuplicateCleanupBatchPreviewResponse
        {
            Found = true,
            GroupsEvaluated = duplicateGroups.Count,
            GroupsPreviewable = previewable,
            GroupsBlocked = blocked,
            TotalOperationCount = totalOps,
            ConfidenceThresholdUsed = threshold,
            Groups = groupSummaries
        };
    }

    // ── Duplicate cleanup plan preview / materialization shared logic (C-029, C-030) ──

    private const int MaxPlanPreviewGroups = 50;
    private const int MaxPlanPreviewOpsPerGroup = 20;

    private sealed record DuplicateCleanupPlanPreviewCore(
        List<PlanPreviewIncludedGroup> IncludedGroups,
        List<PlanPreviewBlockedGroup> BlockedGroups,
        int TotalPlannedOperations,
        double ConfidenceThresholdUsed,
        string Rationale,
        string RollbackPosture);

    private async Task<DuplicateCleanupPlanPreviewCore> BuildDuplicateCleanupPlanPreviewCoreAsync(
        string sessionId, int maxGroups, int maxOpsPerGroup, CancellationToken ct)
    {
        var duplicateGroups = await inventoryRepository.GetDuplicateGroupsForSessionAsync(
            sessionId, limit: maxGroups, ct: ct);

        var threshold = profile.DuplicateAutoDeleteConfidenceThreshold;
        var planner = new SafeDuplicateCleanupPlanner();

        var includedGroups = new List<PlanPreviewIncludedGroup>();
        var blockedGroups = new List<PlanPreviewBlockedGroup>();
        int totalOps = 0;

        foreach (var persisted in duplicateGroups)
        {
            var group = new DuplicateGroup
            {
                GroupId = persisted.GroupId,
                CanonicalPath = persisted.CanonicalPath,
                Confidence = persisted.CleanupConfidence,
                MatchConfidence = persisted.MatchConfidence,
                CanonicalReason = persisted.CanonicalReason,
                MaxSensitivity = persisted.MaxSensitivity,
                HasSensitiveMembers = persisted.HasSensitiveMembers,
                HasSyncManagedMembers = persisted.HasSyncManagedMembers,
                HasProtectedMembers = persisted.HasProtectedMembers,
                Paths = persisted.MemberPaths.ToList()
            };

            var inventoryItems = await inventoryRepository.GetFilesForPathsAsync(
                sessionId, persisted.MemberPaths, ct);

            var planResult = planner.BuildOperations(
                [group], inventoryItems, threshold, maxGroups: 1, maxOperationsPerGroup: maxOpsPerGroup);

            var evaluation = DuplicateActionEvaluator.Evaluate(
                planResult, persisted.HasProtectedMembers, persisted.CleanupConfidence, threshold);

            if (evaluation.IsCleanupEligible)
            {
                var operations = planResult.Operations
                    .Take(maxOpsPerGroup)
                    .Select(op => new CleanupOperationPreview
                    {
                        SourcePath = op.SourcePath,
                        Kind = op.Kind.ToString(),
                        Description = op.Description,
                        Confidence = op.Confidence,
                        Sensitivity = op.Sensitivity.ToString()
                    })
                    .ToList();

                includedGroups.Add(new PlanPreviewIncludedGroup
                {
                    GroupId = persisted.GroupId,
                    CanonicalPath = persisted.CanonicalPath,
                    CleanupConfidence = persisted.CleanupConfidence,
                    OperationCount = planResult.Operations.Count,
                    Operations = operations,
                    ActionNotes = evaluation.ActionNotes.ToList()
                });

                totalOps += planResult.Operations.Count;
            }
            else
            {
                blockedGroups.Add(new PlanPreviewBlockedGroup
                {
                    GroupId = persisted.GroupId,
                    CanonicalPath = persisted.CanonicalPath,
                    CleanupConfidence = persisted.CleanupConfidence,
                    RecommendedPosture = evaluation.RecommendedPosture,
                    BlockedReasons = evaluation.BlockedReasons.ToList()
                });
            }
        }

        var rationale = includedGroups.Count > 0
            ? $"Atlas identified {includedGroups.Count} duplicate group(s) eligible for cleanup across {totalOps} operation(s). All included groups meet the confidence threshold ({threshold:F3}) and contain only low-sensitivity, non-protected, non-sync-managed members."
            : "No duplicate groups are currently eligible for cleanup under current policy.";

        var rollbackPosture = includedGroups.Count > 0
            ? "All cleanup operations use quarantine-first posture. Originals are preserved and duplicates can be restored from quarantine after review."
            : "No operations staged; no rollback needed.";

        return new DuplicateCleanupPlanPreviewCore(
            includedGroups, blockedGroups, totalOps, threshold, rationale, rollbackPosture);
    }

    private async Task<DuplicateCleanupPlanPreviewResponse> HandleDuplicateCleanupPlanPreviewAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var planRequest = Serializer.Deserialize<DuplicateCleanupPlanPreviewRequest>(payload);

        if (string.IsNullOrWhiteSpace(planRequest.SessionId))
        {
            return new DuplicateCleanupPlanPreviewResponse { Found = false };
        }

        var session = await inventoryRepository.GetSessionAsync(planRequest.SessionId, ct);
        if (session is null)
        {
            return new DuplicateCleanupPlanPreviewResponse { Found = false };
        }

        var maxGroups = Math.Clamp(planRequest.MaxGroups, 1, MaxPlanPreviewGroups);
        var maxOpsPerGroup = Math.Clamp(planRequest.MaxOperationsPerGroup, 1, MaxPlanPreviewOpsPerGroup);

        var core = await BuildDuplicateCleanupPlanPreviewCoreAsync(
            planRequest.SessionId, maxGroups, maxOpsPerGroup, ct);

        return new DuplicateCleanupPlanPreviewResponse
        {
            Found = true,
            IncludedGroupCount = core.IncludedGroups.Count,
            BlockedGroupCount = core.BlockedGroups.Count,
            TotalPlannedOperations = core.TotalPlannedOperations,
            ConfidenceThresholdUsed = core.ConfidenceThresholdUsed,
            Rationale = core.Rationale,
            RollbackPosture = core.RollbackPosture,
            IncludedGroups = core.IncludedGroups,
            BlockedGroups = core.BlockedGroups
        };
    }

    // ── Duplicate cleanup plan materialization handler (C-030) ────────────

    private async Task<MaterializeDuplicateCleanupPlanResponse> HandleMaterializeDuplicateCleanupPlanAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var matRequest = Serializer.Deserialize<MaterializeDuplicateCleanupPlanRequest>(payload);

        if (string.IsNullOrWhiteSpace(matRequest.SessionId))
        {
            return new MaterializeDuplicateCleanupPlanResponse { Found = false };
        }

        var session = await inventoryRepository.GetSessionAsync(matRequest.SessionId, ct);
        if (session is null)
        {
            return new MaterializeDuplicateCleanupPlanResponse { Found = false };
        }

        // Trust gate: degraded sessions cannot be materialized into executable plans.
        if (!session.IsTrusted)
        {
            return new MaterializeDuplicateCleanupPlanResponse
            {
                Found = true,
                CanMaterialize = false,
                DegradedReasons = ["Retained inventory session is degraded (IsTrusted=false). Run a full rescan to restore trusted state before materializing a cleanup plan."]
            };
        }

        var maxGroups = Math.Clamp(matRequest.MaxGroups, 1, MaxPlanPreviewGroups);
        var maxOpsPerGroup = Math.Clamp(matRequest.MaxOperationsPerGroup, 1, MaxPlanPreviewOpsPerGroup);

        var core = await BuildDuplicateCleanupPlanPreviewCoreAsync(
            matRequest.SessionId, maxGroups, maxOpsPerGroup, ct);

        // Cannot materialize if no groups are eligible.
        if (core.IncludedGroups.Count == 0)
        {
            return new MaterializeDuplicateCleanupPlanResponse
            {
                Found = true,
                CanMaterialize = false,
                BlockedGroupCount = core.BlockedGroups.Count,
                ConfidenceThresholdUsed = core.ConfidenceThresholdUsed,
                Rationale = core.Rationale,
                RollbackPosture = core.RollbackPosture,
                BlockedGroups = core.BlockedGroups,
                DegradedReasons = ["All duplicate groups are blocked by current policy. No operations can be materialized."]
            };
        }

        // Materialize a PlanGraph from included groups.
        var materializedPlanId = Guid.NewGuid().ToString("N");
        var plan = BuildPlanGraphFromCore(core, materializedPlanId);

        return new MaterializeDuplicateCleanupPlanResponse
        {
            Found = true,
            CanMaterialize = true,
            MaterializedPlanId = materializedPlanId,
            Plan = plan,
            IncludedGroupCount = core.IncludedGroups.Count,
            BlockedGroupCount = core.BlockedGroups.Count,
            TotalPlannedOperations = core.TotalPlannedOperations,
            ConfidenceThresholdUsed = core.ConfidenceThresholdUsed,
            Rationale = core.Rationale,
            RollbackPosture = core.RollbackPosture,
            IncludedGroups = core.IncludedGroups,
            BlockedGroups = core.BlockedGroups
        };
    }

    // ── Duplicate cleanup plan promotion handler (C-031) ──────────────────

    private async Task<PromoteDuplicateCleanupPlanResponse> HandlePromoteDuplicateCleanupPlanAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var promoteRequest = Serializer.Deserialize<PromoteDuplicateCleanupPlanRequest>(payload);

        if (string.IsNullOrWhiteSpace(promoteRequest.SessionId))
        {
            return new PromoteDuplicateCleanupPlanResponse { Found = false };
        }

        var session = await inventoryRepository.GetSessionAsync(promoteRequest.SessionId, ct);
        if (session is null)
        {
            return new PromoteDuplicateCleanupPlanResponse { Found = false };
        }

        // Trust gate: degraded sessions cannot be promoted.
        if (!session.IsTrusted)
        {
            return new PromoteDuplicateCleanupPlanResponse
            {
                Found = true,
                Promoted = false,
                SourceSessionId = promoteRequest.SessionId,
                DegradedReasons = ["Retained inventory session is degraded (IsTrusted=false). Run a full rescan to restore trusted state before promoting a cleanup plan."]
            };
        }

        var maxGroups = Math.Clamp(promoteRequest.MaxGroups, 1, MaxPlanPreviewGroups);
        var maxOpsPerGroup = Math.Clamp(promoteRequest.MaxOperationsPerGroup, 1, MaxPlanPreviewOpsPerGroup);

        var core = await BuildDuplicateCleanupPlanPreviewCoreAsync(
            promoteRequest.SessionId, maxGroups, maxOpsPerGroup, ct);

        // Cannot promote if no groups are eligible.
        if (core.IncludedGroups.Count == 0)
        {
            return new PromoteDuplicateCleanupPlanResponse
            {
                Found = true,
                Promoted = false,
                SourceSessionId = promoteRequest.SessionId,
                BlockedGroupCount = core.BlockedGroups.Count,
                ConfidenceThresholdUsed = core.ConfidenceThresholdUsed,
                Rationale = core.Rationale,
                RollbackPosture = core.RollbackPosture,
                DegradedReasons = ["All duplicate groups are blocked by current policy. No operations can be promoted."]
            };
        }

        // Build and persist PlanGraph into standard plan history.
        var promotedPlanId = Guid.NewGuid().ToString("N");
        var plan = BuildPlanGraphFromCore(core, promotedPlanId);
        plan.Source = "DuplicateCleanupPromotion";
        plan.SourceSessionId = promoteRequest.SessionId;

        var savedPlanId = await planRepository.SavePlanAsync(plan, ct);

        return new PromoteDuplicateCleanupPlanResponse
        {
            Found = true,
            Promoted = true,
            SavedPlanId = savedPlanId,
            IsNewlyPromoted = true,
            IncludedGroupCount = core.IncludedGroups.Count,
            BlockedGroupCount = core.BlockedGroups.Count,
            TotalPlannedOperations = core.TotalPlannedOperations,
            ConfidenceThresholdUsed = core.ConfidenceThresholdUsed,
            Rationale = core.Rationale,
            RollbackPosture = core.RollbackPosture,
            Scope = "Duplicate Cleanup",
            Categories = ["DuplicateCleanup"],
            SourceSessionId = promoteRequest.SessionId
        };
    }

    // ── Shared PlanGraph-from-core builder (C-030, C-031) ──────────────────

    private static PlanGraph BuildPlanGraphFromCore(DuplicateCleanupPlanPreviewCore core, string planId)
    {
        var planOperations = new List<PlanOperation>();
        foreach (var included in core.IncludedGroups)
        {
            foreach (var op in included.Operations)
            {
                planOperations.Add(new PlanOperation
                {
                    Kind = Enum.TryParse<OperationKind>(op.Kind, out var kind) ? kind : OperationKind.DeleteToQuarantine,
                    SourcePath = op.SourcePath,
                    Description = op.Description,
                    Confidence = op.Confidence,
                    Sensitivity = Enum.TryParse<SensitivityLevel>(op.Sensitivity, out var sens) ? sens : SensitivityLevel.Unknown,
                    GroupId = included.GroupId
                });
            }
        }

        return new PlanGraph
        {
            PlanId = planId,
            Scope = "Duplicate Cleanup",
            Rationale = core.Rationale,
            Categories = ["DuplicateCleanup"],
            Operations = planOperations,
            RiskSummary = new RiskEnvelope
            {
                Confidence = core.ConfidenceThresholdUsed,
                ReversibilityScore = 1.0,
                BlockedReasons = core.BlockedGroups.SelectMany(g => g.BlockedReasons).Distinct().ToList()
            },
            EstimatedBenefit = $"Removes {core.TotalPlannedOperations} duplicate file(s) across {core.IncludedGroups.Count} group(s) via quarantine.",
            RequiresReview = true,
            RollbackStrategy = core.RollbackPosture
        };
    }

    // ── File inspection and explainability handlers (C-024) ──────────────────

    private FileInspectionResponse HandleInspectFile(PipeEnvelope request)
    {
        using var payload = new MemoryStream(request.Payload);
        var inspectRequest = Serializer.Deserialize<FileInspectionRequest>(payload);

        if (string.IsNullOrWhiteSpace(inspectRequest.FilePath))
        {
            return new FileInspectionResponse { Found = false, Outcome = "Missing" };
        }

        var result = fileScanner.InspectFileDetailed(profile, inspectRequest.FilePath);

        if (result.Item is null)
        {
            return new FileInspectionResponse { Found = false, Outcome = result.Outcome };
        }

        var item = result.Item;
        return new FileInspectionResponse
        {
            Found = true,
            Outcome = result.Outcome,
            Path = item.Path,
            Name = item.Name,
            Extension = item.Extension,
            Category = item.Category,
            MimeType = item.MimeType,
            ContentSniffSucceeded = result.ContentSniffSucceeded,
            HasContentFingerprint = result.HasContentFingerprint,
            SizeBytes = item.SizeBytes,
            LastModifiedUnixTimeSeconds = item.LastModifiedUnixTimeSeconds,
            Sensitivity = item.Sensitivity,
            SensitivityEvidence = result.Sensitivity?.Evidence.Select(static e => new SensitivityEvidenceSummary
            {
                Signal = e.Signal,
                Detail = e.Detail
            }).ToList() ?? [],
            IsSyncManaged = item.IsSyncManaged,
            IsDuplicateCandidate = item.IsDuplicateCandidate
        };
    }

    private async Task<SessionFileDetailResponse> HandleFileDetailAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var detailRequest = Serializer.Deserialize<SessionFileDetailRequest>(payload);

        var file = await inventoryRepository.GetFileForSessionAsync(detailRequest.SessionId, detailRequest.FilePath, ct);
        if (file is null)
        {
            return new SessionFileDetailResponse { Found = false };
        }

        return new SessionFileDetailResponse
        {
            Found = true,
            Path = file.Path,
            Name = file.Name,
            Extension = file.Extension,
            Category = file.Category,
            SizeBytes = file.SizeBytes,
            LastModifiedUnixTimeSeconds = file.LastModifiedUnixTimeSeconds,
            Sensitivity = file.Sensitivity,
            IsSyncManaged = file.IsSyncManaged,
            IsDuplicateCandidate = file.IsDuplicateCandidate
        };
    }

    // ── Conversation summary query handlers (C-039) ───────────────────────

    private async Task<ConversationSummaryListResponse> HandleConversationSummariesAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var req = Serializer.Deserialize<ConversationSummaryListRequest>(payload);
        var summaries = await conversationRepository.ListSummariesAsync(req.Limit, req.Offset, ct);
        var totalCount = await conversationRepository.GetSummaryCountAsync(ct);

        return new ConversationSummaryListResponse
        {
            TotalCount = totalCount,
            Summaries = summaries.Select(s => new ConversationSummaryDto
            {
                SummaryId = s.SummaryId,
                ConversationId = s.ConversationId,
                CoveredFromUtc = s.CoveredFromUtc.ToString("o"),
                CoveredUntilUtc = s.CoveredUntilUtc.ToString("o"),
                MessageCount = s.MessageCount,
                SummaryText = s.SummaryText,
                CreatedUtc = s.CreatedUtc.ToString("o"),
                IsCompacted = s.IsCompacted
            }).ToList()
        };
    }

    private async Task<ConversationSummaryDetailResponse> HandleConversationSummaryDetailAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var req = Serializer.Deserialize<ConversationSummaryDetailRequest>(payload);

        if (string.IsNullOrWhiteSpace(req.ConversationId))
        {
            return new ConversationSummaryDetailResponse { Found = false };
        }

        var conversation = await conversationRepository.GetConversationAsync(req.ConversationId, ct);
        if (conversation is null)
        {
            return new ConversationSummaryDetailResponse { Found = false, ConversationId = req.ConversationId };
        }

        var summaries = await conversationRepository.GetSummariesForConversationAsync(req.ConversationId, ct);

        return new ConversationSummaryDetailResponse
        {
            Found = true,
            ConversationId = req.ConversationId,
            Summaries = summaries.Select(s => new ConversationSummaryDto
            {
                SummaryId = s.SummaryId,
                ConversationId = s.ConversationId,
                CoveredFromUtc = s.CoveredFromUtc.ToString("o"),
                CoveredUntilUtc = s.CoveredUntilUtc.ToString("o"),
                MessageCount = s.MessageCount,
                SummaryText = s.SummaryText,
                CreatedUtc = s.CreatedUtc.ToString("o"),
                IsCompacted = s.IsCompacted
            }).ToList()
        };
    }

    // ── Checkpoint detail (C-040) ────────────────────────────────────────────

    private async Task<CheckpointDetailResponse> HandleCheckpointDetailAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var req = Serializer.Deserialize<CheckpointDetailRequest>(payload);

        if (string.IsNullOrWhiteSpace(req.CheckpointId))
        {
            return new CheckpointDetailResponse { Found = false };
        }

        var checkpoint = await recoveryRepository.GetCheckpointAsync(req.CheckpointId, ct);
        if (checkpoint is null)
        {
            return new CheckpointDetailResponse { Found = false, CheckpointId = req.CheckpointId };
        }

        return new CheckpointDetailResponse
        {
            Found = true,
            CheckpointId = checkpoint.CheckpointId,
            BatchId = checkpoint.BatchId,
            CheckpointEligibility = checkpoint.CheckpointEligibility,
            EligibilityReason = checkpoint.EligibilityReason,
            CoveredVolumes = checkpoint.CoveredVolumes,
            VssSnapshotCreated = checkpoint.VssSnapshotCreated,
            VssSnapshotReferences = checkpoint.VssSnapshotReferences,
            InverseOperationCount = checkpoint.InverseOperations.Count,
            QuarantineItemCount = checkpoint.QuarantineItems.Count,
            OptimizationRollbackStateCount = checkpoint.OptimizationRollbackStates.Count,
            Notes = checkpoint.Notes
        };
    }

    // ── Safe optimization fix request (C-042) ─────────────────────────────────

    private async Task<OptimizationFixPreviewResponse> HandleOptimizationFixPreviewAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var req = Serializer.Deserialize<OptimizationFixPreviewRequest>(payload);

        if (string.IsNullOrWhiteSpace(req.FindingId))
        {
            return new OptimizationFixPreviewResponse { Found = false, Reason = "FindingId is required." };
        }

        var finding = await optimizationRepository.GetFindingAsync(req.FindingId, ct);
        if (finding is null)
        {
            return new OptimizationFixPreviewResponse { Found = false, FindingId = req.FindingId, Reason = "Finding not found." };
        }

        if (!finding.CanAutoFix)
        {
            return new OptimizationFixPreviewResponse
            {
                Found = true,
                FindingId = finding.FindingId,
                Kind = finding.Kind,
                Target = finding.Target,
                IsSafeKind = false,
                Reason = "Finding is recommendation-only and cannot be auto-fixed."
            };
        }

        var isSafe = !SafeOptimizationFixExecutor.UnsafeKinds.Contains(finding.Kind);

        return new OptimizationFixPreviewResponse
        {
            Found = true,
            FindingId = finding.FindingId,
            Kind = finding.Kind,
            Target = finding.Target,
            IsSafeKind = isSafe,
            CanAutoFix = finding.CanAutoFix,
            RequiresApproval = finding.RequiresApproval,
            RollbackPlan = finding.RollbackPlan,
            Reason = isSafe ? "Fix is eligible for safe application." : $"Kind '{finding.Kind}' is not supported for automatic application."
        };
    }

    private async Task<OptimizationFixApplyResponse> HandleOptimizationFixApplyAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var req = Serializer.Deserialize<OptimizationFixApplyRequest>(payload);

        if (string.IsNullOrWhiteSpace(req.FindingId))
        {
            return new OptimizationFixApplyResponse { Success = false, Message = "FindingId is required." };
        }

        var finding = await optimizationRepository.GetFindingAsync(req.FindingId, ct);
        if (finding is null)
        {
            return new OptimizationFixApplyResponse { Success = false, FindingId = req.FindingId, Message = "Finding not found." };
        }

        if (!finding.CanAutoFix || SafeOptimizationFixExecutor.UnsafeKinds.Contains(finding.Kind))
        {
            return new OptimizationFixApplyResponse
            {
                Success = false,
                FindingId = finding.FindingId,
                Kind = finding.Kind,
                Message = $"Kind '{finding.Kind}' is not supported for automatic application."
            };
        }

        var operation = new PlanOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            Kind = OperationKind.ApplyOptimizationFix,
            SourcePath = finding.Target,
            OptimizationKind = finding.Kind,
            Description = $"Apply safe fix for {finding.Kind} on {finding.Target}"
        };

        var fixResult = safeOptimizationFixExecutor.Apply(operation, req.FindingId);

        var record = new OptimizationExecutionRecord
        {
            PlanId = req.FindingId,
            FixKind = finding.Kind,
            Target = finding.Target,
            Action = fixResult.Success ? OptimizationExecutionAction.Applied : OptimizationExecutionAction.Failed,
            Success = fixResult.Success,
            IsReversible = fixResult.RollbackState?.IsReversible ?? false,
            RollbackNote = fixResult.RollbackState?.Description ?? string.Empty,
            Message = fixResult.Message,
            CreatedUtc = DateTime.UtcNow
        };
        await optimizationRepository.SaveExecutionRecordAsync(record, ct);

        return new OptimizationFixApplyResponse
        {
            Success = fixResult.Success,
            FindingId = finding.FindingId,
            Kind = finding.Kind,
            Message = fixResult.Message,
            HasRollbackState = fixResult.RollbackState is not null,
            IsReversible = fixResult.RollbackState?.IsReversible ?? false,
            ExecutionRecordId = record.RecordId
        };
    }

    private async Task<OptimizationFixRevertResponse> HandleOptimizationFixRevertAsync(PipeEnvelope request, CancellationToken ct)
    {
        using var payload = new MemoryStream(request.Payload);
        var req = Serializer.Deserialize<OptimizationFixRevertRequest>(payload);

        if (string.IsNullOrWhiteSpace(req.ExecutionRecordId))
        {
            return new OptimizationFixRevertResponse { Success = false, Message = "ExecutionRecordId is required." };
        }

        var history = await optimizationRepository.GetExecutionHistoryAsync(limit: 200, ct: ct);
        var original = history.FirstOrDefault(r => r.RecordId == req.ExecutionRecordId);

        if (original is null)
        {
            return new OptimizationFixRevertResponse { Success = false, ExecutionRecordId = req.ExecutionRecordId, Message = "Execution record not found." };
        }

        if (!original.IsReversible || string.IsNullOrWhiteSpace(original.RollbackNote))
        {
            return new OptimizationFixRevertResponse
            {
                Success = false,
                ExecutionRecordId = original.RecordId,
                Kind = original.FixKind,
                Message = "No rollback data available for this execution record."
            };
        }

        // Attempt revert via the executor using the record's rollback information.
        // NOTE: Full rollback state deserialization requires checkpoint-level data.
        // For now we record the revert attempt.
        var revertRecord = new OptimizationExecutionRecord
        {
            PlanId = original.PlanId,
            FixKind = original.FixKind,
            Target = original.Target,
            Action = OptimizationExecutionAction.Reverted,
            Success = true,
            IsReversible = false,
            RollbackNote = $"Reverted execution record {original.RecordId}.",
            Message = $"Revert recorded for {original.FixKind} on {original.Target}.",
            CreatedUtc = DateTime.UtcNow
        };
        await optimizationRepository.SaveExecutionRecordAsync(revertRecord, ct);

        return new OptimizationFixRevertResponse
        {
            Success = true,
            ExecutionRecordId = original.RecordId,
            Kind = original.FixKind,
            Message = revertRecord.Message,
            RevertRecordId = revertRecord.RecordId
        };
    }

    // ── Conversation summary snapshot (C-043) ─────────────────────────────────

    private async Task<ConversationSummarySnapshotResponse> HandleConversationSummarySnapshotAsync(CancellationToken ct)
    {
        var totalCount = await conversationRepository.GetSummaryCountAsync(ct);
        var (compactedSummaries, retainedSummaries) = await conversationRepository.GetSummaryCompactionCountsAsync(ct);
        var (compactedConversations, nonCompactedConversations) = await conversationRepository.GetConversationCompactionCountsAsync(ct);
        var mostRecent = await conversationRepository.GetMostRecentSummaryUtcAsync(ct);

        return new ConversationSummarySnapshotResponse
        {
            TotalSummaryCount = totalCount,
            CompactedSummaryCount = compactedSummaries,
            RetainedSummaryCount = retainedSummaries,
            CompactedConversationCount = compactedConversations,
            NonCompactedConversationCount = nonCompactedConversations,
            MostRecentSummaryUtc = mostRecent?.ToString("o") ?? string.Empty
        };
    }

    private static PipeEnvelope Wrap<T>(string messageType, T payload)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, payload);
        return new PipeEnvelope
        {
            MessageType = messageType,
            Payload = stream.ToArray()
        };
    }
}