# HyTranslate Desktop: technical spec

A Windows desktop translation app with a WinUI 3 interface (https://github.com/microsoft/microsoft-ui-xaml). Translation runs on a **local Ollama** server with the model `maternion/hy-mt2:1.8b` (Tencent Hunyuan HY-MT2, 1.8B, Q4_K_M).

There are two main uses:
1. **Text translation**, like Google Translate: two panes, source and target language, automatic translation as you type.
2. **Document translation** for DOCX, PPTX, XLSX, PDF, TXT and MD. The output file's **structure and formatting must not break** (styles, tables, images, layout, page structure).

All UI text is **Turkish**.

## 1. Environment and project layout

- .NET SDK 9.0.306 is installed. There is NO Visual Studio and NO Windows SDK; everything must build with the `dotnet` CLI alone.
- WinUI 3 comes from the NuGet package `Microsoft.WindowsAppSDK` (latest stable 1.x). There is no WinUI template, so write the project files by hand.

```
Desktop/
  HyTranslate.sln
  src/HyTranslate.Core/        (net9.0 class library: all logic, no UI dependency)
  src/HyTranslate.App/         (WinUI 3 app)
  tests/HyTranslate.Core.Tests/ (xUnit)
  README.md                    (Turkish: setup, ollama pull, run)
```

### HyTranslate.App.csproj (required settings)
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
<RootNamespace>HyTranslate.App</RootNamespace>
<Platforms>x64</Platforms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<UseWinUI>true</UseWinUI>
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<EnableMsixTooling>true</EnableMsixTooling>
<Nullable>enable</Nullable>
```
Packages: `Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`, `CommunityToolkit.Mvvm`. Also include `app.manifest` (PerMonitorV2 DPI).
The app must open with `dotnet run --project src/HyTranslate.App -c Debug -p:Platform=x64`.

### HyTranslate.Core packages
- `DocumentFormat.OpenXml` (3.x) for DOCX, PPTX and XLSX
- `UglyToad.PdfPig` (latest prerelease or stable 1.7+) to read PDF text with positions
- `PDFsharp` (6.x, MIT) to write over PDF pages
- Markdown: use a simple custom parser; no package is needed.

## 2. Ollama integration (`HyTranslate.Core/Ollama`)

- `OllamaClient` (HttpClient; base URL from settings, default `http://localhost:11434`):
  - `Task<bool> IsAliveAsync()`: `GET /api/version`
  - `Task<bool> HasModelAsync(string model)`: `GET /api/tags`
  - `IAsyncEnumerable<PullProgress> PullModelAsync(model)`: `POST /api/pull` with streaming NDJSON (status, total, completed)
  - `IAsyncEnumerable<string> ChatStreamAsync(prompt, options, ct)` and `Task<string> ChatAsync(...)`: `POST /api/chat`, `messages:[{role:"user",content:prompt}]`, **with no system prompt** (the model card says there is none).
  - options (the model card's recommended settings): `temperature 0.7, top_p 0.6, top_k 20, repeat_penalty 1.05, num_predict 4096`. Use `keep_alive: "30m"`.
  - Throw a meaningful `OllamaException` when the connection fails. The UI shows it as "Ollama çalışmıyor. `ollama serve` ile başlatın."

### Prompt template (verbatim from the model card)
- When the source or target language is Chinese (zh / zh-Hant / yue):
  ```
  将以下文本翻译为{target_lang_zh}，注意只需要输出翻译后的结果，不要额外解释：

  {text}
  ```
- Otherwise:
  ```
  Translate the following text into {target_lang_en}.
  Note that you should only output the translated result without any additional explanation:

  {text}
  ```
  (When the source language is known, it may begin with "Translate the following {source} text into {target}.")
- Post-process the output: trim it, and strip any wrapping ``` or quote marks the model adds that were not in the source.

### Languages (`Languages.cs`)
Each entry has a code, an English name (for the prompt), a Chinese name (for the zh prompt), and a Turkish name (for the UI): zh, en, fr, pt, es, ja, tr, ru, ar, ko, th, it, de, vi, ms, id, fil (Filipino), hi, zh-Hant (Traditional Chinese / 繁体中文), pl, cs, nl, km, my (Burmese), fa, gu, ur, te, mr, he, bn, ta, uk, bo (Tibetan), kk, mn, ug, yue (Cantonese / 粤语).
The source language also has an "Algıla (otomatik)" option. In that case, do not name the source in the prompt.

## 3. Translation service (`HyTranslate.Core/Translation`)

- `ITranslator { Task<string> TranslateAsync(string text, string targetLang, string? sourceLang, CancellationToken ct); }`. `OllamaTranslator` implements it. Tests use `FakeTranslator` (for example, a prefix such as `"[tr]" + text`, or a reversed string).
- `TranslationService`:
  - **Segmentation**: split long text (> ~1500 characters) at paragraph boundaries, then sentence boundaries, translate each chunk and join them. Keep blank lines and line breaks exactly as they were.
  - **Skip rules**: do not send whitespace-only text, text that is only digits or punctuation, URLs, e-mail addresses, or text shorter than 2 letters to the model; return it unchanged.
  - **Cache**: `(text, src, tgt)` → translation, a thread-safe dictionary for the session. Repeated cells or headers are translated once.
  - **Whitespace preservation**: keep the segment's leading and trailing whitespace and add it back after translation.
  - Concurrency: `SemaphoreSlim(2)`. One local model is enough, so do not allow too many parallel requests.
- `IProgress<TranslationProgress>` reports done and total segment counts plus the current part ("Sayfa 3/10", "Slayt 2").

### Rich text (inline formatting) strategy: this matters
Inside a paragraph, text is split into several runs with different formatting (bold or italic words, for example). Translating each run separately breaks the meaning, so:
1. Collect the paragraph's runs. If they **all have the same formatting**, translate the joined text as one piece, put the result in the first run, and empty the rest.
2. If formatting differs, wrap **every** run with tags: `<g1>Warning: </g1><g2>Do not</g2><g3> unplug.</g3>`.
   **IMPORTANT (measured on the real model):** do NOT add an extra instruction line to the prompt such as "Keep the tags". The model translates that line too and puts it in the output. Send the tagged text with the standard template; the model keeps the tags anyway.
   **IMPORTANT (measured):** the model keeps the tags but can **change their order** to match the target language's grammar. It can also leave text **outside** the tags. Real output:
   input `Click <g1>Save</g1> to store the <g2>project settings</g2> permanently.` → output `<g2>proje ayarlarını</g2> kalıcı olarak saklamak için <g1>Kaydet</g1>’e tıklayın.`
   Parsing rules:
   - Split the output into an ordered list of `(runIndex, text)` pieces. Text outside tags joins the **preceding** tagged piece; text at the very start joins the first piece.
   - Validation: each `gN` appears exactly once, there are no unknown or nested tags, and tag count = run count. If it fails → fallback (step 3).
   - If the order is unchanged → write each piece's text into its run.
   - If the order changed → **reorder the run elements** in output order (only when all runs are adjacent siblings under the same parent; for example, one run inside `w:hyperlink` and another outside is not allowed). Run properties travel with their runs. If they cannot be reordered → fallback.
3. If tag validation fails, **fall back**: put the whole translated paragraph (with tags removed) in the run with the most text and empty the others. The document never breaks; at worst only the inline formatting is lost. For the fallback, if the tagged output is unusable, translate the plain joined text without tags again.
Write this logic once and generically as `RichParagraphTranslator` (input: `IReadOnlyList<string> runTexts` plus a "same formatting?" flag; output: `RichResult { IReadOnlyList<(int RunIndex, string Text)> Pieces; bool Reordered; bool FellBack; }`). DOCX, PPTX and XLSX rich text all use it. Never change run properties. Run order may change only in the controlled case above; otherwise change **only the text values**.

## 4. Document translators (`HyTranslate.Core/Documents`)

Common interface: `IDocumentTranslator { string[] Extensions; Task TranslateAsync(string inputPath, string outputPath, TranslateOptions opt, IProgress<TranslationProgress>? p, CancellationToken ct); }`. `DocumentTranslatorFactory` picks one by file extension. **The input file is never modified**: copy it to the output path, then open the copy and edit it.
Default output name: `<name>.<targetCode><ext>` (for example `rapor.tr.docx`).

### DOCX (OpenXML)
- Visit the main document, **all headers and footers, footnotes, endnotes, comments, text boxes (`w:txbxContent`) and tables**.
- Unit: `w:p` paragraph. Runs are `w:r` → `w:t`. Runs inside hyperlinks (`w:hyperlink`) and `w:smartTag` / `w:sdt` content also belong to the paragraph.
- **Never** touch field codes (`w:instrText`, `w:fldChar`), deleted text (`w:delText`), or tabs and line breaks (`w:tab`, `w:br`). Treat a `w:br` / `w:tab` inside a run as a run boundary.
- Set `xml:space="preserve"` on every `w:t` you write.
- If a translated paragraph is in Arabic, Hebrew, Persian or Urdu, add `w:bidi` to the paragraph and `w:rtl` to the run (optional, nice to have).

### PPTX
- Every slide, slide layout/master (optional: placeholder text only), notes slides, and text inside tables and grouped shapes.
- Unit: `a:p` paragraph, `a:r` → `a:t`. Do not touch `a:fld` (slide number, date).
- Text may overflow its box: if the shape has `a:bodyPr` with no `a:normAutofit`, adding one can be optional; do not change anything else.

### XLSX
- `SharedStringTable`: each `si` is plain (`t`) or rich (`r` runs) and goes through `RichParagraphTranslator`. Inline strings (`is`) are handled the same way.
- **Do not touch** cells with formulas, numbers, dates or booleans.
- Sheet names: translation is optional and **off by default** (formula references would break).
- Comments (`comments` part): optional.

### TXT
- Preserve the encoding (detect BOM, default UTF-8) and line-ending style (CRLF/LF). Translate paragraph by paragraph, keeping blank lines.

### MD (Markdown)
- Do not translate: fenced code blocks (```` ``` ````/`~~~`), inline `code`, link URLs `[text](url)` (translate only the text), image URLs, HTML tags, front matter (`---` yaml), or table separator lines.
- Keep heading `#`, list `-`/`*`/`1.`, quote `>` prefixes and table pipes `|` exactly; translate only the content. Line-by-line processing is enough.

### PDF (the hardest one; take care)
Approach: **overlay**, which keeps the original page, images, vectors and layout.
1. Read with PdfPig: for each page, get words with `page.GetWords(NearestNeighbourWordExtractor)`, then group them into lines/blocks with `DocstrumBoundingBoxes` (in `UglyToad.PdfPig.DocumentLayoutAnalysis`). Keep each block's text, bounding box, average font size, and dominant color (when available).
2. Translate each block (with TranslationService).
3. Open the copy with PDFsharp (`PdfReader.Open(path, PdfDocumentOpenMode.Modify)`). For each page, use `XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append)`:
   - Cover the block's area with a **white** filled rectangle (pad it by 1pt).
   - Write the translated text **inside the same box**: start at the original font size, wrap at word boundaries, and if it does not fit, **shrink the font in 0.5pt steps** (minimum 5pt). Use top-aligned layout.
   - Coordinates: PdfPig has a bottom-left origin and XGraphics has a top-left origin → `y_top = pageHeight - bbox.Top`. Handle page rotation (`/Rotate`) and the CropBox/MediaBox offset.
4. Fonts: PDFsharp 6 needs an `IFontResolver`. Write a `WindowsFontResolver` that loads `C:\Windows\Fonts\segoeui.ttf` / `segoeuib.ttf` (fallback `arial.ttf`) so Turkish, Cyrillic and similar characters work. For CJK, use `msyh.ttc` / `YuGothM.ttc` when present (optional). Fonts are embedded (Unicode).
5. **Scanned PDFs**: if a page has no text, leave it unchanged and add a warning to the progress report ("Sayfa X metin içermiyor (taranmış olabilir), atlandı").
6. Limitations (show them in the README and as an info line in the UI): the original text stays underneath the white box, and on colored backgrounds the white box shows.

## 5. WinUI 3 interface (`HyTranslate.App`)

- `MainWindow` uses a **Mica** backdrop (`MicaBackdrop`), a custom title bar (`ExtendsContentIntoTitleBar`), and a `NavigationView` (Left/LeftCompact) with these items:
  1. **Metin** (icon: `Character` / glyph E8D2): TextTranslatePage
  2. **Belgeler** (icon: Document): DocumentTranslatePage
  3. The footer has **Ayarlar**: SettingsPage
- Use MVVM (CommunityToolkit.Mvvm `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`). Wire up services through a simple `App.Services` (Microsoft.Extensions.DependencyInjection is fine).
- Use the Fluent design language: `ThemeResource`s and `CardBackgroundFillColorDefaultBrush` cards. Light and dark themes both follow the system.

### TextTranslatePage (Google Translate style)
- Top row: source language `ComboBox` (first item "Dili algıla"), a **swap button** in the middle (⇄, glyph E8AB; disabled when the source is "algıla"), and the target language `ComboBox` (default: Türkçe; if the source is Turkish, English).
- Two side-by-side cards (stacked vertically when the window is narrow, with `VisualStateManager` and an AdaptiveTrigger at 720px):
  - Left: multi-line `TextBox` (AcceptsReturn, TextWrapping=Wrap), placeholder "Çevrilecek metni yazın", character counter "123 / 5000" at the bottom, clear (X) button.
  - Right: read-only `TextBox` (selectable), "Çeviri" placeholder, **copy** button (Clipboard), and a `ProgressRing` while translation runs.
- **Automatic translation**: start 600 ms after typing stops (debounce). A new keystroke **cancels** the previous request (CancellationToken). There is also a manual "Çevir" button and Ctrl+Enter.
- Show the result **streaming** (ChatStreamAsync tokens appear as they arrive).
- Errors appear in an `InfoBar` (for example, when Ollama is down).

### DocumentTranslatePage
- A large drag-and-drop area ("Dosyayı buraya sürükleyin veya seçin", AllowDrop + DragOver/Drop) and a "Dosya seç" button (`FileOpenPicker`; in an unpackaged app, call `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)`). Filter: .docx .pptx .xlsx .pdf .txt .md.
- Selected-file card: name, size, type icon.
- Source/target language selection (same control as on the Text page; you can extract it as a UserControl).
- "Çevir" button → `ProgressBar` (determinate) + status text ("Segment 45/120 · Sayfa 3"), "İptal" button.
- When finished: InfoBar Success "Kaydedildi: …", plus **Dosyayı aç** (`Process.Start` with UseShellExecute) and **Klasörde göster** (`explorer /select,`) buttons. The output path can be chosen with `FileSavePicker` (default: next to the input, named `name.tr.ext`).
- List warnings (such as scanned PDF pages) in an expandable InfoBar.

### SettingsPage
- Ollama address (TextBox, default `http://localhost:11434`), model name (default `maternion/hy-mt2:1.8b`).
- "Bağlantıyı test et" button → status: ✓ Ollama is running / ✓ the model is installed / ✗ the model is not installed.
- "Modeli indir" button → `PullModelAsync` with a ProgressBar.
- Theme: System / Light / Dark.
- Save settings as JSON in `%LOCALAPPDATA%\HyTranslate\settings.json` (unpackaged, so not ApplicationData).
- On first launch, if Ollama or the model is missing, show an InfoBar on the main page with a link to Ayarlar.

## 6. Tests (`tests/HyTranslate.Core.Tests`, xUnit)
Use `FakeTranslator` (`text => "ÇEVRİ:" + text`, and for tags a version that keeps the tags):
- `RichParagraphTranslator`: same formatting → first run full, others empty; tags preserved → spread across runs; tags broken → fallback.
- DOCX: build a document in code with a heading, a table, bold+normal runs, a header/footer and a field code. After translation: the paragraph count, table count, run count and RunProperties stay identical, and the field instrText is unchanged.
- XLSX: shared strings are translated; formula and number cells are unchanged.
- PPTX: build a small presentation in code (or use a minimal one); the a:t values change and the shape count stays the same.
- MD: code blocks, link URLs and heading prefixes are preserved.
- TXT: CRLF and blank lines are preserved.
- PDF: create a 1-page PDF with text using PDFsharp, translate it, and the output opens with PdfPig with the same page count and contains the translated text.
- TranslationService: skip rules and cache (the same text calls the translator only once).

## 7. Acceptance criteria
- `dotnet build HyTranslate.sln -c Debug -p:Platform=x64` → **0 errors**.
- `dotnet test tests/HyTranslate.Core.Tests` → **all tests pass**.
- No TODOs or placeholders; every feature must actually work.
