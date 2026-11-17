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

/// <param name="Route">Waypoints: [0] route start (usually, not always, the back tee), [^1] green anchor. A waypoint on top of
/// the one before it is dropped when reading.</param>
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

    /// <summary>
    /// The par the designer set by hand (<c>creatorDefinedPar</c>), or null when the hole follows the game's own par
    /// from its length. Where it is set it matches <see cref="Par"/> on every file seen.
    /// </summary>
    public int? CreatorPar { get; init; }

    /// <summary>The flag's offset from the cup (<c>flagOffset</c>, m); zero on every file seen.</summary>
    public Vec2 FlagOffset { get; init; }

    /// <summary>Each pin's <c>description</c> number, in pin order; 0 on every file seen, and its meaning isn't known.</summary>
    public IReadOnlyList<int> PinDescriptions { get; init; } = [];
}

/// <summary>
/// A map layer the designer paints with brushes and closed outlines (<c>outOfBounds2</c>, <c>clearTrees</c>,
/// <c>clearDetail</c>, <c>crowdMap2</c>). Splines with <see cref="SurfaceSpline.Closed"/> set are filled areas
/// (state 3 on every file seen); open ones are ribbons their width wide; brushes are their stamp.
/// </summary>
public sealed record MapEdits(IReadOnlyList<SurfaceBrush> Brushes, IReadOnlyList<SurfaceSpline> Splines)
{
    public static readonly MapEdits None = new([], []);
    public bool IsEmpty => Brushes.Count == 0 && Splines.Count == 0;
}

public sealed record SplinePoint(Vec2 In, Vec2 Anchor, Vec2 Out, double Width);

public sealed record SurfaceSpline(int Index, int Surface, bool Closed, double Width, IReadOnlyList<SplinePoint> Points)
{
    /// <summary>A bunker spline's own depth sliders (<c>bunkerOverrideParameters</c>); null when it uses the course's.</summary>
    public BunkerOverride? Bunker { get; init; }
}

/// <summary>
/// The designer's bunker depth sliders (metres): how far the sand sits below the bunker's edge. The game stamps this
/// into the ground when it builds the course, so it is not in <c>height[]</c>.
/// </summary>
/// <param name="BaseDepth">Depth of the floor below the foot of the lip.</param>
/// <param name="LipStart">Where the lip starts; 0.5 on every course seen. Not modelled.</param>
/// <param name="LipDepth">Height of the lip at the bunker's edge.</param>
/// <param name="LipSteepness">0–1: how steep the lip's face is.</param>
public sealed record BunkerDepression(double BaseDepth, double LipStart, double LipDepth, double LipSteepness)
{
    public static readonly BunkerDepression None = new(0, 0.5, 0, 0);

    /// <summary>The floor's depth below the edge: lip plus base, which the file also stores as <c>stampDepth</c>.</summary>
    public double Depth => Math.Max(0, BaseDepth) + Math.Max(0, LipDepth);
}

/// <summary>
/// One bunker brush's or spline's own sliders (<c>bunkerOverrideParameters</c>). A negative value means "use the
/// course's" (<see cref="BunkerSettings.Resolve"/>). <paramref name="FlatFloor"/> is 0 for the course's setting and 1
/// for on (the only values seen); 2 is read as off.
/// </summary>
public sealed record BunkerOverride(double BaseDepth, double LipStart, double LipDepth, double LipSteepness, int FlatFloor);

/// <summary>
/// The pars and tee-set lengths the game saved in <c>CourseMetadata</c>: what its scorecard and course details show.
/// The game re-saves them from the layout, except that its "Keep Yardages" option holds the old lengths (and gives up,
/// re-saving them, when the tee or hole count changes), so they can disagree with the holes as drawn.
/// </summary>
/// <param name="Pars">Par by hole, in hole order (<c>pars</c>).</param>
/// <param name="TeeSetLengths">Metres by tee set, then by hole (<c>teeSetInfos[].holeLengths</c>).</param>
public sealed record StoredScorecard(IReadOnlyList<int> Pars, IReadOnlyList<IReadOnlyList<double>> TeeSetLengths)
{
    /// <summary>Each tee set's colour number (<c>teeSetInfos[].color</c>), front to back; see <see cref="TeeColourName"/>.</summary>
    public IReadOnlyList<int> TeeColours { get; init; } = [];

    /// <summary>How many pin sets the course has (<c>pinSets</c>).</summary>
    public int PinSets { get; init; }

    /// <summary>The sudden-death playoff holes, numbered from 1 (<c>PlayoffHolesSuddenDeath</c> stores them from 0).</summary>
    public IReadOnlyList<int> SuddenDeathHoles { get; init; } = [];

    /// <summary>The aggregate playoff holes, numbered from 1; empty on every file seen.</summary>
    public IReadOnlyList<int> AggregateHoles { get; init; } = [];

    /// <summary>The single playoff hole for a front nine, back nine and full round, numbered from 1; null when not set (−1).</summary>
    public int? PlayoffFront9 { get; init; }
    public int? PlayoffBack9 { get; init; }
    public int? PlayoffFull { get; init; }

    /// <summary>The course's difficulty rating (<c>difficulty</c>): 0 until the game rates it, then 1 Easiest to 5 Hardest.</summary>
    public int Difficulty { get; init; }

    /// <summary>The sudden-death holes a new course gets ([17, 9] in the file: holes 18 and 10).</summary>
    public static readonly IReadOnlyList<int> DefaultSuddenDeath = [18, 10];

    /// <summary>True when the playoff holes are still the game's defaults.</summary>
    public bool PlayoffsAreDefault => SuddenDeathHoles.SequenceEqual(DefaultSuddenDeath) && AggregateHoles.Count == 0
        && PlayoffFront9 is null && PlayoffBack9 is null && PlayoffFull is null;

    /// <summary>
    /// The game's tee colour names by number: Green, Black, Blue, White, Red, Gold. Cassimbo's front tee set is 4, Red,
    /// as its scorecard shows; the other numbers aren't checked colour by colour.
    /// </summary>
    public static string? TeeColourName(int colour) => colour switch
    {
        0 => "Green", 1 => "Black", 2 => "Blue", 3 => "White", 4 => "Red", 5 => "Gold", _ => null,
    };

    /// <summary><see cref="Difficulty"/> in the game's words, or null before it is rated.</summary>
    public string? DifficultyName => Difficulty switch
    {
        1 => "Easiest", 2 => "Easy", 3 => "Medium", 4 => "Hard", 5 => "Hardest", _ => null,
    };

    public static StoredScorecard? Read(JsonObject? meta)
    {
        if (meta is null) return null;
        var pars = (meta["pars"] as JsonArray ?? []).Select(p => (int)CourseModel.Num(p)).ToList();
        var infos = meta["teeSetInfos"] as JsonArray ?? [];
        var sets = infos
            .Select(s => (IReadOnlyList<double>)(s?["holeLengths"] as JsonArray ?? []).Select(l => CourseModel.Num(l, double.NaN)).ToList())
            .ToList();
        if (pars.Count == 0 && sets.Count == 0) return null;
        static List<int> Holes(JsonNode? list) => (list as JsonArray ?? []).Select(h => (int)CourseModel.Num(h) + 1).ToList();
        static int? One(JsonNode? h) => CourseModel.Num(h, -1) is var n && n >= 0 ? (int)n + 1 : null;
        return new StoredScorecard(pars, sets)
        {
            TeeColours = infos.Select(s => (int)CourseModel.Num(s?["color"], -1)).ToList(),
            PinSets = (int)CourseModel.Num(meta["pinSets"]),
            SuddenDeathHoles = Holes(meta["PlayoffHolesSuddenDeath"]),
            AggregateHoles = Holes(meta["PlayoffHolesAggregate"]),
            PlayoffFront9 = One(meta["PlayoffHoleFront9"]),
            PlayoffBack9 = One(meta["PlayoffHoleBack9"]),
            PlayoffFull = One(meta["PlayoffHoleFullCourse"]),
            Difficulty = (int)CourseModel.Num(meta["difficulty"]),
        };
    }
}

/// <summary>
/// The designer's speed and firmness sliders, as the file stores them: 0–1, or −1 for the game's default. The game's
/// own words for them (Soft … Ultra firm, Slow … Ultra fast) are known, but not where on the slider each starts.
/// </summary>
public sealed record PlayingConditions(double TerrainFirmness, double GreenFirmness, double FairwaySpeed, double FairwayFirmness,
    double RoughFirmness, double HeavyRoughFirmness, double BunkerSpeed, double BunkerFirmness)
{
    public static readonly PlayingConditions Default = new(-1, -1, -1, -1, -1, -1, -1, -1);
}

/// <summary>The course's bunker depths (<c>bunkerSettings2</c>).</summary>
/// <param name="HighResStamping">The designer's "high resolution bunkers" switch.</param>
/// <param name="Regular">Painted and drawn bunkers.</param>
/// <param name="Pot">Pot bunker objects. Read, not modelled.</param>
/// <param name="FlatFloor">The floor is level, not following the ground the bunker was cut into.</param>
public sealed record BunkerSettings(bool HighResStamping, BunkerDepression Regular, BunkerDepression Pot, bool FlatFloor)
{
    public static readonly BunkerSettings None = new(false, BunkerDepression.None, BunkerDepression.None, false);

    /// <summary>One bunker's depths: its own sliders where set, the course's otherwise.</summary>
    public (BunkerDepression Depression, bool FlatFloor) Resolve(BunkerOverride? own)
    {
        if (own is null) return (Regular, FlatFloor);
        static double Pick(double mine, double course) => mine >= 0 ? mine : course;
        return (new BunkerDepression(Pick(own.BaseDepth, Regular.BaseDepth), Pick(own.LipStart, Regular.LipStart),
                Pick(own.LipDepth, Regular.LipDepth), Pick(own.LipSteepness, Regular.LipSteepness)),
            own.FlatFloor switch { 1 => true, 2 => false, _ => FlatFloor });
    }
}

/// <param name="Tool">0 = absolute height, 1 = relative (raise/lower/level).</param>
public sealed record HeightStamp(int Index, int Tool, int Type, Vec2 Position, double Scale, double RotationY, double Value);

/// <param name="Scale">Brush diameter in metres (scale.x).</param>
/// <param name="Value">Paint opacity 0–1.</param>
public sealed record SurfaceBrush(int Index, int Category, int Type, Vec2 Position, double Scale, double RotationY, double Value)
{
    /// <summary>A bunker brush's own depth sliders (<c>bunkerOverrideParameters</c>); null when it uses the course's.</summary>
    public BunkerOverride? Bunker { get; init; }
}

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
/// A scatter group from placedObjects4 <c>clusters</c>: the game places <paramref name="Count"/> copies of the asset
/// within <paramref name="Radius"/> m of <paramref name="Centre"/>, from its own seed, so where each one lands isn't stored.
/// </summary>
public sealed record PlacedCluster(string AssetPath, Vec2 Centre, double Radius, int Count);

/// <summary>
/// A planted area from placedObjects4 <c>splines</c>: a closed outline (its waypoints) the game fills with the asset
/// at <paramref name="FillPerSquareMetre"/> (<c>fillPct</c>; read as instances per m², which is inferred).
/// </summary>
public sealed record PlacedArea(string AssetPath, IReadOnlyList<Vec2> Outline, double FillPerSquareMetre)
{
    /// <summary>The outline's area (m², shoelace).</summary>
    public double AreaSquareMetres
    {
        get
        {
            double a = 0;
            for (int i = 0; i < Outline.Count; i++)
            {
                Vec2 p = Outline[i], q = Outline[(i + 1) % Outline.Count];
                a += p.X * q.Z - q.X * p.Z;
            }
            return Math.Abs(a) / 2;
        }
    }

    /// <summary>About how many the game plants in it.</summary>
    public int EstimatedCount => (int)Math.Round(AreaSquareMetres * FillPerSquareMetre);
}

/// <summary>Objects laid along a path (placedObjects4 <c>objectPaths2</c>): walls, fences and the like, by waypoint.</summary>
public sealed record ObjectPath(string AssetPath, IReadOnlyList<Vec2> Points);

/// <summary>
/// Water drawn with the designer's own water tools, stored as <c>GeneratedRiverSetupPrefab</c> object paths flagged
/// <c>isWater</c> rather than in <c>water[]</c>. "Running water" is an open path that follows the ground, as wide as
/// the path (Sangoma's are 3–12.7 m); "create a body of water" is a closed outline filled to <paramref name="Level"/>
/// (the path's absolute height). The game digs the bed for both, so the whole ribbon or outline is water.
/// </summary>
/// <param name="Path">The path as a spline (surface <see cref="Surfaces.Water"/>; <see cref="SurfaceSpline.Width"/> is the water's width).</param>
/// <param name="Level">Water level (m) of a body of water; null for running water.</param>
public sealed record WaterPath(SurfaceSpline Path, double? Level)
{
    public bool IsBody => Path.Closed;

    /// <summary>
    /// Running water whose <c>height</c> is below this holds no visible water, and is left out. Read as how full the
    /// channel is (inferred): Devil's Perch's streams are all 1.0 and show in game; Sangoma's three are 0.01–0.08 and
    /// don't (no river beside the hole 14 pond, and a distance marker stands on dry ground 0.4 m from one's centre line).
    /// </summary>
    public const double MinStreamHeight = 0.25;
}

/// <summary>
/// Surface ids used by surfaceSplines2[].surface and surfaceBrushes2[].surfaceCategory.
/// 0/1/2/3/4/7/10 are confirmed by the reference reference tool. 8 and 15 were identified by
/// matching Cassimbo's layers against an in-game overhead capture, 18 from the designers who paint it (see docs/scorecard-maps.md).
/// </summary>
public static class Surfaces
{
    public const int Bunker = 0, Green = 1, Fairway = 2, Rough = 3, HeavyRough = 4, CartPath = 7, Water = 10;

    /// <summary>Dirt/service road (wide open ribbons).</summary>
    public const int Road = 8;

    /// <summary>Waste area (grey sand/gravel scrub).</summary>
    public const int Waste = 15;

    /// <summary>
    /// Surface weathering, splines and brushes alike: a tint over the ground beneath, not a playing surface.
    /// drgnslyr221 paints bare ground with it on Devil's Perch; Cassimbo's "reverse weathering" splines carry the
    /// fairway green out over brown heavy rough, which is why an overhead capture once read them as fairway cut.
    /// </summary>
    public const int Weathering = 18;

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

    /// <summary>
    /// The id the game gives the course when it first saves it (<c>_id</c>, e.g. "offlineSavebyvwcvzpqavk"). Renaming the
    /// course in the designer keeps it; a copy saved as a new file gets its own. Empty when the file has none.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>Theme, backdrop, surface materials, terrain painting layers and surface weathering as the designer set them.</summary>
    public CourseSurfaces Look { get; init; } = new();
    public required GameVersion Version { get; init; }
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<Hole> Holes { get; init; }
    public required IReadOnlyList<SurfaceSpline> SurfaceSplines { get; init; }
    public required IReadOnlyList<HeightStamp> HeightStamps { get; init; }
    public required IReadOnlyList<SurfaceBrush> SurfaceBrushes { get; init; }
    public required IReadOnlyList<PlacedObject> PlacedObjects { get; init; }
    public IReadOnlyList<PlacedCluster> PlacedClusters { get; init; } = [];
    public IReadOnlyList<PlacedArea> PlacedAreas { get; init; } = [];
    public IReadOnlyList<ObjectPath> ObjectPaths { get; init; } = [];

    /// <summary>
    /// Painted water and water planes (<c>water[]</c>): brushes whose <see cref="SurfaceBrush.Value"/> is the
    /// water level (m). See <see cref="Water"/> for the full footprints.
    /// </summary>
    public IReadOnlyList<SurfaceBrush> WaterBrushes { get; init; } = [];

    /// <summary>The same <c>water[]</c> entries with both footprint axes (<see cref="WaterBrushes"/> keeps scale.x only).</summary>
    public IReadOnlyList<WaterPlane> Water { get; init; } = [];

    /// <summary>Streams and ponds made with the designer's water tools (object paths, not <c>water[]</c>).</summary>
    public IReadOnlyList<WaterPath> WaterPaths { get; init; } = [];

    /// <summary>True when the course has any inland water to trace: <see cref="Water"/> entries or <see cref="WaterPaths"/>.</summary>
    public bool HasWater => Water.Count > 0 || WaterPaths.Count > 0;

    /// <summary>
    /// Spots the designer marked as dry in game though the model shows water (<see cref="NotWaterMarks"/>). Not in the
    /// course file: products set it from NotWater.json in the output folder, and the water tracer drops those pieces.
    /// </summary>
    public IReadOnlyList<Vec2> NotWater { get; set; } = [];

    /// <summary>Distance markers with the game's ground height under each (empty in most files).</summary>
    public IReadOnlyList<DistanceMarker> DistanceMarkers { get; init; } = [];

    /// <summary>The same markers joined into the lines drawn with the measure tool (<see cref="CourseFormat.MeasureLines"/>).</summary>
    public IReadOnlyList<MeasureLine> MeasureLines { get; init; } = [];
    public bool FlattenGreens { get; init; }
    public bool BlurHeight { get; init; }

    /// <summary>The course's bunker depths (<c>bunkerSettings2</c>); <see cref="BunkerSettings.None"/> when the file has none.</summary>
    public BunkerSettings Bunkers { get; init; } = BunkerSettings.None;

    /// <summary>The designer's speed and firmness sliders (0–1 each; −1 is the game's default).</summary>
    public PlayingConditions Conditions { get; init; } = PlayingConditions.Default;

    /// <summary>Out of bounds as the designer painted it (<c>outOfBounds2</c>): filled outlines on every file seen.</summary>
    public MapEdits OutOfBounds { get; init; } = MapEdits.None;

    /// <summary>Where the designer cleared the game's own trees (<c>clearTrees</c>).</summary>
    public MapEdits ClearTrees { get; init; } = MapEdits.None;

    /// <summary>Where the designer cleared the game's own detail: grass tufts, rocks and plants (<c>clearDetail</c>).</summary>
    public MapEdits ClearDetail { get; init; } = MapEdits.None;

    /// <summary>Crowd areas (<c>crowdMap2</c>).</summary>
    public MapEdits Crowd { get; init; } = MapEdits.None;

    /// <summary>Entries in <c>treeDensityMap</c>: empty on every file seen, so their form isn't known.</summary>
    public int TreeDensityEntries { get; init; }

    /// <summary>
    /// Entries in <c>terrainHeight</c>, a second height layer beside <c>height</c>. Empty on every file seen, and not in
    /// the terrain replay.
    /// </summary>
    public int TerrainHeightEntries { get; init; }

    /// <summary>The course's tee marker width (<c>teeMarkerWidth</c>, m): 3.048, 3.9624, 4.572 or 6.096 (10, 13, 15, 20 ft).</summary>
    public double TeeMarkerWidthMetres { get; init; }

    /// <summary>The designer's name for <see cref="TeeMarkerWidthMetres"/>, as its tee marker setting shows it.</summary>
    public string? TeeMarkerWidthName => Math.Round(TeeMarkerWidthMetres, 3) switch
    {
        3.048 => "Very narrow", 3.962 => "Narrow", 4.572 => "Standard", 6.096 => "Wide", _ => null,
    };

    /// <summary>True when any painted or drawn bunker is cut below its edge.</summary>
    public bool HasBunkerDepths =>
        SurfaceBrushes.Any(b => b.Category == Surfaces.Bunker && Bunkers.Resolve(b.Bunker).Depression.Depth > 0)
        || SurfaceSplines.Any(s => s.Surface == Surfaces.Bunker && Bunkers.Resolve(s.Bunker).Depression.Depth > 0);

    /// <summary>
    /// The plot's pre-made hills setting (<c>hillsHeight</c>, metres; the designer shows it in feet, 10 → 33). Terrain the
    /// game makes before any brush, which the brush replay can't see. 0 on every built sample; a fresh blank plot may
    /// have 10. Found by Plot Starter (2026-11-08), which writes it; Studio only reads it.
    /// </summary>
    public double HillsHeightMetres { get; init; }
    public bool HasPremadeHills => HillsHeightMetres > 0.01;

    /// <summary>The course description's unpacked size (bytes), set by <see cref="CourseFile.ReadModel"/>; 0 when unknown.</summary>
    public long DescriptionBytes { get; set; }

    /// <summary>
    /// The scorecard the game saved beside the layout (<c>CourseMetadata</c>), set by <see cref="CourseFile.ReadModel"/>;
    /// null when the file has none. It can lag the layout (<see cref="StoredScorecard"/>).
    /// </summary>
    public StoredScorecard? Stored { get; set; }
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
            Look = CourseSurfaces.From(desc),
            Id = desc["_id"] is JsonValue id && id.TryGetValue(out string? s) ? s : "",
            Version = version,
            SchemaVersion = (int)Num(desc["version"]),
            Holes = ReadHoles(desc["holes3"] as JsonArray),
            SurfaceSplines = ReadSplines(desc["surfaceSplines2"] as JsonArray),
            HeightStamps = ReadStamps(desc["height"] as JsonArray),
            SurfaceBrushes = ReadBrushes(desc["surfaceBrushes2"] as JsonArray),
            PlacedObjects = ReadObjects(desc["placedObjects4"] as JsonArray),
            PlacedClusters = ReadClusters(desc["placedObjects4"] as JsonArray),
            PlacedAreas = ReadAreas(desc["placedObjects4"] as JsonArray),
            ObjectPaths = ReadObjectPaths(desc["placedObjects4"] as JsonArray),
            WaterBrushes = ReadBrushes(desc["water"] as JsonArray),
            Water = ReadWater(desc["water"] as JsonArray),
            WaterPaths = ReadWaterPaths(desc["placedObjects4"] as JsonArray),
            DistanceMarkers = Array(desc["distanceMarkers"]).Select(m => m?["position"])
                .Where(p => p is not null && double.IsFinite(Num(p["y"], double.NaN)))
                .Select(p => new DistanceMarker(ReadVec(p), Num(p!["y"]))).ToList(),
            MeasureLines = CourseFormat.MeasureLines.Read(desc["distanceMarkers"] as JsonArray),
            FlattenGreens = desc["flattenGreens"]?.GetValue<bool>() ?? false,
            BlurHeight = desc["blurHeight"]?.GetValue<bool>() ?? false,
            Bunkers = ReadBunkerSettings(desc["bunkerSettings2"] as JsonObject),
            OutOfBounds = ReadMapEdits(desc["outOfBounds2"] as JsonObject),
            ClearTrees = ReadMapEdits(desc["clearTrees"] as JsonObject),
            ClearDetail = ReadMapEdits(desc["clearDetail"] as JsonObject),
            Crowd = ReadMapEdits(desc["crowdMap2"] as JsonObject),
            TreeDensityEntries = (desc["treeDensityMap"] as JsonArray)?.Count ?? 0,
            TerrainHeightEntries = (desc["terrainHeight"] as JsonArray)?.Count ?? 0,
            TeeMarkerWidthMetres = Num(desc["teeMarkerWidth"]),
            Conditions = new PlayingConditions(Num(desc["terrainFirmness"], -1), Num(desc["greenFirmness"], -1), Num(desc["fairwaySpeed"], -1),
                Num(desc["fairwayFirmness"], -1), Num(desc["roughFirmness"], -1), Num(desc["heavyRoughFirmness"], -1),
                Num(desc["bunkerSpeed"], -1), Num(desc["bunkerFirmness"], -1)),
            HillsHeightMetres = Num(desc["hillsHeight"]),
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
            var route = new List<Vec2>();
            // The routing tool can leave a waypoint on top of the next (Devil's Perch's par 3s put their aim point on
            // the green), which carries no length and would make the approach a zero vector. The later point stays: the
            // last one is the green anchor the pins hang from.
            foreach (var p in Array(h["waypoints"]).Select(ReadVec))
            {
                if (route.Count > 0 && Vec2.Distance(route[^1], p) <= 0.05) route[^1] = p;
                else route.Add(p);
            }
            if (route.Count == 0) continue;
            var anchor = route[^1];
            var tees = Array(h["tees"]).Select(t => ReadVec(t!["position"])).ToList();
            var pins = Array(h["pins"]).Select(p => anchor + ReadVec(p!["position"])).ToList();
            int creator = (int)Num(h["creatorDefinedPar"], -1);
            result.Add(new Hole(i + 1, (int)Num(h["par"], 0), route, tees, pins)
            {
                CreatorPar = creator > 0 ? creator : null,
                FlagOffset = h["flagOffset"] is JsonObject fo ? new Vec2(Num(fo["x"]), Num(fo["y"])) : default,
                PinDescriptions = Array(h["pins"]).Select(p => (int)Num(p?["description"])).ToList(),
            });
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
            result.Add(new SurfaceSpline(i, (int)Num(s["surface"], -1), IsClosed(s), Num(s["width"]), SplinePoints(s))
            {
                Bunker = ReadBunkerOverride(s["bunkerOverrideParameters"] as JsonObject),
            });
        }
        return result;
    }

    static MapEdits ReadMapEdits(JsonObject? e) => e is null ? MapEdits.None
        : new(ReadBrushes(e["brushes"] as JsonArray), ReadSplines(e["splines"] as JsonArray));

    static BunkerSettings ReadBunkerSettings(JsonObject? b)
    {
        if (b is null) return BunkerSettings.None;
        static BunkerDepression Depression(JsonNode? d) => d is null ? BunkerDepression.None
            : new(Num(d["baseDepth"]), Num(d["lipStart"], 0.5), Num(d["lipDepth"]), Num(d["lipSteepness"]));
        return new BunkerSettings(b["highResStamping"]?.GetValue<bool>() ?? false,
            Depression(b["regularBunkerDepressionSettings"]), Depression(b["potBunkerDepressionSettings"]),
            b["flatFloor"]?.GetValue<bool>() ?? false);
    }

    static BunkerOverride? ReadBunkerOverride(JsonObject? o) => o is null ? null
        : new(Num(o["baseDepth"], -1), Num(o["lipStart"], -1), Num(o["lipDepth"], -1), Num(o["lipSteepness"], -1), (int)Num(o["flatFloor"]));

    static List<SplinePoint> SplinePoints(JsonObject s) => Array(s["waypoints"]).Select(w => new SplinePoint(
        ReadVec(w!["pointOne"]), ReadVec(w["waypoint"]), ReadVec(w["pointTwo"]), Num(w["width"]))).ToList();

    static bool IsClosed(JsonObject s) => s.TryGetPropertyValue("state", out var state) && state is not null
        ? Num(state) != 0
        : (s["isClosed"]?.GetValue<bool>() ?? false) || (s["isFilled"]?.GetValue<bool>() ?? false);

    static List<WaterPath> ReadWaterPaths(JsonArray? groups) =>
        Entries(groups, "objectPaths2")
            .Where(e => e.Entry["isWater"]?.GetValue<bool>() == true && e.Entry["path"] is JsonObject)
            .Select((e, i) =>
            {
                var path = e.Entry["path"]!.AsObject();
                var spline = new SurfaceSpline(i, Surfaces.Water, IsClosed(path), Num(path["width"], 3), SplinePoints(path));
                // A body of water sits at the path's own height (heightRule 1); running water follows the ground.
                bool level = Num(e.Entry["heightRule"]) == 1 || path["absoluteY"]?.GetValue<bool>() == true;
                double height = Num(e.Entry["height"], double.NaN);
                if (!level && !spline.Closed && !(height >= WaterPath.MinStreamHeight)) return null;
                return new WaterPath(spline, level ? height : null);
            })
            .OfType<WaterPath>()
            .Where(p => p.Path.Points.Count >= (p.IsBody ? 3 : 2)).ToList();

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
                Num(b["scale"]?["x"]), Num(b["rotation"]?["y"]), Num(b["value"], 1))
            {
                Bunker = ReadBunkerOverride(b["bunkerOverrideParameters"] as JsonObject),
            });
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

    /// <summary>Each asset group in placedObjects4, with its path, and the entries under <paramref name="key"/>.</summary>
    static IEnumerable<(string Path, JsonNode Entry)> Entries(JsonArray? groups, string key) =>
        from g in groups ?? []
        let path = g?["Key"]?["path"]?.GetValue<string>() ?? ""
        from e in Array(g?["Value"]?[key])
        where e is not null
        select (path, e!);

    static List<Vec2> Waypoints(JsonNode? path) => Array(path?["waypoints"]).Select(w => ReadVec(w?["waypoint"])).ToList();

    static List<PlacedCluster> ReadClusters(JsonArray? groups) =>
        Entries(groups, "clusters").Select(e => new PlacedCluster(e.Path, ReadVec(e.Entry["position"]),
            Num(e.Entry["radius"]), (int)Num(e.Entry["count"], 1))).ToList();

    static List<PlacedArea> ReadAreas(JsonArray? groups) =>
        Entries(groups, "splines").Select(e => new PlacedArea(e.Path, Waypoints(e.Entry["path"]), Num(e.Entry["fillPct"])))
            .Where(a => a.Outline.Count >= 3).ToList();

    static List<ObjectPath> ReadObjectPaths(JsonArray? groups) =>
        Entries(groups, "objectPaths2").Select(e => new ObjectPath(e.Path, Waypoints(e.Entry["path"])))
            .Where(p => p.Points.Count >= 2).ToList();

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
