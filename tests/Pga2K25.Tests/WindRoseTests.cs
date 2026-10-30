using Pga2K25.CourseFormat;
using Pga2K25.Maps;

namespace Pga2K25.Tests;

public class WindRoseTests
{
    [Fact]
    public void FromHours_BinsBySectorAndBand_AndSkipsMissingHours()
    {
        // 2 calm, 1 from N at 5 mph, 1 from 350° (still N) at 30 mph, 1 from E at 10 mph, 1 missing.
        double?[] speeds = [1, 2, 5, 30, 10, null];
        double?[] dirs = [0, 90, 0, 350, 90, 0];
        var rose = WindRose.FromHours(speeds, dirs);
        Assert.Equal(5, rose.Hours);
        Assert.Equal(0.4, rose.Calm, 6);
        Assert.Equal(0.2, rose.Sectors[0][0], 6);   // N, 3–8
        Assert.Equal(0.2, rose.Sectors[0][4], 6);   // N, 25+
        Assert.Equal(0.2, rose.Sectors[4][1], 6);   // E, 8–13
        Assert.Equal(1, rose.Calm + Enumerable.Range(0, 16).Sum(rose.SectorTotal), 6);
        Assert.Equal(0, rose.PrevailingFrom);
    }

    [Fact]
    public void FromOpenMeteo_ReadsTheArchiveShape()
    {
        const string json = """
            {"latitude":53.5,"longitude":107.5,"hourly":{"time":["2015-01-01T00:00","2024-12-31T23:00"],
             "wind_speed_10m":[12.0,null],"wind_direction_10m":[315,300]}}
            """;
        var rose = WindRose.FromOpenMeteo(json);
        Assert.Equal(1, rose.Hours);
        Assert.Equal(1, rose.Sectors[14][1], 6);   // NW, 8–13
        Assert.Contains("2015–2024", rose.Source);
        Assert.Throws<InvalidDataException>(() => WindRose.FromOpenMeteo("""{"error":true,"reason":"Latitude must be in range"}"""));
    }

    [Theory]
    [InlineData("trade")] [InlineData("west")] [InlineData("sea")] [InlineData("monsoon")] [InlineData("plains")] [InlineData("valley")]
    public void Climate_AddsUpToAllHours_AndIsRepeatable(string preset)
    {
        var a = WindClimate.Generate(preset, 315, 270, 1, 1, 4127);
        var b = WindClimate.Generate(preset, 315, 270, 1, 1, 4127);
        Assert.Equal(1, a.Calm + Enumerable.Range(0, 16).Sum(a.SectorTotal), 6);
        Assert.Equal(a.Sectors.SelectMany(s => s), b.Sectors.SelectMany(s => s));
        Assert.NotEqual(a.Sectors.SelectMany(s => s), WindClimate.Generate(preset, 315, 270, 1, 1, 4128).Sectors.SelectMany(s => s));
    }

    [Fact]
    public void Climate_TradeWindsComeFromThePrevailingSide_AndSeaBreezeFromTheSea()
    {
        Assert.Equal(45, WindClimate.Generate("trade", 45, null, 1, 1, 1).PrevailingFrom);
        // Sea to the south: the onshore lobe outweighs a weak westerly.
        Assert.Equal(180, WindClimate.Generate("sea", 270, 180, 1, 1, 1).PrevailingFrom);
        // Windier means more breezy hours.
        Assert.True(WindClimate.Generate("west", 270, null, 1.35, 1, 1).Breezy > WindClimate.Generate("west", 270, null, 0.7, 1, 1).Breezy);
    }

    static WindRose AllFrom(int sector)
    {
        var sectors = Enumerable.Range(0, 16).Select(i => i == sector ? new List<double> { 0, 1, 0, 0, 0 } : [0, 0, 0, 0, 0]).ToList();
        return new WindRose { Sectors = sectors };
    }

    [Theory]
    [InlineData(0, "Into the wind")]      // playing north into a north wind
    [InlineData(180, "Downwind")]
    [InlineData(90, "Crosswind")]
    public void Exposure_ReadsTheWindAgainstTheLineOfPlay(int bearing, string character)
    {
        var hw = WindExposure.For(1, 4, bearing, AllFrom(0));
        Assert.Equal(character, hw.Character);
        if (bearing == 90) Assert.Equal(1, hw.FromLeft, 6);   // a north wind on a hole playing east comes from the left
    }

    [Fact]
    public void Place_RoseFollowsTheSource_AndKeepsAFetchedRecord()
    {
        var place = new CoursePlace();
        Assert.Null(place.Rose(270));
        place.WindSource = "data";
        Assert.Equal("model", place.Rose(270)!.Kind);   // no record yet: the model stands in
        place.WindData = WindRose.FromHours([10.0], [90.0]);
        Assert.Same(place.WindData, place.Rose(270));

        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-wind-" + Guid.NewGuid().ToString("N"));
        try
        {
            CoursePlaces.Save(dir, place);
            var back = CoursePlaces.Load(dir);
            Assert.Equal("data", back.WindSource);
            Assert.Equal(place.WindData.Sectors[4][1], back.WindData!.Sectors[4][1]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
