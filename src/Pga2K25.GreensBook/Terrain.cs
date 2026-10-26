using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>
/// Tunables for the heightfield replay. Brushes are stamped with their real shapes (<see cref="BrushShape"/>);
/// the hardness settings only shape brushes of unknown type. Defaults are checked against in-game readings
/// (docs/greens-book.md, "Calibration"; docs/terrain-map.md, "Terrain model").
/// </summary>
public sealed record TerrainModelOptions
{
    /// <summary>
    /// Fraction of the brush radius at full strength before the smoothstep falloff (0 = cone-like, 1 = hard edge).
    /// Calibrated on Cassimbo holes 1, 7, 10, 13 against in-game spot elevations and grid slope colours.
    /// </summary>
    public double Hardness { get; init; } = 0.85;

    /// <summary>Gaussian blur sigma (m) applied when the course sets blurHeight; 0 disables.</summary>
    public double BlurSigmaMetres { get; init; } = 1.0;   // calibrated with Hardness

    /// <summary>
    /// Hardness for brushes at least <see cref="LargeBrushMetres"/> across; null uses <see cref="Hardness"/>.
    /// The greens calibration only constrains the flat tops of stamps, not how big sculpting brushes fall
    /// off, so plot-scale maps use a softer value to avoid circular terraces the game doesn't show.
    /// </summary>
    public double? LargeBrushHardness { get; init; }

    public double LargeBrushMetres { get; init; } = 40;

    /// <summary>
    /// Stamp each brush with its real shape (<see cref="BrushShape"/>: the designer's brush image over ±scale,
    /// rotated). False replays every brush as a round disc of diameter scale with the hardness falloff above,
    /// which is what the model did before the brush shapes were known. Unknown types always use the disc.
    /// </summary>
    public bool BrushShapes { get; init; } = true;

    /// <summary>
    /// Leave out stamps whose centre sits on the plot edge (±<see cref="PlotHalfMetres"/>). The designer clamps the
    /// centre of a brush dragged past the edge to the edge, and the game doesn't apply those stamps to the plot:
    /// Sangoma's six 1000 m lower squares centred on z = ±1000 would sink the ground 35–100 m below its 2,290
    /// in-game distance markers (RMS 5.7 m with them, 2.3 m without).
    /// </summary>
    public bool SkipEdgeStamps { get; init; } = true;

    /// <summary>Half the plot's width: stamp centres are clamped to ±this.</summary>
    public double PlotHalfMetres { get; init; } = 1000;

    /// <summary>
    /// Read each flatten stamp's value as the game's ground where it was clicked (the designer's flatten picks up the
    /// height under the cursor: 85 % of Sagan-Khushun's sub-4 m flatten dabs sit within 3 in of the modelled ground).
    /// Where the replay disagrees, the ground around the stamp is shifted by the gap before the stamp lands, so the
    /// flatten levels what is really out of level instead of building a table on an error left by the big sculpting
    /// brushes. Sagan-Khushun green 9 showed a 17 in crown at pin 1 without it; the game shows a gentle hollow.
    /// </summary>
    public bool FlattenSamples { get; init; } = true;

    /// <summary>Only flattens smaller than this (scale, m) are read as samples; big drags are often set to a height.</summary>
    public double FlattenSampleMaxScale { get; init; } = 8;

    /// <summary>The shift fades out (smoothstep) at this multiple of the stamp's scale.</summary>
    public double FlattenSampleReach { get; init; } = 2;

    /// <summary>Gaps larger than this (m) are taken as a set height, not a sample, and left alone.</summary>
    public double FlattenSampleMaxMetres { get; init; } = 1;

    /// <summary>Raster cell size in metres.</summary>
    public double CellMetres { get; init; } = 0.25;
}

/// <summary>A regular raster of elevations (m) over an X/Z window.</summary>
public sealed class Heightfield
{
    public Vec2 Origin { get; }
    public double Cell { get; }
    public int Width { get; }
    public int Height { get; }
    public double[] Values { get; }

    public Heightfield(Vec2 origin, double cell, int width, int height)
    {
        Origin = origin; Cell = cell; Width = width; Height = height;
        Values = new double[width * height];
    }

    public double this[int ix, int iz]
    {
        get => Values[iz * Width + ix];
        set => Values[iz * Width + ix] = value;
    }

    public Vec2 CellCentre(int ix, int iz) => new(Origin.X + (ix + 0.5) * Cell, Origin.Z + (iz + 0.5) * Cell);

    /// <summary>Bilinear sample at a world point (clamped to the raster).</summary>
    public double Sample(Vec2 p)
    {
        double fx = Math.Clamp((p.X - Origin.X) / Cell - 0.5, 0, Width - 1.001);
        double fz = Math.Clamp((p.Z - Origin.Z) / Cell - 0.5, 0, Height - 1.001);
        int x0 = (int)fx, z0 = (int)fz;
        double tx = fx - x0, tz = fz - z0;
        double a = this[x0, z0] * (1 - tx) + this[x0 + 1, z0] * tx;
        double b = this[x0, z0 + 1] * (1 - tx) + this[x0 + 1, z0 + 1] * tx;
        return a * (1 - tz) + b * tz;
    }

    /// <summary>Downhill-positive gradient (dh/dx, dh/dz) by central differences at a world point.</summary>
    public Vec2 Gradient(Vec2 p)
    {
        double h = Cell;
        return new Vec2(
            (Sample(p + new Vec2(h, 0)) - Sample(p - new Vec2(h, 0))) / (2 * h),
            (Sample(p + new Vec2(0, h)) - Sample(p - new Vec2(0, h))) / (2 * h));
    }
}

public static class TerrainReplay
{
    /// <summary>
    /// Rebuilds elevations over a window by replaying <c>height[]</c> in file order:
    /// tool 0 (flatten) blends toward the stamp's absolute value, tool 1 (raise) adds its value, both weighted
    /// by the brush's shape at its size and rotation (<see cref="StampShape"/>). Unknown types use a circular
    /// plateau + smoothstep falloff. With <see cref="TerrainModelOptions.FlattenSamples"/>, a small flatten first
    /// shifts the ground around it to its value (<see cref="FlattenShifts"/>).
    /// </summary>
    public static Heightfield Build(CourseModel course, Vec2 min, Vec2 max, TerrainModelOptions options)
    {
        double cell = options.CellMetres;
        double sigma = course.BlurHeight ? options.BlurSigmaMetres : 0;
        double margin = sigma * 3;
        var origin = new Vec2(min.X - margin, min.Z - margin);
        int w = (int)Math.Ceiling((max.X - min.X + 2 * margin) / cell);
        int h = (int)Math.Ceiling((max.Z - min.Z + 2 * margin) / cell);
        var field = new Heightfield(origin, cell, w, h);
        var values = field.Values;
        double hard = Math.Clamp(options.Hardness, 0, 0.98);
        double soft = Math.Clamp(options.LargeBrushHardness ?? options.Hardness, 0, 0.98);
        var shifts = options.FlattenSamples ? FlattenShifts(course, options) : null;

        for (int i = 0; i < course.HeightStamps.Count; i++)
        {
            var s = course.HeightStamps[i];
            if (!Applies(s, options)) continue;
            if (shifts is not null && shifts[i] != 0) Shift(field, s.Position, s.Scale * options.FlattenSampleReach, shifts[i]);
            if (options.BrushShapes && BrushShape.ForType(s.Type) is { } shape)
            {
                StampShape(field, s, shape);
                continue;
            }
            double r = s.Scale / 2;
            double inner = s.Scale >= options.LargeBrushMetres ? soft : hard;
            int x0 = Math.Max(0, (int)Math.Floor((s.Position.X - r - origin.X) / cell));
            int x1 = Math.Min(w - 1, (int)Math.Ceiling((s.Position.X + r - origin.X) / cell));
            int z0 = Math.Max(0, (int)Math.Floor((s.Position.Z - r - origin.Z) / cell));
            int z1 = Math.Min(h - 1, (int)Math.Ceiling((s.Position.Z + r - origin.Z) / cell));
            if (x0 > x1 || z0 > z1) continue;

            for (int iz = z0; iz <= z1; iz++)
            {
                double dz = origin.Z + (iz + 0.5) * cell - s.Position.Z;
                for (int ix = x0; ix <= x1; ix++)
                {
                    double dx = origin.X + (ix + 0.5) * cell - s.Position.X;
                    double d = Math.Sqrt(dx * dx + dz * dz) / r;
                    if (d >= 1) continue;
                    double t = Math.Clamp((1 - d) / (1 - inner), 0, 1);
                    double weight = t * t * (3 - 2 * t);
                    int k = iz * w + ix;
                    values[k] = s.Tool == 0 ? values[k] + (s.Value - values[k]) * weight : values[k] + s.Value * weight;
                }
            }
        }

        if (sigma > 0) GaussianBlur(field, sigma / cell);
        return field;
    }

    static bool Applies(HeightStamp s, TerrainModelOptions options) =>
        s.Scale > 0 && double.IsFinite(s.Value) && !(options.SkipEdgeStamps && IsOnPlotEdge(s, options.PlotHalfMetres));

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CourseModel, Dictionary<TerrainModelOptions, double[]>> ShiftCache = new();

    /// <summary>
    /// For each stamp (by its place in <c>height[]</c>), how far to shift the ground around it before it lands:
    /// the gap between a small flatten's value and the replayed ground at its centre, or 0. Worked out once per
    /// course along the whole replay at the stamp centres alone, so every window agrees, whatever it covers.
    /// The gaps ignore the final blur, which only softens.
    /// </summary>
    public static double[] FlattenShifts(CourseModel course, TerrainModelOptions options)
    {
        var byOptions = ShiftCache.GetOrCreateValue(course);
        // Only the settings that change the replay matter; the window and cell don't.
        var key = options with { CellMetres = 0, BlurSigmaMetres = 0 };
        lock (byOptions)
            if (byOptions.TryGetValue(key, out var cached)) return cached;

        var stamps = course.HeightStamps;
        var shifts = new double[stamps.Count];
        double hard = Math.Clamp(options.Hardness, 0, 0.98);
        double soft = Math.Clamp(options.LargeBrushHardness ?? options.Hardness, 0, 0.98);
        for (int i = 0; i < stamps.Count; i++)
        {
            var s = stamps[i];
            if (s.Tool != 0 || s.Scale >= options.FlattenSampleMaxScale || !Applies(s, options)) continue;
            double ground = 0;
            for (int j = 0; j < i; j++)
            {
                var t = stamps[j];
                if (!Applies(t, options)) continue;
                if (shifts[j] != 0) ground += shifts[j] * ShiftWeight(Vec2.Distance(s.Position, t.Position) / (t.Scale * options.FlattenSampleReach));
                double weight = Weight(t, s.Position, options, hard, soft);
                if (weight > 0) ground = t.Tool == 0 ? ground + (t.Value - ground) * weight : ground + t.Value * weight;
            }
            double gap = s.Value - ground;
            if (Math.Abs(gap) <= options.FlattenSampleMaxMetres) shifts[i] = gap;
        }
        lock (byOptions) byOptions[key] = shifts;
        return shifts;
    }

    static double ShiftWeight(double d)
    {
        if (d >= 1) return 0;
        double t = 1 - d;
        return t * t * (3 - 2 * t);
    }

    /// <summary>Raises the ground by <paramref name="by"/> at <paramref name="centre"/>, fading to nothing at <paramref name="reach"/>.</summary>
    static void Shift(Heightfield field, Vec2 centre, double reach, double by)
    {
        var origin = field.Origin;
        double cell = field.Cell;
        int x0 = Math.Max(0, (int)Math.Floor((centre.X - reach - origin.X) / cell));
        int x1 = Math.Min(field.Width - 1, (int)Math.Ceiling((centre.X + reach - origin.X) / cell));
        int z0 = Math.Max(0, (int)Math.Floor((centre.Z - reach - origin.Z) / cell));
        int z1 = Math.Min(field.Height - 1, (int)Math.Ceiling((centre.Z + reach - origin.Z) / cell));
        for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
            {
                double w = ShiftWeight(Vec2.Distance(field.CellCentre(ix, iz), centre) / reach);
                if (w > 0) field[ix, iz] += by * w;
            }
    }

    /// <summary>One stamp's weight at a world point: the same weights <see cref="Build"/> rasterises.</summary>
    static double Weight(HeightStamp s, Vec2 p, TerrainModelOptions options, double hard, double soft)
    {
        double dx = p.X - s.Position.X, dz = p.Z - s.Position.Z;
        if (Math.Abs(dx) > s.Scale * 1.5 || Math.Abs(dz) > s.Scale * 1.5) return 0;   // beyond any brush's reach
        if (options.BrushShapes && BrushShape.ForType(s.Type) is { } shape)
        {
            double half = s.Scale;
            double th = s.RotationY * Math.PI / 180, cos = Math.Cos(th), sin = Math.Sin(th);
            double gain = s.Tool == 0 && shape.Peak > 0 ? 1 / shape.Peak : 1;
            return Math.Min(1, shape.Weight((dx * cos - dz * sin) / half, (dx * sin + dz * cos) / half) * gain);
        }
        double d = Math.Sqrt(dx * dx + dz * dz) / (s.Scale / 2);
        if (d >= 1) return 0;
        double inner = s.Scale >= options.LargeBrushMetres ? soft : hard;
        double tt = Math.Clamp((1 - d) / (1 - inner), 0, 1);
        return tt * tt * (3 - 2 * tt);
    }

    /// <summary>The game's sea plane (world m): every plot has it, though it only shows where the ground dips below.</summary>
    public const double SeaPlaneMetres = 0;

    /// <summary>Sea level: the ocean props' height when the plot has any, otherwise the sea plane.</summary>
    public static double DetectSeaLevel(CourseModel course) => OceanPropLevel(course) ?? SeaPlaneMetres;

    /// <summary>Median explicit height of the ocean props (wave, churn, impact), or null if there are none.</summary>
    public static double? OceanPropLevel(CourseModel course)
    {
        var heights = course.PlacedObjects
            .Where(p => p.Height is not null && p.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Height!.Value).Order().ToList();
        return heights.Count == 0 ? null : heights[heights.Count / 2];
    }

    /// <summary>True when a stamp's centre was clamped to the plot edge (see <see cref="TerrainModelOptions.SkipEdgeStamps"/>).</summary>
    public static bool IsOnPlotEdge(HeightStamp s, double plotHalfMetres = 1000) =>
        Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Z)) >= plotHalfMetres - 0.1;

    /// <summary>
    /// One stamp with its brush image: weight = image brightness at the cell, the image spanning ±scale around
    /// the stamp, top towards +Z, turned by rotation.y as a Unity Y rotation (clockwise seen from above).
    /// Raise (tool 1) adds value × brightness, so dim (soft) brushes add less. Flatten (tool 0) moves the ground
    /// towards the value by brightness ÷ the brush's peak, so every flatten reaches its value at its centre.
    /// That split fits both Cassimbo's flattened greens and Berwick Coast's measured heights.
    /// </summary>
    static void StampShape(Heightfield field, HeightStamp s, BrushShape shape)
    {
        double half = s.Scale, reach = half * Math.Sqrt(2), cell = field.Cell;
        var origin = field.Origin;
        int w = field.Width, h = field.Height;
        int x0 = Math.Max(0, (int)Math.Floor((s.Position.X - reach - origin.X) / cell));
        int x1 = Math.Min(w - 1, (int)Math.Ceiling((s.Position.X + reach - origin.X) / cell));
        int z0 = Math.Max(0, (int)Math.Floor((s.Position.Z - reach - origin.Z) / cell));
        int z1 = Math.Min(h - 1, (int)Math.Ceiling((s.Position.Z + reach - origin.Z) / cell));
        if (x0 > x1 || z0 > z1) return;
        double th = s.RotationY * Math.PI / 180, cos = Math.Cos(th), sin = Math.Sin(th);
        double gain = s.Tool == 0 && shape.Peak > 0 ? 1 / shape.Peak : 1;
        var values = field.Values;
        Parallel.For(z0, z1 + 1, iz =>
        {
            double dz = origin.Z + (iz + 0.5) * cell - s.Position.Z;
            for (int ix = x0; ix <= x1; ix++)
            {
                double dx = origin.X + (ix + 0.5) * cell - s.Position.X;
                // World offset into brush axes: local +X = (cos, −sin), local +Z = (sin, cos).
                double u = (dx * cos - dz * sin) / half, v = (dx * sin + dz * cos) / half;
                double weight = Math.Min(1, shape.Weight(u, v) * gain);
                if (weight <= 0) continue;
                int k = iz * w + ix;
                values[k] = s.Tool == 0 ? values[k] + (s.Value - values[k]) * weight : values[k] + s.Value * weight;
            }
        });
    }

    /// <summary>Separable Gaussian blur in place; <paramref name="sigmaMetres"/> ≤ 0 does nothing.</summary>
    public static void Smooth(Heightfield field, double sigmaMetres)
    {
        if (sigmaMetres > 0) GaussianBlur(field, sigmaMetres / field.Cell);
    }

    static void GaussianBlur(Heightfield f, double sigmaCells)
    {
        int radius = (int)Math.Ceiling(sigmaCells * 3);
        var kernel = new double[2 * radius + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++) sum += kernel[i + radius] = Math.Exp(-i * i / (2 * sigmaCells * sigmaCells));
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        var tmp = new double[f.Values.Length];
        for (int z = 0; z < f.Height; z++)
            for (int x = 0; x < f.Width; x++)
            {
                double acc = 0;
                for (int k = -radius; k <= radius; k++)
                    acc += f[Math.Clamp(x + k, 0, f.Width - 1), z] * kernel[k + radius];
                tmp[z * f.Width + x] = acc;
            }
        for (int z = 0; z < f.Height; z++)
            for (int x = 0; x < f.Width; x++)
            {
                double acc = 0;
                for (int k = -radius; k <= radius; k++)
                    acc += tmp[Math.Clamp(z + k, 0, f.Height - 1) * f.Width + x] * kernel[k + radius];
                f[x, z] = acc;
            }
    }
}
