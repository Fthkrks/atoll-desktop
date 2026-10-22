using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Translation;

namespace HyTranslate.Core.Documents;

public class MdDocumentTranslator : IDocumentTranslator
{
    private readonly TranslationService _translationService;

    public string[] Extensions => new[] { ".md", ".markdown" };

    public MdDocumentTranslator(TranslationService translationService)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
    }

    public async Task TranslateAsync(
        string inputPath,
        string outputPath,
        TranslateOptions opt,
        IProgress<TranslationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var (encoding, lineEnding) = DetectEncodingAndLineEnding(inputPath);
        var content = await File.ReadAllTextAsync(inputPath, encoding, ct).ConfigureAwait(false);
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var outputLines = new List<string>();

        bool inFrontMatter = false;
        bool inCodeBlock = false;
        string codeFence = string.Empty;

        int total = lines.Length;
        int done = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[i];

            // 1. Front matter (--- or +++) at start of file
            if (i == 0 && (line.Trim() == "---" || line.Trim() == "+++"))
            {
                inFrontMatter = true;
                outputLines.Add(line);
                done++;
                continue;
            }

            if (inFrontMatter)
            {
                outputLines.Add(line);
                if (line.Trim() == "---" || line.Trim() == "+++")
                {
                    inFrontMatter = false;
                }
                done++;
                continue;
            }

            // 2. Fenced code blocks (``` or ~~~)
            var trimmed = line.TrimStart();
            if (!inCodeBlock && (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")))
            {
                inCodeBlock = true;
                codeFence = trimmed[..3];
                outputLines.Add(line);
                done++;
                continue;
            }

            if (inCodeBlock)
            {
                outputLines.Add(line);
                if (trimmed.StartsWith(codeFence))
                {
                    inCodeBlock = false;
                    codeFence = string.Empty;
                }
                done++;
                continue;
            }

            // 3. Blank or whitespace lines
            if (string.IsNullOrWhiteSpace(line))
            {
                outputLines.Add(line);
                done++;
                continue;
            }

            // 4. Table separator lines (| --- | :---: |)
            if (IsTableSeparator(line))
            {
                outputLines.Add(line);
                done++;
                continue;
            }

            // 5. Line with prefix (headings, lists, blockquotes)
            var translatedLine = await TranslateMarkdownLineAsync(line, opt, ct).ConfigureAwait(false);
            outputLines.Add(translatedLine);
            done++;
            progress?.Report(new TranslationProgress(done, total, $"Satır {done}/{total}"));
        }

        var result = string.Join(lineEnding, outputLines);
        await File.WriteAllTextAsync(outputPath, result, encoding, ct).ConfigureAwait(false);
    }

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.Contains('-')) return false;
        return Regex.IsMatch(trimmed, @"^\|?\s*:?-+:?\s*(\|(\s*:?-+:?\s*))+\|?$");
    }

    private async Task<string> TranslateMarkdownLineAsync(string line, TranslateOptions opt, CancellationToken ct)
    {
        // Check for table rows (| col1 | col2 |)
        if (line.TrimStart().StartsWith("|") && line.TrimEnd().EndsWith("|"))
        {
            return await TranslateTableRowAsync(line, opt, ct).ConfigureAwait(false);
        }

        // Check for Markdown prefixes: Headings (# ), Blockquotes (> ), Lists (- , * , 1. )
        var prefixMatch = Regex.Match(line, @"^(\s*(?:#{1,6}\s+|(?:>\s*)+|(?:[-*+]|\d+\.)\s+))");
        string prefix = string.Empty;
        string content = line;

        if (prefixMatch.Success)
        {
            prefix = prefixMatch.Groups[1].Value;
            content = line[prefix.Length..];
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return line;
        }

        var translatedContent = await TranslateInlineProtectedTextAsync(content, opt, ct).ConfigureAwait(false);
        return prefix + translatedContent;
    }

    private async Task<string> TranslateTableRowAsync(string line, TranslateOptions opt, CancellationToken ct)
    {
        var cells = line.Split('|');
        for (int i = 1; i < cells.Length - 1; i++)
        {
            var cell = cells[i];
            if (!string.IsNullOrWhiteSpace(cell))
            {
                string leading = cell[.. (cell.Length - cell.TrimStart().Length)];
                string trailing = cell[(cell.TrimEnd().Length)..];
                string core = cell.Trim();

                string translated = await TranslateInlineProtectedTextAsync(core, opt, ct).ConfigureAwait(false);
                cells[i] = leading + translated + trailing;
            }
        }
        return string.Join("|", cells);
    }

    private async Task<string> TranslateInlineProtectedTextAsync(string text, TranslateOptions opt, CancellationToken ct)
    {
        var placeholders = new List<string>();

        // 1. Protect inline code `...`
        text = Regex.Replace(text, @"`[^`\r\n]+`", m =>
        {
            int index = placeholders.Count;
            placeholders.Add(m.Value);
            return $"⟦{index}⟧";
        });

        // 2. Protect Images ![(alt)](url)
        text = Regex.Replace(text, @"!\[(.*?)\]\((.*?)\)", m =>
        {
            int index = placeholders.Count;
            placeholders.Add(m.Value);
            return $"⟦{index}⟧";
        });

        // 3. Protect HTML tags <...>
        text = Regex.Replace(text, @"<[^>\r\n]+>", m =>
        {
            int index = placeholders.Count;
            placeholders.Add(m.Value);
            return $"⟦{index}⟧";
        });

        // 4. Protect Link URLs: [text](url) -> [text]⟦index⟧ where placeholders[index] is "(url)"
        text = Regex.Replace(text, @"\[(.*?)\]\((.*?)\)", m =>
        {
            int index = placeholders.Count;
            placeholders.Add($"({m.Groups[2].Value})");
            return $"[{m.Groups[1].Value}]⟦{index}⟧";
        });

        // Translate the masked text
        var translated = await _translationService.TranslateTextAsync(text, opt.TargetLang, opt.SourceLang, null, ct).ConfigureAwait(false);

        // Restore placeholders
        for (int i = 0; i < placeholders.Count; i++)
        {
            var tokenPattern = @"⟦\s*" + i + @"\s*⟧";
            bool tokenFound = Regex.IsMatch(translated, tokenPattern);

            if (tokenFound)
            {
                if (placeholders[i].StartsWith("(") && placeholders[i].EndsWith(")"))
                {
                    // If the model wrapped the token in parentheses (e.g. (⟦0⟧)), avoid double parens
                    translated = Regex.Replace(translated, @"\(\s*⟦\s*" + i + @"\s*⟧\s*\)", placeholders[i]);
                    // If preceded by ']', join cleanly e.g. [text]⟦0⟧ or [text] ⟦0⟧
                    translated = Regex.Replace(translated, @"\]\s*⟦\s*" + i + @"\s*⟧", $"]{placeholders[i]}");
                }

                // Standard token replacement
                translated = Regex.Replace(translated, tokenPattern, placeholders[i]);
            }
            else
            {
                // Token was dropped by the model/translator. Put original back by appending to the end of the line.
                translated = string.IsNullOrWhiteSpace(translated)
                    ? placeholders[i]
                    : $"{translated.TrimEnd()} {placeholders[i]}";
            }
        }

        return translated;
    }

    public static (Encoding Encoding, string LineEnding) DetectEncodingAndLineEnding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        Encoding encoding = new UTF8Encoding(false);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            encoding = new UTF8Encoding(true);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            encoding = Encoding.Unicode;
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            encoding = Encoding.BigEndianUnicode;

        string sample = encoding.GetString(bytes);
        string lineEnding = sample.Contains("\r\n") ? "\r\n" : "\n";

        return (encoding, lineEnding);
    }
}
