using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>
/// Bunker linework: stipple over the sand, a hachured face on the side the player looks into (toward the green for a
/// greenside bunker, else toward the back tee) and a wobbled ink outline.
/// </summary>
public static class BunkerInk
{
    public static void Draw(SKCanvas canvas, IReadOnlyList<SKPoint> ring, SKPoint target, IllustrationStyle style, Random rng)
    {
        if (ring.Count < 3) return;
        float area = InkLines.Area(ring);
        var centre = InkLines.Centroid(ring);
        float minX = ring.Min(p => p.X), maxX = ring.Max(p => p.X), minY = ring.Min(p => p.Y), maxY = ring.Max(p => p.Y);

        // Stipple: one dot per 70 px², kept 3 px inside the edge.
        using (var dot = InkLines.Fill(style.Stipple))
        {
            int n = (int)(area / 70);
            for (int i = 0; i < n; i++)
            {
                var p = new SKPoint(minX + (float)rng.NextDouble() * (maxX - minX), minY + (float)rng.NextDouble() * (maxY - minY));
                float r = 0.8f + 0.9f * (float)rng.NextDouble();
                if (InkLines.Contains(ring, p) && InkLines.DistanceToEdge(ring, p) >= 3) canvas.DrawCircle(p, r, dot);
            }
        }

        // Hachured face: walk the outline every 5 px; where the outward normal faces the target, stroke inward.
        float tx = target.X - centre.X, ty = target.Y - centre.Y, tn = MathF.Max(1e-3f, MathF.Sqrt(tx * tx + ty * ty));
        tx /= tn; ty /= tn;
        using (var hachure = InkLines.Stroke(style.Hachure, 1.6f))
        using (var outline = PlateProjection.PixelPath(ring))
        using (var measure = new SKPathMeasure(outline, true))
        {
            float len = measure.Length;
            int steps = (int)(len / 5);
            for (int s = 0; s < steps; s++)
            {
                measure.GetPositionAndTangent(s * len / steps, out var p, out var tan);
                var n = new SKPoint(-tan.Y, tan.X);   // one side; flip to point inward
                if (!InkLines.Contains(ring, new SKPoint(p.X + n.X * 2, p.Y + n.Y * 2))) n = new SKPoint(-n.X, -n.Y);
                float facing = -(n.X * tx + n.Y * ty);   // outward normal · direction to target
                if (facing <= 0.25f) continue;
                float l = (8 + 10 * facing) * (0.8f + 0.4f * (float)rng.NextDouble());
                canvas.DrawLine(p, new SKPoint(p.X + n.X * l, p.Y + n.Y * l), hachure);
            }
        }

        using (var path = PlateProjection.PixelPath(ring))
        using (var wobbled = InkLines.Wobble(path, 0.8f, rng))
        using (var ink = InkLines.Stroke(style.Ink.WithAlpha(235), 2.6f))
            canvas.DrawPath(wobbled, ink);
    }
}
