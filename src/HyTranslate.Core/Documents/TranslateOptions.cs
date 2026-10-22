namespace HyTranslate.Core.Documents;

public record TranslateOptions
{
    public string TargetLang { get; init; } = "tr";
    public string? SourceLang { get; init; }
    public bool TranslateSheetNames { get; init; } = false;
    public bool UseOcr { get; init; } = true;

    public TranslateOptions() { }

    public TranslateOptions(string targetLang, string? sourceLang = null, bool translateSheetNames = false, bool useOcr = true)
    {
        TargetLang = targetLang;
        SourceLang = sourceLang;
        TranslateSheetNames = translateSheetNames;
        UseOcr = useOcr;
    }
}
