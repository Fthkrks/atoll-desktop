using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HyTranslate.Core.Documents;

public class DocumentTranslatorFactory
{
    private readonly IEnumerable<IDocumentTranslator> _translators;

    public DocumentTranslatorFactory(IEnumerable<IDocumentTranslator> translators)
    {
        _translators = translators ?? throw new ArgumentNullException(nameof(translators));
    }

    public IDocumentTranslator? GetTranslator(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;

        return _translators.FirstOrDefault(t =>
            t.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
    }

    public static string GetDefaultOutputPath(string inputPath, string targetLang)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);
        var targetFile = $"{nameWithoutExt}.{targetLang.ToLowerInvariant()}{ext}";
        return string.IsNullOrEmpty(dir) ? targetFile : Path.Combine(dir, targetFile);
    }

    public static string MakeUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        int counter = 2;
        while (true)
        {
            var candidateName = $"{nameWithoutExt} ({counter}){ext}";
            var candidate = string.IsNullOrEmpty(dir) ? candidateName : Path.Combine(dir, candidateName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
            counter++;
        }
    }
}
