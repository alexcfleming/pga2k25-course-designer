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
/// One <c>distanceMarkers[]</c> point: where the game set a marker, with <paramref name="Height"/> its own ground
/// height there (m). The only in-game elevations a course file holds, so they check the terrain replay.
/// </summary>
public sealed record DistanceMarker(Vec2 Position, double Height);

/// <summary>One cloud layer of <c>weather3</c>: the game's cloud type and its sliders (0–1).</summary>
public sealed record CloudLayer(string Type, double Cloudiness, double Scale, double Definition);

/// <summary>
/// Lighting and weather as the designer sets them (<c>timeOfDay</c>, <c>sunOrientation</c>, <c>sunInclination</c>,
/// <c>weather3</c>): what a course's QA record notes so the published version can be matched later.
/// </summary>
/// <param name="TimeOfDay">Fraction of the day, 0–1 (0.5 = noon).</param>
public sealed record Atmosphere(double TimeOfDay, double SunOrientation, double SunInclination, double CloudHeight,
    CloudLayer? Cloud1, CloudLayer? Cloud2, double FogCeiling, double FogDensity, double Haze, bool Rain)
{
    /// <summary>Time of day as a 24-hour clock reading.</summary>
    public string Clock => TimeSpan.FromHours(Math.Clamp(TimeOfDay, 0, 1) * 24).ToString(@"hh\:mm");
}

/// <summary>
/// One <c>water[]</c> entry: a flat water surface at <paramref name="Level"/> (world m) over a rectangle of
/// ±<paramref name="HalfX"/> × ±<paramref name="HalfZ"/> (scale.x, scale.z) around the centre, turned by
/// <paramref name="RotationY"/> like the brushes. Large ones are the designer's water planes; small ones are
/// dabs of a painted stream (see GreensBook <c>WaterTracer</c> for how the two are told apart).
/// </summary>
public sealed record WaterPlane(int Index, Vec2 Position, double HalfX, double HalfZ, double RotationY, double Level)
{
    /// <summary>Offset of a world point in the plane's own axes (same convention as the brush stamps).</summary>
    public Vec2 Local(Vec2 p)
    {
        double th = RotationY * Math.PI / 180, cos = Math.Cos(th), sin = Math.Sin(th);
        double dx = p.X - Position.X, dz = p.Z - Position.Z;
        return new Vec2(dx * cos - dz * sin, dx * sin + dz * cos);
    }

    public bool Contains(Vec2 p) => Local(p) is var l && Math.Abs(l.X) <= HalfX && Math.Abs(l.Z) <= HalfZ;

    /// <summary>Radius of the circle that holds the whole rectangle.</summary>
    public double Reach => Math.Sqrt(HalfX * HalfX + HalfZ * HalfZ);
}

/// <summary>One placed asset instance (tree, bush, rock, prop) from placedObjects4.</summary>
/// <param name="AssetPath">Prefab path, e.g. "Assets/Foliage/SpeedTree/Bushes/...".</param>
/// <param name="Height">Explicit elevation (position.y); null when the object snaps to terrain ("-Infinity").</param>
public sealed record PlacedObject(string AssetPath, Vec2 Position, double Scale, double? Height = null);

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

    /// <summary>
    /// Open surface-10 splines narrower than this (m) are walking paths, not streams: on Luke Muscat's course
    /// twenty 1.7–3.6 m ones are the gravel footpaths in game (one crosses the hole 1 creek on a bridge).
    /// </summary>
    public const double MaxWalkingPathWidth = 4.0;

    /// <summary>True for an open spline drawn as a walking path rather than as water.</summary>
    public static bool IsWalkingPath(SurfaceSpline s) => s.Surface == Water && !s.Closed && s.Width < MaxWalkingPathWidth;
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
    public required IReadOnlyList<PlacedObject> PlacedObjects { get; init; }

    /// <summary>
    /// Painted water and water planes (<c>water[]</c>): brushes whose <see cref="SurfaceBrush.Value"/> is the
    /// water level (m). See <see cref="Water"/> for the full footprints.
    /// </summary>
    public IReadOnlyList<SurfaceBrush> WaterBrushes { get; init; } = [];

    /// <summary>The same <c>water[]</c> entries with both footprint axes (<see cref="WaterBrushes"/> keeps scale.x only).</summary>
    public IReadOnlyList<WaterPlane> Water { get; init; } = [];

    /// <summary>Distance markers with the game's ground height under each (empty in most files).</summary>
    public IReadOnlyList<DistanceMarker> DistanceMarkers { get; init; } = [];
    public bool FlattenGreens { get; init; }
    public bool BlurHeight { get; init; }
    public double GreenSpeed { get; init; }

    /// <summary><c>greenFringeRadius</c>: −1 is the game's default; 0.5 shows no visible fringe (Cassimbo, Sagan-Khushun).</summary>
    public double GreenFringeRadius { get; init; } = -1;

    public Atmosphere? Atmosphere { get; init; }

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
            PlacedObjects = ReadObjects(desc["placedObjects4"] as JsonArray),
            WaterBrushes = ReadBrushes(desc["water"] as JsonArray),
            Water = ReadWater(desc["water"] as JsonArray),
            DistanceMarkers = Array(desc["distanceMarkers"]).Select(m => m?["position"])
                .Where(p => p is not null && double.IsFinite(Num(p["y"], double.NaN)))
                .Select(p => new DistanceMarker(ReadVec(p), Num(p!["y"]))).ToList(),
            FlattenGreens = desc["flattenGreens"]?.GetValue<bool>() ?? false,
            BlurHeight = desc["blurHeight"]?.GetValue<bool>() ?? false,
            GreenSpeed = Num(desc["greenSpeed"], -1),
            GreenFringeRadius = Num(desc["greenFringeRadius"], -1),
            Atmosphere = ReadAtmosphere(desc),
        };
    }

    static Atmosphere? ReadAtmosphere(JsonObject desc)
    {
        if (desc["timeOfDay"] is null) return null;
        var w = desc["weather3"];
        static CloudLayer? Cloud(JsonNode? c) => c?["typeName"]?.GetValue<string>() is { Length: > 0 } type
            ? new CloudLayer(type, Num(c["cloudiness"]), Num(c["scale"]), Num(c["definition"])) : null;
        return new Atmosphere(Num(desc["timeOfDay"]), Num(desc["sunOrientation"]), Num(desc["sunInclination"]),
            Num(w?["cloudHeight"]), Cloud(w?["cloudLayer1"]), Cloud(w?["cloudLayer2"]),
            Num(w?["fogCeiling"]), Num(w?["fogDensity"]), Num(w?["haze"]), w?["rain"]?.GetValue<bool>() ?? false);
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

    static List<WaterPlane> ReadWater(JsonArray? water)
    {
        var result = new List<WaterPlane>();
        if (water is null) return result;
        for (int i = 0; i < water.Count; i++)
        {
            var w = water[i]!.AsObject();
            double sx = Num(w["scale"]?["x"]);
            result.Add(new WaterPlane(i, ReadVec(w["position"]), sx, Num(w["scale"]?["z"], sx),
                Num(w["rotation"]?["y"]), Num(w["value"], double.NaN)));
        }
        return result;
    }

    static List<PlacedObject> ReadObjects(JsonArray? groups)
    {
        var result = new List<PlacedObject>();
        if (groups is null) return result;
        foreach (var g in groups)
        {
            string path = g?["Key"]?["path"]?.GetValue<string>() ?? "";
            foreach (var item in Array(g?["Value"]?["items"]))
            {
                double y = Num(item!["position"]?["y"], double.NegativeInfinity);
                result.Add(new PlacedObject(path, ReadVec(item["position"]), Num(item["scale"]?["x"], 1),
                    double.IsFinite(y) ? y : null));
            }
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
        // Values built in memory (e.g. JsonValue.Create(8)) keep their CLR type.
        if (value.TryGetValue(out int i)) return i;
        if (value.TryGetValue(out long l)) return l;
        if (value.TryGetValue(out float f)) return f;
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
