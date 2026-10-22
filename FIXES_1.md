# Fix round 1 (from the end-to-end test against the real model)

The real model `maternion/hy-mt2:1.8b` was tested on sample DOCX/PPTX/XLSX/PDF/TXT/MD files. Fix these bugs:

## 1. CRITICAL: prompt template — the model translates the instruction itself
For short inputs (a single word such as "Comment", or a heading such as "Installation Guide"), the current two-line English template makes the model output the TRANSLATION OF THE INSTRUCTION instead of the text. Real outputs:
`Not edin ki, sadece çevrilmiş sonucu ek bilgi olmadan yazmalısınız:` and `# Lütfen, sadece çevrilmiş sonucu ek bilgi olmadan yazmanızı unutmayın:`
Measured fix: use this SINGLE-LINE template (tested; it works for short and long text, tags and multiple paragraphs):
```
Translate the following segment into {target_en}, without additional explanation.

{text}
```
For Chinese source/target, the zh version is: `把下面的文本翻译成{target_zh}，不要额外解释。\n\n{text}`. Do not add the source language to the prompt.
Also add a **safety guard** (`Languages.PostProcessTranslation`): if the output contains phrases that indicate the instruction was echoed (for example, case-insensitive matches for "without additional explanation", "additional explanation", "ek açıklama", "çevrilmiş sonucu", "sadece çevir", "translated result", "不要额外解释"), and those phrases are NOT in the source text, treat it as a bad output: retry once; if it is still bad, return the source text unchanged. Write a unit test (with a fake translator that returns the echo).

## 2. MD: link URL is lost
Input `Run the following command to install the [package](https://example.com/pkg).` → output `[Package]yi kurmak için ...`; the `(https://example.com/pkg)` part DISAPPEARED. Fix: before translation, replace links and images with **placeholder tokens** (for example, links become `[text]` + a protected URL and images are removed entirely), or better: replace inline code and URLs with tokens like `⟦0⟧`, `⟦1⟧`, translate, then put them back. If a token is missing in the output, put the original back by appending it to the end of the line. Inline code (`Language`) works now; keep it working. Add a test (with a FakeTranslator that simulates dropping the URL, and a normal one).
Also: the MD output file starts with a **BOM** even though the source had none. Write the output in the source file's encoding (BOM only if the source had one). Apply the same rule to TXT.

## 3. PDF: original text residue shows outside the white box
Below the translated lines in the output, the original's descenders/accents (g, y, p, comma) show as thin strips under the white rectangle. PdfPig's word box does not include descenders. Fix: expand the cover rectangle by `0.30 * fontSize` downward, `0.15 * fontSize` upward, and `0.1 * fontSize` horizontally. Also make sure the PdfPig block box is the **union of all line boxes** (the last line must be covered too).
Where possible, use a background color other than white: sample the color just outside the block from the page rendering (optional). At minimum, keep white.

## 4. RichParagraphTranslator: cancellation and errors
- The `catch { return Fallback }` block also catches `OperationCanceledException` and Ollama connection errors (`OllamaException`), then calls the model again. These must be RETHROWN (`catch (Exception e) when (e is not OperationCanceledException and not OllamaException)`).
- The tagged call bypasses the TranslationService cache and SemaphoreSlim (it calls `service.Translator` directly). Add a public method to TranslationService (for example, `TranslateRawAsync`) that goes through the cache and semaphore, and use it.

## 5. XLSX / short cells
After fix #1, check that short cells ("Comment", "Units") translate properly.

## Acceptance
- `dotnet build HyTranslate.sln -c Debug -p:Platform=x64` → 0 errors
- `dotnet test tests/HyTranslate.Core.Tests -p:Platform=x64` → all pass (old + new tests)
