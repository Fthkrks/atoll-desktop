using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyTranslate.Core.Languages;

public static class Languages
{
    public const string AutoCode = "auto";

    public static readonly LanguageItem Auto = new(AutoCode, "Auto", "自动", "Dili algıla");

    public static readonly IReadOnlyList<LanguageItem> SupportedLanguages = new List<LanguageItem>
    {
        new("zh", "Chinese", "中文", "Çince"),
        new("en", "English", "英语", "İngilizce"),
        new("fr", "French", "法语", "Fransızca"),
        new("pt", "Portuguese", "葡萄牙语", "Portekizce"),
        new("es", "Spanish", "西班牙语", "İspanyolca"),
        new("ja", "Japanese", "日语", "Japonca"),
        new("tr", "Turkish", "土耳其语", "Türkçe"),
        new("ru", "Russian", "俄语", "Rusça"),
        new("ar", "Arabic", "阿拉伯语", "Arapça"),
        new("ko", "Korean", "韩语", "Korece"),
        new("th", "Thai", "泰语", "Tayca"),
        new("it", "Italian", "意大利语", "İtalyanca"),
        new("de", "German", "德语", "Almanca"),
        new("vi", "Vietnamese", "越南语", "Vietnamca"),
        new("ms", "Malay", "马来语", "Malayca"),
        new("id", "Indonesian", "印尼语", "Endonezce"),
        new("fil", "Filipino", "菲律宾语", "Filipince"),
        new("hi", "Hindi", "印地语", "Hintçe"),
        new("zh-Hant", "Traditional Chinese", "繁体中文", "Geleneksel Çince"),
        new("pl", "Polish", "波兰语", "Lehçe"),
        new("cs", "Czech", "捷克语", "Çekçe"),
        new("nl", "Dutch", "荷兰语", "Felemenkçe"),
        new("km", "Khmer", "高棉语", "Kmerce"),
        new("my", "Burmese", "缅甸语", "Burmaca"),
        new("fa", "Persian", "波斯语", "Farsça"),
        new("gu", "Gujarati", "古吉拉特语", "Güceratça"),
        new("ur", "Urdu", "乌尔都语", "Urduca"),
        new("te", "Telugu", "泰卢固语", "Teluguca"),
        new("mr", "Marathi", "马拉地语", "Marathice"),
        new("he", "Hebrew", "希伯来语", "İbranice"),
        new("bn", "Bengali", "孟加拉语", "Bengalce"),
        new("ta", "Tamil", "泰米尔语", "Tamilce"),
        new("uk", "Ukrainian", "乌克兰语", "Ukraynaca"),
        new("bo", "Tibetan", "藏语", "Tibetçe"),
        new("kk", "Kazakh", "哈萨克语", "Kazakça"),
        new("mn", "Mongolian", "蒙古语", "Moğolca"),
        new("ug", "Uyghur", "维吾尔语", "Uygurca"),
        new("yue", "Cantonese", "粤语", "Kantonca")
    };

    private static readonly Dictionary<string, LanguageItem> LanguageByCode =
        SupportedLanguages.ToDictionary(l => l.Code, l => l, StringComparer.OrdinalIgnoreCase);

    public static LanguageItem? FindByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        if (string.Equals(code, AutoCode, StringComparison.OrdinalIgnoreCase)) return Auto;
        return LanguageByCode.TryGetValue(code, out var item) ? item : null;
    }

    public static bool IsChinese(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        return string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(code, "zh-Hant", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(code, "yue", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRtl(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        return string.Equals(code, "ar", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(code, "he", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(code, "fa", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(code, "ur", StringComparison.OrdinalIgnoreCase);
    }

    public static readonly string[] EchoPhrases =
    [
        "without additional explanation",
        "additional explanation",
        "ek açıklama",
        "ek bilgi olmadan",
        "çevrilmiş sonucu",
        "sadece çevir",
        "translated result",
        "不要额外解释"
    ];

    public static bool ContainsInstructionEcho(string? output, string? originalText)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;

        foreach (var phrase in EchoPhrases)
        {
            if (output.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                if (originalText == null || !originalText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string BuildPrompt(string text, string targetLangCode, string? sourceLangCode = null)
    {
        var targetItem = FindByCode(targetLangCode) ?? new LanguageItem(targetLangCode, targetLangCode, targetLangCode, targetLangCode);
        var sourceItem = (!string.IsNullOrWhiteSpace(sourceLangCode) && !string.Equals(sourceLangCode, AutoCode, StringComparison.OrdinalIgnoreCase))
            ? FindByCode(sourceLangCode)
            : null;

        bool useChineseTemplate = IsChinese(targetLangCode) || (sourceItem != null && IsChinese(sourceItem.Code)) || IsChinese(sourceLangCode);

        if (useChineseTemplate)
        {
            return $"把下面的文本翻译成{targetItem.ChineseName}，不要额外解释。\n\n{text}";
        }
        else
        {
            return $"Translate the following segment into {targetItem.EnglishName}, without additional explanation.\n\n{text}";
        }
    }

    public static string PostProcessTranslation(string rawText, string originalText)
    {
        if (string.IsNullOrEmpty(rawText)) return string.Empty;

        var text = rawText.Trim();

        // Strip markdown code fences if output is wrapped in them but original wasn't
        if (text.StartsWith("```") && text.EndsWith("```") && text.Length >= 6)
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline != -1 && firstNewline < text.Length - 3)
            {
                text = text[(firstNewline + 1)..^3].Trim();
            }
            else if (text.Length > 6)
            {
                text = text[3..^3].Trim();
            }
        }

        // Strip wrapping quotes if original wasn't wrapped in quotes
        bool originalHasQuotes = !string.IsNullOrEmpty(originalText) &&
                                 ((originalText.StartsWith('"') && originalText.EndsWith('"')) ||
                                  (originalText.StartsWith('\'') && originalText.EndsWith('\'')) ||
                                  (originalText.StartsWith('“') && originalText.EndsWith('”')));

        if (!originalHasQuotes && text.Length >= 2)
        {
            if ((text.StartsWith('"') && text.EndsWith('"')) ||
                (text.StartsWith('\'') && text.EndsWith('\'')) ||
                (text.StartsWith('“') && text.EndsWith('”')))
            {
                text = text[1..^1].Trim();
            }
        }

        // Safety guard: if output contains instruction echo not in originalText, fallback to originalText
        if (ContainsInstructionEcho(text, originalText))
        {
            return originalText;
        }

        return text;
    }
}
