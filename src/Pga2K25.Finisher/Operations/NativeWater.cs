using System.Globalization;
using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Operations;

public sealed record WaterBodyReport(IReadOnlyList<int> SourceSplines, string Kind, double Level, int Waypoints, double? MinimumDryClearance);

public sealed record NativeWaterReport(int Created, IReadOnlyList<(int Spline, string Error)> Errors, IReadOnlyList<WaterBodyReport> Bodies,
    int OpenStreamsIgnored, int StreamLikeClosedIgnored, int PondLakeCandidates);

/// <summary>
/// Pass 4, <c>generate_native_water_from_splines</c> (REV10 "closed pond / lake, strict low point").
/// For every closed, pond-shaped water spline it infers the highest safe level from the shoreline terrain,
/// covers the pond with a low-waypoint convex native water body (a 4-point rectangle first, adding one
/// edge at a time only where land inside the cover would sit below the water), merges neighbouring ponds
/// that can share one level, and writes the bodies to the generated-river object group.
/// Open streams and ribbon-like closed water are skipped by design.
/// </summary>
public static class NativeWater
{
    public const string GroupPath = "Assets/CourseGen/Detail/Walls/GeneratedRiverSetupPrefab";
    public const double DefaultClearance = 0.15, DefaultMergeSpread = 0.5;
    public const double OrientationSteps = 12, ExtraClearance = 0.15, HandleFraction = 0.25;
    public const double Pad = 0.2, DryMargin = 0.08, GridStepMax = 1.0, GridStepMin = 0.65, MaxMergeGap = 250.0;
    public const int MaxWaypoints = 10, SeparatorAngles = 144;
    public const double MinCompactness = 0.25, RelaxCompactness = 0.18, RelaxOrientedFill = 0.5, RelaxAspect = 4.5;

    sealed class Record
    {
        public required int Index { get; init; }
        public required JsonObject Spline { get; init; }
        public required List<Vec2> Polygon { get; init; }
        public required (double X0, double Z0, double X1, double Z1) BBox { get; init; }
        public required double Edge { get; init; }
        public required double BaseLevel { get; init; }
        public required int Samples { get; init; }
        public required double SourceArea { get; init; }
    }

    sealed record DryStats(int Dry, int Exposed, int Unknown, double Fraction, double? MinimumClearance, Vec2? WorstPoint, double? Step,
        string? FallbackReason = null)
    {
        public static DryStats Unsafe(double? step) => new(0, 1, 1, 1.0, null, null, step);
    }

    sealed record Cover(List<Record> Records, List<Vec2>? Polygon, double Rotation, double Level, DryStats Stats, double Area, int Waypoints, string Kind);

    public sealed record ShapeMetrics(double Area, double Perimeter, double Compactness, double OrientedFill, double Aspect, double Minor, double Major);

    // ---------------------------------------------------------------- shape tests

    /// <summary><c>_closed_water_shape_metrics</c>.</summary>
    public static ShapeMetrics? ShapeOf(JsonObject spline)
    {
        var poly = ClosedOutline(spline, 8);
        if (poly.Count < 3) return null;
        double area = Area(poly);
        if (area <= 1e-6) return null;
        double perimeter = 0;
        for (int k = 0; k < poly.Count; k++) perimeter += Vec2.Distance(poly[k], poly[(k + 1) % poly.Count]);
        if (perimeter <= 1e-6) return null;
        double compactness = 4.0 * Math.PI * area / (perimeter * perimeter);
        var best = BestAreaSupportPolygon(poly, 4, 0.0);
        if (best is null) return null;
        var (rectArea, rect, _) = best.Value;
        var edges = Enumerable.Range(0, 4).Select(k => Vec2.Distance(rect[(k + 1) % 4], rect[k])).ToList();
        double minor = Math.Max(1e-9, edges.Min()), major = edges.Max();
        return new ShapeMetrics(area, perimeter, compactness, area / Math.Max(rectArea, 1e-9), major / minor, minor, major);
    }

    /// <summary><c>_closed_water_is_lake_or_pond</c>: conservatively rejects long ribbon-like closed water.</summary>
    public static (bool PondLike, ShapeMetrics? Metrics) IsLakeOrPond(JsonObject spline)
    {
        var m = ShapeOf(spline);
        if (m is null) return (false, null);
        bool pond = m.Compactness >= MinCompactness ||
                    (m.Compactness >= RelaxCompactness && m.OrientedFill >= RelaxOrientedFill && m.Aspect <= RelaxAspect);
        return (pond, m);
    }

    // ---------------------------------------------------------------- polygon helpers

    /// <summary><c>_support_polygon</c>: intersection of <paramref name="sides"/> equally spaced support half-planes.</summary>
    static List<Vec2>? SupportPolygon(IReadOnlyList<Vec2> points, int sides, double pad, double rotation)
    {
        if (points.Count < 3 || sides < 4) return null;
        var normals = new Vec2[sides];
        var support = new double[sides];
        for (int i = 0; i < sides; i++)
        {
            double angle = rotation + 2.0 * Math.PI * i / sides;
            normals[i] = new Vec2(Math.Cos(angle), Math.Sin(angle));
            double h = double.MinValue;
            foreach (var p in points) h = Math.Max(h, p.X * normals[i].X + p.Z * normals[i].Z);
            support[i] = h + pad;
        }
        var poly = new List<Vec2>(sides);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            var n1 = normals[i];
            var n2 = normals[j];
            double det = n1.X * n2.Z - n1.Z * n2.X;
            if (Math.Abs(det) < 1e-10) return null;
            double x = (support[i] * n2.Z - n1.Z * support[j]) / det;
            double z = (n1.X * support[j] - support[i] * n2.X) / det;
            if (!double.IsFinite(x) || !double.IsFinite(z)) return null;
            poly.Add(new Vec2(x, z));
        }
        return poly;
    }

    /// <summary><c>_best_area_support_polygon</c>: smallest-area cover over 12 rotations of one side period.</summary>
    static (double Area, List<Vec2> Poly, double Rotation)? BestAreaSupportPolygon(IReadOnlyList<Vec2> points, int sides, double pad)
    {
        if (points.Count < 3) return null;
        double period = 2.0 * Math.PI / sides;
        (double, List<Vec2>, double)? best = null;
        for (int j = 0; j < OrientationSteps; j++)
        {
            double rotation = period * j / OrientationSteps;
            var poly = SupportPolygon(points, sides, pad, rotation);
            if (poly is null) continue;
            double area = Area(poly);
            if (best is null || area < best.Value.Item1) best = (area, poly, rotation);
        }
        return best;
    }

    /// <summary><c>_clip_polygon_halfplane</c>: Sutherland–Hodgman clip of a convex polygon to n·x ≤ support.</summary>
    static List<Vec2> ClipHalfPlane(List<Vec2> poly, Vec2 normal, double support)
    {
        if (poly.Count == 0) return [];
        var output = new List<Vec2>();
        var prev = poly[^1];
        double prevV = normal.X * prev.X + normal.Z * prev.Z - support;
        bool prevIn = prevV <= 1e-9;
        foreach (var cur in poly)
        {
            double curV = normal.X * cur.X + normal.Z * cur.Z - support;
            bool curIn = curV <= 1e-9;
            if (curIn != prevIn)
            {
                double denom = prevV - curV;
                if (Math.Abs(denom) > 1e-12)
                {
                    double t = prevV / denom;
                    output.Add(prev + (cur - prev) * t);
                }
            }
            if (curIn) output.Add(cur);
            prev = cur; prevV = curV; prevIn = curIn;
        }
        var clean = new List<Vec2>();
        foreach (var p in output)
            if (clean.Count == 0 || Vec2.Distance(p, clean[^1]) > 1e-6) clean.Add(p);
        if (clean.Count > 1 && Vec2.Distance(clean[0], clean[^1]) <= 1e-6) clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    /// <summary><c>_best_water_separator</c>: the support line that keeps all water and cuts off <paramref name="bad"/> by the widest margin.</summary>
    static (double Margin, Vec2 Normal, double Support)? BestSeparator(List<Vec2> sourcePoints, Vec2 bad, double pad)
    {
        (double, Vec2, double)? best = null;
        for (int k = 0; k < SeparatorAngles; k++)
        {
            double angle = 2.0 * Math.PI * k / SeparatorAngles;
            var n = new Vec2(Math.Cos(angle), Math.Sin(angle));
            double h = double.MinValue;
            foreach (var p in sourcePoints) h = Math.Max(h, p.X * n.X + p.Z * n.Z);
            h += pad;
            double margin = bad.X * n.X + bad.Z * n.Z - h;
            if (best is null || margin > best.Value.Item1) best = (margin, n, h);
        }
        if (best is null || best.Value.Item1 <= 1e-4) return null;
        return best;
    }

    static double BBoxGap((double X0, double Z0, double X1, double Z1) a, (double X0, double Z0, double X1, double Z1) b)
    {
        double dx = Math.Max(0.0, Math.Max(b.X0 - a.X1, a.X0 - b.X1));
        double dz = Math.Max(0.0, Math.Max(b.Z0 - a.Z1, a.Z0 - b.Z1));
        return Hypot(dx, dz);
    }

    static (double X0, double Z0, double X1, double Z1) GroupBBox(List<Record> records) =>
        (records.Min(r => r.BBox.X0), records.Min(r => r.BBox.Z0), records.Max(r => r.BBox.X1), records.Max(r => r.BBox.Z1));

    /// <summary><c>_water_source_mask</c>: is the point inside any source water outline?</summary>
    static bool InSource(List<Record> records, Vec2 p)
    {
        foreach (var r in records)
            if (p.X >= r.BBox.X0 && p.X <= r.BBox.X1 && p.Z >= r.BBox.Z0 && p.Z <= r.BBox.Z1 && Contains(r.Polygon, p)) return true;
        return false;
    }

    // ---------------------------------------------------------------- level and safety

    /// <summary>
    /// <c>_shoreline_level_for_closed</c>: samples terrain just outside the outline at two offsets every
    /// ~terrain-spacing metres; the water level is the 10th percentile of the bank minus the clearance.
    /// </summary>
    static (double Level, double Edge, int Samples) ShorelineLevel(JsonObject spline, MultiScaleTerrain sampler, double clearance)
    {
        var dense = DensePolyline(spline, 24, close: true);
        var arc = Arc.From(dense);
        if (arc is null || arc.Length <= 0) throw new InvalidOperationException("invalid water boundary");
        var polygon = dense.Count > 1 && Vec2.Distance(dense[0], dense[^1]) < 1e-6 ? dense.Take(dense.Count - 1).ToList() : dense;
        double step = Math.Max(1.0, sampler.Spacing);
        double[] offsets = [Math.Max(1.5, 1.25 * sampler.Spacing), Math.Max(3.0, 2.25 * sampler.Spacing)];
        var samples = new List<double>();
        for (double d = 0.0; d <= arc.Length; d += step)
        {
            var (x, z, tx, tz) = arc.At(d);
            double nx = -tz, nz = tx;
            foreach (double off in offsets)
            {
                Vec2[] candidates = [new(x + nx * off, z + nz * off), new(x - nx * off, z - nz * off)];
                var outside = candidates.Where(p => !Contains(polygon, p)).ToArray();
                foreach (var p in outside.Length > 0 ? outside : candidates)
                {
                    double v = sampler.Sample(p.X, p.Z);
                    if (double.IsFinite(v)) samples.Add(v);
                }
            }
        }
        if (samples.Count < 5)
            foreach (var p in polygon)
            {
                double v = sampler.Sample(p.X, p.Z);
                if (double.IsFinite(v)) samples.Add(v);
            }
        if (samples.Count < 3) throw new InvalidOperationException("not enough shoreline terrain samples");
        double edge = NumPy.Percentile(samples, 10.0);
        return (edge - clearance, edge, samples.Count);
    }

    /// <summary><c>_water_area_record</c>.</summary>
    static Record AreaRecord(int index, JsonObject spline, MultiScaleTerrain sampler, double clearance)
    {
        var (level, edge, n) = ShorelineLevel(spline, sampler, clearance);
        var polygon = ClosedOutline(spline, 24);
        if (polygon.Count < 3) throw new InvalidOperationException("invalid water boundary");
        return new Record
        {
            Index = index, Spline = spline, Polygon = polygon, BBox = BBox(polygon), Edge = edge,
            BaseLevel = level, Samples = n, SourceArea = Area(polygon),
        };
    }

    /// <summary>
    /// <c>_strict_water_dry_stats</c>: every dry point inside the cover (a 0.65–1 m grid plus the real
    /// fine terrain nodes) must sit above level + 8 cm; unknown terrain also counts as unsafe.
    /// </summary>
    static DryStats StrictDryStats(List<Record> records, List<Vec2> cover, MultiScaleTerrain sampler, double level)
    {
        if (cover.Count == 0) return DryStats.Unsafe(null);
        var (x0, z0, x1, z1) = BBox(cover);
        double step = Math.Min(GridStepMax, Math.Max(GridStepMin, sampler.Spacing / 4.0));
        var xs = NumPy.Arange(x0, x1 + 0.5 * step, step);
        var zs = NumPy.Arange(z0, z1 + 0.5 * step, step);
        if (xs.Length == 0 || zs.Length == 0) return DryStats.Unsafe(step);

        var grid = zs.AsParallel().AsOrdered()
            .SelectMany(z => xs.Where(x => Contains(cover, x, z)).Select(x => new Vec2(x, z)))
            .ToList();
        if (grid.Count == 0) return DryStats.Unsafe(step);

        var dry = grid.AsParallel().AsOrdered().Where(p => !InSource(records, p)).ToList();
        double maxScale = Math.Max(6.0, 1.5 * sampler.Spacing);
        foreach (var g in sampler.Groups)
        {
            if (g.Scale > maxScale) continue;
            foreach (var node in g.Nodes)
            {
                if (node.X < x0 || node.X > x1 || node.Z < z0 || node.Z > z1) continue;
                var p = new Vec2(node.X, node.Z);
                if (Contains(cover, p) && !InSource(records, p)) dry.Add(p);
            }
        }
        if (dry.Count == 0) return new DryStats(0, 0, 0, 0.0, null, null, step);

        var values = sampler.SampleMany(dry);
        double ceiling = level + DryMargin;
        int unknown = 0, exposed = 0, worst = -1;
        double minClearance = double.PositiveInfinity;
        for (int i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i])) { unknown++; continue; }
            if (values[i] <= ceiling) exposed++;
            double clearance = values[i] - level;
            if (clearance < minClearance) { minClearance = clearance; worst = i; }
        }
        return new DryStats(dry.Count, exposed, unknown, exposed / (double)Math.Max(1, dry.Count),
            worst >= 0 ? minClearance : null, worst >= 0 ? dry[worst] : null, step);
    }

    /// <summary>
    /// <c>_adaptive_strict_water_cover</c>: 4-point rectangle first; while a dry low point is exposed, add one
    /// edge (a separating half-plane) at a time, up to 10 waypoints. A single pond that cannot be covered safely
    /// falls back to a copy of its own spline when <paramref name="allowShapeFallback"/>.
    /// </summary>
    static Cover? AdaptiveCover(List<Record> records, MultiScaleTerrain sampler, bool allowShapeFallback)
    {
        if (records.Count == 0) return null;
        var sourcePoints = records.SelectMany(r => r.Polygon).ToList();
        var best = BestAreaSupportPolygon(sourcePoints, 4, Pad);
        if (best is null) return null;
        var (_, poly, rotation) = best.Value;
        double level = records.Min(r => r.BaseLevel) - ExtraClearance;

        while (poly.Count <= MaxWaypoints)
        {
            var stats = StrictDryStats(records, poly, sampler, level);
            if (stats.Exposed == 0 && stats.Unknown == 0)
                return new Cover(records.ToList(), poly, rotation, level, stats, Area(poly), poly.Count, "adaptive-plane");
            if (poly.Count >= MaxWaypoints || stats.WorstPoint is not Vec2 bad) break;
            var separator = BestSeparator(sourcePoints, bad, Pad);
            if (separator is null) break;
            var clipped = ClipHalfPlane(poly, separator.Value.Normal, separator.Value.Support);
            if (clipped.Count < 3 || clipped.Count > MaxWaypoints) break;
            if (Math.Abs(Area(poly) - Area(clipped)) < 1e-5) break;
            poly = clipped;
        }
        if (allowShapeFallback && records.Count == 1)
        {
            var rec = records[0];
            return new Cover(records.ToList(), null, 0.0, level,
                new DryStats(0, 0, 0, 0.0, null, null, null, "no-safe-low-waypoint-convex-cover"),
                rec.SourceArea, Json.Arr(rec.Spline, "waypoints").Count, "exact-source-shape");
        }
        return null;
    }

    /// <summary>
    /// <c>_rev10_group_water_records</c>: solve each pond alone, then repeatedly merge the closest pair whose
    /// levels are within <paramref name="maxLevelSpread"/> and which still has a safe shared cover.
    /// </summary>
    static List<Cover> GroupRecords(List<Record> records, MultiScaleTerrain sampler, double maxLevelSpread, IProgress<string>? log)
    {
        var groups = new List<Cover>();
        foreach (var rec in records)
            if (AdaptiveCover([rec], sampler, allowShapeFallback: true) is Cover solved) groups.Add(solved);

        while (groups.Count > 1)
        {
            var options = new List<(double Gap, int Waypoints, double Area, int I, int J, Cover Solved)>();
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var recs = groups[i].Records.Concat(groups[j].Records).ToList();
                    if (recs.Max(r => r.BaseLevel) - recs.Min(r => r.BaseLevel) > maxLevelSpread + 1e-9) continue;
                    double gap = BBoxGap(GroupBBox(groups[i].Records), GroupBBox(groups[j].Records));
                    if (gap > MaxMergeGap) continue;
                    if (AdaptiveCover(recs, sampler, allowShapeFallback: false) is not Cover solved) continue;
                    options.Add((gap, solved.Waypoints, solved.Area, i, j, solved));
                }
            if (options.Count == 0) break;
            var pick = options.OrderBy(o => o.Gap).ThenBy(o => o.Waypoints).ThenBy(o => o.Area).First();
            log?.Report($"Safe water merge: closed pond/lake splines {string.Join(",", pick.Solved.Records.Select(r => r.Index))} share one " +
                        $"{pick.Solved.Waypoints}-waypoint native body; zero sampled dry-terrain exposure.");
            var merged = groups.Where((_, k) => k != pick.I && k != pick.J).ToList();
            merged.Add(pick.Solved);
            groups = merged;
        }
        return groups.OrderBy(g => g.Records.Min(r => r.Index)).ToList();
    }

    // ---------------------------------------------------------------- object writing

    /// <summary><c>_water_polygon_waypoints</c>: straight-edged polygon (handles a quarter of the way to each neighbour).</summary>
    static JsonArray PolygonWaypoints(List<Vec2> points, double level)
    {
        int n = points.Count;
        if (n < 3) throw new InvalidOperationException("native water polygon needs at least three waypoints");
        var result = new JsonArray();
        for (int i = 0; i < n; i++)
        {
            var p = points[i];
            var prev = points[(i - 1 + n) % n];
            var next = points[(i + 1) % n];
            result.Add(new JsonObject
            {
                ["pointOne"] = Json.Xyz(p.X + HandleFraction * (prev.X - p.X), level, p.Z + HandleFraction * (prev.Z - p.Z)),
                ["pointTwo"] = Json.Xyz(p.X + HandleFraction * (next.X - p.X), level, p.Z + HandleFraction * (next.Z - p.Z)),
                ["waypoint"] = Json.Xyz(p.X, level, p.Z),
            });
        }
        return result;
    }

    /// <summary><c>_copy_water_waypoints_from_spline</c>: the source spline's own shape at the water level.</summary>
    static JsonArray CopyWaypoints(JsonObject spline, double level)
    {
        var result = new JsonArray();
        foreach (var item in Json.Arr(spline, "waypoints"))
        {
            var o = new JsonObject();
            foreach (var key in new[] { "pointOne", "pointTwo", "waypoint" })
            {
                var (x, z) = Json.XZ(item?[key]);
                o[key] = Json.Xyz(x, level, z);
            }
            result.Add(o);
        }
        return result;
    }

    /// <summary><c>_new_water_path</c> with <c>NATIVE_WATER_DEFAULTS</c>.</summary>
    static JsonObject NewWaterPath(JsonArray waypoints, double level) => new()
    {
        ["path"] = new JsonObject
        {
            ["surface"] = 0,
            ["secondarySurface"] = -1,
            ["secondaryWidth"] = Json.F(-1.0),
            ["opacity"] = Json.F(1.0),
            ["waypoints"] = waypoints,
            ["width"] = Json.F(3.0),
            ["hasCurves"] = true,
            ["state"] = 2,
            ["absoluteY"] = true,
        },
        ["height"] = Json.F(level),
        ["spacingRule"] = 0,
        ["flexibilityRule"] = 0,
        ["heightRule"] = 1,
        ["spacing"] = Json.F(0.25),
        ["disableHeightOffset"] = false,
        ["disableSurfaceTexture"] = false,
        ["heightOffset"] = Json.F(-2.0),
        ["edgeHeightOffset"] = Json.F(0.5),
        ["water"] = new JsonObject { ["flowDirection"] = Json.F(1.0), ["flowSpeed"] = Json.F(0.0) },
        ["isWater"] = true,
    };

    static IEnumerable<JsonObject> WaterGroups(JsonObject course) =>
        Json.Arr(course, "placedObjects4").OfType<JsonObject>().Where(g => Json.Str(g["Key"]?["path"]) == GroupPath);

    /// <summary><c>_find_or_create_water_group</c>.</summary>
    static JsonObject FindOrCreateGroup(JsonObject course)
    {
        var groups = Json.EnsureArray(course, "placedObjects4");
        foreach (var group in groups.OfType<JsonObject>())
        {
            if (Json.Str(group["Key"]?["path"]) != GroupPath) continue;
            var val = Json.EnsureObject(group, "Value");
            Json.EnsureArray(val, "items");
            Json.EnsureArray(val, "clusters");
            Json.EnsureArray(val, "splines");
            Json.EnsureArray(val, "objectPaths2");
            val["IsEmpty"] = false;
            return group;
        }
        var created = new JsonObject
        {
            ["Key"] = new JsonObject { ["path"] = GroupPath },
            ["Value"] = new JsonObject { ["items"] = new JsonArray(), ["clusters"] = new JsonArray(), ["splines"] = new JsonArray(), ["objectPaths2"] = new JsonArray(), ["IsEmpty"] = false },
        };
        groups.Add(created);
        return created;
    }

    /// <summary><c>existing_native_water_count</c>.</summary>
    public static int ExistingCount(JsonObject course) =>
        WaterGroups(course).Sum(g => Json.Arr(Json.Obj(g, "Value"), "objectPaths2").Count(p => Json.Truthy(p?["isWater"])));

    /// <summary><c>remove_generated_native_water</c>: drops every <c>isWater</c> path from the generated-river group.</summary>
    public static int RemoveGenerated(JsonObject course, IProgress<string>? log = null)
    {
        int removed = 0;
        foreach (var group in WaterGroups(course).ToList())
        {
            var paths = Json.EnsureArray(Json.EnsureObject(group, "Value"), "objectPaths2");
            for (int i = paths.Count - 1; i >= 0; i--)
                if (Json.Truthy(paths[i]?["isWater"])) { paths.RemoveAt(i); removed++; }
        }
        if (removed > 0) log?.Report($"Removed existing generated native water paths: {removed}.");
        return removed;
    }

    // ---------------------------------------------------------------- the pass

    public static NativeWaterReport Apply(JsonObject course, bool replaceExisting = true, double clearance = DefaultClearance,
        double planeGroupTolerance = DefaultMergeSpread, IProgress<string>? log = null)
    {
        var sampler = new MultiScaleTerrain(course);
        var (closedAreas, openLines) = FeatureClassifier.WaterSplines(course);

        var ponds = new List<(int Index, JsonObject Spline)>();
        int streamLike = 0;
        foreach (var (index, spline) in closedAreas)
        {
            var (pond, m) = IsLakeOrPond(spline);
            if (pond) { ponds.Add((index, spline)); continue; }
            streamLike++;
            log?.Report(m is not null
                ? $"Closed water spline {index}: skipped as stream/ribbon-like (compactness={Py.R(m.Compactness, 3)}, oriented fill={Py.R(m.OrientedFill, 3)}, aspect={Py.R(m.Aspect, 2)})."
                : $"Closed water spline {index}: skipped as invalid/stream-like closed water.");
        }
        if (openLines.Count > 0)
            log?.Report($"Ignored {openLines.Count} open waterway/stream spline(s) by design; Auto Water now targets closed ponds/lakes only.");

        if (replaceExisting) RemoveGenerated(course, log);
        var group = FindOrCreateGroup(course);
        var paths = Json.EnsureArray(Json.EnsureObject(group, "Value"), "objectPaths2");

        var records = new List<Record>();
        var errors = new List<(int, string)>();
        foreach (var (index, spline) in ponds)
        {
            try
            {
                var rec = AreaRecord(index, spline, sampler, clearance);
                records.Add(rec);
                log?.Report($"Pond/lake spline {index}: shoreline low-side={Py.R(rec.Edge, 3)} m; source plane ceiling={Py.R(rec.BaseLevel, 3)} m.");
            }
            catch (Exception ex)
            {
                errors.Add((index, ex.Message));
                log?.Report($"Pond/lake spline {index}: skipped: {ex.Message}");
            }
        }

        double mergeSpread = Math.Max(0.0, Math.Min(planeGroupTolerance, 2.0));
        var solved = GroupRecords(records, sampler, mergeSpread, log);
        var bodies = new List<WaterBodyReport>();
        var groupMeta = new JsonArray();
        int ordinal = 0;
        foreach (var body in solved)
        {
            ordinal++;
            var ids = body.Records.Select(r => r.Index).ToList();
            var waypoints = body.Kind == "exact-source-shape"
                ? CopyWaypoints(body.Records[0].Spline, body.Level)
                : PolygonWaypoints(body.Polygon!, body.Level);
            int count = waypoints.Count;
            paths.Add(NewWaterPath(waypoints, body.Level));
            bodies.Add(new WaterBodyReport(ids, body.Kind, body.Level, count, body.Stats.MinimumClearance));
            var stats = body.Stats;
            groupMeta.Add(new JsonObject
            {
                ["sourceSplines"] = Json.Ints(ids),
                ["waterLevel"] = Json.F(body.Level),
                ["kind"] = body.Kind,
                ["waypoints"] = count,
                ["drySamples"] = stats.Dry,
                ["dryExposed"] = stats.Exposed,
                ["dryUnknown"] = stats.Unknown,
                ["minimumDryClearance"] = stats.MinimumClearance is double mc ? Json.F(mc) : null,
                ["safetyGridStep"] = stats.Step is double st ? Json.F(st) : null,
            });
            if (body.Kind == "exact-source-shape")
                log?.Report($"Water body {ordinal}: spline {ids[0]} required exact source-shape fallback ({count} waypoints) because a lower-waypoint cover could expose water outside the enclosed spline.");
            else
                log?.Report($"Water body {ordinal}: source spline{(ids.Count > 1 ? "s " : " ")}{string.Join(",", ids)}; {count} waypoints; native level={Py.R(body.Level, 3)} m; " +
                            $"sampled dry exposure=0; minimum dry clearance={(stats.MinimumClearance is double c ? Py.R(c, 3) + " m" : "n/a")}.");
        }

        var meta = Provenance.Meta(course);
        Provenance.SetVersion(meta);
        meta["nativeWater"] = new JsonObject
        {
            ["created"] = bodies.Count,
            ["clearance"] = Json.F(clearance),
            ["mode"] = "rev10-closed-pond-lake-strict-low-point",
            ["closedWaterSplines"] = closedAreas.Count,
            ["pondLakeCandidates"] = ponds.Count,
            ["streamLikeClosedSkipped"] = streamLike,
            ["openWaterwaysSkipped"] = openLines.Count,
            ["mergeLevelSpread"] = Json.F(mergeSpread),
            ["drySafetyMargin"] = Json.F(DryMargin),
            ["maxAdaptiveWaypoints"] = MaxWaypoints,
            ["waterOrder"] = "highest-first",
            ["groups"] = groupMeta,
        };
        log?.Report($"Native water finishing complete: {bodies.Count} closed pond/lake body/bodies created; {openLines.Count} open stream(s) ignored; " +
                    $"{streamLike} ribbon-like closed water spline(s) ignored; {errors.Count} pond/lake spline error(s).");
        return new NativeWaterReport(bodies.Count, errors, bodies, openLines.Count, streamLike, ponds.Count);
    }
}
