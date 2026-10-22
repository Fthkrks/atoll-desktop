using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using HyTranslate.Core.Translation;
using DRun = DocumentFormat.OpenXml.Drawing.Run;
using DParagraph = DocumentFormat.OpenXml.Drawing.Paragraph;
using DText = DocumentFormat.OpenXml.Drawing.Text;
using DField = DocumentFormat.OpenXml.Drawing.Field;
using DBreak = DocumentFormat.OpenXml.Drawing.Break;

namespace HyTranslate.Core.Documents;

public class PptxDocumentTranslator : IDocumentTranslator
{
    private readonly TranslationService _translationService;

    public string[] Extensions => new[] { ".pptx" };

    public PptxDocumentTranslator(TranslationService translationService)
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

        using var doc = PresentationDocument.Open(outputPath, true);
        if (doc.PresentationPart == null) return;

        var allParagraphs = CollectAllParagraphs(doc);
        int total = allParagraphs.Count;
        int done = 0;

        foreach (var p in allParagraphs)
        {
            ct.ThrowIfCancellationRequested();
            await TranslateParagraphAsync(p, opt, ct).ConfigureAwait(false);
            done++;
            progress?.Report(new TranslationProgress(done, total, $"Paragraf {done}/{total}"));
        }

        doc.Save();
    }

    private static List<DParagraph> CollectAllParagraphs(PresentationDocument doc)
    {
        var list = new List<DParagraph>();
        var presPart = doc.PresentationPart;
        if (presPart == null) return list;

        // Slide parts
        foreach (var slidePart in presPart.SlideParts)
        {
            if (slidePart.Slide != null)
            {
                list.AddRange(slidePart.Slide.Descendants<DParagraph>());
            }

            // Notes slides
            if (slidePart.NotesSlidePart?.NotesSlide != null)
            {
                list.AddRange(slidePart.NotesSlidePart.NotesSlide.Descendants<DParagraph>());
            }
        }

        // Slide masters and layouts
        if (presPart.SlideMasterParts != null)
        {
            foreach (var masterPart in presPart.SlideMasterParts)
            {
                if (masterPart.SlideMaster != null)
                {
                    list.AddRange(masterPart.SlideMaster.Descendants<DParagraph>());
                }

                foreach (var layoutPart in masterPart.SlideLayoutParts)
                {
                    if (layoutPart.SlideLayout != null)
                    {
                        list.AddRange(layoutPart.SlideLayout.Descendants<DParagraph>());
                    }
                }
            }
        }

        return list;
    }

    private async Task TranslateParagraphAsync(DParagraph p, TranslateOptions opt, CancellationToken ct)
    {
        var currentSegment = new List<DRun>();
        var children = p.ChildElements.ToList();

        foreach (var child in children)
        {
            if (child is DRun r && IsTextRun(r))
            {
                currentSegment.Add(r);
            }
            else
            {
                // Boundary element (a:fld, a:br, etc.)
                if (currentSegment.Count > 0)
                {
                    await ProcessSegmentAsync(currentSegment, p, opt, ct).ConfigureAwait(false);
                    currentSegment.Clear();
                }
            }
        }

        if (currentSegment.Count > 0)
        {
            await ProcessSegmentAsync(currentSegment, p, opt, ct).ConfigureAwait(false);
            currentSegment.Clear();
        }
    }

    private static bool IsTextRun(DRun r)
    {
        if (r.Ancestors<DField>().Any()) return false;
        if (!r.Elements<DText>().Any()) return false;
        if (r.Elements<DBreak>().Any()) return false;
        return true;
    }

    public static string GetPptxFormatKey(DocumentFormat.OpenXml.Drawing.RunProperties? rPr)
    {
        if (rPr == null) return string.Empty;
        return Regex.Replace(rPr.OuterXml, @"\s+(?:[a-zA-Z0-9_]+:)?(lang|dirty|err)=""[^""]*""", "").Trim();
    }

    private async Task ProcessSegmentAsync(
        List<DRun> segmentRuns,
        DParagraph parent,
        TranslateOptions opt,
        CancellationToken ct)
    {
        if (segmentRuns.Count == 0) return;

        var runTexts = segmentRuns
            .Select(r => string.Concat(r.Elements<DText>().Select(t => t.Text)))
            .ToList();

        if (runTexts.All(string.IsNullOrWhiteSpace)) return;

        var formatKeys = segmentRuns
            .Select(r => GetPptxFormatKey(r.RunProperties))
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

        foreach (var piece in result.Pieces)
        {
            var templateRun = segmentRuns[piece.TemplateRunIndex];
            var cloned = (DRun)templateRun.CloneNode(true);
            SetRunText(cloned, piece.Text);
            parent.InsertBefore(cloned, firstRun);
        }

        foreach (var oldRun in segmentRuns)
        {
            oldRun.Remove();
        }
    }

    private static void SetRunText(DRun run, string text)
    {
        var textElements = run.Elements<DText>().ToList();
        foreach (var t in textElements)
        {
            t.Remove();
        }
        run.AppendChild(new DText(text));
    }
}
