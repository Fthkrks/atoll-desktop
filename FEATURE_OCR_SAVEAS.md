# Feature: (A) choose where the translated file is saved, (B) OCR for text inside images in PDFs

Read the existing code first (`src/HyTranslate.Core/Documents/PdfDocumentTranslator.cs`, `src/HyTranslate.App/ViewModels/DocumentViewModel.cs`, `Views/DocumentTranslatePage.xaml(.cs)`, `Core/Settings/AppSettings.cs`, `App.xaml.cs`). Keep the existing style. Do NOT break any other format.

## Problem (measured)
- `crypto00.pdf` (15 pages, lecture slides): each page has only the title in the text layer; the body text ("DES Designed at IBM; Horst Feistel supplied key…", "There are infinitely many primes…") is **inside an embedded raster image**. The current code translates only the titles.
- The built-in Windows OCR (`Windows.Media.Ocr`, installed language: `tr`) reads this text correctly on a 200-DPI rendering (tested: "DES Designed at IBM; Horst Feistel supplied key / elements of design, such as ladder structure. NSA / …"). Handwritten notes are mostly ignored or produce 1–2 character garbage.
- Currently the output path is always forced to `<source folder>\name.<lang>.ext`; the user cannot choose it.

---

## A) Choosing the save location

### AppSettings (new, optional fields)
```csharp
public string LastOutputFolder { get; set; } = "";   // empty = source file's folder
public bool OcrEnabled { get; set; } = true;
public string OcrLanguage { get; set; } = "";        // empty = automatic
```

### DocumentViewModel
- `OutputFilePath` is now **determined as soon as a file is selected** (not when translation starts): `Path.Combine(LastOutputFolder if it exists, else the source folder, Path.GetFileName(DocumentTranslatorFactory.GetDefaultOutputPath(src, targetCode)))`. When the target language changes, if the user has NOT picked a path by hand, recalculate it (`_outputPathChosenByUser` flag; reset when a new file is selected).
- `void SetOutputPath(string path)`: sets `_outputPathChosenByUser = true` and saves `LastOutputFolder = Path.GetDirectoryName(path)` to settings (SettingsManager).
- When translation starts:
  - If the output path is the same as the input path (case-insensitive full path), show ErrorMessage = "Çıktı dosyası kaynak dosyayla aynı olamaz." and do not start.
  - If the path was NOT chosen by hand and the file already exists, make it unique with `name.tr (2).pdf`, `(3)`… (do not overwrite silently). If it was chosen by hand, overwrite it (FileSavePicker already asked for confirmation).
  - If the target folder does not exist, call `Directory.CreateDirectory`.
- The rest (SuccessMessage, open file, show in folder, delete on cancel) keeps working with `OutputFilePath`.

### DocumentTranslatePage.xaml
- Below the selected-file card (visible when a file is selected), add a **"Kayıt yeri"** row:
  - A read-only `TextBox` (or `TextBlock` + ToolTip) bound to `OutputFilePath`.
  - A **"Değiştir…"** button → `FileSavePicker`:
    - `SuggestedFileName` = the file name without its extension.
    - `FileTypeChoices` = only the source extension (e.g. "PDF belgesi" → [".pdf"]).
    - `SuggestedStartLocation = DocumentsLibrary`.
    - `InitializeWithWindow` (unpackaged).
    - On success, call `ViewModel.SetOutputPath(file.Path)`.
    - IMPORTANT: FileSavePicker creates an empty 0-byte file at the chosen path. That is fine; the translator overwrites it. But if translation is cancelled, the existing cancel code deletes it, which is OK.
  - Disable the button while translation is running (`IsTranslating`).

---

## B) OCR – text inside images in PDFs

### Target framework
- `HyTranslate.Core.csproj` and `tests/HyTranslate.Core.Tests/*.csproj`: `TargetFramework` → `net9.0-windows10.0.19041.0`, add `<SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>`. That way the WinRT APIs (`Windows.Data.Pdf`, `Windows.Media.Ocr`, `Windows.Graphics.Imaging`, `Windows.Storage`) can be used from Core. No extra NuGet package is needed (CsWinRT projections come with the TFM). The test project should keep `Platforms` AnyCPU;x64; if needed, add `<RuntimeIdentifier>`; make sure `dotnet test ... -p:Platform=x64` works.

### New: `Core/Ocr/`
```csharp
public sealed record OcrTextLine(string Text, double Left, double Top, double Width, double Height); // PDF points, top-left origin (like XGraphics)
public sealed record OcrPageResult(IReadOnlyList<OcrTextLine> Lines, IOcrPixelSampler? Sampler);

public interface IOcrPixelSampler        // reads colours from the rendered page (for overlay colours)
{
    // rectangle in PDF points, top-left origin
    (byte R, byte G, byte B) BackgroundColor(double left, double top, double width, double height); // median of the pixels on a 3-px ring around the rectangle
    (byte R, byte G, byte B) TextColor(double left, double top, double width, double height);       // mean of the darkest 10% of pixels in the rectangle; if contrast with the background is < 60 (luminance difference), return black or white, whichever has the most contrast
}

public interface IPdfPageOcr
{
    bool IsAvailable { get; }                 // a usable OCR engine exists?
    string? LanguageTag { get; }              // language in use
    Task<OcrPageResult> RecognizePageAsync(string pdfPath, int pageIndex /*0-based*/, CancellationToken ct);
}

public static class OcrLanguages { IReadOnlyList<(string Tag, string DisplayName)> Available(); }  // OcrEngine.AvailableRecognizerLanguages
```

`WindowsPdfOcr : IPdfPageOcr` (constructor: `string? preferredLanguageTag`):
- Engine selection: if `preferredLanguageTag` is not empty and `OcrEngine.IsLanguageSupported(new Language(tag))`, use that; otherwise `OcrEngine.TryCreateFromUserProfileLanguages()`; if that is null, use the first entry of `AvailableRecognizerLanguages`; if none, `IsAvailable=false`. (Turkish OCR also reads English/Latin text well, so it is a fine fallback.)
- Rendering: `StorageFile.GetFileFromPathAsync(fullPath)` → `Windows.Data.Pdf.PdfDocument.LoadFromFileAsync` (cache the document per path, so it is not reopened for every page; release it in Dispose) → `page.RenderToStreamAsync(InMemoryRandomAccessStream, new PdfPageRenderOptions { DestinationWidth = (uint)(page.Size.Width * 200/96) })`. Note: `page.Size` is in DIPs (96/inch); target about 200 DPI. Limit: if the longest side would exceed `OcrEngine.MaxImageDimension`, scale it down.
- `BitmapDecoder.CreateAsync(stream)` → `GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)` → `engine.RecognizeAsync(bitmap)`.
- Coordinates: pixel → PDF point: `pt = px * (pageWidthPt / bitmapWidthPx)`. PDF page width in points = `page.Size.Width * 72/96`. For each `OcrLine`: text = `line.Text`; box = union of all `Words[i].BoundingRect`.
- Sampler: copy the bitmap's BGRA bytes into a `byte[]` (`SoftwareBitmap.CopyToBuffer` + `IBuffer.ToArray()` / `DataReader`) and implement `IOcrPixelSampler` on that array (pure C#).
- Thread safety: one engine + one PdfDocument; call RecognizePageAsync sequentially (the PDF translator already works page by page).

### PdfDocumentTranslator changes
- The constructor gets an optional `IPdfPageOcr? ocr = null` (DI: `services.AddSingleton<IPdfPageOcr>(sp => new WindowsPdfOcr(settings.OcrLanguage))`. Because the setting can change, the cleanest approach is a `Func<IPdfPageOcr?>` factory or a new instance per `TranslateAsync`; choose one, but it must respect `OcrEnabled=false`). `TranslateOptions` gets a new `bool UseOcr { get; init; } = true;` field; DocumentViewModel fills it from `settings.OcrEnabled`.
- For each page, **in addition to** the text-layer blocks: if `opt.UseOcr && ocr.IsAvailable`:
  1. Decide whether to OCR the page: (a) the page has no text at all (scanned), OR (b) the page has images (`page.GetImages()` count > 0 in PdfPig) and their total area is > 5% of the page. Otherwise skip (save time).
  2. `RecognizePageAsync`.
  3. **Filter** the OCR lines (pure function, put it in a separately testable `static class OcrBlockBuilder`):
     - Discard the line if its box overlaps a word box from the text layer by more than 30% (that text is already translated through the text layer). The word boxes come from PdfPig (bottom-left origin) → convert to top-left.
     - Discard it if it has fewer than 3 letters (`char.IsLetter`), or if letters are under 50% of the non-space characters (formulas, numbers like "30 12", handwriting garbage).
  4. **Group into blocks** (`OcrBlockBuilder.Group(lines)`): sort by Top. A line joins the previous block when: its vertical gap to the previous line is `< 0.8 × median line height`, its height is within ±35% of the previous line's, and its horizontal overlap is > 0 or its left edge is within 1.5 × line height of the previous line's left edge (or its center within the same tolerance → centered text). Block text = the lines joined with a space; if a line ends with "-" and the next starts with a lowercase letter, drop the hyphen and join without a space. Block box = union of the lines. `LineCount` = the number of lines. Font size estimate = `median line height (pt) × 1.0` (the OCR word box ≈ ascender–descender height); clamp to 6–72.
  5. Use these blocks like the existing `ExtractedBlock`s: `TranslationService.ShouldSkip` check → translate → cover → `DrawFittedText`.
- **Overlay colours (OCR blocks only)**: the cover rectangle colour = `sampler.BackgroundColor(rect)` (not plain white; it blends better on coloured backgrounds), and the text colour = `sampler.TextColor(rect)`. `DrawFittedText` must take a colour parameter (text-layer blocks keep using black/white as now). Cover padding for OCR blocks: 0.15×fs on every side (OCR boxes are already tight).
- Pages without any text: instead of the current warning "Sayfa N metin içermiyor (taranmış olabilir), atlandı", process them with OCR. If OCR is unavailable or disabled, keep the warning, but with this text: "Sayfa N yalnızca görsel içeriyor; OCR kapalı/yüklü değil, atlandı".
- If OCR is not available at all (no language pack), report this once: "Windows OCR dil paketi bulunamadı; görsellerdeki metin çevrilmedi. (Ayarlar > Saat ve dil > Dil > İngilizce > Optik karakter tanıma)".
- Progress: while OCR runs for a page, set `CurrentPart` to "Sayfa 3/15 – görsel metni okunuyor".
- If OCR throws on one page, do not fail the whole translation: add the warning "Sayfa N: OCR hatası – {msg}" and continue. Let `OperationCanceledException` through.

### Settings page
In the "Belge çevirisi" (or a suitable) card:
- A `ToggleSwitch` "PDF'lerdeki görsellerin içindeki metni de çevir (OCR)" → `OcrEnabled`.
- A `ComboBox` "OCR dili": first entry "Otomatik", then `OcrLanguages.Available()`. If the list is empty, show the note "Yüklü OCR dili yok".

### Tests (`tests/HyTranslate.Core.Tests`)
- `OcrBlockBuilder` filtering: a line that overlaps a text-layer word is dropped; "30 12" and "�s" are dropped; "DES Designed at IBM" is kept.
- Grouping: 3 lines with the same left edge and normal spacing → 1 block, text joined with spaces; a line far below → a separate block; centered lines of different widths (Euclid example) → 1 block; hyphen joining.
- `PdfDocumentTranslator` + a **fake `IPdfPageOcr`** (returns fixed lines) + FakeTranslator: build a 1-page PDF with PDFsharp that contains an image (a small bitmap drawn with `XImage.FromStream`) and a title; after translation, check with PdfPig that the output text contains the translation of the OCR line (FakeTranslator output) and the translated title. Use a fake sampler that returns white/black.
- With `UseOcr=false`, the fake OCR must never be called.
- Output path: a unit test for the helper that makes `name.tr.pdf` → `name.tr (2).pdf` unique (put it in Core as `DocumentTranslatorFactory.MakeUniquePath(string path)`).

## Acceptance
- `dotnet build HyTranslate.sln -c Debug -p:Platform=x64` → 0 errors
- `dotnet test tests/HyTranslate.Core.Tests -p:Platform=x64` → all pass (38 existing + the new ones)
- Add an "OCR" and a "Kayıt yeri" section to the README (Turkish).
