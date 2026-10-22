# Fix round 2 (end-to-end test results)

## 1. CRITICAL: RichParagraphTranslator garbles text when there are too many tags
DOCX paragraph runs: `"Click "`, `"Save"`(bold), `" to store the "`, `"project settings"`(italic), `" permanently."`
Currently EVERY run is tagged (5 tags) and the real model produces garbage:
`<g1>Click</g1><g2>Save</g2><g3>arak</g3><g4>projek tasarım ayarlarını</g4><g5>kalıcı olarak kaydetmek için.</g5>`
(untranslated words, spaces between runs lost, broken sentence). With ONLY the formatted parts tagged, the same model gives an excellent result (measured):
`Click <g1>Save</g1> to store the <g2>project settings</g2> permanently.` → `<g2>proje ayarlarını</g2> kalıcı olarak kaydetmek için <g1>Kaydet</g1>’e tıklayın.`

New algorithm:
1. Give each run a "format key" (DOCX: the OuterXml of RunProperties, with rsid* attributes ignored; PPTX: a:rPr minus the lang/dirty/err attributes; XLSX: rPr). The caller (Docx/Pptx/Xlsx translators) passes these keys to RichParagraphTranslator: `TranslateRunsAsync(IReadOnlyList<string> runTexts, IReadOnlyList<string> formatKeys, ...)`.
2. **Base format** = the format key with the most characters in total.
3. Merge adjacent runs that share a format key (logically).
4. Leave base-format pieces **UNTAGGED**; wrap only the other pieces with `<gN>` (N counts only the tagged pieces). If no piece needs a tag, translate as plain text (the current same-formatting path).
5. Parse the output into an ordered list of pieces: `(FormatSource, Text)`. An untagged piece → base format (template = the base-format run with the longest text). `<gN>` → the format of tagged piece N (template = that piece's first run).
6. **Rebuild** the paragraph: for each output piece, **clone** the matching template run (deep clone, keeping RunProperties), set its text, and write the new runs **in output order** in place of the old ones. Remove the old text runs. Do NOT touch non-text elements inside the paragraph (bookmarks, fields `w:fldChar`/`instrText`, `w:drawing`, `w:tab`, `w:br`, hyperlinks, `a:fld`): TREAT THEM AS SEGMENT BOUNDARIES. Each contiguous text-run group between them is a separate "segment" and goes through RichParagraphTranslator on its own, and is rebuilt within its own range only. (Runs inside a hyperlink are their own segment.)
7. Validation: every tag appears exactly once, there is no nesting, and the total output text is not empty. If invalid → current fallback (translate the plain text and put it in the base template run).
8. **Spacing**: when joining pieces, if the end of one piece and the start of the next are both letters or digits and the target language uses spaces (not zh, zh-Hant, yue, ja, th, km, my, bo), insert a single space between them. Do not leave double spaces.
9. The RichResult structure can change: `Pieces: IReadOnlyList<(int TemplateRunIndex, string Text)>`, `FellBack`. Update all callers (DOCX, PPTX, XLSX rich text `si`).
Tests: the 5-run example above (a fake translator that returns the 2-tag output), merging adjacent same-format runs, a fallback for broken tags, and the paragraph's run count and properties staying correct after rebuild (DOCX test).

## 2. PDF: font size comes out too small
A 20pt heading in the original was written at about 13pt in the output. Do not use the glyph box height for font size; use the median of PdfPig `Letter.PointSize` (fallback: `Letter.FontSize` × the text matrix scale) for the letters in the block. Shrinking to fit the box still applies (it only shrinks when it does not fit). Also, if most of the block's letters are bold (the font name contains "Bold"), draw it bold.

## Acceptance
- `dotnet build HyTranslate.sln -c Debug -p:Platform=x64` → 0 errors
- `dotnet test tests/HyTranslate.Core.Tests -p:Platform=x64` → all pass
