using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HyTranslate.Core.Languages;
using HyTranslate.Core.Translation;

namespace HyTranslate.Core.Documents;

public class DocxDocumentTranslator : IDocumentTranslator
{
    private readonly TranslationService _translationService;

    public string[] Extensions => new[] { ".docx" };

    public DocxDocumentTranslator(TranslationService translationService)
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
        // Never modify the input file; copy first
        File.Copy(inputPath, outputPath, true);

        using var doc = WordprocessingDocument.Open(outputPath, true);
        if (doc.MainDocumentPart == null) return;

        var allParagraphs = CollectAllParagraphs(doc);
        int total = allParagraphs.Count;
        int done = 0;

        bool isRtl = Languages.Languages.IsRtl(opt.TargetLang);

        foreach (var p in allParagraphs)
        {
            ct.ThrowIfCancellationRequested();
            await TranslateParagraphAsync(p, opt, isRtl, ct).ConfigureAwait(false);
            done++;
            progress?.Report(new TranslationProgress(done, total, $"Paragraf {done}/{total}"));
        }

        doc.Save();
    }

    private static List<Paragraph> CollectAllParagraphs(WordprocessingDocument doc)
    {
        var list = new List<Paragraph>();

        if (doc.MainDocumentPart?.Document?.Body != null)
        {
            list.AddRange(doc.MainDocumentPart.Document.Body.Descendants<Paragraph>());
        }

        if (doc.MainDocumentPart?.HeaderParts != null)
        {
            foreach (var hp in doc.MainDocumentPart.HeaderParts)
            {
                if (hp.Header != null)
                    list.AddRange(hp.Header.Descendants<Paragraph>());
            }
        }

        if (doc.MainDocumentPart?.FooterParts != null)
        {
            foreach (var fp in doc.MainDocumentPart.FooterParts)
            {
                if (fp.Footer != null)
                    list.AddRange(fp.Footer.Descendants<Paragraph>());
            }
        }

        if (doc.MainDocumentPart?.FootnotesPart?.Footnotes != null)
        {
            list.AddRange(doc.MainDocumentPart.FootnotesPart.Footnotes.Descendants<Paragraph>());
        }

        if (doc.MainDocumentPart?.EndnotesPart?.Endnotes != null)
        {
            list.AddRange(doc.MainDocumentPart.EndnotesPart.Endnotes.Descendants<Paragraph>());
        }

        if (doc.MainDocumentPart?.WordprocessingCommentsPart?.Comments != null)
        {
            list.AddRange(doc.MainDocumentPart.WordprocessingCommentsPart.Comments.Descendants<Paragraph>());
        }

        return list;
    }

    private async Task TranslateParagraphAsync(Paragraph p, TranslateOptions opt, bool isRtl, CancellationToken ct)
    {
        if (isRtl)
        {
            var pPr = p.ParagraphProperties ?? p.PrependChild(new ParagraphProperties());
            if (pPr.BiDi == null)
            {
                pPr.BiDi = new BiDi();
            }
        }

        await SegmentAndTranslateContainerAsync(p, opt, isRtl, ct).ConfigureAwait(false);
    }

    private async Task SegmentAndTranslateContainerAsync(
        OpenXmlElement container,
        TranslateOptions opt,
        bool isRtl,
        CancellationToken ct)
    {
        var currentSegment = new List<Run>();
        var children = container.ChildElements.ToList();

        foreach (var child in children)
        {
            if (child is ParagraphProperties) continue;

            if (child is Hyperlink hyperlink)
            {
                if (currentSegment.Count > 0)
                {
                    await ProcessSegmentAsync(currentSegment, opt, isRtl, ct).ConfigureAwait(false);
                    currentSegment.Clear();
                }

                await SegmentAndTranslateContainerAsync(hyperlink, opt, isRtl, ct).ConfigureAwait(false);
                continue;
            }

            if (child is SdtRun sdtRun && sdtRun.SdtContentRun != null)
            {
                if (currentSegment.Count > 0)
                {
                    await ProcessSegmentAsync(currentSegment, opt, isRtl, ct).ConfigureAwait(false);
                    currentSegment.Clear();
                }

                await SegmentAndTranslateContainerAsync(sdtRun.SdtContentRun, opt, isRtl, ct).ConfigureAwait(false);
                continue;
            }

            if (child is Run r && IsTextRun(r))
            {
                currentSegment.Add(r);
            }
            else
            {
                // Non-text element: bookmark, field, drawing, break, tab, etc.
                if (currentSegment.Count > 0)
                {
                    await ProcessSegmentAsync(currentSegment, opt, isRtl, ct).ConfigureAwait(false);
                    currentSegment.Clear();
                }
            }
        }

        if (currentSegment.Count > 0)
        {
            await ProcessSegmentAsync(currentSegment, opt, isRtl, ct).ConfigureAwait(false);
            currentSegment.Clear();
        }
    }

    private static bool IsTextRun(Run r)
    {
        if (!r.Elements<Text>().Any()) return false;
        if (r.Elements<FieldChar>().Any()) return false;
        if (r.Elements<FieldCode>().Any()) return false;
        if (r.Elements<Drawing>().Any()) return false;
        if (r.Elements<TabChar>().Any()) return false;
        if (r.Elements<Break>().Any()) return false;
        if (r.Elements<DeletedText>().Any()) return false;
        if (r.Elements<DeletedFieldCode>().Any()) return false;
        return true;
    }

    public static string GetDocxFormatKey(RunProperties? rPr)
    {
        if (rPr == null) return string.Empty;
        return Regex.Replace(rPr.OuterXml, @"\s+(?:[a-zA-Z0-9_]+:)?rsid[a-zA-Z0-9_]*=""[^""]*""", "").Trim();
    }

    private async Task ProcessSegmentAsync(
        List<Run> segmentRuns,
        TranslateOptions opt,
        bool isRtl,
        CancellationToken ct)
    {
        if (segmentRuns.Count == 0) return;

        var runTexts = segmentRuns
            .Select(r => string.Concat(r.Elements<Text>().Select(t => t.Text)))
            .ToList();

        if (runTexts.All(string.IsNullOrWhiteSpace)) return;

        var formatKeys = segmentRuns
            .Select(r => GetDocxFormatKey(r.RunProperties))
            .ToList();

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            _translationService,
            opt.TargetLang,
            opt.SourceLang,
            ct).ConfigureAwait(false);

        if (result.Pieces.Count == 0) return;

        var firstRun = segmentRuns[0];
        var parent = firstRun.Parent;
        if (parent == null) return;

        foreach (var piece in result.Pieces)
        {
            var templateRun = segmentRuns[piece.TemplateRunIndex];
            var cloned = (Run)templateRun.CloneNode(true);
            SetRunText(cloned, piece.Text);

            if (isRtl)
            {
                var rPr = cloned.RunProperties ?? cloned.PrependChild(new RunProperties());
                if (rPr.RightToLeftText == null)
                {
                    rPr.RightToLeftText = new RightToLeftText();
                }
            }

            parent.InsertBefore(cloned, firstRun);
        }

        foreach (var oldRun in segmentRuns)
        {
            oldRun.Remove();
        }
    }

    private static void SetRunText(Run run, string text)
    {
        var textElements = run.Elements<Text>().ToList();
        foreach (var t in textElements)
        {
            t.Remove();
        }
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
