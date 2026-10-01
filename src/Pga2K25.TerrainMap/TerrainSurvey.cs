using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.TerrainMap;

public enum MapExtent
{
    /// <summary>Hole routes and tees plus <see cref="TerrainMapOptions.MarginMetres"/> all round.</summary>
    Routing,

    /// <summary>Everything the terrain stamps cover (the whole 2 km plot on a stock layout).</summary>
    FullPlot,
}

public enum ReliefShading
{
    /// <summary>Pale elevation tint with soft hillshade (topographic-map look).</summary>
    Relief,

    /// <summary>Slope classes (green sites / fairway / steep / severe) with hillshade.</summary>
    Slope,

    /// <summary>Contours on plain paper.</summary>
    None,
}

public sealed record TerrainMapOptions
{
    /// <summary>
    /// Stamp replay: greens-book calibration for small brushes, a softer falloff for large sculpting
    /// brushes (see <see cref="TerrainModelOptions.LargeBrushHardness"/>); a 1 m raster is plenty at plot scale.
    /// </summary>
    public TerrainModelOptions Terrain { get; init; } = new() { CellMetres = 1.0, LargeBrushHardness = 0.3 };

    /// <summary>Extra Gaussian smoothing (sigma, m) of the replayed terrain before contouring; 0 = none.</summary>
    public double SmoothMetres { get; init; }

    public MapExtent Extent { get; init; } = MapExtent.Routing;
    public double MarginMetres { get; init; } = 150;

    /// <summary>
    /// Contour interval in feet; null picks the smallest of 1/2/5/10/… that keeps the ground within
    /// <see cref="CorridorMetres"/> of the routing to at most <see cref="MaxCorridorLevels"/> levels.
    /// </summary>
    public double? ContourIntervalFeet { get; init; }
    public int MaxCorridorLevels { get; init; } = 60;
    public double CorridorMetres { get; init; } = 80;

    /// <summary>
    /// Minor contours are left out where they would run closer than this on the page (steep ground),
    /// as on survey maps; index contours are always drawn. 0 keeps every line.
    /// </summary>
    public double MinContourSpacingPixels { get; init; } = 7;

    /// <summary>Every n-th contour is a heavier, labelled index contour.</summary>
    public int IndexEvery { get; init; } = 5;

    /// <summary>Output resolution of the map area.</summary>
    public double PixelsPerMetre { get; init; } = 4;

    public ReliefShading Shading { get; init; } = ReliefShading.Relief;

    /// <summary>Lettered reference grid at this spacing (yards); 0 hides it.</summary>
    public double GridYards { get; init; } = 100;

    /// <summary>Faint outlines of any surfaces already drawn (fairways, greens, bunkers, water, paths).</summary>
    public bool ShowFeatures { get; init; } = true;

    /// <summary>
    /// Sea level (world m). Null detects it from placed ocean props (their explicit height, 0 m on stock
    /// plots); with no ocean on the plot there is no sea. Ground below it is drawn as water and not contoured.
    /// </summary>
    public double? SeaLevelMetres { get; init; }
    public bool ShowWater { get; init; } = true;

    /// <summary>Spot heights on hilltops.</summary>
    public bool ShowSummits { get; init; } = true;

    /// <summary>Tick marks along each route at this spacing (yards), with a number every second tick.</summary>
    public int TickYards { get; init; } = 50;

    /// <summary>Radius (m) of the green-site circle drawn at each green and used for its slope figure.</summary>
    public double GreenSiteRadiusMetres { get; init; } = 14;

    public string Credit { get; init; } = "";
}

/// <summary>A traced contour in world coordinates.</summary>
public sealed record ContourLine(double Feet, bool Index, IReadOnlyList<Vec2> Points);

public sealed record SpotHeight(Vec2 Position, double Feet);

/// <param name="Route">Back tee → aim points → green anchor.</param>
/// <param name="Yards">Back-tee length by the game's rule (what the scorecard shows).</param>
/// <param name="LegYards">Length of each route leg.</param>
/// <param name="GreenSiteSlopePct">Mean slope inside the green-site circle.</param>
/// <param name="GreenSiteFallDirection">World direction the green site falls towards (unit; zero when flat).</param>
public sealed record HoleRouting(Hole Hole, IReadOnlyList<Vec2> Route, int Yards, IReadOnlyList<int> LegYards,
    double TeeFeet, double GreenFeet, double GreenSiteSlopePct, Vec2 GreenSiteFallDirection)
{
    public double ChangeFeet => GreenFeet - TeeFeet;
}

/// <summary>
/// Everything the terrain map draws, in world space: the replayed heightfield over the map window,
/// contour lines, hilltop spot heights and per-hole routing figures. Elevations are reported in feet
/// above the lowest point of the window (the game's own zero is arbitrary).
/// </summary>
public sealed class TerrainSurvey
{
    public const double FeetPerMetre = 3.2808399;
    public const double YardsPerMetre = 1.0936133;

    public required CourseModel Course { get; init; }
    public required CourseLayers Layers { get; init; }
    public required Vec2 Min { get; init; }
    public required Vec2 Max { get; init; }
    public required Heightfield Field { get; init; }

    /// <summary>World elevation (m) shown as 0 ft: sea level when the plot has a sea, else the lowest ground.</summary>
    public required double DatumMetres { get; init; }

    /// <summary>Sea level (world m), or null when the plot has no sea (or water is switched off).</summary>
    public double? SeaLevelMetres { get; init; }

    /// <summary>The traced shoreline (world coordinates); empty without a sea.</summary>
    public List<List<Vec2>> Shoreline { get; init; } = [];

    /// <summary>
    /// The plot's untouched base is a flat at exactly 0 m with the sea surface a hair above it (y = 1e-6),
    /// so ground at sea level counts as water; the tolerance absorbs blur at the edge of the flat.
    /// </summary>
    public const double ShoreToleranceMetres = 0.02;

    public bool IsWater(double elevationMetres) => SeaLevelMetres is { } sea && elevationMetres <= sea + ShoreToleranceMetres;
    public required double MaxFeet { get; init; }

    /// <summary>Lowest and highest ground (ft) within the corridor around the routing.</summary>
    public required (double Low, double High) CorridorFeet { get; init; }
    public required double IntervalFeet { get; init; }
    public required int IndexEvery { get; init; }
    public required List<ContourLine> Contours { get; init; }
    public required List<SpotHeight> Summits { get; init; }
    public required List<HoleRouting> Holes { get; init; }

    public double Feet(Vec2 p) => (Field.Sample(p) - DatumMetres) * FeetPerMetre;

    /// <summary>Slope (%) at a world point.</summary>
    public double SlopePct(Vec2 p) => Field.Gradient(p).Length * 100;

    /// <summary>The plot sheet: routing (or whole plot) window, interval from the ground near play.</summary>
    public static TerrainSurvey Build(CourseModel course, TerrainMapOptions o)
    {
        var layers = CourseLayers.From(course);
        var (min, max) = Window(course, layers, o);
        return Build(course, o, layers, min, max, p => layers.DistanceToPlay(p) <= o.CorridorMetres, datumMetres: null);
    }

    /// <summary>
    /// Survey of an arbitrary axis-aligned window. The contour interval is chosen from the relief of the
    /// ground where <paramref name="focus"/> holds (at most <see cref="TerrainMapOptions.MaxCorridorLevels"/>
    /// levels). Without a sea, <paramref name="datumMetres"/> fixes the 0 ft level so several sheets agree;
    /// null uses the lowest ground in the window.
    /// </summary>
    public static TerrainSurvey Build(CourseModel course, TerrainMapOptions o, CourseLayers layers, Vec2 min, Vec2 max,
        Func<Vec2, bool> focus, double? datumMetres)
    {
        // Replay a little beyond the window so blur and gradients are clean at the edges.
        double padM = 4 * o.Terrain.CellMetres + 3 * o.SmoothMetres;
        var pad = new Vec2(padM, padM);
        var field = TerrainReplay.Build(course, min - pad, max + pad, o.Terrain);
        TerrainReplay.Smooth(field, o.SmoothMetres);

        double lo = double.MaxValue, hi = double.MinValue;
        for (int iz = 0; iz < field.Height; iz++)
            for (int ix = 0; ix < field.Width; ix++)
            {
                var c = field.CellCentre(ix, iz);
                if (c.X < min.X || c.X > max.X || c.Z < min.Z || c.Z > max.Z) continue;
                double v = field[ix, iz];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
        if (lo > hi) lo = hi = 0;

        double? sea = o.ShowWater ? o.SeaLevelMetres ?? DetectSeaLevel(course) : null;
        if (sea is { } level && lo > level + ShoreToleranceMetres) sea = null;   // the sea never reaches this window
        double datum = sea ?? datumMetres ?? lo;
        if (sea is not null) lo = datum;                     // nothing below the sea is contoured
        hi = Math.Max(hi, datum);
        double minFeet = (lo - datum) * FeetPerMetre, maxFeet = (hi - datum) * FeetPerMetre;
        int indexEvery = Math.Max(1, o.IndexEvery);
        var corridor = FocusRange(field, min, max, focus, sea ?? double.NegativeInfinity);
        var corridorFeet = ((corridor.Low - datum) * FeetPerMetre, (corridor.High - datum) * FeetPerMetre);
        double interval = o.ContourIntervalFeet is > 0 ? o.ContourIntervalFeet.Value
            : ChooseInterval(corridorFeet.Item2 - corridorFeet.Item1, o.MaxCorridorLevels);
        // Keep the whole sheet legible too: at most ~250 index levels over mountains.
        if (o.ContourIntervalFeet is not > 0)
            while ((maxFeet - minFeet) / (interval * indexEvery) > 250) interval = ChooseInterval(maxFeet - minFeet, (int)((maxFeet - minFeet) / interval) - 1);

        // Thin minor contours where slope packs them tighter than the spacing limit.
        double maxMinorSlope = o.MinContourSpacingPixels > 0
            ? interval / FeetPerMetre / (o.MinContourSpacingPixels / o.PixelsPerMetre)
            : double.PositiveInfinity;
        // Thinning leaves slivers where the slope hovers at the limit; drop minor pieces shorter than a few spacings.
        double minFragment = o.MinContourSpacingPixels > 0 ? 6 * o.MinContourSpacingPixels / o.PixelsPerMetre : 0;
        var contours = TraceContours(field, datum, minFeet, maxFeet, interval, indexEvery, maxMinorSlope)
            .Where(c => c.Index || Length(c.Points) >= minFragment).ToList();
        var survey = new TerrainSurvey
        {
            Course = course, Layers = layers, Min = min, Max = max, Field = field, DatumMetres = datum, MaxFeet = maxFeet,
            CorridorFeet = corridorFeet, SeaLevelMetres = sea,
            Shoreline = sea is { } shore ? Trace(field, shore + ShoreToleranceMetres) : [],
            IntervalFeet = interval, IndexEvery = indexEvery, Contours = contours, Summits = [], Holes = [],
        };
        if (o.ShowSummits) survey.Summits.AddRange(FindSummits(survey, interval));
        foreach (var h in layers.Holes) survey.Holes.Add(Route(survey, h, o));
        return survey;
    }

    static double Length(IReadOnlyList<Vec2> pts)
    {
        double sum = 0;
        for (int i = 1; i < pts.Count; i++) sum += Vec2.Distance(pts[i - 1], pts[i]);
        return sum;
    }

    /// <summary>Median explicit height of the ocean props (wave, churn, impact), or null if there are none.</summary>
    public static double? DetectSeaLevel(CourseModel course)
    {
        var heights = course.PlacedObjects
            .Where(p => p.Height is not null && p.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Height!.Value).Order().ToList();
        return heights.Count == 0 ? null : heights[heights.Count / 2];
    }

    static List<List<Vec2>> Trace(Heightfield field, double level)
    {
        var all = new bool[field.Values.Length];
        Array.Fill(all, true);
        return MarchingSquares.Trace(field, field.Values, all, level).Where(l => l.Count >= 2).ToList();
    }

    static (Vec2 Min, Vec2 Max) Window(CourseModel course, CourseLayers layers, TerrainMapOptions o)
    {
        IEnumerable<Vec2> pts = o.Extent == MapExtent.FullPlot && course.HeightStamps.Count > 0
            ? course.HeightStamps.Select(s => s.Position)
            : layers.Holes.SelectMany(h => h.Route.Concat(h.Hole.Tees));
        var list = pts.ToList();
        if (list.Count == 0) throw new InvalidOperationException("The course has no holes or terrain to map.");
        double m = o.Extent == MapExtent.FullPlot ? 0 : o.MarginMetres;
        return (new Vec2(list.Min(p => p.X) - m, list.Min(p => p.Z) - m), new Vec2(list.Max(p => p.X) + m, list.Max(p => p.Z) + m));
    }

    /// <summary>Smallest "nice" interval (ft) that keeps the number of levels within <paramref name="maxLevels"/>.</summary>
    public static double ChooseInterval(double reliefFeet, int maxLevels)
    {
        double[] nice = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500];
        foreach (var i in nice)
            if (reliefFeet / i <= Math.Max(1, maxLevels)) return i;
        return nice[^1];
    }

    /// <summary>Lowest and highest dry elevation (m) where <paramref name="focus"/> holds, on a 4 m lattice.</summary>
    static (double Low, double High) FocusRange(Heightfield field, Vec2 min, Vec2 max, Func<Vec2, bool> focus, double floor)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (double x = min.X; x <= max.X; x += 4)
            for (double z = min.Z; z <= max.Z; z += 4)
            {
                var p = new Vec2(x, z);
                if (!focus(p)) continue;
                double v = field.Sample(p);
                if (v < floor) continue;
                lo = Math.Min(lo, v); hi = Math.Max(hi, v);
            }
        return lo > hi ? (0, 0) : (lo, hi);
    }

    static List<ContourLine> TraceContours(Heightfield field, double datum, double minFeet, double maxFeet, double interval, int indexEvery,
        double maxMinorSlope)
    {
        // Level 0 is the lowest point itself unless ground dips below a fixed datum.
        int k0 = minFeet < 0 ? (int)Math.Ceiling(minFeet / interval) : 1;
        int n = (int)Math.Floor(maxFeet / interval);
        var all = new bool[field.Values.Length];
        Array.Fill(all, true);
        var gentle = all;
        if (double.IsFinite(maxMinorSlope))
        {
            gentle = new bool[all.Length];
            int w = field.Width, h = field.Height;
            Parallel.For(0, h, iz =>
            {
                // A wide stencil (about 3 m) so the mask edge is smooth rather than ragged.
                int r = Math.Max(1, (int)Math.Round(1.5 / field.Cell));
                int zm = Math.Max(0, iz - r), zp = Math.Min(h - 1, iz + r);
                for (int ix = 0; ix < w; ix++)
                {
                    int xm = Math.Max(0, ix - r), xp = Math.Min(w - 1, ix + r);
                    double gx = (field[xp, iz] - field[xm, iz]) / ((xp - xm) * field.Cell);
                    double gz = (field[ix, zp] - field[ix, zm]) / ((zp - zm) * field.Cell);
                    gentle[iz * w + ix] = gx * gx + gz * gz <= maxMinorSlope * maxMinorSlope;
                }
            });
        }
        if (n < k0) return [];
        var byLevel = new List<ContourLine>[n - k0 + 1];
        Parallel.For(k0, n + 1, k =>
        {
            double feet = k * interval;
            bool index = k % indexEvery == 0;
            byLevel[k - k0] = MarchingSquares.Trace(field, field.Values, index ? all : gentle, datum + feet / FeetPerMetre)
                .Where(l => l.Count >= 2)
                .Select(l => new ContourLine(feet, index, l))
                .ToList();
        });
        return byLevel.Where(l => l is not null).SelectMany(l => l).ToList();
    }

    /// <summary>
    /// Hilltops: highest point within ~60 m that stands at least two contour intervals (and 3 m) above
    /// the lowest ground within ~120 m. Searched on a 5 m lattice, best 60 kept, at least 120 m apart.
    /// </summary>
    static IEnumerable<SpotHeight> FindSummits(TerrainSurvey s, double intervalFeet)
    {
        const double step = 5, peakR = 60, baseR = 120, spacing = 120;
        int nx = (int)((s.Max.X - s.Min.X) / step) + 1, nz = (int)((s.Max.Z - s.Min.Z) / step) + 1;
        var h = new double[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                h[i, j] = s.Field.Sample(new Vec2(s.Min.X + i * step, s.Min.Z + j * step));

        double minRise = Math.Max(3, 2 * intervalFeet / FeetPerMetre);
        int pr = (int)(peakR / step), br = (int)(baseR / step);
        var candidates = new List<(Vec2 P, double H)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                double v = h[i, j], low = v;
                bool peak = true;
                for (int a = Math.Max(0, i - br); a <= Math.Min(nx - 1, i + br) && peak; a++)
                    for (int b = Math.Max(0, j - br); b <= Math.Min(nz - 1, j + br); b++)
                    {
                        double w = h[a, b];
                        if (Math.Abs(a - i) <= pr && Math.Abs(b - j) <= pr && (w > v || (w == v && (a, b).CompareTo((i, j)) < 0))) { peak = false; break; }
                        if (w < low) low = w;
                    }
                // Peaks on the window edge are usually the shoulder of a hill outside it.
                if (peak && !s.IsWater(v) && v - low >= minRise && i > 0 && j > 0 && i < nx - 1 && j < nz - 1)
                    candidates.Add((new Vec2(s.Min.X + i * step, s.Min.Z + j * step), v));
            }

        var kept = new List<(Vec2 P, double H)>();
        foreach (var c in candidates.OrderByDescending(c => c.H))
        {
            if (kept.Count >= 60) break;
            if (kept.All(k => Vec2.Distance(k.P, c.P) >= spacing)) kept.Add(c);
        }
        return kept.Select(k => new SpotHeight(k.P, (k.H - s.DatumMetres) * FeetPerMetre));
    }

    static HoleRouting Route(TerrainSurvey s, HoleLayout layout, TerrainMapOptions o)
    {
        var hole = layout.Hole;
        var route = layout.Route;
        int yards = (int)Math.Round(hole.TeeSetLength(Math.Max(0, hole.Tees.Count - 1)) * YardsPerMetre);
        var legs = route.Zip(route.Skip(1), (a, b) => (int)Math.Round(Vec2.Distance(a, b) * YardsPerMetre)).ToList();

        // Green site: mean gradient over a disc around the anchor (sampled on a 2 m lattice).
        var g = hole.GreenAnchor;
        double r = o.GreenSiteRadiusMetres, sumSlope = 0;
        var sumGrad = new Vec2(0, 0);
        int count = 0;
        for (double dx = -r; dx <= r; dx += 2)
            for (double dz = -r; dz <= r; dz += 2)
            {
                if (dx * dx + dz * dz > r * r) continue;
                var grad = s.Field.Gradient(g + new Vec2(dx, dz));
                sumSlope += grad.Length;
                sumGrad += grad;
                count++;
            }
        double slope = count > 0 ? sumSlope / count * 100 : 0;
        // Gradient points uphill; the site falls the other way.
        var fall = count > 0 && sumGrad.Length / count > 0.005 ? (sumGrad * -1).Normalized() : new Vec2(0, 0);

        return new HoleRouting(hole, route, yards, legs, s.Feet(route[0]), s.Feet(g), slope, fall);
    }
}
