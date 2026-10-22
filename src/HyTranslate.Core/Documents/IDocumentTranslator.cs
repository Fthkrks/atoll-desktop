using System;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Translation;

namespace HyTranslate.Core.Documents;

public interface IDocumentTranslator
{
    string[] Extensions { get; }
    Task TranslateAsync(string inputPath, string outputPath, TranslateOptions opt, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default);
}
