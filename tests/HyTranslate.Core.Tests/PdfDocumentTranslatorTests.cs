using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Ocr;
using HyTranslate.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace HyTranslate.Core.Tests;

public class PdfDocumentTranslatorTests
{
    static PdfDocumentTranslatorTests()
    {
        try
        {
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
        }
        catch
        {
            // Already initialized
        }
    }

    [Fact]
    public async Task Pdf_TranslatesText_PreservesPageCount_ReadableWithPdfPig()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pdf_in_{Guid.NewGuid()}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pdf_out_{Guid.NewGuid()}.pdf");

        try
        {
            // Create a 1-page PDF using PDFsharp
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(600);
                page.Height = XUnit.FromPoint(800);

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    var font = new XFont("Segoe UI", 16, XFontStyleEx.Regular);
                    gfx.DrawString("Executive Summary Report", font, XBrushes.Black, new XPoint(50, 100));
                }
                doc.Save(tempInput);
            }

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new PdfDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            // Verify with PdfPig
            using (var pigDoc = UglyToad.PdfPig.PdfDocument.Open(tempOutput))
            {
                Assert.Equal(1, pigDoc.NumberOfPages);
                var page = pigDoc.GetPage(1);
                var text = page.Text;

                Assert.Contains("TR:Executive", text);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Pdf_Heading20pt_MaintainsFontSizeAndDrawsBold()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pdf_heading_in_{Guid.NewGuid()}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pdf_heading_out_{Guid.NewGuid()}.pdf");

        try
        {
            // Create a 1-page PDF with 20pt bold heading
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(600);
                page.Height = XUnit.FromPoint(800);

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    var font = new XFont("Segoe UI", 20, XFontStyleEx.Bold);
                    gfx.DrawString("Executive Summary", font, XBrushes.Black, new XPoint(50, 100));
                }
                doc.Save(tempInput);
            }

            var fake = new FakeTranslator(_ => "Yönetici Özeti");
            var service = new TranslationService(fake);
            var translator = new PdfDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            using (var pigDoc = UglyToad.PdfPig.PdfDocument.Open(tempOutput))
            {
                var page = pigDoc.GetPage(1);
                var letters = page.Letters.Where(l => !char.IsWhiteSpace(l.Value[0])).ToList();

                Assert.NotEmpty(letters);

                // The translated heading should not be shrunk down to ~13pt; it should remain ~20pt
                double medianSize = letters.Select(l => (double)l.PointSize).OrderBy(s => s).ElementAt(letters.Count / 2);
                Assert.InRange(medianSize, 18.0, 22.0);

                // Check that font is bold
                int boldCount = letters.Count(l => !string.IsNullOrEmpty(l.FontName) && l.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase));
                Assert.True(boldCount > letters.Count / 2, "Expected most letters in the heading to be bold");
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Pdf_WithImageAndOcr_TranslatesBothTitleAndOcrText()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pdf_ocr_in_{Guid.NewGuid()}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pdf_ocr_out_{Guid.NewGuid()}.pdf");

        try
        {
            // Create a 1-page PDF containing a title and an image
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(600);
                page.Height = XUnit.FromPoint(800);

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    var font = new XFont("Segoe UI", 16, XFontStyleEx.Regular);
                    gfx.DrawString("Introduction to Ciphers", font, XBrushes.Black, new XPoint(50, 80));

                    byte[] bmpBytes = CreateSampleBmp(200, 200);
                    using var ms = new MemoryStream(bmpBytes, 0, bmpBytes.Length, true, true);
                    using var xImage = XImage.FromStream(ms);
                    gfx.DrawImage(xImage, 50, 150, 200, 200);
                }
                doc.Save(tempInput);
            }

            var fakeOcr = new FakePdfPageOcr();
            var fakeTranslator = new FakeTranslator(t => "TR:" + t);
            var service = new TranslationService(fakeTranslator);
            var translator = new PdfDocumentTranslator(service, fakeOcr);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr", "en", false, true));

            Assert.Equal(1, fakeOcr.CallCount);

            using (var pigDoc = UglyToad.PdfPig.PdfDocument.Open(tempOutput))
            {
                var page = pigDoc.GetPage(1);
                var text = page.Text;

                Assert.Contains("TR:Introduction to Ciphers", text);
                Assert.Contains("TR:DES Designed at IBM", text);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Pdf_WithUseOcrFalse_FakeOcrIsNeverCalled()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pdf_no_ocr_in_{Guid.NewGuid()}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pdf_no_ocr_out_{Guid.NewGuid()}.pdf");

        try
        {
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(600);
                page.Height = XUnit.FromPoint(800);

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    var font = new XFont("Segoe UI", 16, XFontStyleEx.Regular);
                    gfx.DrawString("Introduction to Ciphers", font, XBrushes.Black, new XPoint(50, 80));

                    byte[] bmpBytes = CreateSampleBmp(200, 200);
                    using var ms = new MemoryStream(bmpBytes, 0, bmpBytes.Length, true, true);
                    using var xImage = XImage.FromStream(ms);
                    gfx.DrawImage(xImage, 50, 150, 200, 200);
                }
                doc.Save(tempInput);
            }

            var fakeOcr = new FakePdfPageOcr();
            var fakeTranslator = new FakeTranslator(t => "TR:" + t);
            var service = new TranslationService(fakeTranslator);
            var translator = new PdfDocumentTranslator(service, fakeOcr);

            // UseOcr = false
            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr", "en", false, false));

            Assert.Equal(0, fakeOcr.CallCount);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    private static byte[] CreateSampleBmp(int width, int height)
    {
        int rowStride = ((width * 3 + 3) / 4) * 4;
        int imageSize = rowStride * height;
        int fileSize = 54 + imageSize;

        byte[] bmp = new byte[fileSize];
        bmp[0] = 0x42;
        bmp[1] = 0x4D;
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        BitConverter.GetBytes(imageSize).CopyTo(bmp, 34);

        for (int i = 54; i < fileSize; i++)
        {
            bmp[i] = 200;
        }

        return bmp;
    }

    private class FakePdfPageOcr : IPdfPageOcr
    {
        public bool IsAvailable => true;
        public string? LanguageTag => "en";
        public int CallCount { get; private set; }

        public Task<OcrPageResult> RecognizePageAsync(string pdfPath, int pageIndex, CancellationToken ct)
        {
            CallCount++;
            var lines = new List<OcrTextLine>
            {
                new OcrTextLine("DES Designed at IBM", 60, 160, 150, 15)
            };
            return Task.FromResult(new OcrPageResult(lines, new FakePixelSampler()));
        }
    }

    private class DisposableFakePdfPageOcr : FakePdfPageOcr, IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private class FakePixelSampler : IOcrPixelSampler
    {
        public (byte R, byte G, byte B) BackgroundColor(double left, double top, double width, double height) => (255, 255, 255);
        public (byte R, byte G, byte B) TextColor(double left, double top, double width, double height) => (0, 0, 0);
    }

    [Fact]
    public void DependencyInjection_ResolvesPdfDocumentTranslatorWithoutAmbiguity()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITranslator>(new FakeTranslator());
        services.AddSingleton<TranslationService>();
        services.AddSingleton<Func<IPdfPageOcr?>>(sp => () => new FakePdfPageOcr());
        services.AddSingleton<IDocumentTranslator>(sp =>
            new PdfDocumentTranslator(sp.GetRequiredService<TranslationService>(), sp.GetRequiredService<Func<IPdfPageOcr?>>()));
        services.AddSingleton<DocumentTranslatorFactory>();

        using var sp = services.BuildServiceProvider();
        var translator = sp.GetServices<IDocumentTranslator>().OfType<PdfDocumentTranslator>().SingleOrDefault();
        Assert.NotNull(translator);

        var factory = sp.GetRequiredService<DocumentTranslatorFactory>();
        var resolved = factory.GetTranslator("doc.pdf");
        Assert.Same(translator, resolved);
    }

    [Fact]
    public async Task Pdf_TranslateAsync_DisposesDisposableOcrInstance()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pdf_disp_in_{Guid.NewGuid()}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pdf_disp_out_{Guid.NewGuid()}.pdf");

        try
        {
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(600);
                page.Height = XUnit.FromPoint(800);
                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    byte[] bmpBytes = CreateSampleBmp(200, 200);
                    using var ms = new MemoryStream(bmpBytes, 0, bmpBytes.Length, true, true);
                    using var xImage = XImage.FromStream(ms);
                    gfx.DrawImage(xImage, 50, 150, 200, 200);
                }
                doc.Save(tempInput);
            }

            var disposableOcr = new DisposableFakePdfPageOcr();
            var service = new TranslationService(new FakeTranslator("TR:"));
            var translator = new PdfDocumentTranslator(service, () => disposableOcr);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr", "en", false, true));

            Assert.True(disposableOcr.IsDisposed);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
