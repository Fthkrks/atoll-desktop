using HyTranslate.Core.Settings;

namespace HyTranslate.Core.Translation;

public sealed class BackendTranslator(AppSettings settings, LlamaSharpTranslator embedded, OllamaTranslator ollama) : ITranslator
{
    private ITranslator Current => settings.Backend == "Ollama" ? ollama : embedded;
    public Task<string> TranslateAsync(string text, string targetLang, string? sourceLang = null, CancellationToken ct = default) =>
        Current.TranslateAsync(text, targetLang, sourceLang, ct);
    public IAsyncEnumerable<string> TranslateStreamAsync(string text, string targetLang, string? sourceLang = null, CancellationToken ct = default) =>
        Current.TranslateStreamAsync(text, targetLang, sourceLang, ct);
}
