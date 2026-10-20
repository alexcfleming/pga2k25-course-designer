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

    static readonly Lazy<CourseModel> Luke = new(() => Load("luke-normal-golf-course.course"));
    static readonly Lazy<WaterMap> LukeWater = new(() => WaterTracer.Trace(Luke.Value, new TerrainModelOptions { CellMetres = 0.5 }));

    [Fact]
    public void WaterEntries_KeepBothFootprintAxes()
    {
        var w = Luke.Value.Water;
        Assert.Equal(9, w.Count);
        Assert.Equal(49.923, w[1].HalfX, 3);   // the creek plane is a long rectangle
        Assert.Equal(22.799, w[1].HalfZ, 3);
        Assert.Equal(13.037, w[1].Level, 3);
        Assert.True(w[3].Contains(w[3].Position + new Vec2(0, 60)) != w[3].Contains(w[3].Position + new Vec2(60, 0)));
    }

    [Fact]
    public void LargeEntries_ArePlanes_SmallDabs_AreCutStrokes()
    {
        Assert.All(LukeWater.Value.Strokes, s => Assert.Equal(WaterKind.Plane, s.Kind));
        var berwick = WaterTracer.Trace(Load("berwick-coast.course"), new TerrainModelOptions { CellMetres = 1 });
        Assert.All(berwick.Strokes, s => Assert.Equal(WaterKind.CutStroke, s.Kind));
        // The stream is one continuous 700 m × 10 m stroke, as before.
        Assert.Single(berwick.Bodies);
        Assert.InRange(berwick.Bodies[0].AreaM2, 6000, 9000);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void EdgeSlivers_AreDropped()
    {
        // Plane 3 (by hole 4) clips two small pieces at its rectangle; in game every shoreline is rounded.
        var m = LukeWater.Value;
        Assert.Equal(7, m.Bodies.Count);
        Assert.All(m.Bodies, b => Assert.True(b.AreaM2 > 300));
    }

    [Fact]
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
    static readonly Lazy<CourseModel> Course = new(() =>
        CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "luke-normal-golf-course.course")).ReadModel());
    static readonly Lazy<WaterMap> Water = new(() => WaterTracer.Trace(Course.Value, new YardageBook.YardageOptions().Terrain));

    static YardageBook.HoleAnalysis Analyze(int number) =>
        YardageBook.HoleAnalysis.Analyze(Course.Value, CourseLayers.From(Course.Value), Course.Value.Holes.Single(h => h.Number == number),
            new YardageBook.YardageOptions(), YardageBook.YardagePageRenderer.MapRect.Width / YardageBook.YardagePageRenderer.MapRect.Height,
            inlandWater: Water.Value);

    [Fact]
    public void Hole1_CreekCarryOffTheTee_IsNumbered()
    {
        // 54 yds is well short of the 120-yd bunker filter; a forced carry over water still gets its number.
        var a = Analyze(1);
        Assert.NotEmpty(a.WaterBodies);
        Assert.Contains(a.WaterBodies, b => b.Count > 1);                      // the ring pond keeps its island
        Assert.Contains(a.BunkerLabels, l => !l.IsFront && l.FromTeeYards is >= 45 and <= 65);
    }

    [Fact]
    public void Hole6_PondAcrossTheFairway_IsOneCrossing()
    {
        // Near bank and far bank of one pond: a front (lay-up) number and a carry, not two separate stretches.
        var a = Analyze(6);
        var front = Assert.Single(a.BunkerLabels, l => l.IsFront && l.FromTeeYards is > 230 and < 290);
        var carry = Assert.Single(a.BunkerLabels, l => !l.IsFront && l.FromTeeYards is > 300 and < 360);
        Assert.True(carry.FromTeeYards - front.FromTeeYards >= 50);
    }
}
