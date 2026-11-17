namespace Pga2K25.CourseFormat;

/// <summary>
/// The built fairway, for measuring widths the same way in every product (yardage book, Publish readiness, Hole Data
/// Card): the closed fairway splines. Widths are taken edge to edge across the line of play, square to it, at a station
/// placed by straight-line distance from the back tee, as the game's measure tool and the yardage book's distance arcs
/// measure. Bunkers inside the fairway don't cut a width short. Rough drawn over a fairway can be passed as covers, but
/// no product does yet: whether the game paints a later spline on top isn't confirmed (measure lines on Nodens Estate
/// stop at rough drawn both before and after the fairway).
/// </summary>
public sealed class FairwayArea
{
    readonly List<(List<Vec2> Outline, int Index, (double X0, double X1, double Z0, double Z1) Box)> _fairways;
    readonly List<(List<Vec2> Outline, int Index, (double X0, double X1, double Z0, double Z1) Box)> _covers;

    /// <param name="fairways">Fairway outlines with their spline index (the order the game paints them).</param>
    /// <param name="covers">Rough and heavy-rough outlines with their spline index; each covers the fairways listed before it.</param>
    public FairwayArea(IEnumerable<(List<Vec2> Outline, int Index)> fairways, IEnumerable<(List<Vec2> Outline, int Index)> covers)
    {
        _fairways = fairways.Where(f => f.Outline.Count >= 3).Select(f => (f.Outline, f.Index, Box(f.Outline))).ToList();
        _covers = covers.Where(c => c.Outline.Count >= 3).Select(c => (c.Outline, c.Index, Box(c.Outline))).ToList();
    }

    /// <summary>Every closed fairway spline on the course.</summary>
    public static FairwayArea From(CourseModel course, int samplesPerSegment = 16) =>
        new(Splines(course, samplesPerSegment, Surfaces.Fairway), []);

    /// <summary>Closed splines of the given surfaces, sampled, with their spline index.</summary>
    public static List<(List<Vec2> Outline, int Index)> Splines(CourseModel course, int samplesPerSegment, params int[] surfaces) =>
        course.SurfaceSplines.Where(s => s.Closed && s.Points.Count >= 3 && surfaces.Contains(s.Surface))
            .Select(s => (Geometry.SampleSpline(s, samplesPerSegment), s.Index)).Where(x => x.Item1.Count >= 3).ToList();

    /// <summary>The rough and heavy-rough splines that can cover a fairway.</summary>
    public static List<(List<Vec2> Outline, int Index)> Covers(CourseModel course, int samplesPerSegment = 16) =>
        Splines(course, samplesPerSegment, Surfaces.Rough, Surfaces.HeavyRough);

    public IReadOnlyList<List<Vec2>> Outlines => _fairways.Select(f => f.Outline).ToList();

    static (double, double, double, double) Box(List<Vec2> p) => (p.Min(q => q.X), p.Max(q => q.X), p.Min(q => q.Z), p.Max(q => q.Z));
    static bool InBox((double X0, double X1, double Z0, double Z1) b, Vec2 q) => q.X >= b.X0 && q.X <= b.X1 && q.Z >= b.Z0 && q.Z <= b.Z1;

    /// <summary>True where the game shows fairway: inside a fairway spline and not under rough drawn after it.</summary>
    public bool Contains(Vec2 q)
    {
        foreach (var f in _fairways)
        {
            if (!InBox(f.Box, q) || !Geometry.Contains(f.Outline, q)) continue;
            bool covered = false;
            foreach (var c in _covers)
                if (c.Index > f.Index && InBox(c.Box, q) && Geometry.Contains(c.Outline, q)) { covered = true; break; }
            if (!covered) return true;
        }
        return false;
    }

    /// <summary>
    /// The fairway run across <paramref name="p"/> along <paramref name="across"/> (a unit vector): the run under it,
    /// else the nearest within <paramref name="reach"/> m, as its two ends (signed metres along <paramref name="across"/>)
    /// and how far its nearer edge is from <paramref name="p"/> (0 when under it). Null when there is none.
    /// </summary>
    public (double Lo, double Hi, double Gap)? RunAcross(Vec2 p, Vec2 across, double reach = 90, double step = 0.25)
    {
        var runs = new List<(double From, double To)>();
        double? start = null;
        for (double s = -reach; s <= reach + 1e-9; s += step)
        {
            bool inside = Contains(p + across * s);
            if (inside && start is null) start = s;
            if (!inside && start is double a) { runs.Add((a, s - step)); start = null; }
        }
        if (start is double open) runs.Add((open, reach));
        if (runs.Count == 0) return null;
        static double Gap((double From, double To) r) => r.From <= 0 && r.To >= 0 ? 0 : Math.Min(Math.Abs(r.From), Math.Abs(r.To));
        var near = runs.MinBy(Gap);
        return (near.From, near.To, Gap(near));
    }

    /// <summary>
    /// Where the line of play (back tee first) is <paramref name="metres"/> in a straight line from the back tee: the
    /// first point along it at that distance, and the heading of the leg it lies on. Null when the whole line stays
    /// closer than that.
    /// </summary>
    public static (Vec2 Point, Vec2 Direction)? Station(IReadOnlyList<Vec2> line, double metres)
    {
        if (line.Count < 2) return null;
        var tee = line[0];
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vec2 a = line[i], b = line[i + 1];
            double len = Vec2.Distance(a, b);
            if (len < 1e-9 || Vec2.Distance(tee, b) < metres) continue;
            // |a + d·t − tee| = metres: the larger root on this leg (the point going out from the tee).
            var dir = (b - a) * (1 / len);
            var f = a - tee;
            double bq = f.X * dir.X + f.Z * dir.Z, c = f.X * f.X + f.Z * f.Z - metres * metres;
            double disc = bq * bq - c;
            if (disc < 0) continue;
            double t = -bq + Math.Sqrt(disc);
            if (t < -1e-6 || t > len + 1e-6) continue;
            return (a + dir * Math.Clamp(t, 0, len), dir);
        }
        return null;
    }

    /// <summary>
    /// The width at a station: the fairway run square to the line of play there, edge to edge, as its two ends in
    /// world X/Z, and how far the run's nearer edge is from the line (0 when the line crosses it).
    /// </summary>
    public (Vec2 Left, Vec2 Right, double Gap)? WidthAt(IReadOnlyList<Vec2> line, double metres)
    {
        if (Station(line, metres) is not { } st) return null;
        var across = new Vec2(-st.Direction.Z, st.Direction.X);
        if (RunAcross(st.Point, across) is not { } r) return null;
        return (st.Point + across * r.Lo, st.Point + across * r.Hi, r.Gap);
    }
}
