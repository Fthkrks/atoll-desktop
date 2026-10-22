using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Translation;
using Xunit;

namespace HyTranslate.Core.Tests;

public class DocxDocumentTranslatorTests
{
    [Fact]
    public async Task Docx_PreservesStructureTablesRunPropertiesAndFieldCodes()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"docx_in_{Guid.NewGuid()}.docx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"docx_out_{Guid.NewGuid()}.docx");

        try
        {
            // Build document in code
            using (var doc = WordprocessingDocument.Create(tempInput, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());
                var body = mainPart.Document.Body!;

                // 1. Heading paragraph
                var headingP = new Paragraph(new Run(new Text("Introduction Heading")));
                body.AppendChild(headingP);

                // 2. Bold + normal runs in a paragraph
                var mixedP = new Paragraph();
                var boldRun = new Run(new RunProperties(new Bold()), new Text("Bold text: "));
                var normalRun = new Run(new Text("normal text follows"));
                mixedP.AppendChild(boldRun);
                mixedP.AppendChild(normalRun);
                body.AppendChild(mixedP);

                // 3. Field code run (e.g. PAGE number)
                var fieldP = new Paragraph();
                var fieldRun = new Run(new FieldCode("PAGE") { Space = SpaceProcessingModeValues.Preserve });
                fieldP.AppendChild(fieldRun);
                body.AppendChild(fieldP);

                // 4. Table
                var table = new Table();
                var tr = new TableRow();
                var tc1 = new TableCell(new Paragraph(new Run(new Text("Cell 1 text"))));
                var tc2 = new TableCell(new Paragraph(new Run(new Text("Cell 2 text"))));
                tr.AppendChild(tc1);
                tr.AppendChild(tc2);
                table.AppendChild(tr);
                body.AppendChild(table);

                // 5. Header and Footer
                var headerPart = mainPart.AddNewPart<HeaderPart>();
                headerPart.Header = new Header(new Paragraph(new Run(new Text("Document Header text"))));

                var footerPart = mainPart.AddNewPart<FooterPart>();
                footerPart.Footer = new Footer(new Paragraph(new Run(new Text("Document Footer text"))));

                mainPart.Document.Save();
            }

            // Count before translation
            int initialParagraphs, initialTables, initialRuns;
            using (var doc = WordprocessingDocument.Open(tempInput, false))
            {
                initialParagraphs = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().Count() +
                                    doc.MainDocumentPart.HeaderParts.Sum(h => h.Header.Descendants<Paragraph>().Count()) +
                                    doc.MainDocumentPart.FooterParts.Sum(f => f.Footer.Descendants<Paragraph>().Count());
                initialTables = doc.MainDocumentPart.Document.Body.Descendants<Table>().Count();
                initialRuns = doc.MainDocumentPart.Document.Body.Descendants<Run>().Count() +
                              doc.MainDocumentPart.HeaderParts.Sum(h => h.Header.Descendants<Run>().Count()) +
                              doc.MainDocumentPart.FooterParts.Sum(f => f.Footer.Descendants<Run>().Count());
            }

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new DocxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            // Verify after translation
            using (var doc = WordprocessingDocument.Open(tempOutput, false))
            {
                var body = doc.MainDocumentPart!.Document.Body!;
                int afterParagraphs = body.Descendants<Paragraph>().Count() +
                                      doc.MainDocumentPart.HeaderParts.Sum(h => h.Header.Descendants<Paragraph>().Count()) +
                                      doc.MainDocumentPart.FooterParts.Sum(f => f.Footer.Descendants<Paragraph>().Count());
                int afterTables = body.Descendants<Table>().Count();
                int afterRuns = body.Descendants<Run>().Count() +
                                doc.MainDocumentPart.HeaderParts.Sum(h => h.Header.Descendants<Run>().Count()) +
                                doc.MainDocumentPart.FooterParts.Sum(f => f.Footer.Descendants<Run>().Count());

                Assert.Equal(initialParagraphs, afterParagraphs);
                Assert.Equal(initialTables, afterTables);
                Assert.Equal(initialRuns, afterRuns);

                // Field code unchanged
                var fieldCodes = body.Descendants<FieldCode>().ToList();
                Assert.Single(fieldCodes);
                Assert.Equal("PAGE", fieldCodes[0].Text);

                // Check bold run retains Bold
                var runsWithBold = body.Descendants<Run>().Where(r => r.RunProperties?.Bold != null).ToList();
                Assert.Single(runsWithBold);

                // Check translation was applied
                var headerText = doc.MainDocumentPart.HeaderParts.First().Header.InnerText;
                Assert.Contains("TR:", headerText);

                var footerText = doc.MainDocumentPart.FooterParts.First().Footer.InnerText;
                Assert.Contains("TR:", footerText);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Docx_RebuildFiveRuns_RunCountAndPropertiesStayCorrect()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"docx_five_in_{Guid.NewGuid()}.docx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"docx_five_out_{Guid.NewGuid()}.docx");

        try
        {
            using (var doc = WordprocessingDocument.Create(tempInput, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());
                var body = mainPart.Document.Body!;

                // 5-run paragraph:
                // "Click ", "Save"(bold), " to store the ", "project settings"(italic), " permanently."
                var p = new Paragraph();
                p.AppendChild(new Run(new Text("Click ")));
                p.AppendChild(new Run(new RunProperties(new Bold()), new Text("Save")));
                p.AppendChild(new Run(new Text(" to store the ")));
                p.AppendChild(new Run(new RunProperties(new Italic()), new Text("project settings")));
                p.AppendChild(new Run(new Text(" permanently.")));
                body.AppendChild(p);

                mainPart.Document.Save();
            }

            var fake = new FakeTranslator(_ =>
                "<g2>proje ayarlarını</g2> kalıcı olarak kaydetmek için <g1>Kaydet</g1>’e tıklayın.");
            var service = new TranslationService(fake);
            var translator = new DocxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            using (var doc = WordprocessingDocument.Open(tempOutput, false))
            {
                var body = doc.MainDocumentPart!.Document.Body!;
                var paragraphs = body.Elements<Paragraph>().ToList();
                Assert.Single(paragraphs);

                var runs = paragraphs[0].Elements<Run>().ToList();
                Assert.Equal(4, runs.Count);

                // Run 0: Italic, "proje ayarlarını"
                Assert.NotNull(runs[0].RunProperties?.Italic);
                Assert.Null(runs[0].RunProperties?.Bold);
                Assert.Equal("proje ayarlarını", runs[0].InnerText);

                // Run 1: Normal, " kalıcı olarak kaydetmek için "
                Assert.Null(runs[1].RunProperties?.Italic);
                Assert.Null(runs[1].RunProperties?.Bold);
                Assert.Equal(" kalıcı olarak kaydetmek için ", runs[1].InnerText);

                // Run 2: Bold, "Kaydet"
                Assert.NotNull(runs[2].RunProperties?.Bold);
                Assert.Null(runs[2].RunProperties?.Italic);
                Assert.Equal("Kaydet", runs[2].InnerText);

                // Run 3: Normal, "’e tıklayın."
                Assert.Null(runs[3].RunProperties?.Italic);
                Assert.Null(runs[3].RunProperties?.Bold);
                Assert.Equal("’e tıklayın.", runs[3].InnerText);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
