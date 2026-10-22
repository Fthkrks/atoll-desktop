using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Ocr;
using HyTranslate.Core.Translation;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace HyTranslate.Core.Documents;

public class PdfDocumentTranslator : IDocumentTranslator
{
    private readonly TranslationService _translationService;
    private readonly Func<IPdfPageOcr?>? _ocrFactory;

    static PdfDocumentTranslator()
    {
        try
        {
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
        }
        catch
        {
            // Already initialized or platform restriction
        }
    }

    public string[] Extensions => new[] { ".pdf" };

    public PdfDocumentTranslator(TranslationService translationService, IPdfPageOcr? ocr = null)
        : this(translationService, ocr != null ? () => ocr : null)
    {
    }

    public PdfDocumentTranslator(TranslationService translationService, Func<IPdfPageOcr?>? ocrFactory)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _ocrFactory = ocrFactory;
    }

    private record ExtractedBlock(
        string Text,
        double Left,
        double Top,
        double Width,
        double Height,
        double FontSize,
        bool IsBold,
        int LineCount);

    public async Task TranslateAsync(
        string inputPath,
        string outputPath,
        TranslateOptions opt,
        IProgress<TranslationProgress>? progress = null,
        CancellationToken ct = default)
    {
        IPdfPageOcr? ocr = null;
        if (opt.UseOcr && _ocrFactory != null)
        {
            ocr = _ocrFactory();
        }

        try
        {
            bool canOcr = opt.UseOcr && ocr != null && ocr.IsAvailable;
            bool reportedNoOcrWarning = false;

            // 1. Read layout and text with PdfPig (+ OCR if needed)
            var pageBlocks = new List<(int PageNumber, List<ExtractedBlock> TextBlocks, List<OcrBlock> OcrBlocks, IOcrPixelSampler? Sampler)>();
            var warnings = new List<string>();

            using (var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath))
            {
                int totalPages = pigDoc.NumberOfPages;
                for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                {
                    var page = pigDoc.GetPage(pageNum);
                    var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
                    var images = page.GetImages().ToList();
                    double pageArea = page.Width * page.Height;
                    double imagesArea = images.Sum(img => (double)(img.Bounds.Width * img.Bounds.Height));
                    bool needsOcr = (words.Count == 0) || (images.Count > 0 && pageArea > 0 && (imagesArea / pageArea > 0.05));

                    if (opt.UseOcr && needsOcr && (ocr == null || !ocr.IsAvailable) && !reportedNoOcrWarning)
                    {
                        warnings.Add("Windows OCR dil paketi bulunamadı; görsellerdeki metin çevrilmedi. (Ayarlar > Saat ve dil > Dil > İngilizce > Optik karakter tanıma)");
                        reportedNoOcrWarning = true;
                    }

                    if (words.Count == 0 && !canOcr)
                    {
                        warnings.Add($"Sayfa {pageNum} yalnızca görsel içeriyor; OCR kapalı/yüklü değil, atlandı");
                        pageBlocks.Add((pageNum, new List<ExtractedBlock>(), new List<OcrBlock>(), null));
                        continue;
                    }

                    var extracted = new List<ExtractedBlock>();
                    if (words.Count > 0)
                    {
                        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words).ToList();
                        if (blocks.Count > 0)
                        {
                            foreach (var b in blocks)
                            {
                                if (string.IsNullOrWhiteSpace(b.Text)) continue;

                                double bLeft = b.BoundingBox.Left;
                                double bBottom = b.BoundingBox.Bottom;
                                double bRight = b.BoundingBox.Right;
                                double bTop = b.BoundingBox.Top;

                                // Make sure the block box is the union of all line boxes
                                if (b.TextLines != null && b.TextLines.Count > 0)
                                {
                                    foreach (var line in b.TextLines)
                                    {
                                        if (line.BoundingBox.Left < bLeft) bLeft = line.BoundingBox.Left;
                                        if (line.BoundingBox.Bottom < bBottom) bBottom = line.BoundingBox.Bottom;
                                        if (line.BoundingBox.Right > bRight) bRight = line.BoundingBox.Right;
                                        if (line.BoundingBox.Top > bTop) bTop = line.BoundingBox.Top;
                                    }
                                }

                                int lineCount = b.TextLines?.Count ?? 1;
                                var blockLetters = words
                                    .Where(w => w.BoundingBox.Centroid.X >= bLeft &&
                                                w.BoundingBox.Centroid.X <= bRight &&
                                                w.BoundingBox.Centroid.Y >= bBottom &&
                                                w.BoundingBox.Centroid.Y <= bTop)
                                    .SelectMany(w => w.Letters)
                                    .ToList();

                                if (blockLetters.Count == 0 && b.TextLines != null)
                                {
                                    blockLetters = b.TextLines
                                        .SelectMany(l => l.Words)
                                        .SelectMany(w => w.Letters)
                                        .ToList();
                                }

                                double medianFontSize = GetBlockMedianFontSize(blockLetters);
                                bool isBold = GetBlockIsBold(blockLetters);

                                extracted.Add(new ExtractedBlock(
                                    b.Text,
                                    bLeft,
                                    bTop,
                                    bRight - bLeft,
                                    bTop - bBottom,
                                    medianFontSize,
                                    isBold,
                                    lineCount));
                            }
                        }
                        else
                        {
                            // Fallback to words
                            var combinedText = string.Join(" ", words.Select(w => w.Text));
                            var minLeft = words.Min(w => w.BoundingBox.Left);
                            var maxTop = words.Max(w => w.BoundingBox.Top);
                            var maxRight = words.Max(w => w.BoundingBox.Right);
                            var minBottom = words.Min(w => w.BoundingBox.Bottom);

                            var allLetters = words.SelectMany(w => w.Letters).ToList();
                            double medianFontSize = GetBlockMedianFontSize(allLetters);
                            bool isBold = GetBlockIsBold(allLetters);

                            extracted.Add(new ExtractedBlock(
                                combinedText,
                                minLeft,
                                maxTop,
                                maxRight - minLeft,
                                maxTop - minBottom,
                                medianFontSize,
                                isBold,
                                1));
                        }
                    }

                    var ocrBlocks = new List<OcrBlock>();
                    IOcrPixelSampler? sampler = null;

                    if (needsOcr && canOcr)
                    {
                        progress?.Report(new TranslationProgress(pageNum, totalPages, $"Sayfa {pageNum}/{totalPages} – görsel metni okunuyor"));
                        try
                        {
                            var ocrResult = await ocr!.RecognizePageAsync(inputPath, pageNum - 1, ct).ConfigureAwait(false);
                            if (ocrResult != null && ocrResult.Lines.Count > 0)
                            {
                                sampler = ocrResult.Sampler;
                                var wordBoxes = words.Select(w => OcrBlockBuilder.FromPdfPigWord(w, page.Height)).ToList();
                                var filteredLines = OcrBlockBuilder.FilterLines(ocrResult.Lines, wordBoxes);
                                ocrBlocks = OcrBlockBuilder.Group(filteredLines).ToList();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Sayfa {pageNum}: OCR hatası – {ex.Message}");
                        }
                    }

                    pageBlocks.Add((pageNum, extracted, ocrBlocks, sampler));
                }
            }

            // 2. Copy file to output path
            File.Copy(inputPath, outputPath, true);

            // 3. Open output with PDFsharp and apply overlay
            using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
            {
                int totalPages = pdfDoc.PageCount;

                for (int pIdx = 0; pIdx < pageBlocks.Count && pIdx < totalPages; pIdx++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (pageNum, textBlocks, ocrBlocks, sampler) = pageBlocks[pIdx];

                    if (textBlocks.Count == 0 && ocrBlocks.Count == 0)
                    {
                        progress?.Report(new TranslationProgress(pIdx + 1, totalPages, $"Sayfa {pIdx + 1}/{totalPages}", warnings.LastOrDefault()));
                        continue;
                    }

                    var pdfPage = pdfDoc.Pages[pIdx];
                    double pageHeight = pdfPage.Height.Point;

                    using (var gfx = XGraphics.FromPdfPage(pdfPage, XGraphicsPdfPageOptions.Append))
                    {
                        // Text layer blocks
                        for (int bIdx = 0; bIdx < textBlocks.Count; bIdx++)
                        {
                            var block = textBlocks[bIdx];
                            if (TranslationService.ShouldSkip(block.Text)) continue;

                            var translatedText = await _translationService.TranslateTextAsync(
                                block.Text,
                                opt.TargetLang,
                                opt.SourceLang,
                                null,
                                ct).ConfigureAwait(false);

                            // Coordinates conversion:
                            // PdfPig origin is bottom-left, PDFsharp XGraphics is top-left
                            double x = Math.Max(0, block.Left);
                            double yTop = Math.Max(0, pageHeight - block.Top);
                            double w = Math.Max(10, block.Width);
                            double h = Math.Max(10, block.Height);

                            // Expand cover rectangle: 0.30 * fontSize downward, 0.15 * fontSize upward, and 0.10 * fontSize horizontally
                            double padH = 0.10 * block.FontSize;
                            double padUp = 0.15 * block.FontSize;
                            double padDown = 0.30 * block.FontSize;

                            double coverX = Math.Max(0, x - padH);
                            double coverY = Math.Max(0, yTop - padUp);
                            double coverW = w + (2 * padH);
                            double coverH = h + padUp + padDown;

                            var coverRect = new XRect(coverX, coverY, coverW, coverH);
                            gfx.DrawRectangle(XBrushes.White, coverRect);

                            // Wrap and draw text with font shrinking
                            DrawFittedText(gfx, translatedText, x, yTop, w, h, block.FontSize, block.IsBold, block.LineCount, XBrushes.Black);
                        }

                        // OCR blocks
                        for (int bIdx = 0; bIdx < ocrBlocks.Count; bIdx++)
                        {
                            var ocrBlock = ocrBlocks[bIdx];
                            if (TranslationService.ShouldSkip(ocrBlock.Text)) continue;

                            var translatedText = await _translationService.TranslateTextAsync(
                                ocrBlock.Text,
                                opt.TargetLang,
                                opt.SourceLang,
                                null,
                                ct).ConfigureAwait(false);

                            // Coordinates: OcrBlock is already top-left origin
                            double x = Math.Max(0, ocrBlock.Left);
                            double yTop = Math.Max(0, ocrBlock.Top);
                            double w = Math.Max(10, ocrBlock.Width);
                            double h = Math.Max(10, ocrBlock.Height);

                            // Cover padding for OCR blocks: 0.15 * fs on every side
                            double pad = 0.15 * ocrBlock.FontSize;
                            double coverX = Math.Max(0, x - pad);
                            double coverY = Math.Max(0, yTop - pad);
                            double coverW = w + (2 * pad);
                            double coverH = h + (2 * pad);

                            XBrush coverBrush = XBrushes.White;
                            XBrush textBrush = XBrushes.Black;
                            if (sampler != null)
                            {
                                var bg = sampler.BackgroundColor(ocrBlock.Left, ocrBlock.Top, ocrBlock.Width, ocrBlock.Height);
                                var fg = sampler.TextColor(ocrBlock.Left, ocrBlock.Top, ocrBlock.Width, ocrBlock.Height);
                                coverBrush = new XSolidBrush(XColor.FromArgb(bg.R, bg.G, bg.B));
                                textBrush = new XSolidBrush(XColor.FromArgb(fg.R, fg.G, fg.B));
                            }

                            var coverRect = new XRect(coverX, coverY, coverW, coverH);
                            gfx.DrawRectangle(coverBrush, coverRect);

                            DrawFittedText(gfx, translatedText, x, yTop, w, h, ocrBlock.FontSize, false, ocrBlock.LineCount, textBrush, isOcr: true, alignment: ocrBlock.Alignment);
                        }
                    }

                    progress?.Report(new TranslationProgress(pIdx + 1, totalPages, $"Sayfa {pIdx + 1}/{totalPages}"));
                }

                pdfDoc.Save(outputPath);
            }
        }
        finally
        {
            if (ocr is IDisposable disposableOcr)
            {
                disposableOcr.Dispose();
            }
        }
    }

    private static double GetBlockMedianFontSize(IReadOnlyList<UglyToad.PdfPig.Content.Letter> letters)
    {
        if (letters.Count == 0) return 10.0;

        var sizes = letters.Select(l =>
        {
            if (l.PointSize > 0)
            {
                return (double)l.PointSize;
            }
            if (l.FontSize > 0)
            {
                return l.FontSize;
            }
            return 10.0;
        }).OrderBy(s => s).ToList();

        if (sizes.Count == 0) return 10.0;

        int mid = sizes.Count / 2;
        if (sizes.Count % 2 == 1)
        {
            return sizes[mid];
        }
        return (sizes[mid - 1] + sizes[mid]) / 2.0;
    }

    private static bool GetBlockIsBold(IReadOnlyList<UglyToad.PdfPig.Content.Letter> letters)
    {
        if (letters.Count == 0) return false;
        int boldCount = letters.Count(l => !string.IsNullOrEmpty(l.FontName) && l.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase));
        return boldCount > (letters.Count / 2);
    }

    private static void DrawFittedText(
        XGraphics gfx,
        string text,
        double x,
        double y,
        double width,
        double height,
        double initialFontSize,
        bool isBold,
        int originalLineCount,
        XBrush? textBrush = null,
        bool isOcr = false,
        TextAlign alignment = TextAlign.Left)
    {
        double fontSize = Math.Max(5.0, initialFontSize);
        const double minFontSize = 5.0;
        int expectedLines = Math.Max(1, originalLineCount);

        var fontStyle = isBold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
        var brush = textBrush ?? XBrushes.Black;

        double padUp = 0.15 * initialFontSize;
        double padDown = 0.30 * initialFontSize;
        double maxAvailableHeight = height + padUp + padDown;

        while (fontSize >= minFontSize)
        {
            var font = new XFont("Segoe UI", fontSize, fontStyle);
            var lines = WrapText(gfx, text, font, width);
            double lineHeight = font.GetHeight();
            double totalTextHeight = lines.Count * lineHeight;

            bool fitsHorizontally = lines.All(line => gfx.MeasureString(line, font).Width <= width + (0.20 * fontSize));

            bool fitsVertically;
            if (isOcr)
            {
                fitsVertically = (lines.Count <= expectedLines) && (totalTextHeight <= height + (0.30 * fontSize));
            }
            else
            {
                fitsVertically = (lines.Count <= expectedLines) || (totalTextHeight <= maxAvailableHeight);
            }

            if ((fitsHorizontally && fitsVertically) || fontSize <= minFontSize)
            {
                double currentY = y;
                foreach (var line in lines)
                {
                    double lineWidth = gfx.MeasureString(line, font).Width;
                    double lineX = (alignment == TextAlign.Center)
                        ? x + ((width - lineWidth) / 2.0)
                        : x;

                    gfx.DrawString(line, font, brush, new XPoint(lineX, currentY + lineHeight * 0.8));
                    currentY += lineHeight;
                }
                break;
            }

            fontSize -= 0.5;
        }
    }

    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) return lines;

        string currentLine = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            string testLine = currentLine + " " + words[i];
            var size = gfx.MeasureString(testLine, font);
            if (size.Width <= maxWidth)
            {
                currentLine = testLine;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = words[i];
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines;
    }
}
