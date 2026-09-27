using System.Numerics;
using System.Text.Json;
using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>A spot elevation read off an in-game green screenshot (label text centre, pixels; signed inches).</summary>
public sealed record SpotLabel(double X, double Y, double Inches);

/// <summary>
/// One overhead green screenshot: the four pin cups (dark dot at each flagstick base) anchor the
/// pixel→world mapping; the labels are the in-game measure readings to compare against.
/// </summary>
public sealed record GreenCapture(int Hole, string? Image, IReadOnlyList<double[]> Cups, IReadOnlyList<SpotLabel> Labels);

public sealed record SpotResidual(SpotLabel Label, Vec2 World, double EdgeDistance, bool Used, double ModelInches, double Residual);

public sealed record HoleCalibration(int Hole, double PinFitMetres, double MetresPerPixel,
    double RmsInches, double RangeInches, IReadOnlyList<SpotResidual> Spots)
{
    public double PercentOfRange => RangeInches > 0 ? RmsInches / RangeInches * 100 : double.NaN;
}

public static class Calibration
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static List<GreenCapture> LoadCaptures(string path) =>
        JsonSerializer.Deserialize<List<GreenCapture>>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException("Capture file is empty.");

    /// <summary>
    /// Maps each capture onto the course and compares modelled elevations with the in-game labels.
    /// Labels closer than <paramref name="edgeExclusionMetres"/> to the green edge (or outside it) are
    /// reported but not scored, matching the book's own edge exclusion. Residuals are offset-free
    /// (the in-game 0 is arbitrary) and judged as a percentage of the green's own label range:
    /// under 10 % trustworthy, 10–18 % indicative, over 18 % check.
    /// </summary>
    public static List<HoleCalibration> Evaluate(CourseModel course, IEnumerable<GreenCapture> captures,
        GreensBookOptions options, double edgeExclusionMetres = 1.0)
    {
        var results = new List<HoleCalibration>();
        foreach (var cap in captures)
        {
            var hole = course.Holes.First(h => h.Number == cap.Hole);
            if (cap.Cups.Count != hole.Pins.Count)
                throw new InvalidDataException($"Hole {cap.Hole}: {cap.Cups.Count} cups captured but the course has {hole.Pins.Count} pins.");

            var map = PixelMap.FitToPins(cap.Cups.Select(c => (c[0], c[1])).ToList(), hole.Pins);
            var analysis = GreenAnalyzer.Analyze(course, hole, options);
            var field = analysis.Field;

            var spots = cap.Labels.Select(l =>
            {
                var w = map.ToWorld(l.X, l.Y);
                bool inside = Geometry.Contains(analysis.Outline, w);
                double edge = Geometry.DistanceToEdge(analysis.Outline, w) * (inside ? 1 : -1);
                double model = field.Sample(w) * 39.3700787;
                return (Label: l, World: w, Edge: edge, Used: edge >= edgeExclusionMetres, Model: model);
            }).ToList();

            var used = spots.Where(s => s.Used).ToList();
            double offset = used.Count > 0 ? used.Average(s => s.Label.Inches - s.Model) : 0;
            var residuals = spots.Select(s => new SpotResidual(s.Label, s.World, s.Edge, s.Used, s.Model + offset,
                s.Model + offset - s.Label.Inches)).ToList();
            double rms = used.Count > 0 ? Math.Sqrt(residuals.Where(r => r.Used).Average(r => r.Residual * r.Residual)) : double.NaN;
            double range = used.Count > 0 ? used.Max(s => s.Label.Inches) - used.Min(s => s.Label.Inches) : 0;
            results.Add(new HoleCalibration(cap.Hole, map.PinResidualMetres, map.MetresPerPixel, rms, range, residuals));
        }
        return results;
    }
}

/// <summary>
/// Similarity transform from screenshot pixels to world X/Z. Screen Y points down, so the
/// image is mirrored before fitting; the best of all pin↔cup assignments is kept.
/// </summary>
public sealed class PixelMap
{
    readonly Complex _scaleRotation, _pixelCentre, _worldCentre;
    public double PinResidualMetres { get; }
    public double MetresPerPixel => _scaleRotation.Magnitude;

    PixelMap(Complex sr, Complex pc, Complex wc, double residual)
    {
        _scaleRotation = sr; _pixelCentre = pc; _worldCentre = wc; PinResidualMetres = residual;
    }

    public Vec2 ToWorld(double px, double py)
    {
        var w = (new Complex(px, -py) - _pixelCentre) * _scaleRotation + _worldCentre;
        return new Vec2(w.Real, w.Imaginary);
    }

    public static PixelMap FitToPins(IReadOnlyList<(double X, double Y)> cups, IReadOnlyList<Vec2> pins)
    {
        PixelMap? best = null;
        foreach (var perm in Permutations(Enumerable.Range(0, pins.Count).ToArray()))
        {
            var p = perm.Select(i => new Complex(cups[i].X, -cups[i].Y)).ToArray();
            var w = pins.Select(v => new Complex(v.X, v.Z)).ToArray();
            var pc = Mean(p);
            var wc = Mean(w);
            Complex num = Complex.Zero, den = Complex.Zero;
            for (int i = 0; i < p.Length; i++)
            {
                num += Complex.Conjugate(p[i] - pc) * (w[i] - wc);
                den += Complex.Conjugate(p[i] - pc) * (p[i] - pc);
            }
            var sr = num / den;
            double res = Math.Sqrt(Enumerable.Range(0, p.Length).Average(i => Complex.Abs((p[i] - pc) * sr + wc - w[i]) is var e ? e * e : 0));
            if (best is null || res < best.PinResidualMetres) best = new PixelMap(sr, pc, wc, res);
        }
        return best!;
    }

    static Complex Mean(Complex[] v) => v.Aggregate(Complex.Zero, (a, b) => a + b) / v.Length;

    static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }
}
