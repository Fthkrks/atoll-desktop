namespace HyTranslate.Core.Translation;

public static class ModelPathResolver
{
    public const string BundledFileName = "hy-mt2-1.8b-q4_k_m.gguf";

    public static string? Resolve(string? userPath, string appFolder)
    {
        if (!string.IsNullOrWhiteSpace(userPath) && Path.IsPathFullyQualified(userPath) && File.Exists(userPath))
            return Path.GetFullPath(userPath);
        var bundled = Path.Combine(appFolder, "model", BundledFileName);
        return File.Exists(bundled) ? Path.GetFullPath(bundled) : null;
    }
}
