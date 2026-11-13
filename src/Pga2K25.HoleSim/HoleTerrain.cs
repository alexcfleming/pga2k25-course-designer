using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

namespace Pga2K25.HoleSim;

/// <summary>
/// Ground height over one hole, in feet relative to the green centre (+ above), in the hole frame. Implementations are
/// candidates to compare against in-game readings; none of them is assumed right until the terrain gate says so.
/// </summary>
public interface ITerrainModel
{
    string Id { get; }
    string Name { get; }
    double HeightFt(FramePoint p);

    /// <summary>Uphill gradient (rise over run, dimensionless) toward +Lat and +Along.</summary>
    FramePoint Slope(FramePoint p);
}

/// <summary>The brush replay (<see cref="TerrainReplay"/>, the books' settings), optionally Gaussian-smoothed outside the greens and tees.</summary>
public sealed class ReplayTerrain : ITerrainModel
{
    readonly HoleFrame _frame;
    readonly Heightfield _field;
    readonly double _zeroMetres;

    public string Id { get; }
    public string Name { get; }

    ReplayTerrain(string id, string name, HoleFrame frame, Heightfield field, double zero)
    {
        Id = id; Name = name; _frame = frame; _field = field; _zeroMetres = zero;
    }

    public double HeightFt(FramePoint p) => (_field.Sample(_frame.ToWorld(p)) - _zeroMetres) / HoleFrame.Foot;

    public FramePoint Slope(FramePoint p)
    {
        const double h = 1.5;   // yards
        double dl = (HeightFt(p + new FramePoint(h, 0)) - HeightFt(p - new FramePoint(h, 0))) / (2 * h) / 3;
        double da = (HeightFt(p + new FramePoint(0, h)) - HeightFt(p - new FramePoint(0, h))) / (2 * h) / 3;
        return new FramePoint(dl, da);
    }

    /// <summary>The replay over the hole's grid, as the yardage and greens books build it (1 m cells here).</summary>
    public static ReplayTerrain Build(CourseModel course, HoleModel hole, TerrainModelOptions? options = null) =>
        FromField(hole, BuildField(course, hole, options));

    public static ReplayTerrain FromField(HoleModel hole, Heightfield field) =>
        new("replay", "Brush replay (book settings)", hole.Frame, field, field.Sample(hole.Green));

    /// <summary>
    /// The replay smoothed with a Gaussian of <paramref name="sigmaMetres"/> outside the greens, keeping the replay on
    /// the greens and a few yards round them (the greens calibration only holds there).
    /// </summary>
    public static ReplayTerrain Smoothed(CourseModel course, HoleModel hole, double sigmaMetres = 8, TerrainModelOptions? options = null) =>
        Smoothed(hole, BuildField(course, hole, options), sigmaMetres);

    /// <summary>The smoothed candidate from an already-built replay field (left unchanged).</summary>
    public static ReplayTerrain Smoothed(HoleModel hole, Heightfield raw, double sigmaMetres = 8)
    {
        var smooth = new Heightfield(raw.Origin, raw.Cell, raw.Width, raw.Height);
        Array.Copy(raw.Values, smooth.Values, raw.Values.Length);
        TerrainReplay.Smooth(smooth, sigmaMetres);

        // Weight 1 on the green, fading to 0 by 6 yds outside it; the same round each tee (within 8 yds, fading by 16),
        // because tee boxes are small, sharp flats and readings are often taken against a tee.
        var grid = hole.Grid;
        var greenCells = new List<int>();
        for (int y = 0; y < grid.Height; y++) for (int x = 0; x < grid.Width; x++) if (grid[x, y] == Ground.Green) greenCells.Add(y * grid.Width + x);
        var near = grid.StepsFrom(greenCells, 6);   // yards from the nearest green cell; beyond 6 it no longer matters
        var tees = hole.Tees.Select(t => hole.Frame.ToFrame(t.World)).ToList();
        for (int iz = 0; iz < raw.Height; iz++)
            for (int ix = 0; ix < raw.Width; ix++)
            {
                var f = hole.Frame.ToFrame(raw.CellCentre(ix, iz));
                int gx = (int)Math.Floor(f.Lat - grid.LatMin), gy = (int)Math.Floor(f.Along - grid.AlongMin);
                double d = gx < 0 || gy < 0 || gx >= grid.Width || gy >= grid.Height ? 99 : near[gy * grid.Width + gx];
                double w = Math.Clamp(1 - d / 6.0, 0, 1);
                double dt = tees.Count == 0 ? 99 : tees.Min(p => p.DistanceTo(f));
                w = Math.Max(w, Math.Clamp((16 - dt) / 8.0, 0, 1));
                w = w * w * (3 - 2 * w);
                smooth[ix, iz] = smooth[ix, iz] * (1 - w) + raw[ix, iz] * w;
            }
        return new ReplayTerrain($"smoothed-{sigmaMetres:0}m", $"Replay smoothed {sigmaMetres:0} m outside greens and tees", hole.Frame, smooth, smooth.Sample(hole.Green));
    }

    /// <summary>The replay over the hole's grid plus a margin (1 m cells unless <paramref name="options"/> say otherwise).</summary>
    public static Heightfield BuildField(CourseModel course, HoleModel hole, TerrainModelOptions? options = null)
    {
        options ??= new TerrainModelOptions { CellMetres = 1.0 };
        var g = hole.Grid;
        var corners = new[]
        {
            hole.Frame.ToWorld(new FramePoint(g.LatMin, g.AlongMin)), hole.Frame.ToWorld(new FramePoint(g.LatMax, g.AlongMin)),
            hole.Frame.ToWorld(new FramePoint(g.LatMin, g.AlongMax)), hole.Frame.ToWorld(new FramePoint(g.LatMax, g.AlongMax)),
        };
        var min = new Vec2(corners.Min(c => c.X) - 20, corners.Min(c => c.Z) - 20);
        var max = new Vec2(corners.Max(c => c.X) + 20, corners.Max(c => c.Z) + 20);
        return TerrainReplay.Build(course, min, max, options);
    }
}

/// <summary>
/// A base model plus a smooth correction fitted to this session's readings: Gaussian-weighted residuals with length
/// <see cref="LengthYards"/> (25 yds did best of 25/40/80/150 on Sangoma holes 3, 4 and 11), shrunk toward zero away from the readings. Held-out readings test it on points it didn't see.
/// </summary>
public sealed class CorrectedTerrain : ITerrainModel
{
    readonly ITerrainModel _base;
    readonly IReadOnlyList<(FramePoint P, double ResidualFt)> _fit;

    public double LengthYards { get; }
    public double Shrink { get; }
    public string Id => _base.Id + "+fit";
    public string Name => _base.Name + ", corrected to readings";

    public CorrectedTerrain(ITerrainModel baseModel, IEnumerable<HeightReading> fitReadings, double lengthYards = 25, double shrink = 0.3)
    {
        _base = baseModel; LengthYards = lengthYards; Shrink = shrink;
        // Residual at the reading, relative to its reference; the correction at the reference is taken as zero.
        _fit = fitReadings.Select(r => (r.Where, r.Feet - (baseModel.HeightFt(r.Where) - baseModel.HeightFt(r.Reference)))).ToList();
    }

    public double Correction(FramePoint p)
    {
        double sw = 0, swr = 0, two = 2 * LengthYards * LengthYards;
        foreach (var (q, r) in _fit)
        {
            double d2 = (p.Lat - q.Lat) * (p.Lat - q.Lat) + (p.Along - q.Along) * (p.Along - q.Along);
            double w = Math.Exp(-d2 / two);
            sw += w; swr += w * r;
        }
        return swr / (sw + Shrink);
    }

    public double HeightFt(FramePoint p) => _base.HeightFt(p) + Correction(p);

    public FramePoint Slope(FramePoint p)
    {
        const double h = 1.5;
        double dl = (HeightFt(p + new FramePoint(h, 0)) - HeightFt(p - new FramePoint(h, 0))) / (2 * h) / 3;
        double da = (HeightFt(p + new FramePoint(0, h)) - HeightFt(p - new FramePoint(0, h))) / (2 * h) / 3;
        return new FramePoint(dl, da);
    }
}
