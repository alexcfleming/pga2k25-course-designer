using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pga2K25.CourseFormat;

namespace Pga2K25.HoleSim;

/// <summary>One baseline anchor against the fitted golfer's play-out from the same lie and distance.</summary>
public sealed record CalibrationRow(Lie Lie, double Yards, double Anchor, double Model)
{
    public double Error => Model - Anchor;
}

/// <summary>The fitted golfer and how well it matches each anchor.</summary>
public sealed record GolferFit(GolferParams Golfer, IReadOnlyList<CalibrationRow> Rows, double RmsError, int Evaluations, string Key, string BaselineId)
{
    public double MaxError => Rows.Count == 0 ? 0 : Rows.Max(r => Math.Abs(r.Error));
}

/// <summary>
/// Fits the simulated golfer to the baseline: on a plain calibration hole (flat, a 34-yd fairway, rough either side, a
/// round 26-yd green with the pin in the middle) the golfer plays out from every non-green anchor's lie and distance,
/// shot after shot until it holes out with the fitted putter, and its spreads, lie penalties and reach are adjusted
/// (Nelder–Mead, same random draws every try) until the averages match the anchors. Only the baseline's own rows are
/// used, never values between them. Fits are kept in memory and, given a folder, on disk, keyed by everything they depend on.
/// </summary>
public static class GolferCalibration
{
    public const int Version = 1;
    const double Length = 600, GreenRadius = 13, FairwayHalf = 17;
    static readonly ConcurrentDictionary<string, GolferFit> Memory = new();

    /// <summary>Plausible ranges for the fitted values, in the fit's order.</summary>
    static readonly (double Lo, double Hi)[] Bounds =
    [
        (0.02, 0.10),   // distance spread, share of plays-like
        (0.5, 5),       // spread floor, yds
        (1, 3),         // rough
        (1, 3.5),       // sand
        (1.5, 4),       // recovery
        (0.7, 1.5),     // tee
        (200, 300),     // longest fairway shot, yds
        (240, 320),     // Tour drive carry, yds
        (0.02, 1.5),    // long-shot growth
        (40, 200),      // longest recovery, yds
    ];

    public static string KeyFor(IBaseline baseline, PuttingModel putting, GroundTable ground, ShotRules rules, GolferParams start, int samples)
    {
        var sb = new StringBuilder();
        sb.Append(Version).Append('|').Append(baseline.Id).Append('|');
        if (baseline is TableBaseline tb) foreach (var r in tb.Rows) sb.Append(FormattableString.Invariant($"{r.Lie}:{r.Distance}:{r.Expected};"));
        sb.Append(FormattableString.Invariant($"|{putting.Friction:F5}|{putting.Skill.AimSdRadians:F5}|{putting.Skill.PaceSd:F5}|{putting.Settings}"));
        sb.Append(FormattableString.Invariant($"|{ground.LandingSpeed}"));
        foreach (var (g, p) in ground.ByGround.OrderBy(kv => kv.Key)) sb.Append(FormattableString.Invariant($"|{g}:{p.Friction}:{p.HopMetres}:{p.Kept}"));
        sb.Append('|').Append(rules).Append('|').Append(start).Append('|').Append(samples);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    /// <summary>The cached fit if there is one (memory, then <paramref name="cacheFolder"/>), else null.</summary>
    public static GolferFit? Cached(string key, string? cacheFolder)
    {
        if (Memory.TryGetValue(key, out var f)) return f;
        if (cacheFolder is null) return null;
        var path = Path.Combine(cacheFolder, $"golfer-fit-{key}.json");
        try
        {
            if (!File.Exists(path)) return null;
            var fit = JsonSerializer.Deserialize<GolferFit>(File.ReadAllText(path));
            if (fit is null || fit.Key != key) return null;
            return Memory[key] = fit;
        }
        catch (Exception) { return null; }
    }

    public static GolferFit Fit(IBaseline baseline, PuttingModel putting, GroundTable ground, ShotRules rules, LieMap lies, GolferParams? start = null,
        string? cacheFolder = null, int samples = 300, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        start ??= new GolferParams();
        string key = KeyFor(baseline, putting, ground, rules, start, samples);
        if (Cached(key, cacheFolder) is { } hit) return hit;

        var targets = baseline is TableBaseline tb
            ? tb.Rows.Where(r => r.Lie != Lie.Green).Select(r => (r.Lie, Yards: r.Distance, E: r.Expected)).ToList()
            : [];
        if (targets.Count == 0) throw new InvalidOperationException($"Baseline {baseline.Id} has no rows off the green to fit the golfer to.");

        var basis = Synthetic(baseline, putting, ground, rules, lies, start);
        var puttMap = basis.PuttingMap(1);
        double ratio = start.SidePct / start.DistPct;
        GolferParams From(double[] x) => start with
        {
            DistPct = Math.Exp(x[0]), SidePct = Math.Exp(x[0]) * ratio, FloorYds = Math.Exp(x[1]),
            RoughMult = Math.Exp(x[2]), SandMult = Math.Exp(x[3]), RecoveryMult = Math.Exp(x[4]), TeeMult = Math.Exp(x[5]),
            MaxCarry = Math.Exp(x[6]), TourDriveCarry = Math.Exp(x[7]), LongGrowth = Math.Exp(x[8]), RecoveryCarry = Math.Exp(x[9]),
        };
        double[] x0 =
        [
            Math.Log(start.DistPct), Math.Log(start.FloorYds), Math.Log(start.RoughMult), Math.Log(start.SandMult),
            Math.Log(start.RecoveryMult), Math.Log(start.TeeMult), Math.Log(start.MaxCarry), Math.Log(start.TourDriveCarry),
            Math.Log(start.LongGrowth), Math.Log(start.RecoveryCarry),
        ];
        int evals = 0;
        double Objective(double[] x)
        {
            cancel.ThrowIfCancellationRequested();
            // Keep the golfer plausible: outside these ranges the fit is pushed back, however well it matches.
            double outside = 0;
            for (int i = 0; i < x.Length; i++)
            {
                var (lo, hi) = Bounds[i]; double v = Math.Exp(x[i]);
                if (v < lo) outside += Math.Pow(Math.Log(lo / v), 2); else if (v > hi) outside += Math.Pow(Math.Log(v / hi), 2);
            }
            var model = Evaluate(basis.WithGolfer(From(x)), puttMap, targets, samples, 1);
            evals++;
            if (evals % 20 == 0) progress?.Report($"Fitting the golfer to the anchors: try {evals}");
            double err = 0; for (int i = 0; i < targets.Count; i++) err += (model[i] - targets[i].E) * (model[i] - targets[i].E);
            return err / targets.Count + 10 * outside;
        }
        // Two passes: the second restarts the simplex round the first one's best, which gets it out of a narrow valley.
        var best = NelderMead(Objective, x0, 0.2, 260);
        best = NelderMead(Objective, best, 0.08, 200);
        var golfer = From(best);
        // Report on fresh draws (twice as many), so the residuals aren't flattered by the draws the fit saw.
        var final = Evaluate(basis.WithGolfer(golfer), puttMap, targets, samples * 2, 2);
        var rows = targets.Select((t, i) => new CalibrationRow(t.Lie, t.Yards, t.E, final[i])).ToList();
        var fit = new GolferFit(golfer, rows, Math.Sqrt(rows.Average(r => r.Error * r.Error)), evals, key, baseline.Id);
        Memory[key] = fit;
        if (cacheFolder is not null)
        {
            try { Directory.CreateDirectory(cacheFolder); File.WriteAllText(Path.Combine(cacheFolder, $"golfer-fit-{key}.json"), JsonSerializer.Serialize(fit, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception) { /* a cache that can't be written just means fitting again next time */ }
        }
        return fit;
    }

    /// <summary>Average strokes to hole out from each target's lie and distance on the calibration hole.</summary>
    public static double[] Evaluate(ShotContext c, PuttingMap putts, IReadOnlyList<(Lie Lie, double Yards, double E)> targets, int samples, int seedBase)
    {
        var sums = new double[targets.Count * samples];
        var draws = Draws(seedBase, targets.Count, samples);
        Parallel.For(0, sums.Length, k =>
            sums[k] = PlayOut(c, putts, targets[k / samples].Lie, targets[k / samples].Yards, draws.AsSpan(k * DrawsPerPlayOut, DrawsPerPlayOut)));
        var means = new double[targets.Count];
        for (int row = 0; row < targets.Count; row++)
        {
            double s = 0; for (int i = 0; i < samples; i++) s += sums[row * samples + i];
            means[row] = s / samples;
        }
        return means;
    }

    /// <summary>
    /// Strokes to hole out from a lie and distance on the calibration hole: driver from a tee beyond reach (the Tour
    /// drive), at the pin when it is in reach, otherwise a lay-up that leaves at least 40 yds; then putts.
    /// </summary>
    public static double PlayOut(ShotContext c, PuttingMap putts, Lie lie, double yards, int seed)
    {
        Span<double> draws = stackalloc double[DrawsPerPlayOut];
        FillDraws(seed, draws);
        return PlayOut(c, putts, lie, yards, draws);
    }

    /// <summary>A play-out takes at most twelve strokes, and each stroke two draws (distance or carry, then side).</summary>
    const int MaxStrokes = 12, DrawsPerPlayOut = 2 * MaxStrokes;

    static void FillDraws(int seed, Span<double> draws)
    {
        var r = new Random(seed);
        for (int j = 0; j < draws.Length; j++) draws[j] = ShotDraws.Normal(r);
    }

    sealed record DrawTable(int SeedBase, int Rows, int Samples, double[] Normals);
    static volatile DrawTable? _lastDraws;

    /// <summary>
    /// The draws of every play-out of an evaluation, row by row. A fit evaluates hundreds of golfers on the same
    /// numbered play-outs, so the draws are made once and the last table is kept.
    /// </summary>
    static double[] Draws(int seedBase, int rows, int samples)
    {
        if (_lastDraws is { } t && t.SeedBase == seedBase && t.Rows == rows && t.Samples == samples) return t.Normals;
        var normals = new double[rows * samples * DrawsPerPlayOut];
        Parallel.For(0, rows * samples, k =>
            FillDraws(unchecked(seedBase * 7_919_993 + k / samples * 100_003 + k % samples), normals.AsSpan(k * DrawsPerPlayOut, DrawsPerPlayOut)));
        _lastDraws = new DrawTable(seedBase, rows, samples, normals);
        return normals;
    }

    static double PlayOut(ShotContext c, PuttingMap putts, Lie lie, double yards, ReadOnlySpan<double> draws)
    {
        int next = 0; var g = c.Golfer;
        var pinAt = c.Hole.Pins[0].Frame;
        var pos = new FramePoint(0, pinAt.Along - yards);
        Ground? over = lie switch { Lie.Sand => Ground.Bunker, Lie.Recovery => Ground.Scrub, Lie.Rough => Ground.Rough, _ => Ground.Fairway };
        bool tee = lie == Lie.Tee;
        int strokes = 0;
        for (int k = 0; k < MaxStrokes; k++)
        {
            var here = over ?? c.Grid.At(pos);
            double first = draws[next++], second = draws[next++];
            if (here == Ground.Green && c.Green.OnGreen(pos)) return strokes + putts.At(pos, c.Putting);
            double d = Math.Max(pos.DistanceTo(pinAt), 0.5);
            var dir = (pinAt - pos) * (1 / d);
            FramePoint fin;
            if (tee && d > g.MaxFrom(Ground.Fairway, true) + 20)
            {
                var right = new FramePoint(dir.Along, -dir.Lat);
                double carry = Math.Min(g.TourDriveCarry + first * g.TourDriveCarrySd * g.Skill, d - 30);
                var land = pos + dir * carry + right * (second * g.TourDriveSideSd * g.Skill);
                fin = GeometryRun.Roll(c.Grid, c.Ground, land, dir, c.Ground.LandingSpeed, c.Terrain);
            }
            else
            {
                double reach = g.MaxFrom(here, tee);
                var aim = d <= reach ? pinAt : pos + dir * Math.Min(reach, d - 40);
                fin = ShotEngine.Shoot(c, pos, aim, 1, first, second, tee, value: false, lieOverride: over).Finish;
            }
            strokes++; pos = fin; over = null; tee = false;
        }
        return strokes + ShotEngine.BaselineAfter(c, pos, c.Grid.At(pos), 1);
    }

    /// <summary>The calibration hole: flat, fairway down the middle, rough beyond, a round green with the pin in its centre.</summary>
    /// <param name="terrain">Ground heights for the green (tests tilt it); flat when null.</param>
    /// <param name="extra">More surfaces (tests add water); painted in ground order, so the fairway and green cover water.</param>
    public static ShotContext Synthetic(IBaseline baseline, PuttingModel putting, GroundTable ground, ShotRules rules, LieMap lies, GolferParams golfer, ITerrainModel? terrain = null,
        Func<Func<double, double, Vec2>, IEnumerable<PaintShape>>? extra = null)
    {
        var frame = new HoleFrame(new Vec2(0, 0), new Vec2(0, 1));
        Vec2 W(double lat, double along) => frame.ToWorld(new FramePoint(lat, along));
        var shapes = new List<PaintShape>
        {
            new PaintPolygon(Ground.Rough, [W(-150, -30), W(150, -30), W(150, Length + 60), W(-150, Length + 60)]),
            new PaintPolygon(Ground.Fairway, [W(-FairwayHalf, -30), W(FairwayHalf, -30), W(FairwayHalf, Length - GreenRadius), W(-FairwayHalf, Length - GreenRadius)]),
            new PaintDisc(Ground.Green, W(0, Length), GreenRadius * HoleFrame.Yard),
        };
        if (extra is not null) shapes.AddRange(extra(W));
        var grid = SurfaceGrid.Build(frame, -150, 150, -30, (int)Length + 60, shapes);
        var pin = new FramePoint(0, Length);
        var hole = new HoleModel
        {
            CourseName = "Calibration hole", Stamp = null, Number = 0, Par = 4,
            Tees = [new TeeInfo(0, "Calibration", (int)Length, W(0, 0), 0)], TeeIndex = 0,
            Frame = frame, Green = W(0, Length), GreenFrame = pin,
            Pins = [new PinInfo(1, W(0, Length), pin, 0, 0)],
            AimMarker = W(0, 300), AimFrame = new FramePoint(0, 300), HasAimMarker = false,
            Route = [new FramePoint(0, 0), pin], Widths = [],
            GreenDepthYards = (int)(2 * GreenRadius), GreenFrontYards = (int)(Length - GreenRadius), GreenBackYards = (int)(Length + GreenRadius),
            Hazards = [], UnknownSurfaces = [], Treatments = new Dictionary<int, Ground?>(), Grid = grid, Settings = [], GreenSpeed = null,
            HeightStamps = [], SurfaceSignatures = [],
        };
        var green = GreenSurface.Build(hole, terrain ?? FlatTerrain.Instance);
        return new ShotContext
        {
            Hole = hole, Grid = grid, Ground = ground, Terrain = new CompositeTerrain(green, null), Golfer = golfer, Rules = rules,
            Baseline = baseline, Lies = lies, Putting = putting, Green = green,
        };
    }

    static double[] NelderMead(Func<double[], double> f, double[] x0, double step, int maxEvals)
    {
        int n = x0.Length;
        var pts = new double[n + 1][]; var vals = new double[n + 1];
        pts[0] = (double[])x0.Clone(); vals[0] = f(pts[0]);
        for (int i = 0; i < n; i++) { pts[i + 1] = (double[])x0.Clone(); pts[i + 1][i] += step; vals[i + 1] = f(pts[i + 1]); }
        int evals = n + 1;
        while (evals < maxEvals)
        {
            var order = Enumerable.Range(0, n + 1).OrderBy(i => vals[i]).ToArray();
            pts = order.Select(i => pts[i]).ToArray(); vals = order.Select(i => vals[i]).ToArray();
            if (vals[n] - vals[0] < 1e-6) break;
            var centroid = new double[n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) centroid[j] += pts[i][j] / n;
            double[] Along(double t) => centroid.Select((c, j) => c + t * (pts[n][j] - c)).ToArray();
            var xr = Along(-1); double fr = f(xr); evals++;
            if (fr < vals[0])
            {
                var xe = Along(-2); double fe = f(xe); evals++;
                if (fe < fr) { pts[n] = xe; vals[n] = fe; } else { pts[n] = xr; vals[n] = fr; }
            }
            else if (fr < vals[n - 1]) { pts[n] = xr; vals[n] = fr; }
            else
            {
                var xc = fr < vals[n] ? Along(-0.5) : Along(0.5); double fc = f(xc); evals++;
                if (fc < Math.Min(fr, vals[n])) { pts[n] = xc; vals[n] = fc; }
                else
                {
                    for (int i = 1; i <= n; i++) { pts[i] = pts[i].Select((v, j) => pts[0][j] + 0.5 * (v - pts[0][j])).ToArray(); vals[i] = f(pts[i]); evals++; }
                }
            }
        }
        int bestI = Array.IndexOf(vals, vals.Min());
        return pts[bestI];
    }
}
