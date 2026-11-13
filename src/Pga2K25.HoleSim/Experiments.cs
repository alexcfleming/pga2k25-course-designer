namespace Pga2K25.HoleSim;

/// <summary>A design change tried on the hole's surfaces: its name, what it does, and the edited grid.</summary>
public sealed record DesignEdit(string Name, string Detail, Func<HoleModel, SurfaceGrid> Apply);

/// <summary>One pin before and after an edit, played with the same draws.</summary>
public sealed record PinChange(int Pin, double Before, double After, double Ci95)
{
    /// <summary>Strokes the edit adds (+, harder) or takes off (−, easier).</summary>
    public double Delta => After - Before;
}

public sealed record ExperimentResult(string Name, string Detail, IReadOnlyList<PinChange> Pins, int CellsChanged)
{
    /// <summary>The edit's effect averaged over the pins (each pin counts the same).</summary>
    public double Delta => Pins.Count == 0 ? 0 : Pins.Average(p => p.Delta);
    public double Ci95 => Pins.Count == 0 ? 0 : Math.Sqrt(Pins.Sum(p => p.Ci95 * p.Ci95)) / Pins.Count;
}

/// <summary>
/// Stage 4e: design experiments. A change to the hole's surfaces (a wider or narrower landing zone, a hazard taken
/// out) or your previous save, played pin by pin with the same draws as the hole as it is, so the difference is the
/// edit's and not the luck of the shots. Surface edits only: the ground's heights stay as they are.
/// </summary>
public static class Experiments
{
    /// <summary>
    /// The drive landing zone, as an along-the-hole window: where the player's drives at this aim land, from the 10th to
    /// the 90th percentile, plus 10 yds either end.
    /// </summary>
    public static (double From, double To) LandingZone(HolePlan plan, int samples = 300)
    {
        var m = plan.Context.Hole;
        var along = Enumerable.Range(0, samples).Select(i => GeometryRun.Drive(m, m.Grid, plan.Context.Ground, plan.Player, plan.DriveAim, new Random(unchecked(97 * 1_000_003 + i)), plan.Terrain).Finish.Along)
            .OrderBy(a => a).ToList();
        return (along[samples / 10] - 10, along[samples * 9 / 10] + 10);
    }

    /// <summary>
    /// The fairway made <paramref name="yards"/> wider (+) or narrower (−) on each side between two along-the-hole
    /// distances: rough, heavy rough and native within that many yards of the fairway become fairway; or fairway that
    /// close to its edge becomes rough. Bunkers, scrub, water and greens are left alone.
    /// </summary>
    public static SurfaceGrid WidenFairway(SurfaceGrid grid, int yards, double alongFrom, double alongTo)
    {
        int w = grid.Width, h = grid.Height, n = Math.Abs(yards);
        bool widen = yards > 0;
        // Steps (in cells, diagonals counting one) from the fairway (widen) or from anything not fairway (narrow).
        var from = new List<int>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            bool fw = grid[x, y] == Ground.Fairway;
            if (widen ? fw : !fw && grid[x, y] != Ground.Green) from.Add(y * w + x);
        }
        var dist = grid.StepsFrom(from, n);
        return grid.With((x, y, g) =>
        {
            double along = grid.AlongMin + y + 0.5;
            if (along < alongFrom || along > alongTo) return g;
            int d = dist[y * w + x];
            if (d == 0 || d > n) return g;
            if (widen) return g is Ground.Rough or Ground.HeavyRough or Ground.Native ? Ground.Fairway : g;
            return g == Ground.Fairway ? Ground.Rough : g;
        });
    }

    /// <summary>A hazard taken out: its connected piece (from its seed cell, within its own along-the-hole span) becomes <paramref name="becomes"/>.</summary>
    public static SurfaceGrid RemoveHazard(SurfaceGrid grid, HazardInfo hazard, Ground becomes = Ground.Rough)
    {
        if (hazard.Seed is not { } seed) return grid;
        int w = grid.Width, h = grid.Height;
        int sx = (int)Math.Floor(seed.Lat - grid.LatMin), sy = (int)Math.Floor(seed.Along - grid.AlongMin);
        if (sx < 0 || sy < 0 || sx >= w || sy >= h || grid[sx, sy] != hazard.Ground) return grid;
        var hit = new bool[w * h]; var q = new Queue<(int, int)>(); q.Enqueue((sx, sy)); hit[sy * w + sx] = true;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h || hit[yy * w + xx]) continue;
                double along = grid.AlongMin + yy + 0.5;
                if (grid[xx, yy] != hazard.Ground || along < hazard.FromYards - 2 || along > hazard.ToYards + 2) continue;
                hit[yy * w + xx] = true; q.Enqueue((xx, yy));
            }
        }
        return grid.With((x, y, g) => hit[y * w + x] ? becomes : g);
    }

    /// <summary>
    /// The edits worth trying on this hole: the landing zone 5 yds wider and 5 yds narrower each side (par 4s and 5s),
    /// and each hazard near play taken out (the nearest six to the landing zone or the green).
    /// </summary>
    public static IReadOnlyList<DesignEdit> Suggested(HolePlan plan)
    {
        var m = plan.Context.Hole; var list = new List<DesignEdit>();
        if (m.Par > 3)
        {
            var (a, b) = LandingZone(plan);
            list.Add(new DesignEdit("Landing zone 5 yds wider each side", $"Rough within 5 yds of the fairway becomes fairway, {a:0}–{b:0} yds from the tee.", h => WidenFairway(h.Grid, 5, a, b)));
            list.Add(new DesignEdit("Landing zone 5 yds narrower each side", $"Fairway within 5 yds of its edge becomes rough, {a:0}–{b:0} yds from the tee.", h => WidenFairway(h.Grid, -5, a, b)));
        }
        double landing = m.Par > 3 ? (LandingZone(plan).From + LandingZone(plan).To) / 2 : m.GreenFrame.Along;
        double Near(HazardInfo z) => Math.Min(Math.Abs((z.FromYards + z.ToYards) / 2.0 - landing), Math.Abs((z.FromYards + z.ToYards) / 2.0 - m.GreenFrame.Along));
        foreach (var z in m.Hazards.Where(z => z.Seed is not null && z.AreaSqYd >= 20).OrderBy(Near).Take(6))
        {
            var hz = z;
            list.Add(new DesignEdit($"Take out the {z.Text}", $"{Grounds.Name(z.Ground)} ({z.AreaSqYd} sq yd) becomes rough.", h => RemoveHazard(h.Grid, hz)));
        }
        return list;
    }

    /// <summary>The plan on an edited hole: same golfer, green, terrain and putting maps; the short game and heat maps rebuild on the new surfaces.</summary>
    public static HolePlan OnGrid(HolePlan plan, SurfaceGrid grid)
    {
        var c = plan.Context; var hole = c.Hole.WithGrid(grid);
        var ctx = new ShotContext { Hole = hole, Grid = grid, Ground = c.Ground, Terrain = c.Terrain, Golfer = c.Golfer, Rules = c.Rules, Baseline = c.Baseline, Lies = c.Lies, Putting = c.Putting, Green = c.Green };
        foreach (var (k, v) in c.PuttingMaps) ctx.PuttingMaps[k] = v;
        return new HolePlan { Context = ctx, Player = plan.Player, Reach = plan.Reach, Terrain = plan.Terrain, DriveAim = plan.DriveAim };
    }

    /// <summary>Two versions of the hole (as it is and edited, or the previous save and this one), every pin, the same draws.</summary>
    public static ExperimentResult Compare(string name, string detail, HolePlan before, HolePlan after, IReadOnlyList<HoleSummary>? beforeSummaries = null,
        int samples = 400, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var pins = new List<PinChange>();
        int changed = 0;
        var ga = before.Context.Grid; var gb = after.Context.Grid;
        if (ga.Width == gb.Width && ga.Height == gb.Height)
            for (int y = 0; y < ga.Height; y++) for (int x = 0; x < ga.Width; x++) if (ga[x, y] != gb[x, y]) changed++;
        foreach (var p in before.Context.Hole.Pins)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report($"{name}: pin {p.Number}…");
            var a = beforeSummaries?.FirstOrDefault(s => s.Pin == p.Number && s.Samples == samples) ?? HolePlay.Play(before, p.Number, samples, cancel: cancel);
            var b = HolePlay.Play(after, p.Number, samples, cancel: cancel);
            // Paired: the same draw numbers on both versions, so the spread of the differences sets the error.
            var diff = a.Scores.Zip(b.Scores, (x, y) => y - x).ToArray();
            double mean = diff.Average(), sd = Math.Sqrt(diff.Sum(d => (d - mean) * (d - mean)) / Math.Max(1, diff.Length - 1));
            pins.Add(new PinChange(p.Number, a.Expected, b.Expected, 1.96 * sd / Math.Sqrt(diff.Length)));
        }
        return new ExperimentResult(name, detail, pins, changed);
    }
}
