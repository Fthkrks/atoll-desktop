using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HyTranslate.Core.Ocr;

public enum TextAlign
{
    Left,
    Center
}

public sealed record WordBox(double Left, double Top, double Width, double Height);

public sealed record OcrBlock(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height,
    double FontSize,
    int LineCount,
    TextAlign Alignment = TextAlign.Left);

public static class OcrBlockBuilder
{
    public static WordBox FromPdfPigWord(UglyToad.PdfPig.Content.Word word, double pageHeightPt)
    {
        double left = word.BoundingBox.Left;
        double top = pageHeightPt - word.BoundingBox.Top;
        double width = word.BoundingBox.Width;
        double height = word.BoundingBox.Height;
        return new WordBox(left, top, width, height);
    }

    public static bool HasValidText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        int letterCount = text.Count(char.IsLetter);
        if (letterCount < 3) return false;

        int nonSpaceCount = text.Count(c => !char.IsWhiteSpace(c));
        if (nonSpaceCount == 0) return false;

        return ((double)letterCount / nonSpaceCount) >= 0.50;
    }

    public static bool OverlapsAnyWord(OcrTextLine line, IEnumerable<WordBox>? textLayerWords)
    {
        if (textLayerWords == null) return false;

        double lineArea = line.Width * line.Height;
        if (lineArea <= 0) return false;

        foreach (var word in textLayerWords)
        {
            double wordArea = word.Width * word.Height;
            if (wordArea <= 0) continue;

            double x1 = Math.Max(line.Left, word.Left);
            double y1 = Math.Max(line.Top, word.Top);
            double x2 = Math.Min(line.Left + line.Width, word.Left + word.Width);
            double y2 = Math.Min(line.Top + line.Height, word.Top + word.Height);

            if (x2 > x1 && y2 > y1)
            {
                double intersectArea = (x2 - x1) * (y2 - y1);
                if ((intersectArea / lineArea > 0.30) || (intersectArea / wordArea > 0.30))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IReadOnlyList<OcrTextLine> FilterLines(
        IEnumerable<OcrTextLine> lines,
        IEnumerable<WordBox>? textLayerWords = null)
    {
        if (lines == null) return Array.Empty<OcrTextLine>();

        var wordList = textLayerWords?.ToList();
        var result = new List<OcrTextLine>();

        foreach (var line in lines)
        {
            if (!HasValidText(line.Text))
                continue;

            if (OverlapsAnyWord(line, wordList))
                continue;

            result.Add(line);
        }

        return result;
    }

    public static IReadOnlyList<OcrBlock> Group(IReadOnlyList<OcrTextLine> lines)
    {
        if (lines == null || lines.Count == 0)
            return Array.Empty<OcrBlock>();

        var sorted = lines.OrderBy(l => l.Top).ToList();
        var groups = new List<List<OcrTextLine>>();

        foreach (var line in sorted)
        {
            if (groups.Count == 0)
            {
                groups.Add(new List<OcrTextLine> { line });
                continue;
            }

            var currentGroup = groups[^1];
            var prev = currentGroup[^1];

            double medianHeight = GetMedian(currentGroup.Select(l => l.Height));
            double verticalGap = line.Top - (prev.Top + prev.Height);

            bool gapOk = verticalGap < 0.8 * medianHeight;
            bool heightOk = prev.Height > 0 && Math.Abs(line.Height - prev.Height) <= 0.35 * prev.Height;

            double hOverlap = Math.Min(line.Left + line.Width, prev.Left + prev.Width) - Math.Max(line.Left, prev.Left);
            double leftDiff = Math.Abs(line.Left - prev.Left);
            double lineCenter = line.Left + line.Width / 2.0;
            double prevCenter = prev.Left + prev.Width / 2.0;
            double centerDiff = Math.Abs(lineCenter - prevCenter);

            bool horizontalOk = (hOverlap > 0) || (leftDiff <= 1.5 * prev.Height) || (centerDiff <= 1.5 * prev.Height);

            if (gapOk && heightOk && horizontalOk)
            {
                currentGroup.Add(line);
            }
            else
            {
                groups.Add(new List<OcrTextLine> { line });
            }
        }

        var result = new List<OcrBlock>();
        foreach (var grp in groups)
        {
            string text = JoinLines(grp);
            double left = grp.Min(l => l.Left);
            double top = grp.Min(l => l.Top);
            double right = grp.Max(l => l.Left + l.Width);
            double bottom = grp.Max(l => l.Top + l.Height);
            double width = Math.Max(0, right - left);
            double height = Math.Max(0, bottom - top);

            double fontSize = EstimateFontSize(grp);
            TextAlign alignment = DetectAlignment(grp);

            result.Add(new OcrBlock(text, left, top, width, height, fontSize, grp.Count, alignment));
        }

        return result;
    }

    public static double EstimateFontSize(IReadOnlyList<OcrTextLine> lines)
    {
        if (lines == null || lines.Count == 0) return 10.0;

        if (lines.Count >= 2)
        {
            var sorted = lines.OrderBy(l => l.Top).ToList();
            var pitches = new List<double>();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                pitches.Add(sorted[i + 1].Top - sorted[i].Top);
            }
            double linePitch = GetMedian(pitches);
            if (linePitch > 0)
            {
                return Math.Clamp(linePitch / 1.2, 6.0, 72.0);
            }
        }

        double lineHeight = GetMedian(lines.Select(l => l.Height));
        return Math.Clamp(lineHeight * 0.85, 6.0, 72.0);
    }

    public static TextAlign DetectAlignment(IReadOnlyList<OcrTextLine> lines)
    {
        if (lines == null || lines.Count < 2)
        {
            return TextAlign.Left;
        }

        var lefts = lines.Select(l => l.Left).ToList();
        var centers = lines.Select(l => l.Left + (l.Width / 2.0)).ToList();
        double medianLineHeight = GetMedian(lines.Select(l => l.Height));

        double leftSpread = lefts.Max() - lefts.Min();
        if (leftSpread <= 1.0 * medianLineHeight)
        {
            return TextAlign.Left;
        }

        double sdLeft = StandardDeviation(lefts);
        double sdCenter = StandardDeviation(centers);

        if (sdCenter < 0.5 * sdLeft)
        {
            return TextAlign.Center;
        }

        return TextAlign.Left;
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        if (values == null || values.Count < 2) return 0.0;
        double mean = values.Average();
        double sumSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / values.Count);
    }

    public static string JoinLines(IReadOnlyList<OcrTextLine> lines)
    {
        if (lines.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.Append(lines[0].Text);

        for (int i = 1; i < lines.Count; i++)
        {
            var next = lines[i].Text;
            if (sb.Length > 0 && sb[^1] == '-' && next.Length > 0 && char.IsLower(next[0]))
            {
                sb.Remove(sb.Length - 1, 1);
                sb.Append(next);
            }
            else
            {
                sb.Append(' ');
                sb.Append(next);
            }
        }

        return sb.ToString();
    }

    private static double GetMedian(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0) return 10.0;
        int mid = list.Count / 2;
        if (list.Count % 2 == 1) return list[mid];
        return (list[mid - 1] + list[mid]) / 2.0;
    }
}
