using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.Tests;

public class ScorecardMapTests
{
    static CourseModel Cassimbo() => CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "cassimbo.course")).ReadModel();

    [Fact]
    public void Layers_TraceTheKeyFeatures()
    {
        var layers = CourseLayers.From(Cassimbo());

        Assert.Equal(18, layers.Holes.Count);
        Assert.True(layers.Greens.Count >= 18);
        Assert.NotEmpty(layers.Grass);
        Assert.NotEmpty(layers.BunkerBrushes);
        Assert.NotEmpty(layers.WasteBrushes);
        // Routes start at the back tee and end at the green anchor.
        Assert.All(layers.Holes, h =>
        {
            Assert.Equal(h.Hole.Tees[^1], h.Route[0]);
            Assert.Equal(h.Hole.GreenAnchor, h.Route[^1]);
        });
    }

    [Fact]
    public void Projection_RoundTripsRotationAndFitsTheRect()
    {
        var pts = new List<Vec2> { new(0, 0), new(1000, 0), new(1000, 100), new(0, 100) };   // long thin east-west strip
        var portrait = new SKRect(0, 0, 400, 1000);

        double rot = MapProjection.BestRotation(pts, portrait);
        Assert.InRange(Math.Abs(rot - MapProjection.NorthUpRotation), 60, 90);   // turned to run up (or diagonally across) the page
        Assert.True(MapProjection.FitScale(pts, portrait, rot) > 2.4 * MapProjection.FitScale(pts, portrait, 0));

        var proj = MapProjection.Fit(pts, portrait, rot);
        foreach (var p in pts.Select(proj.Map))
        {
            Assert.InRange(p.X, portrait.Left - 0.5f, portrait.Right + 0.5f);
            Assert.InRange(p.Y, portrait.Top - 0.5f, portrait.Bottom + 0.5f);
        }
    }

    [Fact]
    public void Projection_NorthUpKeepsCompassNorthAtTheTop()
    {
        // Game compass: north = -Z, east = -X (fitted from the Cassimbo designer's bearings).
        var pts = new List<Vec2> { new(0, 0), new(100, 200) };
        var proj = MapProjection.Fit(pts, new SKRect(0, 0, 500, 500), MapProjection.NorthUpRotation);
        var north = proj.MapDirection(MapProjection.North);
        var east = proj.MapDirection(new Vec2(-1, 0));
        Assert.Equal(0, north.X, 5);
        Assert.Equal(-1, north.Y, 5);                      // canvas Y points down
        Assert.Equal(1, east.X, 5);                         // east to the right
    }

    [Fact]
    public void AutoRotation_KeepsNorthInTheUpperHalf()
    {
        var layers = CourseLayers.From(Cassimbo());
        var pts = layers.ExtentPoints().ToList();
        double rot = MapProjection.BestRotation(pts, new SKRect(0, 0, 800, 770));
        var proj = MapProjection.Fit(pts, new SKRect(0, 0, 800, 770), rot);
        Assert.True(proj.MapDirection(MapProjection.North).Y <= 0);
    }

    [Fact]
    public void Hole1_PlaysSouth_AsTheDesignerSheetSays()
    {
        var h = Cassimbo().Holes[0];
        var d = h.Route[1] - h.Tees[^1];
        double bearing = (Math.Atan2(-d.X, -d.Z) * 180 / Math.PI + 360) % 360;   // clockwise from north
        Assert.InRange(bearing, 175 - 20, 175 + 20);                              // sheet: tee bearing 175
    }

    [Fact]
    public void HoleData_ReadsTheDesignerSpreadsheet()
    {
        var data = HoleData.Load(Path.Combine(TestPaths.RepoRoot, "samples", "data", "cassimbo-hole-data.xlsx"));
        Assert.Equal(["Red", "Green", "White", "Blue"], data.TeeNames);
        Assert.Equal(18, data.Rows.Count);
        var h1 = data.For(1)!;
        Assert.Equal(4, h1.Par);
        Assert.Equal([360, 361, 365, 374], h1.Yards.Select(y => y ?? 0));
        Assert.Equal(175, h1.TeeBearing);
        Assert.Equal(7178, data.Rows.Sum(r => r.Yards[3] ?? 0));
    }

    [Fact]
    public void MergedBunkers_FuseDabsIntoFewerSmoothShapes()
    {
        var layers = CourseLayers.From(Cassimbo());
        var merged = layers.MergedBunkers();
        Assert.NotEmpty(merged);
        Assert.True(merged.Count < layers.BunkerBrushes.Count / 5, $"{merged.Count} shapes from {layers.BunkerBrushes.Count} dabs");
    }

    [Fact]
    public void Build_WritesBothThemes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = ScorecardMapBuilder.Build(Path.Combine(TestPaths.SamplesDir, "cassimbo.course"), dir,
                new ScorecardMapOptions(), [MapThemeKind.Vintage, MapThemeKind.Modern], pdf: true, scale: 1);

            Assert.Equal(4, result.Files.Count);
            foreach (var png in result.Files.Where(f => f.EndsWith(".png")))
            {
                using var bmp = SKBitmap.Decode(png);
                Assert.Equal(result.Landscape ? (1100, 850) : (850, 1100), (bmp.Width, bmp.Height));
            }
            Assert.All(result.Files.Where(f => f.EndsWith(".pdf")),
                f => Assert.Equal("%PDF-"u8.ToArray(), File.ReadAllBytes(f).Take(5).ToArray()));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Illustrated_IsVintageWithTheLandscape()
    {
        var (v, i) = (MapTheme.Vintage, MapTheme.Illustrated);
        Assert.True(i.Landscape);
        Assert.False(v.Landscape);
        Assert.Equal((v.Background, v.Ink, v.GrassFill, v.GreenFill, v.BunkerFill), (i.Background, i.Ink, i.GrassFill, i.GreenFill, i.BunkerFill));
        Assert.Same(i, MapTheme.For(MapThemeKind.Illustrated));
    }

    static Landscape LandscapeOf(string sample)
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, sample)).ReadModel();
        var layers = CourseLayers.From(course);
        var rect = new SKRect(40, 40, 794, 810);
        var proj = MapProjection.Fit(layers.ExtentPoints().ToList(), rect, MapProjection.NorthUpRotation);
        return Landscape.Build(course, proj, rect);
    }

    [Fact]
    public void Landscape_Cassimbo_HasSeaLinesBushStippleAndHillsButNoTrees()
    {
        using var l = LandscapeOf("cassimbo.course");
        Assert.NotEmpty(l.WaterLines);     // the sea off the west of the links
        Assert.True(l.Bushes.Count > 1000);
        Assert.Empty(l.Trees);
        Assert.True(l.HasHills);
        Assert.All(l.Hachures, h => Assert.InRange(h.Width, 0.18f, 0.55f));
    }

    [Fact]
    public void Landscape_InlandCourse_HasNoSeaLines()
    {
        // Murray Estate's ground never reaches the sea plane.
        using var l = LandscapeOf("murray-estate.course");
        Assert.Empty(l.WaterLines);
    }

    [Fact]
    public void Build_Illustrated_WritesItsOwnFilesWithTheLandscapeDrawn()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            var result = ScorecardMapBuilder.Build(path, dir, new ScorecardMapOptions(), [MapThemeKind.Vintage, MapThemeKind.Illustrated], pdf: true, scale: 1);
            Assert.Contains(result.Files, f => f.EndsWith("Scorecard Map (Illustrated).png"));
            Assert.Contains(result.Files, f => f.EndsWith("Scorecard Map (Illustrated).pdf"));
            // Same page, less bare parchment: the hachures, bushes and sea lines.
            static int Bare(string png)
            {
                var bg = MapTheme.Vintage.Background;
                using var bmp = SKBitmap.Decode(png);
                return bmp.Pixels.Count(c => Math.Abs(c.Red - bg.Red) + Math.Abs(c.Green - bg.Green) + Math.Abs(c.Blue - bg.Blue) < 6);
            }
            int vintage = Bare(result.Files.Single(f => f.EndsWith("(Vintage).png")));
            int illustrated = Bare(result.Files.Single(f => f.EndsWith("(Illustrated).png")));
            Assert.True(illustrated < vintage * 0.9, $"bare parchment: illustrated {illustrated:N0} px vs vintage {vintage:N0} px");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BarScaleRatio_IsRealDistanceOverPrintedLength()
    {
        // 100 yd = 3,600 in; drawn 1 in long (100 page units) is 1 : 3,600.
        Assert.Equal(3600, ScorecardMapRenderer.BarScaleRatio(100));
        Assert.Equal(6400, ScorecardMapRenderer.BarScaleRatio(56.5f));
    }

    [Fact]
    public void Build_PlanSheet_IsAlwaysLandscapeWithRedFlights()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            var options = new ScorecardMapOptions { Orientation = PageOrientation.Portrait, Credit = "Test Designer", Location = "Somewhere, Far Away" };
            var result = ScorecardMapBuilder.Build(path, dir, options, [MapThemeKind.PlanSheet], pdf: true, scale: 1);
            Assert.True(result.Landscape);
            Assert.Contains(result.Files, f => f.EndsWith("Scorecard Map (Plan Sheet).pdf"));
            using var bmp = SKBitmap.Decode(result.Files.Single(f => f.EndsWith("Scorecard Map (Plan Sheet).png")));
            Assert.Equal(1100, bmp.Width);
            var red = MapTheme.PlanSheet.RouteColour;
            Assert.True(bmp.Pixels.Count(c => Math.Abs(c.Red - red.Red) + Math.Abs(c.Green - red.Green) + Math.Abs(c.Blue - red.Blue) < 40) > 200);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
