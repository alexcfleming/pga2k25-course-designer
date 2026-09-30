using Pga2K25.CourseFormat;

namespace Pga2K25.YardageBook;

/// <summary>
/// Estimates the ground footprint of rock prefabs from the course itself. The .course file stores only
/// a prefab path, position, yaw and a uniform scale; the mesh size lives in the game's assets. But
/// vegetation can't grow inside a rock, so the clear space between each rock and the nearest bush,
/// grass clump or plant — divided by the rock's scale — bounds its radius. Taken per prefab family
/// (the lower quartile, since some instances have plants right up to their edge), this gives a
/// usable footprint without measuring anything in-game.
/// </summary>
public sealed class RockFootprints
{
    /// <summary>Fallback radius (m at scale 1) per family when a course has too few clean examples.</summary>
    public static readonly IReadOnlyDictionary<string, double> Defaults = new Dictionary<string, double>
    {
        ["cluster"] = 9.6, ["small"] = 2.1, ["tiny"] = 2.1, ["stone"] = 1.2, ["rock"] = 0.8,
    };

    const int MinSamples = 5;
    const double MaxRadius = 25;

    readonly Dictionary<string, double> _radius = new(Defaults);

    /// <summary>Per-family estimate: (radius per unit scale, sample count, from course or default).</summary>
    public IReadOnlyDictionary<string, (double Radius, int Samples, bool Measured)> Report { get; }

    public static string Family(string assetPath)
    {
        string n = assetPath.ToLowerInvariant();
        if (n.Contains("cluster")) return "cluster";
        if (n.Contains("tiny")) return "tiny";
        if (n.Contains("small")) return "small";
        if (n.Contains("stone")) return "stone";
        return "rock";
    }

    public RockFootprints(CourseModel course)
    {
        var play = course.SurfaceSplines
            .Where(s => s.Closed && s.Points.Count >= 3 && (s.Surface is Surfaces.Green or Surfaces.Fairway or Surfaces.FairwayCut))
            .Select(s => Geometry.SampleSpline(s, 8)).ToList();
        var probes = course.PlacedObjects
            .Where(p => p.AssetPath.Contains("/Bushes/", StringComparison.OrdinalIgnoreCase)
                     || p.AssetPath.Contains("/Grass/", StringComparison.OrdinalIgnoreCase)
                     || p.AssetPath.Contains("/Plants/", StringComparison.OrdinalIgnoreCase)
                     || p.AssetPath.Contains("/Trees/", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Position).ToList();

        // Spatial hash for nearest-probe queries.
        const double cell = 20;
        var grid = new Dictionary<(int, int), List<Vec2>>();
        foreach (var p in probes)
        {
            var key = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Z / cell));
            (grid.TryGetValue(key, out var l) ? l : grid[key] = []).Add(p);
        }
        double Nearest(Vec2 p)
        {
            int cx = (int)Math.Floor(p.X / cell), cz = (int)Math.Floor(p.Z / cell);
            double best = double.MaxValue;
            for (int ring = 0; ring <= 6; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                    for (int dz = -ring; dz <= ring; dz++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == ring && grid.TryGetValue((cx + dx, cz + dz), out var l))
                            foreach (var q in l) best = Math.Min(best, Vec2.Distance(p, q));
                if (best <= ring * cell) break;   // nothing in a further ring can be closer
            }
            return best;
        }

        var samples = new Dictionary<string, List<double>>();
        foreach (var r in course.PlacedObjects.Where(p => p.AssetPath.Contains("/Rocks/", StringComparison.OrdinalIgnoreCase)))
        {
            if (r.Scale <= 0 || play.Any(f => Geometry.Contains(f, r.Position))) continue;   // no vegetation in play areas
            double d = Nearest(r.Position);
            if (!double.IsFinite(d) || d == double.MaxValue) continue;
            string fam = Family(r.AssetPath);
            (samples.TryGetValue(fam, out var l) ? l : samples[fam] = []).Add(d / r.Scale);
        }

        var report = new Dictionary<string, (double, int, bool)>();
        foreach (var fam in Defaults.Keys)
        {
            var v = samples.GetValueOrDefault(fam) ?? [];
            if (v.Count >= MinSamples)
            {
                v.Sort();
                _radius[fam] = Math.Min(MaxRadius, v[(int)(0.25 * (v.Count - 1))]);
                report[fam] = (_radius[fam], v.Count, true);
            }
            else report[fam] = (_radius[fam], v.Count, false);
        }
        Report = report;
    }

    /// <summary>Footprint radius (m) of a placed rock.</summary>
    public double RadiusOf(PlacedObject rock) => Math.Min(MaxRadius, _radius[Family(rock.AssetPath)] * Math.Max(0.3, rock.Scale));
}
