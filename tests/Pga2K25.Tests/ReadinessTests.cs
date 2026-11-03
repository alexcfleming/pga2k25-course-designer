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
    public void Cassimbo_Length_ShortPar4sAndPar5s()
    {
        // §12.4, card yards from the back tee: median par 4 387, six par 4s under 400, par 5s of 537 and 548.
        var median = Check("median-par4");
        Assert.Equal(Severity.Warning, median.Severity);
        Assert.Contains("387", median.Detail);
        var fours = Check("par4-under400");
        Assert.Equal(Severity.Warning, fours.Severity);
        Assert.Equal([1, 4, 8, 13, 16, 17], fours.Holes);
        var fives = Check("shortest-par5");
        Assert.Equal(Severity.Warning, fives.Severity);
        Assert.Equal([3, 18], fives.Holes);
        Assert.Equal(178, Report.Value.Holes[2].LeftAfter(350));             // "very short par 5", straight from the back tee
        Assert.Equal(Severity.Info, Check("length-total").Severity);         // a range, never judged
        Assert.Null(Report.Value.Check("drivable-front"));                    // retired (§11.4)
        Assert.Null(Report.Value.Check("par5"));
    }

    [Fact]
    public void Cassimbo_GreenSizes_Two_AgainstSeven()
    {
        // "Greens wildly different in size (2 versus 7)." A prompt now: the band flagged an approved course.
        var c = Check("green-scale");
        Assert.Equal(Severity.Info, c.Severity);
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
        var loose = ReadinessChecker.Run(Cassimbo.Value, new ReadinessLimits { ShortestPar5MinYards = 530, MedianPar4MinYards = 380, WidthHighYards = 250 });
        Assert.Equal(Severity.Pass, loose.Check("shortest-par5")!.Severity);
        Assert.Equal(Severity.Pass, loose.Check("median-par4")!.Severity);
        Assert.DoesNotContain(12, loose.Check("landing-widths")!.Holes);
    }

    [Fact]
    public void DesignTeeShot_SetsTheWidthStations()
    {
        var r = ReadinessChecker.Run(Load("murray-estate.course"), new ReadinessLimits { DesignDriveMinYards = 280, DesignDriveMaxYards = 320 });
        Assert.All(r.Holes.Where(h => h.Stations.Count > 0), h => Assert.Equal(280, h.Stations[0].FromTeeYards));
        Assert.All(r.Holes.Where(h => h.Par == 3), h => Assert.Empty(h.Stations));
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
        Assert.Equal(Severity.Info, c.Severity);                              // a note: no reviewer has mentioned it
        Assert.Contains("1 of 14", c.Detail);
        Assert.InRange(Report.Value.MedianFairwayStartYards, 85, 110);
        Assert.All(Report.Value.Holes.Where(h => h.Par == 3), h => Assert.Null(h.FairwayStartYards));
    }

    [Fact]
    public void Overrides_TheDesignersStartAndSharedFairways()
    {
        var r = ReadinessChecker.Run(Cassimbo.Value, overrides: new Dictionary<int, HoleOverride>
        {
            [1] = new(190, false),
            [12] = new(null, true),
        });
        Assert.Equal(190, r.Holes[0].FairwayStartYards);
        Assert.True(r.Holes[0].FairwayStartFileYards < 150);
        Assert.Contains("2 of 13", r.Check("fairway-start")!.Detail);         // hole 1 now in band, hole 12 left out
        Assert.Contains(12, r.Check("landing-widths")!.Holes);               // a shared fairway's width still counts
    }

    [Fact]
    public void Cassimbo_LandingWidths_At300To350()
    {
        // §12.2: over 70 on 1, 7, 11 and 12; under 25 or no fairway by the line on 16 and 18. Shared fairways count.
        var c = Check("landing-widths");
        Assert.Equal(Severity.Warning, c.Severity);
        Assert.Equal([1, 7, 11, 12, 16, 18], c.Holes);
        var h11 = Report.Value.Holes[10];
        Assert.Equal([300, 325, 350], h11.Stations.Select(s => s.FromTeeYards));
        Assert.All(h11.Stations, s => Assert.InRange(s.WidthYards!.Value, 100, 130));
        Assert.Null(Report.Value.Holes[17].Stations[0].WidthYards);          // no fairway by the line at 300 on 18
        Assert.Empty(Report.Value.Holes[16].Stations);                       // 17 is too short to land 40 yds short of the green
        Assert.Contains("1, 7, 11, 12, 13", c.Detail);                       // reasons to write: over 60
        Assert.Equal([1, 11, 12, 13, 14], Check("width-pattern").Holes);
        Assert.True(Check("width-steadiness").Passed);
    }

    [Fact]
    public void Cassimbo_LandingHazards_AreAPromptOnly()
    {
        var c = Check("landing-hazards");
        Assert.Equal(Severity.Info, c.Severity);
        var h8 = Report.Value.Holes[7];
        Assert.Contains(h8.LandingHazards, z => z.Kind == "bunker" && z.Side == "on the line" && z.FromTeeYards is >= 320 and <= 340);
        Assert.Empty(Report.Value.Holes[2].LandingHazards);
        Assert.Equal(Severity.Info, Check("forward-tees").Severity);
        Assert.InRange(Report.Value.Holes[6].ForwardCarryYards!.Value, 170, 215);
    }

    [Fact]
    public void Cassimbo_ProvisionalChecks_OnlyWarn()
    {
        foreach (var id in new[] { "median-par4", "par4-under400", "shortest-par5", "landing-widths", "width-pattern" })
        {
            Assert.Equal(Severity.Warning, Check(id).Severity);
            Assert.True(Check(id).Passed, id);                                 // never a fail: the gate isn't held by them
        }
        foreach (var id in new[] { "green-scale", "green-approach", "fairway-start", "card-straight" })
            Assert.Equal(Severity.Info, Check(id).Severity);
        Assert.Equal([4, 11, 18], Check("card-straight").Holes);
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
