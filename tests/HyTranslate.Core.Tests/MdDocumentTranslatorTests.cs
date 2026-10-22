using System;
using System.IO;
using System.Threading.Tasks;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Translation;
using Xunit;

namespace HyTranslate.Core.Tests;

public class MdDocumentTranslatorTests
{
    [Fact]
    public async Task Md_PreservesCodeBlocksLinkUrlsAndHeadingPrefixes()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"md_in_{Guid.NewGuid()}.md");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"md_out_{Guid.NewGuid()}.md");

        try
        {
            var mdContent = @"---
title: Test Document
layout: default
---

# Main Heading Title

This is a paragraph with [Documentation link](https://example.com/docs) and `var x = 42;` inline code.

```csharp
public void DoNotTranslate()
{
    Console.WriteLine(""Code intact"");
}
```

## Secondary Heading

- List item one
- List item two

| Header 1 | Header 2 |
| --- | --- |
| Data 1 | Data 2 |
";

            await File.WriteAllTextAsync(tempInput, mdContent);

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new MdDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var output = await File.ReadAllTextAsync(tempOutput);

            // Front matter preserved
            Assert.Contains("title: Test Document", output);

            // Headings preserved
            Assert.Contains("# TR:Main Heading Title", output);
            Assert.Contains("## TR:Secondary Heading", output);

            // Link URL preserved
            Assert.Contains("(https://example.com/docs)", output);
            Assert.Contains("TR:This is a paragraph", output);

            // Inline code preserved
            Assert.Contains("`var x = 42;`", output);

            // Fenced code block completely preserved
            Assert.Contains("public void DoNotTranslate()", output);
            Assert.Contains("Console.WriteLine(\"Code intact\");", output);

            // Table separator preserved
            Assert.Contains("| --- | --- |", output);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Md_WhenTranslatorDropsUrlToken_UrlIsRecoveredAtEndOfLine()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"md_in_{Guid.NewGuid()}.md");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"md_out_{Guid.NewGuid()}.md");

        try
        {
            var content = "Run the following command to install the [package](https://example.com/pkg).";
            await File.WriteAllTextAsync(tempInput, content, new System.Text.UTF8Encoding(false));

            // FakeTranslator simulates dropping the URL placeholder token (⟦...⟧)
            var fake = new FakeTranslator(text =>
            {
                var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"\s*⟦\d+⟧", "");
                return "TR:" + stripped;
            });
            var service = new TranslationService(fake);
            var translator = new MdDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var output = await File.ReadAllTextAsync(tempOutput);

            // URL must NOT be lost: it must be recovered and appended to the end of the line
            Assert.Contains("(https://example.com/pkg)", output);
            Assert.EndsWith("(https://example.com/pkg)", output.TrimEnd());
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Md_NormalTranslator_PreservesLinkUrlAndImage()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"md_in_{Guid.NewGuid()}.md");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"md_out_{Guid.NewGuid()}.md");

        try
        {
            var content = "Install [package](https://example.com/pkg) and see ![logo](https://example.com/logo.png).";
            await File.WriteAllTextAsync(tempInput, content, new System.Text.UTF8Encoding(false));

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new MdDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var output = await File.ReadAllTextAsync(tempOutput);

            Assert.Contains("[package](https://example.com/pkg)", output);
            Assert.Contains("![logo](https://example.com/logo.png)", output);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Md_BOM_PreservesSourceEncoding_NoBomWhenSourceHasNoBom()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"md_in_{Guid.NewGuid()}.md");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"md_out_{Guid.NewGuid()}.md");

        try
        {
            // Write without BOM
            await File.WriteAllTextAsync(tempInput, "# Heading Without BOM\nText line", new System.Text.UTF8Encoding(false));

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new MdDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var bytes = await File.ReadAllBytesAsync(tempOutput);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.False(hasBom, "Output should not have a BOM if the source had none.");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Md_BOM_PreservesSourceEncoding_HasBomWhenSourceHasBom()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"md_in_{Guid.NewGuid()}.md");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"md_out_{Guid.NewGuid()}.md");

        try
        {
            // Write with BOM
            await File.WriteAllTextAsync(tempInput, "# Heading With BOM\nText line", new System.Text.UTF8Encoding(true));

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new MdDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            var bytes = await File.ReadAllBytesAsync(tempOutput);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.True(hasBom, "Output should have a BOM if the source had one.");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
