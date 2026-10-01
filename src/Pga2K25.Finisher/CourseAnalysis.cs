using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Greens;
using Pga2K25.Finisher.Operations;
using Pga2K25.Finisher.Pins;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Tees;
using Pga2K25.Finisher.Terrain;

namespace Pga2K25.Finisher;

/// <summary><c>detect_tgctool_background_terrain</c> result: the coarse TGC Tool background lattice.</summary>
public sealed record BackgroundTerrain(double Scale, double Spacing, double SpacingX, double SpacingZ, int Count, double Score);

/// <summary>
/// <c>analyze_course</c>: what the Finisher sees in a course, run before and after finishing.
/// Fine-terrain fields are null when the course has no imported lattice (hand-built Designer courses);
/// the Python tool raises in that case, this port reports it in <see cref="FineTerrainError"/> instead.
/// </summary>
public sealed record CourseAnalysis
{
    public required GameVersion Version { get; init; }
    public required string Name { get; init; }
    public double? GreenSpeed { get; init; }
    public double? GreenFirmness { get; init; }
    public int? FineBrush { get; init; }
    public double? FineSpacing { get; init; }
    public int? FineStamps { get; init; }
    public string? FineTerrainError { get; init; }
    public int CartPaths { get; init; }
    public int WaterAreas { get; init; }
    public int WaterPondLakeCandidates { get; init; }
    public int WaterStreamLikeClosed { get; init; }
    public int Waterways { get; init; }
    public int NativeWaterExisting { get; init; }
    public int LegacyCartpathTail { get; init; }
    public IReadOnlyDictionary<string, int> SplineFeatures { get; init; } = new Dictionary<string, int>();
    public int ActiveTeeRegions { get; init; }
    public IReadOnlyList<int> TeeCounts { get; init; } = [];
    public IReadOnlyList<int> PinCounts { get; init; } = [];
    public int SmartPinsExisting { get; init; }
    public bool UltraSafeGreensExisting { get; init; }
    public string UltraSafeGreensSeverity { get; init; } = "";
    public int UltraSafeGreensChangedNodes { get; init; }
    public int WeatheringBrushesTotal { get; init; }
    public int TopographicWeatheringExisting { get; init; }
    public int TeeFlattenExisting { get; init; }
    public BackgroundTerrain? Background { get; init; }
    public int DetailHeavyRoughExisting { get; init; }
    public string DetailHeavyRoughStorage { get; init; } = "";

    public int TeeTotal => TeeCounts.Sum();
    public int TeeMin => TeeCounts.Count > 0 ? TeeCounts.Min() : 0;
    public int TeeMax => TeeCounts.Count > 0 ? TeeCounts.Max() : 0;
    public int PinTotal => PinCounts.Sum();
    public int PinMin => PinCounts.Count > 0 ? PinCounts.Min() : 0;
    public int PinMax => PinCounts.Count > 0 ? PinCounts.Max() : 0;

    public static CourseAnalysis Analyze(CourseFile file) => Analyze(file.Description);

    public static CourseAnalysis Analyze(JsonObject course)
    {
        var version = CourseModel.DetectVersion(course);
        double? Opt(string key) => course[key] is null ? null : Json.Num(course[key]);
        var basics = new CourseAnalysis
        {
            Version = version,
            Name = Json.Str(course["name"]),
            GreenSpeed = Opt("greenSpeed"),
            GreenFirmness = Opt("greenFirmness"),
        };
        if (version != GameVersion.PGA2K25) return basics;

        int? fineBrush = null, fineStamps = null;
        double? fineSpacing = null;
        string? fineError = null;
        try
        {
            var sampler = new FineTerrainSampler(course);
            (fineBrush, fineSpacing, fineStamps) = (sampler.BrushType, sampler.Spacing, sampler.Count);
        }
        catch (InvalidOperationException ex)
        {
            fineError = ex.Message;
        }

        var (areas, lines) = FeatureClassifier.WaterSplines(course);
        int ponds = areas.Count(a => NativeWater.IsLakeOrPond(a.Spline).PondLike);
        var holes = TeeGeometry.Holes(course).OfType<JsonObject>().ToList();
        var ultra = Provenance.Existing(course, UltraSafeGreens.MetaKey);
        var heavy = Provenance.Existing(course, HeavyRough.MetaKey);
        return basics with
        {
            FineBrush = fineBrush,
            FineSpacing = fineSpacing,
            FineStamps = fineStamps,
            FineTerrainError = fineError,
            CartPaths = FeatureClassifier.CartPaths(course).Count,
            WaterAreas = areas.Count,
            WaterPondLakeCandidates = ponds,
            WaterStreamLikeClosed = areas.Count - ponds,
            Waterways = lines.Count,
            NativeWaterExisting = NativeWater.ExistingCount(course),
            LegacyCartpathTail = CartPathLeveling.LegacyTail(course)?.Count ?? 0,
            SplineFeatures = FeatureClassifier.Counts(course),
            ActiveTeeRegions = TeeGeometry.ActiveRegions(course).Count,
            TeeCounts = holes.Select(h => Json.Arr(h, "tees").Count).ToList(),
            PinCounts = holes.Select(h => Json.Arr(h, "pins").Count).ToList(),
            SmartPinsExisting = Json.Int(Provenance.Existing(course, SmartPins.MetaKey)?["totalPins"], 0),
            UltraSafeGreensExisting = Json.Truthy(ultra?["enabled"]),
            UltraSafeGreensSeverity = Json.Str(ultra?["label"], ""),
            UltraSafeGreensChangedNodes = Json.Int(ultra?["changedNodes"], 0),
            WeatheringBrushesTotal = Json.Arr(course, "surfaceBrushes2").Count(b => Json.Int(b?["surfaceCategory"], -1) == TopographicWeathering.SurfaceCategory),
            TopographicWeatheringExisting = Json.Int(Provenance.Existing(course, TopographicWeathering.MetaKey)?["count"], 0),
            TeeFlattenExisting = Provenance.PriorTeeFlattenTail(course)?.Count ?? 0,
            Background = DetectBackgroundTerrain(course),
            DetailHeavyRoughExisting = Json.Int(heavy?["count"], 0),
            DetailHeavyRoughStorage = heavy is null ? "" : Json.Str(heavy["storage"], "legacy-splines"),
        };
    }

    /// <summary><c>_mode_coordinate_spacing</c>: the most common gap (to 1 cm) between distinct coordinates.</summary>
    static double? ModeCoordinateSpacing(IEnumerable<double> values)
    {
        var arr = values.Where(double.IsFinite).ToArray();
        if (arr.Length < 2) return null;
        var unique = arr.Select(v => Math.Round(v, 4, MidpointRounding.ToEven)).Distinct().Order().ToArray();
        if (unique.Length < 2) return null;
        var diffs = unique.Zip(unique.Skip(1), (a, b) => b - a).Where(d => d > 0.05).ToArray();
        if (diffs.Length < 1) return null;
        var counts = diffs.Select(d => Math.Round(d, 2, MidpointRounding.ToEven)).GroupBy(d => d).Select(g => (Value: g.Key, Count: g.Count()))
            .OrderBy(g => g.Value).ToList();
        return counts.Aggregate((best, g) => g.Count > best.Count ? g : best).Value;
    }

    /// <summary>
    /// <c>detect_tgctool_background_terrain</c>: the absolute Brush 10 family laid out as a regular lattice whose
    /// stamp scale is about 2.5× its spacing (analysis only).
    /// </summary>
    public static BackgroundTerrain? DetectBackgroundTerrain(JsonObject course)
    {
        var groups = new Dictionary<double, List<(double X, double Z)>>();
        var order = new List<double>();
        foreach (var op in Json.Arr(course, "height").OfType<JsonObject>())
        {
            if (Json.Int(op["tool"], 0) != 0 || Json.Int(op["type"], -1) != 10) continue;
            double scale = Py.Round(Json.Num(op["scale"]?["x"], 0.0), 3);
            var (x, z) = Json.XZ(op["position"]);
            if (scale <= 0.0 || !Json.Finite(x, z)) continue;
            if (!groups.TryGetValue(scale, out var list)) { groups[scale] = list = []; order.Add(scale); }
            list.Add((x, z));
        }
        var candidates = new List<BackgroundTerrain>();
        foreach (double scale in order)
        {
            var rows = groups[scale];
            if (rows.Count < 100) continue;
            var dx = ModeCoordinateSpacing(rows.Select(r => r.X));
            var dz = ModeCoordinateSpacing(rows.Select(r => r.Z));
            if (dx is not double sx || dz is not double sz || sx <= 0 || sz <= 0) continue;
            double rx = scale / sx, rz = scale / sz;
            double score = Math.Abs(rx - 2.5) + Math.Abs(rz - 2.5) + 0.5 * (Math.Abs(sx - sz) / Math.Max(sx, sz));
            if (Math.Abs(rx - 2.5) <= 0.35 && Math.Abs(rz - 2.5) <= 0.35)
                candidates.Add(new BackgroundTerrain(scale, 0.5 * (sx + sz), sx, sz, rows.Count, score));
        }
        return candidates.OrderBy(c => c.Score).ThenBy(c => -c.Scale).ThenBy(c => -c.Count).FirstOrDefault();
    }

    /// <summary>The Analyze panel text of the reference UI (<c>App.analyze</c>).</summary>
    public string Summary()
    {
        static string N(double? v) => v is double d ? Py.Repr(d) : "n/a";
        var sb = new StringBuilder();
        sb.Append($"Course: {Name}   |   version: {(int)Version}");
        if (Version != GameVersion.PGA2K25) return sb.ToString();
        string fine = FineBrush is int fb ? $"Brush {fb} / {N(FineSpacing)} m / {FineStamps} stamps" : $"none ({FineTerrainError})";
        sb.Append($"\nFine terrain: {fine}");
        int F(string k) => SplineFeatures.GetValueOrDefault(k);
        sb.Append($"\nFeature splines: tees {F(SplineCategory.TeeBox)}, greens {F(SplineCategory.Green)}, fairways {F(SplineCategory.Fairway)}, " +
                  $"rough {F(SplineCategory.Rough)}, bunkers {F(SplineCategory.Bunker)}, cart paths {F(SplineCategory.CartPath)}");
        sb.Append($"\nTee markers: {TeeTotal} total / {TeeMin}-{TeeMax} per hole   |   active tee areas: {ActiveTeeRegions}");
        sb.Append($"\nPin positions: {PinTotal} total / {PinMin}-{PinMax} per hole   |   Finisher Smart Pins: {SmartPinsExisting}");
        sb.Append($"\nUltra Speed-Safe Greens: {(UltraSafeGreensExisting ? UltraSafeGreensSeverity : "off")}   |   adjusted nodes: {UltraSafeGreensChangedNodes}");
        sb.Append($"\nNative green speed: {N(GreenSpeed)}   |   green firmness: {N(GreenFirmness)}");
        sb.Append($"\nTerrain weathering: {WeatheringBrushesTotal} total   |   Finisher topographic weathering: {TopographicWeatheringExisting}");
        sb.Append($"\nExisting Finisher tee stamps: {TeeFlattenExisting}");
        sb.Append($"\nTGC background terrain: brush {N(Background?.Scale)} m / lattice {N(Background?.Spacing)} m / {Background?.Count ?? 0} stamps" +
                  $"   |   Finisher Heavy Rough brushes: {DetailHeavyRoughExisting}");
        sb.Append($"\nCart paths: {CartPaths}   |   safely detectable legacy flatten tail: {LegacyCartpathTail} stamps");
        sb.Append($"\nClosed water: {WaterAreas} total / {WaterPondLakeCandidates} pond-lake candidates / {WaterStreamLikeClosed} stream-like skipped" +
                  $"   |   open streams: {Waterways}   |   existing native water: {NativeWaterExisting}");
        return sb.ToString();
    }

    public override string ToString() => Summary();
}
