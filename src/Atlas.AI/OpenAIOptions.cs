namespace Atlas.AI;

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string ResponsesModel { get; set; } = "gpt-5";
    public string RealtimeModel { get; set; } = "gpt-4o-realtime-preview";
    public string ApiKey { get; set; } = string.Empty;
    public int MaxInventoryItemsInPrompt { get; set; } = 250;
}