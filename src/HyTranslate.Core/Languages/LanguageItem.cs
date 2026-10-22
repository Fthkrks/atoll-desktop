namespace HyTranslate.Core.Languages;

public record LanguageItem(string Code, string EnglishName, string ChineseName, string TurkishName)
{
    public override string ToString() => TurkishName;
}
