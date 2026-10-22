using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Translation;
using Xunit;
using D = DocumentFormat.OpenXml.Drawing;

namespace HyTranslate.Core.Tests;

public class PptxDocumentTranslatorTests
{
    [Fact]
    public async Task Pptx_TranslatesText_PreservesShapeCount()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pptx_in_{Guid.NewGuid()}.pptx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pptx_out_{Guid.NewGuid()}.pptx");

        try
        {
            // Build minimal PPTX in code
            using (var doc = PresentationDocument.Create(tempInput, PresentationDocumentType.Presentation))
            {
                var presPart = doc.AddPresentationPart();
                presPart.Presentation = new Presentation();

                var slidePart = presPart.AddNewPart<SlidePart>();
                var shapeTree = new ShapeTree();

                // Shape 1: Title
                var shape1 = new Shape(
                    new NonVisualShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "Title" },
                        new NonVisualShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new ShapeProperties(),
                    new TextBody(
                        new D.BodyProperties(),
                        new D.ListStyle(),
                        new D.Paragraph(
                            new D.Run(new D.Text("Quarterly Review"))
                        )
                    )
                );

                // Shape 2: Subtitle
                var shape2 = new Shape(
                    new NonVisualShapeProperties(
                        new NonVisualDrawingProperties { Id = 2, Name = "Subtitle" },
                        new NonVisualShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new ShapeProperties(),
                    new TextBody(
                        new D.BodyProperties(),
                        new D.ListStyle(),
                        new D.Paragraph(
                            new D.Run(new D.Text("Project Updates"))
                        )
                    )
                );

                shapeTree.AppendChild(shape1);
                shapeTree.AppendChild(shape2);

                slidePart.Slide = new Slide(new CommonSlideData(shapeTree));
                slidePart.Slide.Save();

                var slideIdList = presPart.Presentation.AppendChild(new SlideIdList());
                slideIdList.AppendChild(new SlideId { Id = 256, RelationshipId = presPart.GetIdOfPart(slidePart) });
                presPart.Presentation.Save();
            }

            int initialShapes;
            using (var doc = PresentationDocument.Open(tempInput, false))
            {
                var slide = doc.PresentationPart!.SlideParts.First().Slide;
                initialShapes = slide.Descendants<Shape>().Count();
            }

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new PptxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            using (var doc = PresentationDocument.Open(tempOutput, false))
            {
                var slide = doc.PresentationPart!.SlideParts.First().Slide;
                int afterShapes = slide.Descendants<Shape>().Count();

                Assert.Equal(initialShapes, afterShapes);

                var texts = slide.Descendants<D.Text>().Select(t => t.Text).ToList();
                Assert.Contains("TR:Quarterly Review", texts);
                Assert.Contains("TR:Project Updates", texts);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Pptx_TranslatesRichRuns_PreservesRunProperties()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"pptx_rich_in_{Guid.NewGuid()}.pptx");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"pptx_rich_out_{Guid.NewGuid()}.pptx");

        try
        {
            using (var doc = PresentationDocument.Create(tempInput, PresentationDocumentType.Presentation))
            {
                var presPart = doc.AddPresentationPart();
                presPart.Presentation = new Presentation();

                var slidePart = presPart.AddNewPart<SlidePart>();
                var shapeTree = new ShapeTree();

                var p = new D.Paragraph();
                var boldRun = new D.Run(new D.RunProperties { Bold = true }, new D.Text("Important: "));
                var normalRun = new D.Run(new D.Text("Do not unplug device."));
                p.AppendChild(boldRun);
                p.AppendChild(normalRun);

                var shape = new Shape(
                    new NonVisualShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "TextBox" },
                        new NonVisualShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new ShapeProperties(),
                    new TextBody(
                        new D.BodyProperties(),
                        new D.ListStyle(),
                        p
                    )
                );

                shapeTree.AppendChild(shape);
                slidePart.Slide = new Slide(new CommonSlideData(shapeTree));
                slidePart.Slide.Save();

                var slideIdList = presPart.Presentation.AppendChild(new SlideIdList());
                slideIdList.AppendChild(new SlideId { Id = 256, RelationshipId = presPart.GetIdOfPart(slidePart) });
                presPart.Presentation.Save();
            }

            var fake = new FakeTranslator("TR:");
            var service = new TranslationService(fake);
            var translator = new PptxDocumentTranslator(service);

            await translator.TranslateAsync(tempInput, tempOutput, new TranslateOptions("tr"));

            using (var doc = PresentationDocument.Open(tempOutput, false))
            {
                var slide = doc.PresentationPart!.SlideParts.First().Slide;
                var runs = slide.Descendants<D.Run>().ToList();
                Assert.Equal(2, runs.Count);

                Assert.True(runs[0].RunProperties?.Bold?.Value ?? false);
                Assert.Equal("TR:Important: ", runs[0].InnerText);

                Assert.False(runs[1].RunProperties?.Bold?.Value ?? false);
                Assert.Equal("TR:Do not unplug device.", runs[1].InnerText);
            }
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
