using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Operations;

public sealed record HeavyRoughReport(int Created, int FeatureSplines, IReadOnlyDictionary<string, int> FeatureSplineCounts,
    IReadOnlyDictionary<string, int> BrushCounts, int AmbiguousOutsideNormalFallbacks);

/// <summary>
/// Pass 3, <c>paint_heavy_rough_on_detailed_terrain</c>: native Heavy Rough surface brushes (type 8,
/// category 4) every 9 m with centres 2 m outside each green / fairway / bunker / tee outline, and every
/// 12 m along cart-path centrelines. Written as a signed tail of <c>surfaceBrushes2</c>.
/// </summary>
public static class HeavyRough
{
    public const string MetaKey = "detailTerrainHeavyRough";
    public const int Surface = 4, BrushType = 8;
    public const double ClosedScale = 10.0, ClosedOffset = 2.0, ClosedStep = 9.0, CartScale = 13.0, CartStep = 12.0;
    static readonly string[] ClosedFeatures = [SplineCategory.Green, SplineCategory.Fairway, SplineCategory.Bunker, SplineCategory.TeeBox];

    /// <summary><c>remove_prior_detail_heavy_rough</c>: drops the previous tail only if it is untouched.</summary>
    public static int RemovePrior(JsonObject course, IProgress<string>? log = null)
    {
        if (Provenance.Existing(course, MetaKey) is not JsonObject meta) return 0;
        string storage = Json.Str(meta["storage"], "surfaceSplines2");
        if (storage is not ("surfaceSplines2" or "surfaceBrushes" or "surfaceBrushes2")) return 0;
        var items = Json.Arr(course, storage);
        int count = Json.Int(meta["count"], 0);
        int start = Json.Int(meta["start"], items.Count - count);
        string signature = Json.Str(meta["signature"], "");
        if (count <= 0 || start < 0 || start + count != items.Count)
        {
            log?.Report("Heavy Rough: prior generated tail was edited/reordered; preserving it.");
            return 0;
        }
        var tail = items.Skip(start).Take(count).ToList();
        if (signature.Length > 0 && Provenance.TailSignature(tail) != signature)
        {
            log?.Report("Heavy Rough: prior generated tail signature changed; preserving it.");
            return 0;
        }
        for (int i = items.Count - 1; i >= start; i--) items.RemoveAt(i);
        Provenance.Remove(course, MetaKey);
        string what = storage is "surfaceBrushes" or "surfaceBrushes2" ? "brushes" : "legacy splines";
        log?.Report($"Heavy Rough: removed prior Finisher-generated coverage ({count} {what}).");
        return count;
    }

    sealed record Feature(int Index, string Category, bool CartPath, List<Vec2> Polyline);

    /// <summary><c>_heavy_rough_feature_geometry</c>: closed outlines (explicitly closed) and cart-path centrelines.</summary>
    static List<Feature> Features(JsonObject course)
    {
        var classified = FeatureClassifier.Classify(course);
        var splines = Json.Arr(course, "surfaceSplines2");
        var result = new List<Feature>();
        foreach (var (index, category) in classified)
        {
            if (index < 0 || index >= splines.Count || splines[index] is not JsonObject spline) continue;
            if (ClosedFeatures.Contains(category))
            {
                if (!IsClosed(spline)) continue;
                var poly = DensePolyline(spline, 18, close: true);
                if (poly.Count < 3) continue;
                if (Vec2.Distance(poly[0], poly[^1]) > 1e-6) poly.Add(poly[0]);
                result.Add(new Feature(index, category, false, poly));
            }
            else if (category == SplineCategory.CartPath)
            {
                var line = DensePolyline(spline, 12, close: false);
                if (line.Count >= 2) result.Add(new Feature(index, SplineCategory.CartPath, true, line));
            }
        }
        return result;
    }

    /// <summary><c>_iter_arc_samples</c>: evenly spaced (x, z, tangent) samples; closed loops skip the duplicate end.</summary>
    internal static IEnumerable<(double X, double Z, double Tx, double Tz)> ArcSamples(IReadOnlyList<Vec2> points, double step, bool closed)
    {
        var arc = Arc.From(points);
        if (arc is null || arc.Length <= 1e-9) yield break;
        step = Math.Max(0.25, step);
        if (closed)
            for (double d = 0; d < arc.Length - 1e-7; d += step) yield return arc.At(d);
        else
            for (double d = 0; d <= arc.Length + 1e-7; d += step) yield return arc.At(Math.Min(d, arc.Length));
    }

    /// <summary>
    /// <c>_closed_edge_outside_center</c>: the sample moved <paramref name="offset"/> m along the normal to the
    /// outside. Ambiguous cases fall back to a short probe, then to "away from the vertex mean".
    /// </summary>
    static (Vec2? Center, bool Fallback) OutsideCenter(List<Vec2> polyline, double x, double z, double tx, double tz, double offset)
    {
        var polygon = polyline.ToList();
        if (polygon.Count > 1 && Vec2.Distance(polygon[0], polygon[^1]) <= 1e-6) polygon.RemoveAt(polygon.Count - 1);
        if (polygon.Count < 3) return (null, true);
        double n = Hypot(tx, tz);
        if (n <= 1e-9) return (null, true);
        double nx = -tz / n, nz = tx / n;
        var plus = new Vec2(x + nx * offset, z + nz * offset);
        var minus = new Vec2(x - nx * offset, z - nz * offset);
        bool plusInside = Contains(polygon, plus), minusInside = Contains(polygon, minus);
        if (plusInside != minusInside) return (plusInside ? minus : plus, false);

        double probe = Math.Min(0.5, Math.Max(0.1, offset * 0.25));
        bool p2 = Contains(polygon, x + nx * probe, z + nz * probe), m2 = Contains(polygon, x - nx * probe, z - nz * probe);
        if (p2 != m2) return (p2 ? minus : plus, false);

        var c = Mean(polygon);
        return (Vec2.Distance(plus, c) >= Vec2.Distance(minus, c) ? plus : minus, true);
    }

    /// <summary><c>_heavy_rough_surface_brush</c>.</summary>
    internal static JsonObject Brush(double x, double z, double scale)
    {
        scale = Math.Max(0.05, scale);
        return new JsonObject
        {
            ["position"] = Json.XGroundZ(x, z),
            ["rotation"] = Json.Xyz(0, 0, 0),
            ["scale"] = Json.Xyz(scale, 1.0, scale),
            ["type"] = BrushType,
            ["surfaceCategory"] = Surface,
            ["value"] = Json.F(1.0),
            ["holeId"] = -1,
        };
    }

    public static HeavyRoughReport Apply(JsonObject course, IProgress<string>? log = null)
    {
        RemovePrior(course, log);
        var features = Features(course);
        if (features.Count == 0) throw new InvalidOperationException("No Green/Fairway/Bunker/Tee/Cart Path feature geometry was found.");

        var candidates = new List<(double X, double Z, double Scale, string Category, int Index)>();
        int fallbacks = 0;
        foreach (var f in features)
        {
            if (f.CartPath)
            {
                foreach (var (x, z, _, _) in ArcSamples(f.Polyline, CartStep, closed: false))
                    candidates.Add((x, z, CartScale, SplineCategory.CartPath, f.Index));
                continue;
            }
            foreach (var (x, z, tx, tz) in ArcSamples(f.Polyline, ClosedStep, closed: true))
            {
                var (center, fallback) = OutsideCenter(f.Polyline, x, z, tx, tz, ClosedOffset);
                if (center is not Vec2 c) continue;
                if (fallback) fallbacks++;
                candidates.Add((c.X, c.Z, ClosedScale, f.Category, f.Index));
            }
        }

        // One brush per 1 m cell; the larger brush wins a cell (first come on a tie).
        var dedup = new Dictionary<(long, long), int>();
        var order = new List<(long, long)>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var key = ((long)Math.Round(c.X / 1.0, MidpointRounding.ToEven), (long)Math.Round(c.Z / 1.0, MidpointRounding.ToEven));
            if (!dedup.TryGetValue(key, out int prior)) { dedup[key] = i; order.Add(key); }
            else if (c.Scale > candidates[prior].Scale) dedup[key] = i;
        }
        var kept = order.Select(k => candidates[dedup[k]])
            .OrderBy(c => c.Z).ThenBy(c => c.X).ThenBy(c => c.Category, StringComparer.Ordinal).ThenBy(c => c.Index).ToList();

        var brushes = Json.EnsureArray(course, "surfaceBrushes2");
        int start = brushes.Count;
        var generated = kept.Select(c => Brush(c.X, c.Z, c.Scale)).ToList();
        foreach (var b in generated) brushes.Add(b);

        var featureCounts = new Dictionary<string, int>();
        foreach (var f in features) featureCounts[f.Category] = featureCounts.GetValueOrDefault(f.Category) + 1;
        var brushCounts = new Dictionary<string, int>();
        foreach (var c in kept) brushCounts[c.Category] = brushCounts.GetValueOrDefault(c.Category) + 1;

        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        meta[MetaKey] = new JsonObject
        {
            ["storage"] = "surfaceBrushes2",
            ["start"] = start,
            ["count"] = generated.Count,
            ["signature"] = Provenance.TailSignature(generated),
            ["surface"] = Surface,
            ["brushType"] = BrushType,
            ["brushValue"] = Json.F(1.0),
            ["method"] = "closed-edge-outside-2m-cart-centerline-native-brushes",
            ["closedSplineScale"] = Json.F(ClosedScale),
            ["closedSplineOutsideOffsetM"] = Json.F(ClosedOffset),
            ["closedSplineStepM"] = Json.F(ClosedStep),
            ["cartPathScale"] = Json.F(CartScale),
            ["cartPathStepM"] = Json.F(CartStep),
            ["redMaskClipping"] = false,
            ["featureSplineCounts"] = CountsObject(featureCounts),
            ["brushCounts"] = CountsObject(brushCounts),
            ["ambiguousOutsideNormalFallbacks"] = fallbacks,
        };
        log?.Report($"Heavy Rough: painted {generated.Count} native brush(es): closed surfaces scale 10.0 at centers 2.0 m outside the spline; " +
                    "cart paths scale 13.0 on centerline. No red-mask clipping.");
        log?.Report($"Heavy Rough spacing: closed edges {Py.Repr(ClosedStep)} m; cart paths {Py.Repr(CartStep)} m; outside-normal fallbacks={fallbacks}.");
        return new HeavyRoughReport(generated.Count, features.Count, featureCounts, brushCounts, fallbacks);
    }

    internal static JsonObject CountsObject(Dictionary<string, int> counts)
    {
        var o = new JsonObject();
        foreach (var (k, v) in counts) o[k] = v;
        return o;
    }
}
