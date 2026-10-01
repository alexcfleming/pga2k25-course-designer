using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Tees;

public sealed record TeeLevelDetail(IReadOnlyList<int> Holes, int SurfaceIndex, string Mode, double Minor, double Major, double Area,
    double Scale, double SourceRange, int Taps, Vec2 Anchor, int TeesOnSurface);

public sealed record TeeLevelingReport(int Regions, int Holes, int Stamps, IReadOnlyList<TeeLevelDetail> Details);

/// <summary>
/// Pass 7, <c>smart_flatten_tees</c>: neutral Designer "level" taps (tool 1, Brush 9, local state 0.0 ft) on
/// every pad that carries a tee marker — the whole pad when it serves one tee, a local tap around each marker
/// when several tees share a pad. Appended as a tracked tail of <c>height[]</c> so re-runs replace it.
/// </summary>
public static class TeeLeveling
{
    public const int Tool = 1, BrushType = 9;
    public const double TapValue = 0.0, MinScale = 1.75, MaxScale = 7.0, SecondTapRange = 0.3, Rev2BadValue = 0.15;

    /// <summary><c>remove_prior_tee_flatten</c>.</summary>
    public static int RemovePrior(JsonObject course, IProgress<string>? log = null)
    {
        if (Provenance.PriorTeeFlattenTail(course) is not var (start, count)) return 0;
        var height = Json.Arr(course, "height");
        for (int i = height.Count - 1; i >= start; i--) height.RemoveAt(i);
        log?.Report($"Removed prior Course Finisher smart-tee terrain tail: {count} stamps.");
        Provenance.Remove(course, "teeFlatten");
        return count;
    }

    /// <summary><c>_remove_untracked_rev2_tee_tap_tail</c>: the v0.09 REV2 bug left Brush 9 taps at 0.15 m without metadata.</summary>
    public static int RemoveUntrackedRev2Tail(JsonObject course, IProgress<string>? log = null)
    {
        var height = Json.Arr(course, "height");
        int count = 0;
        while (height.Count > 0)
        {
            var op = height[^1] as JsonObject;
            bool matches = op is not null && Json.Int(op["tool"], -1) == Tool && Json.Int(op["type"], -1) == BrushType &&
                           Math.Abs(Json.Num(op["value"], 999.0) - Rev2BadValue) <= 1e-9;
            if (!matches) break;
            height.RemoveAt(height.Count - 1);
            count++;
        }
        if (count > 0) log?.Report($"Removed legacy v0.09 REV2 tee-leveling tail: {count} Brush-9 taps at local state 0.15 m.");
        return count;
    }

    /// <summary><c>_make_tee_level_tap</c>: tool 1 (relative), so it levels to the local state instead of setting a height.</summary>
    public static JsonObject Tap(double x, double z, double scale, double value = TapValue) => new()
    {
        ["tool"] = Tool,
        ["position"] = Json.XGroundZ(x, z),
        ["rotation"] = Json.Xyz(0, 0, 0),
        ["scale"] = Json.Xyz(scale, 1.0, scale),
        ["type"] = BrushType,
        ["value"] = Json.F(value),
        ["holeId"] = -1,
    };

    /// <summary><c>_tee_marker_regions</c>: pads under actual tee markers; route-start matches only if there are none.</summary>
    static List<TeeRegion> MarkerRegions(JsonObject course)
    {
        var regions = TeeGeometry.ActiveRegions(course);
        var actual = regions.Where(r => r.AnchorSource == "tee").ToList();
        return actual.Count > 0 ? actual : regions;
    }

    public static TeeLevelingReport Apply(JsonObject course, IProgress<string>? log = null)
    {
        RemovePrior(course, log);
        RemoveUntrackedRev2Tail(course, log);
        var regions = MarkerRegions(course);
        if (regions.Count == 0)
        {
            log?.Report("Smart tee leveling: no active tee surfaces were detected.");
            return new TeeLevelingReport(0, 0, 0, []);
        }

        var sampler = new FineTerrainSampler(course);
        var height = Json.EnsureArray(course, "height");
        int startIndex = height.Count;
        var grouped = new List<(int Index, List<TeeRegion> Items)>();
        foreach (var r in regions)
        {
            var g = grouped.FirstOrDefault(e => e.Index == r.Surface.Index);
            if (g.Items is null) grouped.Add((r.Surface.Index, [r]));
            else g.Items.Add(r);
        }

        var generated = new List<JsonObject>();
        var details = new List<TeeLevelDetail>();
        log?.Report("=== SMART TEE BOX LEVELING (BRUSH 9 / LOCAL STATE 0.0 FT) ===");
        log?.Report($"Detected {regions.Count} actual tee marker(s) across {grouped.Count} physical tee surface(s).");

        foreach (var (surfaceIndex, items) in grouped)
        {
            var poly = items[0].Surface.Poly;
            var (minor, major) = PcaDimensions(poly);
            double area = items[0].Surface.Area;
            string desc = $"spline {surfaceIndex}";

            var unique = new List<TeeRegion>();
            foreach (var item in items.OrderBy(i => i.Hole).ThenBy(i => i.Anchor.X).ThenBy(i => i.Anchor.Z))
                if (!unique.Any(o => Vec2.Distance(item.Anchor, o.Anchor) < 0.6)) unique.Add(item);
            var anchors = unique.Select(u => u.Anchor).ToList();

            bool dedicatedTeeLike = area <= 1400.0 && major <= 90.0;
            var centers = new List<(double X, double Z, double Scale, List<TeeRegion> Members)>();
            string mode = "localized-shared-surface";
            if (unique.Count == 1 && dedicatedTeeLike)
            {
                foreach (var tap in WholeSurfaceCoverPoints(poly, anchors[0]))
                    centers.Add((tap.X, tap.Z, tap.Scale, unique.ToList()));
                mode = "whole-tee-surface";
            }
            else
            {
                for (int idx = 0; idx < unique.Count; idx++)
                    centers.Add((anchors[idx].X, anchors[idx].Z, LocalScale(poly, anchors, idx), [unique[idx]]));
                if (unique.Count == 1) mode = "localized-guarded-large-surface";
            }

            foreach (var (x, z, scale, members) in centers)
            {
                var localValues = mode == "whole-tee-surface"
                    ? SampleRegionGrid(poly, sampler, Math.Max(0.75, Math.Min(1.5, Math.Max(minor, 1.0) / 6.0)))
                    : SampleLocal(poly, sampler, new Vec2(x, z), scale);
                double localRange = TeeCreation.RobustRange(localValues);
                int tapCount = localValues.Count >= 2 && localRange >= SecondTapRange ? 2 : 1;
                var holes = members.Select(m => m.Hole).Distinct().Order().ToList();
                for (int t = 0; t < tapCount; t++) generated.Add(Tap(x, z, scale, 0.0));
                details.Add(new TeeLevelDetail(holes, surfaceIndex, mode, minor, major, area, scale, localRange, tapCount, new Vec2(x, z), unique.Count));
                log?.Report($"Tee {desc} / hole(s) {string.Join(",", holes)}: {mode}, brush {BrushType}, scale={Py.R(scale, 2)} m, " +
                            $"local state=0.0 ft, source spread={Py.R(localRange, 3)} m, tap(s)={tapCount}.");
            }
        }

        foreach (var g in generated) height.Add(g);
        int holeCount = details.SelectMany(d => d.Holes).Distinct().Count();
        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        meta["teeFlatten"] = new JsonObject
        {
            ["startIndex"] = startIndex,
            ["stamps"] = generated.Count,
            ["regions"] = details.Count,
            ["holes"] = holeCount,
            ["tool"] = Tool,
            ["brushType"] = BrushType,
            ["tapValue"] = Json.F(0.0),
            ["method"] = "whole-surface-unless-shared-brush9-leveling",
            ["details"] = Json.Array(details.Select(d => (JsonNode?)new JsonObject
            {
                ["holes"] = Json.Ints(d.Holes),
                ["surfaceKind"] = "spline",
                ["surfaceIndex"] = d.SurfaceIndex,
                ["mode"] = d.Mode,
                ["minor"] = Json.F(d.Minor),
                ["major"] = Json.F(d.Major),
                ["area"] = Json.F(d.Area),
                ["scale"] = Json.F(d.Scale),
                ["sourceRange"] = Json.F(d.SourceRange),
                ["taps"] = d.Taps,
                ["anchor"] = Json.Floats([d.Anchor.X, d.Anchor.Z]),
                ["teesOnSurface"] = d.TeesOnSurface,
            })),
        };
        log?.Report($"Smart tee leveling complete: {details.Count} leveling region(s), {generated.Count} Brush-9 neutral leveling tap(s).");
        return new TeeLevelingReport(details.Count, holeCount, generated.Count, details);
    }

    /// <summary><c>_sample_region_grid</c>: terrain values on a grid over the whole pad.</summary>
    static List<double> SampleRegionGrid(List<Vec2> poly, FineTerrainSampler sampler, double step)
    {
        var values = new List<double>();
        if (poly.Count < 3) return values;
        var (x0, z0, x1, z1) = BBox(poly);
        foreach (double z in NumPy.Arange(z0, z1 + 1e-6, step))
            foreach (double x in NumPy.Arange(x0, x1 + 1e-6, step))
            {
                if (!Contains(poly, x, z)) continue;
                double v = sampler.Sample(x, z);
                if (double.IsFinite(v)) values.Add(v);
            }
        return values;
    }

    /// <summary><c>_sample_local_tee</c>: terrain values on the pad within <paramref name="radius"/> of the anchor, plus the anchor.</summary>
    static List<double> SampleLocal(List<Vec2> poly, FineTerrainSampler sampler, Vec2 anchor, double radius)
    {
        double step = Math.Max(0.75, Math.Min(1.4, radius / 3.0));
        var values = new List<double>();
        foreach (double z in NumPy.Arange(anchor.Z - radius, anchor.Z + radius + 1e-6, step))
            foreach (double x in NumPy.Arange(anchor.X - radius, anchor.X + radius + 1e-6, step))
            {
                if ((x - anchor.X) * (x - anchor.X) + (z - anchor.Z) * (z - anchor.Z) > radius * radius) continue;
                if (!Contains(poly, x, z)) continue;
                double v = sampler.Sample(x, z);
                if (double.IsFinite(v)) values.Add(v);
            }
        double c = sampler.Sample(anchor.X, anchor.Z);
        if (double.IsFinite(c)) values.Add(c);
        return values;
    }

    /// <summary><c>_tee_whole_surface_cover_points</c>: overlapping taps along the pad's long axis, sized to its width.</summary>
    static List<(double X, double Z, double Scale)> WholeSurfaceCoverPoints(List<Vec2> poly, Vec2 fallbackAnchor)
    {
        var center = CentroidPoint(poly) ?? fallbackAnchor;
        double cx = center.X, cz = center.Z;
        if (poly.Count < 3) return [(cx, cz, MinScale)];
        var (majorAxis, minorAxis, mean) = LinAlg.PrincipalAxes(poly.Select(p => (p.X, p.Z)).ToList());
        double a0 = double.MaxValue, a1 = double.MinValue, b0 = double.MaxValue, b1 = double.MinValue;
        foreach (var p in poly)
        {
            double dx = p.X - mean.X, dz = p.Z - mean.Z;
            double a = dx * majorAxis.X + dz * majorAxis.Z, b = dx * minorAxis.X + dz * minorAxis.Z;
            a0 = Math.Min(a0, a); a1 = Math.Max(a1, a);
            b0 = Math.Min(b0, b); b1 = Math.Max(b1, b);
        }
        double span = Math.Max(0.1, a1 - a0), minor = Math.Max(0.1, b1 - b0);
        double scale = Math.Max(MinScale, Math.Min(MaxScale, 1.2 * minor));
        double effectiveRadius = Math.Max(0.8, 0.7 * scale);
        double step = Math.Max(1.0, 1.45 * effectiveRadius);
        if (span <= 2.0 * effectiveRadius) return [(cx, cz, scale)];
        int count = Math.Max(2, (int)Math.Ceiling(span / step));
        var taps = NumPy.Linspace(a0 + 0.5 * span / count, a1 - 0.5 * span / count, count)
            .Select(pos => (mean.X + pos * majorAxis.X, mean.Z + pos * majorAxis.Z, scale)).ToList();
        if (taps.All(t => Hypot(t.Item1 - cx, t.Item2 - cz) > 0.45 * effectiveRadius)) taps.Add((cx, cz, scale));
        return taps;
    }

    /// <summary><c>_tee_local_scale</c>: 62 % of the pad width, no larger than 72 % of the gap to the nearest other tee.</summary>
    static double LocalScale(List<Vec2> poly, List<Vec2> anchors, int index)
    {
        var (minor, _) = PcaDimensions(poly);
        double scale = Math.Max(MinScale, Math.Min(MaxScale, 0.62 * Math.Max(minor, 1.0)));
        if (anchors.Count > 1)
        {
            double nearest = anchors.Where((_, j) => j != index).Min(o => Vec2.Distance(anchors[index], o));
            scale = Math.Min(scale, Math.Max(MinScale, 0.72 * nearest));
        }
        return scale;
    }
}
