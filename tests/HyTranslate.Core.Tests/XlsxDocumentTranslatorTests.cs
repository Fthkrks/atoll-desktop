using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Translation;
using Xunit;

namespace HyTranslate.Core.Tests;

public class XlsxDocumentTranslatorTests
{
    [Fact]
    public async Task Xlsx_TranslatesSharedStrings_LeavesFormulasAndNumbersUntouched()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"xlsx_in_{Guid.NewGuid()}.xlsx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"xlsx_out_{Guid.NewGuid()}.xlsx");

        try
        {
            // Build minimal Excel file
            using (var doc = SpreadsheetDocument.Create(tempInput, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                // Add SharedStringTablePart
                var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
                sstPart.SharedStringTable = new SharedStringTable();
                sstPart.SharedStringTable.AppendChild(new SharedStringItem(new Text("Monthly Report Title")));
                sstPart.SharedStringTable.Save();

                // Add WorksheetPart
                var wsPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();

                var row1 = new Row { RowIndex = 1 };

                // Cell A1: Shared string
                var cellA1 = new Cell
                {
                    CellReference = "A1",
                    DataType = CellValues.SharedString,
                    CellValue = new CellValue("0")
                };

                // Cell B1: Number
                var cellB1 = new Cell
                {
                    CellReference = "B1",
                    DataType = CellValues.Number,
                    CellValue = new CellValue("999.50")
                };

                // Cell C1: Formula
                var cellC1 = new Cell
                {
                    CellReference = "C1",
                    CellFormula = new CellFormula("SUM(B1:B10)"),
                    CellValue = new CellValue("999.50")
                };

                row1.AppendChild(cellA1);
                row1.AppendChild(cellB1);
                row1.AppendChild(cellC1);
                sheetData.AppendChild(row1);

                wsPart.Worksheet = new Worksheet(sheetData);
                wsPart.Worksheet.Save();

                var sheets = doc.WorkbookPart!.Workbook!.AppendChild(new Sheets());
                sheets.AppendChild(new Sheet
                {
                    Id = doc.WorkbookPart!.GetIdOfPart(wsPart),
                    SheetId = 1,
                    Name = "Sheet1"
                });

                workbookPart.Workbook.Save();
            }

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new XlsxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            // Verify
            using (var doc = SpreadsheetDocument.Open(tempOutput, false))
            {
                var sstPart = doc.WorkbookPart!.SharedStringTablePart;
                Assert.NotNull(sstPart);
                var firstItem = sstPart.SharedStringTable.Elements<SharedStringItem>().First();
                Assert.Equal("TR:Monthly Report Title", firstItem.Text?.Text);

                var ws = doc.WorkbookPart.WorksheetParts.First().Worksheet;
                var cellB1 = ws.Descendants<Cell>().First(c => c.CellReference == "B1");
                Assert.Equal("999.50", cellB1.CellValue?.Text);
                Assert.Equal(CellValues.Number, cellB1.DataType?.Value);

                var cellC1 = ws.Descendants<Cell>().First(c => c.CellReference == "C1");
                Assert.Equal("SUM(B1:B10)", cellC1.CellFormula?.Text);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Xlsx_TranslatesShortCells_CommentAndUnits()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"xlsx_short_{Guid.NewGuid()}.xlsx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"xlsx_short_out_{Guid.NewGuid()}.xlsx");

        try
        {
            using (var doc = SpreadsheetDocument.Create(tempInput, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
                sstPart.SharedStringTable = new SharedStringTable();
                sstPart.SharedStringTable.AppendChild(new SharedStringItem(new Text("Comment")));
                sstPart.SharedStringTable.AppendChild(new SharedStringItem(new Text("Units")));
                sstPart.SharedStringTable.Save();

                var wsPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();

                var row1 = new Row { RowIndex = 1 };
                row1.AppendChild(new Cell { CellReference = "A1", DataType = CellValues.SharedString, CellValue = new CellValue("0") });
                row1.AppendChild(new Cell { CellReference = "B1", DataType = CellValues.SharedString, CellValue = new CellValue("1") });
                sheetData.AppendChild(row1);

                wsPart.Worksheet = new Worksheet(sheetData);
                wsPart.Worksheet.Save();

                var sheets = doc.WorkbookPart!.Workbook!.AppendChild(new Sheets());
                sheets.AppendChild(new Sheet
                {
                    Id = doc.WorkbookPart!.GetIdOfPart(wsPart),
                    SheetId = 1,
                    Name = "Sheet1"
                });

                workbookPart.Workbook.Save();
            }

            var fake = new FakeTranslator(t => t switch
            {
                "Comment" => "Yorum",
                "Units" => "Birimler",
                _ => "TR:" + t
            });
            var service = new TranslationService(fake);
            var translator = new XlsxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            using (var doc = SpreadsheetDocument.Open(tempOutput, false))
            {
                var sstPart = doc.WorkbookPart!.SharedStringTablePart;
                Assert.NotNull(sstPart);
                var items = sstPart.SharedStringTable.Elements<SharedStringItem>().ToList();
                Assert.Equal("Yorum", items[0].Text?.Text);
                Assert.Equal("Birimler", items[1].Text?.Text);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Xlsx_TranslatesRichTextSharedStringRuns_PreservesProperties()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"xlsx_rich_{Guid.NewGuid()}.xlsx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"xlsx_rich_out_{Guid.NewGuid()}.xlsx");

        try
        {
            using (var doc = SpreadsheetDocument.Create(tempInput, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
                sstPart.SharedStringTable = new SharedStringTable();

                // Rich text shared string: Run 1 bold, Run 2 normal
                var richItem = new SharedStringItem();
                richItem.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Run(
                    new DocumentFormat.OpenXml.Spreadsheet.RunProperties(new DocumentFormat.OpenXml.Spreadsheet.Bold()),
                    new DocumentFormat.OpenXml.Spreadsheet.Text("Warning: ")
                ));
                richItem.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Run(
                    new DocumentFormat.OpenXml.Spreadsheet.Text("Do not turn off.")
                ));
                sstPart.SharedStringTable.AppendChild(richItem);
                sstPart.SharedStringTable.Save();

                var wsPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                var row1 = new Row { RowIndex = 1 };
                row1.AppendChild(new Cell { CellReference = "A1", DataType = CellValues.SharedString, CellValue = new CellValue("0") });
                sheetData.AppendChild(row1);

                wsPart.Worksheet = new Worksheet(sheetData);
                wsPart.Worksheet.Save();

                var sheets = doc.WorkbookPart!.Workbook!.AppendChild(new Sheets());
                sheets.AppendChild(new Sheet
                {
                    Id = doc.WorkbookPart!.GetIdOfPart(wsPart),
                    SheetId = 1,
                    Name = "Sheet1"
                });

                workbookPart.Workbook.Save();
            }

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new XlsxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            using (var doc = SpreadsheetDocument.Open(tempOutput, false))
            {
                var sstPart = doc.WorkbookPart!.SharedStringTablePart;
                Assert.NotNull(sstPart);
                var item = sstPart.SharedStringTable.Elements<SharedStringItem>().First();
                var runs = item.Elements<DocumentFormat.OpenXml.Spreadsheet.Run>().ToList();

                Assert.Equal(2, runs.Count);
                Assert.True(runs[0].RunProperties?.Elements<DocumentFormat.OpenXml.Spreadsheet.Bold>().Any());
                Assert.Equal("TR:Warning: ", runs[0].Text?.Text);

                Assert.True(runs[1].RunProperties?.Elements<DocumentFormat.OpenXml.Spreadsheet.Bold>().Any() != true);
                Assert.Equal("TR:Do not turn off.", runs[1].Text?.Text);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
