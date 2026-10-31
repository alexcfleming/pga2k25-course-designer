using Pga2K25.CourseFormat;

namespace Pga2K25.Maps;

public sealed record Ribbon(IReadOnlyList<Vec2> Points, double Width);
public sealed record Disc(Vec2 Centre, double Radius);

/// <param name="Route">Back tee → aim points → green anchor (what a ball-flight line follows).</param>
public sealed record HoleLayout(Hole Hole, IReadOnlyList<Vec2> Route, Vec2 BackTee, Vec2 Direction);

/// <summary>
/// World-space map layers traced from a course: the "key features" a scorecard map shows.
/// Surface ids per <see cref="Surfaces"/>; open splines become ribbons of their stored width,
/// painted brushes become discs of their diameter.
/// </summary>
public sealed class CourseLayers
{
    public required List<List<Vec2>> Grass { get; init; }      // fairway, rough & heavy-rough areas
    public required List<List<Vec2>> Greens { get; init; }
    public required List<List<Vec2>> BunkerAreas { get; init; }
    public required List<Ribbon> BunkerRibbons { get; init; }
    public required List<Disc> BunkerBrushes { get; init; }
    public required List<List<Vec2>> WaterAreas { get; init; }
    public required List<Ribbon> WaterRibbons { get; init; }

    /// <summary>
    /// Ponds and creeks from the course's <c>water[]</c> entries (water planes and painted streams): each body's
    /// shores, outer first then islands (fill even-odd). Empty until <see cref="AddInlandWater"/>, since tracing
    /// replays the terrain and most products that use these layers trace water their own way.
    /// </summary>
    public List<IReadOnlyList<List<Vec2>>> WaterBodies { get; } = [];

    /// <summary>
    /// Adds the course's traced ponds and creeks to <see cref="WaterBodies"/>: those reaching within
    /// <paramref name="reach"/> m of play, kept whole (they count towards the page extent). Ponds out in
    /// the scenery would otherwise be sliced by the page edge.
    /// </summary>
    public CourseLayers AddInlandWater(GreensBook.WaterMap water, double reach = 60)
    {
        WaterBodies.AddRange(water.Bodies.Where(b => b.Shores.Count > 0 && b.Outline.Any(p => DistanceToPlay(p) <= reach)).Select(b => b.Shores));
        return this;
    }
    public required List<List<Vec2>> WasteAreas { get; init; }
    public required List<Ribbon> WasteRibbons { get; init; }
    public required List<Disc> WasteBrushes { get; init; }
    public required List<Ribbon> Paths { get; init; }
    public required List<Ribbon> Roads { get; init; }
    public required List<HoleLayout> Holes { get; init; }

    readonly Dictionary<double, List<List<Vec2>>> _mergedBunkers = new();

    /// <summary>
    /// All bunker pieces (areas, ribbons, painted dabs) fused into smooth outlines. Pieces closer
    /// than roughly <paramref name="mergeMetres"/> join into one bunker and interior pinholes fill,
    /// as a designer would draw it. Painted dabs form a loose lattice in-game, so the default is generous.
    /// </summary>
    public List<List<Vec2>> MergedBunkers(double mergeMetres = 10)
    {
        if (!_mergedBunkers.TryGetValue(mergeMetres, out var loops))
            _mergedBunkers[mergeMetres] = loops = ShapeMerger.Merge(BunkerAreas, BunkerRibbons, BunkerBrushes,
                closeRadius: mergeMetres / 2, smooth: Math.Clamp(mergeMetres * 0.15, 0.6, 1.5),
                keepOut: Grass.Concat(Greens).Select(g => (IReadOnlyList<Vec2>)g).ToList());
        return loops;
    }

    static readonly HashSet<int> GrassSurfaces = [Surfaces.Fairway, Surfaces.Rough, Surfaces.HeavyRough];

    public static CourseLayers From(CourseModel course)
    {
        var layers = new CourseLayers
        {
            Grass = [], Greens = [], BunkerAreas = [], BunkerRibbons = [], BunkerBrushes = [],
            WaterAreas = [], WaterRibbons = [], WasteAreas = [], WasteRibbons = [], WasteBrushes = [],
            Paths = [], Roads = [], Holes = [],
        };

        foreach (var s in course.SurfaceSplines)
        {
            if (s.Points.Count < 2) continue;
            var pts = Geometry.SampleSpline(s, 12);
            if (s.Closed && pts.Count >= 3)
            {
                if (GrassSurfaces.Contains(s.Surface)) layers.Grass.Add(pts);
                else if (s.Surface == Surfaces.Green) layers.Greens.Add(pts);
                else if (s.Surface == Surfaces.Bunker) layers.BunkerAreas.Add(pts);
                else if (s.Surface == Surfaces.Water) layers.WaterAreas.Add(pts);
                else if (s.Surface == Surfaces.Waste) layers.WasteAreas.Add(pts);
            }
            else if (!s.Closed)
            {
                var ribbon = new Ribbon(pts, Math.Max(s.Width, 0.5));
                if (Surfaces.IsWalkingPath(s)) { layers.Paths.Add(ribbon); continue; }
                switch (s.Surface)
                {
                    case Surfaces.Bunker: layers.BunkerRibbons.Add(ribbon); break;
                    case Surfaces.Water: layers.WaterRibbons.Add(ribbon); break;
                    case Surfaces.Waste: layers.WasteRibbons.Add(ribbon); break;
                    case Surfaces.CartPath: layers.Paths.Add(ribbon); break;
                    case Surfaces.Road: layers.Roads.Add(ribbon); break;
                }
            }
        }

        foreach (var b in course.SurfaceBrushes)
        {
            if (b.Scale <= 0 || b.Value < 0.3) continue;
            var disc = new Disc(b.Position, b.Scale / 2);
            if (b.Category == Surfaces.Bunker) layers.BunkerBrushes.Add(disc);
            else if (b.Category == Surfaces.Waste) layers.WasteBrushes.Add(disc);
        }

        foreach (var h in course.Holes)
        {
            var back = h.Tees.Count > 0 ? h.Tees[^1] : h.Route[0];
            var route = new List<Vec2> { back };
            route.AddRange(h.Route.Skip(1));
            var dir = (route.Count > 1 ? route[1] - route[0] : new Vec2(0, 1)).Normalized();
            layers.Holes.Add(new HoleLayout(h, route, back, dir));
        }
        return layers;
    }

    /// <summary>
    /// Points that should fit on the page: hole routes and tees, plus the parts of fairway, green,
    /// bunker and water shapes within <paramref name="reach"/> metres of a hole. Outlying features
    /// (a distant practice area, a road leaving the property) don't force the map to zoom out.
    /// </summary>
    public IEnumerable<Vec2> ExtentPoints(double reach = 60)
    {
        foreach (var h in Holes)
            foreach (var p in h.Route) yield return p;
        foreach (var h in Holes)
            foreach (var t in h.Hole.Tees) yield return t;
        foreach (var poly in Grass.Concat(Greens).Concat(BunkerAreas).Concat(WaterAreas))
            foreach (var p in poly)
                if (DistanceToPlay(p) <= reach) yield return p;
        foreach (var body in WaterBodies)
            foreach (var p in body[0]) yield return p;
    }

    /// <summary>Distance (m) from a point to the nearest hole route (back tee → aim points → green).</summary>
    public double DistanceToPlay(Vec2 p)
    {
        double best = double.MaxValue;
        foreach (var h in Holes)
        {
            foreach (var t in h.Hole.Tees) best = Math.Min(best, Vec2.Distance(p, t));
            for (int i = 1; i < h.Route.Count; i++) best = Math.Min(best, SegmentDistance(p, h.Route[i - 1], h.Route[i]));
        }
        return best;
    }

    /// <summary>Keeps only the runs of each ribbon that stay within <paramref name="reach"/> of play.</summary>
    public List<Ribbon> TrimToPlay(IEnumerable<Ribbon> ribbons, double reach)
    {
        var result = new List<Ribbon>();
        foreach (var r in ribbons)
        {
            var run = new List<Vec2>();
            foreach (var p in r.Points)
            {
                if (DistanceToPlay(p) <= reach) { run.Add(p); continue; }
                if (run.Count >= 2) result.Add(r with { Points = run });
                run = [];
            }
            if (run.Count >= 2) result.Add(r with { Points = run });
        }
        return result;
    }

    static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Z * ab.Z;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / len2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }
}
