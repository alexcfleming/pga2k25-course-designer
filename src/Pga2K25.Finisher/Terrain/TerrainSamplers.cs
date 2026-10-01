using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;

namespace Pga2K25.Finisher.Terrain;

/// <summary>One absolute (tool 0) height stamp: an imported terrain lattice node.</summary>
public readonly record struct TerrainNode(int Index, int Type, double Scale, double X, double Z, double Value);

/// <summary>A same-(type, scale) family of absolute stamps with its spatial index.</summary>
public sealed class TerrainGroup
{
    public required int BrushType { get; init; }
    public required double Scale { get; init; }
    public required TerrainNode[] Nodes { get; init; }
    public required KdTree Tree { get; init; }
    public int Count => Nodes.Length;
    public double MaxDistance => Math.Max(3.25 * Scale, 4.0);

    public static TerrainGroup Create(int type, double scale, IReadOnlyList<TerrainNode> nodes) => new()
    {
        BrushType = type,
        Scale = scale,
        Nodes = nodes.ToArray(),
        Tree = new KdTree(nodes.Select(n => (n.X, n.Z)).ToList()),
    };
}

public static class TerrainNodes
{
    /// <summary>
    /// Absolute stamps of the source terrain in file order (tool 0, finite position and value,
    /// positive scale rounded to 3 dp), ignoring the Finisher's own tee-levelling tail.
    /// </summary>
    public static List<TerrainNode> Read(JsonObject course)
    {
        var height = Json.Arr(course, "height");
        int end = Provenance.SourceHeightLength(course);
        var result = new List<TerrainNode>();
        for (int i = 0; i < end; i++)
        {
            if (height[i] is not JsonObject op || Json.Int(op["tool"], 0) != 0) continue;
            int type = Json.Int(op["type"], -1);
            double scale = Py.Round(Json.Num(op["scale"]?["x"], 0), 3);
            var (x, z) = Json.XZ(op["position"]);
            double value = Json.Num(op["value"]);
            if (!double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(value) || !(scale > 0)) continue;
            result.Add(new TerrainNode(i, type, scale, x, z, value));
        }
        return result;
    }

    /// <summary>(type, scale) groups in first-seen order.</summary>
    public static List<(int Type, double Scale, List<TerrainNode> Nodes)> Group(IEnumerable<TerrainNode> nodes)
    {
        var order = new List<(int, double, List<TerrainNode>)>();
        var map = new Dictionary<(int, double), List<TerrainNode>>();
        foreach (var n in nodes)
        {
            if (!map.TryGetValue((n.Type, n.Scale), out var list))
            {
                map[(n.Type, n.Scale)] = list = [];
                order.Add((n.Type, n.Scale, list));
            }
            list.Add(n);
        }
        return order;
    }

    /// <summary>
    /// Weighted plane through the neighbours, clamped to their range ± 0.25 m; the weighted mean if
    /// the fit fails. Shared by both point samplers.
    /// </summary>
    internal static double PlaneEstimate(TerrainGroup g, double x, double z, ReadOnlySpan<double> ds, ReadOnlySpan<int> ids, double spacing)
    {
        int n = ds.Length;
        Span<double> xs = stackalloc double[n], zs = stackalloc double[n], vs = stackalloc double[n], w = stackalloc double[n];
        double lo = double.MaxValue, hi = double.MinValue, wsum = 0, wv = 0;
        double offset = Math.Max(0.2, 0.15 * spacing);
        for (int i = 0; i < n; i++)
        {
            var node = g.Nodes[ids[i]];
            xs[i] = node.X; zs[i] = node.Z; vs[i] = node.Value;
            double d = ds[i] + offset;
            w[i] = 1.0 / (d * d);
            lo = Math.Min(lo, node.Value); hi = Math.Max(hi, node.Value);
            wsum += w[i]; wv += w[i] * node.Value;
        }
        if (!LinAlg.FitPlane(xs, zs, vs, w, out double a, out double b, out double c)) return wv / wsum;
        double value = a * x + b * z + c;
        return Math.Min(Math.Max(value, lo - 0.25), hi + 0.25);
    }
}

/// <summary>
/// <c>CourseTerrainSampler</c>: samples the single dominant high-resolution lattice (the TGC Tool's
/// <c>type 72</c> stamps, or the finest same-scale group with at least 500 nodes).
/// </summary>
public sealed class FineTerrainSampler
{
    public const int MinimumLatticeNodes = 500;

    public TerrainGroup Group { get; }
    public int BrushType => Group.BrushType;
    public double Spacing => Group.Scale;
    public int Count => Group.Count;

    public FineTerrainSampler(JsonObject course)
    {
        var (type, scale, nodes) = FineCandidates(course);
        Group = TerrainGroup.Create(type, scale, nodes);
    }

    /// <summary><c>_fine_terrain_candidates</c>.</summary>
    public static (int Type, double Scale, List<TerrainNode> Nodes) FineCandidates(JsonObject course)
    {
        var all = TerrainNodes.Read(course);
        var hard = all.Where(n => n.Type == 72).ToList();
        if (hard.Count >= MinimumLatticeNodes)
        {
            // max(counts, key=counts.get): most common scale, first seen wins a tie.
            var counts = new Dictionary<double, int>();
            var firstSeen = new List<double>();
            foreach (var n in hard)
            {
                if (!counts.TryAdd(n.Scale, 1)) counts[n.Scale]++;
                else firstSeen.Add(n.Scale);
            }
            double fine = firstSeen.Aggregate((best, s) => counts[s] > counts[best] ? s : best);
            return (72, fine, hard.Where(n => n.Scale == fine).ToList());
        }
        var viable = TerrainNodes.Group(all).Where(g => g.Nodes.Count >= MinimumLatticeNodes && g.Scale > 0)
            .OrderBy(g => g.Scale).ThenBy(g => -g.Nodes.Count).ToList();
        if (viable.Count == 0)
            throw new InvalidOperationException("Could not identify a high-resolution absolute terrain lattice in height[].");
        return (viable[0].Type, viable[0].Scale, viable[0].Nodes);
    }

    /// <summary><c>CourseTerrainSampler.sample</c>: exact node within 3 cm, else a local plane fit; NaN when too sparse.</summary>
    public double Sample(double x, double z)
    {
        int k = Math.Min(12, Group.Count);
        Span<double> ds = stackalloc double[k];
        Span<int> ids = stackalloc int[k];
        int found = Group.Tree.Query(x, z, k, ds, ids);
        if (found == 0 || !double.IsFinite(ds[0])) return double.NaN;
        if (ds[0] <= 0.03) return Group.Nodes[ids[0]].Value;
        double maxDistance = Math.Max(3.25 * Spacing, 4.0);
        int keep = 0;
        while (keep < found && ds[keep] <= maxDistance) keep++;
        if (keep < 3) return ds[0] <= maxDistance ? Group.Nodes[ids[0]].Value : double.NaN;
        return TerrainNodes.PlaneEstimate(Group, x, z, ds[..keep], ids[..keep], Spacing);
    }
}

/// <summary>
/// <c>WaterTerrainSampler</c> / <c>SmartPinTerrainSampler</c>: every absolute-stamp family with at
/// least 10 nodes, queried finest scale first, so Adaptive (multi-scale) and Dense terrain both work.
/// </summary>
public sealed class MultiScaleTerrain
{
    public const int MinimumGroupNodes = 10;

    /// <summary>Finest scale first; ties by larger count.</summary>
    public IReadOnlyList<TerrainGroup> Groups { get; }

    /// <summary>Finest group scale (<c>sampler.spacing</c>).</summary>
    public double Spacing { get; }

    /// <summary>The group with the most nodes (<c>sampler.points</c> / <c>values</c>).</summary>
    public TerrainGroup Coverage { get; }

    public MultiScaleTerrain(JsonObject course)
    {
        var groups = TerrainNodes.Group(TerrainNodes.Read(course))
            .Where(g => g.Nodes.Count >= MinimumGroupNodes)
            .Select(g => TerrainGroup.Create(g.Type, g.Scale, g.Nodes))
            .OrderBy(g => g.Scale).ThenBy(g => -g.Count).ToList();
        if (groups.Count == 0)
            throw new InvalidOperationException("Could not identify usable absolute terrain stamps for water solving.");
        Groups = groups;
        Spacing = groups[0].Scale;
        Coverage = groups.Aggregate((best, g) => g.Count > best.Count ? g : best);
    }

    /// <summary>
    /// <c>WaterTerrainSampler.sample</c>: first (finest) group with a node within reach decides —
    /// exact node, nearest node when sparse, else a clamped local plane. NaN when nothing is in reach.
    /// </summary>
    public double Sample(double x, double z)
    {
        Span<double> ds = stackalloc double[12];
        Span<int> ids = stackalloc int[12];
        foreach (var g in Groups)
        {
            int found = g.Tree.Query(x, z, Math.Min(12, g.Count), ds, ids);
            if (found == 0 || !double.IsFinite(ds[0])) continue;
            double maxDistance = g.MaxDistance;
            if (ds[0] > maxDistance) continue;
            if (ds[0] <= 0.03) return g.Nodes[ids[0]].Value;
            int keep = 0;
            while (keep < found && ds[keep] <= maxDistance) keep++;
            if (keep < 3) return g.Nodes[ids[0]].Value;
            return TerrainNodes.PlaneEstimate(g, x, z, ds[..keep], ids[..keep], g.Scale);
        }
        return double.NaN;
    }

    /// <summary>
    /// <c>_water_sample_many</c> / <c>SmartPinTerrainSampler.sample_many</c>: inverse-distance blend
    /// of the 8 nearest nodes of the finest group in reach; NaN where no group reaches.
    /// </summary>
    public double SampleIdw(double x, double z)
    {
        Span<double> ds = stackalloc double[8];
        Span<int> ids = stackalloc int[8];
        foreach (var g in Groups)
        {
            int found = g.Tree.Query(x, z, Math.Min(8, g.Count), ds, ids);
            if (found == 0) continue;
            double maxDistance = g.MaxDistance;
            if (!double.IsFinite(ds[0]) || ds[0] > maxDistance) continue;
            if (ds[0] <= 0.03) return g.Nodes[ids[0]].Value;
            double offset = Math.Max(0.2, 0.15 * g.Scale), wsum = 0, acc = 0;
            for (int i = 0; i < found; i++)
            {
                if (!(ds[i] <= maxDistance)) continue;
                double d = ds[i] + offset, w = 1.0 / (d * d);
                wsum += w;
                acc += w * g.Nodes[ids[i]].Value;
            }
            return wsum > 0 ? acc / Math.Max(wsum, 1e-12) : g.Nodes[ids[0]].Value;
        }
        return double.NaN;
    }

    public double[] SampleMany(IReadOnlyList<Vec2> points)
    {
        var result = new double[points.Count];
        Parallel.For(0, points.Count, i => result[i] = SampleIdw(points[i].X, points[i].Z));
        return result;
    }
}
