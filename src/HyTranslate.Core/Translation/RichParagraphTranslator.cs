using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Ollama;

namespace HyTranslate.Core.Translation;

public static class RichParagraphTranslator
{
    private static readonly Regex AnyTagRegex = new(@"</?g(?:\d+)?>", RegexOptions.Compiled);
    private static readonly Regex TagFormatRegex = new(@"^</?g(\d+)>$", RegexOptions.Compiled);

    private static readonly HashSet<string> NoSpaceLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "zh", "zh-Hant", "yue", "ja", "th", "km", "my", "bo"
    };

    public static Task<RichResult> TranslateRunsAsync(
        IReadOnlyList<string> runTexts,
        bool sameFormatting,
        TranslationService service,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        var safeRuns = runTexts ?? Array.Empty<string>();
        var formatKeys = sameFormatting
            ? Enumerable.Repeat("same", safeRuns.Count).ToList()
            : Enumerable.Range(0, safeRuns.Count).Select(i => $"fmt_{i}").ToList();
        return TranslateRunsAsync(safeRuns, formatKeys, service, targetLang, sourceLang, ct);
    }

    public static async Task<RichResult> TranslateRunsAsync(
        IReadOnlyList<string> runTexts,
        IReadOnlyList<string> formatKeys,
        TranslationService service,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        if (runTexts == null || runTexts.Count == 0)
        {
            return new RichResult(Array.Empty<(int, string)>(), false);
        }

        if (formatKeys == null || formatKeys.Count != runTexts.Count)
        {
            formatKeys = Enumerable.Repeat(string.Empty, runTexts.Count).ToList();
        }

        var allJoined = string.Concat(runTexts);
        if (TranslationService.ShouldSkip(allJoined))
        {
            var originalPieces = runTexts.Select((t, i) => (i, t)).ToList();
            return new RichResult(originalPieces, false);
        }

        // 2. Base format = the format key with the most characters in total.
        var charCountsByFormat = new Dictionary<string, int>();
        for (int i = 0; i < runTexts.Count; i++)
        {
            var key = formatKeys[i];
            charCountsByFormat[key] = charCountsByFormat.GetValueOrDefault(key) + runTexts[i].Length;
        }

        string baseFormatKey = formatKeys[0];
        int maxChars = -1;
        foreach (var key in formatKeys.Distinct())
        {
            int count = charCountsByFormat[key];
            if (count > maxChars)
            {
                maxChars = count;
                baseFormatKey = key;
            }
        }

        // Find base template run (the base-format run with the longest text)
        int baseTemplateRunIndex = -1;
        int maxBaseRunLen = -1;
        for (int i = 0; i < runTexts.Count; i++)
        {
            if (formatKeys[i] == baseFormatKey)
            {
                if (runTexts[i].Length > maxBaseRunLen)
                {
                    maxBaseRunLen = runTexts[i].Length;
                    baseTemplateRunIndex = i;
                }
            }
        }
        if (baseTemplateRunIndex == -1) baseTemplateRunIndex = 0;

        // 3. Merge adjacent runs that share a format key (logically).
        var mergedPieces = new List<(string FormatKey, string Text, int FirstRunIndex)>();
        for (int i = 0; i < runTexts.Count; i++)
        {
            string key = formatKeys[i];
            string text = runTexts[i];

            if (mergedPieces.Count > 0 && mergedPieces[^1].FormatKey == key)
            {
                var prev = mergedPieces[^1];
                mergedPieces[^1] = (prev.FormatKey, prev.Text + text, prev.FirstRunIndex);
            }
            else
            {
                mergedPieces.Add((key, text, i));
            }
        }

        // 4. Leave base-format pieces UNTAGGED; wrap only the other pieces with <gN>.
        // If no piece needs a tag, translate as plain text.
        bool needsTags = mergedPieces.Any(p => p.FormatKey != baseFormatKey);
        if (!needsTags)
        {
            var translated = await service.TranslateTextAsync(allJoined, targetLang, sourceLang, null, ct).ConfigureAwait(false);
            return new RichResult(new[] { (baseTemplateRunIndex, translated) }, false);
        }

        int tagCounter = 0;
        var taggedPieceTemplates = new Dictionary<int, int>(); // tagNumber -> firstRunIndex
        var sb = new StringBuilder();

        foreach (var piece in mergedPieces)
        {
            if (piece.FormatKey == baseFormatKey)
            {
                sb.Append(piece.Text);
            }
            else
            {
                tagCounter++;
                taggedPieceTemplates[tagCounter] = piece.FirstRunIndex;
                sb.Append($"<g{tagCounter}>{piece.Text}</g{tagCounter}>");
            }
        }

        int totalTags = tagCounter;
        var taggedInput = sb.ToString();

        string taggedOutput;
        try
        {
            taggedOutput = await service.TranslateRawAsync(taggedInput, targetLang, sourceLang, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OllamaException)
        {
            return await FallbackAsync(allJoined, baseTemplateRunIndex, service, targetLang, sourceLang, ct).ConfigureAwait(false);
        }

        // 7. Validation: every tag appears exactly once, there is no nesting, and the total output text is not empty.
        var (isValid, tagSpans) = ValidateAndExtractTags(taggedOutput, totalTags);
        if (!isValid)
        {
            return await FallbackAsync(allJoined, baseTemplateRunIndex, service, targetLang, sourceLang, ct).ConfigureAwait(false);
        }

        // 5. Parse the output into an ordered list of pieces: (FormatSource, Text).
        // An untagged piece -> base format (template = the base-format run with the longest text).
        // <gN> -> the format of tagged piece N (template = that piece's first run).
        var rawPieces = new List<(int TemplateRunIndex, string Text)>();
        int currPos = 0;

        foreach (var span in tagSpans)
        {
            if (span.OpenStart > currPos)
            {
                string untaggedText = taggedOutput.Substring(currPos, span.OpenStart - currPos);
                if (!string.IsNullOrEmpty(untaggedText))
                {
                    rawPieces.Add((baseTemplateRunIndex, untaggedText));
                }
            }

            string innerText = taggedOutput.Substring(span.OpenEnd, span.CloseStart - span.OpenEnd);
            int templateIndex = taggedPieceTemplates[span.TagNum];
            rawPieces.Add((templateIndex, innerText));

            currPos = span.CloseEnd;
        }

        if (currPos < taggedOutput.Length)
        {
            string trailingText = taggedOutput.Substring(currPos);
            if (!string.IsNullOrEmpty(trailingText))
            {
                rawPieces.Add((baseTemplateRunIndex, trailingText));
            }
        }

        // 8. Spacing: when joining pieces, if the end of one piece and the start of the next are both letters or digits
        // and the target language uses spaces (not zh, zh-Hant, yue, ja, th, km, my, bo), insert a single space between them.
        // Do not leave double spaces.
        bool usesSpaces = !NoSpaceLanguages.Contains(targetLang) && !targetLang.StartsWith("zh-", StringComparison.OrdinalIgnoreCase);

        var finalPieces = ApplySpacingAndMerge(rawPieces, baseTemplateRunIndex, usesSpaces);

        return new RichResult(finalPieces, false);
    }

    private record TagSpan(int TagNum, int OpenStart, int OpenEnd, int CloseStart, int CloseEnd);

    private static (bool IsValid, List<TagSpan> Spans) ValidateAndExtractTags(string output, int totalTags)
    {
        var spans = new List<TagSpan>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return (false, spans);
        }

        // Match any tags that look like g-tags
        var allTagMatches = Regex.Matches(output, @"</?g[^>]*>");
        if (allTagMatches.Count != totalTags * 2)
        {
            return (false, spans);
        }

        var openedTagNums = new HashSet<int>();
        var closedTagNums = new HashSet<int>();
        int currentOpenTag = 0;
        int currentOpenStart = -1;
        int currentOpenEnd = -1;

        foreach (Match m in allTagMatches)
        {
            var tagMatch = TagFormatRegex.Match(m.Value);
            if (!tagMatch.Success)
            {
                return (false, spans);
            }

            int tagNum = int.Parse(tagMatch.Groups[1].Value);
            if (tagNum < 1 || tagNum > totalTags)
            {
                return (false, spans);
            }

            bool isClose = m.Value.StartsWith("</");
            if (!isClose)
            {
                if (currentOpenTag != 0)
                {
                    return (false, spans);
                }
                if (!openedTagNums.Add(tagNum))
                {
                    return (false, spans);
                }
                currentOpenTag = tagNum;
                currentOpenStart = m.Index;
                currentOpenEnd = m.Index + m.Length;
            }
            else
            {
                if (currentOpenTag != tagNum)
                {
                    return (false, spans);
                }
                if (!closedTagNums.Add(tagNum))
                {
                    return (false, spans);
                }

                spans.Add(new TagSpan(tagNum, currentOpenStart, currentOpenEnd, m.Index, m.Index + m.Length));
                currentOpenTag = 0;
            }
        }

        if (currentOpenTag != 0 || openedTagNums.Count != totalTags || closedTagNums.Count != totalTags)
        {
            return (false, spans);
        }

        var plain = AnyTagRegex.Replace(output, "");
        if (string.IsNullOrWhiteSpace(plain))
        {
            return (false, spans);
        }

        return (true, spans);
    }

    private static List<(int TemplateRunIndex, string Text)> ApplySpacingAndMerge(
        List<(int TemplateRunIndex, string Text)> rawPieces,
        int baseTemplateRunIndex,
        bool usesSpaces)
    {
        var result = new List<(int TemplateRunIndex, string Text)>();

        for (int i = 0; i < rawPieces.Count; i++)
        {
            var current = rawPieces[i];
            if (string.IsNullOrEmpty(current.Text)) continue;

            if (result.Count > 0)
            {
                var prev = result[^1];

                if (usesSpaces)
                {
                    if (prev.Text.EndsWith(" ") && current.Text.StartsWith(" "))
                    {
                        current = (current.TemplateRunIndex, current.Text.TrimStart(' '));
                    }
                    else if (char.IsLetterOrDigit(prev.Text[^1]) && !string.IsNullOrEmpty(current.Text) && char.IsLetterOrDigit(current.Text[0]))
                    {
                        if (current.TemplateRunIndex == baseTemplateRunIndex)
                        {
                            current = (current.TemplateRunIndex, " " + current.Text);
                        }
                        else
                        {
                            prev = (prev.TemplateRunIndex, prev.Text + " ");
                            result[^1] = prev;
                        }
                    }
                }

                if (prev.TemplateRunIndex == current.TemplateRunIndex)
                {
                    result[^1] = (prev.TemplateRunIndex, prev.Text + current.Text);
                    continue;
                }
            }

            if (!string.IsNullOrEmpty(current.Text))
            {
                result.Add(current);
            }
        }

        return result;
    }

    private static async Task<RichResult> FallbackAsync(
        string allJoined,
        int baseTemplateRunIndex,
        TranslationService service,
        string targetLang,
        string? sourceLang,
        CancellationToken ct)
    {
        string translated;
        if (TranslationService.ShouldSkip(allJoined))
        {
            translated = allJoined;
        }
        else
        {
            translated = await service.TranslateTextAsync(allJoined, targetLang, sourceLang, null, ct).ConfigureAwait(false);
        }

        return new RichResult(new[] { (baseTemplateRunIndex, translated) }, true);
    }
}
