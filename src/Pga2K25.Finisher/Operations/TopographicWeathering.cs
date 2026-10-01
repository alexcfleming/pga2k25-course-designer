using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Operations;

public sealed record WeatheringFeature(string Role, int SplineIndex, double AreaM2, int Candidates, int Brushes);

public sealed record WeatheringReport(int Count, int Green, int Fairway, int Rough, IReadOnlyDictionary<int, int> Types, IReadOnlyList<WeatheringFeature> Features);

/// <summary>
/// Pass 10, <c>apply_topographic_weathering</c>: native Terrain Weathering brushes (surface category 18,
/// types 10/48/49/50) on exposed convex terrain — ridges and mound tops found by comparing each grid point
/// with a ring around it — inside greens, fairways and rough. Placement and styling use a SHA-256 based
/// pseudo-random function, so the same course always gets the same brushes.
/// </summary>
public static class TopographicWeathering
{
    public const string MetaKey = "topographicWeathering";
    public const int SurfaceCategory = 18;
    public static readonly int[] BrushTypes = [10, 48, 49, 50];

    sealed record RoleParams(double Step, double Ring, double EdgeMin, double Threshold, double TargetArea, int MaxBrushes, int MinBrushes,
        double ExposureHi, double BaseSeparation, double ValueMin, double ValueMax, (int Type, double Upper)[] TypeWeights,
        Dictionary<int, (double Lo, double Hi)> ScaleRanges);

    static readonly Dictionary<string, RoleParams> Roles = new()
    {
        [SplineCategory.Green] = new(0.75, 4.0, 1.5, 0.012, 72.0, 11, 2, 0.08, 2.75, 0.16, 0.32,
            [(10, 0.42), (48, 0.64), (49, 0.86), (50, 1.0)],
            new() { [10] = (2.5, 5.0), [48] = (7.0, 13.5), [49] = (8.0, 15.5), [50] = (11.0, 20.0) }),
        [SplineCategory.Fairway] = new(1.5, 6.0, 2.0, 0.018, 700.0, 16, 2, 0.12, 7.0, 0.035, 0.145,
            [(10, 0.1), (48, 0.4), (49, 0.68), (50, 1.0)],
            new() { [10] = (6.0, 10.0), [48] = (14.0, 23.5), [49] = (18.0, 27.0), [50] = (26.0, 42.0) }),
        [SplineCategory.Rough] = new(2.25, 7.0, 1.5, 0.022, 1500.0, 10, 1, 0.135, 8.0, 0.025, 0.125,
            [(10, 0.08), (48, 0.33), (49, 0.58), (50, 1.0)],
            new() { [10] = (7.0, 11.0), [48] = (15.0, 25.0), [49] = (19.0, 29.0), [50] = (27.0, 43.0) }),
    };

    sealed class Feature
    {
        public required int Index { get; init; }
        public required string Role { get; init; }
        public required List<Vec2> Poly { get; init; }
        public required double Area { get; init; }
        public required (double XMin, double XMax, double ZMin, double ZMax) BBox { get; init; }
        public required KdTree EdgeTree { get; init; }
        public List<List<Vec2>> ExcludePolys { get; set; } = [];
    }

    sealed record Candidate(double X, double Z, double Edge, double Tpi, double PlaneResidual, double Exposure, double Score);

    sealed record Styled(Candidate Item, string Role, int SplineIndex, int Type, double Scale, double Value, double RotationY, double RankScore);

    static bool BBoxOverlap((double XMin, double XMax, double ZMin, double ZMax) a, (double XMin, double XMax, double ZMin, double ZMax) b, double pad) =>
        !(a.XMax + pad < b.XMin || b.XMax + pad < a.XMin || a.ZMax + pad < b.ZMin || b.ZMax + pad < a.ZMin);

    /// <summary><c>_weather_feature_geometries</c>: paintable greens/fairways/rough, each excluding the features that sit on top of it.</summary>
    static List<Feature> FeatureGeometries(JsonObject course)
    {
        var roles = FeatureClassifier.Classify(course);
        var splines = Json.Arr(course, "surfaceSplines2");
        var records = new List<Feature>();
        foreach (var (index, role) in roles)
        {
            if (role is not (SplineCategory.Green or SplineCategory.Fairway or SplineCategory.Rough or SplineCategory.Bunker or SplineCategory.TeeBox)) continue;
            if (index < 0 || index >= splines.Count || splines[index] is not JsonObject spline || !IsClosed(spline)) continue;
            var poly = DensePolyline(spline, 12, close: true);
            if (poly.Count < 3) continue;
            records.Add(new Feature
            {
                Index = index, Role = role, Poly = poly, Area = Math.Abs(Area(poly)),
                BBox = (poly.Min(p => p.X), poly.Max(p => p.X), poly.Min(p => p.Z), poly.Max(p => p.Z)),
                EdgeTree = new KdTree(poly.Select(p => (p.X, p.Z)).ToList()),
            });
        }
        var paint = records.Where(r => r.Role is SplineCategory.Green or SplineCategory.Fairway or SplineCategory.Rough).ToList();
        foreach (var feature in paint)
        {
            string[] blocked = feature.Role switch
            {
                SplineCategory.Rough => [SplineCategory.Green, SplineCategory.TeeBox, SplineCategory.Bunker, SplineCategory.Fairway],
                SplineCategory.Fairway => [SplineCategory.Green, SplineCategory.TeeBox, SplineCategory.Bunker],
                _ => [],
            };
            if (blocked.Length > 0)
                feature.ExcludePolys = records.Where(o => o != feature && blocked.Contains(o.Role) && BBoxOverlap(feature.BBox, o.BBox, 2.0)).Select(o => o.Poly).ToList();
        }
        return paint;
    }

    /// <summary><c>_weather_feature_candidates</c>: grid points whose height stands above the 16-point ring around them (TPI).</summary>
    static List<Candidate> FeatureCandidates(Feature feature, MultiScaleTerrain sampler)
    {
        var p = Roles[feature.Role];
        var (xmin, xmax, zmin, zmax) = feature.BBox;
        var xs = NumPy.Arange(Math.Floor(xmin / p.Step) * p.Step + 0.5 * p.Step, xmax + 1e-9, p.Step);
        var zs = NumPy.Arange(Math.Floor(zmin / p.Step) * p.Step + 0.5 * p.Step, zmax + 1e-9, p.Step);
        if (xs.Length == 0 || zs.Length == 0) return [];
        bool Paintable(Vec2 q) => Contains(feature.Poly, q) && !feature.ExcludePolys.Any(b => Contains(b, q));
        var points = zs.AsParallel().AsOrdered().SelectMany(z => xs.Select(x => new Vec2(x, z)).Where(Paintable)).ToList();
        if (points.Count == 0) return [];

        var edge = new List<double>();
        var kept = new List<Vec2>();
        foreach (var q in points)
        {
            double d = feature.EdgeTree.Nearest(q.X, q.Z).Distance;
            if (d >= p.EdgeMin) { kept.Add(q); edge.Add(d); }
        }
        points = kept;
        if (points.Count == 0) return [];

        var offsets = Enumerable.Range(0, 16).Select(k => k * (2.0 * Math.PI / 16.0)).Select(a => new Vec2(p.Ring * Math.Cos(a), p.Ring * Math.Sin(a))).ToArray();
        int n = points.Count;
        var probes = new List<Vec2>(n * 17);
        probes.AddRange(points);
        foreach (var q in points)
            foreach (var o in offsets) probes.Add(q + o);
        var ringInside = new bool[n * 16];
        Parallel.For(0, n * 16, k => ringInside[k] = Paintable(probes[n + k]));
        var sampled = sampler.SampleMany(probes);

        var center = new double[n];
        var tpi = new double[n];
        var baseValid = new bool[n];
        for (int i = 0; i < n; i++)
        {
            center[i] = sampled[i];
            int count = 0;
            double sum = 0;
            for (int k = 0; k < 16; k++)
            {
                double y = sampled[n + i * 16 + k];
                if (!ringInside[i * 16 + k] || !double.IsFinite(y)) continue;
                count++;
                sum += y;
            }
            double ringMean = count > 0 ? sum / Math.Max(count, 1) : double.NaN;
            tpi[i] = center[i] - ringMean;
            baseValid[i] = double.IsFinite(center[i]) && count >= 5 && double.IsFinite(tpi[i]);
        }

        var planeResidual = new double[n];
        bool green = feature.Role == SplineCategory.Green;
        if (green && baseValid.Count(b => b) >= 6)
        {
            var ids = Enumerable.Range(0, n).Where(i => baseValid[i]).ToArray();
            var px = ids.Select(i => points[i].X).ToArray();
            var pz = ids.Select(i => points[i].Z).ToArray();
            var py = ids.Select(i => center[i]).ToArray();
            if (LinAlg.FitPlane(px, pz, py, ReadOnlySpan<double>.Empty, out double a, out double b, out double c))
                for (int i = 0; i < n; i++) planeResidual[i] = center[i] - (a * points[i].X + b * points[i].Z + c);
        }

        var result = new List<Candidate>();
        for (int i = 0; i < n; i++)
        {
            double exposure, score;
            bool valid = baseValid[i];
            if (green)
            {
                exposure = 0.7 * NanMax0(tpi[i]) + 0.3 * NanMax0(planeResidual[i]);
                valid &= tpi[i] >= -0.005;
                score = exposure + 0.08 * NanMax0(planeResidual[i]);
            }
            else
            {
                exposure = NanMax0(tpi[i]);
                score = exposure;
            }
            if (valid && exposure >= p.Threshold)
                result.Add(new Candidate(points[i].X, points[i].Z, edge[i], tpi[i], planeResidual[i], exposure, score));
        }
        return result;
    }

    /// <summary><c>np.maximum(0.0, x)</c> (NaN propagates).</summary>
    static double NanMax0(double x) => double.IsNaN(x) ? double.NaN : Math.Max(0.0, x);

    /// <summary><c>_weather_rand01</c>: SHA-256 of "role|spline|x.xxx|z.xxx|salt", first 8 bytes / (2⁶⁴ − 1).</summary>
    internal static double Rand01(string role, int splineIndex, double x, double z, string salt)
    {
        string key = $"{role}|{splineIndex}|{Py.Fixed(x, 3)}|{Py.Fixed(z, 3)}|{salt}";
        var raw = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BinaryPrimitives.ReadUInt64BigEndian(raw) / 18446744073709551615.0;
    }

    static int PickType(string role, double r)
    {
        foreach (var (type, upper) in Roles[role].TypeWeights)
            if (r <= upper) return type;
        return 50;
    }

    /// <summary><c>_weather_style_item</c>: brush type, size, opacity and rotation from exposure plus seeded jitter.</summary>
    static Styled Style(Candidate item, string role, int splineIndex, double exposureLo, double exposureHi)
    {
        var p = Roles[role];
        double R(string salt) => Rand01(role, splineIndex, item.X, item.Z, salt);
        double t = Math.Max(0.0, Math.Min(1.0, (item.Exposure - exposureLo) / Math.Max(1e-9, exposureHi - exposureLo)));
        int type = PickType(role, R("type"));
        var (lo, hi) = p.ScaleRanges[type];
        double scale = lo + (0.35 * t + 0.65 * R("scale")) * (hi - lo);
        double value = p.ValueMin + t * (p.ValueMax - p.ValueMin);
        if (role == SplineCategory.Green) value *= 0.82 + 0.23 * R("opacity");
        else
        {
            value *= 0.55 + 0.45 * R("opacity");
            if (type == 50) value *= 0.78 + 0.18 * R("opacity50");
        }
        value = Math.Max(p.ValueMin, Math.Min(p.ValueMax, value));
        double rotation = type == 50 ? 180.0 * R("rotation") : 0.0;
        double rank = item.Score * (0.88 + 0.24 * R("rank"));
        return new Styled(item, role, splineIndex, type, scale, value, rotation, rank);
    }

    /// <summary><c>_weather_select_feature</c>: best-ranked brushes, spaced apart, about one per target area.</summary>
    static List<Styled> SelectFeature(Feature feature, List<Candidate> candidates)
    {
        if (candidates.Count == 0) return [];
        var p = Roles[feature.Role];
        double area = Math.Max(0.0, feature.Area);
        int target = Math.Max(p.MinBrushes, Math.Min(p.MaxBrushes, (int)Math.Round(area / p.TargetArea, MidpointRounding.ToEven)));
        var styled = candidates.Select(c => Style(c, feature.Role, feature.Index, p.Threshold, p.ExposureHi)).ToList();
        var selected = new List<Styled>();
        foreach (var item in styled.OrderByDescending(s => s.RankScore))
        {
            bool tooClose = selected.Any(prev => Hypot(item.Item.X - prev.Item.X, item.Item.Z - prev.Item.Z) < Math.Max(p.BaseSeparation, 0.3 * (item.Scale + prev.Scale)));
            if (tooClose) continue;
            selected.Add(item);
            if (selected.Count >= target) break;
        }
        return selected;
    }

    /// <summary><c>_topographic_weather_brush</c>.</summary>
    static JsonObject Brush(Styled s) => new()
    {
        ["surfaceCategory"] = SurfaceCategory,
        ["position"] = Json.XGroundZ(s.Item.X, s.Item.Z),
        ["rotation"] = Json.Xyz(0.0, s.RotationY, 0.0),
        ["scale"] = Json.Xyz(s.Scale, 1.0, s.Scale),
        ["type"] = s.Type,
        ["value"] = Json.F(s.Value),
        ["holeId"] = -1,
    };

    /// <summary><c>_topographic_weather_key</c>: "cat|type|x|z|sx|sz|ry|value" at 3 dp, for removal by content.</summary>
    public static string Key(JsonNode? brush)
    {
        var pos = brush?["position"];
        var scale = brush?["scale"];
        var rot = brush?["rotation"];
        return string.Join("|",
            Json.Int(brush?["surfaceCategory"], -1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Json.Int(brush?["type"], -1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Py.Fixed(Json.Num(pos?["x"], 0.0), 3), Py.Fixed(Json.Num(pos?["z"], 0.0), 3),
            Py.Fixed(Json.Num(scale?["x"], 0.0), 3), Py.Fixed(Json.Num(scale?["z"], 0.0), 3),
            Py.Fixed(Json.Num(rot?["y"], 0.0), 3), Py.Fixed(Json.Num(brush?["value"], 0.0), 3));
    }

    /// <summary><c>remove_prior_topographic_weathering</c>: removes exactly the recorded brushes (a multiset of keys), wherever they now sit.</summary>
    public static int RemovePrior(JsonObject course, IProgress<string>? log = null)
    {
        var keys = Json.Arr(Provenance.Existing(course, MetaKey), "brushKeys").Select(k => Json.Str(k)).ToList();
        if (keys.Count == 0) return 0;
        var counts = new Dictionary<string, int>();
        foreach (var k in keys) counts[k] = counts.GetValueOrDefault(k) + 1;
        var brushes = Json.Arr(course, "surfaceBrushes2");
        int removed = 0;
        var kept = new JsonArray();
        foreach (var brush in brushes.ToList())
        {
            brushes.Remove(brush);
            string key = Key(brush);
            if (counts.GetValueOrDefault(key) > 0) { counts[key]--; removed++; continue; }
            kept.Add(brush);
        }
        course["surfaceBrushes2"] = kept;
        if (removed > 0) log?.Report($"Removed {removed} prior Finisher topographic weathering brush(es).");
        return removed;
    }

    public static WeatheringReport Apply(JsonObject course, IProgress<string>? log = null)
    {
        RemovePrior(course, log);
        var sampler = new MultiScaleTerrain(course);
        var features = FeatureGeometries(course);
        var chosen = new List<Styled>();
        var featureDetails = new List<WeatheringFeature>();
        foreach (var feature in features)
        {
            var candidates = FeatureCandidates(feature, sampler);
            var selected = SelectFeature(feature, candidates);
            chosen.AddRange(selected);
            featureDetails.Add(new WeatheringFeature(feature.Role, feature.Index, Py.Round(feature.Area, 3), candidates.Count, selected.Count));
        }

        var existing = new List<(double X, double Z, double Scale)>();
        foreach (var brush in Json.Arr(course, "surfaceBrushes2").OfType<JsonObject>())
        {
            if (Json.Int(brush["surfaceCategory"], -1) != SurfaceCategory) continue;
            double x = Json.Num(brush["position"]?["x"], 0.0), z = Json.Num(brush["position"]?["z"], 0.0), s = Json.Num(brush["scale"]?["x"], 0.0);
            if (!double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(s)) continue;
            existing.Add((x, z, Math.Max(0.0, s)));
        }

        static int RoleOrder(string role) => role switch { SplineCategory.Green => 0, SplineCategory.Fairway => 1, SplineCategory.Rough => 2, _ => 9 };
        var deduped = new List<Styled>();
        foreach (var item in chosen.OrderBy(q => RoleOrder(q.Role)).ThenBy(q => -q.RankScore))
        {
            if (existing.Any(e => Hypot(item.Item.X - e.X, item.Item.Z - e.Z) < Math.Max(2.0, 0.2 * (item.Scale + e.Scale)))) continue;
            if (deduped.Any(k => Hypot(item.Item.X - k.Item.X, item.Item.Z - k.Item.Z) < Math.Max(2.0, 0.2 * (item.Scale + k.Scale)))) continue;
            deduped.Add(item);
        }

        var generated = deduped.Select(Brush).ToList();
        var brushes = Json.EnsureArray(course, "surfaceBrushes2");
        foreach (var b in generated) brushes.Add(b);
        int green = deduped.Count(d => d.Role == SplineCategory.Green), fairway = deduped.Count(d => d.Role == SplineCategory.Fairway),
            rough = deduped.Count(d => d.Role == SplineCategory.Rough);
        var typeCounts = BrushTypes.ToDictionary(t => t, t => deduped.Count(d => d.Type == t));

        var meta = Provenance.Meta(course);
        var typeCountsJson = new JsonObject();
        foreach (var (t, c) in typeCounts) typeCountsJson[t.ToString(System.Globalization.CultureInfo.InvariantCulture)] = c;
        meta[MetaKey] = new JsonObject
        {
            ["count"] = generated.Count,
            ["greenBrushes"] = green,
            ["fairwayBrushes"] = fairway,
            ["roughBrushes"] = rough,
            ["surfaceCategory"] = SurfaceCategory,
            ["brushTypes"] = Json.Ints(BrushTypes),
            ["brushTypeCounts"] = typeCountsJson,
            ["greenValueMax"] = Json.F(Roles[SplineCategory.Green].ValueMax),
            ["fairwayValueMax"] = Json.F(Roles[SplineCategory.Fairway].ValueMax),
            ["roughValueMax"] = Json.F(Roles[SplineCategory.Rough].ValueMax),
            ["method"] = "organic-local-topographic-position-mixed-native-weathering",
            ["reference"] = "Palmer Hole 8 manual types 48/49/50 + legacy type 10",
            ["brushKeys"] = Json.Array(generated.Select(g => (JsonNode?)Json.S(Key(g)))),
            ["features"] = Json.Array(featureDetails.Select(f => (JsonNode?)new JsonObject
            {
                ["role"] = f.Role,
                ["splineIndex"] = f.SplineIndex,
                ["areaM2"] = Json.F(f.AreaM2),
                ["candidates"] = f.Candidates,
                ["brushes"] = f.Brushes,
            })),
        };
        log?.Report($"Topographic weathering: painted {generated.Count} mixed native brush(es) ({green} green / {fairway} fairway / {rough} rough), types 10/48/49/50.");
        return new WeatheringReport(generated.Count, green, fairway, rough, typeCounts, featureDetails);
    }
}
