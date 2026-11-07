using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>
/// How a hole sheet lays out the map. <see cref="Strip"/> (since 2.5): ground 75 yd past the route's widest point
/// on each side, as a strip down the left of the frame with the hole design notes beside it. <see cref="FullWidth"/>:
/// the sheets printed before 2.5, as wide as the page's shape allowed; kept so scans of them still import.
/// </summary>
public enum HoleSheetLayout { Strip, FullWidth }

/// <summary>One hole design note for the sheet's notes column: its label, what it asks, and what's written (may be empty).</summary>
public sealed record HoleSheetNote(string Label, string Prompt, string Text);

/// <summary>Per-hole terrain sketch sheets: US Letter portrait, oriented like the yardage book.</summary>
public sealed record HoleSheetOptions
{
    /// <summary>Shading, surfaces, water, terrain model and credit, shared with the plot map.</summary>
    public TerrainMapOptions Map { get; init; } = new();

    public double Dpi { get; init; } = 300;

    /// <summary>Ground shown behind the back tee and beyond the green (m), along the hole.</summary>
    public double MetresBehindTee { get; init; } = 40;
    public double MetresBeyondGreen { get; init; } = 50;

    public HoleSheetLayout Layout { get; init; } = HoleSheetLayout.Strip;

    /// <summary>
    /// Strip layout: ground (yd) kept beyond the route's widest point on each side (tees, aim points, green). A side
    /// the route never crosses to keeps this much beyond the tee→green line. Fairways play 30–50 yd wide, so 75 yd
    /// each side keeps the rough and the trouble on the sheet without the hillsides beyond.
    /// </summary>
    public double SideYards { get; init; } = 75;

    /// <summary>Strip layout: the least width (points) left beside the strip for its labels and the design notes.</summary>
    public double NotesMinWidthPoints { get; init; } = 180;

    /// <summary>Strip layout: the hole design notes by hole number, for the notes column. Holes not listed get a ruled column.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<HoleSheetNote>>? DesignNotes { get; init; }

    /// <summary>Full-width layout (sheets before 2.5): minimum ground (m) kept either side of the widest point of the route.</summary>
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

    /// <summary>
    /// The printed frame in points (the rest is header, grid labels and footer). In the strip layout it holds the
    /// map strip (<see cref="StripPoints"/>) and the notes column; scans are found and straightened by this frame.
    /// </summary>
    public static readonly SKRect MapRectPoints = new(48, 92, PageWidth - 48, PageHeight - 104);

    /// <summary>The space between the strip and the notes column (points), where the strip's right-hand yardages sit.</summary>
    public const float NotesGutterPoints = 28;

    /// <summary><paramref name="points"/>, a rectangle in page points inside <see cref="MapRectPoints"/>, placed the same way in <paramref name="frame"/>.</summary>
    public static SKRect Within(SKRect points, SKRect frame)
    {
        var r = MapRectPoints;
        float sx = frame.Width / r.Width, sy = frame.Height / r.Height;
        return new SKRect(frame.Left + (points.Left - r.Left) * sx, frame.Top + (points.Top - r.Top) * sy,
            frame.Left + (points.Right - r.Left) * sx, frame.Top + (points.Bottom - r.Top) * sy);
    }

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

    /// <summary>Where the map sits in the frame, in page points: the left of it in the strip layout, all of it in the full-width one.</summary>
    public required SKRect StripPoints { get; init; }
    public HoleSheetLayout Kind { get; init; }

    /// <summary>The notes column in page points (empty in the full-width layout).</summary>
    public SKRect NotesPoints => Kind == HoleSheetLayout.Strip
        ? new SKRect(StripPoints.Right + NotesGutterPoints, MapRectPoints.Top, MapRectPoints.Right, MapRectPoints.Bottom) : SKRect.Empty;

    /// <summary>True when 0 ft is sea level (the course has a sea), even if none shows on this sheet.</summary>
    public bool DatumIsSeaLevel { get; init; }

    public Vec2 At(double along, double across) => BackTee + Forward * along + Side * across;

    public bool Contains(Vec2 p, double pad = 0)
    {
        var d = p - BackTee;
        double a = d.X * Forward.X + d.Z * Forward.Z, c = d.X * Side.X + d.Z * Side.Z;
        return a >= Along.From - pad && a <= Along.To + pad && c >= Across.From - pad && c <= Across.To + pad;
    }

    /// <summary>World to <paramref name="frame"/> (the printed frame, at any scale): the window fills the strip.</summary>
    public MapProjection Projection(SKRect frame) => MapProjection.Fit(Window, Within(StripPoints, frame), RotationDegrees, padding: 1.0);

    static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Z * ab.Z;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / len2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }

    /// <summary>
    /// A hole sheet's view without its terrain: the window along and across the hole, its rotation, and the
    /// projection onto the sheet's map frame. Cheap, so the Studio uses it to turn the live map to a sheet's view
    /// and to tie scanned sheets back to the ground.
    /// </summary>
    public sealed record Layout(Vec2 BackTee, Vec2 Forward, Vec2 Side, double RotationDegrees,
        (double From, double To) Along, (double From, double To) Across, IReadOnlyList<Vec2> Window, SKRect StripPoints, HoleSheetLayout Kind)
    {
        /// <summary>World to <paramref name="frame"/> (the printed frame, at any scale): the window fills the strip.</summary>
        public MapProjection Projection(SKRect frame) => MapProjection.Fit(Window, Within(StripPoints, frame), RotationDegrees, padding: 1.0);

        /// <summary>World points under the printed map frame's corners: top-left, top-right, bottom-right, bottom-left.</summary>
        public Vec2[] FrameCorners()
        {
            var p = Projection(MapRectPoints);
            var r = MapRectPoints;
            return [p.Unmap(new(r.Left, r.Top)), p.Unmap(new(r.Right, r.Top)), p.Unmap(new(r.Right, r.Bottom)), p.Unmap(new(r.Left, r.Bottom))];
        }

        /// <summary>World points under the map strip's corners, in the same order: the ground the sheet shows.</summary>
        public Vec2[] StripCorners()
        {
            var p = Projection(MapRectPoints);
            var r = StripPoints;
            return [p.Unmap(new(r.Left, r.Top)), p.Unmap(new(r.Right, r.Top)), p.Unmap(new(r.Right, r.Bottom)), p.Unmap(new(r.Left, r.Bottom))];
        }
    }

    public static Layout LayoutFor(CourseLayers layers, Hole hole, HoleSheetOptions o)
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
        var frame = MapRectPoints;
        (double, double) across;
        SKRect strip;
        if (o.Layout == HoleSheetLayout.FullWidth)
        {
            double aspect = frame.Width / frame.Height;
            double half = (a1 - a0) * aspect / 2;
            if (need > half)
            {
                // A wide dogleg: keep the clearance and show more of the hole's length instead.
                double extra = 2 * need / aspect - (a1 - a0);
                a0 -= extra / 2; a1 += extra / 2; half = need;
            }
            across = (centre - half, centre + half);
            strip = frame;
        }
        else
        {
            // SideYards past the route's widest point each side, measured from the tee→green line. The hole's whole
            // length fills the frame's height, unless the strip would crowd out the notes column.
            double sideM = o.SideYards / TerrainSurvey.YardsPerMetre;
            var offs = shown.Select(Across).ToList();
            across = (Math.Min(0, offs.Min()) - sideM, Math.Max(0, offs.Max()) + sideM);
            double len = a1 - a0, wid = across.Item2 - across.Item1;
            double k = Math.Min(frame.Height / len, (frame.Width - o.NotesMinWidthPoints) / wid);
            float w = (float)(wid * k), h = (float)(len * k);
            strip = new SKRect(frame.Left, frame.MidY - h / 2, frame.Left + w, frame.MidY + h / 2);
        }
        var window = new List<Vec2>
        {
            back + fwd * a0 + side * across.Item1, back + fwd * a0 + side * across.Item2,
            back + fwd * a1 + side * across.Item2, back + fwd * a1 + side * across.Item1,
        };
        return new Layout(back, fwd, side, rotation, (a0, a1), across, window, strip, o.Layout);
    }

    /// <param name="sameGround">A sheet of the same hole with the same options: its ground is reused, so only the
    /// contours and heights are worked out again for <paramref name="datumMetres"/>.</param>
    public static HoleSheet Build(CourseModel course, CourseLayers layers, Hole hole, HoleSheetOptions o, double datumMetres,
        bool datumIsSeaLevel = false, HoleSheet? sameGround = null)
    {
        var layout = layers.Holes.Single(h => h.Hole.Number == hole.Number);
        var (back, fwd, side, rotation, (a0, a1), across, window, strip, kind) = LayoutFor(layers, hole, o);
        double Across(Vec2 p) => (p - back).X * side.X + (p - back).Z * side.Z;

        var line = layout.Route.Prepend(back).Concat(hole.Tees).ToList();
        bool NearPlay(Vec2 p) => InWindow(p) && line.Zip(line.Skip(1)).Any(s => SegmentDistance(p, s.First, s.Second) <= o.CorridorMetres);
        bool InWindow(Vec2 p) =>
            ((p - back).X * fwd.X + (p - back).Z * fwd.Z) is var a && a >= a0 && a <= a1 && Across(p) is var c && c >= across.Item1 && c <= across.Item2;

        // Terrain over the window's bounding box, contour interval from the window itself.
        double u = o.Dpi / 72;
        var projection = MapProjection.Fit(window, strip, rotation, padding: 1.0);
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
        var survey = TerrainSurvey.Build(course, map, layers, min, max, NearPlay, datumMetres, sameGround?.Survey);
        return new HoleSheet
        {
            Routing = survey.Holes.Single(h => h.Hole.Number == hole.Number), Survey = survey,
            BackTee = back, Forward = fwd, Side = side, RotationDegrees = rotation, Along = (a0, a1), Across = across, Window = window,
            StripPoints = strip, Kind = kind,
            DatumIsSeaLevel = datumIsSeaLevel || survey.SeaLevelMetres is not null,
        };
    }
}
