using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HyTranslate.Core.Translation;

public class TranslationService
{
    private readonly ITranslator _translator;
    private readonly SemaphoreSlim _semaphore = new(2, 2);
    private readonly ConcurrentDictionary<(string Text, string? Source, string Target), string> _cache = new();

    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlRegex = new(@"^(https?://|ftp://|www\.)[^\s/$.?#].[^\s]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ITranslator Translator => _translator;
    public int CacheCount => _cache.Count;

    public TranslationService(ITranslator translator)
    {
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
    }

    public void ClearCache() => _cache.Clear();

    public static bool ShouldSkip(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();

        // Shorter than 2 letters or no letters (covers digits and punctuation only)
        int letterCount = trimmed.Count(char.IsLetter);
        if (letterCount < 2)
            return true;

        // URLs
        if (UrlRegex.IsMatch(trimmed) ||
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Email addresses
        if (EmailRegex.IsMatch(trimmed))
        {
            return true;
        }

        return false;
    }

    public async Task<string> TranslateTextAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        IProgress<TranslationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Whitespace preservation: isolate leading and trailing whitespace
        int leadingCount = 0;
        while (leadingCount < text.Length && char.IsWhiteSpace(text[leadingCount]))
            leadingCount++;

        if (leadingCount == text.Length)
            return text; // entirely whitespace

        int trailingCount = 0;
        while (trailingCount < text.Length - leadingCount && char.IsWhiteSpace(text[text.Length - 1 - trailingCount]))
            trailingCount++;

        string leading = text[..leadingCount];
        string trailing = text[(text.Length - trailingCount)..];
        string core = text.Substring(leadingCount, text.Length - leadingCount - trailingCount);

        if (ShouldSkip(core))
            return text;

        string translatedCore;
        if (core.Length <= 1500)
        {
            translatedCore = await TranslateSegmentCachedAsync(core, targetLang, sourceLang, ct).ConfigureAwait(false);
        }
        else
        {
            translatedCore = await TranslateLongTextAsync(core, targetLang, sourceLang, ct).ConfigureAwait(false);
        }

        return leading + translatedCore + trailing;
    }

    public async Task<string> TranslateRawAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        return await TranslateSegmentCachedAsync(text, targetLang, sourceLang, ct).ConfigureAwait(false);
    }

    private async Task<string> TranslateSegmentCachedAsync(
        string segment,
        string targetLang,
        string? sourceLang,
        CancellationToken ct)
    {
        var key = (segment, sourceLang, targetLang);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out cached))
                return cached;

            var translated = await _translator.TranslateAsync(segment, targetLang, sourceLang, ct).ConfigureAwait(false);

            // Safety guard: if output contains instruction echo not in source, retry once
            if (Languages.Languages.ContainsInstructionEcho(translated, segment))
            {
                translated = await _translator.TranslateAsync(segment, targetLang, sourceLang, ct).ConfigureAwait(false);
                if (Languages.Languages.ContainsInstructionEcho(translated, segment))
                {
                    translated = segment;
                }
            }

            translated = Languages.Languages.PostProcessTranslation(translated, segment);
            _cache[key] = translated;
            return translated;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string> TranslateLongTextAsync(
        string text,
        string targetLang,
        string? sourceLang,
        CancellationToken ct)
    {
        // Split by paragraph boundaries, keeping delimiters
        var paragraphParts = Regex.Split(text, @"(\r?\n\s*\r?\n)");
        var sb = new StringBuilder();

        for (int i = 0; i < paragraphParts.Length; i++)
        {
            var part = paragraphParts[i];
            // Even indices are text, odd indices are delimiters
            if (i % 2 == 1)
            {
                sb.Append(part);
                continue;
            }

            if (string.IsNullOrWhiteSpace(part))
            {
                sb.Append(part);
                continue;
            }

            if (part.Length <= 1500)
            {
                var translated = await TranslateTextAsync(part, targetLang, sourceLang, null, ct).ConfigureAwait(false);
                sb.Append(translated);
            }
            else
            {
                // Split by sentence boundaries: e.g. delimiter (?<=[.!?])\s+
                var sentenceParts = Regex.Split(part, @"(?<=[.!?])(\s+)");
                var currentChunk = new StringBuilder();

                for (int j = 0; j < sentenceParts.Length; j++)
                {
                    var sp = sentenceParts[j];
                    if (currentChunk.Length + sp.Length > 1500 && currentChunk.Length > 0)
                    {
                        var tr = await TranslateTextAsync(currentChunk.ToString(), targetLang, sourceLang, null, ct).ConfigureAwait(false);
                        sb.Append(tr);
                        currentChunk.Clear();
                    }
                    currentChunk.Append(sp);
                }

                if (currentChunk.Length > 0)
                {
                    var tr = await TranslateTextAsync(currentChunk.ToString(), targetLang, sourceLang, null, ct).ConfigureAwait(false);
                    sb.Append(tr);
                }
            }
        }

        return sb.ToString();
    }
}
