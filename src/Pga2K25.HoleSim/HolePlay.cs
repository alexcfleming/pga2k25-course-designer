namespace Pga2K25.HoleSim;

/// <summary>Strokes gained against the baseline by phase: they add up to the whole hole's (the baseline's tee value less the strokes taken).</summary>
public sealed record PhaseSg(double Tee, double Approach, double Short, double Putting)
{
    public double Total => Tee + Approach + Short + Putting;
}

/// <summary>A whole hole played many times for one player and pin: what it costs and where.</summary>
public sealed record HoleSummary(
    int Pin, string Player, int Par, int Samples, double TeeYards, double BaselineTee,
    double Expected, double Ci95, PhaseSg Sg,
    double FairwayHit, double GreenInRegulation, double Scrambling, double PenaltyShare,
    double Birdie, double ParOrBetter, double BogeyOrWorse,
    double DriveAim, string Plan)
{
    /// <summary>Each play-out's score, in draw order (experiments pair them with the edited hole's).</summary>
    public IReadOnlyList<double> Scores { get; init; } = [];

    /// <summary>Strokes over (+) or under (−) par.</summary>
    public double ToPar => Expected - Par;
}

/// <summary>What the play-out uses on one hole: the shot context, the player and the plan for each pin.</summary>
public sealed class HolePlan
{
    public required ShotContext Context { get; init; }
    public required PlayerProfile Player { get; init; }
    public required double Reach { get; init; }
    public TerrainUse? Terrain { get; init; }

    /// <summary>The drive's aim, yds right of the aim-marker line (par 4s and 5s).</summary>
    public double DriveAim { get; init; }
}

/// <summary>
/// Stage 4e: the whole hole, tee to cup. Par 4s and 5s start with the player's drive at the chosen aim (the step 6
/// physics and draws); par 3s with the best tee shot. Then: on a par 5 the second shot follows the go-or-lay-up plan
/// from where the drive typically finishes; approach shots use each spot's best aim from the heat map; chips and
/// pitches aim at the pin; on the green the putting map gives the expected putts. Water costs its penalty and the
/// next shot is played from rough at the spot. Every shot is scored against the baseline, so the phases add up.
/// </summary>
public static class HolePlay
{
    public static HoleSummary Play(HolePlan plan, int pin, int samples = 600, int seed = 61, CancellationToken cancel = default)
    {
        var c = plan.Context; var m = c.Hole;
        var pinAt = m.Pins[pin - 1].Frame;
        var shot = c.WithGolfer(c.Golfer with { MaxCarry = Math.Max(c.Golfer.MaxCarry, plan.Reach) });
        var sm = c.ShortMap(pin);
        foreach (var (k, v) in c.ShortMaps) shot.ShortMaps[k] = v;
        var heat = c.HeatMap(pin, cancel);
        var putts = c.PuttingMap(pin);

        // The tee shot's aim (par 3) and the par 5 second-shot plan, from where the drive typically finishes.
        FramePoint? teeAim = null; FramePoint? layup = null; string planText = "";
        if (m.Par == 3)
        {
            teeAim = ApproachSim.Sweep(c, new FramePoint(0, 0), pin, 200, size: 7, spacing: 4, fromTee: true)[0].Aim;
            planText = $"tee shot aimed {Words(teeAim.Value - pinAt, pinAt)}";
        }
        else if (m.Par >= 5)
        {
            var fins = Enumerable.Range(0, 200).Select(i => GeometryRun.Drive(m, m.Grid, c.Ground, plan.Player, plan.DriveAim, new Random(unchecked(seed * 7 + i)), plan.Terrain).Finish)
                .Where(f => m.Grid.At(f) == Ground.Fairway).OrderBy(f => f.DistanceTo(pinAt)).ToList();
            if (fins.Count > 0)
            {
                var second = SecondShot.Plan(c, heat, fins[fins.Count / 2], pin, plan.Reach, cancel: cancel);
                if (!second.ShouldGo && second.BestLayup is { } b) { layup = b.Target; planText = $"second shot laid up to {b.TargetYards:0} yds"; }
                else planText = second.Go.InReach ? "second shot at the green" : "second shot as close as it goes";
            }
        }

        var results = new (double Strokes, double Tee, double App, double Short, double Putt, bool Fairway, bool Gir, bool Penalty)[samples];
        Parallel.For(0, samples, new ParallelOptions { CancellationToken = cancel }, i =>
            results[i] = One(c, shot, plan, pin, heat, sm, putts, teeAim, layup, new Random(unchecked(seed * 1_000_003 + i))));

        var strokes = results.Select(r => r.Strokes).ToArray();
        double mean = strokes.Average(), sd = Math.Sqrt(strokes.Sum(s => (s - mean) * (s - mean)) / Math.Max(1, samples - 1));
        var tee = c.Baseline.ExpectedStrokes(Lie.Tee, pinAt.Length);
        var sg = new PhaseSg(results.Average(r => r.Tee), results.Average(r => r.App), results.Average(r => r.Short), results.Average(r => r.Putt));
        // Scrambling: par or better after missing the green in regulation.
        var missed = results.Where(r => !r.Gir).ToList();
        // Scores with fractional putts: a birdie is a score below par − 0.5, and so on.
        double Share(Func<double, bool> f) => strokes.Count(f) / (double)samples;
        return new HoleSummary(pin, plan.Player.Name, m.Par, samples, pinAt.Length, tee.Value, mean, 1.96 * sd / Math.Sqrt(samples), sg,
            m.Par == 3 ? double.NaN : results.Count(r => r.Fairway) / (double)samples, results.Count(r => r.Gir) / (double)samples,
            missed.Count == 0 ? double.NaN : missed.Count(r => r.Strokes < m.Par + 0.5) / (double)missed.Count, results.Count(r => r.Penalty) / (double)samples,
            Share(s => s < m.Par - 0.5), Share(s => s < m.Par + 0.5), Share(s => s >= m.Par + 0.5), plan.DriveAim, planText) { Scores = strokes };
    }

    static (double, double, double, double, double, bool, bool, bool) One(ShotContext c, ShotContext shot, HolePlan plan, int pin, ApproachHeatMap heat,
        ShortGameMap sm, PuttingMap putts, FramePoint? teeAim, FramePoint? layup, Random r)
    {
        var m = c.Hole; var pinAt = m.Pins[pin - 1].Frame;
        double teeSg = 0, appSg = 0, shortSg = 0, puttSg = 0, strokes = 0;
        bool fairway = false, penalty = false;
        int shotsToGreen = 0;
        double Base(Ground g, FramePoint p, bool fromTee = false)
        {
            if (fromTee) return c.Baseline.ExpectedStrokes(Lie.Tee, p.DistanceTo(pinAt)).Value;
            if (g == Ground.Green && c.Green.OnGreen(p)) return c.Baseline.ExpectedStrokes(Lie.Green, p.DistanceTo(pinAt) * 3).Value;
            var (lie, _) = c.Lies.For(g);
            return c.Baseline.ExpectedStrokes(lie == Lie.Green ? Lie.Fairway : lie, p.DistanceTo(pinAt)).Value;
        }

        var pos = new FramePoint(0, 0); Ground lie = Ground.Fairway; bool onTee = true;
        for (int k = 0; k < 12; k++)
        {
            if (!onTee && lie == Ground.Green && c.Green.OnGreen(pos))
            {
                double p = putts.At(pos, c.Putting);
                puttSg += Base(Ground.Green, pos) - p;
                strokes += p;
                return (strokes, teeSg, appSg, shortSg, puttSg, fairway, shotsToGreen <= m.Par - 2, penalty);
            }
            double before = Base(lie, pos, onTee);
            double d = pos.DistanceTo(pinAt);
            FramePoint fin; Ground g; int phase;   // 0 tee, 1 approach, 2 short game
            if (onTee && m.Par > 3)
            {
                fin = GeometryRun.Drive(m, m.Grid, c.Ground, plan.Player, plan.DriveAim, r, plan.Terrain).Finish;
                g = m.Grid.At(fin); fairway = g == Ground.Fairway; phase = 0;
            }
            else
            {
                // Short game: any shot starting within the short-game map (35 yds of the green), as long as it's playable.
                bool inShort = !onTee && sm.At(pos) is not null && lie is not (Ground.Water or Ground.Scrub);
                FramePoint aim = onTee ? teeAim ?? pinAt
                    : layup is { } t && k == 1 && d > t.DistanceTo(pinAt) + 25 ? t
                    : inShort ? pinAt
                    : heat.At(pos)?.Aim ?? pinAt;
                var s = ShotEngine.Shoot(shot, pos, aim, pin, r, fromTee: onTee, value: false, lieOverride: onTee ? null : lie);
                fin = s.Finish; g = s.Lie; phase = inShort ? 2 : 1;
            }
            int pen = g == Ground.Water ? c.Lies.WaterPenalty : 0;
            if (pen > 0) penalty = true;
            var nextLie = g == Ground.Water ? Ground.Rough : g;
            double after = Base(nextLie, fin);
            double sg = before - 1 - pen - after;
            if (phase == 0) teeSg += sg; else if (phase == 1) appSg += sg; else shortSg += sg;
            strokes += 1 + pen; shotsToGreen += 1 + pen;
            pos = fin; lie = nextLie; onTee = false;
        }
        // Never reached the green in 12 shots: count what's left at the baseline.
        double rest = Base(lie, pos);
        return (strokes + rest, teeSg, appSg, shortSg, puttSg, fairway, false, penalty);
    }

    static string Words(FramePoint rel, FramePoint pinAt)
    {
        var dir = pinAt * (1 / Math.Max(pinAt.Length, 1e-9));
        double along = rel.Lat * dir.Lat + rel.Along * dir.Along, side = rel.Lat * dir.Along - rel.Along * dir.Lat;
        if (Math.Abs(along) < 0.5 && Math.Abs(side) < 0.5) return "at the pin";
        var parts = new List<string>();
        if (Math.Abs(side) >= 0.5) parts.Add($"{Math.Abs(side):0} yds {(side > 0 ? "right" : "left")}");
        if (Math.Abs(along) >= 0.5) parts.Add($"{Math.Abs(along):0} yds {(along > 0 ? "long" : "short")}");
        return string.Join(" and ", parts) + " of the pin";
    }
}
