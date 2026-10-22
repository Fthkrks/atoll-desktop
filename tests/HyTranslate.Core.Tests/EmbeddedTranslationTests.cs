using HyTranslate.Core.Translation;
using HyTranslate.Core.Settings;
using Xunit;

namespace HyTranslate.Core.Tests;

public sealed class EmbeddedTranslationTests
{
    [Fact]
    public void ResolvesExistingUserPathBeforeBundleAndFallsBackWhenMissing()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "model"));
        try
        {
            var user = Path.Combine(folder, "custom.gguf");
            var bundled = Path.Combine(folder, "model", ModelPathResolver.BundledFileName);
            Assert.Null(ModelPathResolver.Resolve(null, folder));
            File.WriteAllText(bundled, "bundle");
            Assert.Equal(bundled, ModelPathResolver.Resolve("", folder));
            Assert.Equal(bundled, ModelPathResolver.Resolve(user, folder));
            File.WriteAllText(user, "custom");
            Assert.Equal(user, ModelPathResolver.Resolve(user, folder));
            File.Delete(bundled);
            Assert.Equal(user, ModelPathResolver.Resolve(user, folder));
            Assert.Null(ModelPathResolver.Resolve(null, folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("tr", "en", "Turkish")]
    [InlineData("tr", "zh", "Turkish")]
    [InlineData("zh", "auto", "Chinese")]
    public void PromptIsExactlyOneInstructionAndPreservesInlineTags(string target, string source, string name)
    {
        const string text = "Click <g1>Save</g1> to store <g2>settings</g2>.";
        var inner = HyTranslate.Core.Languages.Languages.BuildPrompt(text, target, source);
        Assert.Equal($"Translate the following segment into {name}, without additional explanation.\n\n{text}", inner);
        Assert.Equal($"<｜hy_begin▁of▁sentence｜><｜hy_User｜>{inner}<｜hy_Assistant｜>", LlamaSharpTranslator.FallbackChatPrompt(inner));
    }

    [Fact]
    public void DefaultBackendIsBuiltIn() => Assert.Equal("Built-in", new AppSettings().Backend);
}
