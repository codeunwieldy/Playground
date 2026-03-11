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
    IConversationRepository conversationRepository) : BackgroundService
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
            _ => Wrap("error", new ProgressEvent { Stage = "error", Message = $"Unknown message type {request.MessageType}" })
        };
    }

    private async Task<ScanResponse> HandleScanAsync(PipeEnvelope request, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream(request.Payload);
        var scanRequest = Serializer.Deserialize<ScanRequest>(payload);
        return await fileScanner.ScanAsync(profile, scanRequest, cancellationToken);
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