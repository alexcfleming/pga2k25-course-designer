using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

/// <summary>
/// Small flatten stamps carry the game's ground height where they were clicked (TerrainModelOptions.FlattenSamples).
/// When the replay's ground disagrees, it is shifted around the stamp, so the flatten doesn't build a table.
/// </summary>
public class FlattenSampleTests
{
    const double Inches = 39.3700787;

    // A 10 m plateau (round disc, unknown type), then a soft round flatten (type 54) at (0, 0) to `value`.
    static CourseModel Plateau(double value) => new()
    {
        Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49, Holes = [],
        SurfaceSplines = [], SurfaceBrushes = [], PlacedObjects = [],
        HeightStamps =
        [
            new HeightStamp(0, 1, 0, new Vec2(0, 0), 200, 0, 10),
            new HeightStamp(1, 0, 54, new Vec2(0, 0), 7.1, 0, value),
        ],
    };

    static double Crown(CourseModel course, TerrainModelOptions options)
    {
        var f = TerrainReplay.Build(course, new Vec2(-15, -15), new Vec2(15, 15), options);
        var ring = Enumerable.Range(0, 16).Select(i => new Vec2(Math.Cos(i * Math.PI / 8), Math.Sin(i * Math.PI / 8)) * 6);
        return (f.Sample(new Vec2(0, 0)) - ring.Average(f.Sample)) * Inches;
    }

    [Fact]
    public void SmallFlatten_ShiftsTheGroundInsteadOfBuildingATable()
    {
        var course = Plateau(10.6);
        double without = Crown(course, new TerrainModelOptions { FlattenSamples = false });
        double with = Crown(course, new TerrainModelOptions());
        Assert.True(without > 12, $"{without:0.0} in without");
        Assert.True(with < without / 2, $"{with:0.0} in with, {without:0.0} in without");
        Assert.Equal(0.6, TerrainReplay.FlattenShifts(course, new TerrainModelOptions())[1], 2);
    }

    [Fact]
    public void FlattenFarFromTheGround_IsASetHeight_AndLeftAlone()
    {
        var course = Plateau(12);   // 2 m above: past FlattenSampleMaxMetres
        Assert.Equal(0, TerrainReplay.FlattenShifts(course, new TerrainModelOptions())[1]);
        Assert.Equal(Crown(course, new TerrainModelOptions { FlattenSamples = false }), Crown(course, new TerrainModelOptions()), 6);
    }

    [Fact]
    public void Shifts_DontDependOnTheWindow()
    {
        // A window that doesn't contain the flatten's centre still gets its shift.
        var course = Plateau(10.6);
        var o = new TerrainModelOptions { BlurSigmaMetres = 0 };
        var p = new Vec2(5, 0);
        double big = TerrainReplay.Build(course, new Vec2(-15, -15), new Vec2(15, 15), o).Sample(p);
        double small = TerrainReplay.Build(course, new Vec2(4, -1), new Vec2(6, 1), o).Sample(p);
        Assert.Equal(big, small, 3);
    }

    const string Sagan = "sagan-khushun-2026-10-26.course";

    [PrivateSampleFact(Sagan)]
    public void SaganGreen9_Pin1_IsNotCrowned()
    {
        // In game pin 1 sits in a gentle hollow; the replay showed a 17.6 in crown, built by two 7.1 m flatten dabs
        // (#2070, #2072) on ground the big sculpting brushes had left 0.84 m too low.
        var course = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, Sagan)).ReadModel();
        var pin = course.Holes.Single(h => h.Number == 9).Pins[0];
        var ring = Enumerable.Range(0, 16).Select(i => pin + new Vec2(Math.Cos(i * Math.PI / 8), Math.Sin(i * Math.PI / 8)) * 6).ToList();
        double CrownAt(TerrainModelOptions o)
        {
            var f = TerrainReplay.Build(course, pin - new Vec2(20, 20), pin + new Vec2(20, 20), o);
            return (f.Sample(pin) - ring.Average(f.Sample)) * Inches;
        }
        Assert.True(CrownAt(new TerrainModelOptions { FlattenSamples = false }) > 12);
        Assert.InRange(CrownAt(new TerrainModelOptions()), -6, 4);
    }

    [PrivateSampleFact(Sagan)]
    public void SaganGreen9_MatchesInGameSpotElevations()
    {
        // Overhead capture of green 9 with 14 measure labels, zeroed on the pin 1 cup (samples/calibration/sagan-khushun).
        // The green is nearly flat in game; the replay was 10.3 in RMS (49 % of range) without flatten samples.
        var course = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, Sagan)).ReadModel();
        var captures = Calibration.LoadCaptures(Path.Combine(TestPaths.RepoRoot, "samples", "calibration", "sagan-khushun", "captures.json"));
        var without = Calibration.Evaluate(course, captures, new GreensBookOptions { Terrain = new TerrainModelOptions { FlattenSamples = false } }).Single();
        var with = Calibration.Evaluate(course, captures, new GreensBookOptions()).Single();
        Assert.True(with.PinFitMetres < 0.5, $"pins fit to {with.PinFitMetres:0.00} m");
        Assert.True(without.RmsInches > 9, $"{without.RmsInches:0.0} in without");
        Assert.True(with.RmsInches < 6, $"{with.RmsInches:0.0} in with");   // 5.4 in
    }
}
