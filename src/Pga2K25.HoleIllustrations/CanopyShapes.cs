using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>
/// Cloud-edged tree masses: each vegetation outline shrunk by 0.55 r, joined with bumps of radius r·(0.8–1.2) set every
/// 1.25 r round the outline, so a smooth merged mass reads as a row of crowns.
/// </summary>
public static class CanopyShapes
{
    /// <summary>The canopy mask (0–1) for the masses' outlines in raster pixels, with bumps of radius <paramref name="r"/> px.</summary>
    public static float[] Mask(WatercolourCanvas c, IReadOnlyList<List<SKPoint>> masses, float r, Random rng)
    {
        if (masses.Count == 0) return new float[c.Width * c.Height];
        var solid = c.Mask((canvas, paint) =>
        {
            foreach (var m in masses.Where(m => m.Count >= 3)) { using var p = PlateProjection.PixelPath(m); canvas.DrawPath(p, paint); }
        }, blur: 0);
        // Inside distance: the core is what lies deeper than 0.55 r.
        var inside = c.Distance(solid, v => v < 0.5f);
        float shrink = 0.55f * r;
        var bumps = c.Mask((canvas, paint) =>
        {
            foreach (var m in masses.Where(m => m.Count >= 3))
            {
                double perimeter = 0;
                for (int i = 0; i < m.Count; i++) perimeter += SKPoint.Distance(m[i], m[(i + 1) % m.Count]);
                int n = Math.Max(6, (int)(perimeter / (r * 1.25)));
                double step = perimeter / n, run = 0, next = 0;
                for (int i = 0; i < m.Count && n > 0; i++)
                {
                    var a = m[i]; var b = m[(i + 1) % m.Count];
                    double seg = SKPoint.Distance(a, b);
                    while (next <= run + seg)
                    {
                        float t = seg < 1e-6 ? 0 : (float)((next - run) / seg);
                        var p = new SKPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                        canvas.DrawCircle(p, r * (0.8f + 0.4f * (float)rng.NextDouble()), paint);
                        next += step;
                    }
                    run += seg;
                }
            }
        }, blur: 0);
        var mask = c.Map(i => Math.Max(Math.Clamp(inside[i] - shrink + 0.5f, 0, 1), bumps[i]));
        return c.Blur(mask, 0.8f);
    }

    /// <summary>
    /// An ink line just inside the edge of <paramref name="mask"/>, <paramref name="width"/> px wide (alpha 0–1): used where
    /// the outline is a raster union (canopies, waste) rather than a single spline.
    /// </summary>
    public static float[] EdgeInk(WatercolourCanvas c, float[] mask, float width)
    {
        var inside = c.Distance(mask, v => v < 0.5f);
        return c.Map(i => mask[i] < 0.5f ? 0 : Math.Clamp(width - inside[i] + 1, 0, 1));
    }
}
