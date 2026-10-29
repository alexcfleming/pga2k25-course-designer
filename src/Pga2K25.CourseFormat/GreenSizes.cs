namespace Pga2K25.CourseFormat;

/// <summary>
/// The putting-surface outline of a hole's green. A drawn green is the smallest closed green spline that contains
/// the green anchor or a pin, lightly Fourier-smoothed (the outline every greens-book page draws), merged with any
/// green splines that overlap it, since the game shows overlapping greens as one surface. A green the game
/// generates has no outline in the file, so it gets a stand-in ellipse and <see cref="Estimated"/> is set.
/// </summary>
public sealed record GreenOutline(IReadOnlyList<Vec2> Points, bool Estimated)
{
    /// <summary>Default semi-axes (m) of the stand-in ellipse for a green with no stored spline.</summary>
    public const double EstimatedSemiMajorMetres = 15, EstimatedSemiMinorMetres = 12;
    public const int DefaultHarmonics = 8;

    /// <summary>Unit vector of the approach (second-last waypoint → green anchor).</summary>
    public static Vec2 Approach(Hole hole) =>
        hole.Route.Count >= 2 ? (hole.GreenAnchor - hole.Route[^2]).Normalized() : new Vec2(0, 1);

    public static GreenOutline Resolve(CourseModel course, Hole hole, int harmonics = DefaultHarmonics,
        double semiMajor = EstimatedSemiMajorMetres, double semiMinor = EstimatedSemiMinorMetres)
    {
        var probes = new List<Vec2> { hole.GreenAnchor };
        probes.AddRange(hole.Pins);
        var greens = course.SurfaceSplines
            .Where(s => s.Surface == Surfaces.Green && s.Closed && s.Points.Count >= 3)
            .Select(s => Geometry.SampleSpline(s, 24))
            .ToList();
        var best = greens
            .Where(poly => probes.Any(p => Geometry.Contains(poly, p)))
            .OrderBy(Geometry.Area)
            .FirstOrDefault();
        if (best is not null)
        {
            // A green can be drawn as several overlapping splines (Devil's Perch 2 and 8): the game shows them as one
            // surface, so merge every green spline that overlaps this one, and those that overlap them.
            var parts = new List<List<Vec2>> { best };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var g in greens.Where(g => !parts.Contains(g) && parts.Any(p => Geometry.Overlaps(p, g))).ToList())
                {
                    parts.Add(g);
                    grew = true;
                }
            }
            var smoothed = parts.Select(p => (IReadOnlyList<Vec2>)Geometry.FourierSmooth(p, Math.Max(harmonics * 2, 12))).ToList();
            return new(Geometry.Union(smoothed), false);
        }

        // No stored outline: an approach-aligned ellipse, grown until every pin sits 3 m inside.
        var forward = Approach(hole);
        double a = semiMajor, b = semiMinor, grow = 1;
        var side = new Vec2(-forward.Z, forward.X);
        foreach (var pin in hole.Pins)
        {
            var d = pin - hole.GreenAnchor;
            double u = Math.Abs(d.X * forward.X + d.Z * forward.Z) + 3;
            double v = Math.Abs(d.X * side.X + d.Z * side.Z) + 3;
            grow = Math.Max(grow, Math.Sqrt(u * u / (a * a) + v * v / (b * b)));
        }
        return new(Geometry.Ellipse(hole.GreenAnchor, forward, a * grow, b * grow), true);
    }
}

/// <param name="AreaSquareMetres">Area inside the outline.</param>
/// <param name="PerimeterMetres">Length of the outline as drawn (the smoothed spline).</param>
/// <param name="Estimated">No green spline in the file: the size is a stand-in and is left out of every statistic.</param>
public sealed record GreenSize(int Hole, int Par, double AreaSquareMetres, double PerimeterMetres, bool Estimated)
{
    public double SquareFeet => AreaSquareMetres * GreenSizes.SquareFeetPerSquareMetre;
    public double PerimeterYards => PerimeterMetres * GreenSizes.YardsPerMetre;
}

/// <summary>
/// Green sizes across a course: the area and perimeter of every green, and the mean, median, spread and size rank
/// of the built (drawn) greens. Estimated greens have no real outline, so they are listed but never counted.
/// </summary>
public sealed class GreenSizes
{
    public const double SquareFeetPerSquareMetre = 10.7639104, YardsPerMetre = 1.0936133;

    /// <summary>Target perimeter band (yd): an outline between these lengths is on target.</summary>
    public const double TargetMinYards = 100, TargetMaxYards = 120;

    public IReadOnlyList<GreenSize> Greens { get; }
    public IReadOnlyList<GreenSize> Built { get; }

    public double MeanSquareFeet { get; }
    public double MedianSquareFeet { get; }
    /// <summary>Population standard deviation of the built greens' areas.</summary>
    public double StdDevSquareFeet { get; }

    GreenSizes(IReadOnlyList<GreenSize> greens)
    {
        Greens = greens;
        Built = greens.Where(g => !g.Estimated).ToList();
        if (Built.Count == 0) return;
        var areas = Built.Select(g => g.SquareFeet).OrderBy(a => a).ToList();
        MeanSquareFeet = areas.Average();
        int n = areas.Count;
        MedianSquareFeet = n % 2 == 1 ? areas[n / 2] : (areas[n / 2 - 1] + areas[n / 2]) / 2;
        StdDevSquareFeet = Math.Sqrt(areas.Sum(a => (a - MeanSquareFeet) * (a - MeanSquareFeet)) / n);
    }

    public static GreenSizes From(CourseModel course, int harmonics = GreenOutline.DefaultHarmonics,
        double semiMajor = GreenOutline.EstimatedSemiMajorMetres, double semiMinor = GreenOutline.EstimatedSemiMinorMetres) =>
        new(course.Holes.OrderBy(h => h.Number)
            .Select(h => Measure(h, GreenOutline.Resolve(course, h, harmonics, semiMajor, semiMinor))).ToList());

    public static GreenSizes From(IEnumerable<GreenSize> greens) => new(greens.OrderBy(g => g.Hole).ToList());

    public static GreenSize Measure(Hole hole, GreenOutline outline) =>
        new(hole.Number, hole.Par, Geometry.Area(outline.Points), Geometry.Perimeter(outline.Points), outline.Estimated);

    public GreenSize? For(int hole) => Greens.FirstOrDefault(g => g.Hole == hole);

    /// <summary>Size rank among the built greens, 1 = largest (ties share the better rank); null for an estimated green.</summary>
    public int? Rank(int hole) =>
        For(hole) is { Estimated: false } g ? 1 + Built.Count(b => b.SquareFeet > g.SquareFeet) : null;

    /// <summary>
    /// Yards outside the target band: negative when short of the minimum, positive when over the maximum, 0 inside.
    /// Judged on the length as printed (0.1 yd), so a page never shows "120.0 yd" as over.
    /// </summary>
    public static double PerimeterOffTarget(double perimeterYards)
    {
        double yd = Math.Round(perimeterYards, 1);
        return yd < TargetMinYards ? yd - TargetMinYards : yd > TargetMaxYards ? yd - TargetMaxYards : 0;
    }
}
