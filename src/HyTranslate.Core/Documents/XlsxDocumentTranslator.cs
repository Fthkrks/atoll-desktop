using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using HyTranslate.Core.Translation;

namespace HyTranslate.Core.Documents;

public class XlsxDocumentTranslator : IDocumentTranslator
{
    private readonly TranslationService _translationService;

    public string[] Extensions => new[] { ".xlsx" };

    public XlsxDocumentTranslator(TranslationService translationService)
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
        File.Copy(inputPath, outputPath, true);

        using var doc = SpreadsheetDocument.Open(outputPath, true);
        if (doc.WorkbookPart == null) return;

        // 1. SharedStringTable
        var sstPart = doc.WorkbookPart.SharedStringTablePart;
        if (sstPart?.SharedStringTable != null)
        {
            var stringItems = sstPart.SharedStringTable.Elements<SharedStringItem>().ToList();
            int total = stringItems.Count;
            int done = 0;

            foreach (var item in stringItems)
            {
                ct.ThrowIfCancellationRequested();
                await TranslateSharedStringItemAsync(item, opt, ct).ConfigureAwait(false);
                done++;
                progress?.Report(new TranslationProgress(done, total, $"Paylaşılan Metin {done}/{total}"));
            }
        }

        // 2. Inline strings in worksheets
        foreach (var wsPart in doc.WorkbookPart.WorksheetParts)
        {
            var inlineCells = wsPart.Worksheet.Descendants<Cell>()
                .Where(c => c.CellFormula == null &&
                            c.DataType?.Value == CellValues.InlineString &&
                            c.InlineString != null)
                .ToList();

            foreach (var cell in inlineCells)
            {
                ct.ThrowIfCancellationRequested();
                await TranslateInlineStringAsync(cell.InlineString!, opt, ct).ConfigureAwait(false);
            }
        }

        // 3. Optional sheet names
        if (opt.TranslateSheetNames && doc.WorkbookPart.Workbook.Sheets != null)
        {
            foreach (var sheet in doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>())
            {
                if (sheet.Name?.Value != null)
                {
                    var translated = await _translationService.TranslateTextAsync(sheet.Name.Value, opt.TargetLang, opt.SourceLang, null, ct).ConfigureAwait(false);
                    // Excel sheet name max 31 characters and no : \ / ? * [ ]
                    var cleanName = new string(translated.Where(c => !":\\/?*[]".Contains(c)).Take(31).ToArray());
                    if (!string.IsNullOrWhiteSpace(cleanName))
                    {
                        sheet.Name.Value = cleanName;
                    }
                }
            }
        }

        doc.Save();
    }

    private async Task TranslateSharedStringItemAsync(SharedStringItem item, TranslateOptions opt, CancellationToken ct)
    {
        // Simple text
        if (item.Text != null && !string.IsNullOrWhiteSpace(item.Text.Text))
        {
            var translated = await _translationService.TranslateTextAsync(
                item.Text.Text,
                opt.TargetLang,
                opt.SourceLang,
                null,
                ct).ConfigureAwait(false);

            item.Text.Text = translated;
            item.Text.Space = SpaceProcessingModeValues.Preserve;
            return;
        }

        // Rich text runs
        var runs = item.Elements<DocumentFormat.OpenXml.Spreadsheet.Run>().ToList();
        if (runs.Count > 0)
        {
            await TranslateSpreadsheetRunsAsync(item, runs, opt, ct).ConfigureAwait(false);
        }
    }

    private async Task TranslateInlineStringAsync(InlineString inline, TranslateOptions opt, CancellationToken ct)
    {
        if (inline.Text != null && !string.IsNullOrWhiteSpace(inline.Text.Text))
        {
            var translated = await _translationService.TranslateTextAsync(
                inline.Text.Text,
                opt.TargetLang,
                opt.SourceLang,
                null,
                ct).ConfigureAwait(false);

            inline.Text.Text = translated;
            inline.Text.Space = SpaceProcessingModeValues.Preserve;
            return;
        }

        var runs = inline.Elements<DocumentFormat.OpenXml.Spreadsheet.Run>().ToList();
        if (runs.Count > 0)
        {
            await TranslateSpreadsheetRunsAsync(inline, runs, opt, ct).ConfigureAwait(false);
        }
    }

    public static string GetXlsxFormatKey(DocumentFormat.OpenXml.Spreadsheet.RunProperties? rPr)
    {
        return rPr?.OuterXml ?? string.Empty;
    }

    private async Task TranslateSpreadsheetRunsAsync(
        OpenXmlElement parent,
        List<DocumentFormat.OpenXml.Spreadsheet.Run> runs,
        TranslateOptions opt,
        CancellationToken ct)
    {
        var runTexts = runs.Select(r => r.Text?.Text ?? string.Empty).ToList();
        if (runTexts.All(string.IsNullOrWhiteSpace)) return;

        var formatKeys = runs.Select(r => GetXlsxFormatKey(r.RunProperties)).ToList();

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            _translationService,
            opt.TargetLang,
            opt.SourceLang,
            ct).ConfigureAwait(false);

        if (result.Pieces.Count == 0) return;

        var firstRun = runs[0];

        foreach (var piece in result.Pieces)
        {
            var templateRun = runs[piece.TemplateRunIndex];
            var cloned = (DocumentFormat.OpenXml.Spreadsheet.Run)templateRun.CloneNode(true);
            SetXlsxRunText(cloned, piece.Text);
            parent.InsertBefore(cloned, firstRun);
        }

        foreach (var oldRun in runs)
        {
            oldRun.Remove();
        }
    }

    private static void SetXlsxRunText(DocumentFormat.OpenXml.Spreadsheet.Run run, string text)
    {
        if (run.Text == null)
        {
            run.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }
        else
        {
            run.Text.Text = text;
            run.Text.Space = SpaceProcessingModeValues.Preserve;
        }
    }
}
