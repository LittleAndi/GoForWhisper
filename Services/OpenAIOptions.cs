namespace Services;

public sealed class OpenAIOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
