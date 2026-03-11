namespace Atlas.AI;

/// <summary>
/// Provides strict JSON schema definitions for OpenAI Responses API structured output.
/// Each schema follows the json_schema format used in the text.format parameter.
/// </summary>
public static class ResponseSchemas
{
    /// <summary>
    /// Returns the strict JSON schema for plan responses, mirroring the Atlas domain model types.
    /// </summary>
    public static object PlanResponseSchema => new
    {
        type = "json_schema",
        name = "atlas_plan_response",
        schema = new
        {
            type = "object",
            properties = new
            {
                summary = new { type = "string" },
                plan = PlanGraphSchema
            },
            required = new[] { "summary", "plan" },
            additionalProperties = false
        }
    };

    /// <summary>
    /// Returns the strict JSON schema for voice intent parsing responses.
    /// </summary>
    public static object VoiceIntentSchema => new
    {
        type = "json_schema",
        name = "atlas_voice_intent",
        schema = new
        {
            type = "object",
            properties = new
            {
                parsed_intent = new { type = "string" },
                needs_confirmation = new { type = "boolean" }
            },
            required = new[] { "parsed_intent", "needs_confirmation" },
            additionalProperties = false
        }
    };

    private static object PlanGraphSchema => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string" },
            rationale = new { type = "string" },
            categories = new
            {
                type = "array",
                items = new { type = "string" },
                maxItems = 20
            },
            operations = new
            {
                type = "array",
                items = OperationSchema,
                maxItems = 500
            },
            risk_summary = RiskEnvelopeSchema,
            estimated_benefit = new { type = "string" },
            requires_review = new { type = "boolean" },
            rollback_strategy = new { type = "string" }
        },
        required = new[]
        {
            "scope",
            "rationale",
            "operations",
            "risk_summary",
            "requires_review",
            "rollback_strategy"
        },
        additionalProperties = false
    };

    private static object OperationSchema => new
    {
        type = "object",
        properties = new
        {
            operation_id = new { type = "string" },
            kind = new
            {
                type = "string",
                @enum = new[]
                {
                    "CreateDirectory",
                    "MovePath",
                    "RenamePath",
                    "DeleteToQuarantine",
                    "RestoreFromQuarantine",
                    "MergeDuplicateGroup",
                    "ApplyOptimizationFix",
                    "RevertOptimizationFix"
                }
            },
            source_path = new { type = "string" },
            destination_path = new { type = "string" },
            description = new { type = "string" },
            confidence = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0
            },
            marks_safe_duplicate = new { type = "boolean" },
            sensitivity = new
            {
                type = "string",
                @enum = new[] { "Unknown", "Low", "Medium", "High", "Critical" }
            },
            group_id = new { type = "string" },
            optimization_kind = new
            {
                type = "string",
                @enum = new[]
                {
                    "Unknown",
                    "UserStartupEntry",
                    "ScheduledTask",
                    "TemporaryFiles",
                    "CacheCleanup",
                    "DuplicateArchives",
                    "BackgroundApplication",
                    "LowDiskPressure"
                }
            }
        },
        required = new[] { "kind", "description", "confidence", "sensitivity" },
        additionalProperties = false
    };

    private static object RiskEnvelopeSchema => new
    {
        type = "object",
        properties = new
        {
            sensitivity_score = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0
            },
            system_score = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0
            },
            sync_risk = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0
            },
            reversibility_score = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0
            },
            confidence = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0
            },
            approval_requirement = new
            {
                type = "string",
                @enum = new[] { "None", "Review", "ExplicitApproval" }
            },
            blocked_reasons = new
            {
                type = "array",
                items = new { type = "string" }
            }
        },
        required = new[]
        {
            "sensitivity_score",
            "system_score",
            "sync_risk",
            "reversibility_score",
            "confidence",
            "approval_requirement"
        },
        additionalProperties = false
    };
}
