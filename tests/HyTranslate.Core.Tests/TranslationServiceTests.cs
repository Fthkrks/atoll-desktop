using System.Threading.Tasks;
using HyTranslate.Core.Translation;
using Xunit;

namespace HyTranslate.Core.Tests;

public class TranslationServiceTests
{
    [Fact]
    public async Task SkipRules_ReturnTextUnchanged_WithoutCallingTranslator()
    {
        var fake = new FakeTranslator("ÇEVRİ:");
        var service = new TranslationService(fake);

        // Whitespace
        var wsResult = await service.TranslateTextAsync("   \r\n   ", "tr");
        Assert.Equal("   \r\n   ", wsResult);

        // Digits and punctuation only
        var numResult = await service.TranslateTextAsync("12345.67", "tr");
        Assert.Equal("12345.67", numResult);

        var punctResult = await service.TranslateTextAsync("... --- ... ???", "tr");
        Assert.Equal("... --- ... ???", punctResult);

        // URLs
        var urlResult = await service.TranslateTextAsync("https://example.com/api/test", "tr");
        Assert.Equal("https://example.com/api/test", urlResult);

        // Email addresses
        var emailResult = await service.TranslateTextAsync("test.user@example.com", "tr");
        Assert.Equal("test.user@example.com", emailResult);

        // Shorter than 2 letters
        var shortResult = await service.TranslateTextAsync("A", "tr");
        Assert.Equal("A", shortResult);

        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Cache_CallsTranslatorOnlyOnceForSameText()
    {
        var fake = new FakeTranslator("TR:");
        var service = new TranslationService(fake);

        var text = "Hello world";
        var res1 = await service.TranslateTextAsync(text, "tr");
        var res2 = await service.TranslateTextAsync(text, "tr");

        Assert.Equal("TR:Hello world", res1);
        Assert.Equal("TR:Hello world", res2);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task WhitespacePreservation_KeepsLeadingAndTrailingSpaces()
    {
        var fake = new FakeTranslator("TR:");
        var service = new TranslationService(fake);

        var text = "   Hello world   \n";
        var res = await service.TranslateTextAsync(text, "tr");

        Assert.Equal("   TR:Hello world   \n", res);
    }

    [Fact]
    public async Task SafetyGuard_InstructionEcho_PersistentEcho_RetriesOnceAndReturnsSource()
    {
        // Fake translator always echoes the instruction
        var fake = new FakeTranslator(_ => "Not edin ki, sadece çevrilmiş sonucu ek bilgi olmadan yazmalısınız:");
        var service = new TranslationService(fake);

        var result = await service.TranslateTextAsync("Comment", "tr");

        Assert.Equal("Comment", result);
        Assert.Equal(2, fake.CallCount); // 1 initial + 1 retry
    }

    [Fact]
    public async Task SafetyGuard_InstructionEcho_SucceedsOnRetry_ReturnsTranslation()
    {
        int calls = 0;
        var fake = new FakeTranslator(t =>
        {
            calls++;
            if (calls == 1)
            {
                return "Not edin ki, sadece çevrilmiş sonucu ek bilgi olmadan yazmalısınız:";
            }
            return "Yorum";
        });
        var service = new TranslationService(fake);

        var result = await service.TranslateTextAsync("Comment", "tr");

        Assert.Equal("Yorum", result);
        Assert.Equal(2, fake.CallCount);
    }

    [Fact]
    public async Task SafetyGuard_SourceContainsEchoPhrase_DoesNotTreatAsBadOutput()
    {
        var fake = new FakeTranslator("TR:");
        var service = new TranslationService(fake);

        string source = "Provide your answer without additional explanation.";
        var result = await service.TranslateTextAsync(source, "tr");

        Assert.Equal("TR:Provide your answer without additional explanation.", result);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public void PromptTemplate_UsesSingleLineFormat_WithoutSourceLanguage()
    {
        var promptEn = Languages.Languages.BuildPrompt("Comment", "tr", "en");
        Assert.Equal("Translate the following segment into Turkish, without additional explanation.\n\nComment", promptEn);

        var promptZh = Languages.Languages.BuildPrompt("Comment", "zh", "en");
        Assert.Equal("Translate the following segment into Chinese, without additional explanation.\n\nComment", promptZh);
    }

    [Fact]
    public async Task TranslateRawAsync_GoesThroughCache()
    {
        var fake = new FakeTranslator("TR:");
        var service = new TranslationService(fake);

        var res1 = await service.TranslateRawAsync("<g1>Hello</g1>", "tr");
        var res2 = await service.TranslateRawAsync("<g1>Hello</g1>", "tr");

        Assert.Equal("<g1>TR:Hello</g1>", res1);
        Assert.Equal("<g1>TR:Hello</g1>", res2);
        Assert.Equal(1, fake.CallCount);
    }
}
