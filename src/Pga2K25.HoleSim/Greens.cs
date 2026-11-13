namespace Pga2K25.HoleSim;

/// <summary>How the green rolls: rolling friction from the Stimp reading.</summary>
public static class GreenPhysics
{
    public const double Gravity = 9.81;

    /// <summary>Speed a ball leaves a Stimpmeter's ramp (about 6 ft/s); it then rolls the Stimp reading in feet on a flat green.</summary>
    public const double StimpReleaseSpeed = 1.83;

    /// <summary>Rolling friction coefficient that makes a ball released at 1.83 m/s roll <paramref name="stimpFeet"/> feet: Stimp 10.3 → 0.0545.</summary>
    public static double RollingFriction(double stimpFeet) =>
        StimpReleaseSpeed * StimpReleaseSpeed / (2 * Gravity * stimpFeet * HoleFrame.Foot);
}

/// <summary>
/// One hole's green: its cells (the green surface touching the green point), and a fine raster of height and slope
/// over it, built once from a terrain model so putts can roll thousands of times without re-sampling the replay.
/// Heights are feet relative to the green point; slopes are uphill rise over run.
/// </summary>
public sealed class GreenSurface
{
    const double Cell = 0.5;   // yards
    readonly double _lat0, _al0;
    readonly int _w, _h;
    readonly double[] _height, _slopeLat, _slopeAlong;
    readonly ITerrainModel _terrain;

    public IReadOnlyList<FramePoint> Cells { get; }
    public HashSet<(int, int)> CellKeys { get; }
    public FramePoint GreenPoint { get; }
    public string TerrainId => _terrain.Id;
    public string TerrainName => _terrain.Name;
    public double SquareYards => Cells.Count;

    GreenSurface(ITerrainModel terrain, FramePoint green, List<FramePoint> cells)
    {
        _terrain = terrain; GreenPoint = green; Cells = cells;
        CellKeys = cells.Select(Key).ToHashSet();
        _lat0 = cells.Min(c => c.Lat) - 15; _al0 = cells.Min(c => c.Along) - 15;
        _w = (int)Math.Ceiling((cells.Max(c => c.Lat) + 15 - _lat0) / Cell) + 1;
        _h = (int)Math.Ceiling((cells.Max(c => c.Along) + 15 - _al0) / Cell) + 1;
        _height = new double[_w * _h];
        double zero = terrain.HeightFt(green);
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
                _height[y * _w + x] = terrain.HeightFt(new FramePoint(_lat0 + x * Cell, _al0 + y * Cell)) - zero;
        _slopeLat = new double[_w * _h]; _slopeAlong = new double[_w * _h];
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(_w - 1, x + 1), y0 = Math.Max(0, y - 1), y1 = Math.Min(_h - 1, y + 1);
                // feet per yard → feet per foot (÷ 3)
                _slopeLat[y * _w + x] = (_height[y * _w + x1] - _height[y * _w + x0]) / ((x1 - x0) * Cell) / 3;
                _slopeAlong[y * _w + x] = (_height[y1 * _w + x] - _height[y0 * _w + x]) / ((y1 - y0) * Cell) / 3;
            }
    }

    public static (int, int) Key(FramePoint p) => ((int)Math.Floor(p.Lat), (int)Math.Floor(p.Along));

    /// <summary>The green cells (1 yd) connected to the green point, from the hole's surface grid.</summary>
    public static GreenSurface Build(HoleModel hole, ITerrainModel terrain)
    {
        var g = hole.Grid;
        int gx = (int)Math.Floor(hole.GreenFrame.Lat - g.LatMin), gy = (int)Math.Floor(hole.GreenFrame.Along - g.AlongMin);
        // Start from the green cell nearest the green point (the point can sit just off a drawn green's edge).
        (int X, int Y)? start = null; double best = double.MaxValue;
        for (int y = Math.Max(0, gy - 30); y < Math.Min(g.Height, gy + 30); y++)
            for (int x = Math.Max(0, gx - 30); x < Math.Min(g.Width, gx + 30); x++)
                if (g[x, y] == Ground.Green) { double d = (x - gx) * (x - gx) + (y - gy) * (y - gy); if (d < best) { best = d; start = (x, y); } }
        if (start is null) throw new InvalidOperationException($"Hole {hole.Number} has no green near its green point.");
        var seen = new bool[g.Width * g.Height]; var cells = new List<FramePoint>(); var q = new Queue<(int, int)>();
        q.Enqueue(start.Value); seen[start.Value.Y * g.Width + start.Value.X] = true;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue(); cells.Add(g.CellCentre(x, y));
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= g.Width || yy >= g.Height) continue;
                    int k = yy * g.Width + xx; if (seen[k] || g[xx, yy] != Ground.Green) continue;
                    seen[k] = true; q.Enqueue((xx, yy));
                }
        }
        return new GreenSurface(terrain, hole.GreenFrame, cells);
    }

    public bool OnGreen(FramePoint p) => CellKeys.Contains(Key(p));

    /// <summary>True inside the raster (the green plus 15 yds): putts that leave it are stopped there.</summary>
    public bool InRaster(FramePoint p) => p.Lat >= _lat0 && p.Along >= _al0 && p.Lat <= _lat0 + (_w - 1) * Cell && p.Along <= _al0 + (_h - 1) * Cell;

    double Bilinear(double[] a, FramePoint p)
    {
        double fx = Math.Clamp((p.Lat - _lat0) / Cell, 0, _w - 1.0001), fy = Math.Clamp((p.Along - _al0) / Cell, 0, _h - 1.0001);
        int x = (int)fx, y = (int)fy; double tx = fx - x, ty = fy - y;
        return (a[y * _w + x] * (1 - tx) + a[y * _w + x + 1] * tx) * (1 - ty) + (a[(y + 1) * _w + x] * (1 - tx) + a[(y + 1) * _w + x + 1] * tx) * ty;
    }

    public double HeightFt(FramePoint p) => InRaster(p) ? Bilinear(_height, p) : _terrain.HeightFt(p) - _terrain.HeightFt(GreenPoint);
    /// <summary>The slope under a point: both components read from the same raster cell with the same weights (as <see cref="Bilinear"/> would, one at a time).</summary>
    public FramePoint Slope(FramePoint p)
    {
        if (!InRaster(p)) return _terrain.Slope(p);
        double fx = Math.Clamp((p.Lat - _lat0) / Cell, 0, _w - 1.0001), fy = Math.Clamp((p.Along - _al0) / Cell, 0, _h - 1.0001);
        int x = (int)fx, y = (int)fy; double tx = fx - x, ty = fy - y;
        int i = y * _w + x, j = i + _w;
        double[] a = _slopeLat, b = _slopeAlong;
        return new FramePoint(
            (a[i] * (1 - tx) + a[i + 1] * tx) * (1 - ty) + (a[j] * (1 - tx) + a[j + 1] * tx) * ty,
            (b[i] * (1 - tx) + b[i + 1] * tx) * (1 - ty) + (b[j] * (1 - tx) + b[j + 1] * tx) * ty);
    }

    /// <summary>Slope (%) over the green cells: median, 90th percentile and the share too steep for a stopped ball to stay put.</summary>
    public (double P50, double P90, double TooSteep) SlopeStats(double friction, double staticFactor = 1.15)
    {
        var s = Cells.Select(c => Slope(c).Length * 100).OrderBy(v => v).ToList();
        double limit = friction * staticFactor * 100;
        return (s[s.Count / 2], s[(int)Math.Min(s.Count - 1, s.Count * 0.9)], s.Count(v => v > limit) / (double)s.Count);
    }
}

/// <summary>
/// A green's contours scaled about the green point by <see cref="K"/> (1 = the replay, 0 = flat), everywhere else the
/// base model. The game may smooth or level drawn greens; readings round the pins fit K.
/// </summary>
public sealed class ScaledGreenTerrain(ITerrainModel baseModel, Func<FramePoint, bool> onGreen, FramePoint greenPoint, double k, string id, string name) : ITerrainModel
{
    readonly double _zero = baseModel.HeightFt(greenPoint);
    public double K => k;
    public string Id => id;
    public string Name => name;

    const double Fade = 3;   // yards: the scaling fades out over the fringe, so the green's edge gets no false step

    public double HeightFt(FramePoint p)
    {
        double h = baseModel.HeightFt(p);
        double w = Weight(p);
        return w <= 0 ? h : h + (k - 1) * (h - _zero) * w;
    }

    /// <summary>1 on the green, falling to 0 at <see cref="Fade"/> yds outside it.</summary>
    double Weight(FramePoint p)
    {
        if (onGreen(p)) return 1;
        double best = double.MaxValue;
        for (double dy = -Fade; dy <= Fade; dy += 1)
            for (double dx = -Fade; dx <= Fade; dx += 1)
            {
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < best && d <= Fade && onGreen(new FramePoint(p.Lat + dx, p.Along + dy))) best = d;
            }
        return best > Fade ? 0 : 1 - best / Fade;
    }

    public FramePoint Slope(FramePoint p)
    {
        if (k == 1) return baseModel.Slope(p);
        const double h = 1.0;
        double dl = (HeightFt(p + new FramePoint(h, 0)) - HeightFt(p - new FramePoint(h, 0))) / (2 * h) / 3;
        double da = (HeightFt(p + new FramePoint(0, h)) - HeightFt(p - new FramePoint(0, h))) / (2 * h) / 3;
        return new FramePoint(dl, da);
    }
}

/// <summary>
/// The green gate: readings round the pins (relative to the green spot G when it was read, so the tee doesn't count)
/// against the replay's contours, the contours scaled to fit the readings, and a flat green.
/// </summary>
public static class GreenGate
{
    /// <summary>Greens span only a few feet, so the share-of-range test needs less range than the fairway gate.</summary>
    public const double MinRangeInches = 3;

    /// <summary>Two or three readings round each pin: four in all is enough to judge a green (a one-pin hole plus G).</summary>
    public const int MinReadings = 4;

    public sealed record Candidate(ITerrainModel Model, GateResult Gate, double K);

    /// <summary>Green readings re-expressed against G (when present): reading − G, at the reading's spot, zeroed at G's spot.</summary>
    public static IReadOnlyList<HeightReading> RelativeToGreen(IReadOnlyList<HeightReading> all, GreenSurface green)
    {
        var onGreen = all.Where(r => r.Spot != "G" && green.OnGreen(r.Where)).ToList();
        var g = all.FirstOrDefault(r => r.Spot == "G");
        if (g is null) return onGreen;
        return onGreen.Select(r => r with { Reference = g.Where, Feet = r.Feet - g.Feet }).ToList();
    }

    public static IReadOnlyList<Candidate> Evaluate(ITerrainModel baseModel, GreenSurface green, IReadOnlyList<HeightReading> allReadings)
    {
        var readings = RelativeToGreen(allReadings, green);
        var result = new List<Candidate>();
        var replay = new ScaledGreenTerrain(baseModel, green.OnGreen, green.GreenPoint, 1, "green-replay", "Replay contours");
        result.Add(new(replay, TerrainGate.Evaluate(replay, readings, minRangeInches: MinRangeInches, minReadings: MinReadings), 1));
        var fit = readings.Where(r => !r.HeldOut).ToList();
        if (fit.Count >= 2)
        {
            // Least squares for k in reading ≈ k · model (both relative to the same reference).
            double sxy = 0, sxx = 0;
            foreach (var r in fit) { double m = replay.HeightFt(r.Where) - replay.HeightFt(r.Reference); sxy += m * r.Feet; sxx += m * m; }
            double k = sxx < 1e-9 ? 1 : Math.Clamp(sxy / sxx, 0, 1.5);
            var scaled = new ScaledGreenTerrain(baseModel, green.OnGreen, green.GreenPoint, k, "green-scaled", $"Replay contours × {k:0.00} (fitted)");
            result.Add(new(scaled, TerrainGate.Evaluate(scaled, readings, fitted: true, minRangeInches: MinRangeInches), k));
        }
        var flat = new ScaledGreenTerrain(baseModel, green.OnGreen, green.GreenPoint, 0, "green-flat", "Flat green");
        result.Add(new(flat, TerrainGate.Evaluate(flat, readings, minRangeInches: MinRangeInches, minReadings: MinReadings), 0));
        return result;
    }

    /// <summary>
    /// Two or three MEASURE spots per pin on the green: 4 yds above the pin and 4 yds below on its fall line, and 4 yds
    /// across. None is held back until the user ticks it. On a flat spot the line runs long and short of the pin from the tee.
    /// </summary>
    public static IReadOnlyList<MeasureSpot> SuggestSpots(HoleModel hole, GreenSurface green)
    {
        var list = new List<MeasureSpot>();
        foreach (var pin in hole.Pins)
        {
            var p = pin.Frame; var s = green.Slope(p);
            var downhill = s.Length > 0.005 ? new FramePoint(-s.Lat, -s.Along) * (1 / s.Length) : (p * (1 / Math.Max(p.Length, 1e-6))) * -1;
            var across = new FramePoint(downhill.Along, -downhill.Lat);
            FramePoint Place(FramePoint dir)
            {
                for (double d = 4; d >= 1.5; d -= 0.5) { var q = p + dir * d; if (green.OnGreen(q)) return q; }
                return p + dir * 1.5;
            }
            var spots = new[] { ("a", Place(downhill * -1), false), ("b", Place(downhill), false), ("c", Place(across), false) };
            foreach (var (tag, at, held) in spots)
                list.Add(new MeasureSpot($"P{pin.Number}{tag}", at, (int)Math.Round(at.Length), green.HeightFt(at), held));
        }
        return list;
    }
}
