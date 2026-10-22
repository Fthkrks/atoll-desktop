using System;

namespace HyTranslate.Core.Translation;

public record TranslationProgress
{
    public int Done { get; init; }
    public int Total { get; init; }
    public string? CurrentPart { get; init; }
    public string? Message { get; init; }

    public double Percent => Total > 0 ? Math.Clamp((double)Done / Total * 100.0, 0, 100) : 0;

    public TranslationProgress(int done, int total, string? currentPart = null, string? message = null)
    {
        Done = done;
        Total = total;
        CurrentPart = currentPart;
        Message = message;
    }
}
