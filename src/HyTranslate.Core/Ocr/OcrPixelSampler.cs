using System;
using System.Collections.Generic;
using System.Linq;

namespace HyTranslate.Core.Ocr;

public sealed class OcrPixelSampler : IOcrPixelSampler
{
    private readonly byte[] _bgraBytes;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly double _scalePtToPx;

    public OcrPixelSampler(byte[] bgraBytes, int pixelWidth, int pixelHeight, double pageWidthPt)
    {
        _bgraBytes = bgraBytes ?? throw new ArgumentNullException(nameof(bgraBytes));
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _scalePtToPx = pageWidthPt > 0 ? pixelWidth / pageWidthPt : 1.0;
    }

    public (byte R, byte G, byte B) BackgroundColor(double left, double top, double width, double height)
    {
        int pxLeft = (int)Math.Round(left * _scalePtToPx);
        int pxTop = (int)Math.Round(top * _scalePtToPx);
        int pxWidth = Math.Max(1, (int)Math.Round(width * _scalePtToPx));
        int pxHeight = Math.Max(1, (int)Math.Round(height * _scalePtToPx));
        int pxRight = pxLeft + pxWidth;
        int pxBottom = pxTop + pxHeight;

        int minX = Math.Max(0, pxLeft - 3);
        int maxX = Math.Min(_pixelWidth - 1, pxRight + 2);
        int minY = Math.Max(0, pxTop - 3);
        int maxY = Math.Min(_pixelHeight - 1, pxBottom + 2);

        var rList = new List<byte>();
        var gList = new List<byte>();
        var bList = new List<byte>();

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                // Must be outside the inner rectangle [pxLeft, pxRight) x [pxTop, pxBottom)
                if (x < pxLeft || x >= pxRight || y < pxTop || y >= pxBottom)
                {
                    int offset = (y * _pixelWidth + x) * 4;
                    if (offset + 2 < _bgraBytes.Length)
                    {
                        bList.Add(_bgraBytes[offset]);
                        gList.Add(_bgraBytes[offset + 1]);
                        rList.Add(_bgraBytes[offset + 2]);
                    }
                }
            }
        }

        if (rList.Count == 0)
        {
            return (255, 255, 255);
        }

        rList.Sort();
        gList.Sort();
        bList.Sort();

        int mid = rList.Count / 2;
        return (rList[mid], gList[mid], bList[mid]);
    }

    public (byte R, byte G, byte B) TextColor(double left, double top, double width, double height)
    {
        int pxLeft = (int)Math.Round(left * _scalePtToPx);
        int pxTop = (int)Math.Round(top * _scalePtToPx);
        int pxWidth = Math.Max(1, (int)Math.Round(width * _scalePtToPx));
        int pxHeight = Math.Max(1, (int)Math.Round(height * _scalePtToPx));
        int pxRight = pxLeft + pxWidth;
        int pxBottom = pxTop + pxHeight;

        int minX = Math.Max(0, pxLeft);
        int maxX = Math.Min(_pixelWidth - 1, pxRight - 1);
        int minY = Math.Max(0, pxTop);
        int maxY = Math.Min(_pixelHeight - 1, pxBottom - 1);

        var pixels = new List<(byte R, byte G, byte B, double Lum)>();

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int offset = (y * _pixelWidth + x) * 4;
                if (offset + 2 < _bgraBytes.Length)
                {
                    byte b = _bgraBytes[offset];
                    byte g = _bgraBytes[offset + 1];
                    byte r = _bgraBytes[offset + 2];
                    double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                    pixels.Add((r, g, b, lum));
                }
            }
        }

        if (pixels.Count == 0)
        {
            return (0, 0, 0);
        }

        pixels.Sort((p1, p2) => p1.Lum.CompareTo(p2.Lum));
        int takeCount = Math.Max(1, (int)Math.Ceiling(pixels.Count * 0.10));
        var darkest = pixels.Take(takeCount).ToList();

        double avgR = darkest.Average(p => p.R);
        double avgG = darkest.Average(p => p.G);
        double avgB = darkest.Average(p => p.B);

        byte textR = (byte)Math.Clamp(Math.Round(avgR), 0, 255);
        byte textG = (byte)Math.Clamp(Math.Round(avgG), 0, 255);
        byte textB = (byte)Math.Clamp(Math.Round(avgB), 0, 255);

        var (bgR, bgG, bgB) = BackgroundColor(left, top, width, height);
        double bgLum = 0.299 * bgR + 0.587 * bgG + 0.114 * bgB;
        double textLum = 0.299 * textR + 0.587 * textG + 0.114 * textB;
        double contrast = Math.Abs(bgLum - textLum);

        if (contrast < 60.0)
        {
            double contrastWithBlack = bgLum;
            double contrastWithWhite = 255.0 - bgLum;
            return contrastWithBlack >= contrastWithWhite ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255);
        }

        return (textR, textG, textB);
    }
}
