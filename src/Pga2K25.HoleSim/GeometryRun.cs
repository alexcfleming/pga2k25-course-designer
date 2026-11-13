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

/// <summary>
/// Terrain for a run, allowed by the gate: the model, how far it can be trusted ("Trustworthy", "Indicative: results carry
/// a warning", or "terrain unverified" for an override) and the elevation carry rule.
/// </summary>
public sealed record TerrainUse(ITerrainModel Model, string Label, double YardsPerFootDrop = 1 / 3.0);

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
    /// <summary>The terrain the run rolled on; null for a geometry-only run.</summary>
    public TerrainUse? Terrain { get; init; }

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
    /// <summary>
    /// One drive from the tee (the frame's origin) at <paramref name="aim"/> yds right of the aim-marker line: carry and
    /// side drawn from the player's spreads (in that order, so runs and play-outs share draws), the elevation carry when
    /// terrain is on, then the roll. Returns where it lands and where it finishes.
    /// </summary>
    public static (FramePoint Landing, FramePoint Finish) Drive(HoleModel hole, SurfaceGrid grid, GroundTable ground, PlayerProfile player, double aim, Random rng, TerrainUse? terrain = null)
    {
        var aimDir = hole.AimFrame.Length > 1e-6 ? hole.AimFrame * (1 / hole.AimFrame.Length) : new FramePoint(0, 1);
        var aimRight = new FramePoint(aimDir.Along, -aimDir.Lat);
        double carry = player.Carry + ShotDraws.Normal(rng) * player.CarrySd;
        double side = aim + ShotDraws.Normal(rng) * player.SideSd;
        var landing = aimDir * carry + aimRight * side;
        if (terrain is not null)
        {
            // Carry gains (loses) yards for ground below (above) the tee: PLACEHOLDER 1 yd per 3 ft.
            carry += (terrain.Model.HeightFt(new FramePoint(0, 0)) - terrain.Model.HeightFt(landing)) * terrain.YardsPerFootDrop;
            landing = aimDir * carry + aimRight * side;
        }
        var finish = Roll(grid, ground, landing, landing * (1 / Math.Max(landing.Length, 1e-6)), ground.LandingSpeed, terrain?.Model);
        return (landing, finish);
    }

    public static GeometryRunResult Run(HoleModel hole, PlayerProfile player, int pin, GroundTable ground, LieMap lies,
        IBaseline baseline, RunSettings? settings = null, SurfaceGrid? grid = null, TerrainUse? terrain = null)
    {
        settings ??= new RunSettings();
        grid ??= hole.Grid;
        var pinF = hole.Pins.Count > 0 ? hole.Pins[Math.Clamp(pin - 1, 0, hole.Pins.Count - 1)].Frame : hole.GreenFrame;
        var aimDir = hole.AimFrame.Length > 1e-6 ? hole.AimFrame * (1 / hole.AimFrame.Length) : new FramePoint(0, 1);
        var aimRight = new FramePoint(aimDir.Along, -aimDir.Lat);
        var tee = baseline.ExpectedStrokes(Lie.Tee, pinF.Length);
        double teeHeight = terrain?.Model.HeightFt(new FramePoint(0, 0)) ?? 0;

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
                var (landing, finish) = Drive(hole, grid, ground, player, aim, rng, terrain);
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
            terrain is null ? "Geometry only: flat ground, no slope roll, no carry change for elevation, no wind."
                : $"Terrain: {terrain.Model.Name} ({terrain.Label}). Slope roll and elevation carry on; no wind.",
            $"Baseline {baseline.Id}" + (baseline.Secondary ? " (secondary anchors, sparse)" : "") + ". " + StrokesGained.SignConvention,
        };
        if (!tee.Covered) notes.Add($"The tee distance ({pinF.Length:0} yds) is outside the baseline's tee rows; the nearest row was used.");
        var best = aims.OrderByDescending(a => a.MeanSg).First();
        if (best == aims[0] || best == aims[^1]) notes.Add($"The best aim ({best.Aim:+0;-0;0}) is at the edge of the sweep; the true best may lie further out.");
        double worstOutside = aims.Max(a => a.OutsideTableShare);
        if (worstOutside >= 0.005) notes.Add($"Up to {worstOutside:P0} of finishes at one aim fall outside the baseline's rows for their lie (valued at the nearest row, not extrapolated).");
        return new GeometryRunResult(player, pin, baseline.Id, tee.Value, tee.Coverage, aims, settings, notes) { Terrain = terrain };
    }

    /// <summary>
    /// The proof of concept's bounce and roll: a forward hop, then rolling friction μ·g against the ball's motion and,
    /// when a terrain model is given, gravity down the slope, until the ball stops (slow and on ground flatter than its
    /// friction) or runs onto sand, scrub or water. Without terrain the ground is flat. Positions in yards, speeds in m/s.
    /// </summary>
    public static FramePoint Roll(SurfaceGrid grid, GroundTable table, FramePoint landing, FramePoint heading, double speed, ITerrainModel? terrain = null)
    {
        var g = grid.At(landing);
        if (Grounds.Stops(g)) return landing;
        var p0 = table.For(g);
        double hop = p0.HopMetres * (speed > 4 ? 1 : 0.4) / HoleFrame.Yard;
        var p = landing + heading * hop;
        var v = heading * (speed * p0.Kept);   // m/s, in frame directions
        const double dt = 0.05, gravity = 9.81;
        var under = g; double mu = p0.Friction;   // the ground under the ball: its friction is looked up again only when it changes
        for (int k = 0; k < 500; k++)
        {
            g = grid.At(p);
            if (Grounds.Stops(g)) break;
            if (g != under) { under = g; mu = table.For(g).Friction; }
            var slope = terrain?.Slope(p) ?? new FramePoint(0, 0);
            double sp = v.Length;
            if (sp < 0.08 && slope.Length < mu) break;
            // Friction can only stop the ball, never push it back: cap it at the speed it has.
            var frictionDir = sp > 1e-9 ? v * (1 / sp) : new FramePoint(0, 0);
            double frictionDv = Math.Min(mu * gravity * dt, sp);
            v = v - frictionDir * frictionDv - slope * (gravity * dt);
            p += v * (dt / HoleFrame.Yard);
        }
        return p;
    }
}
