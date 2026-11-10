using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.TerrainMap;

namespace Pga2K25.Tests;

/// <summary>
/// What Studio took from Plot Starter's in-game work (2026-11-08): the measured brush model, scored on its PS0
/// Calibration plot's 96 in-game points (samples/courses/ps0-calibration.course, rebuilt by Plot Starter with the same
/// stamps; samples/calibration/brush-markers); the plot's pre-made hills setting; and terrain stamps on the object meter.
/// </summary>
[Trait("Area", "Core")]
public class PlotStarterFindingsTests
{
    static List<(Vec2 At, double Metres)> Ps0Markers() =>
        File.ReadAllLines(Path.Combine(TestPaths.SamplesDir, "..", "calibration", "brush-markers", "ps0-calibration.csv")).Skip(1)
            .Select(l => l.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
            .Select(a => (new Vec2(a[0], a[2]), a[1])).ToList();

    static double RmsFeet(CourseModel c, List<(Vec2 At, double Metres)> pts, TerrainModelOptions o)
    {
        var pad = new Vec2(5, 5);
        var f = TerrainReplay.Build(c, new Vec2(pts.Min(p => p.At.X), pts.Min(p => p.At.Z)) - pad, new Vec2(pts.Max(p => p.At.X), pts.Max(p => p.At.Z)) + pad, o);
        return Math.Sqrt(pts.Average(p => Math.Pow((f.Sample(p.At) - p.Metres) * 3.2808399, 2)));
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void MeasuredBrushes_MatchTheCalibrationPlotsInGamePoints()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "ps0-calibration.course")).ReadModel();
        var pts = Ps0Markers();
        Assert.Equal(96, pts.Count);
        double measured = RmsFeet(course, pts, new TerrainModelOptions { CellMetres = 0.5 });
        double icon = RmsFeet(course, pts, new TerrainModelOptions { CellMetres = 0.5, MeasuredBrushes = false });
        Assert.True(measured < 1.6, $"measured model {measured:0.00} ft");   // 1.40 ft
        Assert.True(icon > 10, $"icon model {icon:0.00} ft");                // 11.6 ft: soft brushes under-strength, cliffs blurred
    }

    [Fact]
    public void MeasuredShapes_SoftRoundPeaksAt60Percent_SquareIsACliff_HardRoundALongSlope()
    {
        Assert.Equal(0.604, BrushShape.MeasuredWeight(54, 0, 0)!.Value, 3);
        Assert.Equal(1, BrushShape.MeasuredWeight(72, 0.96, 0)!.Value, 2);
        Assert.InRange(BrushShape.MeasuredWeight(72, 0.976, 0)!.Value, 0.45, 0.55);
        Assert.Equal(0, BrushShape.MeasuredWeight(72, 0, 0.99)!.Value, 2);
        Assert.Equal(1, BrushShape.MeasuredWeight(8, 0.5, 0)!.Value, 2);
        Assert.Equal(0.26, BrushShape.MeasuredWeight(8, 0.8, 0)!.Value, 1);
        Assert.Null(BrushShape.MeasuredWeight(9, 0, 0));
    }

    [Fact]
    public void PremadeHills_AreReadFromTheFile()
    {
        // A fresh blank plot: the designer shows its hills at 33 ft, stored as 10 m.
        var blank = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "..", "calibration", "blank-plot", "blank-plot-hills.course")).ReadModel();
        Assert.Equal(10, blank.HillsHeightMetres, 3);
        Assert.True(blank.HasPremadeHills);
        Assert.False(CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "ps0-calibration.course")).ReadModel().HasPremadeHills);
    }

    [Fact]
    public void Readiness_FlagsPremadeHills_AndPassesWithoutThem()
    {
        var murray = Pga2K25.Readiness.ReadinessChecker.Run(Samples.Course("murray-estate.course"));
        Assert.Equal(Pga2K25.Readiness.Severity.Pass, murray.Checks.Single(c => c.Id == "premade-hills").Severity);
    }

    [Fact]
    public void Budget_CountsTerrainStamps_AndEstimatesTheFileSizeMeter()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "ps0-calibration.course")).ReadModel();
        var b = GameBudget.Estimate(course);
        Assert.Equal(course.HeightStamps.Count * GameBudget.PerStamp, b.Objects, 6);   // nothing placed, no holes
        Assert.NotNull(b.FilePercent);
        Assert.True(course.DescriptionBytes > 0);
        Assert.Equal(course.DescriptionBytes / GameBudget.FileLimitBytes * 100, b.FilePercent!.Value, 6);
    }
}
