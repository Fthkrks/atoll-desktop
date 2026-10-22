using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Translation;

namespace HyTranslate.Core.Documents;

public class TxtDocumentTranslator : IDocumentTranslator
{
    private readonly TranslationService _translationService;

    public string[] Extensions => new[] { ".txt" };

    public TxtDocumentTranslator(TranslationService translationService)
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

        // Normalize split to preserve structure
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var outputLines = new List<string>();

        int total = lines.Length;
        int done = 0;

        // Group into paragraphs (contiguous non-empty lines)
        int i = 0;
        while (i < lines.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                outputLines.Add(lines[i]);
                i++;
                done++;
                progress?.Report(new TranslationProgress(done, total, $"Satır {done}/{total}"));
                continue;
            }

            int pStart = i;
            var pLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                pLines.Add(lines[i]);
                i++;
            }

            string paragraphText = string.Join(lineEnding, pLines);
            string translated = await _translationService.TranslateTextAsync(
                paragraphText,
                opt.TargetLang,
                opt.SourceLang,
                null,
                ct).ConfigureAwait(false);

            outputLines.Add(translated);
            done += (i - pStart);
            progress?.Report(new TranslationProgress(done, total, $"Satır {done}/{total}"));
        }

        var resultText = string.Join(lineEnding, outputLines);
        await File.WriteAllTextAsync(outputPath, resultText, encoding, ct).ConfigureAwait(false);
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

        // Detect line ending
        string sample = encoding.GetString(bytes);
        string lineEnding = sample.Contains("\r\n") ? "\r\n" : "\n";

        return (encoding, lineEnding);
    }
}
