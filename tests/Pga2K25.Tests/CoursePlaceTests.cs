using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class CoursePlaceTests
{
    static string TempDir() => Path.Combine(Path.GetTempPath(), "pga2k25-place-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Place_RoundTripsAndSkipsBlankLines()
    {
        string dir = TempDir();
        try
        {
            Assert.Empty(CoursePlaces.Load(dir).PlaceLines);   // no file: an empty place
            CoursePlaces.Save(dir, new CoursePlace { Lines = ["Olkhon Island", "  ", " Lake Baikal "], SeaSide = "WEST", WindFromDegrees = 315 });
            var place = CoursePlaces.Load(dir);
            Assert.Equal(["Olkhon Island", "Lake Baikal"], place.PlaceLines);
            Assert.Equal("Olkhon Island, Lake Baikal", place.Line);
            Assert.Equal("WEST", place.SeaSide);
            Assert.Equal(315, place.WindFromDegrees);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Place_UnreadableFileIsAnEmptyPlace()
    {
        string dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(CoursePlaces.PathFor(dir), "{ not json");
            Assert.Empty(CoursePlaces.Load(dir).PlaceLines);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CourseCards_TakeSeaSideFromThePlace_UnlessTheCardSettingsSetIt()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
            CoursePlaces.Save(dir, new CoursePlace { SeaSide = "EAST", WindFromDegrees = 270 });
            var fromPlace = CourseCardBuilder.Build(path, dir, new CourseCardConfig { Formats = ["png"] }, force: true);
            Assert.Equal("EAST", fromPlace.OceanSide);
            Assert.DoesNotContain(fromPlace.Issues, i => i.Message.Contains("No prevailing wind"));

            var fromCard = CourseCardBuilder.Build(path, dir, new CourseCardConfig { Formats = ["png"], OceanSide = "SOUTH" }, force: true);
            Assert.Equal("SOUTH", fromCard.OceanSide);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SiteInset_CoastalPlotHasSea_InlandPlotDoesNot()
    {
        static Maps.SiteInset Site(string file)
        {
            var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, file)).ReadModel();
            return Maps.SiteInset.Build(course, Maps.CourseLayers.From(course));
        }
        var sagan = Site("sagan-khushun.course");
        Assert.True(sagan.HasSea);
        Assert.NotEmpty(sagan.Footprint);
        Assert.Equal(sagan.Max.X - sagan.Min.X, sagan.Max.Z - sagan.Min.Z, 6);   // a true square

        var murray = Site("murray-estate.course");
        Assert.False(murray.HasSea);
        Assert.NotEmpty(murray.Footprint);
    }

    [Fact]
    public void Region_SameSeedSameCoast_DifferentSeedDifferent()
    {
        var a = Maps.RegionGenerator.Generate(Maps.RegionShape.Island, 4127, 270);
        var b = Maps.RegionGenerator.Generate(Maps.RegionShape.Island, 4127, 270);
        var c = Maps.RegionGenerator.Generate(Maps.RegionShape.Island, 4128, 270);
        Assert.Equal(a.Land[0], b.Land[0]);
        Assert.NotEqual(a.Land[0], c.Land[0]);
        // Pinned values: if these move, every saved seed would draw a different coast.
        Assert.Equal(18 * 16, a.Land[0].Count);
        Assert.Equal(105.767f, a.Land[0][5].X, 2);
    }

    [Theory]
    [InlineData(270)]   // sea west: the course is the island's west shore
    [InlineData(0)]     // sea north
    [InlineData(135)]   // sea south-east
    public void Region_CourseSitsOnTheShoreFacingTheSea(int seaBearing)
    {
        foreach (var shape in new[] { Maps.RegionShape.Island, Maps.RegionShape.Peninsula })
        {
            var r = Maps.RegionGenerator.Generate(shape, 7, seaBearing);
            // Page frame: x east, y south. The pin is the land point furthest towards the sea, give or take the coast's wiggle.
            double rad = seaBearing * Math.PI / 180;
            var toSea = new SkiaSharp.SKPoint((float)Math.Sin(rad), (float)-Math.Cos(rad));
            float Along(SkiaSharp.SKPoint p) => p.X * toSea.X + p.Y * toSea.Y;
            Assert.True(Along(r.Pin) >= r.Land[0].Max(Along) - 40, $"{shape} at {seaBearing}°");
        }
    }

    [Fact]
    public void Region_InlandHasNoSeaAndARiver()
    {
        var r = Maps.RegionGenerator.Generate(Maps.RegionShape.Inland, 3, null);
        Assert.False(r.SeaAround);
        Assert.Empty(r.Land);
        Assert.Single(r.Rivers);
        Assert.Equal(2, r.Water.Count);
    }

    [Fact]
    public void SeaBearing_FollowsThePlaceCard()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course")).ReadModel();
        Assert.Equal(45, Maps.OceanFinder.SeaBearing(new CoursePlace { SeaSide = "NORTH-EAST" }, course));
        Assert.Null(Maps.OceanFinder.SeaBearing(new CoursePlace { SeaSide = "none" }, course));
        Assert.Equal(Maps.OceanFinder.SeaBearing(course), Maps.OceanFinder.SeaBearing(new CoursePlace(), course));
    }
}
