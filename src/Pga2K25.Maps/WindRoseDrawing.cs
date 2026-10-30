using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// Draws a <see cref="WindRose"/>: a petal per direction, stacked by speed band from light (centre) to strong
/// (rim), dashed rings of percent of hours, the calm share in the middle and an mph key underneath.
/// </summary>
public static class WindRoseDrawing
{
    /// <summary>Band shades from <paramref name="paper"/> towards <paramref name="ink"/>, lightest first.</summary>
    public static SKColor[] Shades(SKColor ink, SKColor paper) =>
        Enumerable.Range(0, 5).Select(b => Mix(paper, ink, 0.17f + b * 0.2f)).ToArray();

    /// <summary>Draws the rose fitted in <paramref name="box"/>, with the mph key along the bottom when <paramref name="key"/>.</summary>
    public static void Draw(SKCanvas canvas, SKRect box, WindRose rose, SKTypeface face, SKColor ink, SKColor paper, float unit, bool key = true)
    {
        float keyH = key ? 18 * unit : 0, labelPad = 15 * unit;
        float R = Math.Min(box.Width, box.Height - keyH) / 2 - labelPad;
        // Rose and key centred together in the box.
        float used = 2 * (R + labelPad) + keyH;
        var c = new SKPoint(box.MidX, box.Top + (box.Height - used) / 2 + labelPad + R);
        float hole = R * 0.16f;
        var shades = Shades(ink, paper);

        double max = Enumerable.Range(0, 16).Max(rose.SectorTotal);
        double step = max > .2 ? .05 : max > .1 ? .03 : .02, top = Math.Max(step, Math.Ceiling(max / step - 1e-9) * step);
        float Rad(double f) => hole + (R - hole) * (float)(f / top);
        SKPoint P(double deg, float r) { double a = deg * Math.PI / 180; return new(c.X + (float)Math.Sin(a) * r, c.Y - (float)Math.Cos(a) * r); }

        using var ring = new SKPaint { Color = ink.WithAlpha(130), StrokeWidth = 0.5f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke,
                                       PathEffect = SKPathEffect.CreateDash([2.5f * unit, 3 * unit], 0) };
        for (double f = step; f <= top + 1e-9; f += step) canvas.DrawCircle(c, Rad(f), ring);
        using var spoke = new SKPaint { Color = ink.WithAlpha(90), StrokeWidth = 0.4f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke };
        for (int i = 0; i < 16; i++) canvas.DrawLine(P(i * 22.5, hole), P(i * 22.5, R + 4 * unit), spoke);

        using var edge = new SKPaint { Color = ink, StrokeWidth = 0.5f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke };
        for (int i = 0; i < 16; i++)
        {
            double f0 = 0;
            for (int b = 0; b < 5; b++)
            {
                double v = rose.Sectors[i][b];
                if (v <= 0) continue;
                float r0 = Rad(f0), r1 = Rad(f0 + v);
                using var path = Wedge(c, r0, r1, i * 22.5 - 9, i * 22.5 + 9);
                using var fill = new SKPaint { Color = shades[b], IsAntialias = true };
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, edge);
                f0 += v;
            }
        }

        // Ring values up the NNE spoke from the second ring out, haloed so petals don't strike through them.
        float small = 6.5f * unit;
        for (double f = 2 * step; f <= top + 1e-9; f += step)   // the first ring sits among the short petals
        {
            var at = P(11.25, Rad(f));
            Text(canvas, $"{f * 100:0}%", at.X + 2 * unit, at.Y - 1.5f * unit, face, small, ink.WithAlpha(200), paper);
        }
        for (int i = 0; i < 16; i += 2)
        {
            bool card = i % 4 == 0;
            var at = P(i * 22.5, R + (card ? 9 : 8) * unit);
            float size = (card ? 9.5f : 6.5f) * unit;
            Text(canvas, WindRose.SectorNames[i], at.X, at.Y + size * 0.36f, face, size, ink, null, SKTextAlign.Center);
        }

        using (var bg = new SKPaint { Color = paper, IsAntialias = true }) canvas.DrawCircle(c, hole, bg);
        using (var rim = new SKPaint { Color = ink, StrokeWidth = 0.8f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke }) canvas.DrawCircle(c, hole, rim);
        float calmSize = Math.Min(9 * unit, hole * 0.62f);
        Text(canvas, $"{rose.Calm * 100:0}%", c.X, c.Y + calmSize * 0.1f, face, calmSize, ink, null, SKTextAlign.Center);
        Text(canvas, "CALM", c.X, c.Y + calmSize * 0.95f, face, calmSize * 0.55f, ink, null, SKTextAlign.Center);

        if (!key) return;
        float kw = 22 * unit, gap = 6 * unit, y = c.Y + R + labelPad + 12 * unit;
        float total = Measure("MPH", face, 6.5f * unit) + gap + WindRose.BandLabels.Sum(l => kw * 0.55f + 3 * unit + Measure(l, face, 6.5f * unit) + gap);
        float x = box.MidX - total / 2;
        Text(canvas, "MPH", x, y, face, 6.5f * unit, ink, null);
        x += Measure("MPH", face, 6.5f * unit) + gap;
        for (int b = 0; b < 5; b++)
        {
            var sw = new SKRect(x, y - 6.5f * unit, x + kw * 0.55f, y);
            using (var f = new SKPaint { Color = shades[b] }) canvas.DrawRect(sw, f);
            canvas.DrawRect(sw, edge);
            x += kw * 0.55f + 3 * unit;
            Text(canvas, WindRose.BandLabels[b], x, y, face, 6.5f * unit, ink, null);
            x += Measure(WindRose.BandLabels[b], face, 6.5f * unit) + gap;
        }
    }

    static SKPath Wedge(SKPoint c, float r0, float r1, double a0, double a1)
    {
        using var b = new SKPathBuilder();
        // Skia arcs start at 3 o'clock and run clockwise; compass bearings start at 12.
        b.ArcTo(new SKRect(c.X - r1, c.Y - r1, c.X + r1, c.Y + r1), (float)(a0 - 90), (float)(a1 - a0), true);
        b.ArcTo(new SKRect(c.X - r0, c.Y - r0, c.X + r0, c.Y + r0), (float)(a1 - 90), (float)(a0 - a1), false);
        b.Close();
        return b.Detach();
    }

    static float Measure(string text, SKTypeface face, float size) { using var f = new SKFont(face, size); return f.MeasureText(text); }

    static void Text(SKCanvas canvas, string text, float x, float y, SKTypeface face, float size, SKColor colour, SKColor? halo,
        SKTextAlign align = SKTextAlign.Left)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        if (halo is SKColor h)
        {
            using var hp = new SKPaint { Color = h.WithAlpha(220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.35f, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawText(text, x, y, align, font, hp);
        }
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }

    static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));
}
