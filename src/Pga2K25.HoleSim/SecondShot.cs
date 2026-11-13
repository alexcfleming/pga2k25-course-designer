namespace Pga2K25.HoleSim;

/// <summary>One lay-up target: shots from the second-shot spot aimed there, valued by where they finish.</summary>
/// <param name="Rechecked">One of the best few, played again on fresh draws (the value shown is from those).</param>
public sealed record LayupOption(FramePoint Target, double TargetYards, double Expected, double FairwayShare, double HazardShare, bool Rechecked = false);

/// <summary>Going for the green: the best aim round the pin with the player's longest fairway club.</summary>
public sealed record GoOption(FramePoint Aim, double Expected, double GreenHit, double HazardShare, bool InReach);

/// <summary>Go or lay up from one spot on a par 5, to one pin.</summary>
public sealed record SecondShotPlan(FramePoint From, Ground Lie, double Yards, double Reach, double Skill, GoOption Go, IReadOnlyList<LayupOption> Layups)
{
    /// <summary>The best of the lay-ups played again on fresh draws (so the winner isn't just the luckiest of many).</summary>
    public LayupOption? BestLayup => Layups.Where(l => l.Rechecked).MinBy(l => l.Expected) ?? Layups.MinBy(l => l.Expected);

    /// <summary>Strokes saved by going instead of the best lay-up (+ means go, − means lay up).</summary>
    public double GoGain => BestLayup is { } b ? b.Expected - Go.Expected : 0;

    public bool ShouldGo => GoGain > 0;

    /// <summary>The best lay-up in each 10-yd band of distance left to the pin.</summary>
    public IReadOnlyList<LayupOption> ByDistance() => Layups.GroupBy(l => Math.Floor(l.TargetYards / 10) * 10).OrderBy(g => g.Key).Select(g => g.MinBy(l => l.Expected)!).ToList();
}

/// <summary>
/// Stage 4d: the par 5 second shot. From a spot after the drive, the player either goes at the green with its longest
/// fairway club (best of nine aims round the pin) or lays up to a fairway spot short of it. Every shot is played with
/// the 4b shot engine, the same draws for every option, and valued by where it finishes: the putting map on the green,
/// the short game round it, the approach heat map for the fairway, rough and bunkers beyond (each spot with its own best
/// third shot), and the baseline anywhere else. The lay-up that wins is the one that leaves the cheapest third shot,
/// misses included, so a lay-up to a favourite distance only wins when the fairway there is wide enough to find.
/// </summary>
public static class SecondShot
{
    /// <summary>Expected strokes still to play from where a shot finished.</summary>
    public static double Value(ShotContext c, ApproachHeatMap heat, FramePoint finish, Ground lie, int pin)
    {
        if (lie == Ground.Green && c.Green.OnGreen(finish)) return c.PuttingMap(pin).At(finish, c.Putting);
        if (lie is Ground.Water or Ground.Scrub) { var (a, p) = ShotEngine.After(c, finish, lie, pin); return a + p; }
        if (c.ShortMaps.TryGetValue(pin, out var sm) && sm.At(finish) is { } e) return e;
        if (heat.At(finish) is { } spot && spot.Lie == lie) return spot.Expected;
        return ShotEngine.BaselineAfter(c, finish, lie, pin);
    }

    /// <param name="heat">The approach heat map for this pin, built with the same golfer (it values the third shot).</param>
    /// <param name="reach">The player's longest shot off the fairway (carry and roll), yds.</param>
    public static SecondShotPlan Plan(ShotContext c, ApproachHeatMap heat, FramePoint from, int pin, double reach, int samples = 150,
        double minLayupYards = 40, CancellationToken cancel = default)
    {
        c.ShortMap(pin);
        var pinAt = c.Hole.Pins[pin - 1].Frame;
        var lie = c.Grid.At(from);
        var shot = c.WithGolfer(c.Golfer with { MaxCarry = reach });
        foreach (var (k, v) in c.ShortMaps) shot.ShortMaps[k] = v;   // the short game doesn't depend on the long clubs
        double yards = from.DistanceTo(pinAt);

        (double E, double OnGreen, double Fairway, double Hazard) Play(FramePoint aim, int n, int seed)
        {
            double sum = 0; int on = 0, fw = 0, hz = 0;
            var draws = ShotDraws.For(seed, n);
            for (int i = 0; i < n; i++)
            {
                var s = ShotEngine.Shoot(shot, from, aim, pin, draws[i].Distance, draws[i].Side, value: false);
                sum += 1 + Value(c, heat, s.Finish, s.Lie, pin);
                if (s.Lie == Ground.Green && c.Green.OnGreen(s.Finish)) on++;
                if (s.Lie == Ground.Fairway) fw++;
                if (s.Lie is Ground.Bunker or Ground.Water or Ground.Scrub) hz++;
            }
            return (sum / n, on / (double)n, fw / (double)n, hz / (double)n);
        }

        // Go: nine aims round the pin (4 yds apart in the shot's frame), the best on twice the shots.
        var dir = (pinAt - from) * (1 / Math.Max(yards, 0.5)); var right = new FramePoint(dir.Along, -dir.Lat);
        var goAims = new List<FramePoint>();
        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) goAims.Add(pinAt + right * (i * 4) + dir * (j * 4));
        var goScores = new double[goAims.Count];
        Parallel.For(0, goAims.Count, new ParallelOptions { CancellationToken = cancel }, k => goScores[k] = Play(goAims[k], samples, 41).E);
        var goAim = goAims[Array.IndexOf(goScores, goScores.Min())];
        var go = Play(goAim, samples * 2, 43);
        var goOption = new GoOption(goAim, go.E, go.OnGreen, go.Hazard, yards <= reach);

        // Lay up: every fairway spot of the heat map in reach, at least minLayupYards from the pin and short of it: on the
        // golfer's side of the green (not the fairway behind it) and nearer the pin than the ball is now.
        var back = from - pinAt;
        var targets = heat.Spots.Where(s => s.Lie == Ground.Fairway && s.Yards >= minLayupYards && s.Yards < yards - 10 && s.Where.DistanceTo(from) <= reach
                && (s.Where - pinAt).Lat * back.Lat + (s.Where - pinAt).Along * back.Along > 0)
            .Select(s => s.Where).ToList();
        var layups = new LayupOption[targets.Count];
        Parallel.For(0, targets.Count, new ParallelOptions { CancellationToken = cancel }, k =>
        {
            var r = Play(targets[k], samples, 47);
            layups[k] = new LayupOption(targets[k], targets[k].DistanceTo(pinAt), r.E, r.Fairway, r.Hazard);
        });
        // The best few again on fresh draws, so the winner isn't just the luckiest of many.
        var list = layups.ToList();
        foreach (var top in list.OrderBy(l => l.Expected).Take(5).ToList())
        {
            var r = Play(top.Target, samples * 2, 53);
            list[list.IndexOf(top)] = top with { Expected = r.E, FairwayShare = r.Fairway, HazardShare = r.Hazard, Rechecked = true };
        }
        return new SecondShotPlan(from, lie, yards, reach, c.Golfer.Skill, goOption, list);
    }
}
