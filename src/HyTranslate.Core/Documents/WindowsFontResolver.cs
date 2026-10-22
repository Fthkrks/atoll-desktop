using System;
using System.Collections.Concurrent;
using System.IO;
using PdfSharp.Fonts;

namespace HyTranslate.Core.Documents;

public class WindowsFontResolver : IFontResolver
{
    private static readonly ConcurrentDictionary<string, byte[]> FontCache = new(StringComparer.OrdinalIgnoreCase);

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Normalise name
        var name = familyName.ToLowerInvariant();

        // Check for CJK or specific scripts if needed
        string faceName;
        if (name.Contains("segoe") || name.Contains("arial") || name.Contains("sans") || name.Contains("default"))
        {
            if (isBold && isItalic) faceName = "segoeuiz.ttf";
            else if (isBold) faceName = "segoeuib.ttf";
            else if (isItalic) faceName = "segoeuii.ttf";
            else faceName = "segoeui.ttf";
        }
        else
        {
            // Default to Segoe UI or Arial
            if (isBold && isItalic) faceName = "segoeuiz.ttf";
            else if (isBold) faceName = "segoeuib.ttf";
            else if (isItalic) faceName = "segoeuii.ttf";
            else faceName = "segoeui.ttf";
        }

        return new FontResolverInfo(faceName);
    }

    public byte[]? GetFont(string faceName)
    {
        if (FontCache.TryGetValue(faceName, out var cachedBytes))
        {
            return cachedBytes;
        }

        var fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var fontPath = Path.Combine(fontsFolder, faceName);

        if (!File.Exists(fontPath))
        {
            // Fallback to Arial if Segoe UI face missing
            fontPath = Path.Combine(fontsFolder, "arial.ttf");
        }

        if (File.Exists(fontPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(fontPath);
                FontCache[faceName] = bytes;
                return bytes;
            }
            catch
            {
                // Return null if cannot read
            }
        }

        return null;
    }
}
