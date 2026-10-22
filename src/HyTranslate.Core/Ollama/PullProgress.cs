using System.Text.Json.Serialization;

namespace HyTranslate.Core.Ollama;

public record PullProgress
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("total")]
    public long Total { get; init; }

    [JsonPropertyName("completed")]
    public long Completed { get; init; }

    public double Percent => Total > 0 ? Math.Clamp((double)Completed / Total * 100.0, 0, 100) : 0;
}
