using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class CourseCardTests
{
    static readonly string SaganPath = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
    static readonly string CassimboPath = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");

    static CourseCardData Data(string path, out List<CardIssue> issues, CourseHoles? entries = null)
    {
        issues = [];
        var data = CourseCardData.From(CourseFile.Load(path).ReadModel(), entries, null, issues);
        issues.AddRange(data.Validate());
        return data;
    }

    [Theory]
    [InlineData(0, -10, 0)]      // north is −Z
    [InlineData(-10, 0, 90)]     // east is −X
    [InlineData(0, 10, 180)]
    [InlineData(10, 0, 270)]
    [InlineData(-10, -10, 45)]
    public void Bearing_UsesTheGameCompass(double dx, double dz, int expected) =>
        Assert.Equal(expected, CourseCardData.Bearing(new Vec2(0, 0), new Vec2(dx, dz)));

    [Fact]
    public void Sagan_IsCleanAndTotalsComeFromTheScorecard()
    {
        var d = Data(SaganPath, out var issues);
        Assert.DoesNotContain(issues, i => i.Severity != Severity.Info);
        Assert.Equal((72, 36, 36), (d.Total.Par, d.Out.Par, d.In.Par));
        Assert.Equal(7266, d.Total.Yards.Single());
        Assert.Equal(d.Total.Yards[0], d.Out.Yards[0] + d.In.Yards[0]);
        Assert.All(d.Holes.Where(h => h.Par == 3), h => Assert.Equal(h.TeeBearing, h.ApproachBearing));
        Assert.Equal(90, d.Holes[0].TeeBearing);        // hole 1 plays east on the terrain map
    }

    [Fact]
    public void HeaderTotals_AreLive()
    {
        var d = Data(SaganPath, out _);
        using var r = new CourseCardRenderer(d, new CourseCardConfig(), "WEST");
        var (main, _, halves) = r.HeaderTotals();
        Assert.Contains($"{d.Total.Yards[0]:N0}", main);
        Assert.Equal($"OUT {d.Out.Yards[0]:N0}  ·  IN {d.In.Yards[0]:N0}", halves);
    }

    [Fact]
    public void Cassimbo_ReportsInvertedTeesWithoutCorrecting_AndTakesNamesFromTheHolesCard()
    {
        var entries = new CourseHoles { TeeNames = ["Red", "Green", "White", "Blue"] };
        entries.Entry(1).Name = "Cacimbo";
        entries.Entry(2).GreenNotes = "Hand-written green";
        entries.Entry(2).Hazards = ["Hand-written hazard"];
        var d = Data(CassimboPath, out var issues, entries);
        // The game's own scorecard has the forward tee longer than the next on hole 4: an ERROR, left as is.
        Assert.Contains(issues, i => i.Severity == Severity.Error && i.Message.StartsWith("Hole 4: tee yardages"));
        Assert.Equal(new[] { 364, 362, 373, 380 }, d.Holes[3].Yards);
        Assert.Equal(new[] { "Red", "Green", "White", "Blue" }, d.TeeNames);   // names only, from the Holes card
        Assert.Equal(632, d.Holes[11].Yards[^1]);                               // yardages from the file
        Assert.Equal("Cacimbo", d.Holes[0].Name);
        Assert.Null(d.Holes[1].Name);
        Assert.Equal("Hand-written green", d.Holes[1].GreenNotes);
        Assert.Equal(["Hand-written hazard"], d.Holes[1].Hazards);

        // Names for the wrong number of tee sets are reported and not used.
        var wrong = Data(CassimboPath, out var wrongIssues, new CourseHoles { TeeNames = ["Red", "Blue"] });
        Assert.Equal(new[] { "Tee 1", "Tee 2", "Tee 3", "Back" }, wrong.TeeNames);
        Assert.Contains(wrongIssues, i => i.Severity == Severity.Warn && i.Message.StartsWith("The Holes card names 2 tee sets"));
    }

    [Fact]
    public void CourseDataCardWind_AgreesWithTheWindCard()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-cards-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A fetched record that blows mostly from the east, with a stale south-west wind set by hand on both the
            // Place card and the card settings (as the Sagan-Khushun card file has, from before the wind rose).
            var rose = WindRose.FromHours([10.0, 12.0, 9.0, 15.0], [90.0, 88.0, 95.0, 225.0]);
            CoursePlaces.Save(dir, new CoursePlace { WindSource = "data", WindData = rose, WindFromDegrees = 225 });
            var config = new CourseCardConfig { Dpi = 30, Formats = ["png"], WindFromDegrees = 225 };
            var withRose = CourseCardBuilder.Build(SaganPath, dir, config);
            Assert.Equal(rose.PrevailingFrom, withRose.WindFromDegrees);   // the Wind Card prints the rose's prevailing direction
            Assert.Equal(WindRose.SectorOf(90), WindRose.SectorOf(withRose.WindFromDegrees!.Value));
            Assert.Contains(withRose.Files, f => f.EndsWith("Wind Card.png"));
            Assert.Contains(withRose.Issues, i => i.Message.StartsWith("Wind set by hand (SW) differs"));

            // The climate model: the arrow follows the generated rose, the one the Wind Card draws.
            var model = new CoursePlace { WindSource = "model", Climate = "sea", WindFromDegrees = 315 };
            CoursePlaces.Save(dir, model);
            var modelled = CourseCardBuilder.Build(SaganPath, dir, config with { WindFromDegrees = null });
            var drawn = model.Rose(Maps.OceanFinder.SeaBearing(model, CourseFile.Load(SaganPath).ReadModel()))!;
            Assert.Equal(drawn.PrevailingFrom, modelled.WindFromDegrees);

            // No rose: the wind set by hand, card settings first.
            CoursePlaces.Save(dir, new CoursePlace { WindFromDegrees = 270 });
            Assert.Equal(225, CourseCardBuilder.Build(SaganPath, dir, config).WindFromDegrees);
            Assert.Equal(270, CourseCardBuilder.Build(SaganPath, dir, config with { WindFromDegrees = null }).WindFromDegrees);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Build_StopsOnErrorsUnlessForced()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-cards-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new CourseCardConfig { Dpi = 40, Formats = ["png"] };
            Assert.Throws<InvalidDataException>(() => CourseCardBuilder.Build(CassimboPath, dir, config));
            Assert.Empty(Directory.GetFiles(dir, "*.png"));
            var forced = CourseCardBuilder.Build(CassimboPath, dir, config, force: true);
            Assert.Equal(4, forced.Files.Count);   // compass rose, hole data, course data and green size cards
            var sagan = CourseCardBuilder.Build(SaganPath, dir, config);
            Assert.Equal("WEST", sagan.OceanSide);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
