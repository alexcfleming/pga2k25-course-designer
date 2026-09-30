using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pga2K25.CourseFormat;

/// <summary>Planar world point. 2K25 uses X/Z as the ground plane, metres.</summary>
public readonly record struct Vec2(double X, double Z)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Z * s);
    public double Length => Math.Sqrt(X * X + Z * Z);
    public Vec2 Normalized() { double l = Length; return l > 1e-12 ? new(X / l, Z / l) : new(0, 0); }
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
}

public enum GameVersion { Unknown = -1, PGA2K21 = 19, PGA2K23 = 23, PGA2K25 = 25 }

/// <param name="Route">Waypoints: [0] route start (usually, not always, the back tee), [^1] green anchor.</param>
/// <param name="Tees">Tee markers ordered front to back.</param>
/// <param name="Pins">Absolute pin positions (file stores offsets from the green anchor).</param>
public sealed record Hole(int Number, int Par, IReadOnlyList<Vec2> Route, IReadOnlyList<Vec2> Tees, IReadOnlyList<Vec2> Pins)
{
    /// <summary>Green centre / pin anchor: the last route waypoint.</summary>
    public Vec2 GreenAnchor => Route[^1];

    /// <summary>Route polyline length in metres (what CourseMetadata.length sums).</summary>
    public double RouteLength => Route.Zip(Route.Skip(1), Vec2.Distance).Sum();

    /// <summary>
    /// Playing length for tee set <paramref name="set"/> as CourseMetadata.teeSetInfos stores it:
    /// tee → Route[1] → … → green. Holes with fewer tees reuse their last tee.
    /// </summary>
    public double TeeSetLength(int set)
    {
        if (Tees.Count == 0 || Route.Count < 2) return RouteLength;
        var tee = Tees[Math.Min(set, Tees.Count - 1)];
        return Vec2.Distance(tee, Route[1]) + Route.Skip(1).Zip(Route.Skip(2), Vec2.Distance).Sum();
    }
}

public sealed record SplinePoint(Vec2 In, Vec2 Anchor, Vec2 Out, double Width);

public sealed record SurfaceSpline(int Index, int Surface, bool Closed, double Width, IReadOnlyList<SplinePoint> Points);

/// <param name="Tool">0 = absolute height, 1 = relative (raise/lower/level).</param>
public sealed record HeightStamp(int Index, int Tool, int Type, Vec2 Position, double Scale, double RotationY, double Value);

/// <param name="Scale">Brush diameter in metres (scale.x).</param>
/// <param name="Value">Paint opacity 0–1.</param>
public sealed record SurfaceBrush(int Index, int Category, int Type, Vec2 Position, double Scale, double RotationY, double Value);

/// <summary>
/// Surface ids used by surfaceSplines2[].surface and surfaceBrushes2[].surfaceCategory.
/// 0/1/2/3/4/7/10 are confirmed by the reference Course Finisher. 8, 15 and 18 were identified by
/// matching Cassimbo's layers against an in-game overhead capture (see docs/scorecard-maps.md).
/// </summary>
public static class Surfaces
{
    public const int Bunker = 0, Green = 1, Fairway = 2, Rough = 3, HeavyRough = 4, CartPath = 7, Water = 10;

    /// <summary>Dirt/service road (wide open ribbons).</summary>
    public const int Road = 8;

    /// <summary>Waste area (grey sand/gravel scrub).</summary>
    public const int Waste = 15;

    /// <summary>Inner fairway cut drawn inside Fairway shapes (splines). As a brush category the
    /// reference tool uses 18 for terrain weathering, so 18 brushes are not treated as fairway.</summary>
    public const int FairwayCut = 18;
}

/// <summary>Read-only typed view over a CourseDescription JSON document.</summary>
public sealed class CourseModel
{
    public required string Name { get; init; }
    public required GameVersion Version { get; init; }
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<Hole> Holes { get; init; }
    public required IReadOnlyList<SurfaceSpline> SurfaceSplines { get; init; }
    public required IReadOnlyList<HeightStamp> HeightStamps { get; init; }
    public required IReadOnlyList<SurfaceBrush> SurfaceBrushes { get; init; }
    public bool FlattenGreens { get; init; }
    public bool BlurHeight { get; init; }
    public double GreenSpeed { get; init; }

    public static GameVersion DetectVersion(JsonObject desc)
    {
        if (desc.ContainsKey("useV43AltSurfaceSplineScale") && desc.ContainsKey("holes3")) return GameVersion.PGA2K25;
        if (desc.ContainsKey("useV30Rough") && desc.ContainsKey("holes2")) return GameVersion.PGA2K23;
        if (desc.ContainsKey("useV19DetailPlacement") && desc.ContainsKey("holes")) return GameVersion.PGA2K21;
        return GameVersion.Unknown;
    }

    public static CourseModel From(JsonObject desc)
    {
        var version = DetectVersion(desc);
        if (version != GameVersion.PGA2K25)
            throw new NotSupportedException($"Only native PGA 2K25 courses are supported (detected {version}).");

        return new CourseModel
        {
            Name = desc["name"]?.GetValue<string>() ?? "",
            Version = version,
            SchemaVersion = (int)Num(desc["version"]),
            Holes = ReadHoles(desc["holes3"] as JsonArray),
            SurfaceSplines = ReadSplines(desc["surfaceSplines2"] as JsonArray),
            HeightStamps = ReadStamps(desc["height"] as JsonArray),
            SurfaceBrushes = ReadBrushes(desc["surfaceBrushes2"] as JsonArray),
            FlattenGreens = desc["flattenGreens"]?.GetValue<bool>() ?? false,
            BlurHeight = desc["blurHeight"]?.GetValue<bool>() ?? false,
            GreenSpeed = Num(desc["greenSpeed"], -1),
        };
    }

    static List<Hole> ReadHoles(JsonArray? holes)
    {
        var result = new List<Hole>();
        if (holes is null) return result;
        for (int i = 0; i < holes.Count; i++)
        {
            var h = holes[i]!.AsObject();
            var route = Array(h["waypoints"]).Select(ReadVec).ToList();
            if (route.Count == 0) continue;
            var anchor = route[^1];
            var tees = Array(h["tees"]).Select(t => ReadVec(t!["position"])).ToList();
            var pins = Array(h["pins"]).Select(p => anchor + ReadVec(p!["position"])).ToList();
            result.Add(new Hole(i + 1, (int)Num(h["par"], 0), route, tees, pins));
        }
        return result;
    }

    static List<SurfaceSpline> ReadSplines(JsonArray? splines)
    {
        var result = new List<SurfaceSpline>();
        if (splines is null) return result;
        for (int i = 0; i < splines.Count; i++)
        {
            var s = splines[i]!.AsObject();
            var points = Array(s["waypoints"]).Select(w => new SplinePoint(
                ReadVec(w!["pointOne"]), ReadVec(w["waypoint"]), ReadVec(w["pointTwo"]), Num(w["width"]))).ToList();
            bool closed = s.TryGetPropertyValue("state", out var state) && state is not null
                ? Num(state) != 0
                : (s["isClosed"]?.GetValue<bool>() ?? false) || (s["isFilled"]?.GetValue<bool>() ?? false);
            result.Add(new SurfaceSpline(i, (int)Num(s["surface"], -1), closed, Num(s["width"]), points));
        }
        return result;
    }

    static List<HeightStamp> ReadStamps(JsonArray? height)
    {
        var result = new List<HeightStamp>();
        if (height is null) return result;
        for (int i = 0; i < height.Count; i++)
        {
            var s = height[i]!.AsObject();
            result.Add(new HeightStamp(i, (int)Num(s["tool"]), (int)Num(s["type"]), ReadVec(s["position"]),
                Num(s["scale"]?["x"]), Num(s["rotation"]?["y"]), Num(s["value"])));
        }
        return result;
    }

    static List<SurfaceBrush> ReadBrushes(JsonArray? brushes)
    {
        var result = new List<SurfaceBrush>();
        if (brushes is null) return result;
        for (int i = 0; i < brushes.Count; i++)
        {
            var b = brushes[i]!.AsObject();
            result.Add(new SurfaceBrush(i, (int)Num(b["surfaceCategory"], -1), (int)Num(b["type"]), ReadVec(b["position"]),
                Num(b["scale"]?["x"]), Num(b["rotation"]?["y"]), Num(b["value"], 1)));
        }
        return result;
    }

    static IEnumerable<JsonNode?> Array(JsonNode? node) => node as JsonArray ?? [];

    static Vec2 ReadVec(JsonNode? node) => new(Num(node?["x"]), Num(node?["z"]));

    /// <summary>Reads a JSON number; tolerates the game's string sentinels such as "-Infinity".</summary>
    public static double Num(JsonNode? node, double fallback = 0)
    {
        if (node is not JsonValue value) return fallback;
        if (value.TryGetValue(out double d)) return d;
        if (value.TryGetValue(out JsonElement e))
        {
            if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
            if (e.ValueKind == JsonValueKind.String &&
                double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        if (value.TryGetValue(out string? s) &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            return p;
        return fallback;
    }
}
