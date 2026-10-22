# FIXES_3 – OCR overlay fine-tuning and DI (after the E2E test)

E2E: `crypto00.pdf` was translated with the real model and WindowsPdfOcr. The result is mostly good, but:

## 1. The OCR block overflows its box (overlaps the next line)
Euclid page: the source block is 3 centered lines ("The greatest common divisor of two / numbers is easily computed / (using "Euclid's Algorithm"):"). The translation was drawn with a larger font and wider line spacing than the source; the 3rd line spilled onto "gcd(12,30)=6" below it. Also, the source was centered, but the translation was drawn left-aligned.

Fix (OCR blocks only; do not change the text-layer behaviour):
- **Font size estimate**: if the block has ≥2 lines, `linePitch = median(Top[i+1]-Top[i])`, `fontSize = linePitch / 1.2`. For a single line, `fontSize = lineHeight × 0.85`. Clamp to 6–72. Put this in `OcrBlockBuilder` (testable).
- **Strict height limit**: in `DrawFittedText`, for OCR blocks the rule `lines.Count <= expectedLines` must NOT be enough on its own; the total text height (`lines.Count × lineHeight`) must be `<= block height + 0.3×fs`. Otherwise shrink the font (minimum 5). Draw the text with line height = `font.GetHeight()`.
- **Alignment detection**: add `TextAlign Alignment` (Left/Center) to `OcrBlock`. If the block has ≥2 lines and the standard deviation of the line centres is < 0.5 × that of the left edges (and the left edges differ by > 1 × line height) → Center. A single-line block is Left. If Center, draw each line centred in the block width (`x + (w - lineWidth)/2`).
- The cover rectangle must still use the block's original box (+0.15×fs); the text must not overflow it.

## 2. DI ambiguity (risk of the app crashing)
`PdfDocumentTranslator` has two 2-parameter constructors, `(TranslationService, IPdfPageOcr?)` and `(TranslationService, Func<IPdfPageOcr?>?)`, and both IPdfPageOcr and Func are registered in DI → `Microsoft.Extensions.DependencyInjection` throws "ambiguous constructors".
Fix: in `App.xaml.cs`, register explicitly with a factory:
`services.AddSingleton<IDocumentTranslator>(sp => new PdfDocumentTranslator(sp.GetRequiredService<TranslationService>(), sp.GetRequiredService<Func<IPdfPageOcr?>>()));`
Remove the transient `IPdfPageOcr` registration (it is not needed).
Also: if the `WindowsPdfOcr` instance created inside `TranslateAsync` is `IDisposable`, dispose it with `using`/`finally` when the translation ends (it holds the PdfDocument/file handle).

## 3. Tests
- OcrBlockBuilder: font size from line pitch (lines at Top 100, 120, 140 → fs ≈ 16.7).
- Alignment: 3 lines with different widths and the same centre → Center; 3 lines with the same left edge → Left.
- Add a test (in the test project, with a ServiceCollection built the same way) that `PdfDocumentTranslator` resolves from DI, or at least do the factory registration in `App.xaml.cs` correctly.

Acceptance: `dotnet build HyTranslate.sln -c Debug -p:Platform=x64` 0 errors; `dotnet test tests/HyTranslate.Core.Tests -p:Platform=x64` all pass.
