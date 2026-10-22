using System;
using System.IO;
using System.Threading.Tasks;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Translation;
using Xunit;

namespace HyTranslate.Core.Tests;

public class TxtDocumentTranslatorTests
{
    [Fact]
    public async Task Txt_PreservesCRLFAndBlankLines()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"txt_in_{Guid.NewGuid()}.txt");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"txt_out_{Guid.NewGuid()}.txt");

        try
        {
            var originalContent = "First paragraph line 1\r\nFirst paragraph line 2\r\n\r\n\r\nSecond paragraph line 1\r\n";
            await File.WriteAllTextAsync(tempInput, originalContent);

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new TxtDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var translatedContent = await File.ReadAllTextAsync(tempOutput);

            // Must contain CRLF
            Assert.Contains("\r\n", translatedContent);
            // Must contain consecutive blank lines
            Assert.Contains("\r\n\r\n\r\n", translatedContent);

            // Translations must be present
            Assert.Contains("TR:First paragraph", translatedContent);
            Assert.Contains("TR:Second paragraph", translatedContent);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Txt_BOM_PreservesSourceEncoding_NoBomWhenSourceHasNoBom()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"txt_in_{Guid.NewGuid()}.txt");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"txt_out_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(tempInput, "Plain text line without BOM", new System.Text.UTF8Encoding(false));

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new TxtDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var bytes = await File.ReadAllBytesAsync(tempOutput);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.False(hasBom, "Output TXT should not have a BOM if the source had none.");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Txt_BOM_PreservesSourceEncoding_HasBomWhenSourceHasBom()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"txt_in_{Guid.NewGuid()}.txt");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"txt_out_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(tempInput, "Plain text line with BOM", new System.Text.UTF8Encoding(true));

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new TxtDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var bytes = await File.ReadAllBytesAsync(tempOutput);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.True(hasBom, "Output TXT should have a BOM if the source had one.");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
