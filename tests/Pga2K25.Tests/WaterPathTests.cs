using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

public class WaterPathTests
{
    // drgnslyr221's save of 31 Oct: streams made with "running water", ponds with "create a body of water".
    const string DevilsPerch = "devils-perch-2026-10-31.course";

    static CourseModel Load() => CourseFile.Load(Path.Combine(TestPaths.PrivateDir, DevilsPerch)).ReadModel();

    static Vec2 Centre(WaterPath p) => new(p.Path.Points.Average(q => q.Anchor.X), p.Path.Points.Average(q => q.Anchor.Z));

    [PrivateSampleFact(DevilsPerch)]
    public void DevilsPerch_WaterToolPaths_AreRead()
    {
        var course = Load();
        Assert.Equal(14, course.WaterPaths.Count);
        var ponds = course.WaterPaths.Where(p => p.IsBody).ToList();
        Assert.Equal(6, ponds.Count);
        Assert.All(ponds, p => Assert.InRange(p.Level!.Value, 30, 52));
        Assert.All(course.WaterPaths.Where(p => !p.IsBody), p => Assert.Null(p.Level));
        Assert.True(course.HasWater);
    }

    [PrivateSampleFact(DevilsPerch)]
    public void DevilsPerch_PondsAndStreams_AreTracedAsWater()
    {
        // None of these has a water[] entry: before, the maps showed dry ground at every one.
        var course = Load();
        var o = new TerrainModelOptions { CellMetres = 1 };
        foreach (var pond in course.WaterPaths.Where(p => p.IsBody))
        {
            var c = Centre(pond);
            var water = WaterTracer.Trace(course, TerrainReplay.Build(course, c - new Vec2(80, 80), c + new Vec2(80, 80), o));
            int body = water.BodyAt(c);
            Assert.True(body >= 0, $"pond at {c} is dry");
            Assert.Equal(pond.Level!.Value, water.Bodies[body].Level, 3);
        }

        // The creek from the clubhouse lake towards hole 2, half way along.
        var creek = course.WaterPaths.Where(p => !p.IsBody).MaxBy(p => p.Path.Points.Count)!;
        var mid = Geometry.SampleSpline(creek.Path, 8) is var pts ? pts[pts.Count / 2] : default;
        var map = WaterTracer.Trace(course, TerrainReplay.Build(course, mid - new Vec2(20, 20), mid + new Vec2(20, 20), o));
        Assert.True(map.IsWet(mid));
    }

    [PrivateSampleFact("sangoma.course")]
    public void Sangoma_NearlyEmptyRivers_AreLeftOut()
    {
        // Three running-water paths with height 0.01–0.08: no river shows beside the hole 14 pond in game, and a
        // distance marker stands on dry ground 0.4 m from one's centre line.
        var course = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, "sangoma.course")).ReadModel();
        Assert.Empty(course.WaterPaths);
    }
}
