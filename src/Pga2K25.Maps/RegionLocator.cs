using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>Draws a generated <see cref="Region"/>: as a framed locator with the site inset, or as a faint watermark.</summary>
public static class RegionLocator
{
    /// <summary>
    /// A framed, north-up locator in <paramref name="box"/>: the region with the course marked in red, its name, a km
    /// scale, and the <paramref name="site"/> inset (when given) in the bottom-right corner, joined to the mark.
    /// </summary>
    public static void DrawLocator(SKCanvas canvas, SKRect box, Region region, string? name, SKTypeface face,
        SKColor ink, SKColor paper, SKColor red, float unit, SiteInset? site = null, SKColor? seaInk = null)
    {
        float scale = Math.Min(box.Width, box.Height) / 290f;
        var centre = new SKPoint(box.MidX, box.MidY);
        SKPoint T(SKPoint p) => new(centre.X + p.X * scale, centre.Y + p.Y * scale);

        canvas.Save();
        canvas.ClipRect(box);
        using (var bg = new SKPaint { Color = paper }) canvas.DrawRect(box, bg);
        DrawGeometry(canvas, region, T, box, ink, paper, seaInk ?? ink.WithAlpha(140), unit * 0.45f, hatchStep: 3 * unit);
        if (!string.IsNullOrWhiteSpace(name))
            Label(canvas, name.ToUpperInvariant(), T(region.LabelAt), face, 9 * unit, ink, paper, unit);
        var pin = T(region.Pin);
        float half = 4 * unit;
        using (var mark = new SKPaint { Color = red, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f * unit, IsAntialias = true })
            canvas.DrawRect(new SKRect(pin.X - half, pin.Y - half, pin.X + half, pin.Y + half), mark);

        // km scale, top left
        double km = region.KmPerUnit * 290 / Math.Min(box.Width, box.Height) * box.Width;   // km across the box
        double bar = Nice(km / 4);
        float barPx = (float)(bar / km * box.Width);
        using (var s = new SKPaint { Color = ink, StrokeWidth = 1.1f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke })
            canvas.DrawLine(box.Left + 8 * unit, box.Top + 10 * unit, box.Left + 8 * unit + barPx, box.Top + 10 * unit, s);
        Text(canvas, $"{bar:0.#} km", box.Left + 11 * unit + barPx, box.Top + 12.5f * unit, face, 6.5f * unit, ink);

        if (site is not null)
        {
            float side = Math.Min(box.Width, box.Height) * 0.38f;
            var inset = new SKRect(box.Right - side - 6 * unit, box.Bottom - side - 6 * unit, box.Right - 6 * unit, box.Bottom - 6 * unit);
            using (var lead = new SKPaint { Color = red, StrokeWidth = 0.6f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke,
                                            PathEffect = SKPathEffect.CreateDash([3 * unit, 2 * unit], 0) })
            {
                canvas.DrawLine(pin.X + half, pin.Y - half, inset.Left, inset.Top, lead);
                canvas.DrawLine(pin.X + half, pin.Y + half, inset.Left, inset.Bottom, lead);
            }
            site.Draw(canvas, inset, ink, paper, red, unit * 0.7f, seaInk);
        }
        canvas.Restore();
        using var frame = new SKPaint { Color = ink, StrokeWidth = 0.9f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(box, frame);
    }

    /// <summary>
    /// The region faint behind a map: the course's mark lands on <paramref name="courseAt"/>, the region spans about
    /// <paramref name="span"/> page units, clipped to <paramref name="clip"/>, at <paramref name="alpha"/> strength.
    /// </summary>
    public static void DrawWatermark(SKCanvas canvas, SKRect clip, SKPoint courseAt, float span, Region region, string? name,
        SKTypeface face, SKColor ink, SKColor paper, float unit, byte alpha = 34)
    {
        float scale = span / 290f;
        SKPoint T(SKPoint p) => new(courseAt.X + (p.X - region.Pin.X) * scale, courseAt.Y + (p.Y - region.Pin.Y) * scale);
        canvas.Save();
        canvas.ClipRect(clip);
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        canvas.SaveLayer(layer);
        DrawGeometry(canvas, region, T, clip, ink.WithAlpha(150), paper, ink.WithAlpha(110), unit * scale / 2.6f, hatchStep: 14 * unit);
        if (!string.IsNullOrWhiteSpace(name) && region.SeaAround)
            Text(canvas, name.ToUpperInvariant(), T(region.LabelAt).X, T(region.LabelAt).Y, face, 26 * unit, ink, SKTextAlign.Center, tracking: 9 * unit);
        canvas.Restore();
        canvas.Restore();
    }

    static void DrawGeometry(SKCanvas canvas, Region region, Func<SKPoint, SKPoint> T, SKRect area, SKColor ink, SKColor paper,
        SKColor seaInk, float w, float hatchStep)
    {
        using var hatch = new SKPaint { Color = seaInk, StrokeWidth = Math.Max(0.4f, hatchStep / 6), IsAntialias = true, Style = SKPaintStyle.Stroke };
        void Hatch(SKRect r) { for (float y = r.Top + hatchStep / 2; y < r.Bottom; y += hatchStep) canvas.DrawLine(r.Left, y, r.Right, y, hatch); }
        SKPath Path(List<SKPoint> pts, bool close)
        {
            using var b = new SKPathBuilder();
            b.MoveTo(T(pts[0]));
            foreach (var p in pts.Skip(1)) b.LineTo(T(p));
            if (close) b.Close();
            return b.Detach();
        }
        using var edge = new SKPaint { Color = ink, StrokeWidth = 1.3f * w, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
        using var fill = new SKPaint { Color = paper, IsAntialias = true };

        if (region.SeaAround)
        {
            Hatch(area);
            foreach (var land in region.Land)
            {
                using var path = Path(land, true);
                // Ripple lines round the coast: wide faint strokes, each cut back by paper, as on old charts.
                foreach (var (s, a) in new[] { (12f, 0.25f), (7.5f, 0.4f), (3.5f, 0.55f) })
                {
                    using var ring = new SKPaint { Color = ink.WithAlpha((byte)(255 * a)), StrokeWidth = (s + 1.2f) * w, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
                    using var cut = new SKPaint { Color = paper, StrokeWidth = s * w, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
                    canvas.DrawPath(path, ring);
                    canvas.DrawPath(path, cut);
                }
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, edge);
            }
        }
        foreach (var water in region.Water)
        {
            using var path = Path(water, true);
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            Hatch(area);
            canvas.Restore();
            canvas.DrawPath(path, edge);
        }
        foreach (var river in region.Rivers)
        {
            using var path = Path(river, false);
            using var banks = new SKPaint { Color = ink, StrokeWidth = 3.2f * w, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            using var flow = new SKPaint { Color = paper, StrokeWidth = 1.6f * w, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, banks);
            canvas.DrawPath(path, flow);
        }
        using var hill = new SKPaint { Color = ink, StrokeWidth = 0.6f * w, IsAntialias = true, Style = SKPaintStyle.Stroke };
        foreach (var h in region.Hills)
        {
            // A small fan of hachures, steepest in the middle.
            var c = T(h);
            float s = 9 * w;
            for (int i = -3; i <= 3; i++)
                canvas.DrawLine(c.X + i * s * 0.28f, c.Y - s * (1 - Math.Abs(i) / 4f) * 0.6f, c.X + i * s * 0.36f, c.Y + s * 0.25f, hill);
        }
    }

    static void Label(SKCanvas canvas, string text, SKPoint at, SKTypeface face, float size, SKColor ink, SKColor paper, float unit)
    {
        using var font = new SKFont(face, size);
        using var halo = new SKPaint { Color = paper.WithAlpha(220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.35f, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawText(text, at.X, at.Y + size * 0.35f, SKTextAlign.Center, font, halo);
        Text(canvas, text, at.X, at.Y + size * 0.35f, face, size, ink, SKTextAlign.Center);
    }

    static void Text(SKCanvas canvas, string text, float x, float y, SKTypeface face, float size, SKColor colour,
        SKTextAlign align = SKTextAlign.Left, float tracking = 0)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        if (tracking <= 0) { canvas.DrawText(text, x, y, align, font, paint); return; }
        var widths = text.Select(ch => font.MeasureText(ch.ToString())).ToArray();
        float total = widths.Sum() + tracking * (text.Length - 1);
        float cx = align switch { SKTextAlign.Right => x - total, SKTextAlign.Center => x - total / 2, _ => x };
        for (int i = 0; i < text.Length; i++) { canvas.DrawText(text[i].ToString(), cx, y, SKTextAlign.Left, font, paint); cx += widths[i] + tracking; }
    }

    /// <summary>1, 2 or 5 × a power of ten, at or below <paramref name="x"/>.</summary>
    static double Nice(double x)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(x, 1e-6))));
        return x >= 5 * p ? 5 * p : x >= 2 * p ? 2 * p : p;
    }
}
