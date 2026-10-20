using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class CourseCardTests
{
    static readonly string SaganPath = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
    static readonly string CassimboPath = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
    static readonly string WorkbookPath = Path.Combine(TestPaths.RepoRoot, "samples", "data", "cassimbo-hole-data.xlsx");

    static CourseCardData Data(string path, out List<CardIssue> issues, Maps.HoleData? workbook = null)
    {
        issues = [];
        var data = CourseCardData.From(CourseFile.Load(path).ReadModel(), workbook, null, issues);
        issues.AddRange(data.Validate(workbook));
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
    public void Cassimbo_ReportsInvertedTeesAndStaleWorkbookWithoutCorrecting()
    {
        var workbook = Maps.HoleData.Load(WorkbookPath);
        var d = Data(CassimboPath, out var issues, workbook);
        // The game's own scorecard has the forward tee longer than the next on hole 4: an ERROR, left as is.
        Assert.Contains(issues, i => i.Severity == Severity.Error && i.Message.StartsWith("Hole 4: tee yardages"));
        Assert.Equal(new[] { 364, 362, 373, 380 }, d.Holes[3].Yards);
        Assert.Contains(issues, i => i.Severity == Severity.Warn && i.Message.Contains("Hole 12 Blue: workbook 624 yds, course file 632"));
        Assert.Equal(new[] { "Red", "Green", "White", "Blue" }, d.TeeNames);   // names only, from the workbook
        Assert.Equal(632, d.Holes[11].Yards[^1]);                               // the file wins
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
