using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.Tests;

/// <summary>
/// Water planes (Luke Muscat's Normal Golf Course: nine large <c>water[]</c> rectangles) against painted
/// streams (Berwick Coast: 55 small dabs whose channel the game cuts).
/// </summary>
public class WaterTests
{
    static CourseModel Load(string file) => CourseFile.Load(Path.Combine(TestPaths.SamplesDir, file)).ReadModel();

    /// <summary>Luke's course is a private sample (samples/private); its tests skip without it.</summary>
    public const string LukeFile = "luke-normal-golf-course.course";
    public static CourseModel LoadLuke() => CourseFile.Load(Path.Combine(TestPaths.PrivateDir, LukeFile)).ReadModel();

    static readonly Lazy<CourseModel> Luke = new(LoadLuke);
    static readonly Lazy<WaterMap> LukeWater = new(() => WaterTracer.Trace(Luke.Value, new TerrainModelOptions { CellMetres = 0.5 }));

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void WaterEntries_KeepBothFootprintAxes()
    {
        var w = Luke.Value.Water;
        Assert.Equal(9, w.Count);
        Assert.Equal(49.923, w[1].HalfX, 3);   // the creek plane is a long rectangle
        Assert.Equal(22.799, w[1].HalfZ, 3);
        Assert.Equal(13.037, w[1].Level, 3);
        Assert.True(w[3].Contains(w[3].Position + new Vec2(0, 60)) != w[3].Contains(w[3].Position + new Vec2(60, 0)));
    }

    [PrivateSampleFact(LukeFile)]
    public void LargeEntries_ArePlanes() => Assert.All(LukeWater.Value.Strokes, s => Assert.Equal(WaterKind.Plane, s.Kind));

    [Fact]
    public void SmallDabs_AreCutStrokes()
    {
        var berwick = WaterTracer.Trace(Load("berwick-coast.course"), new TerrainModelOptions { CellMetres = 1 });
        Assert.All(berwick.Strokes, s => Assert.Equal(WaterKind.CutStroke, s.Kind));
        // The stream is one continuous 700 m × 10 m stroke, as before.
        Assert.Single(berwick.Bodies);
        Assert.InRange(berwick.Bodies[0].AreaM2, 6000, 9000);
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void Planes_ShowOnlyWhereTheGroundIsBelowTheLevel()
    {
        var m = LukeWater.Value;
        var ring = Luke.Value.Water[0];
        Assert.False(m.IsWet(ring.Position));                                    // the ring pond's island
        Assert.True(m.IsWet(ring.Position + new Vec2(-12, 0)));                  // ...inside its ring of water
        Assert.False(m.IsWet(ring.Position + new Vec2(0, ring.HalfZ - 5)));      // land inside the footprint stays land
        Assert.InRange(m.Bodies.Sum(b => b.AreaM2), 30000, 36000);
        Assert.All(m.Bodies, b => Assert.InRange(b.MaxDepthMetres, 2, 6));
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void ShallowDugChannel_JoinsTheHole1Creek()
    {
        // In game the creek runs from the ring pond to the east pool; the replayed bed rises ~1.6 m between them.
        var m = LukeWater.Value;
        int creek = m.BodyAt(new Vec2(400, -497)), pool = m.BodyAt(new Vec2(338, -479));
        Assert.True(creek >= 0);
        Assert.Equal(creek, pool);
        Assert.Equal(creek, m.BodyAt(Luke.Value.Water[0].Position + new Vec2(-12, 0)));
        Assert.InRange(m.Bodies[creek].ChannelMetres, 50, 150);
        Assert.Single(m.Bodies, b => b.ChannelMetres > 0);                          // nothing else on the course qualifies
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void Hole1_CarriesTheCreekOffTheTee()
    {
        var m = LukeWater.Value;
        var h1 = Luke.Value.Holes[0];
        var a = h1.Tees[^1];
        var g = h1.GreenAnchor;
        double len = Vec2.Distance(a, g);
        var wetYards = Enumerable.Range(0, (int)(len * 4)).Select(i => i * 0.25)
            .Where(t => m.IsWet(a + (g - a) * (t / len))).Select(t => t / 0.9144).ToList();
        Assert.NotEmpty(wetYards);
        Assert.InRange(wetYards.Min(), 35, 60);
        Assert.InRange(wetYards.Max(), 35, 65);
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void EdgeSlivers_AreDropped()
    {
        // Plane 3 (by hole 4) clips two small pieces at its rectangle; in game every shoreline is rounded.
        var m = LukeWater.Value;
        Assert.Equal(7, m.Bodies.Count);
        Assert.All(m.Bodies, b => Assert.True(b.AreaM2 > 300));
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void NarrowOpenWaterSplines_AreWalkingPaths()
    {
        var layers = CourseLayers.From(Luke.Value);
        Assert.Empty(layers.WaterRibbons);
        Assert.Equal(20, Luke.Value.SurfaceSplines.Count(Surfaces.IsWalkingPath));
        Assert.True(layers.Paths.Count >= 20);
    }
}

/// <summary>Yardage pages on Luke Muscat's course: water planes drawn and given carry numbers.</summary>
public class WaterYardageTests
{
    static readonly Lazy<CourseModel> Course = new(WaterTests.LoadLuke);
    static readonly Lazy<WaterMap> Water = new(() => WaterTracer.Trace(Course.Value, new YardageBook.YardageOptions().Terrain));

    static YardageBook.HoleAnalysis Analyze(int number) =>
        YardageBook.HoleAnalysis.Analyze(Course.Value, CourseLayers.From(Course.Value), Course.Value.Holes.Single(h => h.Number == number),
            new YardageBook.YardageOptions(), YardageBook.YardagePageRenderer.MapRect.Width / YardageBook.YardagePageRenderer.MapRect.Height,
            inlandWater: Water.Value);

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void Hole1_CreekCarryOffTheTee_IsNumbered()
    {
        // 54 yds is well short of the 120-yd bunker filter; a forced carry over water still gets its number.
        var a = Analyze(1);
        Assert.NotEmpty(a.WaterBodies);
        Assert.Contains(a.WaterBodies, b => b.Count > 1);                      // the ring pond keeps its island
        Assert.Contains(a.BunkerLabels, l => !l.IsFront && l.FromTeeYards is >= 45 and <= 65);
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void Hole6_PondAcrossTheFairway_IsOneCrossing()
    {
        // Near bank and far bank of one pond: a front (lay-up) number and a carry, not two separate stretches.
        var a = Analyze(6);
        var front = Assert.Single(a.BunkerLabels, l => l.IsFront && l.FromTeeYards is > 230 and < 290);
        var carry = Assert.Single(a.BunkerLabels, l => !l.IsFront && l.FromTeeYards is > 300 and < 360);
        Assert.True(carry.FromTeeYards - front.FromTeeYards >= 50);
    }
}

public class WaterScorecardTests
{
    [PrivateSampleFact(WaterTests.LukeFile)]
    public void ScorecardLayers_KeepPondsNearPlay_Whole()
    {
        var course = WaterTests.LoadLuke();
        var water = WaterTracer.Trace(course, new TerrainModelOptions { CellMetres = 0.5 });
        var layers = CourseLayers.From(course).AddInlandWater(water);
        Assert.InRange(layers.WaterBodies.Count, 1, water.Bodies.Count - 1);       // the pond out in the scenery is left off
        Assert.Contains(layers.WaterBodies, b => b.Count > 1);                     // islands kept as separate shores
        var extent = layers.ExtentPoints().ToList();
        Assert.All(layers.WaterBodies, b => Assert.Contains(b[0][0], extent));     // ponds are fitted whole, not sliced
    }
}

/// <summary>The hole inspector's "water in play" list on Luke Muscat's course.</summary>
public class HoleWaterTests
{
    static readonly Lazy<TerrainMap.TerrainSurvey> Survey = new(() => TerrainMap.TerrainSurvey.Build(
        WaterTests.LoadLuke(),
        new TerrainMap.TerrainMapOptions { Terrain = new TerrainMap.TerrainMapOptions().Terrain with { CellMetres = 1 } }));

    static List<TerrainMap.HoleWaterRow> Along(int hole) =>
        TerrainMap.HoleWater.Along(Survey.Value, Survey.Value.Holes.Single(h => h.Hole.Number == hole));

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void Hole1_CarriesTheCreek()
    {
        var creek = Assert.Single(Along(1), r => r.Crosses);
        Assert.InRange(creek.FrontYards, 40, 60);
        Assert.InRange(creek.CarryYards - creek.FrontYards, 2, 15);              // a narrow creek, not the old 60-yd lozenge
        Assert.InRange(creek.LevelFeet, 12, 16);                                 // 14 ft on the map
        var row = new Pga2K25.Studio.ViewModels.WaterRowViewModel(creek);
        Assert.StartsWith("Carry over water · 14 ft", row.Label);
        Assert.Contains("to green", row.Detail);
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void Hole6_CarriesThePond_FrontToFarBank()
    {
        var pond = Assert.Single(Along(6), r => r.Crosses);
        Assert.InRange(pond.FrontYards, 230, 290);
        Assert.InRange(pond.CarryYards, 300, 360);
    }

    [PrivateSampleFact(WaterTests.LukeFile)]
    public void WaterBesideTheLine_IsListedWithItsSide()
    {
        var beside = Survey.Value.Holes.SelectMany(h => Along(h.Hole.Number)).Where(r => !r.Crosses).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, r => Assert.Contains(r.Side, new[] { "left", "right" }));
        Assert.All(beside, r => Assert.InRange(r.OffsetYards, 0, (int)Math.Ceiling(TerrainMap.HoleWater.AlongsideMetres * 1.0936)));
    }
}
