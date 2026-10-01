using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Operations;

public sealed record CartPathReport(int Paths, int Stamps, double MaxDelta, int PreservedPaths,
    IReadOnlyList<(int Spline, double Length, int Stamps, double MaxDelta)> Details);

/// <summary>
/// Pass 5, <c>apply_cartpath_flatten</c>: samples the fine terrain along each cart path (centre and both
/// shoulders every 0.5 m), blends toward a smoothed profile (±20 cm), and appends absolute Brush 10 stamps
/// along the path. Paths that already carry manual post-import terrain strokes can be left alone.
/// </summary>
public static class CartPathLeveling
{
    public const int BrushType = 10;

    /// <summary><c>_make_pixel</c>: one absolute (tool 0) height stamp in the game's own layout.</summary>
    public static JsonObject Pixel(double x, double z, double value, double scale, int brushType = BrushType) => new()
    {
        ["tool"] = 0,
        ["position"] = Json.XGroundZ(x, z),
        ["rotation"] = Json.Xyz(0, 0, 0),
        ["_orientation"] = Json.F(0.0),
        ["scale"] = Json.Xyz(scale, 1.0, scale),
        ["type"] = brushType,
        ["value"] = Json.F(value),
        ["holeId"] = -1,
        ["radius"] = Json.F(0.0),
        ["orientation"] = Json.F(0.0),
    };

    /// <summary>
    /// <c>legacy_integrated_cartpath_tail</c>: detects the old V2 pass's block at the very end of <c>height[]</c>
    /// by its deterministic stamp scales (max(3.40, 1.40·width) or max(2.80, 1.15·width)) — never by proximity.
    /// </summary>
    public static (int Start, int Count, IReadOnlySet<double> Scales)? LegacyTail(JsonObject course)
    {
        var height = Json.Arr(course, "height");
        int length = Provenance.SourceHeightLength(course);
        if (length < 100) return null;
        var cartpaths = FeatureClassifier.CartPaths(course);
        if (cartpaths.Count == 0) return null;
        var expected = new HashSet<double>();
        foreach (var (_, spline) in cartpaths)
        {
            double width = Json.Num(spline["width"], 2.0);
            expected.Add(Py.Round(Math.Max(3.4, 1.4 * width), 3));
            expected.Add(Py.Round(Math.Max(2.8, 1.15 * width), 3));
        }
        int start = length;
        while (start > 0)
        {
            var op = height[start - 1] as JsonObject;
            double scale = Json.Num(op?["scale"]?["x"], 0);
            if (op is not null && Json.Int(op["tool"], 0) == 0 && Json.Int(op["type"], -1) == 10 && expected.Any(t => Math.Abs(scale - t) <= 0.015))
                start--;
            else break;
        }
        int count = length - start;
        if (count < 100) return null;
        var scales = new HashSet<double>();
        for (int i = start; i < length; i++) scales.Add(Py.Round(Json.Num(height[i]?["scale"]?["x"], 0), 3));
        return (start, count, scales);
    }

    /// <summary><c>remove_legacy_cartpath_tail</c>: deletes from the legacy block to the end of <c>height[]</c>.</summary>
    public static int RemoveLegacyTail(JsonObject course, IProgress<string>? log = null)
    {
        if (LegacyTail(course) is not var (start, count, _)) return 0;
        var height = Json.Arr(course, "height");
        for (int i = height.Count - 1; i >= start; i--) height.RemoveAt(i);
        log?.Report($"Removed legacy integrated cartpath flatten tail: {count} Brush 10 stamps.");
        return count;
    }

    /// <summary>
    /// <c>_existing_postfine_edit_counts</c>: small absolute strokes added after the fine lattice (types 9/10/15/54/72,
    /// scale ≤ 8 m) within 1.5 m of a path; a path with at least max(5, length/20) of them counts as hand-edited.
    /// </summary>
    static Dictionary<int, (int Count, int Threshold, double Length)> ExistingEditCounts(JsonObject course, FineTerrainSampler sampler, int? excludedTailStart)
    {
        var result = new Dictionary<int, (int, int, double)>();
        if (sampler.Count == 0) return result;
        int fineEnd = sampler.Group.Nodes.Max(n => n.Index);
        var height = Json.Arr(course, "height");
        int limit = excludedTailStart ?? height.Count;
        var edits = new List<Vec2>();
        for (int idx = 0; idx < height.Count; idx++)
        {
            if (idx <= fineEnd || idx >= limit) continue;
            if (height[idx] is not JsonObject op || Json.Int(op["tool"], 0) != 0) continue;
            int type = Json.Int(op["type"], -1);
            double scale = Json.Num(op["scale"]?["x"], 0);
            if (type is not (9 or 10 or 15 or 54 or 72) || scale <= 0.0 || scale > 8.0) continue;
            var (x, z) = Json.XZ(op["position"]);
            if (!Json.Finite(x, z)) continue;
            edits.Add(new Vec2(x, z));
        }
        if (edits.Count == 0) return result;
        foreach (var (index, spline) in FeatureClassifier.CartPaths(course))
        {
            var arc = Arc.From(DensePolyline(spline, 12, close: false));
            if (arc is null) continue;
            var tree = new KdTree(arc.Points.Select(p => (p.X, p.Z)).ToList());
            int count = edits.Count(e => tree.Nearest(e.X, e.Z).Distance <= 1.5);
            int threshold = Math.Max(5, (int)Math.Ceiling(arc.Length / 20.0));
            if (count >= threshold) result[index] = (count, threshold, arc.Length);
        }
        return result;
    }

    /// <summary>Fills NaNs by linear interpolation over sample index (<c>np.interp(bad, good, arr[good])</c>).</summary>
    static void InterpolateGaps(double[] values)
    {
        var good = Enumerable.Range(0, values.Length).Where(i => double.IsFinite(values[i])).Select(i => (double)i).ToList();
        var goodValues = good.Select(i => values[(int)i]).ToList();
        for (int i = 0; i < values.Length; i++)
            if (!double.IsFinite(values[i])) values[i] = NumPy.Interp(i, good, goodValues);
    }

    public static CartPathReport Apply(JsonObject course, bool replaceLegacy = true, bool preserveExistingEdits = true, IProgress<string>? log = null)
    {
        var priorTail = LegacyTail(course);
        int? tailStart = priorTail?.Start;
        var sampler = new FineTerrainSampler(course);
        var existing = preserveExistingEdits ? ExistingEditCounts(course, sampler, tailStart) : [];
        if (replaceLegacy) RemoveLegacyTail(course, log);

        var cartpaths = FeatureClassifier.CartPaths(course);
        var stamps = new List<JsonObject>();
        var details = new List<(int, double, int, double)>();
        double globalMax = 0.0;
        log?.Report($"Finisher terrain source: Brush {sampler.BrushType}, dominant fine spacing/scale {Py.R(sampler.Spacing, 3)} m, {sampler.Count} stamps.");

        foreach (var (index, spline) in cartpaths)
        {
            if (existing.TryGetValue(index, out var info))
            {
                log?.Report($"Cartpath {index}: preserved; detected {info.Count} existing post-fine terrain strokes near this path.");
                continue;
            }
            double width = Json.Num(spline["width"], 2.0);
            var arc = Arc.From(DensePolyline(spline, 24, close: false));
            if (arc is null || arc.Length < 2.0) continue;
            double length = arc.Length;
            double brushScale = Math.Max(3.4, 1.4 * width);
            double sideOffset = Math.Max(1.5, 0.75 * width);
            const double profileStep = 0.5;
            var distances = NumPy.Arange(0.0, length + 1e-6, profileStep).ToList();
            if (distances.Count == 0 || distances[^1] < length) distances.Add(length);

            int n = distances.Count;
            var center = new double[n];
            var left = new double[n];
            var right = new double[n];
            for (int j = 0; j < n; j++)
            {
                var (x, z, tx, tz) = arc.At(distances[j]);
                double nx = -tz, nz = tx;
                center[j] = sampler.Sample(x, z);
                left[j] = sampler.Sample(x + nx * sideOffset, z + nz * sideOffset);
                right[j] = sampler.Sample(x - nx * sideOffset, z - nz * sideOffset);
            }
            int valid = center.Count(double.IsFinite);
            if (valid < Math.Max(4, (int)(0.5 * n)))
            {
                log?.Report($"Cartpath spline {index}: skipped; insufficient fine terrain coverage.");
                continue;
            }
            if (valid < n) InterpolateGaps(center);
            foreach (var arr in new[] { left, right })
            {
                if (arr.Count(double.IsFinite) >= 2) InterpolateGaps(arr);
                else Array.Copy(center, arr, n);
            }

            double sigma = Math.Max(0.5, 1.5 / profileStep);
            var smoothed = NumPy.GaussianFilter1D(center, sigma);
            var target = new double[n];
            const double endpointBlend = 4.0;
            for (int j = 0; j < n; j++)
            {
                target[j] = center[j] + Math.Clamp(smoothed[j] - center[j], -0.2, 0.2);
                double edge = Math.Min(distances[j], length - distances[j]);
                double blend = Math.Min(1.0, Math.Max(0.0, edge / endpointBlend));
                blend = blend * blend * (3.0 - 2.0 * blend);
                target[j] = center[j] * (1.0 - blend) + target[j] * blend;
            }
            var shoulder = Enumerable.Range(0, n).Select(j => Math.Max(Math.Abs(left[j] - center[j]), Math.Abs(right[j] - center[j]))).ToArray();

            double spacing = Math.Min(1.7, Math.Max(1.3, 0.45 * brushScale));
            int count = 0;
            double pathMax = 0.0;
            for (double d = 0.5 * spacing; d <= length - 0.5 * spacing + 1e-9; d += spacing)
            {
                var (x, z, _, _) = arc.At(d);
                double value = NumPy.Interp(d, distances, target);
                double source = sampler.Sample(x, z);
                if (!double.IsFinite(source)) source = NumPy.Interp(d, distances, center);
                value = Math.Min(Math.Max(value, source - 0.2), source + 0.2);
                double localShoulder = NumPy.Interp(d, distances, shoulder);
                double delta = value - source;
                pathMax = Math.Max(pathMax, Math.Abs(delta));
                globalMax = Math.Max(globalMax, Math.Abs(delta));
                double scale = localShoulder > 0.75 ? Math.Max(2.8, 1.15 * width) : brushScale;
                stamps.Add(Pixel(x, z, value, scale, BrushType));
                count++;
            }
            details.Add((index, length, count, pathMax));
            log?.Report($"Cartpath {index}: {Py.R(length, 1)} m, stamps={count}, max correction={Py.R(pathMax, 3)} m.");
        }

        var height = Json.EnsureArray(course, "height");
        foreach (var s in stamps) height.Add(s);
        var meta = Provenance.Meta(course);
        Provenance.SetVersion(meta);
        meta["cartpathFlatten"] = new JsonObject
        {
            ["stamps"] = stamps.Count,
            ["maxCorrection"] = Json.F(Py.Round(globalMax, 6)),
            ["preservedPaths"] = existing.Count,
        };
        log?.Report($"Cartpath finishing complete: {stamps.Count} Brush 10 stamps; global max correction={Py.R(globalMax, 3)} m; preserved already-edited paths={existing.Count}.");
        return new CartPathReport(details.Count, stamps.Count, globalMax, existing.Count, details);
    }
}
