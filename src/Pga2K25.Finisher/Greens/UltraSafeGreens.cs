using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Pins;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Greens;

public enum GreenSafety { Safe, MoreSafe, MostSafe }

public sealed record GreenSafetyProfile(string Key, string Label, double TargetLocalP95Pct, double TargetRelief90M, double MinimumFactor);

public sealed record GreenReliefMetrics(int Count, double LocalP95Pct, double LocalP99Pct, double Relief90M);

public sealed record UltraSafeGreenDetail(int Hole, int GreenSpline, double Factor, double RequestedFactor, double ReferenceElevation,
    GreenReliefMetrics Before, int CandidateTerrainRows, int WeightedRows, bool ProfileLimited)
{
    public GreenReliefMetrics? After { get; set; }
}

public sealed record UltraSafeGreensReport(string Severity, string Label, int ChangedNodes, IReadOnlyList<UltraSafeGreenDetail> Details);

/// <summary>
/// Pass 8, <c>apply_ultra_speed_safe_greens</c>: compresses each green's vertical relief toward its median
/// height — every Adaptive/Dense terrain scale at once, blended out over a 4 m smoothstep collar — until the
/// 95th-percentile slope and 90 % relief meet the chosen profile (never below its minimum factor).
/// Original node values are stored so the next run restores them first.
/// </summary>
public static class UltraSafeGreens
{
    public const string MetaKey = "ultraSpeedSafeGreens";
    public const double BufferM = 4.0, CoreEdgeM = 2.5;

    public static readonly GreenSafetyProfile[] Profiles =
    [
        new("safe", "Safe", 5.5, 1.5, 0.65),
        new("more_safe", "More Safe", 4.75, 1.25, 0.55),
        new("most_safe", "Most Safe", 4.0, 1.0, 0.45),
    ];

    public static GreenSafetyProfile Profile(GreenSafety s) => Profiles[(int)s];

    public static GreenSafetyProfile Profile(string severity)
    {
        string key = (string.IsNullOrWhiteSpace(severity) ? "more_safe" : severity).Trim().ToLowerInvariant().Replace(' ', '_');
        return Profiles.FirstOrDefault(p => p.Key == key)
            ?? throw new ArgumentException("Ultra Speed-Safe Greens severity must be Safe, More Safe, or Most Safe.");
    }

    /// <summary><c>_ultra_safe_restore_prior</c>: put back the exact source values saved by the previous run.</summary>
    public static int RestorePrior(JsonObject course, IProgress<string>? log = null)
    {
        if (Provenance.Existing(course, MetaKey) is not JsonObject meta) return 0;
        var height = Json.Arr(course, "height");
        int restored = 0;
        foreach (var row in Json.Arr(meta, "originalValues"))
        {
            if (row is not JsonArray pair || pair.Count < 2) continue;
            int index = Json.Int(pair[0], -1);
            double value = Json.Num(pair[1]);
            if (index < 0 || index >= height.Count || !double.IsFinite(value)) continue;
            if (height[index] is not JsonObject op || Json.Int(op["tool"], -1) != 0) continue;
            op["value"] = Json.F(value);
            restored++;
        }
        if (restored > 0) log?.Report($"Ultra Speed-Safe Greens: restored {restored} original terrain node value(s) before recalculation.");
        Provenance.Remove(course, MetaKey);
        return restored;
    }

    static double Smoothstep01(double t)
    {
        t = Math.Min(1.0, Math.Max(0.0, t));
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary><c>_ultra_safe_choose_factor</c>: the compression needed to meet both targets, floored by the profile.</summary>
    static (double Factor, double Requested) ChooseFactor(GreenReliefMetrics m, GreenSafetyProfile profile)
    {
        var ratios = new List<double> { 1.0 };
        foreach (var (current, target) in new[] { (m.LocalP95Pct, profile.TargetLocalP95Pct), (m.Relief90M, profile.TargetRelief90M) })
            if (current > target + 1e-9 && current > 1e-9) ratios.Add(target / current);
        double requested = ratios.Min();
        double factor = Math.Max(profile.MinimumFactor, Math.Min(1.0, requested));
        if (factor >= 0.985) factor = 1.0;
        return (factor, requested);
    }

    /// <summary><c>_ultra_safe_actual_metrics</c>: measured on the rendered multi-resolution surface.</summary>
    public static (GreenReliefMetrics Metrics, GreenGrid Grid) ActualMetrics(JsonObject hole, GreenOutline green, MultiScaleTerrain sampler)
    {
        var grid = GreenGrid.Build(hole, green, sampler);
        var slopes = grid.ValidValues(grid.Slope).ToArray();
        var heights = grid.ValidValues(grid.Heights).ToArray();
        if (slopes.Length == 0 || heights.Length == 0) return (new GreenReliefMetrics(0, 0, 0, 0), grid);
        Array.Sort(slopes);
        Array.Sort(heights);
        return (new GreenReliefMetrics(slopes.Length, NumPy.PercentileSorted(slopes, 95), NumPy.PercentileSorted(slopes, 99),
            NumPy.PercentileSorted(heights, 95) - NumPy.PercentileSorted(heights, 5)), grid);
    }

    /// <summary><c>_ultra_safe_source_rows</c>: absolute terrain nodes in every (type, scale ≤ 10 m) family of at least 10.</summary>
    static (List<TerrainNode> Rows, List<(int Type, double Scale)> Groups) SourceRows(JsonObject course)
    {
        var parsed = TerrainNodes.Read(course);
        var counts = new Dictionary<(int, double), int>();
        foreach (var n in parsed) counts[(n.Type, n.Scale)] = counts.GetValueOrDefault((n.Type, n.Scale)) + 1;
        var viable = counts.Where(kv => kv.Value >= 10 && kv.Key.Item2 <= 10.0).Select(kv => kv.Key).ToHashSet();
        var rows = parsed.Where(n => viable.Contains((n.Type, n.Scale))).ToList();
        if (rows.Count == 0) throw new InvalidOperationException("Ultra Speed-Safe Greens could not identify substantial absolute terrain groups.");
        return (rows, viable.OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToList());
    }

    public static UltraSafeGreensReport Apply(JsonObject course, string severity = "more_safe", IProgress<string>? log = null)
    {
        var profile = Profile(severity);
        RestorePrior(course, log);
        var (greens, _) = SmartPins.GreenGeometries(course);
        var holes = Tees.TeeGeometry.Holes(course);
        if (greens.Count == 0 || holes.Count == 0) throw new InvalidOperationException("Ultra Speed-Safe Greens could not find putting-green geometry.");

        var analysis = new MultiScaleTerrain(course);
        var (sourceRows, sourceGroups) = SourceRows(course);
        var height = Json.Arr(course, "height");
        log?.Report($"=== ULTRA SPEED-SAFE GREENS: {profile.Label.ToUpperInvariant()} ===");
        log?.Report("Adaptive-aware relief compression across all local terrain scales; 4.0 m smooth transition collar.");

        var used = new HashSet<int>();
        var assignments = new Dictionary<int, (double Effect, double Reference, int Detail)>();
        var assignmentOrder = new List<int>();
        var details = new List<UltraSafeGreenDetail>();
        for (int h = 0; h < holes.Count; h++)
        {
            if (holes[h] is not JsonObject hole) continue;
            var green = SmartPins.HoleGreen(hole, greens, used);
            if (green is null) continue;
            used.Add(green.Index);
            var (before, grid) = ActualMetrics(hole, green, analysis);
            var (factor, requested) = ChooseFactor(before, profile);
            var validHeights = grid.ValidValues(grid.Heights).ToList();
            double reference = validHeights.Count > 0 ? NumPy.Median(validHeights) : analysis.Sample(green.Center.X, green.Center.Z);
            if (!double.IsFinite(reference)) reference = 0.0;

            var (x0, z0, x1, z1) = BBox(green.Poly);
            x0 -= BufferM; z0 -= BufferM; x1 += BufferM; z1 += BufferM;
            var rows = Enumerable.Range(0, sourceRows.Count)
                .Where(i => sourceRows[i].X >= x0 && sourceRows[i].X <= x1 && sourceRows[i].Z >= z0 && sourceRows[i].Z <= z1).ToList();
            int affected = 0;
            if (rows.Count > 0 && factor < 0.999999)
            {
                var edgeTree = new KdTree(green.Poly.Select(p => (p.X, p.Z)).ToList());
                foreach (int row in rows)
                {
                    var p = new Vec2(sourceRows[row].X, sourceRows[row].Z);
                    double weight;
                    if (Contains(green.Poly, p)) weight = 1.0;
                    else
                    {
                        if (edgeTree.Nearest(p.X, p.Z).Distance > BufferM + 0.75) continue;
                        double dist = EdgeClearance(p, green.Poly);
                        if (dist > BufferM + 1e-9) continue;
                        weight = Smoothstep01(1.0 - dist / Math.Max(1e-9, BufferM));
                        if (weight <= 1e-6) continue;
                    }
                    double effect = weight * (1.0 - factor);
                    if (!assignments.TryGetValue(row, out var prev)) assignmentOrder.Add(row);
                    if (!assignments.ContainsKey(row) || effect > prev.Effect) assignments[row] = (effect, reference, details.Count);
                    affected++;
                }
            }
            details.Add(new UltraSafeGreenDetail(h + 1, green.Index, factor, requested, reference, before, rows.Count, affected,
                requested < profile.MinimumFactor - 1e-9));
        }

        var originals = new SortedDictionary<int, double>();
        int changed = 0;
        foreach (int row in assignmentOrder)
        {
            var (effect, reference, _) = assignments[row];
            if (effect <= 1e-9) continue;
            var node = sourceRows[row];
            double fresh = reference + (node.Value - reference) * (1.0 - effect);
            if (Math.Abs(fresh - node.Value) < 0.0005) continue;
            originals.TryAdd(node.Index, node.Value);
            ((JsonObject)height[node.Index]!)["value"] = Json.F(fresh);
            changed++;
        }

        var after = new MultiScaleTerrain(course);
        var (greensAfter, _) = SmartPins.GreenGeometries(course);
        var byIndex = greensAfter.GroupBy(g => g.Index).ToDictionary(g => g.Key, g => g.Last());
        bool anyRequested = false, anyEffect = false;
        foreach (var detail in details)
        {
            // The Python zips details with holes, which drifts when a hole had no green; look the hole up instead.
            if (!byIndex.TryGetValue(detail.GreenSpline, out var g) || holes[detail.Hole - 1] is not JsonObject hole) continue;
            var (afterMetrics, _) = ActualMetrics(hole, g, after);
            detail.After = afterMetrics;
            if (detail.Factor < 0.999999) anyRequested = true;
            if (afterMetrics.LocalP95Pct < detail.Before.LocalP95Pct - 0.02) anyEffect = true;
            string note = detail.ProfileLimited ? " / compression floor reached" : "";
            log?.Report($"Hole {detail.Hole}: {Py.R(100.0 * detail.Factor, 1)}% vertical relief / p95 {Py.R(detail.Before.LocalP95Pct, 2)}% -> " +
                        $"{Py.R(afterMetrics.LocalP95Pct, 2)}% / delta {Py.R(detail.Before.Relief90M, 3)} m -> {Py.R(afterMetrics.Relief90M, 3)} m{note}.");
        }
        if (anyRequested && changed == 0)
            throw new InvalidOperationException("Ultra Speed-Safe Greens requested compression but changed zero terrain nodes; processing stopped rather than silently claim success.");
        if (anyRequested && !anyEffect)
            log?.Report("WARNING: Ultra Speed-Safe Greens changed terrain nodes, but p95 slope did not materially decrease on the analyzed greens.");

        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        meta[MetaKey] = new JsonObject
        {
            ["enabled"] = true,
            ["severity"] = profile.Key,
            ["label"] = profile.Label,
            ["bufferM"] = Json.F(BufferM),
            ["changedNodes"] = changed,
            ["originalValues"] = Json.Array(originals.Select(kv => (JsonNode?)new JsonArray(Json.I(kv.Key), Json.F(kv.Value)))),
            ["sourceGroups"] = Json.Array(sourceGroups.Select(g => (JsonNode?)new JsonArray(Json.I(g.Type), Json.F(g.Scale)))),
            ["profiles"] = ProfilesJson(),
            ["method"] = "adaptive-aware-multiscale-uniform-relief-compression-with-smooth-collar",
            ["details"] = Json.Array(details.Select(d => (JsonNode?)DetailJson(d))),
        };
        log?.Report($"Ultra Speed-Safe Greens complete: {changed} terrain node value(s) adjusted across {sourceGroups.Count} Adaptive/Dense terrain group(s).");
        return new UltraSafeGreensReport(profile.Key, profile.Label, changed, details);
    }

    static JsonObject ProfilesJson()
    {
        var o = new JsonObject();
        foreach (var p in Profiles)
            o[p.Key] = new JsonObject
            {
                ["label"] = p.Label,
                ["targetLocalP95Pct"] = Json.F(p.TargetLocalP95Pct),
                ["targetRelief90M"] = Json.F(p.TargetRelief90M),
                ["minimumFactor"] = Json.F(p.MinimumFactor),
            };
        return o;
    }

    static JsonObject MetricsJson(GreenReliefMetrics m) => new()
    {
        ["count"] = m.Count,
        ["localP95Pct"] = Json.F(m.LocalP95Pct),
        ["localP99Pct"] = Json.F(m.LocalP99Pct),
        ["relief90M"] = Json.F(m.Relief90M),
    };

    static JsonObject DetailJson(UltraSafeGreenDetail d)
    {
        var o = new JsonObject
        {
            ["hole"] = d.Hole,
            ["greenSpline"] = d.GreenSpline,
            ["factor"] = Json.F(d.Factor),
            ["requestedFactor"] = Json.F(d.RequestedFactor),
            ["referenceElevation"] = Json.F(d.ReferenceElevation),
            ["before"] = MetricsJson(d.Before),
            ["candidateTerrainRows"] = d.CandidateTerrainRows,
            ["weightedRows"] = d.WeightedRows,
            ["profileLimited"] = d.ProfileLimited,
        };
        if (d.After is not null) o["afterActual"] = MetricsJson(d.After);
        return o;
    }
}
