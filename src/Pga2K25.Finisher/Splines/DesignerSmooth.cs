using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;

namespace Pga2K25.Finisher.Splines;

public sealed record DesignerSmoothReport(IReadOnlyList<string> Selected, int Inspected, int Changed, int Waypoints,
    int PointsChanged, int GuardedTees, int DegeneratePoints);

/// <summary>
/// Pass 1, <c>designer_smooth_selected_splines</c>: rewrites every waypoint's Bézier handles along the
/// neighbour tangent (prev → next), each handle 37.5 % of its adjacent segment, clamped to 1–30 m —
/// the same result as the Designer's own Smooth Spline command. Simple tee boxes (&lt; 6 points) are left alone.
/// </summary>
public static class DesignerSmooth
{
    public const double HandleFactor = 0.375, HandleMin = 1.0, HandleMax = 30.0;
    public const int TeeMinWaypoints = 6;

    public static DesignerSmoothReport Apply(JsonObject course, IEnumerable<string> categories, IProgress<string>? log = null)
    {
        var requested = Requested(categories);
        if (requested.Count == 0) return new DesignerSmoothReport([], 0, 0, 0, 0, 0, 0);

        var roles = FeatureClassifier.Classify(course);
        var splines = Json.Arr(course, "surfaceSplines2");
        log?.Report("=== NATIVE DESIGNER SMOOTH SPLINE (runs first) ===");
        log?.Report("Formula: tangent(prev->next), handle=37.5% adjacent segment, clamp 1..30 m.");

        int inspected = 0, changed = 0, waypoints = 0, pointsChanged = 0, guardedTees = 0, degenerate = 0;
        foreach (var category in SplineCategory.All)
        {
            if (!requested.Contains(category)) continue;
            int cSplines = 0, cChanged = 0, cWaypoints = 0, cPoints = 0, cGuarded = 0, cDegenerate = 0;
            foreach (var (index, role) in roles)
            {
                if (role != category) continue;
                var r = SmoothOne((JsonObject)splines[index]!, category);
                cSplines++;
                cWaypoints += r.Waypoints;
                cPoints += r.PointsChanged;
                cDegenerate += r.Degenerate;
                if (r.Guarded)
                {
                    cGuarded++;
                    log?.Report($"Tee box spline {index}: skipped native smoothing; {r.Waypoints}-waypoint simple tee protected (minimum 6).");
                }
                else if (r.Changed) cChanged++;
            }
            inspected += cSplines;
            changed += cChanged;
            waypoints += cWaypoints;
            pointsChanged += cPoints;
            if (category == SplineCategory.TeeBox) guardedTees += cGuarded;
            degenerate += cDegenerate;
            string extra = category == SplineCategory.TeeBox && cGuarded > 0 ? $", {cGuarded} simple tees protected" : "";
            log?.Report($"{SplineCategory.Label(category)}: {cSplines} splines, {cChanged} smoothed, {cPoints} waypoint handle pairs/ends rewritten{extra}.");
        }

        var selected = requested.Order(StringComparer.Ordinal).ToList();
        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        meta["designerSmooth"] = new JsonObject
        {
            ["selected"] = Json.Array(selected.Select(s => (JsonNode?)Json.S(s))),
            ["changed"] = changed,
            ["pointsChanged"] = pointsChanged,
            ["guardedSimpleTees"] = guardedTees,
            ["factor"] = Json.F(HandleFactor),
            ["minimumHandle"] = Json.F(HandleMin),
            ["maximumHandle"] = Json.F(HandleMax),
        };
        log?.Report($"Native Designer Smooth complete: {changed} splines changed; {guardedTees} simple tee boxes protected.");
        return new DesignerSmoothReport(selected, inspected, changed, waypoints, pointsChanged, guardedTees, degenerate);
    }

    internal static HashSet<string> Requested(IEnumerable<string> categories) =>
        categories.Select(c => c.Trim().ToLowerInvariant()).Where(c => SplineCategory.All.Contains(c)).ToHashSet();

    static double ClampHandle(double length) => Math.Min(HandleMax, Math.Max(HandleMin, length * HandleFactor));

    static Vec2? Unit(Vec2 v)
    {
        double mag = Math.Sqrt(v.X * v.X + v.Z * v.Z);
        return mag <= 1e-12 ? null : new Vec2(v.X / mag, v.Z / mag);
    }

    static void SetHandle(JsonObject wp, string key, Vec2 p)
    {
        if (wp[key] is not JsonObject handle) wp[key] = handle = new JsonObject { ["x"] = Json.F(0), ["y"] = Json.F(0), ["z"] = Json.F(0) };
        handle["x"] = Json.F(p.X);
        handle["z"] = Json.F(p.Z);
    }

    record OneResult(bool Changed, int Waypoints, bool Guarded, int PointsChanged, int Degenerate);

    /// <summary><c>_designer_smooth_one_spline</c>.</summary>
    static OneResult SmoothOne(JsonObject spline, string category)
    {
        var wps = Json.Arr(spline, "waypoints");
        int n = wps.Count;
        bool closed = FinisherGeometry.IsClosed(spline);
        if (category == SplineCategory.TeeBox && n < TeeMinWaypoints) return new(false, n, true, 0, 0);
        if ((closed && n < 3) || (!closed && n < 2)) return new(false, n, false, 0, 0);

        var pts = wps.Select(w => new Vec2(Json.Num(w?["waypoint"]?["x"]), Json.Num(w?["waypoint"]?["z"]))).ToList();
        int changed = 0, degenerate = 0;
        for (int i = 0; i < n; i++)
        {
            var wp = (JsonObject)wps[i]!;
            var p = pts[i];
            if (!closed && i == 0)
            {
                if (Unit(pts[1] - p) is not Vec2 t) { degenerate++; continue; }
                SetHandle(wp, "pointTwo", p + t * ClampHandle(Vec2.Distance(pts[1], p)));
                changed++;
                continue;
            }
            if (!closed && i == n - 1)
            {
                if (Unit(p - pts[i - 1]) is not Vec2 t) { degenerate++; continue; }
                SetHandle(wp, "pointOne", p - t * ClampHandle(Vec2.Distance(p, pts[i - 1])));
                changed++;
                continue;
            }
            var prev = pts[(i - 1 + n) % n];
            var next = pts[(i + 1) % n];
            if (Unit(next - prev) is not Vec2 tangent) { degenerate++; continue; }
            SetHandle(wp, "pointOne", p - tangent * ClampHandle(Vec2.Distance(p, prev)));
            SetHandle(wp, "pointTwo", p + tangent * ClampHandle(Vec2.Distance(next, p)));
            changed++;
        }
        return new(changed > 0, n, false, changed, degenerate);
    }
}
