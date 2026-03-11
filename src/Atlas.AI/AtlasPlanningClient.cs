using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Core.Contracts;
using Atlas.Storage.Repositories;
using Microsoft.Extensions.Options;

namespace Atlas.AI;

public interface IAtlasPlanningClient
{
    Task<PlanResponse> CreatePlanAsync(PlanRequest request, CancellationToken cancellationToken);
    Task<VoiceIntentResponse> ParseVoiceIntentAsync(VoiceIntentRequest request, CancellationToken cancellationToken);
}

public sealed class OpenAIResponsesPlanningClient : IAtlasPlanningClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _options;
    private readonly IConversationRepository? _conversationRepository;
    private readonly PlanSemanticValidator _validator = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public OpenAIResponsesPlanningClient(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        IConversationRepository? conversationRepository = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _conversationRepository = conversationRepository;
    }

    public async Task<PlanResponse> CreatePlanAsync(PlanRequest request, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BuildFallbackPlan(request);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = JsonContent.Create(new
        {
            model = ResolveModel(),
            input = new[]
            {
                new
                {
                    role = "system",
                    content = new[] { new { type = "input_text", text = PromptCatalog.PlanningSystemPrompt } }
                },
                new
                {
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = BuildPlanningPayload(request)
                        }
                    }
                }
            },
            text = new
            {
                format = ResponseSchemas.PlanResponseSchema
            }
        });

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ResponsesPayload>(JsonOptions, cancellationToken);
        var jsonText = payload?.OutputText;
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return BuildFallbackPlan(request);
        }

        return ParsePlanResponse(jsonText, request);
    }

    public async Task<VoiceIntentResponse> ParseVoiceIntentAsync(VoiceIntentRequest request, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new VoiceIntentResponse
            {
                ParsedIntent = request.Transcript.Trim(),
                NeedsConfirmation = true
            };
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = JsonContent.Create(new
        {
            model = ResolveModel(),
            input = new[]
            {
                new
                {
                    role = "system",
                    content = new[] { new { type = "input_text", text = PromptCatalog.VoiceIntentPrompt } }
                },
                new
                {
                    role = "user",
                    content = new[] { new { type = "input_text", text = request.Transcript } }
                }
            },
            text = new
            {
                format = ResponseSchemas.VoiceIntentSchema
            }
        });

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ResponsesPayload>(JsonOptions, cancellationToken);

        var jsonText = payload?.OutputText;
        if (!string.IsNullOrWhiteSpace(jsonText))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<VoiceIntentResponse>(jsonText, JsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch
            {
                // Fall through to default.
            }
        }

        return new VoiceIntentResponse
        {
            ParsedIntent = payload?.OutputText ?? request.Transcript,
            NeedsConfirmation = true
        };
    }

    private string BuildPlanningPayload(PlanRequest request)
    {
        var maxItems = _options.MaxInventoryItemsInPrompt;
        var sampledInventory = request.Scan.Inventory
            .Take(request.PolicyProfile.ScanRoots.Count > 0 ? maxItems : 100)
            .Select(item => new
            {
                item.Path,
                item.Category,
                item.SizeBytes,
                item.Sensitivity,
                item.IsSyncManaged,
                item.IsDuplicateCandidate
            });

        return JsonSerializer.Serialize(new
        {
            request.UserIntent,
            request.PolicyProfile,
            volumes = request.Scan.Volumes,
            duplicates = request.Scan.Duplicates,
            inventory = sampledInventory
        }, JsonOptions);
    }

    private PlanResponse ParsePlanResponse(string jsonText, PlanRequest request)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<PlanResponse>(jsonText, JsonOptions);
            if (parsed is not null && parsed.Plan is not null)
            {
                var report = _validator.Validate(parsed);
                if (report.IsValid)
                {
                    return parsed;
                }

                // Semantic validation failed; fall through to fallback.
            }
        }
        catch
        {
            // Fall through to deterministic fallback.
        }

        return BuildFallbackPlan(request);
    }

    private static PlanResponse BuildFallbackPlan(PlanRequest request)
    {
        var plan = new PlanGraph
        {
            Scope = request.UserIntent,
            Rationale = "Heuristic local fallback generated without remote model access.",
            EstimatedBenefit = "Quick duplicate cleanup and category-folder preparation.",
            RollbackStrategy = "Inverse operations plus quarantine restores.",
            Categories = request.Scan.Inventory.Select(static item => item.Category)
                .Where(static category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList()
        };

        var anchorRoot = request.PolicyProfile.MutableRoots.FirstOrDefault()
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        foreach (var category in plan.Categories)
        {
            var safeCategory = string.Concat(category.Where(char.IsLetterOrDigit));
            if (string.IsNullOrWhiteSpace(safeCategory))
            {
                continue;
            }

            plan.Operations.Add(new PlanOperation
            {
                Kind = OperationKind.CreateDirectory,
                DestinationPath = Path.Combine(anchorRoot, "Atlas Organized", safeCategory),
                Description = $"Create a destination folder for {category} files.",
                Confidence = 0.9d,
                Sensitivity = SensitivityLevel.Low
            });
        }

        foreach (var duplicate in request.Scan.Duplicates.Where(static group => group.Confidence >= 0.985d))
        {
            foreach (var duplicatePath in duplicate.Paths.Where(path => !string.Equals(path, duplicate.CanonicalPath, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Operations.Add(new PlanOperation
                {
                    Kind = OperationKind.DeleteToQuarantine,
                    SourcePath = duplicatePath,
                    Description = $"Quarantine a safe duplicate from group {duplicate.GroupId}.",
                    Confidence = duplicate.Confidence,
                    MarksSafeDuplicate = true,
                    Sensitivity = SensitivityLevel.Low,
                    GroupId = duplicate.GroupId
                });
            }
        }

        plan.RequiresReview = plan.Operations.Any(static operation => operation.Kind == OperationKind.DeleteToQuarantine);
        plan.RiskSummary = new RiskEnvelope
        {
            SensitivityScore = 0.25d,
            SystemScore = 0.2d,
            SyncRisk = 0.1d,
            ReversibilityScore = 0.95d,
            Confidence = 0.84d,
            ApprovalRequirement = plan.RequiresReview ? ApprovalRequirement.Review : ApprovalRequirement.None
        };

        return new PlanResponse
        {
            Plan = plan,
            Summary = "Local fallback plan prepared. Connect an OpenAI API key to enable model-authored reorganization plans."
        };
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return _options.ApiKey;

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_APIKEY")
            ?? string.Empty;
    }

    private string ResolveModel()
    {
        if (!string.IsNullOrWhiteSpace(_options.ResponsesModel))
            return _options.ResponsesModel;

        return Environment.GetEnvironmentVariable("ATLAS_OPENAI_MODEL") ?? "gpt-5";
    }

    private sealed class ResponsesPayload
    {
        public string OutputText
        {
            get
            {
                if (Output is null)
                {
                    return string.Empty;
                }

                foreach (var item in Output)
                {
                    if (item.Content is null)
                    {
                        continue;
                    }

                    foreach (var content in item.Content)
                    {
                        if (!string.IsNullOrWhiteSpace(content.Text))
                        {
                            return content.Text;
                        }
                    }
                }

                return string.Empty;
            }
        }

        public List<OutputItem>? Output { get; set; }
    }

    private sealed class OutputItem
    {
        public List<OutputContent>? Content { get; set; }
    }

    private sealed class OutputContent
    {
        public string? Text { get; set; }
    }
}
