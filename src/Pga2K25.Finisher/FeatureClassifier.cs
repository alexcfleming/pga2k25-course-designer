using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Tees;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher;

/// <summary>Spline roles used by the smoothing, edging, pin and weathering passes.</summary>
public static class SplineCategory
{
    public const string TeeBox = "teebox", Green = "green", Fairway = "fairway", Rough = "rough", Bunker = "bunker", CartPath = "cartpath";

    /// <summary>The processing / reporting order (<c>SPLINE_SMOOTH_CATEGORIES</c>).</summary>
    public static readonly string[] All = [TeeBox, Green, Fairway, Rough, Bunker, CartPath];

    public static string Label(string category) => category switch
    {
        TeeBox => "Tee Boxes",
        Green => "Greens",
        Fairway => "Fairways",
        Rough => "Rough",
        Bunker => "Bunkers",
        CartPath => "Cart Paths",
        _ => category,
    };
}

/// <summary>Which surface splines are tees, greens, fairways, rough, bunkers, cart paths and water.</summary>
public static class FeatureClassifier
{
    /// <summary><c>_hole_reference_points</c>: every tee marker, and every green anchor (last waypoint).</summary>
    public static (List<Vec2> Tees, List<Vec2> Greens) HoleReferencePoints(JsonObject course)
    {
        var tees = new List<Vec2>();
        var greens = new List<Vec2>();
        foreach (var hole in TeeGeometry.Holes(course).OfType<JsonObject>())
        {
            tees.AddRange(TeeGeometry.TeePoints(hole));
            if (TeeGeometry.TerminalPoint(hole) is Vec2 g) greens.Add(g);
        }
        return (tees, greens);
    }

    /// <summary><c>_surface1_role</c>: a closed surface-1 spline is a tee box or a green by which anchors it contains.</summary>
    static string? Surface1Role(JsonObject spline, List<Vec2> teePoints, List<Vec2> greenPoints)
    {
        if (!IsClosed(spline)) return null;
        var poly = DensePolyline(spline, 12, close: true);
        if (poly.Count < 3) return null;
        int th = teePoints.Count(p => Contains(poly, p));
        int gh = greenPoints.Count(p => Contains(poly, p));
        if (th > 0 && gh == 0) return SplineCategory.TeeBox;
        if (gh > 0 && th == 0) return SplineCategory.Green;
        if (th == 0 && gh == 0) return null;
        var wps = Json.Arr(spline, "waypoints");
        if (wps.Count == 0) return null;
        double cx = wps.Average(w => Json.Num(w?["waypoint"]?["x"])), cz = wps.Average(w => Json.Num(w?["waypoint"]?["z"]));
        double td = teePoints.Count > 0 ? teePoints.Min(p => Hypot(cx - p.X, cz - p.Z)) : double.PositiveInfinity;
        double gd = greenPoints.Count > 0 ? greenPoints.Min(p => Hypot(cx - p.X, cz - p.Z)) : double.PositiveInfinity;
        return td <= gd ? SplineCategory.TeeBox : SplineCategory.Green;
    }

    /// <summary>
    /// <c>classify_spline_features</c>: index → category. Pads under a tee marker are tee boxes whatever
    /// their surface; then 0 bunker, 1 green/tee, 2 fairway, 3 rough, 7 (≥ 1.9 m, unfilled) cart path.
    /// </summary>
    public static SortedDictionary<int, string> Classify(JsonObject course)
    {
        var (tees, greens) = HoleReferencePoints(course);
        var activeTees = TeeGeometry.ActiveSplineIndices(course);
        var result = new SortedDictionary<int, string>();
        var splines = Json.Arr(course, "surfaceSplines2");
        for (int index = 0; index < splines.Count; index++)
        {
            if (splines[index] is not JsonObject spline) continue;
            int surface = Json.Int(spline["surface"], -1);
            double width = Json.Num(spline["width"], 0.0);
            string? category = null;
            if (activeTees.Contains(index)) category = SplineCategory.TeeBox;
            else if (surface == Surfaces.Bunker) category = SplineCategory.Bunker;
            else if (surface == Surfaces.Green) category = Surface1Role(spline, tees, greens);
            else if (surface == Surfaces.Fairway) category = SplineCategory.Fairway;
            else if (surface == Surfaces.Rough) category = SplineCategory.Rough;
            else if (surface == Surfaces.CartPath && width >= 1.9 && !Json.Truthy(spline["isFilled"])) category = SplineCategory.CartPath;
            if (category is not null) result[index] = category;
        }
        return result;
    }

    /// <summary><c>spline_feature_counts</c>.</summary>
    public static Dictionary<string, int> Counts(JsonObject course)
    {
        var counts = SplineCategory.All.ToDictionary(c => c, _ => 0);
        foreach (var category in Classify(course).Values)
            if (counts.ContainsKey(category)) counts[category]++;
        return counts;
    }

    /// <summary><c>find_cartpaths</c>: open-or-closed surface 7 splines at least 1.9 m wide that are not filled.</summary>
    public static List<(int Index, JsonObject Spline)> CartPaths(JsonObject course)
    {
        var result = new List<(int, JsonObject)>();
        var splines = Json.Arr(course, "surfaceSplines2");
        for (int i = 0; i < splines.Count; i++)
        {
            if (splines[i] is not JsonObject s) continue;
            if (Json.Int(s["surface"], -1) == Surfaces.CartPath && Json.Num(s["width"], 0.0) >= 1.9 && !Json.Truthy(s["isFilled"]))
                result.Add((i, s));
        }
        return result;
    }

    /// <summary><c>find_water_splines</c>: closed thin water outlines (areas) and open waterways ≥ 1 m wide (lines).</summary>
    public static (List<(int Index, JsonObject Spline)> Areas, List<(int Index, JsonObject Spline)> Lines) WaterSplines(JsonObject course)
    {
        var areas = new List<(int, JsonObject)>();
        var lines = new List<(int, JsonObject)>();
        var splines = Json.Arr(course, "surfaceSplines2");
        for (int i = 0; i < splines.Count; i++)
        {
            if (splines[i] is not JsonObject s || Json.Int(s["surface"], -1) != Surfaces.Water) continue;
            double width = Json.Num(s["width"], 0.0);
            double state = Json.Num(s["state"], 0);
            bool closed = Json.Truthy(s["isFilled"]) || (double.IsFinite(state) ? (int)state != 0 : false);
            if (closed && width <= 0.25) areas.Add((i, s));
            else if (!closed && width >= 1.0) lines.Add((i, s));
        }
        return (areas, lines);
    }
}
