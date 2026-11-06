namespace Pga2K25.HoleSim;

/// <summary>One spot of the approach heat map: the best of its aims, valued against the baseline from the same lie and distance.</summary>
public sealed record HeatSpot(FramePoint Where, Ground Lie, double Yards, double Expected, double? Baseline, FramePoint Aim, double GreenHit)
{
    /// <summary>Strokes better (+) or worse (−) than the baseline from the same lie and distance: the angle, the lie's surroundings and the green, not the distance.</summary>
    public double? Gain => Baseline - Expected;
}

/// <summary>Fairway spots the same distance from the pin (a 10-yd band): the best side against the worst.</summary>
public sealed record DistanceBand(double From, double To, int Spots, HeatSpot Best, HeatSpot Worst)
{
    public double Premium => Worst.Expected - Best.Expected;
}

/// <summary>Expected strokes from spots across the hole to one pin, each with its own best aim.</summary>
public sealed class ApproachHeatMap
{
    public const double BandYards = 10;

    public int Pin { get; }
    public double Spacing { get; }
    public int Samples { get; }
    public IReadOnlyList<HeatSpot> Spots { get; }

    /// <summary>
    /// Each spot against the others the same distance away (its 10-yd band): strokes better (+) or worse (−) than the
    /// band's median spot. Distance drops out, so what's left is the side, the angle and the lie. Null for a spot whose
    /// band has fewer than three spots.
    /// </summary>
    public IReadOnlyDictionary<HeatSpot, double> Relative { get; }

    public ApproachHeatMap(int pin, double spacing, IReadOnlyList<HeatSpot> spots, int samples)
    {
        Pin = pin; Spacing = spacing; Spots = spots; Samples = samples;
        var rel = new Dictionary<HeatSpot, double>();
        foreach (var band in spots.GroupBy(s => Math.Floor(s.Yards / BandYards)))
        {
            var list = band.ToList();
            if (list.Count < 3) continue;
            var sorted = list.Select(s => s.Expected).OrderBy(v => v).ToList();
            double median = sorted[sorted.Count / 2];
            foreach (var s in list) rel[s] = median - s.Expected;
        }
        Relative = rel;
    }

    /// <summary>The best fairway spot (lowest expected strokes), or the best spot of any lie when there's no fairway.</summary>
    public HeatSpot? BestFairway => Spots.Where(s => s.Lie == Ground.Fairway).MinBy(s => s.Expected) ?? Spots.MinBy(s => s.Expected);

    /// <summary>
    /// The fairway spot that beats the others at its own distance by the most (full shots, 60 yds and out): the best
    /// angle. Null when no spot is at least <see cref="ClearGain"/> better than its band's median.
    /// </summary>
    public HeatSpot? BestAngle => Spots.Where(s => s.Lie == Ground.Fairway && s.Yards >= 60 && Relative.TryGetValue(s, out var r) && r >= ClearGain).MaxBy(s => Relative[s]);

    /// <summary>Smaller than this, a spot's edge over its band is within the noise of the run.</summary>
    public const double ClearGain = 0.03;

    /// <summary>The median gain against the baseline: how much harder (−) or easier (+) than the baseline this pin plays overall from these spots.</summary>
    public double MedianGain
    {
        get
        {
            var g = Spots.Where(s => s.Gain is not null).Select(s => s.Gain!.Value).OrderBy(v => v).ToList();
            return g.Count == 0 ? 0 : g[g.Count / 2];
        }
    }

    public HeatSpot? At(FramePoint p) => Spots.Where(s => Math.Abs(s.Where.Lat - p.Lat) <= Spacing / 2 && Math.Abs(s.Where.Along - p.Along) <= Spacing / 2).MinBy(s => s.Where.DistanceTo(p));

    /// <summary>10-yd distance bands with at least three fairway spots at least 10 yds apart side to side.</summary>
    public IReadOnlyList<DistanceBand> Bands()
    {
        var list = new List<DistanceBand>();
        foreach (var g in Spots.Where(s => s.Lie == Ground.Fairway).GroupBy(s => Math.Floor(s.Yards / BandYards) * BandYards).OrderBy(g => g.Key))
        {
            var spots = g.ToList();
            if (spots.Count < 3) continue;
            var best = spots.MinBy(s => s.Expected)!; var worst = spots.MaxBy(s => s.Expected)!;
            if (best.Where.DistanceTo(worst.Where) < 10) continue;
            list.Add(new DistanceBand(g.Key, g.Key + BandYards, spots.Count, best, worst));
        }
        return list;
    }
}

/// <summary>Expected strokes still to play from every spot round the green, by where a shot finishes: putts on the green, the short game off it.</summary>
public sealed class LeaveMap(int pin, IReadOnlyList<(FramePoint Where, double Expected)> cells)
{
    public int Pin { get; } = pin;
    public IReadOnlyList<(FramePoint Where, double Expected)> Cells { get; } = cells;

    /// <summary>The colour scale: the 5th and 95th percentiles of the cells (at least 0.6 apart).</summary>
    public (double Lo, double Hi) Range
    {
        get
        {
            if (Cells.Count == 0) return (2, 3);
            var v = Cells.Select(c => c.Expected).OrderBy(x => x).ToList();
            double lo = v[(int)(v.Count * 0.05)], hi = v[Math.Min(v.Count - 1, (int)(v.Count * 0.95))];
            if (hi - lo < 0.6) { double mid = (hi + lo) / 2; lo = mid - 0.3; hi = mid + 0.3; }
            return (lo, hi);
        }
    }
}

/// <summary>One direction into the green, at a fixed distance, played from a fairway lie with its best aim.</summary>
/// <param name="Degrees">Right (+) or left (−) of the line of play, seen from the fairway looking at the green.</param>
public sealed record AngleOption(double Degrees, FramePoint From, FramePoint Aim, double Expected, double GreenHit);

/// <summary>Tee shots on a par 3 from one tee: the best aim and the pin itself.</summary>
public sealed record TeeShotOption(TeeInfo Tee, FramePoint From, double Yards, double PlaysLike, AimOption Best, AimOption AtPin, double? StrokesGained, Coverage Coverage);

/// <summary>
/// Stage 4c: the approach heat map by pin, the leave map round the green, the ideal angle into each pin from its
/// contours, and par 3 tee shots from every tee. Everything here reuses the 4b shot engine with the same random draws
/// for every aim, so differences between spots, aims and angles are fair.
/// </summary>
public static class ApproachMaps
{
    /// <summary>
    /// Spots every <paramref name="spacing"/> yds on fairway, rough and bunkers within <paramref name="corridor"/> yds of the
    /// line of play, from <paramref name="minYards"/> to <paramref name="maxYards"/> from the pin (outside the short game).
    /// </summary>
    public static IReadOnlyList<FramePoint> Spots(HoleModel m, int pin, double maxYards, double spacing = 5, double minYards = 40, double corridor = 45)
    {
        var pinAt = m.Pins[pin - 1].Frame; var g = m.Grid; var list = new List<FramePoint>();
        for (double along = Math.Ceiling(g.AlongMin / spacing) * spacing + spacing / 2; along < g.AlongMax; along += spacing)
            for (double lat = Math.Ceiling(g.LatMin / spacing) * spacing + spacing / 2; lat < g.LatMax; lat += spacing)
            {
                var p = new FramePoint(lat, along);
                double d = p.DistanceTo(pinAt);
                if (d < minYards || d > maxYards) continue;
                if (g.At(p) is not (Ground.Fairway or Ground.Rough or Ground.HeavyRough or Ground.Bunker)) continue;
                if (Polyline.Nearest(m.Route, p).Distance > corridor) continue;
                list.Add(p);
            }
        return list;
    }

    /// <summary>The heat map: at each spot, the best of nine aims (3 × 3, 4 yds apart) on <paramref name="samples"/> shots, then that aim on three times as many fresh shots (the same draws at every spot).</summary>
    public static ApproachHeatMap Heat(ShotContext c, int pin, IReadOnlyList<FramePoint> spots, double spacing = 5, int samples = 200,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        c.ShortMap(pin);
        var pinAt = c.Hole.Pins[pin - 1].Frame;
        var result = new HeatSpot[spots.Count]; int done = 0;
        Parallel.For(0, spots.Count, new ParallelOptions { CancellationToken = cancel }, i =>
        {
            var from = spots[i]; var lie = c.Grid.At(from);
            var aims = ApproachSim.Sweep(c, from, pin, samples, seed: 11, size: 3, spacing: 4, parallel: false);
            var (e, hit) = Mean(c, from, aims[0].Aim, pin, samples * 3, 29, false, null);
            double yds = from.DistanceTo(pinAt);
            var (bl, _) = c.Lies.For(lie);
            var start = c.Baseline.ExpectedStrokes(bl == Lie.Green ? Lie.Fairway : bl, yds);
            result[i] = new HeatSpot(from, lie, yds, e, start.Covered ? start.Value : null, aims[0].Aim, hit);
            int n = Interlocked.Increment(ref done);
            if (n % 20 == 0) progress?.Report(n / (double)spots.Count);
        });
        return new ApproachHeatMap(pin, spacing, result, samples);
    }

    /// <summary>Expected strokes to hole out by where a shot finishes, every yard within the short game's reach of the green.</summary>
    public static LeaveMap Leave(ShotContext c, int pin)
    {
        var sm = c.ShortMap(pin); var putts = c.PuttingMap(pin); var g = c.Grid;
        var cells = new List<(FramePoint, double)>();
        double lat0 = c.Green.Cells.Min(p => p.Lat) - ShortGameMap.Reach, lat1 = c.Green.Cells.Max(p => p.Lat) + ShortGameMap.Reach;
        double al0 = c.Green.Cells.Min(p => p.Along) - ShortGameMap.Reach, al1 = c.Green.Cells.Max(p => p.Along) + ShortGameMap.Reach;
        for (double along = Math.Floor(al0) + 0.5; along < al1; along++)
            for (double lat = Math.Floor(lat0) + 0.5; lat < lat1; lat++)
            {
                var p = new FramePoint(lat, along);
                if (lat < g.LatMin || lat >= g.LatMax || along < g.AlongMin || along >= g.AlongMax) continue;
                var ground = g.At(p);
                if (ground == Ground.Green && c.Green.OnGreen(p)) { cells.Add((p, putts.At(p, c.Putting))); continue; }
                if (sm.At(p) is { } e && ground is not (Ground.Water or Ground.Scrub)) { cells.Add((p, e)); continue; }
                if (ground is Ground.Water or Ground.Scrub)
                {
                    var (after, penalty) = ShotEngine.After(c, p, ground, pin);
                    cells.Add((p, after + penalty));
                }
            }
        return new LeaveMap(pin, cells);
    }

    /// <summary>
    /// The best direction into a pin from its contours: shots from <paramref name="yards"/> yds, from the line of play and
    /// every <paramref name="step"/>° either side up to <paramref name="maxDegrees"/>, all from a fairway lie with the same
    /// draws and each with its best aim. Spots off the course are played as if they were fairway.
    /// </summary>
    public static IReadOnlyList<AngleOption> Angles(ShotContext c, int pin, double yards = 150, double maxDegrees = 75, double step = 15, int samples = 240,
        CancellationToken cancel = default)
    {
        c.ShortMap(pin);
        var pinAt = c.Hole.Pins[pin - 1].Frame;
        var back = ApproachSetup.SpotOnRoute(c.Hole, pin, yards) - pinAt;
        back = back * (1 / Math.Max(back.Length, 1e-9));
        var degrees = new List<double>();
        for (double d = -maxDegrees; d <= maxDegrees + 1e-9; d += step) degrees.Add(d);
        var result = new AngleOption[degrees.Count];
        Parallel.For(0, degrees.Count, new ParallelOptions { CancellationToken = cancel }, k =>
        {
            double th = degrees[k] * Math.PI / 180;
            // Counter-clockwise in (lat, along) turns a spot behind the green toward +lat: the golfer's right.
            var dir = new FramePoint(back.Lat * Math.Cos(th) - back.Along * Math.Sin(th), back.Lat * Math.Sin(th) + back.Along * Math.Cos(th));
            var from = pinAt + dir * yards;
            var aims = ApproachSim.Sweep(c, from, pin, samples / 2, seed: 11, size: 3, spacing: 4, lieOverride: Ground.Fairway, parallel: false);
            var (e, hit) = Mean(c, from, aims[0].Aim, pin, samples, 31, false, Ground.Fairway);
            result[k] = new AngleOption(degrees[k], from, aims[0].Aim, e, hit);
        });
        return result;
    }

    /// <summary>Par 3 tee shots from every tee to one pin: 49 aims each (7 × 7, 4 yds apart, out to 12 yds from the pin), the best and the pin itself, with strokes gained against the baseline's tee rows.</summary>
    public static IReadOnlyList<TeeShotOption> TeeShots(ShotContext c, int pin, int samples = 300)
    {
        var pinAt = c.Hole.Pins[pin - 1].Frame; var list = new List<TeeShotOption>();
        foreach (var t in c.Hole.Tees)
        {
            var from = c.Hole.Frame.ToFrame(t.World);
            var aims = ApproachSim.Sweep(c, from, pin, samples, size: 7, spacing: 4, fromTee: true);
            var atPin = aims.First(o => o.Offset.Length < 1e-9);
            double yds = from.DistanceTo(pinAt);
            double plays = yds + (c.Terrain.HeightFt(pinAt) - c.Terrain.HeightFt(from)) * c.Rules.YardsPerFootRise;
            var start = c.Baseline.ExpectedStrokes(Lie.Tee, yds);
            list.Add(new TeeShotOption(t, from, yds, plays, aims[0], atPin, start.Covered ? start.Value - aims[0].Expected : null, start.Coverage));
        }
        return list;
    }

    static (double Expected, double GreenHit) Mean(ShotContext c, FramePoint from, FramePoint aim, int pin, int n, int seed, bool fromTee, Ground? lie)
    {
        double sum = 0; int on = 0;
        for (int i = 0; i < n; i++)
        {
            var s = ShotEngine.Shoot(c, from, aim, pin, new Random(unchecked(seed * 1_000_003 + i)), fromTee, lieOverride: lie);
            sum += 1 + s.After + s.Penalty; if (s.Lie == Ground.Green && c.Green.OnGreen(s.Finish)) on++;
        }
        return (sum / n, on / (double)n);
    }
}
