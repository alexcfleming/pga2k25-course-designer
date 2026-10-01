using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Tests;

public class TerrainMapTests
{
    static readonly string SaganPath = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
    static readonly Lazy<CourseModel> Course = new(() => CourseFile.Load(SaganPath).ReadModel());

    // A coarse raster keeps these quick; the default settings are exercised by the build test.
    static TerrainSurvey Survey(TerrainMapOptions? o = null) =>
        TerrainSurvey.Build(Course.Value, o ?? new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = 2 } });

    [Theory]
    [InlineData(40, 60, 1)]
    [InlineData(100, 60, 2)]
    [InlineData(213, 60, 5)]
    [InlineData(751, 60, 20)]
    public void ChooseInterval_PicksSmallestNiceStepThatFits(double relief, int levels, double expected) =>
        Assert.Equal(expected, TerrainSurvey.ChooseInterval(relief, levels));

    [Fact]
    public void Survey_RoutesEveryHoleWithScorecardYards()
    {
        var s = Survey();
        Assert.Equal(18, s.Holes.Count);
        foreach (var h in s.Holes)
        {
            Assert.Equal((int)Math.Round(h.Hole.TeeSetLength(h.Hole.Tees.Count - 1) * TerrainSurvey.YardsPerMetre), h.Yards);
            Assert.Equal(h.Route.Count - 1, h.LegYards.Count);
            Assert.InRange(h.TeeFeet, 0, s.MaxFeet);
            Assert.InRange(h.GreenFeet, 0, s.MaxFeet);
        }
        Assert.Equal(7266, s.Holes.Sum(h => h.Yards));
        Assert.True(s.MaxFeet > 100);
    }

    [Fact]
    public void Contours_AreIntervalMultiplesWithEveryFifthAnIndex()
    {
        var s = Survey();
        Assert.NotEmpty(s.Contours);
        foreach (var c in s.Contours)
        {
            double k = c.Feet / s.IntervalFeet;
            Assert.Equal(Math.Round(k), k, 6);
            Assert.Equal(Math.Round(k) % s.IndexEvery == 0, c.Index);
        }
    }

    [Fact]
    public void LargeBrushSoftening_FlattensGreenSites()
    {
        // With the greens-book hardness on every brush, big sculpting stamps make circular cliffs.
        var hard = Survey(new TerrainMapOptions { Terrain = new() { CellMetres = 2 }, ContourIntervalFeet = 5 });
        var soft = Survey(new TerrainMapOptions { Terrain = new() { CellMetres = 2, LargeBrushHardness = 0.3 }, ContourIntervalFeet = 5 });
        Assert.True(soft.Holes.Average(h => h.GreenSiteSlopePct) < hard.Holes.Average(h => h.GreenSiteSlopePct));
    }

    [Fact]
    public void Sea_IsDetectedFromOceanPropsAndNothingBelowItIsContoured()
    {
        Assert.Equal(0, TerrainSurvey.DetectSeaLevel(Course.Value)!.Value, 3);
        var s = Survey();
        Assert.NotNull(s.SeaLevelMetres);
        Assert.Equal(s.SeaLevelMetres!.Value, s.DatumMetres, 6);          // elevations are above sea level
        Assert.NotEmpty(s.Shoreline);
        Assert.All(s.Contours, c => Assert.True(c.Feet > 0));
        Assert.All(s.Holes, h => Assert.False(s.IsWater(s.Field.Sample(h.Route[0]))));   // every tee is dry

        var dry = Survey(new TerrainMapOptions { Terrain = new() { CellMetres = 2, LargeBrushHardness = 0.3 }, ShowWater = false });
        Assert.Null(dry.SeaLevelMetres);
        Assert.Empty(dry.Shoreline);
    }

    [Fact]
    public void Build_WritesOnePngWithMapAndPanel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-terrain-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = TerrainMapBuilder.Build(SaganPath, dir, new TerrainMapOptions { PixelsPerMetre = 1 });
            Assert.True(File.Exists(result.PngPath));
            using var bitmap = SKBitmap.Decode(result.PngPath);
            Assert.Equal(result.Size.Width, bitmap.Width);
            Assert.True(bitmap.Width > result.Survey.Max.X - result.Survey.Min.X);   // map plus side panel
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
