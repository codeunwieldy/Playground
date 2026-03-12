using System.IO.Pipes;
using System.Text.Json;
using Atlas.AI;
using Atlas.Core.Contracts;
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
    IInventoryRepository inventoryRepository) : BackgroundService
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
                PlanId = p.PlanId, Scope = p.Scope, Summary = p.Summary, CreatedUtc = p.CreatedUtc.ToString("o")
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

        var plans = await planRepository.ListPlansAsync(limit, listRequest.Offset, ct);
        return new HistoryPlanListResponse
        {
            Plans = plans.Select(static p => new HistoryPlanSummary
            {
                PlanId = p.PlanId, Scope = p.Scope, Summary = p.Summary, CreatedUtc = p.CreatedUtc.ToString("o")
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
        return new HistoryPlanDetailResponse { Found = true, Plan = plan, Batches = batches.ToList() };
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