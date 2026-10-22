using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HyTranslate.Core.Translation;
using Xunit;

namespace HyTranslate.Core.Tests;

public class RichParagraphTranslatorTests
{
    [Fact]
    public async Task FiveRunExample_OnlyFormattedTagged_ReorderedCorrectly()
    {
        // 5-run example from FIXES_2.md:
        // "Click ", "Save"(bold), " to store the ", "project settings"(italic), " permanently."
        var runTexts = new List<string>
        {
            "Click ",
            "Save",
            " to store the ",
            "project settings",
            " permanently."
        };
        var formatKeys = new List<string>
        {
            "normal",
            "bold",
            "normal",
            "italic",
            "normal"
        };

        string? capturedPrompt = null;
        var fake = new FakeTranslator(input =>
        {
            capturedPrompt = input;
            return "<g2>proje ayarlarını</g2> kalıcı olarak kaydetmek için <g1>Kaydet</g1>’e tıklayın.";
        });
        var service = new TranslationService(fake);

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            service,
            targetLang: "tr");

        // Base format ("normal") must be UNTAGGED; only non-base ("bold", "italic") get <g1>, <g2>
        Assert.Equal("Click <g1>Save</g1> to store the <g2>project settings</g2> permanently.", capturedPrompt);

        Assert.False(result.FellBack);
        Assert.Equal(4, result.Pieces.Count);

        // Piece 0: <g2> -> "project settings" (template index 3)
        Assert.Equal(3, result.Pieces[0].TemplateRunIndex);
        Assert.Equal("proje ayarlarını", result.Pieces[0].Text);

        // Piece 1: untagged -> base format (template index 2 or 4, longest normal run)
        Assert.True(result.Pieces[1].TemplateRunIndex == 2 || result.Pieces[1].TemplateRunIndex == 4);
        Assert.Equal(" kalıcı olarak kaydetmek için ", result.Pieces[1].Text);

        // Piece 2: <g1> -> "Save" (template index 1)
        Assert.Equal(1, result.Pieces[2].TemplateRunIndex);
        Assert.Equal("Kaydet", result.Pieces[2].Text);

        // Piece 3: untagged -> base format
        Assert.True(result.Pieces[3].TemplateRunIndex == 2 || result.Pieces[3].TemplateRunIndex == 4);
        Assert.Equal("’e tıklayın.", result.Pieces[3].Text);
    }

    [Fact]
    public async Task MergingAdjacentSameFormatRuns()
    {
        // Runs 0 & 1 share "normal", runs 2 & 3 share "bold"
        var runTexts = new List<string> { "Hello ", "beautiful ", "world", "!" };
        var formatKeys = new List<string> { "normal", "normal", "bold", "bold" };

        string? capturedPrompt = null;
        var fake = new FakeTranslator(input =>
        {
            capturedPrompt = input;
            return "TR:Hello beautiful <g1>TR:world!</g1>";
        });
        var service = new TranslationService(fake);

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            service,
            targetLang: "tr");

        // Runs 0 & 1 were merged into "Hello beautiful " (base format, untagged)
        // Runs 2 & 3 were merged into "world!" (tagged <g1>)
        Assert.Equal("Hello beautiful <g1>world!</g1>", capturedPrompt);

        Assert.False(result.FellBack);
        Assert.Equal(2, result.Pieces.Count);

        // Piece 0: base format (template index 1 = longest normal run "beautiful ")
        Assert.Equal(1, result.Pieces[0].TemplateRunIndex);
        Assert.Equal("TR:Hello beautiful ", result.Pieces[0].Text);

        // Piece 1: bold format (template index 2 = first run of merged piece)
        Assert.Equal(2, result.Pieces[1].TemplateRunIndex);
        Assert.Equal("TR:world!", result.Pieces[1].Text);
    }

    [Fact]
    public async Task SameFormatting_TranslatesAsPlainText_NoTags()
    {
        var fake = new FakeTranslator("TR:");
        var service = new TranslationService(fake);

        var runTexts = new List<string> { "Hello ", "world!" };
        var formatKeys = new List<string> { "normal", "normal" };

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            service,
            targetLang: "tr");

        Assert.False(result.FellBack);
        Assert.Single(result.Pieces);
        Assert.Equal(0, result.Pieces[0].TemplateRunIndex);
        Assert.Equal("TR:Hello world!", result.Pieces[0].Text);
    }

    [Fact]
    public async Task FallbackForBrokenTags()
    {
        // Translator returns broken tags
        var fake = new FakeTranslator(_ => "Bozuk çeviri tagsiz");
        var service = new TranslationService(fake);

        var runTexts = new List<string> { "Warning: ", "Do not turn off." };
        var formatKeys = new List<string> { "bold", "normal" };

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            service,
            targetLang: "tr");

        Assert.True(result.FellBack);
        Assert.Single(result.Pieces);
        // Base format template run was index 1 ("Do not turn off.", length 17 > length 9)
        Assert.Equal(1, result.Pieces[0].TemplateRunIndex);
        Assert.Equal("Bozuk çeviri tagsiz", result.Pieces[0].Text);
    }

    [Fact]
    public async Task Spacing_InsertsSingleSpace_WhenLettersAdjacent_InSpacedLanguages()
    {
        var runTexts = new List<string> { "Save", "button" };
        var formatKeys = new List<string> { "bold", "normal" };

        // Model returns output without space between tagged run and untagged run
        var fake = new FakeTranslator(_ => "<g1>Kaydet</g1>düğmesi");
        var service = new TranslationService(fake);

        var resultTr = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            service,
            targetLang: "tr");

        // Turkish uses spaces, so a single space should be inserted between 't' and 'd'
        var combinedTr = string.Concat(resultTr.Pieces[0].Text, resultTr.Pieces[1].Text);
        Assert.Equal("Kaydet düğmesi", combinedTr);

        // Chinese does NOT use spaces
        var fakeZh = new FakeTranslator(_ => "<g1>保存</g1>按钮");
        var serviceZh = new TranslationService(fakeZh);

        var resultZh = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            serviceZh,
            targetLang: "zh");

        var combinedZh = string.Concat(resultZh.Pieces[0].Text, resultZh.Pieces[1].Text);
        Assert.Equal("保存按钮", combinedZh);
    }

    [Fact]
    public async Task Cancellation_IsRethrown_NotSwallowedByFallback()
    {
        var fake = new FakeTranslator(_ => throw new OperationCanceledException());
        var service = new TranslationService(fake);

        var runTexts = new List<string> { "Run 1", "Run 2" };
        var formatKeys = new List<string> { "fmt1", "fmt2" };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RichParagraphTranslator.TranslateRunsAsync(
                runTexts,
                formatKeys,
                service,
                targetLang: "tr"));
    }

    [Fact]
    public async Task OllamaException_IsRethrown_NotSwallowedByFallback()
    {
        var fake = new FakeTranslator(_ => throw new Ollama.OllamaException("Connection failed"));
        var service = new TranslationService(fake);

        var runTexts = new List<string> { "Run 1", "Run 2" };
        var formatKeys = new List<string> { "fmt1", "fmt2" };

        await Assert.ThrowsAsync<Ollama.OllamaException>(() =>
            RichParagraphTranslator.TranslateRunsAsync(
                runTexts,
                formatKeys,
                service,
                targetLang: "tr"));
    }

    [Fact]
    public async Task GeneralException_TriggersFallback()
    {
        var fake = new FakeTranslator(text =>
        {
            if (text.Contains("<g1>"))
                throw new InvalidOperationException("Tagged processing failed");
            return "Fallback text";
        });
        var service = new TranslationService(fake);

        var runTexts = new List<string> { "Short", "Longer text here" };
        var formatKeys = new List<string> { "bold", "normal" };

        var result = await RichParagraphTranslator.TranslateRunsAsync(
            runTexts,
            formatKeys,
            service,
            targetLang: "tr");

        Assert.True(result.FellBack);
        Assert.Single(result.Pieces);
        Assert.Equal("Fallback text", result.Pieces[0].Text);
    }
}
