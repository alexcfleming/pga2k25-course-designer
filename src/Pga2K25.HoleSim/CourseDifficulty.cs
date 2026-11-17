using Pga2K25.CourseFormat;

namespace Pga2K25.HoleSim;

/// <summary>One hole as the whole-hole play-out scores it: every pin, for each player, from the back tee.</summary>
/// <param name="SteepGreen">Share of the replayed green too steep to hold a stopped ball. A replay green over
/// <see cref="CourseDifficulty.DoubtfulGreenShare"/> is usually wrong (the game levels greens), so the hole is marked.</param>
public sealed record HoleDifficulty(int Hole, int Par, int Yards, string TeeName, IReadOnlyList<HoleSummary> Power, IReadOnlyList<HoleSummary> Normal,
    double SteepGreen)
{
    public double PowerScore => Power.Average(s => s.Expected);
    public double NormalScore => Normal.Average(s => s.Expected);
    public double PowerToPar => PowerScore - Par;
    public double NormalToPar => NormalScore - Par;

    /// <summary>Both players' average over par: what the holes are ranked by.</summary>
    public double ToPar => (PowerToPar + NormalToPar) / 2;

    /// <summary>The easiest and hardest pin, both players averaged, in strokes over par.</summary>
    public (double Easiest, double Hardest) PinRange
    {
        get
        {
            var byPin = Power.Zip(Normal, (p, n) => (p.Expected + n.Expected) / 2 - Par).ToList();
            return (byPin.Min(), byPin.Max());
        }
    }

    public bool Doubtful => SteepGreen > CourseDifficulty.DoubtfulGreenShare;
}

/// <summary>What one player's round adds up to over the course.</summary>
public sealed record RoundFigures(double Score, double ToPar, double FairwayHit, double GreenInRegulation, double Scrambling,
    double HolesWithPenalty, PhaseSg Sg, double Birdies, double BogeysOrWorse);

/// <summary>The whole course played: every hole, both players, every pin.</summary>
public sealed record CourseDifficultyResult(string CourseName, IReadOnlyList<HoleDifficulty> Holes, int Samples, string BaselineId,
    DateTime CourseSavedUtc, double Seconds)
{
    public int Par => Holes.Sum(h => h.Par);
    public RoundFigures Power => CourseDifficulty.Round(Holes, h => h.Power);
    public RoundFigures Normal => CourseDifficulty.Round(Holes, h => h.Normal);

    /// <summary>Holes from hardest to easiest (both players averaged over every pin); ties keep hole order.</summary>
    public IReadOnlyList<HoleDifficulty> Ranked => Holes.OrderByDescending(h => h.ToPar).ThenBy(h => h.Hole).ToList();
}

/// <summary>
/// The Course Difficulty Estimate: the hole design simulator's whole-hole play-out (Hole › Summary) on every hole, for
/// both players and every pin, from the back tee. Every hole is played the same way, as the CLI's <c>holesim --whole</c>
/// does: the replayed green smoothed as in the green step, flat ground outside it, the golfer fitted to the baseline
/// anchors, the drive at its best line from a quick sweep. No hole's terrain gate or live checks are used, so the holes
/// stand on the same footing; a green the replay makes too steep is marked instead.
/// </summary>
public static class CourseDifficulty
{
    /// <summary>A replayed green with more than this share too steep to hold a ball is marked as doubtful.</summary>
    public const double DoubtfulGreenShare = 0.25;

    /// <summary>Play-outs per pin per player; the Summary tab's own figure.</summary>
    public const int DefaultSamples = 600;

    /// <summary>Plays every hole. About a minute for 18 holes on Cassimbo once the golfer fit is cached (a few seconds more the first time).</summary>
    public static CourseDifficultyResult Measure(string coursePath, int samples = DefaultSamples, IProgress<string>? progress = null,
        CancellationToken cancel = default, string? fitCacheFolder = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stamp = FileStamp.Read(coursePath);
        var file = CourseFile.Load(coursePath);
        var course = file.ReadModel();
        var sheet = AssumptionSheet.Load();
        var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
        double stimp = AssumptionSheet.Stimp(course.GreenSpeed) ?? 10;
        double smoothing = sheet["terrain.smoothing"].Number;
        var players = new[] { sheet.Profile("power"), sheet.Profile("normal") };
        var layers = Maps.CourseLayers.From(course);   // built once, shared by every hole
        fitCacheFolder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "holesim");
        string name = course.Name;
        var holes = new List<HoleDifficulty>();
        foreach (var hole in course.Holes)
        {
            cancel.ThrowIfCancellationRequested();
            if (hole.Pins.Count == 0 || hole.Tees.Count == 0 || hole.Route.Count < 2) continue;
            progress?.Report($"Playing hole {hole.Number} of {course.Holes.Count}, every pin for both players…");
            var m = HoleModelBuilder.Build(course, file.Description, hole.Number, new HoleModelOptions(), stamp, layers);
            if (m.Pins.Count == 0) continue;
            name = m.CourseName;
            var surface = GreenSurface.Build(m, ReplayTerrain.Smoothed(course, m, smoothing));
            var (ctx, _) = ApproachSetup.Build(m, surface, null, sheet, baseline, stimp, fitCacheFolder, cancel: cancel);
            var steep = surface.SlopeStats(ctx.Putting.Friction, ctx.Putting.Settings.StaticFactor).TooSteep;
            var played = new List<HoleSummary>[2];
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                double aim = m.Par == 3 ? 0
                    : GeometryRun.Run(m, player, 1, ctx.Ground, sheet.Lies(), baseline, new RunSettings { SamplesPerAim = 400 }).Best.Aim;
                var plan = new HolePlan { Context = ctx, Player = player, Reach = sheet.Reach(player.Id), DriveAim = aim };
                played[i] = m.Pins.Select(p => HolePlay.Play(plan, p.Number, samples, cancel: cancel)).ToList();
            }
            holes.Add(new HoleDifficulty(m.Number, m.Par, m.Tee.Yards, m.Tee.Name, played[0], played[1], steep));
        }
        return new CourseDifficultyResult(name, holes, samples, baseline.Id, stamp.ModifiedUtc, watch.Elapsed.TotalSeconds);
    }

    /// <summary>A player's round: each figure averaged over the hole's pins, then added (scores, penalties) or averaged (rates).</summary>
    internal static RoundFigures Round(IReadOnlyList<HoleDifficulty> holes, Func<HoleDifficulty, IReadOnlyList<HoleSummary>> pick)
    {
        double Avg(HoleDifficulty h, Func<HoleSummary, double> f) => pick(h).Select(f).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();
        double Mean(Func<HoleSummary, double> f, Func<HoleDifficulty, bool>? only = null) =>
            holes.Where(only ?? (_ => true)).Select(h => Avg(h, f)).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();
        double Sum(Func<HoleSummary, double> f) => holes.Sum(h => Avg(h, f));
        double score = Sum(s => s.Expected);
        return new RoundFigures(score, score - holes.Sum(h => h.Par),
            Mean(s => s.FairwayHit, h => h.Par >= 4), Mean(s => s.GreenInRegulation), Mean(s => s.Scrambling),
            Sum(s => s.PenaltyShare),
            new PhaseSg(Sum(s => s.Sg.Tee), Sum(s => s.Sg.Approach), Sum(s => s.Sg.Short), Sum(s => s.Sg.Putting)),
            Sum(s => s.Birdie), Sum(s => s.BogeyOrWorse));
    }

    /// <summary>
    /// A suggested handicap (stroke index) for each hole, from the ranking (<paramref name="ranked"/>, hardest first).
    /// On 18 holes the harder nine (more strokes over par in all, both players averaged; a tie goes to the nine with
    /// the hardest hole) takes the odd numbers and the other nine the even, each nine's holes hardest first, so the
    /// strokes a player receives fall on both nines (Alex's call, 17 Nov 2026). Any other course is numbered by rank.
    /// Returns hole number → stroke index.
    /// </summary>
    public static Dictionary<int, int> StrokeIndex(IReadOnlyList<HoleDifficulty> ranked)
    {
        var result = new Dictionary<int, int>();
        bool nines = ranked.Count == 18 && ranked.Count(h => h.Hole is >= 1 and <= 9) == 9 && ranked.Count(h => h.Hole is >= 10 and <= 18) == 9;
        if (!nines)
        {
            for (int i = 0; i < ranked.Count; i++) result[ranked[i].Hole] = i + 1;
            return result;
        }
        static bool Front(HoleDifficulty h) => h.Hole <= 9;
        double front = ranked.Where(Front).Sum(h => h.ToPar), back = ranked.Where(h => !Front(h)).Sum(h => h.ToPar);
        bool frontHarder = Math.Abs(front - back) > 1e-9 ? front > back : Front(ranked[0]);
        int odd = 1, even = 2;
        foreach (var h in ranked)   // hardest first, so each nine takes its numbers in its own order
            result[h.Hole] = Front(h) == frontHarder ? (odd += 2) - 2 : (even += 2) - 2;
        return result;
    }
}
