using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The engraved landscape the Illustrated scorecard theme draws under the course: hachures on the big hills, water
/// lines on the sea, a stipple dot per bush and a shaded canopy per tree. Built once per map in page units from
/// the course file (terrain replay, sea level, placed objects), then drawn for the PNG and the PDF alike.
/// </summary>
public sealed class Landscape : IDisposable
{
    /// <summary>Slope is measured across this (m), so dune humps don't register and big landforms do.</summary>
    public const double SlopeSpanMetres = 24;
    /// <summary>Ground gentler than this (rise / run) gets no hachures.</summary>
    public const double MinSlope = 0.11;
    /// <summary>Hachure spacing on the page (units of 1/100 in).</summary>
    const float HachureStep = 2.6f;
    /// <summary>Water-line spacing on the page.</summary>
    const float WaterLineStep = 1.8f;
    /// <summary>Canopy radius (m) of a tree at scale 1, and the stipple dot (m) of a bush.</summary>
    const double TreeRadiusMetres = 4.0, BushRadiusMetres = 0.6;

    public sealed record Hachure(SKPoint From, SKPoint To, float Width, byte Alpha);
    public sealed record Tree(SKPoint At, float Radius, SKPath Crown);

    public List<Hachure> Hachures { get; } = [];
    /// <summary>Water-line runs: y, x from, x to.</summary>
    public List<(float Y, float X0, float X1)> WaterLines { get; } = [];
    public List<(SKPoint At, float Radius)> Bushes { get; } = [];
    /// <summary>Back to front (top of the page first).</summary>
    public List<Tree> Trees { get; } = [];

    public bool HasHills => Hachures.Count > 0;

    public static bool IsTree(PlacedObject p) => p.AssetPath.Contains("/Trees/", StringComparison.OrdinalIgnoreCase);
    public static bool IsBush(PlacedObject p) => p.AssetPath.Contains("/Bushes/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The landscape inside <paramref name="mapRect"/> (page units) under projection <paramref name="p"/>.</summary>
    public static Landscape Build(CourseModel course, MapProjection p, SKRect mapRect)
    {
        var l = new Landscape();
        var corners = new[] { new SKPoint(mapRect.Left, mapRect.Top), new SKPoint(mapRect.Right, mapRect.Top),
                              new SKPoint(mapRect.Left, mapRect.Bottom), new SKPoint(mapRect.Right, mapRect.Bottom) }.Select(p.Unmap).ToList();
        double pad = SlopeSpanMetres;
        var min = new Vec2(corners.Min(c => c.X) - pad, corners.Min(c => c.Z) - pad);
        var max = new Vec2(corners.Max(c => c.X) + pad, corners.Max(c => c.Z) + pad);
        // The terrain map's plot-scale replay: 2 m is plenty for slope over 24 m.
        var field = TerrainReplay.Build(course, min, max, new TerrainModelOptions { CellMetres = 2, LargeBrushHardness = 0.3 });
        double sea = TerrainReplay.DetectSeaLevel(course);
        bool Wet(Vec2 w) => field.Sample(w) <= sea + 0.05;

        var rnd = new Random(7);
        double span = SlopeSpanMetres / 2;
        for (float y = mapRect.Top, row = 0; y < mapRect.Bottom; y += HachureStep, row++)
            for (float x = mapRect.Left + row % 2 * HachureStep / 2; x < mapRect.Right; x += HachureStep)
            {
                var at = new SKPoint(x + (float)(rnd.NextDouble() - 0.5) * 0.8f, y + (float)(rnd.NextDouble() - 0.5) * 0.8f);
                var w = p.Unmap(at);
                if (Wet(w)) continue;
                var g = new Vec2((field.Sample(w + new Vec2(span, 0)) - field.Sample(w - new Vec2(span, 0))) / (2 * span),
                                 (field.Sample(w + new Vec2(0, span)) - field.Sample(w - new Vec2(0, span))) / (2 * span));
                double s = g.Length;
                if (s < MinSlope) continue;
                // Strokes run downhill, longer, heavier and darker the steeper it is.
                var down = p.MapDirection(new Vec2(-g.X / s, -g.Z / s));
                float len = (float)Math.Clamp(0.8 + 8 * s, 1.2, 4.4) / 2;
                l.Hachures.Add(new Hachure(new SKPoint(at.X - down.X * len, at.Y - down.Y * len), new SKPoint(at.X + down.X * len, at.Y + down.Y * len),
                    (float)Math.Clamp(0.18 + 0.7 * s, 0.18, 0.55), (byte)Math.Clamp(-20 + 420 * s, 40, 210)));
            }

        // Sea: every row in the shallows, every second below 2 m, every third below 6 m.
        int line = 0;
        for (float y = mapRect.Top + 1; y < mapRect.Bottom; y += WaterLineStep, line++)
        {
            float? start = null;
            for (float x = mapRect.Left; x <= mapRect.Right + 0.5f; x += 0.5f)
            {
                double depth = sea - field.Sample(p.Unmap(new SKPoint(x, y)));
                bool drawn = depth > 0.05 && (depth < 2 || (depth < 6 ? line % 2 == 0 : line % 3 == 0));
                if (drawn && start is null) start = x;
                if (!drawn && start is { } x0) { if (x - x0 > 1) l.WaterLines.Add((y, x0, x)); start = null; }
            }
        }

        float k = p.PointsPerMetre;
        var view = SKRect.Inflate(mapRect, 10, 10);
        foreach (var b in course.PlacedObjects.Where(IsBush))
            if (p.Map(b.Position) is var at && view.Contains(at))
                l.Bushes.Add((at, Math.Max(0.35f, (float)(BushRadiusMetres * k * Math.Max(0.5, b.Scale)))));
        foreach (var t in course.PlacedObjects.Where(IsTree).Select(t => (At: p.Map(t.Position), t.Scale)).Where(t => view.Contains(t.At)).OrderBy(t => t.At.Y))
        {
            float r = Math.Max(1.1f, (float)(TreeRadiusMetres * k * Math.Max(0.5, t.Scale)));
            l.Trees.Add(new Tree(t.At, r, Crown(t.At, r, rnd)));
        }
        return l;
    }

    /// <summary>A centre puff and four lobes, unioned: reads as a canopy at any size.</summary>
    static SKPath Crown(SKPoint c, float r, Random rnd)
    {
        var path = new SKPath();
        path.AddCircle(c.X, c.Y, r * 0.62f);
        double turn = rnd.NextDouble() * Math.PI * 2;
        for (int i = 0; i < 4; i++)
        {
            double a = turn + i * Math.PI / 2 + (rnd.NextDouble() - 0.5) * 0.6;
            using var lobe = new SKPath();
            lobe.AddCircle(c.X + r * 0.42f * (float)Math.Cos(a), c.Y + r * 0.42f * (float)Math.Sin(a), r * (0.5f + 0.12f * (float)rnd.NextDouble()));
            if (path.Op(lobe, SKPathOp.Union) is { } union) { path.Dispose(); path = union; }
        }
        return path;
    }

    /// <summary>Hachures, sea, bushes, then trees: everything the course is drawn over.</summary>
    public void Draw(SKCanvas canvas, SKColor ink, SKColor crownFill)
    {
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        foreach (var h in Hachures)
        {
            stroke.StrokeWidth = h.Width;
            stroke.Color = ink.WithAlpha(h.Alpha);
            canvas.DrawLine(h.From, h.To, stroke);
        }

        stroke.StrokeWidth = 0.3f;
        stroke.Color = ink.WithAlpha(150);
        stroke.StrokeCap = SKStrokeCap.Butt;
        foreach (var (y, x0, x1) in WaterLines) canvas.DrawLine(x0, y, x1, y, stroke);

        using var dot = new SKPaint { IsAntialias = true, Color = ink.WithAlpha(150) };
        foreach (var (at, r) in Bushes) canvas.DrawCircle(at, r, dot);

        using var shadow = new SKPaint { IsAntialias = true, Color = ink.WithAlpha(150) };
        using var crown = new SKPaint { IsAntialias = true, Color = crownFill };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = ink, StrokeWidth = 0.28f };
        using var shade = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = ink.WithAlpha(170), StrokeWidth = 0.22f, StrokeCap = SKStrokeCap.Round };
        foreach (var t in Trees)
        {
            canvas.DrawCircle(t.At.X + t.Radius * 0.35f, t.At.Y + t.Radius * 0.4f, t.Radius, shadow);
            canvas.DrawPath(t.Crown, crown);
            canvas.DrawPath(t.Crown, edge);
            // Three curved strokes on the shaded (lower right) side.
            for (int i = 0; i < 3; i++)
            {
                float rr = t.Radius * (0.45f + 0.15f * i);
                canvas.DrawArc(new SKRect(t.At.X - rr, t.At.Y - rr, t.At.X + rr, t.At.Y + rr), 10 + 25 * i, 50, false, shade);
            }
        }
    }

    public void Dispose()
    {
        foreach (var t in Trees) t.Crown.Dispose();
    }
}
