using System.Text.Json;
using System.Text.Json.Serialization;
using Pga2K25.CourseFormat;

namespace Pga2K25.TerrainMap;

/// <summary>A landing area along the line of play, yards from the tee.</summary>
public sealed record RoutingZone(string Name, int FromYards, int ToYards)
{
    public const int WidthYards = 40;
    public double CentreYards => (FromYards + ToYards) / 2.0;
}

/// <summary>
/// A placeholder arrow's fixed length and the usual range for its par, back-tee yards; where it can bend, yards
/// along the line of play; and its landing zones.
/// </summary>
public sealed record RoutingPar(int Par, int Yards, int MinYards, int MaxYards, IReadOnlyList<int> BendYards, IReadOnlyList<RoutingZone> Zones)
{
    static readonly RoutingZone Drive = new("Drive zone", 250, 300), Layup = new("Layup zone", 420, 470);

    // Par 4s turn where the drive lands; par 5s at 40 % and 80 % of the way.
    public static readonly IReadOnlyList<RoutingPar> All =
    [
        new(3, 185, 140, 240, [], []),
        new(4, 420, 350, 480, [275], [Drive]),
        new(5, 545, 500, 600, [218, 436], [Drive, Layup]),
    ];

    public static RoutingPar For(int par) => All.FirstOrDefault(p => p.Par == par) ?? All[1];
}

/// <summary>
/// One hole in a routing test: a tee point (world X/Z, metres), a bearing in degrees clockwise from game north
/// (−Z; east is −X), and a turn at each of its par's bend points (degrees, + right, − left). Its length along the
/// line of play is its par's placeholder, never its own; bending a hole never changes its yards.
/// </summary>
public sealed class RoutingArrow
{
    public const double MetresPerYard = 0.9144;
    /// <summary>How far one bend can turn, either way; turns snap to <see cref="TurnStep"/>.</summary>
    public const double MaxTurn = 80, TurnStep = 5;

    public int Par { get; set; } = 4;
    public double X { get; set; }
    public double Z { get; set; }
    public double Bearing { get; set; }
    /// <summary>Turn at each bend point, in order. Missing entries (a 1.7 file has none) are straight.</summary>
    public List<double> Bends { get; set; } = [];

    [JsonIgnore] public Vec2 Tee => new(X, Z);
    [JsonIgnore] public RoutingPar Placeholder => RoutingPar.For(Par);
    [JsonIgnore] public double LengthMetres => Placeholder.Yards * MetresPerYard;
    /// <summary>Unit direction of play off the tee, in world X/Z.</summary>
    [JsonIgnore] public Vec2 Direction => Heading(Bearing);
    [JsonIgnore] public Vec2 Green => Points[^1];
    [JsonIgnore] public int LegCount => Placeholder.BendYards.Count + 1;
    [JsonIgnore] public bool IsStraight => Enumerable.Range(0, LegCount - 1).All(k => Turn(k) == 0);

    public double Turn(int bend) => bend < Bends.Count && bend < LegCount - 1 ? Bends[bend] : 0;

    /// <summary>Sets one bend's turn, snapped to 5° and held within ±80°.</summary>
    public void SetTurn(int bend, double degrees)
    {
        if (bend < 0 || bend >= LegCount - 1) return;
        while (Bends.Count <= bend) Bends.Add(0);
        Bends[bend] = SnapTurn(degrees);
    }

    public void Straighten() => Bends.Clear();

    public static double SnapTurn(double degrees)
    {
        double d = ((degrees + 180) % 360 + 360) % 360 - 180;
        return Math.Clamp(Math.Round(d / TurnStep) * TurnStep, -MaxTurn, MaxTurn) + 0.0;  // + 0.0 turns −0 into 0
    }

    /// <summary>The bearing of leg <paramref name="leg"/> (0 = off the tee).</summary>
    public double LegBearing(int leg)
    {
        double b = Bearing;
        for (int k = 0; k < leg; k++) b += Turn(k);
        return (b % 360 + 360) % 360;
    }

    /// <summary>Yards along the line of play where each leg starts, and the green: 0, the bends, the length.</summary>
    [JsonIgnore] public IReadOnlyList<int> Breaks => [0, .. Placeholder.BendYards, Placeholder.Yards];

    /// <summary>The tee, each bend point and the green, in world X/Z.</summary>
    [JsonIgnore]
    public IReadOnlyList<Vec2> Points
    {
        get
        {
            var br = Breaks;
            var pts = new List<Vec2> { Tee };
            for (int k = 0; k + 1 < br.Count; k++)
                pts.Add(pts[k] + Heading(LegBearing(k)) * ((br[k + 1] - br[k]) * MetresPerYard));
            return pts;
        }
    }

    /// <summary>The point <paramref name="yards"/> along the line of play; past the green it carries on along the last leg.</summary>
    public Vec2 PointAlong(double yards)
    {
        var br = Breaks;
        var pts = Points;
        int k = 0;
        while (k < LegCount - 1 && yards > br[k + 1]) k++;
        return pts[k] + Heading(LegBearing(k)) * ((yards - br[k]) * MetresPerYard);
    }

    /// <summary>The table's shape: "Straight", "L 25°", "R 20° · L 15°".</summary>
    [JsonIgnore]
    public string ShapeShort => IsStraight ? "Straight"
        : string.Join(" · ", Enumerable.Range(0, LegCount - 1).Select(Turn).Where(t => t != 0).Select(t => $"{(t < 0 ? "L" : "R")} {Math.Abs(t):0}°"));

    /// <summary>"Dogleg left 25° at 275 yd", "Double dogleg: right 20° at 218 yd, then left 15° at 436 yd".</summary>
    [JsonIgnore]
    public string ShapeLong
    {
        get
        {
            if (LegCount == 1) return "Straight. Par 3s have no bend.";
            var parts = Enumerable.Range(0, LegCount - 1).Where(k => Turn(k) != 0)
                .Select(k => $"{(Turn(k) < 0 ? "left" : "right")} {Math.Abs(Turn(k)):0}° at {Placeholder.BendYards[k]} yd").ToList();
            return parts.Count switch
            {
                0 => "Straight.",
                2 => $"Double dogleg: {string.Join(", then ", parts)}.",
                _ => $"Dogleg {parts[0]}.",
            };
        }
    }

    public static Vec2 Heading(double bearing)
    {
        double a = bearing * Math.PI / 180;
        return new Vec2(-Math.Sin(a), -Math.Cos(a));
    }

    /// <summary>The bearing of a world direction, 0–360.</summary>
    public static double BearingOf(Vec2 d) => (Math.Atan2(-d.X, -d.Z) * 180 / Math.PI + 360) % 360;
}

/// <summary>
/// Two holes whose corridors crowd each other: their closest approach, centre line to centre line, between points
/// <paramref name="A"/> (on hole <paramref name="HoleA"/>) and <paramref name="B"/>. Under the corridor width they
/// overlap; under the width plus the clearance they are tight.
/// </summary>
public sealed record RoutingConflict(int HoleA, int HoleB, int Yards, Vec2 A, Vec2 B)
{
    public bool Overlap => Yards < RoutingCorridors.WidthYards;
    public int ClearYards => Yards - RoutingCorridors.WidthYards;
}

/// <summary>Corridors: a band along each hole's line of play, and the pairs of holes that crowd each other.</summary>
public static class RoutingCorridors
{
    public const int WidthYards = 70, ClearanceYards = 30;
    /// <summary>For back-to-back holes, this much of the first's end and the next's start isn't checked: the green
    /// and the next tee are meant to sit close.</summary>
    public const int BackToBackYards = 60;
    const int StepYards = 10;

    /// <summary>Every pair of holes closer than the corridor width plus the clearance, closest first.</summary>
    public static List<RoutingConflict> Conflicts(IReadOnlyList<RoutingArrow> arrows)
    {
        var samples = arrows.Select(a =>
        {
            int yards = a.Placeholder.Yards;
            var s = new List<(double Yards, Vec2 At)>();
            for (double y = 0; y < yards; y += StepYards) s.Add((y, a.PointAlong(y)));
            s.Add((yards, a.Green));
            return s;
        }).ToList();
        var result = new List<RoutingConflict>();
        for (int i = 0; i < arrows.Count; i++)
            for (int j = i + 1; j < arrows.Count; j++)
            {
                bool backToBack = j == i + 1;
                double end = arrows[i].Placeholder.Yards - BackToBackYards, best = double.MaxValue;
                Vec2 a = default, b = default;
                foreach (var p in samples[i])
                {
                    if (backToBack && p.Yards > end) continue;
                    foreach (var q in samples[j])
                    {
                        if (backToBack && q.Yards < BackToBackYards) continue;
                        double d = Vec2.Distance(p.At, q.At);
                        if (d < best) (best, a, b) = (d, p.At, q.At);
                    }
                }
                int yd = (int)Math.Round(best / RoutingArrow.MetresPerYard);
                if (best < double.MaxValue && yd < WidthYards + ClearanceYards) result.Add(new RoutingConflict(i + 1, j + 1, yd, a, b));
            }
        return result.OrderBy(c => c.Yards).ToList();
    }
}

/// <summary>
/// A course's one routing test, saved as RoutingTest.json in its output folder: arrows in hole order. Clearing it
/// deletes the file; there are no versions. Only Studio reads it, and no product draws it.
/// </summary>
public static class RoutingTest
{
    public const string FileName = "RoutingTest.json";

    sealed class Saved
    {
        public List<RoutingArrow> Arrows { get; set; } = [];
        public DateTime Updated { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathFor(string outputDir) => Path.Combine(outputDir, FileName);

    public static List<RoutingArrow> Load(string outputDir)
    {
        string path = PathFor(outputDir);
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), Json)?.Arrows ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Saves the arrows, or deletes the file when there are none.</summary>
    public static void Save(string outputDir, IEnumerable<RoutingArrow> arrows)
    {
        var list = arrows.ToList();
        string path = PathFor(outputDir);
        if (list.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(outputDir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Saved { Arrows = list, Updated = DateTime.Now }, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>A landing zone on one hole: its centre on the line of play and its rise from the tee.</summary>
public sealed record RoutingZoneRise(RoutingZone Zone, Vec2 Centre, double? RiseFeet);

/// <param name="WalkYards">Green to the next hole's tee, straight; null on the last hole.</param>
/// <param name="RiseFeet">Ground at the arrow's head less ground at its tee; null without a survey.</param>
public sealed record RoutingHole(int Number, RoutingArrow Arrow, int Yards, double? RiseFeet, int? WalkYards, IReadOnlyList<RoutingZoneRise> Zones);

/// <summary>The totals a routing test adds up to: par, placeholder yards, the nines, the par mix and the walks.</summary>
public sealed record RoutingTally(IReadOnlyList<RoutingHole> Holes)
{
    public int Par => Holes.Sum(h => h.Arrow.Par);
    public int Yards => Holes.Sum(h => h.Yards);
    public int OutPar => Holes.Take(9).Sum(h => h.Arrow.Par);
    public int OutYards => Holes.Take(9).Sum(h => h.Yards);
    public int InPar => Holes.Skip(9).Sum(h => h.Arrow.Par);
    public int InYards => Holes.Skip(9).Sum(h => h.Yards);
    public int Count(int par) => Holes.Count(h => h.Arrow.Par == par);
    public int WalkYards => Holes.Sum(h => h.WalkYards ?? 0);
    public int LongestWalkYards => Holes.Max(h => h.WalkYards) ?? 0;

    public static RoutingTally Of(IReadOnlyList<RoutingArrow> arrows, Func<Vec2, double>? feet = null)
    {
        double? Rise(RoutingArrow a, Vec2 at) => feet is null ? null : feet(at) - feet(a.Tee);
        return new(arrows.Select((a, i) => new RoutingHole(
            i + 1, a, a.Placeholder.Yards,
            Rise(a, a.Green),
            i + 1 < arrows.Count ? (int)Math.Round(Vec2.Distance(a.Green, arrows[i + 1].Tee) / RoutingArrow.MetresPerYard) : null,
            a.Placeholder.Zones.Select(z => { var c = a.PointAlong(z.CentreYards); return new RoutingZoneRise(z, c, Rise(a, c)); }).ToList())).ToList());
    }
}
