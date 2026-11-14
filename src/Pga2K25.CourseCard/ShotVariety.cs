using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.TerrainMap;

namespace Pga2K25.CourseCard;

/// <summary>
/// A golfer for the shot variety card: a bag of clubs (total yards, carry and roll, on flat ground), the longest
/// club they'd hit off the fairway at a green, and which tee set they play (0 = back, 1 = one tee forward, …).
/// The bags are placeholders: the game's golfers vary too much to model one, so these stand for a typical
/// Power Drive and Normal player and can be replaced.
/// </summary>
public sealed record VarietyGolfer(string Name, string Short, IReadOnlyDictionary<string, int> Bag, int ReachYards, int TeesUp, int CarryYards)
{
    /// <summary>Clubs longest first, as the card's grid lists them; "½" is a part wedge, under a full lob wedge.</summary>
    public static readonly IReadOnlyList<string> Clubs = ["Dr", "3W", "5W", "3H", "4i", "5i", "6i", "7i", "8i", "9i", "PW", "GW", "SW", "LW"];
    public const string PartWedge = "½";

    static IReadOnlyDictionary<string, int> BagOf(params int[] yards) => Clubs.Zip(yards).ToDictionary(p => p.First, p => p.Second);

    /// <summary>Power Drive: 320 yd carry (the hole simulator's), 265 yd off the fairway, from the back tee.</summary>
    public static readonly VarietyGolfer Power = new("Power Drive", "Power",
        BagOf(345, 290, 270, 255, 240, 228, 214, 200, 186, 172, 158, 142, 122, 100), 265, 0, 320);

    /// <summary>Normal: 270 yd carry (the hole simulator's), 240 yd off the fairway, one tee forward of the back.</summary>
    public static readonly VarietyGolfer Normal = new("Normal", "Normal",
        BagOf(290, 250, 235, 220, 205, 195, 182, 170, 158, 145, 132, 118, 100, 80), 240, 1, 270);

    /// <summary>The club family a club belongs to, for the card's bands and the family check.</summary>
    public static string Family(string club) => club switch
    {
        "Dr" or "3W" or "5W" or "3H" => "Woods",
        "4i" or "5i" => "Long irons",
        "6i" or "7i" => "Mid irons",
        "8i" or "9i" => "Short irons",
        _ => "Wedges",
    };

    public static readonly IReadOnlyList<string> Families = ["Woods", "Long irons", "Mid irons", "Short irons", "Wedges"];

    /// <summary>The shortest club that gets <paramref name="yards"/> there (within 4 yds), never driver off the deck.</summary>
    public string ClubFor(double yards)
    {
        for (int i = Clubs.Count - 1; i >= 1; i--)
            if (Bag[Clubs[i]] >= yards - 4) return Clubs[i];
        return "3W";
    }

    /// <summary>As <see cref="ClubFor"/>, but well under a full lob wedge it's a part shot.</summary>
    public string ClubInto(double yards) => yards < Bag["LW"] - 10 ? PartWedge : ClubFor(yards);
}

/// <summary>How one golfer plays one hole: the tee club, the lay-up club on a par 5 that isn't reached, and the shot into the green.</summary>
/// <param name="Yards">Scorecard yards from the golfer's tee.</param>
/// <param name="Approach">Yards into the green, plays-like for height (par 3s: the tee shot).</param>
/// <param name="Layup">The par 5 lay-up club, or null (reached in two, or not a par 5).</param>
/// <param name="ClubbedDown">The route bends at the aim point and driver would run through it, so a shorter club off the tee.</param>
public sealed record HoleShot(int Hole, int Par, int Yards, string Tee, string? Layup, int Approach, string Into,
    bool ReachedInTwo = false, bool ClubbedDown = false, bool Drivable = false);

/// <summary>One variety check: what it asks, the figure, and whether it passes (null: reported only).</summary>
public sealed record VarietyCheck(string Name, string Figure, bool? Passes);

/// <summary>A golfer's round: every hole's shots and the checks on the shots into greens.</summary>
public sealed record VarietyRound(VarietyGolfer Golfer, string TeeName, IReadOnlyList<HoleShot> Holes, IReadOnlyList<VarietyCheck> Checks)
{
    public IReadOnlyDictionary<string, int> FamilyCounts =>
        VarietyGolfer.Families.ToDictionary(f => f, f => Holes.Count(h => VarietyGolfer.Family(h.Into) == f));

    /// <summary>The tightest cluster of approach yards: where a <see cref="ShotVariety.ClusterYards"/>-yd window starts and how many fall in it.</summary>
    public (int From, int Count) Cluster
    {
        get
        {
            var yds = Holes.Select(h => h.Approach).ToList();
            var best = (From: 0, Count: 0);
            foreach (int lo in yds.Select(y => y).Distinct().OrderBy(y => y))
            {
                int n = yds.Count(y => y >= lo && y < lo + ShotVariety.ClusterYards);
                if (n > best.Count) best = (lo, n);
            }
            return best;
        }
    }
}

/// <summary>
/// The shot variety card's model. Each hole is played by each <see cref="VarietyGolfer"/> from the course file: the
/// route and its aim points, the tee set, and the ground's heights.
/// <list type="bullet">
/// <item>Par 3: the tee shot, at the hole's yards played-like for height (1 yd per 3 ft, the hole simulator's rule).</item>
/// <item>Par 4 and 5: driver along the route. Where the route bends <see cref="CornerDegrees"/> or more at the aim point
/// and driver would run more than 20 yds past it, the golfer takes the longest club that stops within 20 yds of it.</item>
/// <item>Into the green: what's left along the route, played-like for height, with the shortest club that gets there.</item>
/// <item>Par 5: within the golfer's reach, they go for the green; otherwise they lay up to a full sand wedge, and that
/// wedge is the shot into the green.</item>
/// </list>
/// </summary>
public static class ShotVariety
{
    public const double CornerDegrees = 20, YardsPerFootOfRise = 1 / 3.0;
    public const int ClusterYards = 15;

    /// <summary>The checks' limits (first guesses, kept here so they read in one place).</summary>
    public const int MinClubs = 8, MaxSameClub = 3, MaxInCluster = 4, MaxRun = 2, MinPar3SpreadYards = 50, MinNotDriver = 2;

    public static IReadOnlyList<VarietyRound> For(CourseModel course, IReadOnlyList<string> teeNames, IReadOnlyList<VarietyGolfer>? golfers = null)
    {
        golfers ??= [VarietyGolfer.Power, VarietyGolfer.Normal];
        var holes = course.Holes.Where(h => h.Route.Count >= 2 && h.Tees.Count > 0).OrderBy(h => h.Number).ToList();
        if (holes.Count == 0) return [];
        var ground = Ground(course, holes);
        int sets = Math.Max(1, holes.Max(h => h.Tees.Count));
        return golfers.Select(g =>
        {
            int set = Math.Max(0, sets - 1 - g.TeesUp);
            string tee = set < teeNames.Count ? teeNames[set] : "Back";
            var shots = holes.Select(h => Play(h, g, set, ground)).ToList();
            return new VarietyRound(g, tee, shots, Checks(shots));
        }).ToList();
    }

    /// <summary>The ground over every hole, coarse (2 m): only height changes along the line are read from it.</summary>
    static Func<Vec2, double> Ground(CourseModel course, IReadOnlyList<Hole> holes)
    {
        var pts = holes.SelectMany(h => h.Route.Concat(h.Tees)).ToList();
        var pad = new Vec2(20, 20);
        var min = new Vec2(pts.Min(p => p.X), pts.Min(p => p.Z)) - pad;
        var max = new Vec2(pts.Max(p => p.X), pts.Max(p => p.Z)) + pad;
        var field = TerrainReplay.Build(course, min, max, new TerrainMapOptions().Terrain with { CellMetres = 2 });
        return p => field.Sample(p) * TerrainSurvey.FeetPerMetre;
    }

    /// <summary>One hole for one golfer from tee set <paramref name="set"/>: tee → the route's aim points → green.</summary>
    public static HoleShot Play(Hole hole, VarietyGolfer g, int set, Func<Vec2, double> feet)
    {
        const double Y = TerrainSurvey.YardsPerMetre;
        var path = hole.Route.Skip(1).Prepend(hole.Tees[Math.Min(set, hole.Tees.Count - 1)]).ToList();
        var legs = path.Zip(path.Skip(1), (a, b) => Vec2.Distance(a, b) * Y).ToList();
        double total = legs.Sum();
        int card = (int)Math.Round(hole.TeeSetLength(set) * Y);
        double greenFeet = feet(path[^1]);
        Vec2 At(double yards)
        {
            double left = yards / Y;
            for (int i = 1; i < path.Count; i++)
            {
                double seg = Vec2.Distance(path[i - 1], path[i]);
                if (left <= seg) return path[i - 1] + (path[i] - path[i - 1]) * (left / Math.Max(seg, 1e-9));
                left -= seg;
            }
            return path[^1];
        }
        double PlaysLike(double from) => (total - from) + (greenFeet - feet(At(from))) * YardsPerFootOfRise;

        if (hole.Par <= 3)
        {
            double y = PlaysLike(0);
            string c = g.ClubInto(y);
            return new HoleShot(hole.Number, hole.Par, card, c, null, (int)Math.Round(y), c);
        }

        string tee = "Dr";
        double drive = g.Bag["Dr"];
        bool down = false;
        if (legs.Count > 1)
        {
            var a = (path[1] - path[0]).Normalized();
            var b = (path[2] - path[1]).Normalized();
            double bend = Math.Acos(Math.Clamp(a.X * b.X + a.Z * b.Z, -1, 1)) * 180 / Math.PI;
            if (bend >= CornerDegrees && drive > legs[0] + 20)
            {
                tee = VarietyGolfer.Clubs.FirstOrDefault(c => g.Bag[c] <= legs[0] + 20) ?? "LW";
                drive = g.Bag[tee];
                down = true;
            }
        }
        // A drive never finishes past the green: a short par 4 leaves a pitch at most.
        bool drivable = hole.Par == 4 && total - drive <= 60;
        drive = Math.Min(drive, Math.Max(0, total - 30));
        double rest = PlaysLike(drive);
        if (hole.Par == 4)
        {
            string into = g.ClubInto(rest);
            return new HoleShot(hole.Number, hole.Par, card, tee, null, (int)Math.Round(rest), into, ClubbedDown: down, Drivable: drivable);
        }
        if (rest <= g.ReachYards)
        {
            string into = g.ClubFor(rest);
            return new HoleShot(hole.Number, hole.Par, card, tee, null, (int)Math.Round(rest), into, ReachedInTwo: true, ClubbedDown: down);
        }
        // Lay up to a full sand wedge (or as far as the longest fairway club goes); the wedge in is the shot into the green.
        double second = Math.Min(rest - g.Bag["SW"], g.ReachYards);
        double third = rest - second;
        return new HoleShot(hole.Number, hole.Par, card, tee, g.ClubFor(second), (int)Math.Round(third), g.ClubInto(third), ClubbedDown: down);
    }

    /// <summary>The checks on the shots into greens (par 3 tee shots, par 4 approaches, par 5 shots in, lay-up wedges included).</summary>
    public static IReadOnlyList<VarietyCheck> Checks(IReadOnlyList<HoleShot> holes)
    {
        var into = holes.Select(h => h.Into).ToList();
        var counts = into.GroupBy(c => c).Select(g => (Club: g.Key, Count: g.Count())).OrderByDescending(x => x.Count).ToList();
        int run = 1, best = 1;
        for (int i = 1; i < into.Count; i++) { run = into[i] == into[i - 1] ? run + 1 : 1; best = Math.Max(best, run); }
        var round = new VarietyRound(VarietyGolfer.Power, "", holes, []);
        var (from, inCluster) = round.Cluster;
        var missing = VarietyGolfer.Families.Where(f => into.All(c => VarietyGolfer.Family(c) != f)).ToList();
        var p3 = holes.Where(h => h.Par == 3).ToList();
        int p3Spread = p3.Count > 0 ? p3.Max(h => h.Approach) - p3.Min(h => h.Approach) : 0;
        int p3Clubs = p3.Select(h => h.Into).Distinct().Count();
        var long_ = holes.Where(h => h.Par > 3).ToList();
        int notDriver = long_.Count(h => h.Tee != "Dr");
        var p5 = holes.Where(h => h.Par == 5).ToList();
        return
        [
            new("Clubs into greens", $"{counts.Count} different", counts.Count >= MinClubs),
            new("Most-used club", counts.Count == 0 ? "—" : $"{counts[0].Club} on {counts[0].Count} holes", counts.Count > 0 && counts[0].Count <= MaxSameClub),
            new("Tightest cluster", $"{inCluster} within {from}–{from + ClusterYards} yds", inCluster <= MaxInCluster),
            new("Back to back", best == 1 ? "never repeats" : $"{best} holes running", best <= MaxRun),
            new("Club families", missing.Count == 0 ? "all five used" : $"no {string.Join(", ", missing).ToLowerInvariant()}", missing.Count == 0),
            new("Par 3s", p3.Count == 0 ? "none" : $"{p3Clubs} clubs, {p3Spread} yds apart", p3.Count > 0 && p3Clubs == p3.Count && p3Spread >= MinPar3SpreadYards),
            new("Off the tee", $"not driver on {notDriver} of {long_.Count}", notDriver >= MinNotDriver),
            new("Par 5s in two", $"{p5.Count(h => h.ReachedInTwo)} of {p5.Count}", null),
        ];
    }
}
