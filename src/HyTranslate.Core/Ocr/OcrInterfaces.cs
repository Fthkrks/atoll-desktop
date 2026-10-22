using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Ocr;

namespace HyTranslate.Core.Ocr;

public sealed record OcrTextLine(string Text, double Left, double Top, double Width, double Height); // PDF points, top-left origin (like XGraphics)
public sealed record OcrPageResult(IReadOnlyList<OcrTextLine> Lines, IOcrPixelSampler? Sampler);

public interface IOcrPixelSampler        // reads colours from the rendered page (for overlay colours)
{
    // rectangle in PDF points, top-left origin
    (byte R, byte G, byte B) BackgroundColor(double left, double top, double width, double height); // median of the pixels on a 3-px ring around the rectangle
    (byte R, byte G, byte B) TextColor(double left, double top, double width, double height);       // mean of the darkest 10% of pixels in the rectangle; if contrast with the background is < 60 (luminance difference), return black or white, whichever has the most contrast
}

public interface IPdfPageOcr
{
    bool IsAvailable { get; }                 // a usable OCR engine exists?
    string? LanguageTag { get; }              // language in use
    Task<OcrPageResult> RecognizePageAsync(string pdfPath, int pageIndex /*0-based*/, CancellationToken ct);
}

public static class OcrLanguages
{
    public static IReadOnlyList<(string Tag, string DisplayName)> Available()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(lang => (lang.LanguageTag, lang.DisplayName))
                .ToList();
        }
        catch
        {
            return Array.Empty<(string Tag, string DisplayName)>();
        }
    }
}
