using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyTranslate.Core.Ocr;

public sealed class WindowsPdfOcr : IPdfPageOcr, IDisposable
{
    private readonly OcrEngine? _engine;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedPdfPath;
    private Windows.Data.Pdf.PdfDocument? _cachedDoc;

    public bool IsAvailable { get; }
    public string? LanguageTag { get; }

    public WindowsPdfOcr(string? preferredLanguageTag = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(preferredLanguageTag))
            {
                var preferredLang = new Language(preferredLanguageTag);
                if (OcrEngine.IsLanguageSupported(preferredLang))
                {
                    _engine = OcrEngine.TryCreateFromLanguage(preferredLang);
                }
            }

            if (_engine == null)
            {
                _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            if (_engine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
            {
                _engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
            }
        }
        catch
        {
            _engine = null;
        }

        IsAvailable = _engine != null;
        LanguageTag = _engine?.RecognizerLanguage?.LanguageTag;
    }

    public async Task<OcrPageResult> RecognizePageAsync(string pdfPath, int pageIndex, CancellationToken ct)
    {
        if (!IsAvailable || _engine == null)
        {
            return new OcrPageResult(Array.Empty<OcrTextLine>(), null);
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fullPath = Path.GetFullPath(pdfPath);
            if (_cachedDoc == null || !string.Equals(_cachedPdfPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(fullPath).AsTask(ct).ConfigureAwait(false);
                _cachedDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(storageFile).AsTask(ct).ConfigureAwait(false);
                _cachedPdfPath = fullPath;
            }

            if (pageIndex < 0 || pageIndex >= _cachedDoc.PageCount)
            {
                return new OcrPageResult(Array.Empty<OcrTextLine>(), null);
            }

            using var page = _cachedDoc.GetPage((uint)pageIndex);

            double targetW = page.Size.Width * 200.0 / 96.0;
            double targetH = page.Size.Height * 200.0 / 96.0;
            uint maxDim = OcrEngine.MaxImageDimension;
            if (targetW > maxDim || targetH > maxDim)
            {
                double scale = Math.Min((double)maxDim / targetW, (double)maxDim / targetH);
                targetW *= scale;
                targetH *= scale;
            }

            uint destWidth = (uint)Math.Max(1, Math.Round(targetW));
            var renderOptions = new PdfPageRenderOptions { DestinationWidth = destWidth };

            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, renderOptions).AsTask(ct).ConfigureAwait(false);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied).AsTask(ct).ConfigureAwait(false);

            var ocrResult = await _engine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);

            double pageWidthPt = page.Size.Width * 72.0 / 96.0;
            double bitmapWidthPx = softwareBitmap.PixelWidth;
            double scalePxToPt = bitmapWidthPx > 0 ? (pageWidthPt / bitmapWidthPx) : 1.0;

            var lines = new List<OcrTextLine>();
            foreach (var line in ocrResult.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text)) continue;

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;

                if (line.Words.Count > 0)
                {
                    foreach (var w in line.Words)
                    {
                        var r = w.BoundingRect;
                        if (r.Left < minX) minX = r.Left;
                        if (r.Top < minY) minY = r.Top;
                        if (r.Right > maxX) maxX = r.Right;
                        if (r.Bottom > maxY) maxY = r.Bottom;
                    }
                }

                if (maxX > minX && maxY > minY)
                {
                    lines.Add(new OcrTextLine(
                        line.Text,
                        minX * scalePxToPt,
                        minY * scalePxToPt,
                        (maxX - minX) * scalePxToPt,
                        (maxY - minY) * scalePxToPt));
                }
            }

            byte[] bgraBytes = new byte[softwareBitmap.PixelWidth * softwareBitmap.PixelHeight * 4];
            var buffer = new Windows.Storage.Streams.Buffer((uint)bgraBytes.Length);
            softwareBitmap.CopyToBuffer(buffer);
            var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bgraBytes);

            var sampler = new OcrPixelSampler(bgraBytes, softwareBitmap.PixelWidth, softwareBitmap.PixelHeight, pageWidthPt);

            return new OcrPageResult(lines, sampler);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _cachedDoc = null;
        _cachedPdfPath = null;
        _lock.Dispose();
    }
}
