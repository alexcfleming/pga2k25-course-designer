using System.Text.Json;
using System.Text.Json.Serialization;
using Pga2K25.CourseFormat;

namespace Pga2K25.TerrainMap;

/// <summary>A placeholder arrow's fixed length and the usual range for its par, back-tee yards.</summary>
public sealed record RoutingPar(int Par, int Yards, int MinYards, int MaxYards)
{
    public static readonly IReadOnlyList<RoutingPar> All = [new(3, 185, 140, 240), new(4, 420, 350, 480), new(5, 545, 500, 600)];

    public static RoutingPar For(int par) => All.FirstOrDefault(p => p.Par == par) ?? All[1];
}

/// <summary>
/// One hole in a routing test: a tee point (world X/Z, metres) and a bearing in degrees clockwise from game north
/// (−Z; east is −X). Its length is its par's placeholder, never its own.
/// </summary>
public sealed class RoutingArrow
{
    public const double MetresPerYard = 0.9144;

    public int Par { get; set; } = 4;
    public double X { get; set; }
    public double Z { get; set; }
    public double Bearing { get; set; }

    [JsonIgnore] public Vec2 Tee => new(X, Z);
    [JsonIgnore] public RoutingPar Placeholder => RoutingPar.For(Par);
    [JsonIgnore] public double LengthMetres => Placeholder.Yards * MetresPerYard;
    /// <summary>Unit direction of play in world X/Z.</summary>
    [JsonIgnore] public Vec2 Direction => Heading(Bearing);
    [JsonIgnore] public Vec2 Green => Tee + Direction * LengthMetres;

    public static Vec2 Heading(double bearing)
    {
        double a = bearing * Math.PI / 180;
        return new Vec2(-Math.Sin(a), -Math.Cos(a));
    }

    /// <summary>The bearing of a world direction, 0–360.</summary>
    public static double BearingOf(Vec2 d) => (Math.Atan2(-d.X, -d.Z) * 180 / Math.PI + 360) % 360;
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

/// <param name="WalkYards">Green to the next hole's tee, straight; null on the last hole.</param>
/// <param name="RiseFeet">Ground at the arrow's head less ground at its tee; null without a survey.</param>
public sealed record RoutingHole(int Number, RoutingArrow Arrow, int Yards, double? RiseFeet, int? WalkYards);

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

    public static RoutingTally Of(IReadOnlyList<RoutingArrow> arrows, Func<Vec2, double>? feet = null) =>
        new(arrows.Select((a, i) => new RoutingHole(
            i + 1, a, a.Placeholder.Yards,
            feet is null ? null : feet(a.Green) - feet(a.Tee),
            i + 1 < arrows.Count ? (int)Math.Round(Vec2.Distance(a.Green, arrows[i + 1].Tee) / RoutingArrow.MetresPerYard) : null)).ToList());
}
