using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// A small north-up locator of the whole plot: its edge, the sea (ground the replay puts below the detected sea level)
/// hatched, on plots with surf props), and the course's footprint (fairways and greens fused into one outline) in red. Everything comes from the
/// course file. An inland plot shows the edge and footprint only.
/// </summary>
public sealed class SiteInset
{
    /// <summary>Replay resolution: a locator a few inches across needs no finer.</summary>
    const double CellMetres = 8;

    public required Vec2 Min { get; init; }
    public required Vec2 Max { get; init; }
    /// <summary>Wet cells as world rectangles (min corner, size = <see cref="Cell"/>), row runs merged.</summary>
    public required List<(Vec2 Min, double Width)> Sea { get; init; }
    public required double Cell { get; init; }
    public required List<List<Vec2>> Footprint { get; init; }

    public bool HasSea => Sea.Count > 0;

    public static SiteInset Build(CourseModel course, CourseLayers layers)
    {
        // The plot: everything sculpted plus the routing, squared about its centre so the inset is a true square.
        var pts = course.HeightStamps.Select(s => s.Position).Concat(layers.ExtentPoints()).ToList();
        if (pts.Count == 0) throw new InvalidOperationException("The course has no terrain or holes to locate.");
        var lo = new Vec2(pts.Min(p => p.X), pts.Min(p => p.Z));
        var hi = new Vec2(pts.Max(p => p.X), pts.Max(p => p.Z));
        double half = Math.Max(hi.X - lo.X, hi.Z - lo.Z) / 2 * 1.04;
        var c = new Vec2((lo.X + hi.X) / 2, (lo.Z + hi.Z) / 2);
        var min = new Vec2(c.X - half, c.Z - half);
        var max = new Vec2(c.X + half, c.Z + half);

        var sea = new List<(Vec2, double)>();
        // Unsculpted ground sits at the sea plane too, so only a plot with surf props (the coast the designer built)
        // gets a sea; elsewhere low ground would read as water.
        bool coastal = course.PlacedObjects.Any(o => o.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase));
        if (coastal && course.HeightStamps.Count > 0)
        {
            var field = TerrainReplay.Build(course, min, max, new TerrainModelOptions { CellMetres = CellMetres, LargeBrushHardness = 0.3 });
            double level = TerrainReplay.DetectSeaLevel(course) + 0.05;
            for (int iz = 0; iz < field.Height; iz++)
                for (int ix = 0; ix < field.Width; ix++)
                {
                    if (field[ix, iz] > level) continue;
                    int start = ix;
                    while (ix + 1 < field.Width && field[ix + 1, iz] <= level) ix++;
                    sea.Add((new Vec2(field.Origin.X + start * field.Cell, field.Origin.Z + iz * field.Cell), (ix - start + 1) * field.Cell));
                }
            // An unsculpted plot sits flat at the sea plane all round, so "all wet" means no sea, not all sea.
            double wet = sea.Sum(s => s.Item2) * field.Cell, area = field.Width * field.Height * field.Cell * field.Cell;
            if (wet > area * 0.97) sea.Clear();
        }

        // Fairways and greens, plus each hole's route as a 50 m corridor so holes with few drawn shapes still count.
        var play = layers.Grass.Concat(layers.Greens).Select(g => (IReadOnlyList<Vec2>)g).ToList();
        var routes = layers.Holes.Select(h => new Ribbon(h.Route.ToList(), 50)).ToList();
        var footprint = play.Count + routes.Count == 0 ? [] : ShapeMerger.Merge(play, routes, [], closeRadius: 30, smooth: 8, cell: 4);
        return new SiteInset { Min = min, Max = max, Sea = sea, Cell = CellMetres, Footprint = footprint };
    }

    /// <summary>
    /// Draws the inset into <paramref name="box"/> (a square works best); <paramref name="unit"/> scales strokes.
    /// The sea hatch is <paramref name="seaInk"/>, or faded ink when null.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect box, SKColor ink, SKColor paper, SKColor red, float unit = 1, SKColor? seaInk = null)
    {
        var p = MapProjection.Fit([Min, Max, new Vec2(Min.X, Max.Z), new Vec2(Max.X, Min.Z)], box, MapProjection.NorthUpRotation, padding: 1.0);
        canvas.Save();
        canvas.ClipRect(box);
        using (var bg = new SKPaint { Color = paper, Style = SKPaintStyle.Fill }) canvas.DrawRect(box, bg);

        if (HasSea)
        {
            using var seaPath = new SKPathBuilder();
            foreach (var (m, w) in Sea)
            {
                SKPoint a = p.Map(m), b = p.Map(new Vec2(m.X + w, m.Z + Cell));
                seaPath.AddRect(new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            }
            using var path = seaPath.Detach();
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            using var hatch = new SKPaint { Color = seaInk ?? ink.WithAlpha(150), StrokeWidth = 0.5f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke };
            for (float y = box.Top + 1.5f * unit; y < box.Bottom; y += 3 * unit) canvas.DrawLine(box.Left, y, box.Right, y, hatch);
            canvas.Restore();
        }

        using var fill = new SKPaint { Color = red.WithAlpha(46), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var edge = new SKPaint { Color = red, StrokeWidth = 0.8f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
        // One even-odd path, so holes in the outline stay holes rather than doubling the tint.
        using (var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd })
        {
            foreach (var loop in Footprint) p.AddPolyline(b, loop, true);
            using var path = b.Detach();
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, edge);
        }
        canvas.Restore();
        using var frame = new SKPaint { Color = ink, StrokeWidth = 0.9f * unit, IsAntialias = true, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(box, frame);
    }

    /// <summary>The plot's side in metres, for the caption.</summary>
    public double SideMetres => Max.X - Min.X;
}
