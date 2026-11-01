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
    public void HolesCard_RoundTripsAndNamesTheTable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var holes = new CourseHoles { TeeNames = ["Red", "Green", "White", "Blue"] };
            holes.Entry(3).Name = "  Fog Bank ";
            holes.Entry(1).Name = "Cacimbo";
            CourseHolesFile.Save(dir, holes);
            var back = CourseHolesFile.Load(dir);
            Assert.Equal(["Red", "Green", "White", "Blue"], back.Tees);
            Assert.Equal([1, 3], back.Holes.Select(h => h.Hole));
            Assert.Equal("Fog Bank", back.NameOf(3));
            Assert.Null(back.NameOf(2));
            Assert.True(back.HasNames);
            Assert.False(new CourseHoles { TeeNames = ["Red"] }.HasNames);

            // The Plan Sheet's table gains a Name column with names, and is unchanged without.
            string course = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            byte[] Draw(CourseHoles h) => File.ReadAllBytes(ScorecardMapBuilder.Build(course, dir, new ScorecardMapOptions { Holes = h },
                [MapThemeKind.PlanSheet], scale: 1).Files[0]);
            var named = Draw(back);
            var plain = Draw(new CourseHoles { TeeNames = back.TeeNames });
            var plainAgain = Draw(new CourseHoles { TeeNames = back.TeeNames, Holes = [new HoleEntry { Hole = 1, GreenNotes = "not a name" }] });
            Assert.NotEqual(named, plain);
            Assert.Equal(plain, plainAgain);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
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
    [Fact]
    public void ModernIllustrated_IsModernPaintedIn()
    {
        var (m, mi) = (MapTheme.Modern, MapTheme.ModernIllustrated);
        Assert.Same(mi, MapTheme.For(MapThemeKind.ModernIllustrated));
        Assert.Equal("Modern Illustrated", mi.Name);
        Assert.Equal((m.Ink, m.TitleFonts, m.BodyFonts), (mi.Ink, mi.TitleFonts, mi.BodyFonts));
        Assert.False(mi.Landscape);   // not the engraved landscape: it has its own scenery
    }

    [Fact]
    public void StripTeeSets_AreUpToFiveLongestFirst()
    {
        Assert.Equal([5, 4, 3, 2, 1], ScorecardMapRenderer.StripTeeSets(6));
        Assert.Equal([4, 3, 2, 1, 0], ScorecardMapRenderer.StripTeeSets(5));   // Devil's Perch has five
        Assert.Equal([3, 2, 1, 0], ScorecardMapRenderer.StripTeeSets(4));
        Assert.Equal([1, 0], ScorecardMapRenderer.StripTeeSets(2));
        Assert.Equal([0], ScorecardMapRenderer.StripTeeSets(1));
    }

    [Fact]
    public void Layers_Fairways_AreTheFairwayAreasOnly()
    {
        var course = Cassimbo();
        var layers = CourseLayers.From(course);
        Assert.NotEmpty(layers.Fairways);
        Assert.True(layers.Fairways.Count < layers.Grass.Count);
        Assert.All(layers.Fairways, f => Assert.Contains(f, layers.Grass));
        Assert.Equal(course.SurfaceSplines.Count(s => s.Closed && s.Surface == Surfaces.Fairway && s.Points.Count >= 2), layers.Fairways.Count);
    }

    static Scenery SceneryOf(string sample)
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, sample)).ReadModel();
        var layers = CourseLayers.From(course);
        var rect = new SKRect(30, 30, 1070, 640);
        var proj = MapProjection.Fit(layers.ExtentPoints().ToList(), rect, MapProjection.NorthUpRotation);
        return Scenery.Build(course, proj, rect);
    }

    [Fact]
    public void Scenery_Cassimbo_HasTheSeaBushesAndGrassButNoTrees()
    {
        using var s = SceneryOf("cassimbo.course");
        Assert.True(s.HasSea);                // the sea off the west of the links
        Assert.True(s.Bushes.Count > 1000);   // 4,810 placed, plus scatter groups
        Assert.NotEmpty(s.Tufts);
        Assert.Empty(s.Trees);
        Assert.Equal(((int)Math.Ceiling(1040 * 1.5f), (int)Math.Ceiling(610 * 1.5f)), (s.Ground.Width, s.Ground.Height));
    }

    [Fact]
    public void Scenery_MurrayEstate_FillsItsPlantedAreasWithTreesInland()
    {
        // Murray Estate's 371 trees are all in four planted areas, and its ground never reaches the sea plane.
        using var s = SceneryOf("murray-estate.course");
        Assert.False(s.HasSea);
        Assert.InRange(s.Trees.Count, 100, 371);
        Assert.Equal(s.Trees.OrderBy(t => t.At.Y).Select(t => t.At), s.Trees.Select(t => t.At));   // back to front
    }

    [Fact]
    public void Build_ModernIllustrated_IsAlwaysLandscapeWithThePlantingDrawn()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            var options = new ScorecardMapOptions { Orientation = PageOrientation.Portrait, Credit = "Test Designer" };
            var result = ScorecardMapBuilder.Build(path, dir, options, [MapThemeKind.ModernIllustrated], pdf: true, scale: 1);
            Assert.True(result.Landscape);
            Assert.Contains(result.Files, f => f.EndsWith("Scorecard Map (Modern Illustrated).pdf"));
            using var bmp = SKBitmap.Decode(result.Files.Single(f => f.EndsWith("Scorecard Map (Modern Illustrated).png")));
            Assert.Equal((1100, 850), (bmp.Width, bmp.Height));
            static int Near(SKBitmap b, SKColor c) => b.Pixels.Count(p => Math.Abs(p.Red - c.Red) + Math.Abs(p.Green - c.Green) + Math.Abs(p.Blue - c.Blue) < 12);
            Assert.True(Near(bmp, Scenery.Bush) > 2000, "bushes drawn");
            Assert.True(Near(bmp, MapTheme.ModernIllustrated.GreenFill) > 2000, "greens and the table header drawn");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
    static SKBitmap Render(MapThemeKind theme, ScorecardMapOptions options)
    {
        var course = Cassimbo();
        var layers = CourseLayers.From(course);
        using var renderer = new ScorecardMapRenderer(options with { Theme = theme, Holes = new CourseHoles() });
        var (page, _, _) = renderer.Plan(layers);
        var bmp = new SKBitmap((int)page.Width, (int)page.Height);
        using var canvas = new SKCanvas(bmp);
        renderer.Draw(canvas, course.Name, course, layers);
        return bmp;
    }

    [Fact]
    public void Strip_LandscapeMapsSpanThePageWithEveryTeeSetUnderneath()
    {
        // Cassimbo's four tee sets: the strip under a full-width map, a row each (Tee 4 … Tee 1) and Par.
        foreach (var theme in new[] { MapThemeKind.Vintage, MapThemeKind.Modern, MapThemeKind.Illustrated })
        {
            using var bmp = Render(theme, new ScorecardMapOptions { Orientation = PageOrientation.Landscape });
            Assert.Equal((1100, 850), (bmp.Width, bmp.Height));
            // The map reaches the right of the page (no side panel): grass colour near the right margin.
            var grass = MapTheme.For(theme).GrassFill;
            bool Near(SKColor c) => Math.Abs(c.Red - grass.Red) + Math.Abs(c.Green - grass.Green) + Math.Abs(c.Blue - grass.Blue) < 8;
            Assert.Contains(Enumerable.Range(40, 600).SelectMany(y => Enumerable.Range(900, 150).Select(x => bmp.GetPixel(x, y))), Near);
        }
    }

    [Fact]
    public void Strip_PortraitPageKeepsTheStripUnderTheMap()
    {
        using var bmp = Render(MapThemeKind.Vintage, new ScorecardMapOptions { Orientation = PageOrientation.Portrait });
        Assert.Equal((850, 1100), (bmp.Width, bmp.Height));
    }

    [Fact]
    public void Roses_WindRoseIsDrawnOnlyWhenTheCourseHasOne()
    {
        var wind = WindClimate.Generate("west", 270, null, 1, 1, 1);
        using var plain = Render(MapThemeKind.Modern, new ScorecardMapOptions { Orientation = PageOrientation.Landscape });
        using var windy = Render(MapThemeKind.Modern, new ScorecardMapOptions { Orientation = PageOrientation.Landscape, Wind = wind });
        int changed = 0;
        for (int y = 0; y < plain.Height; y++)
            for (int x = 0; x < plain.Width; x++)
                if (plain.GetPixel(x, y) != windy.GetPixel(x, y)) changed++;
        // The wind rose's 170 × 150 panel, and the compass rose moving to the next emptiest corner.
        Assert.InRange(changed, 5_000, 60_000);
    }

    [Fact]
    public void Build_TakesTheWindRoseFromThePlaceCard()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            byte[] Build(string sub)
            {
                var result = ScorecardMapBuilder.Build(path, Path.Combine(dir, sub), new ScorecardMapOptions(), [MapThemeKind.Vintage], scale: 1);
                return File.ReadAllBytes(result.Files[0]);
            }
            var before = Build("none");
            CoursePlaces.Save(Path.Combine(dir, "windy"), new CoursePlace { WindSource = "model", Climate = "west" });
            Assert.NotEqual(before, Build("windy"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
