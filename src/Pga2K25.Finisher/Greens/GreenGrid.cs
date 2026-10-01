using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Pins;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Greens;

/// <summary>
/// <c>_greens_book_green_grid</c>: the green sampled on a 0.35 m grid in approach-aligned (u = right,
/// v = toward the green) coordinates, gap-filled, Gaussian-smoothed, with slope in percent. Rows are v, columns u.
/// </summary>
public sealed class GreenGrid
{
    public const double Step = 0.35;

    public required Vec2 Center { get; init; }
    public required Vec2 Forward { get; init; }
    public required Vec2 Right { get; init; }
    public required double[] Us { get; init; }
    public required double[] Vs { get; init; }
    public required bool[,] Valid { get; init; }
    public required double[,] Heights { get; init; }
    public required double[,] Filled { get; init; }
    public required double[,] Smooth { get; init; }
    public required double[,] GradU { get; init; }
    public required double[,] GradV { get; init; }
    public required double[,] Slope { get; init; }
    public required double[] PolyU { get; init; }
    public required double[] PolyV { get; init; }

    public int Rows => Vs.Length;
    public int Cols => Us.Length;

    public Vec2 World(double u, double v) => new(Center.X + u * Right.X + v * Forward.X, Center.Z + u * Right.Z + v * Forward.Z);

    public IEnumerable<double> ValidValues(double[,] grid)
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (Valid[r, c]) yield return grid[r, c];
    }

    public static GreenGrid Build(JsonObject hole, GreenOutline green, MultiScaleTerrain sampler)
    {
        var center = green.Center;
        var forward = SmartPins.ApproachAxis(hole);
        var right = new Vec2(forward.Z, -forward.X);
        var polyU = green.Poly.Select(p => (p.X - center.X) * right.X + (p.Z - center.Z) * right.Z).ToArray();
        var polyV = green.Poly.Select(p => (p.X - center.X) * forward.X + (p.Z - center.Z) * forward.Z).ToArray();
        const double margin = 1.25;
        double umin = Math.Floor((polyU.Min() - margin) / Step) * Step, umax = Math.Ceiling((polyU.Max() + margin) / Step) * Step;
        double vmin = Math.Floor((polyV.Min() - margin) / Step) * Step, vmax = Math.Ceiling((polyV.Max() + margin) / Step) * Step;
        var us = NumPy.Arange(umin, umax + 0.25 * Step, Step);
        var vs = NumPy.Arange(vmin, vmax + 0.25 * Step, Step);
        int rows = vs.Length, cols = us.Length;

        var world = new List<Vec2>(rows * cols);
        foreach (double v in vs)
            foreach (double u in us)
                world.Add(new Vec2(center.X + u * right.X + v * forward.X, center.Z + u * right.Z + v * forward.Z));
        var samples = sampler.SampleMany(world);
        var heights = new double[rows, cols];
        var valid = new bool[rows, cols];
        int validCount = 0;
        Parallel.For(0, rows, r =>
        {
            for (int c = 0; c < cols; c++)
            {
                int k = r * cols + c;
                heights[r, c] = samples[k];
                valid[r, c] = Contains(green.Poly, world[k]) && double.IsFinite(samples[k]);
            }
        });
        foreach (bool b in valid) if (b) validCount++;
        if (validCount < 16) throw new InvalidOperationException("Green terrain sampling produced too few valid cells.");

        var filled = NumPy.FillNearest(heights, valid);
        var smooth = NumPy.GaussianFilter2D(filled, Math.Max(0.75, 0.75 / Step));
        var (gradV, gradU) = NumPy.Gradient(smooth, Step);
        var slope = new double[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                slope[r, c] = valid[r, c] ? 100.0 * double.Hypot(gradU[r, c], gradV[r, c]) : double.NaN;

        return new GreenGrid
        {
            Center = center, Forward = forward, Right = right, Us = us, Vs = vs, Valid = valid, Heights = heights,
            Filled = filled, Smooth = smooth, GradU = gradU, GradV = gradV, Slope = slope, PolyU = polyU, PolyV = polyV,
        };
    }
}
