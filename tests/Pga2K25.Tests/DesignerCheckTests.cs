using Pga2K25.CourseFormat;
using Pga2K25.Readiness;
using Severity = Pga2K25.Readiness.Severity;

namespace Pga2K25.Tests;

/// <summary>
/// The readiness checks that mirror the course designer's own (<see cref="ReadinessChecker.DesignerChecks"/>): a tee
/// and a route on every hole, the game's saved scorecard against the layout, and whether bunker depths will be cut.
/// </summary>
[Trait("Area", "Readiness")]
public class DesignerCheckTests
{
    static ReadinessCheck Check(CourseModel course, string id) =>
        ReadinessChecker.DesignerChecks(course).SingleOrDefault(c => c.Id == id) ?? throw new Xunit.Sdk.XunitException($"no check {id}");

    static CourseModel Course(IReadOnlyList<Hole> holes, StoredScorecard? stored = null) => new()
    {
        Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49, Holes = holes,
        SurfaceSplines = [], HeightStamps = [], SurfaceBrushes = [], PlacedObjects = [], Stored = stored,
    };

    // A 300 m par 4 straight up +Z with two tees, 20 m apart.
    static Hole Four(int n, double x = 0) => new(n, 4, [new(x, 0), new(x, 300)], [new(x, 20), new(x, 0)], [new(x, 300)]);

    [Fact]
    public void SampleCourses_HaveATeeAndARouteOnEveryHole()
    {
        foreach (var file in new[] { "cassimbo.course", "sagan-khushun.course", "murray-estate.course", "berwick-coast.course" })
        {
            var c = Check(Samples.Course(file), "hole-layout");
            Assert.True(c.Severity == Severity.Pass, $"{file}: {c.Detail}");
        }
    }

    [Fact]
    public void MissingTee_ShortRoute_AndRouteBackToItsStart_AreFlagged()
    {
        var holes = new List<Hole>
        {
            Four(1),
            new(2, 4, [new(50, 0), new(50, 300)], [], [new(50, 300)]),                       // no tee marker
            new(3, 3, [new(100, 150)], [new(100, 150)], [new(100, 150)]),                    // one waypoint
            new(4, 4, [new(150, 0), new(150, 150), new(150.5, 0)], [new(150, 0)], [new(150.5, 0)]),   // ends where it starts
        };
        var c = Check(Course(holes), "hole-layout");
        Assert.Equal(Severity.Flag, c.Severity);
        Assert.Equal([2, 3, 4], c.Holes);
        Assert.Contains("No tee marker on 2", c.Detail);
        Assert.Contains("Fewer than 2 waypoints on 3", c.Detail);
        Assert.Contains("same place on 4", c.Detail);
    }

    [Fact]
    public void SampleCourses_SavedScorecardMatchesTheLayout()
    {
        foreach (var file in new[] { "cassimbo.course", "sagan-khushun.course", "murray-estate.course", "berwick-coast.course" })
        {
            var course = Samples.Course(file);
            Assert.NotNull(course.Stored);
            var c = Check(course, "stored-scorecard");
            Assert.True(c.Severity == Severity.Pass, $"{file}: {c.Detail}");
        }
    }

    [Fact]
    public void SavedScorecard_IsReadFromTheMetadata()
    {
        var sagan = Samples.Course("sagan-khushun.course");
        Assert.Equal(18, sagan.Stored!.Pars.Count);
        Assert.Equal(sagan.Holes.Select(h => h.Par), sagan.Stored.Pars);
        Assert.Equal(sagan.Holes.Sum(h => h.Par), sagan.Stored.Pars.Sum());
        Assert.All(sagan.Stored.TeeSetLengths, set => Assert.Equal(18, set.Count));
    }

    [Fact]
    public void KeptYardages_AndAChangedPar_AreFlagged()
    {
        var holes = new List<Hole> { Four(1), Four(2, 50) };
        // Tee 1 (the front, 280 m) and tee 2 (300 m) as drawn, but hole 2's back tee kept at its old 290 m and its par at 5.
        var stored = new StoredScorecard([4, 5], [[280, 280], [300, 290]]);
        var c = Check(Course(holes, stored), "stored-scorecard");
        Assert.Equal(Severity.Flag, c.Severity);
        Assert.Equal([2], c.Holes);
        Assert.Contains("Par differs on 2 (saved 5, drawn 4)", c.Detail);
        Assert.Contains("2 tee 2 (saved 317, drawn 328)", c.Detail);
        Assert.Contains("Keep Yardages", c.Detail);

        // Within half a yard is the same length; a hole count the scorecard doesn't have is flagged.
        Assert.Equal(Severity.Pass, Check(Course(holes, new StoredScorecard([4, 4], [[280.3, 280], [300, 299.7]])), "stored-scorecard").Severity);
        var fewer = Check(Course(holes, new StoredScorecard([4], [[280], [300]])), "stored-scorecard");
        Assert.Equal(Severity.Flag, fewer.Severity);
        Assert.Contains("1 holes; the layout has 2", fewer.Detail);
    }

    [Fact]
    public void NoSavedScorecard_AsksForASave()
    {
        Assert.Equal(Severity.Confirm, Check(Course([Four(1)]), "stored-scorecard").Severity);
    }

    static readonly BunkerOverride Deep = new(0.2, -1, 0.2, 0.8, 0);

    [Fact]
    public void BunkerCheck_ConfirmsWithHighResOff_PassesWithItOnAndMemoryLow()
    {
        var on = new BunkerSettings(true, BunkerDepression.None, BunkerDepression.None, false);
        CourseModel Make(BunkerSettings s) => new()
        {
            Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49, Holes = [Four(1)],
            SurfaceSplines = [], HeightStamps = [], PlacedObjects = [], Bunkers = s,
            SurfaceBrushes = [new SurfaceBrush(0, Surfaces.Bunker, 0, new Vec2(0, 150), 10, 0, 1) { Bunker = Deep }],
        };
        var pass = Check(Make(on), "bunker-stamping");
        Assert.Equal(Severity.Pass, pass.Severity);                    // an empty plot: memory about 10 %
        Assert.Contains("about 10 %", pass.Detail);
        var off = Check(Make(on with { HighResStamping = false }), "bunker-stamping");
        Assert.Equal(Severity.Confirm, off.Severity);
        Assert.Contains("high-resolution bunkers are off", off.Detail);
    }

    [Fact]
    public void BunkerCheck_Samples()
    {
        // All three have high-resolution bunkers on. Sagan-Khushun (63 % memory in game) and Murray Estate are under 85 %
        // by Studio's estimate; Cassimbo's estimate is about 98 %, so its bunker depths may not be cut in game.
        foreach (var file in new[] { "sagan-khushun.course", "murray-estate.course" })
        {
            var c = Check(Samples.Course(file), "bunker-stamping");
            Assert.True(c.Severity == Severity.Pass, $"{file}: {c.Detail}");
        }
        var cassimbo = Check(Samples.Course("cassimbo.course"), "bunker-stamping");
        Assert.Equal(Severity.Warning, cassimbo.Severity);
        Assert.Contains("about 98 %", cassimbo.Detail);
    }

    [Fact]
    public void BunkerCheck_OnlyWhenBunkersHaveDepth()
    {
        var plain = Course([Four(1)]);
        Assert.False(plain.HasBunkerDepths);
        Assert.DoesNotContain(ReadinessChecker.DesignerChecks(plain), c => c.Id == "bunker-stamping");
        Assert.True(Samples.Course("sagan-khushun.course").HasBunkerDepths);   // four drawn bunkers 0.1–0.4 m deep
        Assert.Contains(ReadinessChecker.DesignerChecks(Samples.Course("sagan-khushun.course")), c => c.Id == "bunker-stamping");
    }
}
