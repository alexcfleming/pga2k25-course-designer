using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>Per-hole terrain sketch sheets: US Letter portrait, oriented like the yardage book.</summary>
public sealed record HoleSheetOptions
{
    /// <summary>Shading, surfaces, water, terrain model and credit, shared with the plot map.</summary>
    public TerrainMapOptions Map { get; init; } = new();

    public double Dpi { get; init; } = 300;

    /// <summary>Ground shown behind the back tee and beyond the green (m), along the hole.</summary>
    public double MetresBehindTee { get; init; } = 40;
    public double MetresBeyondGreen { get; init; } = 50;

    /// <summary>Minimum ground (m) kept either side of the widest point of the route (doglegs).</summary>
    public double SideClearanceMetres { get; init; } = 60;

    /// <summary>Terrain raster for the sheet; finer than the plot map.</summary>
    public double CellMetres { get; init; } = 0.5;

    /// <summary>
    /// Contour interval (unless <see cref="TerrainMapOptions.ContourIntervalFeet"/> is set) is the smallest of
    /// 1 / 2 / 5 … ft that keeps the ground within <see cref="CorridorMetres"/> of this hole's route to at most
    /// this many levels. Hillsides and sea at the sheet's edges don't coarsen it; steep ground is thinned instead.
    /// </summary>
    public int MaxLevels { get; init; } = 60;
    public double CorridorMetres { get; init; } = 50;

    /// <summary>Minor contours closer than this on paper (points) are thinned on steep ground.</summary>
    public double MinContourSpacingPoints { get; init; } = 2.0;

    /// <summary>Grid squares on the hole axis (yards from the back tee, and left / right of the centre line).</summary>
    public double GridYards { get; init; } = 50;

    /// <summary>Spot heights at every grid crossing.</summary>
    public bool GridSpotHeights { get; init; } = true;

    /// <summary>Ticks along the route (numbered every second tick).</summary>
    public int TickYards { get; init; } = 25;

    public bool Png { get; init; } = true;
    public bool Pdf { get; init; } = true;
}

/// <summary>
/// One hole's sheet geometry and terrain. The view is the yardage book's: back tee → green straight up the
/// page (same rotation formula as <c>HoleAnalysis</c>), widened to the Letter map area.
/// </summary>
public sealed class HoleSheet
{
    /// <summary>Letter portrait in points.</summary>
    public const float PageWidth = 612, PageHeight = 792;

    /// <summary>Map area in points (the rest is header, grid labels and footer).</summary>
    public static readonly SKRect MapRectPoints = new(48, 92, PageWidth - 48, PageHeight - 104);

    public required HoleRouting Routing { get; init; }
    public required TerrainSurvey Survey { get; init; }
    public required Vec2 BackTee { get; init; }
    public required Vec2 Forward { get; init; }
    public required Vec2 Side { get; init; }
    public required double RotationDegrees { get; init; }

    /// <summary>Window along the hole (m from the back tee) and across it (m from the tee→green line).</summary>
    public required (double From, double To) Along { get; init; }
    public required (double From, double To) Across { get; init; }
    public required IReadOnlyList<Vec2> Window { get; init; }

    /// <summary>True when 0 ft is sea level (the course has a sea), even if none shows on this sheet.</summary>
    public bool DatumIsSeaLevel { get; init; }

    public Vec2 At(double along, double across) => BackTee + Forward * along + Side * across;

    public bool Contains(Vec2 p, double pad = 0)
    {
        var d = p - BackTee;
        double a = d.X * Forward.X + d.Z * Forward.Z, c = d.X * Side.X + d.Z * Side.Z;
        return a >= Along.From - pad && a <= Along.To + pad && c >= Across.From - pad && c <= Across.To + pad;
    }

    public MapProjection Projection(SKRect map) => MapProjection.Fit(Window, map, RotationDegrees, padding: 1.0);

    static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Z * ab.Z;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / len2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }

    public static HoleSheet Build(CourseModel course, CourseLayers layers, Hole hole, HoleSheetOptions o, double datumMetres,
        bool datumIsSeaLevel = false)
    {
        var layout = layers.Holes.Single(h => h.Hole.Number == hole.Number);
        var back = layout.BackTee;
        var green = hole.GreenAnchor;
        var fwd = (green - back).Normalized();
        var side = new Vec2(fwd.Z, -fwd.X);
        double rotation = Math.Atan2(fwd.X, fwd.Z) * 180 / Math.PI;   // as HoleAnalysis: forward points up the page

        double Across(Vec2 p) => (p - back).X * side.X + (p - back).Z * side.Z;
        var shown = layout.Route.Concat(hole.Tees).ToList();
        double centre = layout.Route.Average(Across);   // centred on the route's lateral spread, as the yardage book
        double need = shown.Max(p => Math.Abs(Across(p) - centre)) + o.SideClearanceMetres;

        double a0 = -o.MetresBehindTee, a1 = Vec2.Distance(back, green) + o.MetresBeyondGreen;
        double aspect = MapRectPoints.Width / MapRectPoints.Height;
        double half = (a1 - a0) * aspect / 2;
        if (need > half)
        {
            // A wide dogleg: keep the clearance and show more of the hole's length instead.
            double extra = 2 * need / aspect - (a1 - a0);
            a0 -= extra / 2; a1 += extra / 2; half = need;
        }
        var across = (centre - half, centre + half);
        var window = new List<Vec2>
        {
            back + fwd * a0 + side * across.Item1, back + fwd * a0 + side * across.Item2,
            back + fwd * a1 + side * across.Item2, back + fwd * a1 + side * across.Item1,
        };

        var line = layout.Route.Prepend(back).Concat(hole.Tees).ToList();
        bool NearPlay(Vec2 p) => InWindow(p) && line.Zip(line.Skip(1)).Any(s => SegmentDistance(p, s.First, s.Second) <= o.CorridorMetres);
        bool InWindow(Vec2 p) =>
            ((p - back).X * fwd.X + (p - back).Z * fwd.Z) is var a && a >= a0 && a <= a1 && Across(p) is var c && c >= across.Item1 && c <= across.Item2;

        // Terrain over the window's bounding box, contour interval from the window itself.
        double u = o.Dpi / 72;
        var projection = MapProjection.Fit(window, MapRectPoints, rotation, padding: 1.0);
        double ppm = projection.PointsPerMetre * u;
        var map = o.Map with
        {
            Terrain = o.Map.Terrain with { CellMetres = o.CellMetres },
            PixelsPerMetre = ppm,
            MinContourSpacingPixels = o.MinContourSpacingPoints * u,
            MaxCorridorLevels = o.MaxLevels,
        };
        var pad = new Vec2(10, 10);
        var min = new Vec2(window.Min(p => p.X), window.Min(p => p.Z)) - pad;
        var max = new Vec2(window.Max(p => p.X), window.Max(p => p.Z)) + pad;
        var survey = TerrainSurvey.Build(course, map, layers, min, max, NearPlay, datumMetres);
        return new HoleSheet
        {
            Routing = survey.Holes.Single(h => h.Hole.Number == hole.Number), Survey = survey,
            BackTee = back, Forward = fwd, Side = side, RotationDegrees = rotation, Along = (a0, a1), Across = across, Window = window,
            DatumIsSeaLevel = datumIsSeaLevel || survey.SeaLevelMetres is not null,
        };
    }
}
