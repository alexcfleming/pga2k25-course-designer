using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

[Trait("Area", "Products")]
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

    [Trait("Speed", "Slow")]
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

        // Names for the wrong number of tee sets are reported and not used: the cards fall back to the tee colours the
        // game saved with the course (since 3.8; "Tee 1 … Back" before, and still when the file has no colours).
        var wrong = Data(CassimboPath, out var wrongIssues, new CourseHoles { TeeNames = ["Red", "Blue"] });
        Assert.Equal(new[] { "Red", "Green", "White", "Blue" }, wrong.TeeNames);
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
            var config = new CourseCardConfig { Dpi = 30, Formats = ["png"], WindFromDegrees = 225, AutoHoleNotes = false };
            var withRose = CourseCardBuilder.Build(SaganPath, dir, config);
            Assert.Equal(rose.PrevailingFrom, withRose.WindFromDegrees);   // the Wind Card prints the rose's prevailing direction
            Assert.Equal(WindRose.SectorOf(90), WindRose.SectorOf(withRose.WindFromDegrees!.Value));
            Assert.Contains(withRose.Files, f => f.EndsWith("Wind Card.png"));
            Assert.Empty(withRose.NotDrawn);
            Assert.Null(withRose.CombinedPdf);                             // PNGs only: no combined PDF
            Assert.Contains(withRose.Issues, i => i.Message.StartsWith("Wind set by hand (SW) differs"));

            // The climate model: the arrow follows the generated rose, the one the Wind Card draws.
            var model = new CoursePlace { WindSource = "model", Climate = "sea", WindFromDegrees = 315 };
            CoursePlaces.Save(dir, model);
            var modelled = CourseCardBuilder.Build(SaganPath, dir, config with { WindFromDegrees = null });
            var drawn = model.Rose(Maps.OceanFinder.SeaBearing(model, CourseFile.Load(SaganPath).ReadModel()))!;
            Assert.Equal(drawn.PrevailingFrom, modelled.WindFromDegrees);

            // No rose: the wind set by hand, card settings first.
            CoursePlaces.Save(dir, new CoursePlace { WindFromDegrees = 270 });
            var noRose = CourseCardBuilder.Build(SaganPath, dir, config);
            Assert.Equal(225, noRose.WindFromDegrees);
            // A card left out is named with its reason, on the result, in the log and in the validation file.
            Assert.Equal([("Wind Card", "no wind rose on the Place card")], noRose.NotDrawn);
            Assert.Contains(noRose.Issues, i => i.Message == "Cards not drawn: Wind Card (no wind rose on the Place card).");
            Assert.Equal(270, CourseCardBuilder.Build(SaganPath, dir, config with { WindFromDegrees = null }).WindFromDegrees);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Build_StopsOnErrorsUnlessForced()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-cards-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new CourseCardConfig { Dpi = 40, Formats = ["png"], AutoHoleNotes = false };
            Assert.Throws<InvalidDataException>(() => CourseCardBuilder.Build(CassimboPath, dir, config));
            Assert.Empty(Directory.GetFiles(dir, "*.png"));
            var forced = CourseCardBuilder.Build(CassimboPath, dir, config, force: true);
            Assert.Equal(6, forced.Files.Count);   // compass rose, hole data, course data, course details, green size and shot variety cards
            Assert.Contains(forced.Files, f => f.EndsWith("Course Details Card.png"));
            // With PDFs, every card also goes into one "Course Cards" PDF, a page per card.
            var sagan = CourseCardBuilder.Build(SaganPath, dir, config with { Formats = ["pdf"] });
            Assert.Equal("WEST", sagan.OceanSide);
            Assert.Equal(Path.Combine(dir, "Sagan-Khushun - Course Cards.pdf"), sagan.CombinedPdf);
            Assert.Equal(sagan.Files.Count - 1, PdfPages(sagan.CombinedPdf!));
            Assert.All(sagan.Files, f => Assert.True(File.Exists(f), f));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Trait("Speed", "Slow")]
    [PrivateSampleFact("luke-normal-golf-course.course")]
    public void Incomplete_course_stops_unless_forced_and_forced_cards_are_drafts()
    {
        // Luke's 9-hole course fails "Expected 18 holes"; Studio builds forced so it still gets cards, marked DRAFT.
        var path = Path.Combine(TestPaths.PrivateDir, "luke-normal-golf-course.course");
        var dir = Path.Combine(Path.GetTempPath(), $"cards-draft-{Guid.NewGuid():N}");
        try
        {
            var config = new CourseCardConfig { Formats = ["png"], Dpi = 40, AutoHoleNotes = false };
            Assert.Throws<InvalidDataException>(() => CourseCardBuilder.Build(path, dir, config));
            var result = CourseCardBuilder.Build(path, dir, config, force: true);
            Assert.Contains(result.Issues, i => i.Severity == Severity.Error && i.Message.Contains("18 holes"));
            Assert.Contains(result.Files, f => f.EndsWith("Course Data Card.png"));
            Assert.Contains(result.Files, f => f.EndsWith("Hole Data Card.png"));
            var report = File.ReadAllText(Directory.GetFiles(dir, "* - Card Validation.txt").Single());
            Assert.Contains("Drawn as DRAFT cards", report);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Pages in a PDF Skia wrote: its page objects.</summary>
    static int PdfPages(string path) =>
        System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)), @"/Type\s*/Page\b(?!s)").Count;

    [Fact]
    public void LengthSpreads_GiveEachParsShortestAndLongest()
    {
        var data = Data(SaganPath, out _);
        using var r = new CourseCardRenderer(data, new CourseCardConfig(), null);
        var spreads = r.LengthSpreads();
        Assert.Equal([3, 4, 5], spreads.Select(s => s.Par));
        foreach (var (par, text) in spreads)
        {
            var yards = data.Holes.Where(h => h.Par == par).Select(h => h.Yards[data.RoseTee]).ToList();
            Assert.Equal($"Par {par}  {yards.Min()}–{yards.Max()} ({yards.Max() - yards.Min()})", text);
        }
    }

    [Fact]
    public void EveryCardDraws_OnTheCommonPage()
    {
        // The whole set on one canvas size (the rose keeps its own): hole discs, width columns, the dogleg mark,
        // estimated-green stubs, the sorted chart's spreads and the rose's bearings all draw without a fault.
        var course = Samples.Course("murray-estate.course");
        var issues = new List<CardIssue>();
        var data = CourseCardData.From(course, null, null, issues);
        Assert.Contains(data.Holes, h => Math.Abs(h.Bend) > CourseCardData.MaxBendDegrees);    // hole 12: marked on the card
        Assert.Contains(data.Greens.Greens, g => g.Estimated);
        using var r = new CourseCardRenderer(data, new CourseCardConfig(), null)
        {
            FairwayWidths = Pga2K25.Readiness.ReadinessChecker.FairwayWidths(course).ToDictionary(w => w.Number),
        };
        using var card = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)CourseCardRenderer.CardPage.Width, (int)CourseCardRenderer.CardPage.Height));
        r.DrawTablePage(card.Canvas);
        r.DrawCoursePage(card.Canvas);
        r.DrawGreenSizePage(card.Canvas);
        using var rose = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)CourseCardRenderer.RosePage.Width, (int)CourseCardRenderer.RosePage.Height));
        r.DrawRosePage(rose.Canvas);
    }

    // ───────── fairway width columns on the hole data card ─────────

    static Pga2K25.Readiness.HoleFairwayWidths Widths(bool built, params double?[] yards) =>
        new(1, [], yards.Select((w, i) => new Pga2K25.Readiness.StationWidth(300 + 25 * i, w)).ToList(), built);

    [Fact]
    public void WidthCell_ShowsTheRangeAndFlagsWhatIsOutside()
    {
        (string, CourseCardRenderer.WidthTone) Cell(Pga2K25.Readiness.HoleFairwayWidths w) => CourseCardRenderer.WidthCell(w, w.Driver, 25, 35);
        Assert.Equal(("26–28", CourseCardRenderer.WidthTone.Plain), Cell(Widths(true, 28.2, 26.4, 27)));
        Assert.Equal(("30", CourseCardRenderer.WidthTone.Plain), Cell(Widths(true, 30.2, 29.8)));           // stations agree: one number
        Assert.Equal(("30–45", CourseCardRenderer.WidthTone.Flag), Cell(Widths(true, 30, 45, 41)));         // one station wide
        Assert.Equal(("15–27", CourseCardRenderer.WidthTone.Flag), Cell(Widths(true, 19, 15, 27)));         // narrow
        Assert.Equal(("none–30", CourseCardRenderer.WidthTone.Flag), Cell(Widths(true, 30, 30, null)));     // no fairway at one station
        Assert.Equal(("none", CourseCardRenderer.WidthTone.Flag), Cell(Widths(true, null, null)));
        Assert.Equal(("not built", CourseCardRenderer.WidthTone.Quiet), Cell(Widths(false, null, null, null))); // nothing to measure: not judged
        Assert.Equal(("–", CourseCardRenderer.WidthTone.Quiet), Cell(Widths(true)));                        // too short for a station
    }

    [Fact]
    public void FairwayWidths_AreTheOnesReadinessJudges_AndWidenTheHoleDataCard()
    {
        var widths = Pga2K25.Readiness.ReadinessChecker.FairwayWidths(Samples.Cassimbo);
        var report = ReadinessTests.Report.Value;
        Assert.Equal(report.Holes.Where(h => h.Par >= 4).Select(h => h.Number), widths.Select(w => w.Number));
        Assert.All(widths, w =>
        {
            var h = report.Holes[w.Number - 1];
            Assert.Equal(h.Stations, w.Driver);
            Assert.Equal(h.ThreeWoodStations, w.ThreeWood);
            Assert.Equal(h.FairwayBuilt, w.Built);
        });

        var data = Data(CassimboPath, out _);
        using var plain = new CourseCardRenderer(data, new CourseCardConfig(), null);
        using var wide = new CourseCardRenderer(data, new CourseCardConfig(), null) { FairwayWidths = widths.ToDictionary(w => w.Number) };
        Assert.Equal(CourseCardRenderer.CardPage, CourseCardRenderer.TablePage);   // one page size for every card but the rose
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1224, 835));
        plain.DrawTablePage(surface.Canvas);                                   // both layouts draw
        wide.DrawTablePage(surface.Canvas);
    }
}
