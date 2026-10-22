using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Languages;
using HyTranslate.Core.Ollama;

namespace HyTranslate.Core.Translation;

public class OllamaTranslator : ITranslator
{
    private readonly OllamaClient _client;

    public OllamaTranslator(OllamaClient client)
    {
        _client = client;
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        var prompt = Languages.Languages.BuildPrompt(text, targetLang, sourceLang);
        var rawResult = await _client.ChatAsync(prompt, ct: ct).ConfigureAwait(false);
        if (Languages.Languages.ContainsInstructionEcho(rawResult, text))
        {
            rawResult = await _client.ChatAsync(prompt, ct: ct).ConfigureAwait(false);
            if (Languages.Languages.ContainsInstructionEcho(rawResult, text))
            {
                return text;
            }
        }
        return Languages.Languages.PostProcessTranslation(rawResult, text);
    }

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = Languages.Languages.BuildPrompt(text, targetLang, sourceLang);
        var sb = new StringBuilder();

        await foreach (var chunk in _client.ChatStreamAsync(prompt, ct: ct).ConfigureAwait(false))
        {
            sb.Append(chunk);
            yield return chunk;
        }
    }
}
