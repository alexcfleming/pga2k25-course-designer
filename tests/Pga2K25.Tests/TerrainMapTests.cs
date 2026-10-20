using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
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
    public void LargeBrushSoftening_FlattensGreenSites_InTheDiscFallback()
    {
        // The old round-disc model: with the greens-book hardness on every brush, big stamps make circular cliffs.
        var hard = Survey(new TerrainMapOptions { Terrain = new() { CellMetres = 2, BrushShapes = false }, ContourIntervalFeet = 5 });
        var soft = Survey(new TerrainMapOptions { Terrain = new() { CellMetres = 2, BrushShapes = false, LargeBrushHardness = 0.3 }, ContourIntervalFeet = 5 });
        Assert.True(soft.Holes.Average(h => h.GreenSiteSlopePct) < hard.Holes.Average(h => h.GreenSiteSlopePct));
    }

    [Fact]
    public void Sea_IsDetectedFromOceanPropsAndNothingBelowItIsContoured()
    {
        Assert.Equal(0, TerrainSurvey.OceanPropLevel(Course.Value)!.Value, 3);
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
    public void PaintedWater_IsOneContinuousStroke()
    {
        var s = Berwick.Value;
        Assert.Equal(55, s.Course.WaterBrushes.Count);                     // a 700 m stream at 8.5 m
        Assert.Equal(0, s.SeaLevelMetres);                                   // no ocean props: the game's sea plane
        Assert.NotEmpty(s.PaintedWaterEdges);
        int wet = s.PaintedWater.Count(w => w);
        // About 700 m long and 10 m wide: the dabs are joined into one stroke, not a string of beads.
        Assert.InRange(wet * s.Field.Cell * s.Field.Cell, 6000, 9000);
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void WaterPlanes_AreClippedToTheGround_OnTheTerrainMap()
    {
        var s = TerrainSurvey.Build(WaterTests.LoadLuke(),
            new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = 1 } });
        var water = Assert.IsType<WaterMap>(s.InlandWater);
        Assert.All(water.Strokes, k => Assert.Equal(WaterKind.Plane, k.Kind));
        Assert.Equal(7, water.Bodies.Count);
        Assert.Single(water.Bodies, b => b.ChannelMetres > 0);                 // the hole 1 creek, joined up
        Assert.False(s.IsWet(s.Course.Water[0].Position));                     // the ring pond's island
        Assert.Null(s.SeaLevelMetres);                                         // inland water doesn't set the datum
        Assert.Empty(s.Layers.WaterRibbons);                                   // the narrow blue lines are paths
    }

    [Fact]
    public void GreenSite_SeparatesTiltFromLumpiness()
    {
        // Murray Estate 8 is nearly level overall but uneven; Murray 4 is an even slope.
        var s = Survey("murray-estate.course");
        var h8 = s.Holes.Single(h => h.Hole.Number == 8);
        var h4 = s.Holes.Single(h => h.Hole.Number == 4);
        Assert.True(h8.GreenSiteGradePct < 4 && h8.GreenSiteReliefFeet > 3, $"{h8.GreenSiteGradePct} / {h8.GreenSiteReliefFeet}");
        Assert.True(h4.GreenSiteGradePct > 12 && h4.GreenSiteReliefFeet < 3, $"{h4.GreenSiteGradePct} / {h4.GreenSiteReliefFeet}");
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

public class BrushShapeTests
{
    [Theory]
    [InlineData(8, 0)]      // hard circle, top-left of page 1
    [InlineData(37, 29)]
    [InlineData(39, 30)]    // rock speckle: type 38 isn't in the palette
    [InlineData(54, 45)]    // the soft round
    [InlineData(72, 63)]    // the square that flattens a blank plot
    [InlineData(77, 68)]
    public void Type_MapsToPaletteSlot(int type, int slot) => Assert.Equal(slot, Pga2K25.GreensBook.BrushShape.Slot(type));

    [Fact]
    public void Type_OutsideThePalette_HasNoShape()
    {
        Assert.Null(Pga2K25.GreensBook.BrushShape.Slot(38));
        Assert.Null(Pga2K25.GreensBook.BrushShape.Slot(7));
        Assert.Null(Pga2K25.GreensBook.BrushShape.Slot(78));
        Assert.NotNull(Pga2K25.GreensBook.BrushShape.ForType(54));
    }

    [Fact]
    public void TestPlot_RaisedDabsAreTheBrushSize_OnFlatGround()
    {
        // One raise dab of each brush at +50 ft ("100 yd" in the designer, stored as scale 17.81) on a plot
        // flattened by 1000 m squares. The hard circle (type 73) spans about ±0.9 × scale: ~32 m across, as in game.
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "brush-test-plot.course")).ReadModel();
        var circle = course.HeightStamps.Single(s => s.Type == 73);
        var f = Pga2K25.GreensBook.TerrainReplay.Build(course, circle.Position - new Vec2(40, 40), circle.Position + new Vec2(40, 40),
            new Pga2K25.GreensBook.TerrainModelOptions { CellMetres = 0.5 });
        double ground = f.Sample(circle.Position + new Vec2(35, 0)), top = f.Sample(circle.Position);
        Assert.Equal(1.33, ground, 2);                                      // the last base square's level
        Assert.InRange(top - ground, 0.9 * circle.Value, 1.0 * circle.Value);
        double Rise(double r) => f.Sample(circle.Position + new Vec2(r, 0)) - ground;
        Assert.True(Rise(12) > 0.8 * circle.Value && Rise(19) < 0.2 * circle.Value, $"{Rise(12):0.0} / {Rise(19):0.0}");
    }
}

public class RouteHeightTests
{
    /// <summary>
    /// Height change along each route leg measured in game (samples/calibration/route-heights) against the model:
    /// the check that the terrain away from greens is right.
    /// </summary>
    static (double Rms, int Count) Score(string course, string legs)
    {
        var model = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, course)).ReadModel();
        var rows = File.ReadAllLines(Path.Combine(TestPaths.SamplesDir, "..", "calibration", "route-heights", legs))
            .Where(l => l.Length > 0 && char.IsDigit(l[0])).Select(l => l.Split(',')).ToList();
        var pts = model.Holes.SelectMany(h => h.Route).ToList();
        var pad = new Vec2(5, 5);
        var f = Pga2K25.GreensBook.TerrainReplay.Build(model, new Vec2(pts.Min(p => p.X), pts.Min(p => p.Z)) - pad,
            new Vec2(pts.Max(p => p.X), pts.Max(p => p.Z)) + pad, new Pga2K25.GreensBook.TerrainModelOptions { CellMetres = 1 });
        double sum = 0;
        foreach (var r in rows)
        {
            var route = model.Holes.Single(h => h.Number == int.Parse(r[0])).Route;
            var (a, b) = r[1] == "tee-aim" ? (route[0], route[1]) : (route[^2], route[^1]);
            double err = (f.Sample(b) - f.Sample(a)) * 3.2808399 - double.Parse(r[3], System.Globalization.CultureInfo.InvariantCulture);
            sum += err * err;
        }
        return (Math.Sqrt(sum / rows.Count), rows.Count);
    }

    [Fact]
    public void BerwickCoast_MatchesInGameRouteHeights()
    {
        var (rms, n) = Score("berwick-coast.course", "berwick-coast.csv");
        Assert.Equal(36, n);
        Assert.True(rms < 3.5, $"RMS {rms:0.0} ft (the old round-disc model was 22 ft)");
    }

    [Fact]
    public void MurrayEstate_MatchesInGameRouteHeights()
    {
        var (rms, n) = Score("murray-estate.course", "murray-estate.csv");
        Assert.Equal(8, n);   // hole 3 aim → green measured twice: its green is built with textured raise brushes
        Assert.True(rms < 10, $"RMS {rms:0.0} ft (the old round-disc model was 15 ft)");
    }
}

public class MapReadBackTests
{
    [Fact]
    public void Projection_UnmapIsTheInverseOfMap()
    {
        var p = Pga2K25.Maps.MapProjection.Fit([new Vec2(-300, -200), new Vec2(400, 500)], SkiaSharp.SKRect.Create(10, 20, 800, 600),
            Pga2K25.Maps.MapProjection.NorthUpRotation, padding: 1.0);
        foreach (var w in new[] { new Vec2(0, 0), new Vec2(-250.5, 130.25), new Vec2(399, -199) })
        {
            var back = p.Unmap(p.Map(w));
            Assert.Equal(w.X, back.X, 3);
            Assert.Equal(w.Z, back.Z, 3);
        }
    }

    [Fact]
    public void Renderer_ReportsTheMapFrameAndGridSquares()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course")).ReadModel();
        var o = new TerrainMapOptions { PixelsPerMetre = 1, Terrain = new TerrainMapOptions().Terrain with { CellMetres = 4 } };
        var s = TerrainSurvey.Build(course, o);
        using var r = new TerrainMapRenderer(o);
        var (map, projection) = r.MapFrame(s);
        Assert.Equal("A1", r.GridReference(s, new SkiaSharp.SKPoint(map.Left + 1, map.Top + 1)));
        Assert.Null(r.GridReference(s, new SkiaSharp.SKPoint(map.Left - 5, map.Top - 5)));
        // The first hole's tee maps onto the map and reads back to the same ground.
        var tee = s.Holes[0].Route[0];
        var px = projection.Map(tee);
        Assert.True(map.Contains(px));
        Assert.Equal(s.Feet(tee), s.Feet(projection.Unmap(px)), 1);
    }
}
