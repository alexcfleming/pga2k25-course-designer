using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The painted land the Modern Illustrated scorecard theme draws round the course: a soft hillshade on sand, the sea
/// tinted by depth, and everything the designer planted (trees, bushes, grasses, flowers, walls and fences). Built
/// once per map in page units from the course file, then drawn for the PNG and the PDF alike.
/// </summary>
public sealed class Scenery : IDisposable
{
    /// <summary>Slope is measured across this (m): enough to soften brush-stamp edges, small enough to keep dunes.</summary>
    const double SlopeSpanMetres = 12;
    /// <summary>Hillshade samples per page unit (1/100 in); it is drawn smoothed, so this is plenty for print.</summary>
    const float ShadeDensity = 1.5f;
    /// <summary>Canopy radius (m) of a tree at scale 1 (as on the terrain map's planting), and a bush's radius.</summary>
    const double TreeRadiusMetres = 4.0, BushRadiusMetres = 1.4;
    /// <summary>Scattered plants drawn per square page unit at most, so a 250,000-plant grass area stays a texture.</summary>
    const double MaxScatterPerUnit = 0.6;

    public static readonly SKColor Sand = SKColor.Parse("#ead9a6"), SandLit = SKColor.Parse("#f8efcf"), SandShade = SKColor.Parse("#c4ab72");
    public static readonly SKColor Shallows = SKColor.Parse("#9acbbd"), Deep = SKColor.Parse("#4f8f8a");
    public static readonly SKColor Broadleaf = SKColor.Parse("#5f8a3a"), BroadleafLit = SKColor.Parse("#7ea64e"), BroadleafEdge = SKColor.Parse("#456a2a");
    public static readonly SKColor Conifer = SKColor.Parse("#3f6334"), ConiferLit = SKColor.Parse("#557d45"), ConiferEdge = SKColor.Parse("#2c4626");
    public static readonly SKColor Bush = SKColor.Parse("#6f8c3d"), BushLit = SKColor.Parse("#8ea957");
    public static readonly SKColor Tuft = SKColor.Parse("#7f8f40"), TreeShadow = SKColor.Parse("#3d4a20").WithAlpha(70);
    public static readonly SKColor Wall = SKColor.Parse("#8f8a7c"), Fence = SKColor.Parse("#8a6a44");
    static readonly SKColor[] Blooms = [SKColor.Parse("#d9677e"), SKColor.Parse("#e8c547"), SKColor.Parse("#f2efe6"), SKColor.Parse("#9b6fc4")];

    public sealed record Tree(SKPoint At, float Radius, bool IsConifer);

    /// <summary>The hillshade and sea for the map rectangle, drawn first.</summary>
    public required SKImage Ground { get; init; }
    public required SKRect Rect { get; init; }
    public bool HasSea { get; private set; }
    /// <summary>Back to front (top of the page first).</summary>
    public List<Tree> Trees { get; } = [];
    public List<(SKPoint At, float Radius)> Bushes { get; } = [];
    public List<SKPoint> Tufts { get; } = [];
    public List<(SKPoint At, SKColor Colour)> Flowers { get; } = [];
    public List<(List<SKPoint> Points, bool Fence)> Walls { get; } = [];

    public bool HasConifers => Trees.Any(t => t.IsConifer);
    public bool HasBroadleaf => Trees.Any(t => !t.IsConifer);

    /// <summary>The scenery inside <paramref name="mapRect"/> (page units) under projection <paramref name="p"/>.</summary>
    public static Scenery Build(CourseModel course, MapProjection p, SKRect mapRect)
    {
        var corners = new[] { new SKPoint(mapRect.Left, mapRect.Top), new SKPoint(mapRect.Right, mapRect.Top),
                              new SKPoint(mapRect.Left, mapRect.Bottom), new SKPoint(mapRect.Right, mapRect.Bottom) }.Select(p.Unmap).ToList();
        double pad = SlopeSpanMetres;
        var min = new Vec2(corners.Min(c => c.X) - pad, corners.Min(c => c.Z) - pad);
        var max = new Vec2(corners.Max(c => c.X) + pad, corners.Max(c => c.Z) + pad);
        var field = TerrainReplay.Build(course, min, max, new TerrainModelOptions { CellMetres = 2, LargeBrushHardness = 0.3 });
        double sea = TerrainReplay.DetectSeaLevel(course);

        // Lit from the upper left of the page, whatever the map's rotation.
        var light = (p.Unmap(new SKPoint(mapRect.Left, mapRect.Top)) - p.Unmap(new SKPoint(mapRect.Left + 1, mapRect.Top + 1))).Normalized();
        int w = (int)Math.Ceiling(mapRect.Width * ShadeDensity), h = (int)Math.Ceiling(mapRect.Height * ShadeDensity);
        var pixels = new SKColor[w * h];
        bool anySea = false;
        double span = SlopeSpanMetres / 2;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                var at = p.Unmap(new SKPoint(mapRect.Left + (x + 0.5f) / ShadeDensity, mapRect.Top + (y + 0.5f) / ShadeDensity));
                double z = field.Sample(at);
                if (z <= sea + 0.05)
                {
                    anySea = true;
                    pixels[y * w + x] = Lerp(Shallows, Deep, Math.Clamp((sea - z) / 8, 0, 1));
                    continue;
                }
                double gx = (field.Sample(at + new Vec2(span, 0)) - field.Sample(at - new Vec2(span, 0))) / (2 * span);
                double gz = (field.Sample(at + new Vec2(0, span)) - field.Sample(at - new Vec2(0, span))) / (2 * span);
                // Ground rising towards the light faces it.
                double lit = Math.Clamp(-(gx * light.X + gz * light.Z) * 2.4, -1, 1);
                pixels[y * w + x] = lit >= 0 ? Lerp(Sand, SandLit, lit) : Lerp(Sand, SandShade, -lit);
            }
        });
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque) { Pixels = pixels };
        var s = new Scenery { Ground = SKImage.FromBitmap(bitmap), Rect = mapRect, HasSea = anySea };

        float k = p.PointsPerMetre;
        var view = SKRect.Inflate(mapRect, 10, 10);
        var rnd = new Random(11);
        void Add(PlantSpecies species, SKPoint at, double scale)
        {
            if (!view.Contains(at)) return;
            switch (species.Kind)
            {
                case PlantKind.Tree: s.Trees.Add(new Tree(at, Math.Max(1.4f, (float)(TreeRadiusMetres * k * Math.Max(0.5, scale))), species.Form == TreeForm.Conifer)); break;
                case PlantKind.Bush: s.Bushes.Add((at, Math.Max(0.8f, (float)(BushRadiusMetres * k * Math.Max(0.5, scale))))); break;
                case PlantKind.Grass: s.Tufts.Add(at); break;
                default: s.Flowers.Add((at, Blooms[rnd.Next(Blooms.Length)])); break;
            }
        }
        foreach (var o in course.PlacedObjects)
            if (PlantSpecies.Of(o.AssetPath) is { } species) Add(species, p.Map(o.Position), o.Scale);
        // Scatter groups and planted areas: the game places those plants itself, so they are spread at random inside
        // the group's circle or the area's outline, right in number (up to a texture's worth) but not plant by plant.
        foreach (var g in course.PlacedClusters)
        {
            if (PlantSpecies.Of(g.AssetPath) is not { } species || g.Count <= 0) continue;
            double pageArea = Math.PI * Math.Pow(g.Radius * k, 2);
            int n = Math.Min(g.Count, Math.Max(1, (int)(pageArea * MaxScatterPerUnit)));
            for (int i = 0; i < n; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2, d = Math.Sqrt(rnd.NextDouble()) * g.Radius;
                Add(species, p.Map(new Vec2(g.Centre.X + Math.Cos(a) * d, g.Centre.Z + Math.Sin(a) * d)), 1);
            }
        }
        foreach (var a in course.PlacedAreas)
        {
            if (PlantSpecies.Of(a.AssetPath) is not { } species || a.Outline.Count < 3) continue;
            var outline = a.Outline.Select(p.Map).ToList();
            using var b = new SKPathBuilder();
            b.AddPoly(outline.ToArray(), close: true);
            using var path = b.Detach();
            var box = path.Bounds;
            int n = Math.Min(a.EstimatedCount, (int)(a.AreaSquareMetres * k * k * MaxScatterPerUnit));
            for (int i = 0, tries = 0; i < n && tries < n * 4; tries++)
            {
                var at = new SKPoint(box.Left + (float)rnd.NextDouble() * box.Width, box.Top + (float)rnd.NextDouble() * box.Height);
                if (!path.Contains(at.X, at.Y)) continue;
                Add(species, at, 1);
                i++;
            }
        }
        s.Trees.Sort((a, b) => a.At.Y.CompareTo(b.At.Y));
        // Walls and fences; the designer's own water is an object path too (GeneratedRiverSetupPrefab), drawn as water.
        foreach (var path in course.ObjectPaths.Where(o => o.AssetPath.Contains("/Walls/") && !o.AssetPath.Contains("River", StringComparison.OrdinalIgnoreCase)))
            if (path.Points.Count >= 2)
                s.Walls.Add((path.Points.Select(p.Map).ToList(), path.AssetPath.Contains("Fence", StringComparison.OrdinalIgnoreCase)));
        return s;
    }

    /// <summary>The hillshade and the sea: drawn before the course.</summary>
    public void DrawGround(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(Ground, Rect, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    /// <summary>Walls and fences, grasses and flowers, bushes, then trees with their shadows: drawn over the course.</summary>
    public void DrawPlanting(SKCanvas canvas)
    {
        foreach (var (points, fence) in Walls)
        {
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = fence ? Fence : Wall, StrokeWidth = fence ? 0.5f : 0.8f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            if (fence) stroke.PathEffect = SKPathEffect.CreateDash([1.6f, 0.8f], 0);
            using var b = new SKPathBuilder();
            b.AddPoly(points.ToArray(), close: false);
            using var path = b.Detach();
            canvas.DrawPath(path, stroke);
        }

        // A grass tuft is three short strokes; a flower a coloured dot.
        using var tuft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Tuft, StrokeWidth = 0.28f, StrokeCap = SKStrokeCap.Round };
        foreach (var t in Tufts)
        {
            canvas.DrawLine(t.X - 0.5f, t.Y + 0.4f, t.X - 0.2f, t.Y - 0.6f, tuft);
            canvas.DrawLine(t.X, t.Y + 0.4f, t.X, t.Y - 0.8f, tuft);
            canvas.DrawLine(t.X + 0.5f, t.Y + 0.4f, t.X + 0.2f, t.Y - 0.6f, tuft);
        }
        using var bloom = new SKPaint { IsAntialias = true };
        foreach (var (at, colour) in Flowers) { bloom.Color = colour; canvas.DrawCircle(at, 0.5f, bloom); }

        using var bush = new SKPaint { IsAntialias = true, Color = Bush };
        using var bushLit = new SKPaint { IsAntialias = true, Color = BushLit };
        foreach (var (at, r) in Bushes)
        {
            canvas.DrawCircle(at, r, bush);
            canvas.DrawCircle(at.X - r * 0.25f, at.Y - r * 0.25f, r * 0.5f, bushLit);
        }

        // All the shadows first, so a tree's shadow never falls across the tree in front of it.
        using var shadow = new SKPaint { IsAntialias = true, Color = TreeShadow };
        foreach (var t in Trees) canvas.DrawCircle(t.At.X + t.Radius * 0.45f, t.At.Y + t.Radius * 0.45f, t.Radius, shadow);
        using var body = new SKPaint { IsAntialias = true };
        using var lit = new SKPaint { IsAntialias = true };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.3f };
        foreach (var t in Trees) DrawTree(canvas, t.At, t.Radius, t.IsConifer, body, lit, edge);
    }

    /// <summary>A broadleaf canopy (a disc lit on its upper left) or a conifer (a darker seven-point star).</summary>
    public static void DrawTree(SKCanvas canvas, SKPoint at, float r, bool conifer, SKPaint body, SKPaint lit, SKPaint edge)
    {
        body.Color = conifer ? Conifer : Broadleaf;
        lit.Color = conifer ? ConiferLit : BroadleafLit;
        edge.Color = conifer ? ConiferEdge : BroadleafEdge;
        if (conifer)
        {
            using var star = Star(at, r, 7);
            canvas.DrawPath(star, body);
            canvas.DrawPath(star, edge);
            canvas.DrawCircle(at.X - r * 0.2f, at.Y - r * 0.2f, r * 0.35f, lit);
        }
        else
        {
            canvas.DrawCircle(at, r, body);
            canvas.DrawCircle(at, r, edge);
            canvas.DrawCircle(at.X - r * 0.28f, at.Y - r * 0.28f, r * 0.55f, lit);
        }
    }

    public static SKPath Star(SKPoint at, float r, int points)
    {
        using var b = new SKPathBuilder();
        for (int i = 0; i < points * 2; i++)
        {
            double a = i * Math.PI / points - Math.PI / 2;
            float rr = i % 2 == 0 ? r : r * 0.62f;
            var q = new SKPoint(at.X + (float)Math.Cos(a) * rr, at.Y + (float)Math.Sin(a) * rr);
            if (i == 0) b.MoveTo(q); else b.LineTo(q);
        }
        b.Close();
        return b.Detach();
    }

    static SKColor Lerp(SKColor a, SKColor b, double t) =>
        new((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));

    public void Dispose() => Ground.Dispose();
}
