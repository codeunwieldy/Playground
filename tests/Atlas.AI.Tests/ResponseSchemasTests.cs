using System.Text.Json;
using Atlas.AI;

namespace Atlas.AI.Tests;

/// <summary>
/// Tests for ResponseSchemas - verifies that the schema definitions produce
/// valid JSON schema objects with the expected structure.
/// </summary>
public class ResponseSchemasTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    [Fact]
    public void PlanResponseSchema_SerializesToJson()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(json));
    }

    [Fact]
    public void PlanResponseSchema_HasRequiredTopLevelFields()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("json_schema", root.GetProperty("type").GetString());
        Assert.Equal("atlas_plan_response", root.GetProperty("name").GetString());

        var schemaObj = root.GetProperty("schema");
        Assert.Equal("object", schemaObj.GetProperty("type").GetString());

        var properties = schemaObj.GetProperty("properties");
        Assert.True(properties.TryGetProperty("summary", out _));
        Assert.True(properties.TryGetProperty("plan", out _));

        Assert.False(schemaObj.GetProperty("additional_properties").GetBoolean());
    }

    [Fact]
    public void PlanResponseSchema_PlanHasOperationsArray()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var planProps = doc.RootElement
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("plan")
            .GetProperty("properties");

        Assert.True(planProps.TryGetProperty("operations", out var ops));
        Assert.Equal("array", ops.GetProperty("type").GetString());
        Assert.Equal(500, ops.GetProperty("max_items").GetInt32());
    }

    [Fact]
    public void PlanResponseSchema_OperationsHaveKindEnum()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var opsItems = doc.RootElement
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("plan")
            .GetProperty("properties")
            .GetProperty("operations")
            .GetProperty("items");

        var kind = opsItems.GetProperty("properties").GetProperty("kind");
        Assert.Equal("string", kind.GetProperty("type").GetString());

        var enumValues = kind.GetProperty("enum");
        var values = new List<string>();
        foreach (var item in enumValues.EnumerateArray())
        {
            values.Add(item.GetString()!);
        }

        Assert.Contains("CreateDirectory", values);
        Assert.Contains("MovePath", values);
        Assert.Contains("RenamePath", values);
        Assert.Contains("DeleteToQuarantine", values);
        Assert.Contains("RestoreFromQuarantine", values);
        Assert.Contains("MergeDuplicateGroup", values);
        Assert.Contains("ApplyOptimizationFix", values);
        Assert.Contains("RevertOptimizationFix", values);
        Assert.Equal(8, values.Count);
    }

    [Fact]
    public void PlanResponseSchema_ConfidenceHasBounds()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var confidence = doc.RootElement
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("plan")
            .GetProperty("properties")
            .GetProperty("operations")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("confidence");

        Assert.Equal("number", confidence.GetProperty("type").GetString());
        Assert.Equal(0.0, confidence.GetProperty("minimum").GetDouble());
        Assert.Equal(1.0, confidence.GetProperty("maximum").GetDouble());
    }

    [Fact]
    public void PlanResponseSchema_RiskEnvelopeHasBoundedScores()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var risk = doc.RootElement
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("plan")
            .GetProperty("properties")
            .GetProperty("risk_summary");

        var riskProps = risk.GetProperty("properties");

        foreach (var scoreName in new[] { "sensitivity_score", "system_score", "sync_risk", "reversibility_score", "confidence" })
        {
            var score = riskProps.GetProperty(scoreName);
            Assert.Equal(0.0, score.GetProperty("minimum").GetDouble());
            Assert.Equal(1.0, score.GetProperty("maximum").GetDouble());
        }
    }

    [Fact]
    public void PlanResponseSchema_AdditionalPropertiesDisabled()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var schemaObj = doc.RootElement.GetProperty("schema");
        Assert.False(schemaObj.GetProperty("additional_properties").GetBoolean());

        var plan = schemaObj.GetProperty("properties").GetProperty("plan");
        Assert.False(plan.GetProperty("additional_properties").GetBoolean());

        var opsItems = plan.GetProperty("properties").GetProperty("operations").GetProperty("items");
        Assert.False(opsItems.GetProperty("additional_properties").GetBoolean());

        var risk = plan.GetProperty("properties").GetProperty("risk_summary");
        Assert.False(risk.GetProperty("additional_properties").GetBoolean());
    }

    [Fact]
    public void VoiceIntentSchema_SerializesToJson()
    {
        var schema = ResponseSchemas.VoiceIntentSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(json));
    }

    [Fact]
    public void VoiceIntentSchema_HasExpectedFields()
    {
        var schema = ResponseSchemas.VoiceIntentSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("json_schema", root.GetProperty("type").GetString());
        Assert.Equal("atlas_voice_intent", root.GetProperty("name").GetString());

        var schemaObj = root.GetProperty("schema");
        var properties = schemaObj.GetProperty("properties");
        Assert.True(properties.TryGetProperty("parsed_intent", out _));
        Assert.True(properties.TryGetProperty("needs_confirmation", out _));

        Assert.False(schemaObj.GetProperty("additional_properties").GetBoolean());
    }

    [Fact]
    public void PlanResponseSchema_SensitivityEnum_MatchesDomainModel()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var sensitivity = doc.RootElement
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("plan")
            .GetProperty("properties")
            .GetProperty("operations")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("sensitivity");

        var values = new List<string>();
        foreach (var item in sensitivity.GetProperty("enum").EnumerateArray())
        {
            values.Add(item.GetString()!);
        }

        Assert.Contains("Unknown", values);
        Assert.Contains("Low", values);
        Assert.Contains("Medium", values);
        Assert.Contains("High", values);
        Assert.Contains("Critical", values);
        Assert.Equal(5, values.Count);
    }

    [Fact]
    public void PlanResponseSchema_ApprovalRequirementEnum_MatchesDomainModel()
    {
        var schema = ResponseSchemas.PlanResponseSchema;
        var json = JsonSerializer.Serialize(schema, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var approval = doc.RootElement
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("plan")
            .GetProperty("properties")
            .GetProperty("risk_summary")
            .GetProperty("properties")
            .GetProperty("approval_requirement");

        var values = new List<string>();
        foreach (var item in approval.GetProperty("enum").EnumerateArray())
        {
            values.Add(item.GetString()!);
        }

        Assert.Contains("None", values);
        Assert.Contains("Review", values);
        Assert.Contains("ExplicitApproval", values);
        Assert.Equal(3, values.Count);
    }
}
