using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyTranslate.Core.Translation;

public interface ITranslator
{
    Task<string> TranslateAsync(string text, string targetLang, string? sourceLang = null, CancellationToken ct = default);
    IAsyncEnumerable<string> TranslateStreamAsync(string text, string targetLang, string? sourceLang = null, CancellationToken ct = default);
}
