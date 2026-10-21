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
    /// plateau + smoothstep falloff.
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

        foreach (var s in course.HeightStamps)
        {
            if (s.Scale <= 0 || !double.IsFinite(s.Value)) continue;
            if (options.SkipEdgeStamps && IsOnPlotEdge(s, options.PlotHalfMetres)) continue;
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
