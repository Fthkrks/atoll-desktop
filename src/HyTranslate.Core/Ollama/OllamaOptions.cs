using System.Text.Json.Serialization;

namespace HyTranslate.Core.Ollama;

public class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("top_p")]
    public double TopP { get; set; } = 0.6;

    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 20;

    [JsonPropertyName("repeat_penalty")]
    public double RepeatPenalty { get; set; } = 1.05;

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; set; } = 4096;
}
