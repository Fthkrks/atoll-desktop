using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HyTranslate.Core.Translation;

public class FakeTranslator : ITranslator
{
    private readonly Func<string, string> _translateFunc;

    public int CallCount { get; private set; }

    public FakeTranslator(string prefix = "ÇEVRİ:")
    {
        _translateFunc = text =>
        {
            if (string.IsNullOrEmpty(text)) return text;

            // If text contains <gN> tags, translate inner content and untagged content, preserving tags
            var tagRegex = new Regex(@"<g(\d+)>(.*?)</g\1>", RegexOptions.Singleline);
            if (tagRegex.IsMatch(text))
            {
                var sb = new StringBuilder();
                int lastIdx = 0;
                foreach (Match m in tagRegex.Matches(text))
                {
                    if (m.Index > lastIdx)
                    {
                        var untagged = text[lastIdx..m.Index];
                        sb.Append(string.IsNullOrWhiteSpace(untagged) ? untagged : prefix + untagged);
                    }
                    var tagNum = m.Groups[1].Value;
                    var inner = m.Groups[2].Value;
                    sb.Append($"<g{tagNum}>{(string.IsNullOrWhiteSpace(inner) ? inner : prefix + inner)}</g{tagNum}>");
                    lastIdx = m.Index + m.Length;
                }
                if (lastIdx < text.Length)
                {
                    var untagged = text[lastIdx..];
                    sb.Append(string.IsNullOrWhiteSpace(untagged) ? untagged : prefix + untagged);
                }
                return sb.ToString();
            }

            return prefix + text;
        };
    }

    public FakeTranslator(Func<string, string> translateFunc)
    {
        _translateFunc = translateFunc ?? throw new ArgumentNullException(nameof(translateFunc));
    }

    public Task<string> TranslateAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(_translateFunc(text));
    }

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        CallCount++;
        var result = _translateFunc(text);
        yield return result;
        await Task.CompletedTask;
    }

    public void ResetCount()
    {
        CallCount = 0;
    }
}
