using System.Collections.Concurrent;

namespace Pga2K25.HoleSim;

/// <summary>Putting rules the model doesn't know from the game (PLACEHOLDER values on the assumption sheet).</summary>
public sealed record PuttingSettings
{
    /// <summary>Hole radius (m): a regulation 4.25 in cup.</summary>
    public double HoleRadiusM { get; init; } = 0.054;

    /// <summary>A ball passing over the hole drops only when slower than this (m/s).</summary>
    public double CaptureSpeed { get; init; } = 1.3;

    /// <summary>Putts are struck to finish this far past the hole on a flat green (m).</summary>
    public double PastM { get; init; } = 0.45;

    /// <summary>A stopped ball stays put on slopes up to this multiple of the rolling friction.</summary>
    public double StaticFactor { get; init; } = 1.15;

    public double StepSeconds { get; init; } = 0.02;
}

/// <summary>The putter's two errors, fitted so a flat green reproduces the baseline's green anchors.</summary>
public sealed record PuttingSkill(double AimSdRadians, double PaceSd, IReadOnlyList<(double Feet, double Model, double Baseline)> Fit)
{
    public double AimSdDegrees => AimSdRadians * 180 / Math.PI;
}

/// <summary>Expected putts from one spot: the value, the read (aim offset across the line in metres, and pace factor).</summary>
public readonly record struct PuttValue(double Expected, double AimOffsetM, double Pace);

/// <summary>
/// Putts rolled on the green: friction from Stimp, gravity down the slope, a hole that captures slow balls. The
/// golfer reads each putt (the pace and aim that hole a perfect stroke, chosen by which does best under the golfer's
/// own errors), then strokes it with an aim error and a pace error. Pace error scales with the flat-green speed for
/// the distance, since feel is for distance, so a soft downhill putt carries more of it. A miss costs one more stroke
/// plus the expected putts left from where it stops, on a flat-green curve.
/// </summary>
public sealed class PuttingModel
{
    static readonly ConcurrentDictionary<string, PuttingSkill> FitCache = new();
    static readonly double[] Paces = [0.45, 0.55, 0.65, 0.75, 0.85, 0.95, 1.05, 1.15, 1.3, 1.5, 1.75];

    readonly double[] _flatMake = new double[121];   // make probability on a flat green by whole feet

    public double Friction { get; }
    public PuttingSettings Settings { get; }
    public PuttingSkill Skill { get; }

    PuttingModel(double friction, PuttingSettings settings, PuttingSkill skill)
    {
        Friction = friction; Settings = settings; Skill = skill;
        for (int f = 0; f <= 120; f++) _flatMake[f] = f == 0 ? 1 : FlatMake(f, skill.AimSdRadians, skill.PaceSd, f <= 15 ? 1500 : 400, f * 10);
    }

    /// <summary>A model for this green speed, its putter fitted to the baseline's green rows (cached per speed and baseline).</summary>
    public static PuttingModel Create(double stimpFeet, IBaseline baseline, PuttingSettings? settings = null)
    {
        settings ??= new PuttingSettings();
        double mu = GreenPhysics.RollingFriction(stimpFeet);
        string key = FormattableString.Invariant($"{baseline.Id}|{mu:F5}|{settings}");
        var skill = FitCache.GetOrAdd(key, _ => FitSkill(mu, settings, baseline));
        return new PuttingModel(mu, settings, skill);
    }

    static PuttingSkill FitSkill(double mu, PuttingSettings settings, IBaseline baseline)
    {
        // The baseline's own green rows (published points only, never values interpolated between them).
        var targets = baseline is TableBaseline tb
            ? tb.Rows.Where(r => r.Lie == Lie.Green).Select(r => (Ft: r.Distance, E: r.Expected)).ToList()
            : new List<(double Ft, double E)>();
        if (targets.Count == 0) throw new InvalidOperationException($"Baseline {baseline.Id} has no green rows to fit the putter to.");
        var probe = new PuttingModel(mu, settings, new PuttingSkill(0, 0, []), skipTable: true);
        double best = double.MaxValue, bestAim = 0.025, bestPace = 0.08;
        double Err(double aimDeg, double pace, int n)
        {
            double aim = aimDeg * Math.PI / 180, err = 0;
            foreach (var (ft, e) in targets)
            {
                // Same expected-putts rule the map uses: a miss leaves a short second putt (made almost always).
                double p = probe.FlatMake(ft, aim, pace, n, (int)(ft * 10));
                double second = 1 + (1 - probe.FlatMake(Math.Min(ft * 0.15 + 1.5, 6), aim, pace, n / 2, (int)(ft * 10) + 1));
                double modelE = 1 + (1 - p) * second;
                err += (modelE - e) * (modelE - e);
            }
            return err;
        }
        // A coarse sweep, then a finer one round the best pair.
        foreach (var aimDeg in new[] { 0.6, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0, 2.3, 2.6, 3.0 })
            foreach (var pace in new[] { 0.04, 0.06, 0.08, 0.10, 0.12, 0.15 })
            {
                double err = Err(aimDeg, pace, 500);
                if (err < best) { best = err; bestAim = aimDeg; bestPace = pace; }
            }
        double coarseAim = bestAim, coarsePace = bestPace; best = double.MaxValue;
        for (double a = coarseAim - 0.2; a <= coarseAim + 0.2001; a += 0.05)
            for (double pc = Math.Max(0.02, coarsePace - 0.02); pc <= coarsePace + 0.0201; pc += 0.005)
            {
                double err = Err(a, pc, 1500);
                if (err < best) { best = err; bestAim = a; bestPace = pc; }
            }
        bestAim *= Math.PI / 180;
        var fitted = new PuttingModel(mu, settings, new PuttingSkill(bestAim, bestPace, []));
        var report = targets.Select(t => (t.Ft, fitted.ExpectedFlat(t.Ft), t.E)).ToList();
        return new PuttingSkill(bestAim, bestPace, report);
    }

    PuttingModel(double friction, PuttingSettings settings, PuttingSkill skill, bool skipTable) { Friction = friction; Settings = settings; Skill = skill; }

    double FlatMake(double ft, double aimSd, double paceSd, int n, int seed)
    {
        var r = new Random(seed); int made = 0; var flat = FlatGreen.Instance;
        for (int i = 0; i < n; i++)
            if (Putt(flat, new FramePoint(0, 0), new FramePoint(0, ft / 3), 0, 1, Normal(r) * aimSd, Normal(r) * paceSd).Holed) made++;
        return made / (double)n;
    }

    public double FlatMakeProbability(double ft)
    {
        if (ft >= 120) return _flatMake[120];
        int a = (int)Math.Max(0, ft); double t = ft - a;
        return _flatMake[a] * (1 - t) + _flatMake[Math.Min(a + 1, 120)] * t;
    }

    /// <summary>Expected putts on a flat green from <paramref name="ft"/> feet: one, plus a likely second, plus a rare third.</summary>
    public double ExpectedFlat(double ft) => 1 + (1 - FlatMakeProbability(ft)) * (1 + (1 - FlatMakeProbability(Math.Min(ft * 0.15 + 1.5, 6))));

    /// <summary>Anything that can give a slope under the ball: the green raster, or a test plane.</summary>
    public interface ISlopeField { FramePoint Slope(FramePoint p); bool Inside(FramePoint p); }

    sealed class FlatGreen : ISlopeField
    {
        public static readonly FlatGreen Instance = new();
        public FramePoint Slope(FramePoint p) => new(0, 0);
        public bool Inside(FramePoint p) => true;
    }

    sealed class SurfaceField(GreenSurface s) : ISlopeField
    {
        public FramePoint Slope(FramePoint p) => s.Slope(p);
        public bool Inside(FramePoint p) => s.InRaster(p);
    }

    public static ISlopeField Field(GreenSurface surface) => new SurfaceField(surface);

    /// <summary>One putt from <paramref name="start"/> at <paramref name="hole"/> (yards in the hole frame).</summary>
    public (bool Holed, FramePoint Stop) Putt(ISlopeField field, FramePoint start, FramePoint hole, double aimOffsetM, double pace, double aimError, double paceError)
    {
        const double Y = HoleFrame.Yard, G = GreenPhysics.Gravity;
        double dt = Settings.StepSeconds, mu = Friction;
        var dv = hole - start; double len = Math.Max(dv.Length, 1e-6), dist = len * Y;
        var dir = dv * (1 / len); var right = new FramePoint(dir.Along, -dir.Lat);
        var aim = hole + right * (aimOffsetM / Y); var ad = aim - start; var u = ad * (1 / Math.Max(ad.Length, 1e-6));
        double c = Math.Cos(aimError), sn = Math.Sin(aimError);
        var u2 = new FramePoint(u.Lat * c - u.Along * sn, u.Lat * sn + u.Along * c);
        var v = u2 * (Math.Sqrt(2 * mu * G * (dist + Settings.PastM)) * Math.Max(0.05, pace + paceError));
        var p = start;
        for (int k = 0; k < 3000; k++)
        {
            var slope = field.Slope(p); double sp = v.Length;
            if (sp < 0.03 && slope.Length < mu * Settings.StaticFactor) break;
            var fdir = sp > 1e-9 ? v * (1 / sp) : new FramePoint(0, 0);
            var vn = v - fdir * Math.Min(mu * G * dt, sp) - slope * (G * dt);
            var pn = p + vn * (dt / Y);
            if (SegmentDistance(p, pn, hole) * Y < Settings.HoleRadiusM && sp < Settings.CaptureSpeed) return (true, hole);
            p = pn; v = vn;
            if (!field.Inside(p)) break;
        }
        return (false, p);
    }

    static double SegmentDistance(FramePoint a, FramePoint b, FramePoint c)
    {
        var ab = b - a; double l2 = ab.Lat * ab.Lat + ab.Along * ab.Along;
        double t = l2 < 1e-12 ? 0 : Math.Clamp(((c.Lat - a.Lat) * ab.Lat + (c.Along - a.Along) * ab.Along) / l2, 0, 1);
        return (a + ab * t).DistanceTo(c);
    }

    /// <summary>Expected putts from <paramref name="start"/> to <paramref name="hole"/>, with the golfer's read.</summary>
    public PuttValue Expected(ISlopeField field, FramePoint start, FramePoint hole, int samples = 40, int seed = 0)
    {
        double ft = start.DistanceTo(hole) * 3;
        if (ft < 0.6) return new PuttValue(1, 0, 1);
        // Candidate reads: for each pace, the middle of the aims that hole a perfect stroke, or the aim that stops nearest.
        double span = Math.Min(4.0, 0.4 + ft * 0.08);
        var candidates = new List<(double Aim, double Pace)>();
        foreach (var pace in Paces)
        {
            double sum = 0; int n = 0, steps = 12; double nearA = 0, nearD = double.MaxValue;
            for (int i = -steps; i <= steps; i++)
            {
                double a = span * i / steps;
                var res = Putt(field, start, hole, a, pace, 0, 0);
                if (res.Holed) { sum += a; n++; }
                else { double d = res.Stop.DistanceTo(hole); if (d < nearD) { nearD = d; nearA = a; } }
            }
            candidates.Add(n > 0 ? (sum / n, pace) : (nearA, pace));
        }
        // The read: the candidate with the fewest expected putts under the golfer's errors (same draws for each).
        double bestAim = 0, bestPace = 1, bestE = double.MaxValue;
        foreach (var (a, pace) in candidates)
        {
            double e = Trial(field, start, hole, a, pace, 12, seed * 31 + 5);
            if (e < bestE) { bestE = e; bestAim = a; bestPace = pace; }
        }
        return new PuttValue(Trial(field, start, hole, bestAim, bestPace, samples, seed), bestAim, bestPace);
    }

    double Trial(ISlopeField field, FramePoint start, FramePoint hole, double aim, double pace, int n, int seed)
    {
        var r = new Random(seed); double sum = 0;
        for (int i = 0; i < n; i++)
        {
            var res = Putt(field, start, hole, aim, pace, Normal(r) * Skill.AimSdRadians, Normal(r) * Skill.PaceSd);
            sum += res.Holed ? 1 : 1 + ExpectedFlat(res.Stop.DistanceTo(hole) * 3);
        }
        return sum / n;
    }

    static double Normal(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
}

/// <summary>Expected putts from every green cell to one pin.</summary>
public sealed class PuttingMap
{
    readonly Dictionary<(int, int), PuttValue> _cells;

    public int Pin { get; }
    public FramePoint PinAt { get; }
    public string TerrainId { get; }
    public IReadOnlyDictionary<(int, int), PuttValue> Cells => _cells;
    public double Average => _cells.Values.Average(v => v.Expected);

    PuttingMap(int pin, FramePoint at, string terrainId, Dictionary<(int, int), PuttValue> cells) { Pin = pin; PinAt = at; TerrainId = terrainId; _cells = cells; }

    /// <summary>Builds the map in parallel; each cell has its own seed, so the result doesn't depend on thread timing.</summary>
    public static PuttingMap Build(GreenSurface green, PuttingModel model, int pin, FramePoint pinAt, IProgress<double>? progress = null,
        CancellationToken cancel = default, int samples = 40)
    {
        var field = PuttingModel.Field(green);
        var cells = green.Cells.ToArray();
        var values = new PuttValue[cells.Length];
        int done = 0;
        Parallel.For(0, cells.Length, new ParallelOptions { CancellationToken = cancel }, i =>
        {
            values[i] = model.Expected(field, cells[i], pinAt, samples, i + 1000 * pin);
            int d = Interlocked.Increment(ref done);
            if (progress is not null && d % 25 == 0) progress.Report(d / (double)cells.Length);
        });
        var dict = new Dictionary<(int, int), PuttValue>();
        for (int i = 0; i < cells.Length; i++) dict[GreenSurface.Key(cells[i])] = values[i];
        return new PuttingMap(pin, pinAt, green.TerrainId, dict);
    }

    /// <summary>Expected putts from a point on the green: its cell, a neighbour within 2 yds, or the flat-green curve.</summary>
    public double At(FramePoint p, PuttingModel model)
    {
        var k = GreenSurface.Key(p);
        if (_cells.TryGetValue(k, out var v)) return v.Expected;
        for (int d = 1; d <= 2; d++)
            for (int dx = -d; dx <= d; dx++)
                for (int dy = -d; dy <= d; dy++)
                    if (_cells.TryGetValue((k.Item1 + dx, k.Item2 + dy), out v)) return v.Expected;
        return model.ExpectedFlat(p.DistanceTo(PinAt) * 3);
    }

    /// <summary>Share of the green from which two putts or fewer are expected to fall short: cells needing 2.3 or more.</summary>
    public double HardShare(double threshold = 2.3) => _cells.Values.Count(v => v.Expected >= threshold) / (double)_cells.Count;

    /// <summary>Average expected putts from the ring of green cells <paramref name="lo"/>–<paramref name="hi"/> feet from the pin,
    /// split into the half above the hole and the half below it (by the fall line at the pin).</summary>
    public (double Above, double Below) AboveBelow(GreenSurface green, double lo = 15, double hi = 30)
    {
        var s = green.Slope(PinAt);
        var downhill = s.Length > 0.002 ? new FramePoint(-s.Lat, -s.Along) * (1 / s.Length) : new FramePoint(0, -1);
        double sa = 0, sb = 0; int na = 0, nb = 0;
        foreach (var c in green.Cells)
        {
            double ft = c.DistanceTo(PinAt) * 3; if (ft < lo || ft > hi) continue;
            var rel = c - PinAt; double dot = rel.Lat * downhill.Lat + rel.Along * downhill.Along;
            double e = _cells[GreenSurface.Key(c)].Expected;
            if (dot < 0) { sa += e; na++; } else { sb += e; nb++; }
        }
        return (na == 0 ? double.NaN : sa / na, nb == 0 ? double.NaN : sb / nb);
    }
}
