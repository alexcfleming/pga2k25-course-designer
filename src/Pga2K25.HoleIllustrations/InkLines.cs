using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>Hand-inked line helpers: wobbled outlines, unions and simple polygon tests in raster pixels.</summary>
public static class InkLines
{
    /// <summary>
    /// Resamples every contour of <paramref name="path"/> every 3 px and pushes each point along its normal by a smoothed
    /// random offset of up to about ±<paramref name="amplitude"/> px, so a clean spline outline reads hand-inked.
    /// </summary>
    public static SKPath Wobble(SKPath path, float amplitude, Random rng)
    {
        using var result = new SKPathBuilder();
        using var measure = new SKPathMeasure(path, true);
        do
        {
            float len = measure.Length;
            if (len < 2) continue;
            int n = Math.Max(8, (int)(len / 3));
            var off = new float[n];
            for (int i = 0; i < n; i++) off[i] = (float)(rng.NextDouble() * 2 - 1) * amplitude * 1.6f;
            for (int pass = 0; pass < 3; pass++)
            {
                var s = new float[n];
                for (int i = 0; i < n; i++) s[i] = (off[(i - 1 + n) % n] + off[i] + off[(i + 1) % n]) / 3;
                off = s;
            }
            for (int i = 0; i < n; i++)
            {
                measure.GetPositionAndTangent(i * len / n, out var pos, out var tan);
                var p = new SKPoint(pos.X - tan.Y * off[i], pos.Y + tan.X * off[i]);
                if (i == 0) result.MoveTo(p); else result.LineTo(p);
            }
            if (measure.IsClosed) result.Close();
        }
        while (measure.NextContour());
        return result.Detach();
    }

    /// <summary>One path whose outline is the union of the given paths (one ink line round overlapping pieces).</summary>
    public static SKPath Union(IEnumerable<SKPath> paths)
    {
        var union = new SKPath();
        foreach (var p in paths)
        {
            if (p.IsEmpty) continue;
            if (union.Op(p, SKPathOp.Union) is { } next) { union.Dispose(); union = next; }
        }
        return union;
    }

    public static SKPaint Stroke(SKColor colour, float width) =>
        new() { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = colour, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };

    public static SKPaint Fill(SKColor colour) => new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = colour };

    public static bool Contains(IReadOnlyList<SKPoint> poly, SKPoint p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    public static float DistanceToEdge(IReadOnlyList<SKPoint> poly, SKPoint p)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[j]; var b = poly[i];
            float dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            float t = len2 < 1e-9f ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
            best = Math.Min(best, SKPoint.Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t)));
        }
        return best;
    }

    public static float Area(IReadOnlyList<SKPoint> poly)
    {
        double s = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) s += (double)poly[j].X * poly[i].Y - (double)poly[i].X * poly[j].Y;
        return (float)Math.Abs(s / 2);
    }

    public static SKPoint Centroid(IReadOnlyList<SKPoint> poly)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            double cross = (double)poly[j].X * poly[i].Y - (double)poly[i].X * poly[j].Y;
            a += cross; cx += (poly[j].X + poly[i].X) * cross; cy += (poly[j].Y + poly[i].Y) * cross;
        }
        if (Math.Abs(a) < 1e-9) return new SKPoint(poly.Average(p => p.X), poly.Average(p => p.Y));
        return new SKPoint((float)(cx / (3 * a)), (float)(cy / (3 * a)));
    }
}
