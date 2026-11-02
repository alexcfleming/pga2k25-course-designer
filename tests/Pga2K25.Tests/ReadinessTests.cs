using Pga2K25.CourseFormat;
using Pga2K25.Readiness;
using Severity = Pga2K25.Readiness.Severity;

namespace Pga2K25.Tests;

/// <summary>
/// Publish-readiness checks from the course file. Cassimbo was not approved for TGC Tours; with a 360-yd test drive
/// (about what a reviewer hit) the file alone has to show what the review found.
/// </summary>
public class ReadinessTests
{
    static CourseModel Load(string file) => CourseFile.Load(Path.Combine(TestPaths.SamplesDir, file)).ReadModel();

    static readonly Lazy<CourseModel> Cassimbo = new(() => Load("cassimbo.course"));
    internal static readonly Lazy<ReadinessReport> Report = new(() =>
        ReadinessChecker.Run(Cassimbo.Value, new ReadinessLimits { TestDriveYards = 360 }));

    static ReadinessCheck Check(string id) => Report.Value.Check(id) ?? throw new Xunit.Sdk.XunitException($"no check {id}");

    [Fact]
    public void Cassimbo_ShortPar5s_AreTheReviewsHole3()
    {
        var c = Check("par5");
        Assert.Equal(Severity.High, c.Severity);
        Assert.Equal([3, 18], c.Holes);
        Assert.Equal(177, Report.Value.Holes[2].LeftAfterDrive);
    }

    [Fact]
    public void Cassimbo_FrontNine_HasAThirdDrivablePar4()
    {
        // "Third or fourth drivable par 4 on the front nine."
        var c = Check("drivable-front");
        Assert.Equal(Severity.High, c.Severity);
        Assert.Equal([1, 4, 8], c.Holes);
        Assert.Equal(Severity.High, Check("drivable-back").Severity);
    }

    [Fact]
    public void Cassimbo_GreenSizes_Two_AgainstSeven()
    {
        // "Greens wildly different in size (2 versus 7)." Provisional now: the band warns, it doesn't flag.
        var c = Check("green-scale");
        Assert.Equal(Severity.Warning, c.Severity);
        Assert.True(c.Passed);
        Assert.Contains(2, c.Holes);
        Assert.Contains(7, c.Holes);
        var h = Report.Value.Holes;
        Assert.InRange(h[1].GreenRatio!.Value, 0.25, 0.35);
        Assert.InRange(h[6].GreenRatio!.Value, 2.2, 2.5);
    }

    [Fact]
    public void Cassimbo_Hole12_PlaysBesideHole10sGreen()
    {
        // "Hole 12: shot played over another green." Its line passes about 20 m from hole 10's green.
        var c = Check("corridor");
        Assert.Equal(Severity.Flag, c.Severity);
        Assert.Contains(12, c.Holes);
        var h12 = Report.Value.Holes[11];
        Assert.Equal(10, h12.NearGreenOf);
        Assert.InRange(h12.NearGreenYards!.Value, 15, 25);
    }

    [Fact]
    public void Cassimbo_Hole7_ClimbsOverTheLimit()
    {
        var c = Check("elevation");
        Assert.Equal(Severity.Flag, c.Severity);
        Assert.Equal([7], c.Holes);
        Assert.InRange(Report.Value.PlotRangeFeet, 90, 120);
        Assert.Equal(Severity.Flag, Check("plot-range").Severity);
    }

    [Fact]
    public void Cassimbo_Scorecard_AndSettingsPass()
    {
        Assert.Equal(Severity.Pass, Check("par").Severity);
        Assert.Equal(Severity.Pass, Check("tee-totals").Severity);
        Assert.Equal(Severity.Pass, Check("same-par").Severity);
        Assert.Equal(Severity.Pass, Check("own-green").Severity);
        Assert.Equal([6528, 6785, 7013, 7220], Report.Value.TeeSets.Select(t => t.Total));
        Assert.Equal(Severity.Pass, Check("fringe").Severity);         // 0.5: no visible fringe in game
        Assert.Equal(Severity.Pass, Check("green-speed").Severity);
        Assert.Equal(10.9, Report.Value.Stimp!.Value, 1);
    }

    [Fact]
    public void Stimp_FollowsTheInGameReadings()
    {
        Assert.Equal(10.9, ReadinessChecker.Stimp(0.97)!.Value, 2);    // Cassimbo
        Assert.Equal(10.1, ReadinessChecker.Stimp(0.905)!.Value, 2);   // Sagan-Khushun
        Assert.Null(ReadinessChecker.Stimp(-1));
    }

    [Fact]
    public void Cassimbo_LightingIsRecorded()
    {
        var a = Report.Value.Atmosphere!;
        Assert.Equal("15:43", a.Clock);
        Assert.Equal("Cumulus_02", a.Cloud1!.Type);
        Assert.Equal("Altocumulus_01", a.Cloud2!.Type);
        Assert.Equal(Severity.Info, Check("lighting").Severity);
    }

    [Fact]
    public void Cassimbo_EveryHoleHasFourPinsPlaced()
    {
        Assert.All(Report.Value.Holes, h =>
        {
            Assert.Equal(4, h.Pins.Count);
            Assert.All(h.Pins, p => { Assert.Contains(p.Depth, "FCB"); Assert.Contains(p.Side, "LCR"); });
        });
    }

    [Fact]
    public void Limits_AreTheCoursesToSet()
    {
        var loose = ReadinessChecker.Run(Cassimbo.Value, new ReadinessLimits { TestDriveYards = 300, ScaleLow = 0.2, ScaleHigh = 3 });
        Assert.Equal(Severity.Pass, loose.Check("green-scale")!.Severity);
        Assert.Equal(Severity.Pass, loose.Check("par5")!.Severity);          // 237 yds left on hole 3 after 300
    }

    [Fact]
    public void WithoutATestDrive_TheDriveChecksAskForOne()
    {
        var r = ReadinessChecker.Run(Load("murray-estate.course"));
        Assert.Equal(Severity.Confirm, r.Check("drive")!.Severity);
        Assert.Null(r.Check("par5"));
        Assert.All(r.Holes, h => { Assert.Null(h.DriveClass); Assert.Null(h.LeftAfterDrive); });
    }

    static HoleMeasures Hole(int n, int par, int yards) => new() { Number = n, Par = par, Yards = yards };

    [Fact]
    public void SameParRuns_FollowSection7()
    {
        // Section 10's lesson: three par 4s to open, 21 yds apart, is worth a look. A design habit, not an approval
        // risk (approved courses have four par 4s in a row), so it flags at most.
        var opening = ReadinessChecker.Runs([Hole(1, 4, 402), Hole(2, 4, 381), Hole(3, 4, 395), Hole(4, 3, 170)]);
        var run = Assert.Single(opening);
        Assert.Equal(Severity.Flag, run.Severity);
        Assert.StartsWith("A narrow run to open the round", run.Finding);
        Assert.Equal(21, run.Spread);

        var pairs = ReadinessChecker.Runs([Hole(5, 4, 380), Hole(6, 4, 410), Hole(7, 3, 160), Hole(8, 5, 520), Hole(9, 5, 600)]);
        Assert.Equal(Severity.Flag, pairs[0].Severity);   // 30 yds apart: narrow
        Assert.Equal(Severity.Pass, pairs[1].Severity);   // 80 yds apart: length does it
    }

    [Fact]
    public void Cassimbo_TgcMinimums_ParAndYardageRangePass()
    {
        Assert.Equal(Severity.Pass, Check("par-range").Severity);
        Assert.Equal(Severity.Pass, Check("same-par-range").Severity);
        var tight = ReadinessChecker.Run(Cassimbo.Value, new ReadinessLimits { TestDriveYards = 360, ParMax = 71, SameParSpreadYards = 120 });
        Assert.Equal(Severity.Flag, tight.Check("par-range")!.Severity);
        Assert.Equal(Severity.Flag, tight.Check("same-par-range")!.Severity);   // par 5s span 95 yds
    }

    [Fact]
    public void Cassimbo_Pins_EdgeAndSlopeAtTheCup()
    {
        var c = Check("pins");
        Assert.Equal(Severity.Flag, c.Severity);
        Assert.Equal([1, 3, 4, 9, 15], c.Holes);
        Assert.InRange(Report.Value.Holes[8].Pins[0].SlopePct!.Value, 12, 20);
        Assert.InRange(Report.Value.Holes[3].Pins[3].EdgeYards!.Value, 0.3, 1.2);
    }

    [Fact]
    public void Cassimbo_FairwayStart_MatchesTheBookMeasure()
    {
        // From the yardage-book vectors (hole: yds), measured by hand the same way: 25 yds wide held for 35 yds.
        var book = new Dictionary<int, int> { [1] = 125, [3] = 135, [4] = 80, [6] = 60, [7] = 245, [8] = 90, [9] = 140,
            [11] = 85, [12] = 115, [13] = 60, [14] = 75, [16] = 60, [17] = 85, [18] = 60 };
        var holes = Report.Value.Holes.Where(h => book.ContainsKey(h.Number)).ToList();
        Assert.All(holes, h => Assert.NotNull(h.FairwayStartYards));
        Assert.True(holes.Count(h => Math.Abs(h.FairwayStartYards!.Value - book[h.Number]) <= 15) >= 12);
        var c = Check("fairway-start");
        Assert.Equal(Severity.Warning, c.Severity);
        Assert.Contains("1 of 14", c.Detail);
        Assert.InRange(Report.Value.MedianFairwayStartYards, 85, 110);
        Assert.All(Report.Value.Holes.Where(h => h.Par == 3), h => Assert.Null(h.FairwayStartYards));
    }

    [Fact]
    public void Overrides_TheDesignersStartAndSharedFairwaysCount()
    {
        var r = ReadinessChecker.Run(Cassimbo.Value, new ReadinessLimits { TestDriveYards = 360 }, overrides: new Dictionary<int, HoleOverride>
        {
            [1] = new(190, false),
            [12] = new(null, true),
        });
        Assert.Equal(190, r.Holes[0].FairwayStartYards);
        Assert.True(r.Holes[0].FairwayStartFileYards < 150);
        Assert.Contains("2 of 13", r.Check("fairway-start")!.Detail);         // hole 1 now in band, hole 12 left out
        Assert.DoesNotContain(12, r.Check("fairway-start")!.Holes);
        Assert.DoesNotContain(12, r.Check("landing-width")!.Holes);          // its 167-yd landing is shared, not a flag
    }

    [Fact]
    public void Cassimbo_ProvisionalChecks_OnlyWarn()
    {
        var shortFours = Check("short-par4");
        Assert.Equal(Severity.Warning, shortFours.Severity);
        Assert.Equal([1, 4, 8, 13, 17], shortFours.Holes);
        Assert.Equal([2, 4, 10], Check("small-greens").Holes);
        Assert.Equal(Severity.Pass, Check("par3-greens").Severity);            // 147 to 248 yds, greens grow with them
        Assert.Equal(Severity.Info, Check("narrow-fairway").Severity);
        foreach (var id in new[] { "fairway-start", "short-par4", "small-greens", "width-steadiness", "green-scale" })
            Assert.True(Check(id).Passed, id);                                 // never a fail: the gate isn't held by them
    }

    [PrivateSampleFact("sagan-khushun-2026-10-26.course")]
    public void SaganKhushun_FairwayStart_MatchesTheBookMeasure()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, "sagan-khushun-2026-10-26.course")).ReadModel();
        var r = ReadinessChecker.Run(course, new ReadinessLimits { TestDriveYards = 360 });
        var book = new Dictionary<int, int> { [1] = 60, [3] = 115, [4] = 120, [6] = 155, [7] = 60, [9] = 60, [10] = 70,
            [11] = 60, [12] = 120, [14] = 60, [16] = 90, [17] = 60, [18] = 60 };
        var holes = r.Holes.Where(h => book.ContainsKey(h.Number)).ToList();
        Assert.True(holes.Count(h => h.FairwayStartYards is double y && Math.Abs(y - book[h.Number]) <= 15) >= 11);
        Assert.Contains("1 of 13", r.Check("fairway-start")!.Detail);
    }
}
