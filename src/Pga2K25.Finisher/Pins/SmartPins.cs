using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Pins;

/// <summary>A closed green or bunker outline (<c>_smart_pin_green_geometries</c> item).</summary>
public sealed record GreenOutline(int Index, List<Vec2> Poly, Vec2 Center);

/// <summary>A scored pin location (the Python candidate dict).</summary>
public sealed class PinCandidate
{
    public double X { get; init; }
    public double Z { get; init; }
    public double EdgeClearance { get; init; }
    public double BunkerDistance { get; init; }
    public double Elevation { get; init; }
    public double SlopePct { get; init; }
    public double MaxLocalGradePct { get; init; }
    public double LocalResidual { get; init; }
    public double Relief3m { get; init; }
    public double DownhillDrop3m { get; init; }
    public double Score { get; set; }
    public int TierIndex { get; set; }
    public string Tier { get; set; } = "";
    public double Along { get; set; }
    public double Lateral { get; set; }
    public (int Front, int Side) Quadrant { get; set; }
    public bool ForcedFallback { get; set; }
    public double? ForcedMinSeparation { get; set; }
    public bool PreservedExisting { get; set; }
    public int? Slot { get; set; }

    public int FrontHalf => Along >= 0.0 ? 1 : -1;
    public int SideHalf => Lateral >= 0.0 ? 1 : -1;
    public double DistanceTo(PinCandidate o) => Hypot(X - o.X, Z - o.Z);
    public PinCandidate Copy() => (PinCandidate)MemberwiseClone();
}

public sealed record PinHoleReport(int Hole, int? GreenSpline, int Candidates, string? Mode, string? Warning, int PreservedPins, int ForcedFallbackPins,
    double MinimumSeparation, double MinimumEdgeClearance, double MaximumSlopePct, double MaximumLocalGradePct, string? WorstTier,
    IReadOnlyList<PinCandidate> Pins);

public sealed record SmartPinsReport(int Holes, int Pins, string Mode, int Preserved, int Moved, int Added, IReadOnlyList<PinHoleReport> Details);

/// <summary>
/// Pass 9, <c>smart_place_pins</c>: four fast-green-safe pins per green, chosen from a 0.5 m grid inside the
/// green (≥ 1 m from the edge) scored on slope, local grade, plane residual, 3 m relief and drop, edge and
/// bunker clearance, spread across quadrants. "repair" keeps the existing pins that are safe and replaces the
/// rest with minimum total movement; "rebuild" picks all four afresh.
/// </summary>
public static class SmartPins
{
    public const string MetaKey = "smartPins";
    public const int Count = 4;
    public const double GridStep = 0.5, LocalRadius = 0.9, NeighborRadius = 3.0;
    public const double PreferredEdge = 3.6, TargetEdge = 4.5, AbsoluteEdge = 2.0, PreferredSeparation = 6.0, MinSeparation = 4.0;
    public const double FastTargetSlope = 2.0, FastMaxSlope = 2.4, FastTargetLocalGrade = 2.0, FastMaxLocalGrade = 2.4;

    /// <summary>(name, min edge m, max slope %, max residual m, max 3 m relief, max 3 m drop).</summary>
    public static readonly (string Name, double Edge, double Slope, double Residual, double Relief, double Drop)[] Tiers =
    [
        ("preferred", 3.6, 3.5, 0.02, 0.3, 0.25),
        ("good", 3.0, 4.5, 0.035, 0.4, 0.35),
        ("usable", 2.5, 6.0, 0.06, 0.55, 0.5),
        ("fallback", 2.0, 8.0, 0.1, 0.75, 0.65),
        ("emergency", 1.5, 12.0, 0.15, 1.0, 0.85),
    ];

    // ---------------------------------------------------------------- geometry shared with greens passes

    /// <summary><c>_smart_pin_green_geometries</c>: closed greens and bunkers (18 samples per segment).</summary>
    public static (List<GreenOutline> Greens, List<GreenOutline> Bunkers) GreenGeometries(JsonObject course)
    {
        var roles = FeatureClassifier.Classify(course);
        var splines = Json.Arr(course, "surfaceSplines2");
        var greens = new List<GreenOutline>();
        var bunkers = new List<GreenOutline>();
        foreach (var (index, role) in roles)
        {
            if (role is not (SplineCategory.Green or SplineCategory.Bunker)) continue;
            if (index < 0 || index >= splines.Count || splines[index] is not JsonObject spline || !IsClosed(spline)) continue;
            var poly = DensePolyline(spline, 18, close: true);
            if (poly.Count < 3) continue;
            var item = new GreenOutline(index, poly, Mean(poly));
            (role == SplineCategory.Green ? greens : bunkers).Add(item);
        }
        return (greens, bunkers);
    }

    /// <summary><c>_smart_pin_hole_green</c>: the green containing the hole's last waypoint (unused first), else the nearest unused one.</summary>
    public static GreenOutline? HoleGreen(JsonObject hole, List<GreenOutline> greens, ISet<int>? used = null)
    {
        var wps = Json.Arr(hole, "waypoints");
        if (wps.Count == 0 || greens.Count == 0) return null;
        var (ax, az) = Json.XZ(wps[^1]);
        var anchor = new Vec2(ax, az);
        used ??= new HashSet<int>();
        var inside = greens.Where(g => Contains(g.Poly, anchor)).ToList();
        if (inside.Count > 0)
            return inside.OrderBy(g => used.Contains(g.Index) ? 1 : 0).ThenBy(g => Vec2.Distance(g.Center, anchor)).First();
        var available = greens.Where(g => !used.Contains(g.Index)).ToList();
        if (available.Count == 0) available = greens;
        return Tees.TeeGeometry.MinBy(available, g => Vec2.Distance(g.Center, anchor));
    }

    /// <summary><c>_smart_pin_approach_axis</c>: unit vector of the last route leg (default +Z).</summary>
    public static Vec2 ApproachAxis(JsonObject hole)
    {
        var wps = Json.Arr(hole, "waypoints");
        if (wps.Count >= 2)
        {
            var (ax, az) = Json.XZ(wps[^2]);
            var (bx, bz) = Json.XZ(wps[^1]);
            double n = Hypot(bx - ax, bz - az);
            if (n > 1e-9) return new Vec2((bx - ax) / n, (bz - az) / n);
        }
        return new Vec2(0.0, 1.0);
    }

    // ---------------------------------------------------------------- scoring

    /// <summary><c>_smart_pin_quality</c>.</summary>
    static double Quality(PinCandidate c)
    {
        double edge = c.EdgeClearance;
        double edgeScore = edge >= TargetEdge ? 18.0
            : edge >= PreferredEdge ? 12.0 + 6.0 * ((edge - PreferredEdge) / Math.Max(1e-9, TargetEdge - PreferredEdge))
            : Math.Max(0.0, 12.0 * (edge - AbsoluteEdge) / Math.Max(1e-9, PreferredEdge - AbsoluteEdge));
        double slopeScore = Math.Max(-30.0, 36.0 - 12.0 * Math.Max(0.0, c.SlopePct - 1.0));
        double gradeScore = Math.Max(-30.0, 30.0 - 12.0 * Math.Max(0.0, c.MaxLocalGradePct - 1.0));
        double residualScore = Math.Max(-15.0, 14.0 - 300.0 * Math.Max(0.0, c.LocalResidual - 0.005));
        double reliefScore = Math.Max(-15.0, 16.0 - 35.0 * Math.Max(0.0, c.Relief3m - 0.12));
        double dropScore = Math.Max(-12.0, 10.0 - 30.0 * Math.Max(0.0, c.DownhillDrop3m - 0.1));
        double bunkerScore = c.BunkerDistance >= 8.0 ? 0.0 : -Math.Max(0.0, (8.0 - c.BunkerDistance) * 2.5);
        return edgeScore + slopeScore + gradeScore + residualScore + reliefScore + dropScore + bunkerScore;
    }

    /// <summary><c>_smart_pin_quality_tier</c>.</summary>
    static (int Index, string Label) QualityTier(PinCandidate c)
    {
        for (int i = 0; i < Tiers.Length; i++)
        {
            var t = Tiers[i];
            if (c.EdgeClearance + 1e-9 >= t.Edge && c.SlopePct <= t.Slope + 1e-9 && c.LocalResidual <= t.Residual + 1e-9 &&
                c.Relief3m <= t.Relief + 1e-9 && c.DownhillDrop3m <= t.Drop + 1e-9)
                return (i, t.Name);
        }
        return (Tiers.Length, "emergency+");
    }

    /// <summary>The 9 local offsets (centre + 0.9 m ring) followed by 1.5 m and 3 m rings of 8.</summary>
    static readonly Vec2[] Offsets = BuildOffsets();

    static Vec2[] BuildOffsets()
    {
        var list = new List<Vec2> { new(0.0, 0.0) };
        foreach (double r in new[] { LocalRadius, 1.5, NeighborRadius })
            for (int k = 0; k < 8; k++)
            {
                double angle = 2.0 * Math.PI * k / 8.0;
                list.Add(new Vec2(r * Math.Cos(angle), r * Math.Sin(angle)));
            }
        return list.ToArray();
    }

    /// <summary>pinv of the 9×3 local plane design [dx, dz, 1] (full rank, so (AᵀA)⁻¹Aᵀ).</summary>
    static readonly double[,] LocalPinv = BuildPinv();

    static double[,] BuildPinv()
    {
        var m = new double[3, 3];
        for (int i = 0; i < 9; i++)
        {
            double[] row = [Offsets[i].X, Offsets[i].Z, 1.0];
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++) m[a, b] += row[a] * row[b];
        }
        double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        var inv = new double[3, 3];
        inv[0, 0] = (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) / det;
        inv[0, 1] = (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) / det;
        inv[0, 2] = (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) / det;
        inv[1, 0] = (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) / det;
        inv[1, 1] = (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) / det;
        inv[1, 2] = (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) / det;
        inv[2, 0] = (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) / det;
        inv[2, 1] = (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) / det;
        inv[2, 2] = (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) / det;
        var pinv = new double[3, 9];
        for (int a = 0; a < 3; a++)
            for (int i = 0; i < 9; i++)
                pinv[a, i] = inv[a, 0] * Offsets[i].X + inv[a, 1] * Offsets[i].Z + inv[a, 2];
        return pinv;
    }

    /// <summary>Terrain metrics from the 25 probe heights around a point (shared by the grid and exact evaluation).</summary>
    static PinCandidate? Measure(double x, double z, double edge, double bunker, ReadOnlySpan<double> heights, bool requireAllNine, bool requireFiniteCount)
    {
        var local = heights[..9];
        for (int i = 0; i < 9; i++) if (!double.IsFinite(local[i])) return null;
        double c0 = 0, c1 = 0, c2 = 0;
        for (int i = 0; i < 9; i++) { c0 += LocalPinv[0, i] * local[i]; c1 += LocalPinv[1, i] * local[i]; c2 += LocalPinv[2, i] * local[i]; }
        double residual = 0, maxLocal = 0;
        for (int i = 0; i < 9; i++)
        {
            residual = Math.Max(residual, Math.Abs(local[i] - (c0 * Offsets[i].X + c1 * Offsets[i].Z + c2)));
            if (i > 0) maxLocal = Math.Max(maxLocal, Math.Abs(local[i] - local[0]));
        }
        double slope = 100.0 * Hypot(c0, c1);
        double maxGrade = maxLocal / Math.Max(1e-9, LocalRadius) * 100.0;
        double lo = double.MaxValue, hi = double.MinValue;
        int finite = 0;
        foreach (double h in heights)
        {
            if (!double.IsFinite(h)) continue;
            finite++;
            lo = Math.Min(lo, h); hi = Math.Max(hi, h);
        }
        if (requireFiniteCount && finite < 9) return null;
        double relief = finite > 0 ? hi - lo : 9999.0;
        double drop = finite > 0 ? Math.Max(0.0, local[0] - lo) : 9999.0;
        var item = new PinCandidate
        {
            X = x, Z = z, EdgeClearance = edge, BunkerDistance = bunker, Elevation = local[0], SlopePct = slope,
            MaxLocalGradePct = maxGrade, LocalResidual = residual, Relief3m = relief, DownhillDrop3m = drop,
        };
        item.Score = Quality(item);
        (item.TierIndex, item.Tier) = QualityTier(item);
        return item;
    }

    /// <summary><c>_smart_pin_candidate_grid</c>: scored 0.5 m grid points at least 1 m inside the green.</summary>
    static List<PinCandidate> CandidateGrid(List<Vec2> poly, MultiScaleTerrain sampler, KdTree? bunkerTree)
    {
        var (x0, z0, x1, z1) = BBox(poly);
        double startX = Math.Floor(x0 / GridStep) * GridStep + 0.5 * GridStep;
        double startZ = Math.Floor(z0 / GridStep) * GridStep + 0.5 * GridStep;
        var geometry = new List<(double X, double Z, double Edge, double Bunker)>();
        for (double z = startZ; z <= z1 + 1e-9; z += GridStep)
            for (double x = startX; x <= x1 + 1e-9; x += GridStep)
            {
                if (!Contains(poly, x, z)) continue;
                double edge = EdgeClearance(new Vec2(x, z), poly);
                if (edge < 1.0) continue;
                geometry.Add((x, z, edge, bunkerTree is not null ? bunkerTree.Nearest(x, z).Distance : 9999.0));
            }
        if (geometry.Count == 0) return [];
        var probes = new List<Vec2>(geometry.Count * Offsets.Length);
        foreach (var g in geometry)
            foreach (var o in Offsets) probes.Add(new Vec2(g.X + o.X, g.Z + o.Z));
        var heights = sampler.SampleMany(probes);
        var result = new List<PinCandidate>();
        for (int i = 0; i < geometry.Count; i++)
        {
            var g = geometry[i];
            var item = Measure(g.X, g.Z, g.Edge, g.Bunker, heights.AsSpan(i * Offsets.Length, Offsets.Length), true, true);
            if (item is not null) result.Add(item);
        }
        return result;
    }

    /// <summary><c>_smart_pin_evaluate_exact</c>: an existing pin scored at its exact position (null if outside the green).</summary>
    static PinCandidate? EvaluateExact(MultiScaleTerrain sampler, double x, double z, List<Vec2> poly, KdTree? bunkerTree)
    {
        if (!Contains(poly, x, z)) return null;
        double edge = EdgeClearance(new Vec2(x, z), poly);
        var heights = sampler.SampleMany(Offsets.Select(o => new Vec2(x + o.X, z + o.Z)).ToList());
        double bunker = bunkerTree is not null ? bunkerTree.Nearest(x, z).Distance : 9999.0;
        return Measure(x, z, edge, bunker, heights, true, false);
    }

    static bool ExistingIsSafe(PinCandidate? c) => c is not null &&
        c.EdgeClearance + 1e-9 >= AbsoluteEdge && c.SlopePct <= FastMaxSlope + 1e-9 && c.MaxLocalGradePct <= FastMaxLocalGrade + 1e-9 &&
        c.LocalResidual <= 0.100000001 && c.Relief3m <= 0.750000001 && c.DownhillDrop3m <= 0.650000001;

    /// <summary><c>_smart_pin_add_orientation</c>: along / lateral / quadrant relative to the green centre and approach.</summary>
    static void AddOrientation(PinCandidate c, JsonObject hole, GreenOutline green)
    {
        var forward = ApproachAxis(hole);
        var right = new Vec2(forward.Z, -forward.X);
        double dx = c.X - green.Center.X, dz = c.Z - green.Center.Z;
        c.Along = dx * forward.X + dz * forward.Z;
        c.Lateral = dx * right.X + dz * right.Z;
        c.Quadrant = (c.FrontHalf, c.SideHalf);
    }

    static bool FastTarget(PinCandidate c) => c.EdgeClearance + 1e-9 >= AbsoluteEdge && c.SlopePct <= FastTargetSlope + 1e-9 && c.MaxLocalGradePct <= FastTargetLocalGrade + 1e-9;
    static bool FastCeiling(PinCandidate c) => c.EdgeClearance + 1e-9 >= AbsoluteEdge && c.SlopePct <= FastMaxSlope + 1e-9 && c.MaxLocalGradePct <= FastMaxLocalGrade + 1e-9;

    static double MinPairwise(IReadOnlyList<PinCandidate> s)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < s.Count; i++)
            for (int j = i + 1; j < s.Count; j++) best = Math.Min(best, s[i].DistanceTo(s[j]));
        return best;
    }

    // ---------------------------------------------------------------- set selection

    /// <summary><c>_smart_pin_greedy_set</c>: best-scored first, then greedily add the best score + spread bonus.</summary>
    static List<PinCandidate> GreedySet(List<PinCandidate> pool, double minSeparation)
    {
        if (pool.Count == 0) return [];
        var ordered = pool.OrderByDescending(c => c.Score).Take(750).ToList();
        var selected = new List<PinCandidate> { ordered[0] };
        while (selected.Count < Count)
        {
            var quads = selected.Select(s => s.Quadrant).ToHashSet();
            var fronts = selected.Select(s => s.FrontHalf).ToHashSet();
            var sides = selected.Select(s => s.SideHalf).ToHashSet();
            PinCandidate? best = null;
            double bestValue = -1e99;
            foreach (var c in ordered)
            {
                if (selected.Contains(c)) continue;
                double minDist = selected.Min(s => c.DistanceTo(s));
                if (minDist + 1e-9 < minSeparation) continue;
                double bonus = 2.0 * Math.Min(minDist, 12.0);
                if (!quads.Contains(c.Quadrant)) bonus += 10.0;
                if (!fronts.Contains(c.FrontHalf)) bonus += 4.0;
                if (!sides.Contains(c.SideHalf)) bonus += 4.0;
                double value = c.Score + bonus;
                if (value > bestValue) { bestValue = value; best = c; }
            }
            if (best is null) break;
            selected.Add(best);
        }
        return selected;
    }

    /// <summary><c>_smart_pin_partial_value</c>: rank an incomplete set while searching.</summary>
    static double PartialValue(List<PinCandidate> selected)
    {
        if (selected.Count == 0) return -1e99;
        double minSep = selected.Count > 1 ? MinPairwise(selected) : 0.0;
        return 10000.0 * selected.Count + selected.Sum(c => c.Score) + 8.0 * Math.Min(minSep, 8.0) + 4.0 * selected.Select(c => c.Quadrant).Distinct().Count();
    }

    /// <summary><c>_smart_pin_force_complete</c>: finish an unsolved set with the best remaining candidates, relaxing spacing step by step.</summary>
    static (List<PinCandidate> Selected, List<PinCandidate> Forced) ForceComplete(List<PinCandidate> selected, List<PinCandidate> candidates)
    {
        selected = selected.ToList();
        var forced = new List<PinCandidate>();
        if (candidates.Count == 0) return (selected, forced);
        var ordered = candidates.OrderByDescending(c => c.Score).ToList();
        while (selected.Count < Count)
        {
            PinCandidate? chosen = null;
            double chosenSep = 0.0, chosenValue = -1e99;
            foreach (double sepFloor in new[] { 1.5, 1.0, 0.75, 0.5, 0.0 })
            {
                foreach (var c in ordered)
                {
                    if (selected.Contains(c)) continue;
                    double minDist = selected.Count > 0 ? selected.Min(s => c.DistanceTo(s)) : 9999.0;
                    if (minDist + 1e-9 < sepFloor) continue;
                    var quads = selected.Select(s => s.Quadrant).ToHashSet();
                    var fronts = selected.Select(s => s.FrontHalf).ToHashSet();
                    var sides = selected.Select(s => s.SideHalf).ToHashSet();
                    double bonus = 1.5 * Math.Min(minDist, 10.0);
                    if (!quads.Contains(c.Quadrant)) bonus += 8.0;
                    if (!fronts.Contains(c.FrontHalf)) bonus += 3.0;
                    if (!sides.Contains(c.SideHalf)) bonus += 3.0;
                    double value = c.Score + bonus;
                    if (value > chosenValue)
                    {
                        chosenValue = value;
                        chosen = c;
                        chosenSep = selected.Count > 0 ? minDist : 9999.0;
                    }
                }
                if (chosen is not null) break;
            }
            if (chosen is null) break;
            chosen.ForcedFallback = true;
            chosen.ForcedMinSeparation = chosenSep < 9999.0 ? chosenSep : 0.0;
            selected.Add(chosen);
            forced.Add(chosen);
        }
        return (selected, forced);
    }

    /// <summary>
    /// <c>_smart_pin_select_four</c>: fast-target pins at 6 → 4 m spacing, then the 2.4 % ceiling, then tighter
    /// spacing, then a 1.25 m edge; failing all that, force-complete the best partial set.
    /// </summary>
    static (List<PinCandidate> Set, double MinSeparation) SelectFour(List<PinCandidate> candidates, JsonObject hole, GreenOutline green)
    {
        if (candidates.Count == 0) return ([], 0.0);
        foreach (var c in candidates)
        {
            c.ForcedFallback = false;
            c.ForcedMinSeparation = null;
            AddOrientation(c, hole, green);
        }
        var fastTarget = candidates.Where(FastTarget).ToList();
        var fastCeiling = candidates.Where(FastCeiling).ToList();
        var fastCeilingEdgeRelaxed = candidates.Where(c => c.EdgeClearance + 1e-9 >= 1.25 && c.SlopePct <= FastMaxSlope + 1e-9 && c.MaxLocalGradePct <= FastMaxLocalGrade + 1e-9).ToList();
        var stages = new (List<PinCandidate> Pool, double[] Seps)[]
        {
            (fastTarget, [6.0, 5.5, 5.0, 4.5, 4.0]),
            (fastCeiling, [6.0, 5.5, 5.0, 4.5, 4.0]),
            (fastTarget, [3.5, 3.0, 2.5, 2.0]),
            (fastCeiling, [3.5, 3.0, 2.5, 2.0]),
            (fastCeilingEdgeRelaxed, [4.0, 3.5, 3.0, 2.5, 2.0, 1.5, 1.0]),
        };
        var bestSet = new List<PinCandidate>();
        var bestPartial = new List<PinCandidate>();
        double bestPartialValue = -1e99, bestMinSep = 0.0;
        foreach (var (pool, seps) in stages)
        {
            if (pool.Count == 0) continue;
            foreach (double sep in seps)
            {
                var selected = GreedySet(pool, sep);
                double partial = PartialValue(selected);
                if (partial > bestPartialValue) { bestPartial = selected.ToList(); bestPartialValue = partial; }
                if (selected.Count != Count) continue;
                bestSet = selected;
                bestMinSep = MinPairwise(selected);
                break;
            }
            if (bestSet.Count > 0) break;
        }
        if (bestSet.Count != Count)
        {
            (bestSet, _) = ForceComplete(bestPartial, candidates);
            if (bestSet.Count != Count) return ([], 0.0);
            bestMinSep = MinPairwise(bestSet);
        }
        return (bestSet.OrderBy(c => c.Along).ThenBy(c => c.Lateral).ToList(), bestMinSep);
    }

    /// <summary><c>_smart_pin_fill_locked</c>: fill the unsafe / missing slots around the pins being kept.</summary>
    static (List<PinCandidate> Selected, List<PinCandidate> Forced) FillLocked(List<PinCandidate> candidates, JsonObject hole, GreenOutline green,
        List<PinCandidate> locked, int needed, List<PinCandidate> banned)
    {
        if (needed <= 0) return ([], []);
        foreach (var c in candidates)
        {
            AddOrientation(c, hole, green);
            c.ForcedFallback = false;
        }
        locked = locked.Select(c => c.Copy()).ToList();
        foreach (var c in locked) AddOrientation(c, hole, green);

        static bool NearAny(PinCandidate c, List<PinCandidate> others) => others.Any(q => Hypot(c.X - q.X, c.Z - q.Z) < 0.45);

        List<PinCandidate> ChooseFrom(List<PinCandidate> pool, double sep, List<PinCandidate> baseline)
        {
            var selected = new List<PinCandidate>();
            var current = baseline.ToList();
            var ordered = pool.OrderByDescending(c => c.Score).Take(900).ToList();
            while (selected.Count < needed)
            {
                var quads = current.Select(c => c.Quadrant).ToHashSet();
                var fronts = current.Select(c => c.FrontHalf).ToHashSet();
                var sides = current.Select(c => c.SideHalf).ToHashSet();
                PinCandidate? best = null;
                double bestValue = -1e99;
                foreach (var cand in ordered)
                {
                    if (selected.Contains(cand) || NearAny(cand, banned) || NearAny(cand, current)) continue;
                    double minDist = current.Count > 0 ? current.Min(q => cand.DistanceTo(q)) : 9999.0;
                    if (minDist + 1e-9 < sep) continue;
                    double bonus = 1.7 * Math.Min(minDist, 10.0);
                    if (!quads.Contains(cand.Quadrant)) bonus += 8.0;
                    if (!fronts.Contains(cand.FrontHalf)) bonus += 3.0;
                    if (!sides.Contains(cand.SideHalf)) bonus += 3.0;
                    double value = cand.Score + bonus;
                    if (value > bestValue) { bestValue = value; best = cand; }
                }
                if (best is null) break;
                selected.Add(best);
                current.Add(best);
            }
            return selected;
        }

        var pools = new (List<PinCandidate> Pool, double[] Seps)[]
        {
            (candidates.Where(FastTarget).ToList(), [6.0, 5.5, 5.0, 4.5, 4.0, 3.0, 2.0]),
            (candidates.Where(FastCeiling).ToList(), [6.0, 5.5, 5.0, 4.5, 4.0, 3.0, 2.0, 1.5, 1.0]),
        };
        foreach (var (pool, seps) in pools)
            foreach (double sep in seps)
            {
                var selected = ChooseFrom(pool, sep, locked);
                if (selected.Count == needed) return (selected, []);
            }

        var result = new List<PinCandidate>();
        var currentAll = locked.ToList();
        var forced = new List<PinCandidate>();
        var byScore = candidates.OrderByDescending(c => c.Score).ToList();
        foreach (double sep in new[] { 1.5, 1.0, 0.75, 0.5, 0.0 })
        {
            while (result.Count < needed)
            {
                PinCandidate? best = null;
                double bestValue = -1e99;
                foreach (var cand in byScore)
                {
                    if (result.Contains(cand) || NearAny(cand, banned) || NearAny(cand, currentAll)) continue;
                    double md = currentAll.Count > 0 ? currentAll.Min(q => cand.DistanceTo(q)) : 9999.0;
                    if (md + 1e-9 < sep) continue;
                    double value = cand.Score + 1.5 * Math.Min(md, 10.0);
                    if (value > bestValue) { bestValue = value; best = cand; }
                }
                if (best is null) break;
                best.ForcedFallback = true;
                result.Add(best);
                forced.Add(best);
                currentAll.Add(best);
            }
            if (result.Count >= needed) break;
        }
        return (result, forced);
    }

    sealed record SlotRow(int Slot, PinCandidate? Old, bool Keep);

    /// <summary><c>_smart_pin_assign_replacements_to_slots</c>: replacements go to the unsafe slots with the least total movement.</summary>
    static Dictionary<int, PinCandidate> AssignReplacements(List<PinCandidate> replacements, List<SlotRow> rows)
    {
        var mapping = new Dictionary<int, PinCandidate>();
        if (replacements.Count == 0) return mapping;
        var oldSlots = rows.Where(r => r.Old is not null).ToList();
        var missingSlots = rows.Where(r => r.Old is null).ToList();
        var remaining = replacements.ToList();
        if (oldSlots.Count > 0)
        {
            int n = Math.Min(oldSlots.Count, remaining.Count);
            List<PinCandidate>? best = null;
            double bestCost = double.PositiveInfinity;
            foreach (var chosen in Permutations(remaining, n))
            {
                double cost = 0;
                for (int i = 0; i < n; i++) cost += Hypot(chosen[i].X - oldSlots[i].Old!.X, chosen[i].Z - oldSlots[i].Old!.Z);
                if (cost < bestCost) { bestCost = cost; best = chosen; }
            }
            if (best is not null && best.Count > 0)
                for (int i = 0; i < best.Count; i++)
                {
                    mapping[oldSlots[i].Slot] = best[i];
                    remaining.Remove(best[i]);
                }
        }
        foreach (var (row, c) in missingSlots.Zip(remaining)) mapping[row.Slot] = c;
        return mapping;
    }

    /// <summary><c>itertools.permutations(items, r)</c> in the same (lexicographic by position) order.</summary>
    static IEnumerable<List<T>> Permutations<T>(List<T> items, int r)
    {
        var used = new bool[items.Count];
        var current = new List<T>();
        IEnumerable<List<T>> Recurse()
        {
            if (current.Count == r) { yield return current.ToList(); yield break; }
            for (int i = 0; i < items.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current.Add(items[i]);
                foreach (var p in Recurse()) yield return p;
                current.RemoveAt(current.Count - 1);
                used[i] = false;
            }
        }
        return Recurse();
    }

    // ---------------------------------------------------------------- the pass

    public static SmartPinsReport Apply(JsonObject course, string mode = "repair", IProgress<string>? log = null)
    {
        mode = (string.IsNullOrWhiteSpace(mode) ? "repair" : mode).Trim().ToLowerInvariant();
        if (mode is not ("repair" or "rebuild")) throw new ArgumentException("Smart pin mode must be 'repair' or 'rebuild'.");
        var holes = Tees.TeeGeometry.Holes(course);
        if (holes.Count == 0) throw new InvalidOperationException("No holes3[] data was found.");
        log?.Report(mode == "repair"
            ? "=== SMART PIN PLACEMENT: PRESERVE GOOD / REPAIR UNSAFE ==="
            : "=== SMART PIN PLACEMENT: REBUILD ALL 4 TERRAIN-SCORED PINS ===");

        var sampler = new MultiScaleTerrain(course);
        var (greens, bunkers) = GreenGeometries(course);
        if (greens.Count == 0) throw new InvalidOperationException("No putting-green spline geometry was detected.");
        var bunkerPoints = bunkers.SelectMany(b => b.Poly).Select(p => (p.X, p.Z)).ToList();
        var bunkerTree = bunkerPoints.Count > 0 ? new KdTree(bunkerPoints) : null;

        var used = new HashSet<int>();
        var details = new List<PinHoleReport>();
        int totalCandidates = 0, totalPreserved = 0, totalMoved = 0, totalAdded = 0;
        for (int h = 0; h < holes.Count; h++)
        {
            if (holes[h] is not JsonObject hole) continue;
            int holeIndex = h + 1;
            var green = HoleGreen(hole, greens, used) ?? throw new InvalidOperationException($"Hole {holeIndex}: no green spline could be assigned.");
            used.Add(green.Index);
            var candidates = CandidateGrid(green.Poly, sampler, bunkerTree);
            totalCandidates += candidates.Count;
            var wps = Json.Arr(hole, "waypoints");
            var (anchorX, anchorZ) = Json.XZ(wps[^1]);

            List<PinCandidate> selected;
            double minSep;
            if (mode == "repair")
            {
                var existing = Json.Arr(hole, "pins").Take(Count).ToList();
                var preserved = new List<PinCandidate>();
                var rows = new List<SlotRow>();
                for (int slot = 0; slot < Count; slot++)
                {
                    PinCandidate? old = null;
                    if (slot < existing.Count)
                    {
                        var pos = existing[slot]?["position"];
                        double x = anchorX + Json.Num(pos?["x"], 0.0), z = anchorZ + Json.Num(pos?["z"], 0.0);
                        old = EvaluateExact(sampler, x, z, green.Poly, bunkerTree);
                        if (old is not null) AddOrientation(old, hole, green);
                    }
                    if (old is not null && ExistingIsSafe(old) && preserved.All(q => Hypot(old.X - q.X, old.Z - q.Z) >= 0.75))
                    {
                        old.PreservedExisting = true;
                        old.Slot = slot;
                        preserved.Add(old);
                        rows.Add(new SlotRow(slot, old, true));
                        continue;
                    }
                    rows.Add(new SlotRow(slot, old, false));
                }
                int need = Count - preserved.Count;
                var bannedOld = rows.Where(r => !r.Keep && r.Old is not null).Select(r => r.Old!).ToList();
                var (replacements, _) = FillLocked(candidates, hole, green, preserved, need, bannedOld);
                if (replacements.Count != need)
                {
                    log?.Report($"WARNING: Hole {holeIndex}: could not resolve all replacement pins; existing pin array preserved.");
                    details.Add(new PinHoleReport(holeIndex, null, 0, null, "replacement-failed-existing-preserved", 0, 0, 0, 0, 0, 0, null, []));
                    continue;
                }
                var assignment = AssignReplacements(replacements, rows.Where(r => !r.Keep).ToList());
                var final = new PinCandidate?[Count];
                foreach (var r in rows) final[r.Slot] = r.Keep ? r.Old : assignment.GetValueOrDefault(r.Slot);
                if (final.Any(c => c is null))
                {
                    log?.Report($"WARNING: Hole {holeIndex}: incomplete repair mapping; existing pin array preserved.");
                    continue;
                }
                selected = final.Select(c => c!).ToList();
                minSep = MinPairwise(selected);
                foreach (var r in rows)
                {
                    if (r.Keep) continue;
                    var fresh = selected[r.Slot];
                    int pinNo = r.Slot + 1;
                    if (r.Old is null)
                    {
                        log?.Report($"Hole {holeIndex} Pin {pinNo}: missing -> added at {Py.R(fresh.SlopePct, 2)}% slope ({Py.R(fresh.MaxLocalGradePct, 2)}% local).");
                        totalAdded++;
                    }
                    else
                    {
                        log?.Report($"Hole {holeIndex} Pin {pinNo}: unsafe at {Py.R(r.Old.SlopePct, 2)}% slope / {Py.R(r.Old.MaxLocalGradePct, 2)}% local / edge {Py.R(r.Old.EdgeClearance, 2)} m " +
                                    $"-> repositioned to {Py.R(fresh.SlopePct, 2)}% / {Py.R(fresh.MaxLocalGradePct, 2)}% local.");
                        totalMoved++;
                    }
                    if (fresh.ForcedFallback)
                        log?.Report($"WARNING: Hole {holeIndex} Pin {pinNo} replacement required forced fallback at {Py.R(fresh.SlopePct, 2)}% slope " +
                                    $"({Py.R(fresh.MaxLocalGradePct, 2)}% local), edge {Py.R(fresh.EdgeClearance, 2)} m.");
                }
                totalPreserved += preserved.Count;
                if (preserved.Count == 4) log?.Report($"Hole {holeIndex}: 4 existing pins accepted - no changes.");
            }
            else
            {
                (selected, minSep) = SelectFour(candidates, hole, green);
                if (selected.Count != Count)
                {
                    log?.Report($"WARNING: Hole {holeIndex}: fewer than four distinct interior pin candidates exist; existing pins preserved.");
                    details.Add(new PinHoleReport(holeIndex, green.Index, candidates.Count, null, "fewer-than-four-distinct-candidates-existing-pins-preserved", 0, 0, 0, 0, 0, 0, null, []));
                    continue;
                }
            }

            hole["pins"] = Json.Array(selected.Select(c => (JsonNode?)new JsonObject
            {
                ["position"] = Json.Xyz(c.X - anchorX, 0.0, c.Z - anchorZ),
                ["description"] = 0,
            }));
            int worstTierIndex = selected.Max(c => c.TierIndex);
            string worstTier = worstTierIndex < Tiers.Length ? Tiers[worstTierIndex].Name : "emergency+";
            double minEdge = selected.Min(c => c.EdgeClearance);
            double maxSlope = selected.Max(c => c.SlopePct);
            double maxLocal = selected.Max(c => c.MaxLocalGradePct);
            int forcedCount = selected.Count(c => c.ForcedFallback);
            int preservedCount = selected.Count(c => c.PreservedExisting);
            details.Add(new PinHoleReport(holeIndex, green.Index, candidates.Count, mode, null, preservedCount, forcedCount, minSep, minEdge, maxSlope, maxLocal, worstTier, selected));
            log?.Report($"Hole {holeIndex}: 4 pins / {candidates.Count} candidates / preserved {preservedCount} / min edge {Py.R(minEdge, 2)} m / " +
                        $"min spacing {Py.R(minSep, 2)} m / max slope {Py.R(maxSlope, 2)}% / max local {Py.R(maxLocal, 2)}%.");
        }

        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        int totalPins = details.Sum(d => d.Pins.Count);
        double greenSpeed = Json.Num(course["greenSpeed"], -1.0);
        meta[MetaKey] = new JsonObject
        {
            ["countPerGreen"] = Count,
            ["holes"] = details.Count,
            ["totalPins"] = totalPins,
            ["totalCandidates"] = totalCandidates,
            ["mode"] = mode,
            ["preservedPins"] = totalPreserved,
            ["movedPins"] = totalMoved,
            ["addedPins"] = totalAdded,
            ["forcedFallbackPins"] = details.Sum(d => d.ForcedFallbackPins),
            ["fastTargetSlopePct"] = Json.F(FastTargetSlope),
            ["fastMaxSlopePct"] = Json.F(FastMaxSlope),
            ["courseGreenSpeed"] = Json.F(double.IsFinite(greenSpeed) && greenSpeed != 0 ? greenSpeed : -1.0),
            ["method"] = mode == "repair" ? "preserve-good-repair-unsafe" : "rebuild-all-four-fast-green-safe",
            ["details"] = Json.Array(details.Select(d => (JsonNode?)DetailJson(d))),
        };
        log?.Report($"Smart pin placement complete: {totalPins} pins / preserved {totalPreserved} / moved {totalMoved} / added {totalAdded}.");
        return new SmartPinsReport(details.Count, totalPins, mode, totalPreserved, totalMoved, totalAdded, details);
    }

    static JsonObject DetailJson(PinHoleReport d)
    {
        var o = new JsonObject { ["hole"] = d.Hole };
        if (d.Mode is null)
        {
            if (d.GreenSpline is int gs) { o["greenSpline"] = gs; o["candidates"] = d.Candidates; }
            o["warning"] = d.Warning;
            o["pins"] = new JsonArray();
            return o;
        }
        o["greenSpline"] = d.GreenSpline;
        o["candidates"] = d.Candidates;
        o["mode"] = d.Mode;
        o["preservedPins"] = d.PreservedPins;
        o["forcedFallbackPins"] = d.ForcedFallbackPins;
        o["minimumSeparationM"] = Json.F(d.MinimumSeparation);
        o["minimumEdgeClearanceM"] = Json.F(d.MinimumEdgeClearance);
        o["maximumSlopePct"] = Json.F(d.MaximumSlopePct);
        o["maximumLocalGradePct"] = Json.F(d.MaximumLocalGradePct);
        o["worstTier"] = d.WorstTier;
        o["pins"] = Json.Array(d.Pins.Select(c => (JsonNode?)new JsonObject
        {
            ["x"] = Json.F(c.X),
            ["z"] = Json.F(c.Z),
            ["edgeClearanceM"] = Json.F(c.EdgeClearance),
            ["slopePct"] = Json.F(c.SlopePct),
            ["maxLocalGradePct"] = Json.F(c.MaxLocalGradePct),
            ["tier"] = c.Tier,
            ["preservedExisting"] = c.PreservedExisting,
            ["forcedFallback"] = c.ForcedFallback,
        }));
        return o;
    }
}
