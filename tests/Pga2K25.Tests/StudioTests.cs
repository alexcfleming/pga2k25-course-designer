using Pga2K25.Studio.Services;

namespace Pga2K25.Tests;

public class StudioTests
{
    [Theory]
    [InlineData("+13'6\"", 13.5)]
    [InlineData("-0'7", -0.5833)]
    [InlineData("13 ft 6 in", 13.5)]
    [InlineData("-5.33", -5.33)]
    [InlineData("−2 ft 4 in", -2.3333)]
    [InlineData("9", 9)]
    public void TryParseFeet_ReadsTheMeasureToolAndTypedForms(string text, double feet)
    {
        Assert.True(Measurements.TryParseFeet(text, out double v));
        Assert.Equal(feet, v, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3'14\"")]
    public void TryParseFeet_RejectsNonsense(string text) => Assert.False(Measurements.TryParseFeet(text, out _));

    [Fact]
    public void Measurements_SaveThenLoad_KeepsEveryReading()
    {
        string path = Path.Combine(Path.GetTempPath(), $"studio-measure-{Guid.NewGuid():N}", "Measurements.csv");
        var legs = new List<MeasuredLeg> { new(3, false, 235, -4.08), new(1, true, 302, 3), new(3, false, 235, -3.5) };
        Measurements.Save(path, legs);
        var back = Measurements.Load(path);
        Assert.Equal(3, back.Count);
        Assert.Equal(new MeasuredLeg(1, true, 302, 3), back[0]);
        Assert.Contains(back, m => m.Hole == 3 && m.Feet == -4.08);
        // The same format as the repository's calibration files.
        var sample = Measurements.Load(Path.Combine(TestPaths.SamplesDir, "..", "calibration", "route-heights", "berwick-coast.csv"));
        Assert.Equal(36, sample.Count);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void CourseLibrary_ReadsACourseWithoutChangingIt()
    {
        string path = Path.Combine(TestPaths.SamplesDir, "berwick-coast.course");
        var before = File.GetLastWriteTimeUtc(path);
        var entry = CourseLibrary.Read(path);
        Assert.Null(entry.Error);
        Assert.Equal("Berwick Coast", entry.Name);
        Assert.Equal(18, entry.Holes);
        Assert.Equal(72, entry.Par);
        Assert.Equal(7035, entry.Yards);
        Assert.NotNull(entry.Thumbnail);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Products_FindTheirPagesAndPdfsInReadingOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"studio-products-{Guid.NewGuid():N}");
        var settings = new StudioSettings { OutputRoot = root };
        string dir = Products.OutputDir(settings, "Test Course");
        Directory.CreateDirectory(Path.Combine(dir, "Test Course - Green Studies"));
        foreach (var f in new[] { "Test Course - Greens Book.png", "Test Course - Greens Book.pdf",
                                  "Test Course - Green Studies/Hole_10.png", "Test Course - Green Studies/Hole_02.png" })
            File.WriteAllBytes(Path.Combine(dir, f), [0]);
        var greens = Products.All.Single(p => p.Name == "Greens book");

        var pages = Products.PagesOf(greens, settings, "Test Course");
        Assert.Equal(["Greens Book", "Hole 2", "Hole 10"], pages.Select(p => p.Title));
        Assert.Single(Products.PdfsOf(greens, settings, "Test Course"));
        Assert.Empty(Products.PagesOf(Products.All.Single(p => p.Name == "Course cards"), settings, "Test Course"));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void TerrainSettings_MapOntoTheTerrainMapOptions()
    {
        var t = new TerrainSettings
        {
            Area = TerrainSettings.Buildable, MarginMetres = 90, IntervalFeet = 5, IndexEvery = 4, GridYards = 50,
            Shading = "Slope", ShowSurfaces = false, ShowGrid = true, ShowHilltops = false, ShowWater = false, PrintPixelsPerMetre = 3,
        };
        var o = t.ToOptions("CREDIT");
        Assert.Equal(Pga2K25.TerrainMap.MapExtent.Buildable, o.Extent);
        Assert.Equal(90, o.MarginMetres);
        Assert.Equal(5, o.ContourIntervalFeet);
        Assert.Equal(4, o.IndexEvery);
        Assert.Equal(50, o.GridYards);
        Assert.Equal(Pga2K25.TerrainMap.ReliefShading.Slope, o.Shading);
        Assert.False(o.ShowFeatures);
        Assert.False(o.ShowSummits);
        Assert.False(o.ShowWater);
        Assert.Equal(3, o.PixelsPerMetre);
        Assert.Equal("CREDIT", o.Credit);
        Assert.Equal(2, t.ToOptions("", pixelsPerMetre: 2).PixelsPerMetre);   // the live view's resolution
        t.ShowGrid = false;
        Assert.Equal(0, t.ToOptions("").GridYards);                              // grid off draws no grid

        // Defaults match the command line's.
        var d = new TerrainSettings().ToOptions("");
        var cli = new Pga2K25.TerrainMap.TerrainMapOptions();
        Assert.Equal((cli.Extent, cli.MarginMetres, cli.ContourIntervalFeet, cli.IndexEvery, cli.GridYards, cli.Shading, cli.PixelsPerMetre),
                     (d.Extent, d.MarginMetres, d.ContourIntervalFeet, d.IndexEvery, d.GridYards, d.Shading, d.PixelsPerMetre));
    }

    [Fact]
    public void TerrainSettings_SurviveASaveThroughJson()
    {
        var s = new StudioSettings();
        s.ExtrasFor("a.course").Terrain.Area = TerrainSettings.WholePlot;
        s.ExtrasFor("a.course").Terrain.IntervalFeet = 10;
        var back = System.Text.Json.JsonSerializer.Deserialize<StudioSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.Equal(TerrainSettings.WholePlot, back.ExtrasFor("a.course").Terrain.Area);
        Assert.Equal(10, back.ExtrasFor("a.course").Terrain.IntervalFeet);
        Assert.Null(back.ExtrasFor("b.course").Terrain.IntervalFeet);
    }

    [Fact]
    public void Settings_RecentKeepsTheNewestSix()
    {
        var s = new StudioSettings();
        for (int i = 0; i < 8; i++) s.Touch($"c{i}.course");
        s.Touch("c3.course");
        Assert.Equal(StudioSettings.MaxRecent, s.Recent.Count);
        Assert.Equal("c3.course", s.Recent[0]);
        Assert.Equal("c7.course", s.Recent[1]);
    }

    [Fact]
    public void ShellOpen_ReportsAMissingFileInsteadOfThrowing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.pdf");
        Assert.Contains("isn't there any more", ShellOpen.Open(missing));
    }
}
