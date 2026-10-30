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

    /// <summary>
    /// The buildable ground (gentle ground connected to the clubhouse and practice facilities, e.g. a mesa's
    /// plateau) plus <see cref="TerrainMapOptions.BuildableMarginMetres"/>.
    /// </summary>
    Buildable,
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

    /// <summary>Margin round the buildable ground for <see cref="MapExtent.Buildable"/>: enough to show where it falls away.</summary>
    public double BuildableMarginMetres { get; init; } = 40;

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
    /// Sea level (world m). Null uses the placed ocean props' height when there are any, otherwise the game's
    /// sea plane at world 0 m (Berwick Coast has no ocean props, yet its in-game coastline follows 0 m).
    /// Ground below it is drawn as water and not contoured; a window that never dips below it has no sea.
    /// </summary>
    public double? SeaLevelMetres { get; init; }
    public bool ShowWater { get; init; } = true;

    /// <summary>Dashed outlines of the water planes' rectangles: where lowering the ground would flood.</summary>
    public bool ShowWaterFootprints { get; init; }

    /// <summary>Spot heights on hilltops.</summary>
    public bool ShowSummits { get; init; } = true;

    /// <summary>Tick marks along each route at this spacing (yards), with a number every second tick.</summary>
    public int TickYards { get; init; } = 50;

    /// <summary>Radius (m) of the green-site circle drawn at each green and used for its slope figure.</summary>
    public double GreenSiteRadiusMetres { get; init; } = 14;

    /// <summary>
    /// Draw the green-site circle and fall arrow on holes whose green spline is drawn. False keeps them only on
    /// greens not yet drawn: the spline shows the green itself, and the game levels it when it's built.
    /// </summary>
    public bool ShowDrawnGreenSites { get; init; } = true;

    /// <summary>Whether a hole's green-site circle is drawn (see <see cref="ShowDrawnGreenSites"/>).</summary>
    public bool ShowsGreenSite(HoleRouting h) => ShowDrawnGreenSites || !h.GreenSiteIsDrawn;

    /// <summary>What the designer planted, by layer (see <see cref="Planting"/>); none by default.</summary>
    public PlantingLayers Planting { get; init; } = PlantingLayers.None;

    /// <summary>Conifers darker than broadleaf trees; false draws every tree in one green.</summary>
    public bool TreesByType { get; init; } = true;

    public string Credit { get; init; } = "";

    /// <summary>Where the course is, printed before the subtitle. Blank takes the course's Place.json.</summary>
    public string Place { get; init; } = "";

    /// <summary>A site locator (the whole plot, its sea and the course) at the top of the panel. Place.json can turn it on.</summary>
    public bool SiteInset { get; init; }

    /// <summary>A generated region drawn as the panel's locator, with the site inset in its corner. Wins over <see cref="SiteInset"/>.</summary>
    public Maps.Region? Region { get; init; }

    /// <summary>The region's name (place line 1).</summary>
    public string RegionName { get; init; } = "";

    /// <summary>Also write "&lt;course&gt; - Terrain Map.pdf": the same sheet, with contours, routing and text as vectors.</summary>
    public bool Pdf { get; init; }

    /// <summary>The PDF's page size: sheet pixels ÷ this = inches (raised if the page would pass PDF's 200 in limit).</summary>
    public double PrintDpi { get; init; } = 250;
}

/// <summary>A traced contour in world coordinates.</summary>
public sealed record ContourLine(double Feet, bool Index, IReadOnlyList<Vec2> Points);

public sealed record SpotHeight(Vec2 Position, double Feet);

/// <summary>A named facility found among the placed props (clubhouse, range, practice green, helipad).</summary>
/// <param name="Label">Short text drawn beside it, e.g. a range marker's "150".</param>
public sealed record Landmark(string Name, Vec2 Position, string Label, bool Minor = false);

/// <param name="Route">Back tee → aim points → green anchor.</param>
/// <param name="Yards">Back-tee length by the game's rule (what the scorecard shows).</param>
/// <param name="LegYards">Length of each route leg.</param>
/// <param name="GreenFeet">Ground at the green site's centre.</param>
/// <param name="GreenSiteSlopePct">Mean slope over the green site (bumps count as much as tilt).</param>
/// <param name="GreenSiteFallDirection">World direction the site's best-fit plane falls towards (unit; zero when flat).</param>
public sealed record HoleRouting(Hole Hole, IReadOnlyList<Vec2> Route, int Yards, IReadOnlyList<int> LegYards,
    double TeeFeet, double GreenFeet, double GreenSiteSlopePct, Vec2 GreenSiteFallDirection)
{
    public double ChangeFeet => GreenFeet - TeeFeet;

    /// <summary>Grade of the best-fit plane through the green site (%): the site's overall tilt.</summary>
    public double GreenSiteGradePct { get; init; }

    /// <summary>Largest departure of the ground from that plane inside the site (ft): its lumpiness.</summary>
    public double GreenSiteReliefFeet { get; init; }

    /// <summary>
    /// Centre of the green site: the drawn green's centroid when a green is already drawn at the anchor
    /// (<see cref="GreenSiteIsDrawn"/>, and the figures cover that green), otherwise the route's last point.
    /// </summary>
    public Vec2 GreenSiteCentre { get; init; }

    public bool GreenSiteIsDrawn { get; init; }
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

    Planting? _planting;
    /// <summary>What the designer planted, sorted into layers (read once, when first drawn).</summary>
    public Planting Planting => _planting ??= Planting.From(Course);
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

    /// <summary>
    /// Inland water from the course's <c>water[]</c> entries (painted streams and water planes, see
    /// <see cref="WaterTracer"/>), traced over <see cref="Field"/>; null when there is none on the sheet.
    /// Sea is separate (<see cref="IsWater(double)"/>).
    /// </summary>
    public WaterMap? InlandWater { get; init; }

    /// <summary>Inland water, one flag per <see cref="Field"/> cell; empty when there is none.</summary>
    public bool[] PaintedWater => InlandWater?.Wet ?? [];

    /// <summary>Shorelines of the inland water (world coordinates).</summary>
    public List<List<Vec2>> PaintedWaterEdges => InlandWater?.Outlines ?? [];

    /// <summary>Sea or painted water at a world point.</summary>
    public bool IsWet(Vec2 p)
    {
        if (IsWater(Field.Sample(p))) return true;
        if (PaintedWater.Length == 0) return false;
        int ix = (int)Math.Floor((p.X - Field.Origin.X) / Field.Cell), iz = (int)Math.Floor((p.Z - Field.Origin.Z) / Field.Cell);
        return ix >= 0 && iz >= 0 && ix < Field.Width && iz < Field.Height && PaintedWater[iz * Field.Width + ix];
    }
    public required double MaxFeet { get; init; }

    /// <summary>Lowest and highest ground (ft) within the corridor around the routing.</summary>
    public required (double Low, double High) CorridorFeet { get; init; }
    public required double IntervalFeet { get; init; }
    public required int IndexEvery { get; init; }
    public required List<ContourLine> Contours { get; init; }
    public required List<SpotHeight> Summits { get; init; }
    public required List<HoleRouting> Holes { get; init; }

    /// <summary>Clubhouse, driving range (tee line and yardage markers), practice green, helipad.</summary>
    public List<Landmark> Landmarks { get; init; } = [];

    public double Feet(Vec2 p) => (Field.Sample(p) - DatumMetres) * FeetPerMetre;

    /// <summary>Slope (%) at a world point.</summary>
    public double SlopePct(Vec2 p) => Field.Gradient(p).Length * 100;

    /// <summary>The plot sheet: routing (or whole plot) window, interval from the ground near play.</summary>
    public static TerrainSurvey Build(CourseModel course, TerrainMapOptions o)
    {
        var layers = CourseLayers.From(course);
        var (min, max) = Window(course, layers, o with { Extent = o.Extent == MapExtent.Buildable ? MapExtent.FullPlot : o.Extent });
        var landmarks = FindLandmarks(course, min, max).ToList();
        Func<Vec2, bool> focus = p => layers.DistanceToPlay(p) <= o.CorridorMetres;
        double? datum = null;
        if (layers.Holes.Count == 0 || o.Extent == MapExtent.Buildable)
        {
            // Buildable ground: seeded at the facilities (and any routing), searched over the whole plot.
            var seeds = landmarks.Where(l => !l.Minor).Select(l => l.Position).Concat(layers.Holes.SelectMany(h => h.Route)).ToList();
            var ground = BuildableGround(course, o, min, max, seeds);
            // A blank plot has no play to focus on: the buildable ground sets the contour interval instead.
            if (layers.Holes.Count == 0) focus = ground.Contains;
            if (o.Extent == MapExtent.Buildable && ground.Bounds is var (bMin, bMax))
            {
                var m = new Vec2(o.BuildableMarginMetres, o.BuildableMarginMetres);
                (min, max) = (new Vec2(Math.Max(min.X, bMin.X - m.X), Math.Max(min.Z, bMin.Z - m.Z)),
                              new Vec2(Math.Min(max.X, bMax.X + m.X), Math.Min(max.Z, bMax.Z + m.Z)));
                datum = ground.LowestMetres;   // heights stay those of the whole-plot map
                landmarks = landmarks.Where(l => l.Position.X >= min.X && l.Position.X <= max.X && l.Position.Z >= min.Z && l.Position.Z <= max.Z).ToList();
            }
        }
        var survey = Build(course, o, layers, min, max, focus, datumMetres: datum);
        survey.Landmarks.AddRange(landmarks);
        return survey;
    }

    /// <summary>
    /// The gentle ground (slope ≤ <paramref name="maxSlopePct"/> % over ~50 m) connected to the clubhouse and practice
    /// facilities, found by flood fill on an 8 m lattice; without facilities, the largest such area. On a mesa this is the plateau
    /// and not the cliffs or the lowland around it, so the contour interval suits where the holes will go.
    /// Falls back to the whole sheet when the area is tiny.
    /// </summary>
    /// <param name="Bounds">World bounding box of the area, or null when it fell back to the whole sheet.</param>
    /// <param name="LowestMetres">Lowest ground on the whole plot (so a cropped sheet keeps the plot's 0 ft).</param>
    sealed record Ground(Func<Vec2, bool> Contains, (Vec2 Min, Vec2 Max)? Bounds, double LowestMetres);

    static Ground BuildableGround(CourseModel course, TerrainMapOptions o, Vec2 min, Vec2 max, List<Vec2> seeds,
        double maxSlopePct = 20)
    {
        const double cell = 8;
        var f = TerrainReplay.Build(course, min, max, o.Terrain with { CellMetres = cell });
        int w = f.Width, h = f.Height;
        // Slope over a ~50 m baseline: mounds and hollows smaller than that (bunker-sized brush work) don't
        // break the ground up, while cliffs and long banks still bound it.
        const int r = 3;
        var gentle = new bool[w * h];
        for (int iz = r; iz < h - r; iz++)
            for (int ix = r; ix < w - r; ix++)
            {
                double gx = (f[ix + r, iz] - f[ix - r, iz]) / (2 * r * cell), gz = (f[ix, iz + r] - f[ix, iz - r]) / (2 * r * cell);
                gentle[iz * w + ix] = Math.Sqrt(gx * gx + gz * gz) * 100 <= maxSlopePct;
            }

        var label = new int[w * h];
        int next = 0;
        var sizes = new List<int> { 0 };
        for (int start = 0; start < label.Length; start++)
        {
            if (!gentle[start] || label[start] != 0) continue;
            next++;
            int size = 0;
            var stack = new Stack<int>();
            stack.Push(start);
            label[start] = next;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                size++;
                int x = k % w, z = k / w;
                foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                {
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int n = nz * w + nx;
                    if (gentle[n] && label[n] == 0) { label[n] = next; stack.Push(n); }
                }
            }
            sizes.Add(size);
        }

        int Cell(Vec2 p) => Math.Clamp((int)((p.Z - f.Origin.Z) / cell), 0, h - 1) * w + Math.Clamp((int)((p.X - f.Origin.X) / cell), 0, w - 1);
        // Seed regions: those holding a facility (or the nearest gentle cell to it); otherwise the largest region.
        var chosen = new HashSet<int>();
        foreach (var s in seeds)
        {
            int k = Cell(s);
            if (label[k] == 0)
            {
                double best = double.MaxValue;
                for (int i = 0; i < label.Length; i++)
                {
                    if (label[i] == 0) continue;
                    double d = Vec2.Distance(f.CellCentre(i % w, i / w), s);
                    if (d < best) { best = d; k = i; }
                }
            }
            if (label[k] != 0) chosen.Add(label[k]);
        }
        if (chosen.Count == 0 && sizes.Count > 1) chosen.Add(Enumerable.Range(1, sizes.Count - 1).MaxBy(i => sizes[i]));
        double lowest = f.Values.Min();
        if (chosen.Sum(i => sizes[i]) < 50) return new Ground(_ => true, null, lowest);
        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
        for (int i = 0; i < label.Length; i++)
        {
            if (!chosen.Contains(label[i])) continue;
            var c = f.CellCentre(i % w, i / w);
            x0 = Math.Min(x0, c.X); z0 = Math.Min(z0, c.Z); x1 = Math.Max(x1, c.X); z1 = Math.Max(z1, c.Z);
        }
        return new Ground(p => chosen.Contains(label[Cell(p)]), (new Vec2(x0 - cell / 2, z0 - cell / 2), new Vec2(x1 + cell / 2, z1 + cell / 2)), lowest);
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
        // Keep the whole sheet legible too: at most ~120 index levels over mountains and cliffs.
        if (o.ContourIntervalFeet is not > 0)
            while ((maxFeet - minFeet) / (interval * indexEvery) > 120) interval = ChooseInterval(maxFeet - minFeet, (int)((maxFeet - minFeet) / interval) - 1);

        // Thin minor contours where slope packs them tighter than the spacing limit.
        double maxMinorSlope = o.MinContourSpacingPixels > 0
            ? interval / FeetPerMetre / (o.MinContourSpacingPixels / o.PixelsPerMetre)
            : double.PositiveInfinity;
        // Thinning leaves slivers where the slope hovers at the limit; drop minor pieces shorter than a few spacings.
        double minFragment = o.MinContourSpacingPixels > 0 ? 6 * o.MinContourSpacingPixels / o.PixelsPerMetre : 0;
        var contours = TraceContours(field, datum, minFeet, maxFeet, interval, indexEvery, maxMinorSlope)
            .Where(c => c.Index || Length(c.Points) >= minFragment).ToList();
        var inland = o.ShowWater && course.Water.Count > 0 ? WaterTracer.Trace(course, field) : null;
        var survey = new TerrainSurvey
        {
            Course = course, Layers = layers, Min = min, Max = max, Field = field, DatumMetres = datum, MaxFeet = maxFeet,
            CorridorFeet = corridorFeet, SeaLevelMetres = sea,
            Shoreline = sea is { } shore ? Trace(field, shore + ShoreToleranceMetres) : [],
            InlandWater = inland is { Any: true } ? inland : null,
            IntervalFeet = interval, IndexEvery = indexEvery, Contours = contours, Summits = [], Holes = [],
        };
        if (o.ShowSummits) survey.Summits.AddRange(FindSummits(survey, interval));
        foreach (var h in layers.Holes) survey.Holes.Add(Route(survey, h, o));
        return survey;
    }

    /// <summary>Area centroid of a simple polygon (vertex mean when degenerate).</summary>
    static Vec2 Centroid(IReadOnlyList<Vec2> poly)
    {
        double a = 0, cx = 0, cz = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            double cross = poly[j].X * poly[i].Z - poly[i].X * poly[j].Z;
            a += cross; cx += (poly[j].X + poly[i].X) * cross; cz += (poly[j].Z + poly[i].Z) * cross;
        }
        return Math.Abs(a) < 1e-9 ? new Vec2(poly.Average(q => q.X), poly.Average(q => q.Z)) : new Vec2(cx / (3 * a), cz / (3 * a));
    }

    static double Length(IReadOnlyList<Vec2> pts)
    {
        double sum = 0;
        for (int i = 1; i < pts.Count; i++) sum += Vec2.Distance(pts[i - 1], pts[i]);
        return sum;
    }

    /// <inheritdoc cref="TerrainReplay.SeaPlaneMetres"/>
    public const double SeaPlaneMetres = TerrainReplay.SeaPlaneMetres;

    /// <inheritdoc cref="TerrainReplay.DetectSeaLevel"/>
    public static double DetectSeaLevel(CourseModel course) => TerrainReplay.DetectSeaLevel(course);

    /// <inheritdoc cref="TerrainReplay.OceanPropLevel"/>
    public static double? OceanPropLevel(CourseModel course) => TerrainReplay.OceanPropLevel(course);

    static List<List<Vec2>> Trace(Heightfield field, double level)
    {
        var all = new bool[field.Values.Length];
        Array.Fill(all, true);
        return MarchingSquares.Trace(field, field.Values, all, level).Where(l => l.Count >= 2).ToList();
    }

    static (Vec2 Min, Vec2 Max) Window(CourseModel course, CourseLayers layers, TerrainMapOptions o)
    {
        // No holes yet (a blank plot with a clubhouse and range): always the whole plot.
        bool full = (o.Extent == MapExtent.FullPlot || layers.Holes.Count == 0) && course.HeightStamps.Count > 0;
        IEnumerable<Vec2> pts = full
            ? course.HeightStamps.Select(s => s.Position)
            : layers.Holes.SelectMany(h => h.Route.Concat(h.Hole.Tees));
        var list = pts.ToList();
        if (list.Count == 0) throw new InvalidOperationException("The course has no holes or terrain to map.");
        double m = full ? 0 : o.MarginMetres;
        return (new Vec2(list.Min(p => p.X) - m, list.Min(p => p.Z) - m), new Vec2(list.Max(p => p.X) + m, list.Max(p => p.Z) + m));
    }

    /// <summary>
    /// Facilities worth routing around, from the placed props: the clubhouse, the driving range (its tee line from
    /// the dividers, bag stands and ball pyramids, plus each yardage marker), the practice green (its flags) and a
    /// helipad. Several props of one kind are drawn once, at their centre.
    /// </summary>
    public static IEnumerable<Landmark> FindLandmarks(CourseModel course, Vec2 min, Vec2 max)
    {
        bool Inside(Vec2 p) => p.X >= min.X && p.X <= max.X && p.Z >= min.Z && p.Z <= max.Z;
        string Name(PlacedObject p) => p.AssetPath[(p.AssetPath.LastIndexOf('/') + 1)..];
        var props = course.PlacedObjects.Where(p => Inside(p.Position)).ToList();

        IEnumerable<Landmark> Group(string name, Func<string, bool> match, string label)
        {
            var set = props.Where(p => match(Name(p))).ToList();
            if (set.Count > 0)
                yield return new Landmark(name, new Vec2(set.Average(p => p.Position.X), set.Average(p => p.Position.Z)), label);
        }
        foreach (var l in Group("Clubhouse", n => n.Contains("Clubhouse", StringComparison.OrdinalIgnoreCase), "Clubhouse")) yield return l;
        foreach (var l in Group("Driving range", n => n.StartsWith("RangeDivider") || n.StartsWith("RangeBagStand") || n.StartsWith("RangeBallPyramid"), "Range tee")) yield return l;
        foreach (var p in props.Where(p => Name(p).StartsWith("RangeMarker")))
        {
            // RangeMarkerA150Prefab → "150".
            string digits = new(Name(p).SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            yield return new Landmark("Range marker", p.Position, int.TryParse(digits, out int y) ? y.ToString() : "", Minor: true);
        }
        foreach (var l in Group("Practice green", n => n.StartsWith("PuttingGreenFlag"), "Practice green")) yield return l;
        foreach (var l in Group("Helipad", n => n.StartsWith("Helipad"), "Helipad")) yield return l;
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
        var notCliff = all;
        if (double.IsFinite(maxMinorSlope))
        {
            gentle = new bool[all.Length];
            notCliff = new bool[all.Length];
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
                    double g2 = gx * gx + gz * gz;
                    gentle[iz * w + ix] = g2 <= maxMinorSlope * maxMinorSlope;
                    notCliff[iz * w + ix] = g2 <= 4 * maxMinorSlope * maxMinorSlope;   // index lines thin at twice the slope
                }
            });
        }
        if (n < k0) return [];
        var byLevel = new List<ContourLine>[n - k0 + 1];
        Parallel.For(k0, n + 1, k =>
        {
            double feet = k * interval;
            bool index = k % indexEvery == 0;
            // Every fifth index line (e.g. each 50 ft) runs everywhere so cliffs still read; others thin with the slope.
            var mask = !index ? gentle : k % (indexEvery * 5) == 0 ? all : notCliff;
            byLevel[k - k0] = MarchingSquares.Trace(field, field.Values, mask, datum + feet / FeetPerMetre)
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

        // Green site: the drawn green when there is one at the anchor (the route's last point can sit off its centre),
        // otherwise a disc around the anchor; sampled on a 2 m lattice.
        var anchor = hole.GreenAnchor;
        double r = o.GreenSiteRadiusMetres;
        var drawn = s.Layers.Greens.Where(g => g.Count >= 3)
            .Select(g => (Poly: g, Centre: Centroid(g)))
            .Where(g => Geometry.Contains(g.Poly, anchor) || Geometry.DistanceToEdge(g.Poly, anchor) <= 10)
            .OrderBy(g => Vec2.Distance(g.Centre, anchor))
            .FirstOrDefault();
        var centre = drawn.Poly is null ? anchor : drawn.Centre;
        var pts = new List<Vec2>();
        if (drawn.Poly is { } poly)
        {
            double x0 = poly.Min(q => q.X), x1 = poly.Max(q => q.X), z0 = poly.Min(q => q.Z), z1 = poly.Max(q => q.Z);
            for (double x = x0 + 1; x <= x1; x += 2)
                for (double z = z0 + 1; z <= z1; z += 2)
                    if (Geometry.Contains(poly, new Vec2(x, z))) pts.Add(new Vec2(x, z));
        }
        if (pts.Count < 3)
            for (double dx = -r; dx <= r; dx += 2)
                for (double dz = -r; dz <= r; dz += 2)
                    if (dx * dx + dz * dz <= r * r) pts.Add(anchor + new Vec2(dx, dz));

        double slope = pts.Average(q => s.Field.Gradient(q).Length) * 100;
        // Best-fit plane h = a + bx·x + bz·z (least squares about the mean): its grade and fall, and the worst residual.
        var hs = pts.Select(q => s.Field.Sample(q)).ToList();
        double mx = pts.Average(q => q.X), mz = pts.Average(q => q.Z), mh = hs.Average();
        double sxx = 0, sxz = 0, szz = 0, sxh = 0, szh = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            double x = pts[i].X - mx, z = pts[i].Z - mz, hh = hs[i] - mh;
            sxx += x * x; sxz += x * z; szz += z * z; sxh += x * hh; szh += z * hh;
        }
        double det = sxx * szz - sxz * sxz;
        double bx = det > 1e-9 ? (sxh * szz - szh * sxz) / det : 0, bz = det > 1e-9 ? (szh * sxx - sxh * sxz) / det : 0;
        double grade = Math.Sqrt(bx * bx + bz * bz) * 100;
        double relief = Enumerable.Range(0, pts.Count).Max(i => Math.Abs(hs[i] - (mh + bx * (pts[i].X - mx) + bz * (pts[i].Z - mz)))) * FeetPerMetre;
        // The plane's gradient points uphill; the site falls the other way.
        var fall = grade > 0.5 ? new Vec2(-bx, -bz).Normalized() : new Vec2(0, 0);

        return new HoleRouting(hole, route, yards, legs, s.Feet(route[0]), s.Feet(centre), slope, fall)
        {
            GreenSiteGradePct = grade, GreenSiteReliefFeet = relief, GreenSiteCentre = centre, GreenSiteIsDrawn = drawn.Poly is not null,
        };
    }
}
