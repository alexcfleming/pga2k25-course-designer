using Pga2K25.CourseCard;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>The Course Difficulty Estimate: every hole played by the hole design simulator, summed and drawn on one sheet.</summary>
[Trait("Area", "Products")]
public class CourseDifficultyTests
{
    static readonly string CassimboPath = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");

    static HoleSummary Pin(int pin, int par, double expected, double birdie = 0.2, double bogey = 0.2, double gir = 0.6, double fairway = 0.5,
        double scrambling = 0.4, double penalty = 0, PhaseSg? sg = null) =>
        new(pin, "x", par, 100, 400, par + 0.1, expected, 0.02, sg ?? new PhaseSg(0, 0, 0, 0), par == 3 ? double.NaN : fairway, gir, scrambling, penalty,
            birdie, 1 - bogey, bogey, 0, "");

    /// <summary>A hole whose pins play <paramref name="over"/> strokes over par for Power, Normal half a stroke worse.</summary>
    static HoleDifficulty Hole(int n, int par, params double[] over) => new(n, par, 400, "Back",
        over.Select((o, i) => Pin(i + 1, par, par + o)).ToList(), over.Select((o, i) => Pin(i + 1, par, par + o + 0.5)).ToList(), 0.05);

    [Fact]
    public void Hole_AveragesItsPins_AndRanksByBothPlayers()
    {
        var h = Hole(1, 4, 0.1, 0.3);
        Assert.Equal(4.2, h.PowerScore, 6);
        Assert.Equal(4.7, h.NormalScore, 6);
        Assert.Equal(0.45, h.ToPar, 6);
        Assert.Equal((0.35, 0.55), (Math.Round(h.PinRange.Easiest, 6), Math.Round(h.PinRange.Hardest, 6)));
        Assert.False(h.Doubtful);
        Assert.True((h with { SteepGreen = 0.4 }).Doubtful);
    }

    [Fact]
    public void Round_AddsScoresAndPenalties_AndAveragesRates_LeavingPar3sOutOfFairways()
    {
        var par4 = new HoleDifficulty(1, 4, 400, "Back",
            [Pin(1, 4, 4.2, fairway: 0.6, penalty: 0.1, sg: new PhaseSg(-0.1, -0.1, 0, -0.1))], [Pin(1, 4, 4.6, fairway: 0.4)], 0);
        var par3 = new HoleDifficulty(2, 3, 180, "Back",
            [Pin(1, 3, 3.1, gir: 0.4, penalty: 0.2, sg: new PhaseSg(0, -0.05, 0, -0.05))], [Pin(1, 3, 3.1, gir: 0.4)], 0);
        var r = new CourseDifficultyResult("Test", [par4, par3], 100, "b", DateTime.UtcNow, 1);
        Assert.Equal(7, r.Par);
        Assert.Equal(7.3, r.Power.Score, 6);
        Assert.Equal(0.3, r.Power.ToPar, 6);
        Assert.Equal(0.6, r.Power.FairwayHit, 6);           // the par 3 has no fairway figure
        Assert.Equal(0.5, r.Power.GreenInRegulation, 6);    // (0.6 + 0.4) / 2
        Assert.Equal(0.3, r.Power.HolesWithPenalty, 6);
        Assert.Equal(-0.4, r.Power.Sg.Total, 6);
        Assert.Equal([1, 2], r.Ranked.Select(h => h.Hole));  // the par 4 plays further over par
    }

    [Fact]
    public void StrokeIndex_GivesTheHarderNineTheOdds_EachNineHardestFirst()
    {
        // The back nine plays harder in all, though the front holds the single hardest hole.
        var holes = Enumerable.Range(1, 18).Select(n => Hole(n, 4, n <= 9 ? 0.02 * n : 0.1 + 0.01 * n)).ToList();
        holes[0] = Hole(1, 4, 0.9);
        var ranked = new CourseDifficultyResult("x", holes, 1, "b", DateTime.UtcNow, 1).Ranked;
        var si = CourseDifficulty.StrokeIndex(ranked);
        Assert.Equal(Enumerable.Range(1, 18), si.Values.Order());
        Assert.All(Enumerable.Range(10, 9), n => Assert.Equal(1, si[n] % 2));   // back nine harder: odds
        Assert.All(Enumerable.Range(1, 9), n => Assert.Equal(0, si[n] % 2));
        Assert.Equal(1, si[18]);                                               // the back nine's hardest
        Assert.Equal(2, si[1]);                                                // the course's hardest, on the easier nine
        Assert.Equal(18, si[2]);                                               // the front nine's easiest
    }

    [Fact]
    public void StrokeIndex_OnNineHoles_IsTheRank()
    {
        var ranked = new CourseDifficultyResult("x", Enumerable.Range(1, 9).Select(n => Hole(n, 4, 0.05 * n)).ToList(), 1, "b", DateTime.UtcNow, 1).Ranked;
        var si = CourseDifficulty.StrokeIndex(ranked);
        Assert.Equal(1, si[9]);
        Assert.Equal(9, si[1]);
    }

    [Fact]
    public void Sheet_DrawsFor18Holes_AndForAShortCourse()
    {
        var data = CourseCardData.From(Pga2K25.CourseFormat.CourseFile.Load(CassimboPath).ReadModel(), null, null, []);
        foreach (int count in new[] { 18, 9, 17 })
        {
            // 17: an 18-hole course whose hole 16 can't be played yet keeps its row, saying why.
            var holes = Enumerable.Range(1, count == 17 ? 18 : count).Where(n => count != 17 || n != 16)
                .Select(n => Hole(n, n % 3 + 3, 0.05 * n, 0.08 * n - 0.3)).ToList();
            var result = new CourseDifficultyResult("Cassimbo", holes, 100, "b", DateTime.UtcNow, 1)
            {
                Skipped = count == 17 ? [new SkippedHole(16, 5, 624, "no green drawn near its green point")] : [],
            };
            using var renderer = new CourseCardRenderer(data, new CourseCardConfig(), null) { Difficulty = result };
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)CourseCardRenderer.CardPage.Width, (int)CourseCardRenderer.CardPage.Height));
            renderer.DrawDifficultyPage(surface.Canvas);
        }
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Cassimbo_EveryHolePlayed_AndTheSheetWritten()
    {
        string dir = Path.Combine(Path.GetTempPath(), "difficulty-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var built = CourseDifficultyBuilder.Build(CassimboPath, dir, new CourseCardConfig { Formats = ["png", "pdf"], Dpi = 72 }, samples: 60);
            var r = built.Result;
            Assert.Equal(18, r.Holes.Count);
            Assert.Equal(72, r.Par);
            Assert.All(r.Holes, h => Assert.Equal(4, h.Power.Count));   // every pin, both players
            Assert.All(r.Holes, h => Assert.Equal(h.Power.Count, h.Normal.Count));
            Assert.InRange(r.Power.Score, 66, 90);
            Assert.True(r.Normal.Score > r.Power.Score, "the shorter hitter scores higher");
            Assert.Equal(2, built.Files.Count);
            Assert.All(built.Files, f => Assert.True(new FileInfo(f).Length > 10_000, f));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
