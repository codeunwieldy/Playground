using System.Net;
using System.Text.Json;
using Atlas.AI;
using Atlas.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Atlas.AI.Tests;

/// <summary>
/// Tests that OpenAIResponsesPlanningClient uses OpenAIOptions for configuration,
/// handles invalid model output by falling back, and integrates semantic validation.
/// Uses deterministic fake HTTP responses - no live network calls.
/// </summary>
public class PlanningClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static PlanRequest BuildMinimalRequest() => new()
    {
        UserIntent = "Organize documents",
        Scan = new ScanResponse
        {
            Volumes = new List<VolumeSnapshot>(),
            Inventory = new List<FileInventoryItem>
            {
                new() { Path = @"C:\Users\Test\file.txt", Category = "Documents" }
            },
            Duplicates = new List<DuplicateGroup>()
        },
        PolicyProfile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test" },
            ScanRoots = new List<string> { @"C:\Users\Test" }
        }
    };

    private static IOptions<OpenAIOptions> BuildOptions(string apiKey = "", string model = "gpt-5", string baseUrl = "https://api.openai.com", int maxItems = 250)
    {
        return Options.Create(new OpenAIOptions
        {
            ApiKey = apiKey,
            ResponsesModel = model,
            BaseUrl = baseUrl,
            MaxInventoryItemsInPrompt = maxItems
        });
    }

    private static HttpClient BuildFakeClient(HttpStatusCode statusCode, object responseBody)
    {
        var handler = new FakeHttpHandler(statusCode, responseBody);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
    }

    // ---- No API key returns fallback ----

    [Fact]
    public async Task CreatePlanAsync_NoApiKey_ReturnsFallback()
    {
        var client = new OpenAIResponsesPlanningClient(
            new HttpClient { BaseAddress = new Uri("https://api.openai.com") },
            BuildOptions(apiKey: ""));

        var result = await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        Assert.Contains("fallback", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public async Task ParseVoiceIntentAsync_NoApiKey_ReturnsTranscript()
    {
        var client = new OpenAIResponsesPlanningClient(
            new HttpClient { BaseAddress = new Uri("https://api.openai.com") },
            BuildOptions(apiKey: ""));

        var result = await client.ParseVoiceIntentAsync(
            new VoiceIntentRequest { Transcript = " organize photos " },
            CancellationToken.None);

        Assert.Equal("organize photos", result.ParsedIntent);
        Assert.True(result.NeedsConfirmation);
    }

    // ---- Valid model output accepted ----

    [Fact]
    public async Task CreatePlanAsync_ValidModelOutput_ReturnsParsedPlan()
    {
        var fakePlan = new
        {
            summary = "Organize by category.",
            plan = new
            {
                plan_id = "abc123",
                scope = "Organize documents",
                rationale = "Sort by category for easier navigation.",
                categories = new[] { "Documents" },
                operations = new[]
                {
                    new
                    {
                        operation_id = "op1",
                        kind = "CreateDirectory",
                        source_path = "",
                        destination_path = @"C:\Users\Test\Organized",
                        description = "Create folder.",
                        confidence = 0.95,
                        marks_safe_duplicate = false,
                        sensitivity = "Low",
                        group_id = "",
                        optimization_kind = "Unknown"
                    }
                },
                risk_summary = new
                {
                    sensitivity_score = 0.2,
                    system_score = 0.2,
                    sync_risk = 0.1,
                    reversibility_score = 0.9,
                    confidence = 0.85,
                    approval_requirement = "None",
                    blocked_reasons = Array.Empty<string>()
                },
                estimated_benefit = "Better organization.",
                requires_review = false,
                rollback_strategy = "Inverse operations."
            }
        };

        var responsePayload = new
        {
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new { text = JsonSerializer.Serialize(fakePlan, JsonOptions) }
                    }
                }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        Assert.Equal("Organize by category.", result.Summary);
        Assert.NotNull(result.Plan);
        Assert.Single(result.Plan.Operations);
        Assert.Equal(OperationKind.CreateDirectory, result.Plan.Operations[0].Kind);
    }

    // ---- Invalid model output triggers fallback ----

    [Fact]
    public async Task CreatePlanAsync_InvalidJson_ReturnsFallback()
    {
        var responsePayload = new
        {
            output = new[]
            {
                new { content = new[] { new { text = "THIS IS NOT JSON" } } }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        Assert.Contains("fallback", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_SemanticValidationFails_ReturnsFallback()
    {
        // Plan targets a protected path - should fail semantic validation
        var fakePlan = new
        {
            summary = "Bad plan targeting protected paths.",
            plan = new
            {
                plan_id = "bad1",
                scope = "Delete system files",
                rationale = "Misguided cleanup.",
                categories = Array.Empty<string>(),
                operations = new[]
                {
                    new
                    {
                        operation_id = "op1",
                        kind = "DeleteToQuarantine",
                        source_path = @"C:\Windows\System32\ntdll.dll",
                        destination_path = "",
                        description = "Delete system file.",
                        confidence = 0.9,
                        marks_safe_duplicate = false,
                        sensitivity = "Low",
                        group_id = "",
                        optimization_kind = "Unknown"
                    }
                },
                risk_summary = new
                {
                    sensitivity_score = 0.2,
                    system_score = 0.6,
                    sync_risk = 0.0,
                    reversibility_score = 0.9,
                    confidence = 0.9,
                    approval_requirement = "None",
                    blocked_reasons = Array.Empty<string>()
                },
                estimated_benefit = "Space savings.",
                requires_review = false,
                rollback_strategy = "Restore from quarantine."
            }
        };

        var responsePayload = new
        {
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new { text = JsonSerializer.Serialize(fakePlan, JsonOptions) }
                    }
                }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        // Should fallback because plan targets C:\Windows
        Assert.Contains("fallback", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_EmptyOutputText_ReturnsFallback()
    {
        var responsePayload = new
        {
            output = new[]
            {
                new { content = new[] { new { text = "" } } }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        Assert.Contains("fallback", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_FallbackSkipsSensitiveDuplicateDeletes()
    {
        var request = BuildMinimalRequest();
        request.Scan.Inventory.AddRange(
        [
            new FileInventoryItem
            {
                Path = @"C:\Users\Test\Documents\tax.pdf",
                Name = "tax.pdf",
                Extension = ".pdf",
                Category = "Documents",
                Sensitivity = SensitivityLevel.Low
            },
            new FileInventoryItem
            {
                Path = @"C:\Users\Test\Finance\tax-copy.pdf",
                Name = "tax-copy.pdf",
                Extension = ".pdf",
                Category = "Documents",
                Sensitivity = SensitivityLevel.High
            }
        ]);
        request.Scan.Duplicates.Add(new DuplicateGroup
        {
            CanonicalPath = @"C:\Users\Test\Documents\tax.pdf",
            Confidence = 0.995d,
            Paths =
            {
                @"C:\Users\Test\Documents\tax.pdf",
                @"C:\Users\Test\Finance\tax-copy.pdf"
            }
        });

        var client = new OpenAIResponsesPlanningClient(
            new HttpClient { BaseAddress = new Uri("https://api.openai.com") },
            BuildOptions(apiKey: ""));

        var result = await client.CreatePlanAsync(request, CancellationToken.None);

        Assert.DoesNotContain(result.Plan.Operations, static operation => operation.Kind == OperationKind.DeleteToQuarantine);
        Assert.Contains("excluded sensitive", result.Plan.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_FallbackUsesActualDuplicateSensitivityAndGroupId()
    {
        var request = BuildMinimalRequest();
        request.Scan.Inventory.AddRange(
        [
            new FileInventoryItem
            {
                Path = @"C:\Users\Test\Documents\notes.txt",
                Name = "notes.txt",
                Extension = ".txt",
                Category = "Documents",
                Sensitivity = SensitivityLevel.Low
            },
            new FileInventoryItem
            {
                Path = @"C:\Users\Test\Downloads\notes-copy.txt",
                Name = "notes-copy.txt",
                Extension = ".txt",
                Category = "Documents",
                Sensitivity = SensitivityLevel.Low
            }
        ]);
        request.Scan.Duplicates.Add(new DuplicateGroup
        {
            GroupId = "dup-001",
            CanonicalPath = @"C:\Users\Test\Documents\notes.txt",
            Confidence = 0.995d,
            Paths =
            {
                @"C:\Users\Test\Documents\notes.txt",
                @"C:\Users\Test\Downloads\notes-copy.txt"
            }
        });

        var client = new OpenAIResponsesPlanningClient(
            new HttpClient { BaseAddress = new Uri("https://api.openai.com") },
            BuildOptions(apiKey: ""));

        var result = await client.CreatePlanAsync(request, CancellationToken.None);

        var operation = Assert.Single(result.Plan.Operations.Where(static operation => operation.Kind == OperationKind.DeleteToQuarantine));
        Assert.Equal(SensitivityLevel.Low, operation.Sensitivity);
        Assert.Equal("dup-001", operation.GroupId);
        Assert.True(operation.MarksSafeDuplicate);
    }

    // ---- Voice intent parsing ----

    [Fact]
    public async Task ParseVoiceIntentAsync_ValidResponse_Parsed()
    {
        var voiceResponse = new
        {
            parsed_intent = "organize photos by year",
            needs_confirmation = false
        };

        var responsePayload = new
        {
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new { text = JsonSerializer.Serialize(voiceResponse, JsonOptions) }
                    }
                }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.ParseVoiceIntentAsync(
            new VoiceIntentRequest { Transcript = "organize my photos by year" },
            CancellationToken.None);

        Assert.Equal("organize photos by year", result.ParsedIntent);
        Assert.False(result.NeedsConfirmation);
    }

    [Theory]
    [InlineData("delete old finance docs", "delete old finance docs")]
    [InlineData("move AppData to D drive", "move appdata to d drive")]
    [InlineData("clean everything", "clean everything")]
    public async Task ParseVoiceIntentAsync_RiskyVoiceIntent_ForcesConfirmation(string transcript, string parsedIntent)
    {
        var voiceResponse = new
        {
            parsed_intent = parsedIntent,
            needs_confirmation = false
        };

        var responsePayload = new
        {
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new { text = JsonSerializer.Serialize(voiceResponse, JsonOptions) }
                    }
                }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.ParseVoiceIntentAsync(
            new VoiceIntentRequest { Transcript = transcript },
            CancellationToken.None);

        Assert.Equal(parsedIntent, result.ParsedIntent);
        Assert.True(result.NeedsConfirmation);
    }

    [Fact]
    public async Task ParseVoiceIntentAsync_InvalidJson_FallsBackGracefully()
    {
        var responsePayload = new
        {
            output = new[]
            {
                new { content = new[] { new { text = "NOT JSON" } } }
            }
        };

        var httpClient = BuildFakeClient(HttpStatusCode.OK, responsePayload);
        var client = new OpenAIResponsesPlanningClient(httpClient, BuildOptions(apiKey: "test-key"));

        var result = await client.ParseVoiceIntentAsync(
            new VoiceIntentRequest { Transcript = "organize photos" },
            CancellationToken.None);

        // Should fall back gracefully - NeedsConfirmation should be true
        Assert.True(result.NeedsConfirmation);
        Assert.False(string.IsNullOrWhiteSpace(result.ParsedIntent));
    }

    // ---- OpenAIOptions is actually used ----

    [Fact]
    public async Task CreatePlanAsync_UsesConfiguredModel()
    {
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, new
        {
            output = new[]
            {
                new { content = new[] { new { text = "{}" } } }
            }
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var client = new OpenAIResponsesPlanningClient(
            httpClient,
            BuildOptions(apiKey: "test-key-123", model: "gpt-4o-custom"));

        await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("gpt-4o-custom", handler.CapturedRequestBody);
    }

    [Fact]
    public async Task CreatePlanAsync_UsesProjectedInventoryPayload_WithMimeSignals()
    {
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, new
        {
            output = new[]
            {
                new { content = new[] { new { text = "{}" } } }
            }
        });

        var request = BuildMinimalRequest();
        request.Scan.Inventory.Add(new FileInventoryItem
        {
            Path = @"C:\Users\Test\Finance\statement.pdf",
            Name = "statement.pdf",
            Extension = ".pdf",
            Category = "Documents",
            MimeType = "application/pdf",
            ContentFingerprint = "fp-001",
            Sensitivity = SensitivityLevel.High,
            IsDuplicateCandidate = true
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var client = new OpenAIResponsesPlanningClient(
            httpClient,
            BuildOptions(apiKey: "test-key-123", model: "gpt-5"));

        await client.CreatePlanAsync(request, CancellationToken.None);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("inventory_projection", handler.CapturedRequestBody);
        Assert.Contains("duplicate_projection", handler.CapturedRequestBody);
        Assert.Contains("volume_projection", handler.CapturedRequestBody);
        Assert.Contains("application/pdf", handler.CapturedRequestBody);
        Assert.Contains("has_content_fingerprint", handler.CapturedRequestBody);
        Assert.DoesNotContain("\"content_fingerprint\"", handler.CapturedRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_RedactsSensitivePaths_WhenUploadSensitiveContentIsFalse()
    {
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, new
        {
            output = new[]
            {
                new { content = new[] { new { text = "{}" } } }
            }
        });

        var request = BuildMinimalRequest();
        request.PolicyProfile.UploadSensitiveContent = false;
        request.Scan.Inventory.Add(new FileInventoryItem
        {
            Path = @"C:\Users\Test\Finance\statement.pdf",
            Name = "statement.pdf",
            Extension = ".pdf",
            Category = "Documents",
            MimeType = "application/pdf",
            Sensitivity = SensitivityLevel.High,
            IsDuplicateCandidate = true
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var client = new OpenAIResponsesPlanningClient(
            httpClient,
            BuildOptions(apiKey: "test-key-123", model: "gpt-5"));

        await client.CreatePlanAsync(request, CancellationToken.None);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("sensitive-item-001", handler.CapturedRequestBody);
        Assert.DoesNotContain(@"C:\\Users\\Test\\Finance\\statement.pdf", handler.CapturedRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_UsesAuthorizationHeader()
    {
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, new
        {
            output = new[]
            {
                new { content = new[] { new { text = "{}" } } }
            }
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var client = new OpenAIResponsesPlanningClient(
            httpClient,
            BuildOptions(apiKey: "sk-test-my-key-xyz"));

        await client.CreatePlanAsync(BuildMinimalRequest(), CancellationToken.None);

        Assert.Equal("sk-test-my-key-xyz", handler.CapturedAuthorizationToken);
    }

    // ---- Helpers ----

    /// <summary>
    /// Simple fake HTTP handler that returns a canned response.
    /// </summary>
    private sealed class FakeHttpHandler(HttpStatusCode statusCode, object responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(responseBody, JsonOptions);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Fake HTTP handler that captures the request for assertion.
    /// </summary>
    private sealed class CapturingHttpHandler(HttpStatusCode statusCode, object responseBody) : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }
        public string? CapturedAuthorizationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedAuthorizationToken = request.Headers.Authorization?.Parameter;
            if (request.Content is not null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var json = JsonSerializer.Serialize(responseBody, JsonOptions);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
