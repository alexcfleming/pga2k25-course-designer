using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>
/// Tunables for the heightfield replay. The game's brush shapes, hardness and
/// post-processing are not stored in the .course file; defaults are fitted to
/// in-game readings (see docs/greens-book.md, "Calibration").
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
    /// tool 0 blends toward the stamp's absolute value, tool 1 adds its value,
    /// both weighted by a circular plateau + smoothstep falloff.
    /// Brush shape ids and rotation are ignored (all treated as round).
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
        double inner = Math.Clamp(options.Hardness, 0, 0.98);

        foreach (var s in course.HeightStamps)
        {
            double r = s.Scale / 2;
            if (r <= 0 || !double.IsFinite(s.Value)) continue;
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
