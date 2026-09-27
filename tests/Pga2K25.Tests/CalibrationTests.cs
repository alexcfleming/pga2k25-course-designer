using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

/// <summary>
/// Regression guard for the terrain model: Cassimbo holes 1, 7, 10, 13 were captured in-game
/// with spot-elevation labels. Any model change must keep every hole in the "trustworthy" band.
/// </summary>
public class CalibrationTests
{
    static readonly string CapturesPath = Path.Combine(TestPaths.RepoRoot, "samples", "calibration", "cassimbo", "captures.json");

    [Fact]
    public void Cassimbo_DefaultModel_MatchesInGameSpotElevations()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "cassimbo.course")).ReadModel();
        var results = Calibration.Evaluate(course, Calibration.LoadCaptures(CapturesPath), new GreensBookOptions());

        Assert.Equal([1, 7, 10, 13], results.Select(r => r.Hole));
        Assert.All(results, r =>
        {
            Assert.True(r.PinFitMetres < 0.5, $"hole {r.Hole}: screenshot pins fit to {r.PinFitMetres:0.00} m");
            Assert.True(r.PercentOfRange < 10.5, $"hole {r.Hole}: spot RMS {r.PercentOfRange:0.0}% of range");
        });
    }

    [Fact]
    public void PixelMap_RecoversAKnownTransformAndPinOrder()
    {
        // World pins mapped to pixels by rotate 30°, 0.02 m/px, mirrored Y (screen down), then shuffled.
        var pins = new[] { new Vec2(10, 5), new Vec2(-4, 12), new Vec2(3, -8), new Vec2(15, -2) };
        double s = 0.02, a = Math.PI / 6;
        (double X, double Y) ToPixel(Vec2 w)
        {
            double u = (w.X * Math.Cos(-a) - w.Z * Math.Sin(-a)) / s, v = (w.X * Math.Sin(-a) + w.Z * Math.Cos(-a)) / s;
            return (u + 800, -v + 600);
        }
        var cups = new[] { pins[2], pins[0], pins[3], pins[1] }.Select(ToPixel).ToList();

        var map = PixelMap.FitToPins(cups, pins);

        Assert.True(map.PinResidualMetres < 1e-6);
        Assert.Equal(s, map.MetresPerPixel, 6);
        var back = map.ToWorld(ToPixel(new Vec2(7, 7)).X, ToPixel(new Vec2(7, 7)).Y);
        Assert.Equal(7, back.X, 6);
        Assert.Equal(7, back.Z, 6);
    }
}
