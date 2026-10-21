using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.Tests;

/// <summary>
/// Sangoma (a private sample, samples/private/sangoma.course, with in-game screenshots of its four ponds beside it):
/// five water planes over 103,803 height stamps, and 2,290 distance markers that carry the game's own ground height.
/// The markers caught a replay error the ponds made plain: 1000 m lower squares centred on the plot edge, which the
/// game doesn't apply, sank the ground 35–100 m and flooded hole 14's plane as a solid rectangle.
/// </summary>
public class SangomaTests
{
    public const string File = "sangoma.course";
    public static CourseModel Load() => CourseFile.Load(Path.Combine(TestPaths.PrivateDir, File)).ReadModel();

    static readonly Lazy<CourseModel> Course = new(Load);
    static readonly Lazy<WaterMap> Water = new(() => WaterTracer.Trace(Course.Value, new TerrainModelOptions { CellMetres = 0.5 }));

    /// <summary>Replay minus game height (m) at every distance marker.</summary>
    static List<double> MarkerErrors(TerrainModelOptions options)
    {
        var marks = Course.Value.DistanceMarkers;
        var pad = new Vec2(5, 5);
        var f = TerrainReplay.Build(Course.Value, new Vec2(marks.Min(m => m.Position.X), marks.Min(m => m.Position.Z)) - pad,
            new Vec2(marks.Max(m => m.Position.X), marks.Max(m => m.Position.Z)) + pad, options);
        return marks.Select(m => f.Sample(m.Position) - m.Height).OrderBy(e => e).ToList();
    }

    static double Rms(List<double> e) => Math.Sqrt(e.Average(x => x * x));

    [PrivateSampleFact(File)]
    public void DistanceMarkers_CarryTheGamesGroundHeight()
    {
        var marks = Course.Value.DistanceMarkers;
        Assert.Equal(2290, marks.Count);
        Assert.All(marks, m => Assert.InRange(m.Height, 140, 200));
    }

    [PrivateSampleFact(File)]
    public void Replay_MatchesTheDistanceMarkers()
    {
        var e = MarkerErrors(new TerrainModelOptions { CellMetres = 1 });
        Assert.InRange(e[e.Count / 2], -1, 1);                              // median −0.4 m
        Assert.True(Rms(e) < 2.6, $"RMS {Rms(e):0.00} m");                  // 2.3 m; the rest is spread over big soft brushes
    }

    [PrivateSampleFact(File)]
    public void EdgeCentredStamps_AreLeftOut()
    {
        // Two −46.65 m squares at z = −1000 and four −32.73 m at z = +1000, all 1000 m across.
        var edge = Course.Value.HeightStamps.Where(s => TerrainReplay.IsOnPlotEdge(s) && s.Tool == 1 && s.Scale >= 1000).ToList();
        Assert.Equal(6, edge.Count);
        var with = MarkerErrors(new TerrainModelOptions { CellMetres = 1, SkipEdgeStamps = false });
        Assert.True(Rms(with) > 5, $"RMS {Rms(with):0.00} m with them");
        Assert.True(with[0] < -90);                                          // 100 m low at the north end of the routing
    }

    [PrivateSampleFact(File)]
    public void Hole14Pond_IsAPond_NotItsRectangle()
    {
        // Plane 2 is 128 × 147 m (18,760 m²); in game a small pond with rounded banks.
        var plane = Course.Value.Water[2];
        var body = Assert.Single(Water.Value.Bodies, b => plane.Contains(b.LabelPoint));
        Assert.InRange(body.AreaM2, 3000, 8000);
        Assert.InRange(body.MaxDepthMetres, 4, 10);

        var old = WaterTracer.Trace(Course.Value, new TerrainModelOptions { CellMetres = 1, SkipEdgeStamps = false });
        Assert.Contains(old.Bodies, b => plane.Contains(b.LabelPoint) && b.AreaM2 > 15000);
    }

    [PrivateSampleFact(File)]
    public void FourPonds_FromFivePlanes()
    {
        // Plane 3, a 294 × 1116 m strip at 137.4 m, has ground above its level everywhere under it: it shows nothing.
        var m = Water.Value;
        Assert.Equal(4, m.Bodies.Count);
        Assert.DoesNotContain(m.Bodies, b => Math.Abs(b.Level - Course.Value.Water[3].Level) < 0.01);
        Assert.All(m.Bodies, b => Assert.InRange(b.MaxDepthMetres, 1, 10));
    }

    [PrivateSampleFact(File)]
    public void Hole11Pond_IsOneOval()
    {
        var plane = Course.Value.Water[1];
        var body = Water.Value.Bodies[Water.Value.BodyAt(plane.Position)];
        Assert.InRange(body.AreaM2, 8000, 11000);
        Assert.Single(body.Shores);                                          // no island, as in game
    }

    [PrivateSampleFact(File)]
    public void DistanceMarkers_StayOutOfTheWater()
    {
        // The markers sit on dry ground in game; only two, on the hole 4 pond's bank, land in the model's water, and
        // they stand less than half a metre above its level.
        var m = Water.Value;
        var wet = Course.Value.DistanceMarkers.Where(d => m.IsWet(d.Position)).ToList();
        Assert.True(wet.Count <= 2, $"{wet.Count} markers in the water");
        Assert.All(wet, d => Assert.InRange(d.Height - m.Bodies[m.BodyAt(d.Position)].Level, -0.5, 0.5));
    }
}

/// <summary>Sangoma's ponds in play: the hole inspector's list and the yardage book.</summary>
public class SangomaWaterInPlayTests
{
    static readonly Lazy<CourseModel> Course = new(SangomaTests.Load);

    static readonly Lazy<TerrainMap.TerrainSurvey> Survey = new(() => TerrainMap.TerrainSurvey.Build(
        Course.Value, new TerrainMap.TerrainMapOptions { Terrain = new TerrainMap.TerrainMapOptions().Terrain with { CellMetres = 1 } }));

    static List<TerrainMap.HoleWaterRow> Along(int hole) =>
        TerrainMap.HoleWater.Along(Survey.Value, Survey.Value.Holes.Single(h => h.Hole.Number == hole));

    [PrivateSampleFact(SangomaTests.File)]
    public void Hole11_CarriesThePond()
    {
        // A par 3 over the pond from the tee to the green's front ("hole 11 pond 3" screenshot).
        var pond = Assert.Single(Along(11), r => r.Crosses);
        Assert.InRange(pond.FrontYards, 20, 45);
        Assert.InRange(pond.CarryYards, 125, 155);
    }

    [PrivateSampleFact(SangomaTests.File)]
    public void Hole2_PondOnTheLeft()
    {
        var pond = Assert.Single(Along(2));
        Assert.False(pond.Crosses);
        Assert.Equal("left", pond.Side);
    }

    [PrivateSampleFact(SangomaTests.File)]
    public void Hole14_PondIsOutOfPlay()
    {
        // The pond sits well left of the line; before the edge fix its rectangle reached the fairway.
        Assert.Empty(Along(14));
    }

    [PrivateSampleFact(SangomaTests.File)]
    public void Hole11_YardagePage_NumbersTheCarry()   // par 3: the carry 39 yds short of the green still counts
    {
        var c = Course.Value;
        var water = WaterTracer.Trace(c, new YardageBook.YardageOptions().Terrain);
        var a = YardageBook.HoleAnalysis.Analyze(c, CourseLayers.From(c), c.Holes.Single(h => h.Number == 11),
            new YardageBook.YardageOptions(), YardageBook.YardagePageRenderer.MapRect.Width / YardageBook.YardagePageRenderer.MapRect.Height,
            inlandWater: water);
        Assert.NotEmpty(a.WaterBodies);
        Assert.Contains(a.BunkerLabels, l => !l.IsFront && l.FromTeeYards is >= 125 and <= 155);
    }
}
