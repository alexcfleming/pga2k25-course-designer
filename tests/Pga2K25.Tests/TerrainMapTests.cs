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

public class BlankPlotTests
{
    [Fact]
    public void BlankPlot_MapsTheWholePlotWithLandmarksAndAPlateauInterval()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "falcons-nest.course")).ReadModel();
        Assert.Empty(course.Holes);
        var s = TerrainSurvey.Build(course, new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = 2 } });
        Assert.Empty(s.Holes);
        Assert.True(s.Max.X - s.Min.X > 1900);                              // the whole 2 km plot
        Assert.Contains(s.Landmarks, l => l.Name == "Clubhouse");
        Assert.Contains(s.Landmarks, l => l.Name == "Driving range");
        Assert.Contains(s.Landmarks, l => l.Name == "Range marker" && l.Label == "150");
        // The plateau around the clubhouse sets the interval, not the 800 ft of cliffs around it.
        Assert.True(s.IntervalFeet <= 5, $"interval {s.IntervalFeet}");
        Assert.True(s.MaxFeet / (s.IntervalFeet * s.IndexEvery) <= 120);
    }
}

public class WaterAndGreenSiteTests
{
    static TerrainSurvey Survey(string file) =>
        TerrainSurvey.Build(CourseFile.Load(Path.Combine(TestPaths.SamplesDir, file)).ReadModel(),
            new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = 2 } });

    static readonly Lazy<TerrainSurvey> Berwick = new(() => Survey("berwick-coast.course"));

    [Fact]
    public void PaintedWater_IsDrawnWhereTheGroundIsBelowItsLevel()
    {
        var s = Berwick.Value;
        Assert.Equal(55, s.Course.WaterBrushes.Count);                     // a 700 m stream at 8.5 m
        Assert.Null(s.SeaLevelMetres);
        Assert.NotEmpty(s.PaintedWaterEdges);
        Assert.NotEmpty(s.PaintedWaterFootprint);
        int wet = 0;
        for (int i = 0; i < s.PaintedWater.Length; i++)
        {
            if (!s.PaintedWater[i]) continue;
            wet++;
            Assert.True(s.Field.Values[i] < 8.51);
        }
        Assert.InRange(wet * s.Field.Cell * s.Field.Cell, 1000, 7000);     // the dabs are joined into a stroke, not beads
    }

    [Fact]
    public void GreenSite_SeparatesTiltFromLumpiness()
    {
        // Berwick 11 sits among dune mounds: nearly level overall but very uneven. Berwick 8 is an even, steep slope.
        var s = Berwick.Value;
        var h11 = s.Holes.Single(h => h.Hole.Number == 11);
        var h8 = s.Holes.Single(h => h.Hole.Number == 8);
        Assert.True(h11.GreenSiteGradePct < 8 && h11.GreenSiteReliefFeet > 10, $"{h11.GreenSiteGradePct} / {h11.GreenSiteReliefFeet}");
        Assert.True(h8.GreenSiteGradePct > 25, $"{h8.GreenSiteGradePct}");
    }

    [Fact]
    public void GreenSite_UsesADrawnGreenAtTheAnchor()
    {
        // Murray Estate 1's route ends 11 m off its drawn green's centre, beside raised pads.
        var s = Survey("murray-estate.course");
        var h1 = s.Holes.Single(h => h.Hole.Number == 1);
        Assert.True(h1.GreenSiteIsDrawn);
        Assert.True(Vec2.Distance(h1.GreenSiteCentre, h1.Hole.GreenAnchor) > 5);
        Assert.Contains(s.Layers.Greens, g => Geometry.Contains(g, h1.GreenSiteCentre));
        Assert.True(h1.GreenSiteGradePct < 5, $"{h1.GreenSiteGradePct}");
        Assert.All(s.Holes.Where(h => !h.GreenSiteIsDrawn), h => Assert.Equal(h.Hole.GreenAnchor, h.GreenSiteCentre));
    }
}

public class HoleSheetTests
{
    static readonly string SaganPath = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
    static readonly Lazy<CourseModel> Course = new(() => CourseFile.Load(SaganPath).ReadModel());

    static HoleSheet Sheet(int hole, HoleSheetOptions? o = null)
    {
        var course = Course.Value;
        return HoleSheet.Build(course, Pga2K25.Maps.CourseLayers.From(course), course.Holes.Single(h => h.Number == hole),
            o ?? new HoleSheetOptions { CellMetres = 1 }, datumMetres: 0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(12)]
    public void Orientation_MatchesTheYardageBook(int hole)
    {
        var course = Course.Value;
        var analysis = Pga2K25.YardageBook.HoleAnalysis.Analyze(course, Pga2K25.Maps.CourseLayers.From(course),
            course.Holes.Single(h => h.Number == hole), new Pga2K25.YardageBook.YardageOptions(), 0.4);
        var sheet = Sheet(hole);
        Assert.Equal(analysis.RotationDegrees, sheet.RotationDegrees, 6);

        // Back tee at the bottom of the map, green near the top.
        var p = sheet.Projection(HoleSheet.MapRectPoints);
        var tee = p.Map(sheet.BackTee);
        var green = p.Map(sheet.Routing.Hole.GreenAnchor);
        Assert.True(green.Y < tee.Y);
        Assert.InRange(green.X - tee.X, -1, 1);
    }

    [Fact]
    public void Window_KeepsTheWholeRouteWithClearance()
    {
        var o = new HoleSheetOptions { CellMetres = 1 };
        var sheet = Sheet(12, o);
        foreach (var pt in sheet.Routing.Route) Assert.True(sheet.Contains(pt));
        Assert.True(sheet.Along.From <= -o.MetresBehindTee + 1e-6);
        Assert.Equal(HoleSheet.MapRectPoints.Width / HoleSheet.MapRectPoints.Height,
            (sheet.Across.To - sheet.Across.From) / (sheet.Along.To - sheet.Along.From), 6);
        Assert.True(sheet.Survey.IntervalFeet <= 2);    // finer than the plot map's 5 ft
    }

    [Fact]
    public void Build_WritesLetterPngAndPdf()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-sheets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = HoleSheetBuilder.Build(SaganPath, dir, new HoleSheetOptions { Dpi = 100, CellMetres = 1 }, [2]);
            using var bitmap = SKBitmap.Decode(result.PngPaths.Single());
            Assert.Equal((850, 1100), (bitmap.Width, bitmap.Height));
            // Letter in points (Skia writes e.g. 792.00006).
            var pdf = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(result.PdfPath!));
            var box = System.Text.RegularExpressions.Regex.Match(pdf, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]");
            Assert.True(box.Success);
            Assert.Equal(612, double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 2);
            Assert.Equal(792, double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 2);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
