using System.Collections.Generic;

namespace HyTranslate.Core.Translation;

public record RichResult(
    IReadOnlyList<(int TemplateRunIndex, string Text)> Pieces,
    bool FellBack
)
{
    public bool Reordered { get; init; }
}
