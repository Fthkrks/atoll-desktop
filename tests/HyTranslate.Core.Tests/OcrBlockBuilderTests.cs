using System.Collections.Generic;
using System.Linq;
using HyTranslate.Core.Ocr;
using Xunit;

namespace HyTranslate.Core.Tests;

public class OcrBlockBuilderTests
{
    [Fact]
    public void FilterLines_DropsOverlappingAndNoise_KeepsValidLines()
    {
        // Word in text layer
        var textLayerWords = new List<WordBox>
        {
            new WordBox(50, 100, 60, 20)
        };

        var lines = new List<OcrTextLine>
        {
            // Overlaps text layer word (> 30% of word area)
            new OcrTextLine("Header text layer word", 50, 100, 120, 20),
            // Fewer than 3 letters
            new OcrTextLine("30 12", 50, 200, 50, 15),
            // Only 1 letter
            new OcrTextLine("s", 50, 250, 20, 15),
            // Valid OCR line in an image
            new OcrTextLine("DES Designed at IBM", 50, 400, 200, 15)
        };

        var filtered = OcrBlockBuilder.FilterLines(lines, textLayerWords);

        Assert.Single(filtered);
        Assert.Equal("DES Designed at IBM", filtered[0].Text);
    }

    [Fact]
    public void Group_ThreeLinesSameLeftEdgeNormalSpacing_ProducesSingleBlockJoinedWithSpaces()
    {
        var lines = new List<OcrTextLine>
        {
            new OcrTextLine("Line one", 50, 100, 100, 12),
            new OcrTextLine("Line two", 50, 116, 100, 12),
            new OcrTextLine("Line three", 50, 132, 100, 12)
        };

        var blocks = OcrBlockBuilder.Group(lines);

        Assert.Single(blocks);
        Assert.Equal("Line one Line two Line three", blocks[0].Text);
        Assert.Equal(3, blocks[0].LineCount);
    }

    [Fact]
    public void Group_LineFarBelow_ProducesSeparateBlock()
    {
        var lines = new List<OcrTextLine>
        {
            new OcrTextLine("Header line", 50, 100, 100, 12),
            new OcrTextLine("Footer line far below", 50, 300, 100, 12)
        };

        var blocks = OcrBlockBuilder.Group(lines);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Header line", blocks[0].Text);
        Assert.Equal("Footer line far below", blocks[1].Text);
    }

    [Fact]
    public void Group_CenteredLinesOfDifferentWidths_ProducesSingleBlock()
    {
        // Line 1: center = 250 + 50 = 300
        var line1 = new OcrTextLine("Short centered title", 250, 100, 100, 14);
        // Line 2: center = 200 + 100 = 300, gap = 118 - 114 = 4
        var line2 = new OcrTextLine("A much wider centered subtitle under it", 200, 118, 200, 14);

        var blocks = OcrBlockBuilder.Group(new[] { line1, line2 });

        Assert.Single(blocks);
        Assert.Equal("Short centered title A much wider centered subtitle under it", blocks[0].Text);
        Assert.Equal(2, blocks[0].LineCount);
    }

    [Fact]
    public void Group_HyphenJoining_DropsHyphenWhenNextLineStartsWithLowercase()
    {
        var lines = new List<OcrTextLine>
        {
            new OcrTextLine("algo-", 50, 100, 80, 12),
            new OcrTextLine("rithm", 50, 116, 80, 12)
        };

        var blocks = OcrBlockBuilder.Group(lines);

        Assert.Single(blocks);
        Assert.Equal("algorithm", blocks[0].Text);
    }

    [Fact]
    public void Group_HyphenJoining_PreservesHyphenWhenNextLineStartsWithUppercase()
    {
        var lines = new List<OcrTextLine>
        {
            new OcrTextLine("Multi-", 50, 100, 80, 12),
            new OcrTextLine("Vendor", 50, 116, 80, 12)
        };

        var blocks = OcrBlockBuilder.Group(lines);

        Assert.Single(blocks);
        Assert.Equal("Multi- Vendor", blocks[0].Text);
    }

    [Fact]
    public void EstimateFontSize_ThreeLinesWithTops100_120_140_ReturnsApprox16_7()
    {
        var lines = new List<OcrTextLine>
        {
            new OcrTextLine("First line", 50, 100, 100, 15),
            new OcrTextLine("Second line", 50, 120, 100, 15),
            new OcrTextLine("Third line", 50, 140, 100, 15)
        };

        double fontSize = OcrBlockBuilder.EstimateFontSize(lines);
        Assert.InRange(fontSize, 16.6, 16.7);

        var blocks = OcrBlockBuilder.Group(lines);
        Assert.Single(blocks);
        Assert.InRange(blocks[0].FontSize, 16.6, 16.7);
    }

    [Fact]
    public void EstimateFontSize_SingleLine_Returns85PercentOfLineHeight()
    {
        var lines = new List<OcrTextLine>
        {
            new OcrTextLine("Single line", 50, 100, 100, 20)
        };

        double fontSize = OcrBlockBuilder.EstimateFontSize(lines);
        Assert.Equal(17.0, fontSize); // 20 * 0.85

        var blocks = OcrBlockBuilder.Group(lines);
        Assert.Single(blocks);
        Assert.Equal(17.0, blocks[0].FontSize);
    }

    [Fact]
    public void DetectAlignment_ThreeLinesDifferentWidthsSameCenter_ReturnsCenter()
    {
        // Line 1: center = 150 + 50 = 200
        var line1 = new OcrTextLine("Short line", 150, 100, 100, 12);
        // Line 2: center = 120 + 80 = 200
        var line2 = new OcrTextLine("Medium width line", 120, 116, 160, 12);
        // Line 3: center = 90 + 110 = 200
        var line3 = new OcrTextLine("Longest line of the three", 90, 132, 220, 12);

        var lines = new List<OcrTextLine> { line1, line2, line3 };

        var alignment = OcrBlockBuilder.DetectAlignment(lines);
        Assert.Equal(TextAlign.Center, alignment);

        var blocks = OcrBlockBuilder.Group(lines);
        Assert.Single(blocks);
        Assert.Equal(TextAlign.Center, blocks[0].Alignment);
    }

    [Fact]
    public void DetectAlignment_ThreeLinesSameLeftEdge_ReturnsLeft()
    {
        var line1 = new OcrTextLine("Short line", 50, 100, 100, 12);
        var line2 = new OcrTextLine("Medium width line", 50, 116, 160, 12);
        var line3 = new OcrTextLine("Longest line of the three", 50, 132, 220, 12);

        var lines = new List<OcrTextLine> { line1, line2, line3 };

        var alignment = OcrBlockBuilder.DetectAlignment(lines);
        Assert.Equal(TextAlign.Left, alignment);

        var blocks = OcrBlockBuilder.Group(lines);
        Assert.Single(blocks);
        Assert.Equal(TextAlign.Left, blocks[0].Alignment);
    }

    [Fact]
    public void DetectAlignment_SingleLine_ReturnsLeft()
    {
        var line = new OcrTextLine("Single line", 150, 100, 100, 12);
        var lines = new List<OcrTextLine> { line };

        var alignment = OcrBlockBuilder.DetectAlignment(lines);
        Assert.Equal(TextAlign.Left, alignment);

        var blocks = OcrBlockBuilder.Group(lines);
        Assert.Single(blocks);
        Assert.Equal(TextAlign.Left, blocks[0].Alignment);
    }
}
