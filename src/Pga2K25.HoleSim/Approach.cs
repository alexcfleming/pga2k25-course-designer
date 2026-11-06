namespace Pga2K25.HoleSim;

/// <summary>
/// The simulated golfer's shot skill. Spreads are shares of the plays-like distance (fairway lie), scaled by lie; the
/// values are fitted to the baseline's anchors by <see cref="GolferCalibration"/> (PLACEHOLDER defaults until then).
/// </summary>
public sealed record GolferParams
{
    public double SidePct { get; init; } = 0.035;
    public double DistPct { get; init; } = 0.045;
    public double RoughMult { get; init; } = 1.4;
    public double HeavyMult { get; init; } = 1.7;
    public double SandMult { get; init; } = 1.8;
    public double RecoveryMult { get; init; } = 2.5;
    public double TeeMult { get; init; } = 0.9;

    /// <summary>A spread (yds SD) every shot has on top of the share of its length: short chips aren't perfect.</summary>
    public double FloorYds { get; init; } = 1.5;

    /// <summary>Long shots spread more than their share: past 150 yds the share grows by this much per 100 yds.</summary>
    public double LongGrowth { get; init; } = 0.5;

    /// <summary>The longest shot from a fairway lie (carry plus release, yds); further away the golfer advances.</summary>
    public double MaxCarry { get; init; } = 250;

    /// <summary>The longest shot out of scrub (a recovery), yds.</summary>
    public double RecoveryCarry { get; init; } = 90;

    /// <summary>The Tour driver the tee anchors imply (only used to fit the anchors; Power Drive and Normal set real drives).</summary>
    public double TourDriveCarry { get; init; } = 280;
    public double TourDriveCarrySd { get; init; } = 12;
    public double TourDriveSideSd { get; init; } = 16;

    /// <summary>Multiplies every spread: 1 = the fitted golfer; above 1 tries the hole for a weaker player.</summary>
    public double Skill { get; init; } = 1.0;

    /// <summary>Distance and side spread (yds SD) for a shot of this plays-like length with this lie multiplier.</summary>
    public (double Dist, double Side) Spread(double playsLike, double mult)
    {
        double grow = 1 + LongGrowth * Math.Max(0, playsLike - 150) / 100;
        double k = mult * Skill;
        return (k * Math.Sqrt(FloorYds * FloorYds + Math.Pow(DistPct * playsLike * grow, 2)),
                k * Math.Sqrt(0.64 * FloorYds * FloorYds + Math.Pow(SidePct * playsLike * grow, 2)));
    }

    public double Mult(Ground lie, bool fromTee) => fromTee ? TeeMult : lie switch
    {
        Ground.Fairway or Ground.Green => 1.0,
        Ground.Rough => RoughMult,
        Ground.Bunker => SandMult,
        Ground.Scrub => RecoveryMult,
        _ => HeavyMult,
    };

    public double MaxFrom(Ground lie, bool fromTee) => fromTee ? MaxCarry * 1.05 : lie switch
    {
        Ground.Scrub => RecoveryCarry,
        Ground.Bunker => MaxCarry * 0.75,
        Ground.Rough => MaxCarry * 0.92,
        Ground.Fairway or Ground.Green => MaxCarry,
        _ => MaxCarry * 0.8,
    };
}

/// <summary>Release and height rules for shots to the green (PLACEHOLDER: from playtest carry and total later).</summary>
public sealed record ShotRules
{
    /// <summary>Planned release (roll after landing) as a share of the plays-like distance on longer shots.</summary>
    public double ReleasePct { get; init; } = 0.05;
    public double ReleaseMin { get; init; } = 1;
    public double ReleaseMax { get; init; } = 14;

    /// <summary>Short shots release more: a chip of d yds plans to roll (ChipShare − d/100) of its length.</summary>
    public double ChipShare { get; init; } = 0.45;

    /// <summary>Plays-like distance changes this many yards per foot of rise to the target.</summary>
    public double YardsPerFootRise { get; init; } = 1 / 3.0;

    public double Release(double playsLike) =>
        Math.Clamp(Math.Max(ReleasePct * playsLike, (ChipShare - playsLike / 100) * playsLike), ReleaseMin, ReleaseMax);
}

/// <summary>Everything a shot to the green needs on one hole.</summary>
public sealed class ShotContext
{
    public required HoleModel Hole { get; init; }
    public required SurfaceGrid Grid { get; init; }
    public required GroundTable Ground { get; init; }

    /// <summary>Height and slope: the green model on and round the green, the fairway terrain (or flat) elsewhere.</summary>
    public required ITerrainModel Terrain { get; init; }
    public required GolferParams Golfer { get; init; }
    public required ShotRules Rules { get; init; }
    public required IBaseline Baseline { get; init; }
    public required LieMap Lies { get; init; }
    public required PuttingModel Putting { get; init; }
    public required GreenSurface Green { get; init; }
    // Maps are read from many threads at once and built under a lock, once each (another step may be building too).
    public System.Collections.Concurrent.ConcurrentDictionary<int, PuttingMap> PuttingMaps { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<int, ShortGameMap> ShortMaps { get; } = new();
    readonly object _building = new();

    public PuttingMap PuttingMap(int pin)
    {
        if (PuttingMaps.TryGetValue(pin, out var m)) return m;
        lock (_building) return PuttingMaps.TryGetValue(pin, out m) ? m : PuttingMaps[pin] = HoleSim.PuttingMap.Build(Green, Putting, pin, Hole.Pins[pin - 1].Frame);
    }

    /// <summary>The same hole with another golfer: putting maps carry over (they don't depend on the golfer), short-game maps don't.</summary>
    public ShotContext WithGolfer(GolferParams golfer)
    {
        var c = new ShotContext { Hole = Hole, Grid = Grid, Ground = Ground, Terrain = Terrain, Golfer = golfer, Rules = Rules, Baseline = Baseline, Lies = Lies, Putting = Putting, Green = Green };
        foreach (var (k, v) in PuttingMaps) c.PuttingMaps[k] = v;
        return c;
    }

    public ShortGameMap ShortMap(int pin)
    {
        if (ShortMaps.TryGetValue(pin, out var m)) return m;
        var putting = PuttingMap(pin);   // first, so the short game's workers find it without the lock
        lock (_building) return ShortMaps.TryGetValue(pin, out m) ? m : ShortMaps[pin] = ShortGameMap.Build(this, pin);
    }
}

/// <summary>Level ground everywhere (the calibration hole, and holes with no terrain allowed).</summary>
public sealed class FlatTerrain : ITerrainModel
{
    public static FlatTerrain Instance { get; } = new();
    public string Id => "flat";
    public string Name => "Flat";
    public double HeightFt(FramePoint p) => 0;
    public FramePoint Slope(FramePoint p) => new(0, 0);
}

/// <summary>The green model inside the green's raster (green plus 15 yds), the fairway model (or flat ground) outside it.</summary>
/// <remarks>The green's raster holds heights relative to the green point; they are put back on the fairway model's datum.
/// With no fairway model, heights are relative to the green point, so plays-like distances see only the green's own contours.</remarks>
public sealed class CompositeTerrain(GreenSurface green, ITerrainModel? fairway) : ITerrainModel
{
    readonly double _datum = -(fairway?.HeightFt(green.GreenPoint) ?? 0);
    public string Id => $"{green.TerrainId}+{fairway?.Id ?? "flat"}";
    public string Name => fairway is null ? $"{green.TerrainName} on the green, flat elsewhere" : $"{green.TerrainName} on the green, {fairway.Name} elsewhere";
    public double HeightFt(FramePoint p) => green.InRaster(p) ? green.HeightFt(p) - _datum : fairway?.HeightFt(p) ?? 0;
    public FramePoint Slope(FramePoint p) => green.InRaster(p) ? green.Slope(p) : fairway?.Slope(p) ?? new FramePoint(0, 0);
}

public readonly record struct ShotOutcome(FramePoint Landing, FramePoint Finish, Ground Lie, double After, int Penalty);

/// <summary>
/// One shot at the green, and what it leaves. The golfer aims at a point, works out the plays-like distance (height
/// to the target counts), plans a release and carries the ball short by that much, with a spread by distance and lie.
/// The landing speed is set so the ball would roll the planned release on a flat green; on the real ground it bounces
/// and rolls with the slopes, so a ball landing into a slope holds and one on a downslope runs on.
/// </summary>
public static class ShotEngine
{
    const double HopOnGreen = 0.6;   // metres: the short-shot first bounce (40% of the green's 1.5 m hop)

    public static ShotOutcome Shoot(ShotContext c, FramePoint from, FramePoint aim, int pin, Random r, bool fromTee = false, bool value = true, Ground? lieOverride = null)
    {
        var lie = fromTee ? Ground.Fairway : lieOverride ?? c.Grid.At(from);
        var dv = aim - from; double d = Math.Max(dv.Length, 0.5);
        var dir = dv * (1 / d); var right = new FramePoint(dir.Along, -dir.Lat);
        double plays = Math.Max(1, d + (c.Terrain.HeightFt(aim) - c.Terrain.HeightFt(from)) * c.Rules.YardsPerFootRise);
        double release = c.Rules.Release(plays);
        double reach = c.Golfer.MaxFrom(lie, fromTee);
        double carryGround = Math.Min(d - release, reach - release);   // can't carry further than the club allows
        var (sdD, sdS) = c.Golfer.Spread(plays, c.Golfer.Mult(lie, fromTee));
        var land = from + dir * (carryGround + Normal(r) * sdD) + right * (Normal(r) * sdS);
        double mu = c.Ground.For(Ground.Green).Friction;
        double speed = Math.Sqrt(2 * mu * GreenPhysics.Gravity * Math.Max(0.05, release * HoleFrame.Yard - HopOnGreen));
        var fin = GeometryRun.Roll(c.Grid, c.Ground, land, dir, speed, c.Terrain);
        var g = c.Grid.At(fin);
        if (!value) return new ShotOutcome(land, fin, g, 0, 0);
        var (after, penalty) = After(c, fin, g, pin);
        return new ShotOutcome(land, fin, g, after, penalty);
    }

    /// <summary>Expected strokes left from a finish: putts on the green, the short-game map round it, the baseline beyond.</summary>
    public static (double After, int Penalty) After(ShotContext c, FramePoint fin, Ground g, int pin)
    {
        if (g == Ground.Green && c.Green.OnGreen(fin)) return (c.PuttingMap(pin).At(fin, c.Putting), 0);
        if (g != Ground.Water && g != Ground.Scrub && c.ShortMaps.TryGetValue(pin, out var sm) && sm.At(fin) is { } e) return (e, 0);
        return (BaselineAfter(c, fin, g, pin), g == Ground.Water ? c.Lies.WaterPenalty : 0);
    }

    /// <summary>Beyond the short game: the baseline's value for the lie at that distance (the nearest row if outside the table).</summary>
    public static double BaselineAfter(ShotContext c, FramePoint fin, Ground g, int pin)
    {
        var (lie, penalty) = c.Lies.For(g);
        if (lie == Lie.Green) lie = Lie.Fairway;      // the green next door, or the fringe: play it as a short fairway shot
        double yds = fin.DistanceTo(c.Hole.Pins[pin - 1].Frame);
        return penalty + c.Baseline.ExpectedStrokes(lie, yds).Value;
    }

    internal static double Normal(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
}

/// <summary>
/// Expected strokes from every chip-and-pitch spot within <see cref="Reach"/> yds of the green to one pin: a short
/// shot with the best of a few aims, then the putting map (or the baseline for a chip that misses the green).
/// </summary>
public sealed class ShortGameMap
{
    public const int Reach = 35;
    readonly Dictionary<(int, int), double> _cells;
    public int Pin { get; }
    public IReadOnlyDictionary<(int, int), double> Cells => _cells;

    ShortGameMap(int pin, Dictionary<(int, int), double> cells) { Pin = pin; _cells = cells; }

    public double? At(FramePoint p)
    {
        var k = GreenSurface.Key(p);
        if (_cells.TryGetValue(k, out var v)) return v;
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (_cells.TryGetValue((k.Item1 + dx, k.Item2 + dy), out v)) return v;
        return null;
    }

    public static ShortGameMap Build(ShotContext c, int pin, int samples = 32, CancellationToken cancel = default)
    {
        var grid = c.Grid; var pinAt = c.Hole.Pins[pin - 1].Frame; var putting = c.PuttingMap(pin);
        // Off-green cells within reach of this green (chessboard distance on the grid), not scrub or water.
        int w = grid.Width, h = grid.Height;
        var dist = new int[w * h]; Array.Fill(dist, int.MaxValue); var q = new Queue<int>();
        foreach (var cell in c.Green.Cells)
        {
            int x = (int)Math.Floor(cell.Lat - grid.LatMin), y = (int)Math.Floor(cell.Along - grid.AlongMin);
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            dist[y * w + x] = 0; q.Enqueue(y * w + x);
        }
        while (q.Count > 0)
        {
            int k = q.Dequeue(), x = k % w, y = k / w; if (dist[k] >= Reach) continue;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                int kk = yy * w + xx; if (dist[kk] <= dist[k] + 1) continue; dist[kk] = dist[k] + 1; q.Enqueue(kk);
            }
        }
        var spots = new List<FramePoint>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int k = y * w + x; if (dist[k] == 0 || dist[k] > Reach) continue;
            var g = grid[x, y]; if (g is Ground.Scrub or Ground.Water or Ground.Green) continue;
            spots.Add(grid.CellCentre(x, y));
        }
        var values = new double[spots.Count];
        Parallel.For(0, spots.Count, new ParallelOptions { CancellationToken = cancel }, i =>
        {
            var from = spots[i]; var dv = pinAt - from; double len = Math.Max(dv.Length, 0.5);
            var dir = dv * (1 / len); var right = new FramePoint(dir.Along, -dir.Lat);
            double best = double.MaxValue; FramePoint bestAim = pinAt;
            foreach (var off in new[] { new FramePoint(0, 0), new FramePoint(0, -2), new FramePoint(0, 2), new FramePoint(-2, 0), new FramePoint(2, 0) })
            {
                var aim = pinAt + right * off.Lat + dir * off.Along;
                double e = Mean(c, from, aim, pin, 12, i * 7 + 1);
                if (e < best) { best = e; bestAim = aim; }
            }
            values[i] = Mean(c, from, bestAim, pin, samples, i * 7 + 3);
        });
        var cells = new Dictionary<(int, int), double>();
        for (int i = 0; i < spots.Count; i++) cells[GreenSurface.Key(spots[i])] = values[i];
        return new ShortGameMap(pin, cells);

        static double Mean(ShotContext c, FramePoint from, FramePoint aim, int pin, int n, int seed)
        {
            var r = new Random(seed); double sum = 0; var putting = c.PuttingMap(pin);
            for (int k = 0; k < n; k++)
            {
                var o = ShotEngine.Shoot(c, from, aim, pin, r, value: false);
                double after = o.Lie == Ground.Green && c.Green.OnGreen(o.Finish) ? putting.At(o.Finish, c.Putting)
                    : ShotEngine.BaselineAfter(c, o.Finish, o.Lie, pin);
                sum += 1 + after;
            }
            return sum / n;
        }
    }
}

/// <summary>The result of shots from one spot to one aim.</summary>
public sealed record ApproachResult(
    FramePoint From, Ground Lie, FramePoint Aim, int Pin, double Yards, double PlaysLike, int Samples,
    double Expected, double SgCi95, double? StrokesGained, Coverage StartCoverage,
    double GreenHit, double ProximityFt, double[] ShareByGround, double Short, double Long, double Left, double Right,
    IReadOnlyList<ShotOutcome> Shots)
{
    public double Share(Ground g) => ShareByGround[(int)g];
}

public sealed record AimOption(FramePoint Offset, FramePoint Aim, double Expected, double GreenHit);

/// <summary>Approach shots (and par 3 tee shots) from a spot: one aim, or a sweep of aims round the pin to find the best.</summary>
public static class ApproachSim
{
    /// <summary>Shots from <paramref name="from"/> at <paramref name="aim"/>; the same draws for any aim with the same seed.</summary>
    public static ApproachResult Run(ShotContext c, FramePoint from, FramePoint aim, int pin, int n = 600, int seed = 7, bool fromTee = false, Ground? lieOverride = null)
    {
        c.ShortMap(pin);   // build the short game first so misses are valued by it
        var pinAt = c.Hole.Pins[pin - 1].Frame;
        var lie = fromTee ? Ground.Fairway : lieOverride ?? c.Grid.At(from);
        var shots = new ShotOutcome[n];
        for (int i = 0; i < n; i++) shots[i] = ShotEngine.Shoot(c, from, aim, pin, new Random(unchecked(seed * 1_000_003 + i)), fromTee, lieOverride: lieOverride);
        var values = shots.Select(s => 1 + s.After + s.Penalty).ToArray();
        double mean = values.Average(), sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, n - 1));
        var counts = new double[Grounds.All.Count];
        foreach (var s in shots) counts[(int)s.Lie] += 1.0 / n;
        var on = shots.Where(s => s.Lie == Ground.Green && c.Green.OnGreen(s.Finish)).ToList();
        var dirToPin = (pinAt - from) * (1 / Math.Max((pinAt - from).Length, 0.5)); var right = new FramePoint(dirToPin.Along, -dirToPin.Lat);
        double shortS = 0, longS = 0, leftS = 0, rightS = 0;
        foreach (var s in shots.Where(s => !(s.Lie == Ground.Green && c.Green.OnGreen(s.Finish))))
        {
            var rel = s.Finish - pinAt; double along = rel.Lat * dirToPin.Lat + rel.Along * dirToPin.Along, side = rel.Lat * right.Lat + rel.Along * right.Along;
            if (Math.Abs(along) >= Math.Abs(side)) { if (along < 0) shortS++; else longS++; } else { if (side < 0) leftS++; else rightS++; }
        }
        double yards = from.DistanceTo(pinAt);
        double plays = yards + (c.Terrain.HeightFt(pinAt) - c.Terrain.HeightFt(from)) * c.Rules.YardsPerFootRise;
        // Strokes gained for the shot: E(start) − (1 + what it leaves), with E(start) from the baseline for the start lie.
        var (startLie, _) = fromTee ? (Lie.Tee, 0) : c.Lies.For(lie);
        var start = c.Baseline.ExpectedStrokes(startLie == Lie.Green ? Lie.Fairway : startLie, yards);
        double? sg = start.Covered ? start.Value - mean : null;
        return new ApproachResult(from, lie, aim, pin, yards, plays, n, mean, 1.96 * sd / Math.Sqrt(n), sg, start.Coverage,
            on.Count / (double)n, on.Count == 0 ? double.NaN : on.Average(s => s.Finish.DistanceTo(pinAt) * 3), counts,
            shortS / n, longS / n, leftS / n, rightS / n, shots);
    }

    /// <summary>
    /// Aims on a <paramref name="size"/> × <paramref name="size"/> grid round the pin, <paramref name="spacing"/> yds apart
    /// in the shot's own frame (right of and beyond the pin), each with the same draws; best first.
    /// </summary>
    public static IReadOnlyList<AimOption> Sweep(ShotContext c, FramePoint from, int pin, int n = 200, int seed = 11, int size = 5, double spacing = 3, bool fromTee = false, Ground? lieOverride = null, bool parallel = true)
    {
        c.ShortMap(pin);
        var pinAt = c.Hole.Pins[pin - 1].Frame;
        var dir = (pinAt - from) * (1 / Math.Max((pinAt - from).Length, 0.5)); var right = new FramePoint(dir.Along, -dir.Lat);
        int half = size / 2;
        var offsets = new List<FramePoint>();
        for (int i = -half; i <= half; i++) for (int j = -half; j <= half; j++) offsets.Add(new FramePoint(i * spacing, j * spacing));
        var options = new AimOption[offsets.Count];
        Parallel.For(0, offsets.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel ? -1 : 1 }, k =>
        {
            var o = offsets[k]; var aim = pinAt + right * o.Lat + dir * o.Along;
            double sum = 0; int on = 0;
            for (int i = 0; i < n; i++)
            {
                var s = ShotEngine.Shoot(c, from, aim, pin, new Random(unchecked(seed * 1_000_003 + i)), fromTee, lieOverride: lieOverride);
                sum += 1 + s.After + s.Penalty; if (s.Lie == Ground.Green && c.Green.OnGreen(s.Finish)) on++;
            }
            options[k] = new AimOption(o, aim, sum / n, on / (double)n);
        });
        return options.OrderBy(o => o.Expected).ThenBy(o => o.Offset.Length).ToList();
    }
}

/// <summary>Puts together everything an approach run needs on a real hole, with the golfer fitted (or read from the cache).</summary>
public static class ApproachSetup
{
    public static (ShotContext Context, GolferFit Fit) Build(HoleModel hole, GreenSurface green, ITerrainModel? fairway, AssumptionSheet sheet, IBaseline baseline,
        double stimp, string? cacheFolder = null, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var putting = PuttingModel.Create(stimp, baseline, sheet.Putting());
        var ground = sheet.GroundRules(putting.Friction);
        var rules = sheet.ShotRules(); var lies = sheet.Lies();
        var fit = GolferCalibration.Fit(baseline, putting, ground, rules, lies, sheet.GolferStart(), cacheFolder, 300, progress, cancel);
        var c = new ShotContext
        {
            Hole = hole, Grid = hole.Grid, Ground = ground, Terrain = new CompositeTerrain(green, fairway), Golfer = fit.Golfer, Rules = rules,
            Baseline = baseline, Lies = lies, Putting = putting, Green = green,
        };
        return (c, fit);
    }

    /// <summary>The spot on the line of play <paramref name="yards"/> from the pin, moved <paramref name="side"/> yds right (+) or left (−) of the line.</summary>
    public static FramePoint SpotOnRoute(HoleModel hole, int pin, double yards, double side = 0)
    {
        var pinAt = hole.Pins[Math.Clamp(pin - 1, 0, hole.Pins.Count - 1)].Frame;
        var route = hole.Route;
        // Walk back from the green: the first point at least that far from the pin.
        for (int i = route.Count - 1; i > 0; i--)
        {
            var a = route[i - 1]; var b = route[i]; double len = a.DistanceTo(b);
            for (double s = len; s >= 0; s -= 0.25)
            {
                var p = a + (b - a) * (s / Math.Max(len, 1e-9));
                if (p.DistanceTo(pinAt) < yards) continue;
                var dir = (b - a) * (1 / Math.Max(len, 1e-9));
                return p + new FramePoint(dir.Along, -dir.Lat) * side;
            }
        }
        return route[0];
    }
}
