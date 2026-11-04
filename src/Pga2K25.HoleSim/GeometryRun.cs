namespace Pga2K25.HoleSim;

public sealed record PlayerProfile(string Id, string Name, double Carry, double CarrySd, double SideSd);

/// <param name="Friction">Rolling friction coefficient μ.</param>
/// <param name="HopMetres">How far the first bounce carries the ball forward at full landing speed.</param>
/// <param name="Kept">Share of landing speed kept after the first bounce.</param>
public sealed record GroundParams(double Friction, double HopMetres, double Kept);

public sealed record GroundTable(double LandingSpeed, IReadOnlyDictionary<Ground, GroundParams> ByGround)
{
    public GroundParams For(Ground g) => ByGround.TryGetValue(g, out var p) ? p : ByGround[Ground.Native];
}

/// <summary>How the simulator's grounds score against a baseline's lies.</summary>
public sealed record LieMap(Lie Native, Lie HeavyRough, Lie Scrub, int WaterPenalty)
{
    public (Lie Lie, int Penalty) For(Ground g) => g switch
    {
        Ground.Fairway => (Lie.Fairway, 0),
        Ground.Rough => (Lie.Rough, 0),
        Ground.HeavyRough => (HeavyRough, 0),
        Ground.Bunker => (Lie.Sand, 0),
        Ground.Green => (Lie.Green, 0),
        Ground.Scrub => (Scrub, 0),
        Ground.Water => (Lie.Rough, WaterPenalty),
        _ => (Native, 0),
    };
}

public sealed record RunSettings
{
    public int Seed { get; init; } = 7;
    public int SamplesPerAim { get; init; } = 2000;
    public double AimMin { get; init; } = -60;
    public double AimMax { get; init; } = 60;
    public double AimStep { get; init; } = 5;

    /// <summary>Finish points kept per aim for the pattern map.</summary>
    public int KeepPoints { get; init; } = 400;
}

public sealed record ShotSample(FramePoint Landing, FramePoint Finish, Ground Ground);

/// <summary>One aim's tee shots. Aim is yards right (+) or left (−) of the line from the tee to the game's aim marker.</summary>
public sealed record AimResult(
    double Aim,
    double[] ShareByGround,
    double MeanYardsLeft,
    double MedianYardsLeft,
    double MeanSg,
    double SgCi95,
    double ExpectedStrokes,
    double OutsideTableShare,
    IReadOnlyList<ShotSample> Samples)
{
    public double Share(Ground g) => ShareByGround[(int)g];
}

public sealed record GeometryRunResult(
    PlayerProfile Player,
    int Pin,
    string BaselineId,
    double TeeExpected,
    Coverage TeeCoverage,
    IReadOnlyList<AimResult> Aims,
    RunSettings Settings,
    IReadOnlyList<string> Notes)
{
    public AimResult Best => Aims.OrderByDescending(a => a.MeanSg).First();
}

/// <summary>
/// Geometry-only tee shots (Stage 2): drives land with the player's carry and side spread around an aim, then roll
/// on flat ground by surface until they stop. No slope, no elevation and no wind, because the terrain hasn't been
/// checked against the game yet. Each finish is valued with the baseline:
/// SG = E(tee, distance to pin) − E(finish lie, distance to pin) − 1 − penalty. Distances are straight-line to the pin.
/// Deterministic for a seed, and every aim uses the same random draws (paired), so aims compare fairly.
/// </summary>
public static class GeometryRun
{
    public static GeometryRunResult Run(HoleModel hole, PlayerProfile player, int pin, GroundTable ground, LieMap lies,
        IBaseline baseline, RunSettings? settings = null, SurfaceGrid? grid = null)
    {
        settings ??= new RunSettings();
        grid ??= hole.Grid;
        var pinF = hole.Pins.Count > 0 ? hole.Pins[Math.Clamp(pin - 1, 0, hole.Pins.Count - 1)].Frame : hole.GreenFrame;
        var aimDir = hole.AimFrame.Length > 1e-6 ? hole.AimFrame * (1 / hole.AimFrame.Length) : new FramePoint(0, 1);
        var aimRight = new FramePoint(aimDir.Along, -aimDir.Lat);
        var tee = baseline.ExpectedStrokes(Lie.Tee, pinF.Length);

        var aims = new List<AimResult>();
        int steps = (int)Math.Round((settings.AimMax - settings.AimMin) / settings.AimStep);
        for (int k = 0; k <= steps; k++)
        {
            double aim = settings.AimMin + k * settings.AimStep;
            int n = settings.SamplesPerAim;
            var counts = new int[Grounds.All.Count];
            var left = new double[n]; var sg = new double[n];
            int outside = 0; var keep = new List<ShotSample>();
            for (int i = 0; i < n; i++)
            {
                var rng = new Random(unchecked(settings.Seed * 1_000_003 + i));   // same draws for every aim
                double carry = player.Carry + Normal(rng) * player.CarrySd;
                double side = aim + Normal(rng) * player.SideSd;
                var landing = aimDir * carry + aimRight * side;
                var finish = Roll(grid, ground, landing, landing * (1 / Math.Max(landing.Length, 1e-6)), ground.LandingSpeed);
                var g = grid.At(finish);
                counts[(int)g]++;
                double d = finish.DistanceTo(pinF);
                left[i] = d;
                var (lie, penalty) = lies.For(g);
                var e = baseline.ExpectedStrokes(lie, lie == Lie.Green ? d * 3 : d);
                if (!e.Covered) outside++;
                sg[i] = StrokesGained.Shot(tee.Value, e.Value, penalty);
                if (keep.Count < settings.KeepPoints) keep.Add(new ShotSample(landing, finish, g));
            }
            double mean = sg.Average(), sd = Math.Sqrt(sg.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, n - 1));
            Array.Sort(left);
            aims.Add(new AimResult(aim, counts.Select(c => c / (double)n).ToArray(), left.Average(), left[n / 2], mean,
                1.96 * sd / Math.Sqrt(n), tee.Value - mean, outside / (double)n, keep));
        }

        var notes = new List<string>
        {
            "Geometry only: flat ground, no slope roll, no carry change for elevation, no wind.",
            $"Baseline {baseline.Id}" + (baseline.Secondary ? " (secondary anchors, sparse)" : "") + ". " + StrokesGained.SignConvention,
        };
        if (!tee.Covered) notes.Add($"The tee distance ({pinF.Length:0} yds) is outside the baseline's tee rows; the nearest row was used.");
        var best = aims.OrderByDescending(a => a.MeanSg).First();
        if (best == aims[0] || best == aims[^1]) notes.Add($"The best aim ({best.Aim:+0;-0;0}) is at the edge of the sweep; the true best may lie further out.");
        double worstOutside = aims.Max(a => a.OutsideTableShare);
        if (worstOutside > 0) notes.Add($"Up to {worstOutside:P0} of finishes at one aim fall outside the baseline's rows for their lie (valued at the nearest row, not extrapolated).");
        return new GeometryRunResult(player, pin, baseline.Id, tee.Value, tee.Coverage, aims, settings, notes);
    }

    /// <summary>
    /// The proof of concept's bounce-and-roll with the slope term off: a forward hop, then rolling friction μ·g until
    /// the ball stops or runs onto sand, scrub or water. Positions in yards, speeds in m/s.
    /// </summary>
    public static FramePoint Roll(SurfaceGrid grid, GroundTable table, FramePoint landing, FramePoint heading, double speed)
    {
        var g = grid.At(landing);
        if (Grounds.Stops(g)) return landing;
        var p0 = table.For(g);
        double hop = p0.HopMetres * (speed > 4 ? 1 : 0.4) / HoleFrame.Yard;
        var p = landing + heading * hop;
        double v = speed * p0.Kept;
        const double dt = 0.05, gravity = 9.81;
        for (int k = 0; k < 500 && v > 0.08; k++)
        {
            g = grid.At(p);
            if (Grounds.Stops(g)) break;
            double mu = table.For(g).Friction;
            double step = v * dt - 0.5 * mu * gravity * dt * dt;
            p += heading * (Math.Max(step, 0) / HoleFrame.Yard);
            v -= mu * gravity * dt;
        }
        return p;
    }

    static double Normal(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
}
