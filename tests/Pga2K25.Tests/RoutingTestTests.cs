using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;

namespace Pga2K25.Tests;

public class RoutingTestTests
{
    [Theory]
    [InlineData(0, 0, -1)]   // north is −Z
    [InlineData(90, -1, 0)]  // east is −X
    [InlineData(180, 0, 1)]
    [InlineData(270, 1, 0)]
    public void Bearing_FollowsTheGameCompass(double bearing, double x, double z)
    {
        var d = RoutingArrow.Heading(bearing);
        Assert.Equal(x, d.X, 9);
        Assert.Equal(z, d.Z, 9);
        Assert.Equal(bearing, RoutingArrow.BearingOf(d), 6);
    }

    [Fact]
    public void Arrow_IsItsParsPlaceholderLength()
    {
        foreach (var p in RoutingPar.All)
        {
            var a = new RoutingArrow { Par = p.Par, X = 100, Z = -50, Bearing = 37 };
            Assert.Equal(p.Yards * 0.9144, Vec2.Distance(a.Tee, a.Green), 6);
        }
        Assert.Equal((185, 420, 545), (RoutingPar.For(3).Yards, RoutingPar.For(4).Yards, RoutingPar.For(5).Yards));
    }

    [Fact]
    public void SaveThenLoad_KeepsOrder_AndSavingNoneDeletesTheFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"studio-routing-{Guid.NewGuid():N}");
        Assert.Empty(RoutingTest.Load(dir));
        RoutingTest.Save(dir, [new() { Par = 5, X = 1, Z = 2, Bearing = 270 }, new() { Par = 3, X = -4.5, Z = 8, Bearing = 12.5 }]);
        var back = RoutingTest.Load(dir);
        Assert.Equal([5, 3], back.Select(a => a.Par));
        Assert.Equal(12.5, back[1].Bearing);
        Assert.Equal(new Vec2(-4.5, 8), back[1].Tee);
        // Only what defines an arrow is saved: par, tee and bearing.
        Assert.DoesNotContain("green", File.ReadAllText(RoutingTest.PathFor(dir)));
        RoutingTest.Save(dir, []);
        Assert.False(File.Exists(RoutingTest.PathFor(dir)));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Tally_AddsParYardsNinesAndWalks()
    {
        // 4 par 3s, 10 par 4s, 4 par 5s, each tee 50 m east of the last green.
        int[] pars = [4, 4, 3, 5, 4, 4, 3, 4, 5, 4, 3, 4, 5, 4, 4, 3, 4, 5];
        var arrows = new List<RoutingArrow>();
        var at = new Vec2(0, 0);
        foreach (int p in pars)
        {
            var a = new RoutingArrow { Par = p, X = at.X, Z = at.Z, Bearing = 0 };
            arrows.Add(a);
            at = a.Green + new Vec2(-50, 0);
        }
        // Ground rising 1 ft per metre northward (−Z).
        var t = RoutingTally.Of(arrows, w => -w.Z);
        Assert.Equal(72, t.Par);
        Assert.Equal(4 * 185 + 10 * 420 + 4 * 545, t.Yards);
        Assert.Equal(pars.Take(9).Sum(), t.OutPar);
        Assert.Equal(t.Par, t.OutPar + t.InPar);
        Assert.Equal(t.Yards, t.OutYards + t.InYards);
        Assert.Equal((4, 10, 4), (t.Count(3), t.Count(4), t.Count(5)));
        Assert.All(t.Holes.SkipLast(1), h => Assert.Equal(55, h.WalkYards));  // 50 m
        Assert.Null(t.Holes[^1].WalkYards);
        Assert.Equal(17 * 55, t.WalkYards);
        Assert.Equal(185 * 0.9144, t.Holes[2].RiseFeet!.Value, 6);
        Assert.Null(RoutingTally.Of(arrows).Holes[0].RiseFeet);
    }
}
