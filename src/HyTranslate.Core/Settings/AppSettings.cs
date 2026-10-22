namespace HyTranslate.Core.Settings;

public class AppSettings
{
    public string Backend { get; set; } = "Built-in";
    public string EmbeddedModelPath { get; set; } = "";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "maternion/hy-mt2:1.8b";
    public string Theme { get; set; } = "System";
    public bool TranslateSheetNames { get; set; } = false;
    public string DefaultSourceLang { get; set; } = "auto";
    public string DefaultTargetLang { get; set; } = "tr";

    public string ModelsPath { get; set; } = "";          // empty = do not manage, use OllamaUrl
    public string OllamaExePath { get; set; } = "";       // empty = auto-detect
    public int ManagedPort { get; set; } = 11435;

    public string LastOutputFolder { get; set; } = "";   // empty = source file's folder
    public bool OcrEnabled { get; set; } = true;
    public string OcrLanguage { get; set; } = "";        // empty = automatic
}

