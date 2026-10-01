using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;

namespace Pga2K25.Finisher.Splines;

public sealed record SplineOptimizeReport(IReadOnlyList<string> Selected, int Inspected, int Changed, int Before, int After,
    int Removed, double MaxError);

/// <summary>
/// Pass 2, <c>optimize_selected_splines</c> (<c>smooth_selected_splines</c>): greedily deletes waypoints
/// whose removal keeps the curve within a per-category tolerance (two-way Hausdorff check against the
/// original segments), protecting ends, sharp corners, lopsided handles and near-coincident points.
/// Neighbouring handles are left as they are.
/// </summary>
public static class SplineOptimizer
{
    public static double Tolerance(string category) => category switch
    {
        SplineCategory.Fairway or SplineCategory.Rough => 0.10,
        SplineCategory.CartPath => 0.08,
        _ => 0.05,   // tee boxes, greens, bunkers
    };

    public static SplineOptimizeReport Apply(JsonObject course, IEnumerable<string> categories, IProgress<string>? log = null)
    {
        var requested = DesignerSmooth.Requested(categories);
        var selected = requested.Order(StringComparer.Ordinal).ToList();
        if (requested.Count == 0) return new SplineOptimizeReport([], 0, 0, 0, 0, 0, 0);

        var roles = FeatureClassifier.Classify(course);
        var splines = Json.Arr(course, "surfaceSplines2");
        log?.Report("=== SPLINE OPTIMIZATION (runs after Designer Smooth, before terrain finishing) ===");
        int inspected = 0, changed = 0, before = 0, after = 0, removed = 0;
        double maxError = 0;
        foreach (var category in SplineCategory.All)
        {
            if (!requested.Contains(category)) continue;
            double tolerance = Tolerance(category);
            int cSplines = 0, cChanged = 0, cBefore = 0, cAfter = 0, cRemoved = 0;
            double cMax = 0;
            foreach (var (index, role) in roles)
            {
                if (role != category) continue;
                var r = OptimizeOne((JsonObject)splines[index]!, tolerance);
                cSplines++;
                cBefore += r.Before;
                cAfter += r.After;
                cRemoved += r.Before - r.After;
                cMax = Math.Max(cMax, r.MaxError);
                if (r.Changed) cChanged++;
            }
            inspected += cSplines; changed += cChanged; before += cBefore; after += cAfter; removed += cRemoved;
            maxError = Math.Max(maxError, cMax);
            log?.Report($"{SplineCategory.Label(category)}: {cSplines} splines, {cChanged} changed, {cBefore} -> {cAfter} waypoints ({cRemoved} removed), max deviation {Py.R(cMax, 3)} m.");
        }

        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        meta["splineOptimization"] = new JsonObject
        {
            ["selected"] = Json.Array(selected.Select(s => (JsonNode?)Json.S(s))),
            ["removed"] = removed,
            ["changed"] = changed,
            ["maxDeviation"] = Json.F(maxError),
        };
        log?.Report($"Spline optimization complete: {inspected} inspected, {changed} changed, {removed} waypoints removed.");
        return new SplineOptimizeReport(selected, inspected, changed, before, after, removed, maxError);
    }

    record OneResult(bool Changed, int Before, int After, double MaxError);

    /// <summary><c>_smooth_one_spline</c>.</summary>
    static OneResult OptimizeOne(JsonObject spline, double tolerance)
    {
        var wps = Json.Arr(spline, "waypoints");
        int n = wps.Count;
        bool closed = FinisherGeometry.IsClosed(spline);
        if (n < 3) return new(false, n, n, 0);
        var w = wps.Select(Read).ToArray();
        var alive = Enumerable.Repeat(true, n).ToArray();
        int aliveCount = n, minPoints = closed ? 4 : 2;
        bool changed = false, passChange = true;
        double maxError = 0;
        while (passChange && aliveCount > minPoints)
        {
            passChange = false;
            for (int i = 0; i < n; i++)
            {
                if (!alive[i] || aliveCount <= minPoints) continue;
                int? prev = PrevAlive(alive, i, closed), next = NextAlive(alive, i, closed);
                if (prev is null || next is null || prev == next) continue;
                if (Protected(w, alive, i, prev.Value, next.Value, closed)) continue;
                double err = CandidateError(w, prev.Value, next.Value, closed, tolerance);
                if (err <= tolerance + 1e-9)
                {
                    alive[i] = false;
                    aliveCount--;
                    changed = passChange = true;
                    maxError = Math.Max(maxError, err);
                }
            }
        }
        if (changed)
            spline["waypoints"] = Json.Array(Enumerable.Range(0, n).Where(i => alive[i]).Select(i => wps[i]?.DeepClone()));
        return new(changed, n, aliveCount, maxError);
    }

    readonly record struct Wp(Vec2 In, Vec2 Anchor, Vec2 Out);

    static Wp Read(JsonNode? w) => new(P(w?["pointOne"]), P(w?["waypoint"]), P(w?["pointTwo"]));

    static Vec2 P(JsonNode? p) => new(Json.Num(p?["x"]), Json.Num(p?["z"]));

    static int? PrevAlive(bool[] alive, int i, bool closed)
    {
        int n = alive.Length, j = i;
        for (int step = 0; step < n; step++)
        {
            if (j == 0)
            {
                if (!closed) return null;
                j = n - 1;
            }
            else j--;
            if (alive[j]) return j;
        }
        return null;
    }

    static int? NextAlive(bool[] alive, int i, bool closed)
    {
        int n = alive.Length, j = i;
        for (int step = 0; step < n; step++)
        {
            j++;
            if (j >= n)
            {
                if (!closed) return null;
                j = 0;
            }
            if (alive[j]) return j;
        }
        return null;
    }

    static double CenterAngleDeg(Vec2 a, Vec2 b, Vec2 c)
    {
        var va = a - b;
        var vb = c - b;
        double am = va.Length, bm = vb.Length;
        if (am < 1e-9 || bm < 1e-9) return 180.0;
        double dot = Math.Min(1.0, Math.Max(-1.0, (va.X * vb.X + va.Z * vb.Z) / (am * bm)));
        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    /// <summary><c>_candidate_protected</c>.</summary>
    static bool Protected(Wp[] w, bool[] alive, int i, int prev, int next, bool closed)
    {
        int n = w.Length;
        if (!closed && (i == 0 || i + 1 == n)) return true;
        var center = w[i].Anchor;
        double h1 = Vec2.Distance(w[i].In, center), h2 = Vec2.Distance(w[i].Out, center);
        if (h1 < 0.035 || h2 < 0.035) return true;
        double mn = Math.Min(h1, h2), mx = Math.Max(h1, h2);
        if (mn > 1e-6 && mx / mn > 6.0) return true;
        if (CenterAngleDeg(w[prev].Anchor, center, w[next].Anchor) <= 110.0) return true;
        for (int j = 0; j < n; j++)
        {
            if (j == i || j == prev || j == next || !alive[j]) continue;
            if (Vec2.Distance(center, w[j].Anchor) < 0.3) return true;
        }
        return false;
    }

    static Vec2 Bezier(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double t)
    {
        double u = 1.0 - t;
        return p0 * (u * u * u) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
    }

    /// <summary><c>_original_segment_indices</c>.</summary>
    static List<int> SegmentIndices(int n, int a, int b, bool closed)
    {
        if (a == b) return closed ? Enumerable.Range(0, n).ToList() : [];
        if (!closed) return b > a ? Enumerable.Range(a, b - a).ToList() : [];
        var result = new List<int>();
        int cur = a;
        while (cur != b && result.Count <= n)
        {
            result.Add(cur);
            cur = (cur + 1) % n;
        }
        return result;
    }

    /// <summary><c>_candidate_error</c>: original prev→next curve vs the single Bézier that would replace it.</summary>
    static double CandidateError(Wp[] w, int prev, int next, bool closed, double limit, int perSegment = 10)
    {
        int n = w.Length;
        var segs = SegmentIndices(n, prev, next, closed);
        if (segs.Count == 0) return double.PositiveInfinity;
        var reference = new List<Vec2>();
        for (int si = 0; si < segs.Count; si++)
        {
            int cur = segs[si], nx = (cur + 1) % n;
            for (int k = 0; k <= perSegment; k++)
            {
                if (si > 0 && k == 0) continue;
                reference.Add(Bezier(w[cur].Anchor, w[cur].Out, w[nx].In, w[nx].Anchor, k / (double)perSegment));
            }
        }
        if (reference.Count < 2) return double.PositiveInfinity;
        var candidate = Enumerable.Range(0, 33)
            .Select(k => Bezier(w[prev].Anchor, w[prev].Out, w[next].In, w[next].Anchor, k / 32.0)).ToList();
        double worst = MaxDistanceToPolyline(reference, candidate);
        if (worst > limit) return worst;
        return Math.Max(worst, MaxDistanceToPolyline(candidate, reference));
    }

    /// <summary><c>_points_to_polyline_max_distance</c>.</summary>
    static double MaxDistanceToPolyline(List<Vec2> points, List<Vec2> line)
    {
        if (points.Count == 0 || line.Count == 0) return double.PositiveInfinity;
        if (line.Count == 1) return points.Max(p => Vec2.Distance(p, line[0]));
        double worst = 0;
        foreach (var p in points)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i];
                var d = line[i + 1] - a;
                double l2 = d.X * d.X + d.Z * d.Z;
                double t = l2 > 1e-18 ? Math.Clamp(((p.X - a.X) * d.X + (p.Z - a.Z) * d.Z) / l2, 0.0, 1.0) : 0.0;
                double dx = p.X - (a.X + t * d.X), dz = p.Z - (a.Z + t * d.Z);
                best = Math.Min(best, dx * dx + dz * dz);
            }
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return worst;
    }
}
