using Pga2K25.CourseFormat;
using Pga2K25.Readiness;

namespace Pga2K25.Tests;

/// <summary>The routing checks from Plot Starter's rules (walks, climbs, cliffs, gorges, plot edge, doglegs), on made-up ground.</summary>
[Trait("Area", "Readiness")]
public class RoutingCheckTests
{
    const double M = 1 / 1.0936133;   // metres per yard

    static Hole Straight(int n, Vec2 tee, Vec2 green, int par = 4) =>
        new(n, par, [tee, (tee + green) * 0.5, green], [tee], [green]);

    static Dictionary<string, ReadinessCheck> Run(IReadOnlyList<Hole> holes, Func<Vec2, double>? feet = null, Func<Vec2, double>? slope = null) =>
        RoutingChecks.Run(holes, feet ?? (_ => 100), slope ?? (_ => 0), new ReadinessLimits()).ToDictionary(c => c.Id);

    [Fact]
    public void Turn_IsPositiveForALeftBend_SeenNorthUp()
    {
        // North is −Z and east −X: heading north, then turning west (+X) is a left turn on the map.
        Assert.True(RoutingChecks.Turn([new Vec2(0, 0), new Vec2(0, -200), new Vec2(100, -300)]) > 30);
        Assert.True(RoutingChecks.Turn([new Vec2(0, 0), new Vec2(0, -200), new Vec2(-100, -300)]) < -30);
    }

    [Fact]
    public void LongWalks_AndWalksThroughAGorge_AreWarned()
    {
        var holes = new[]
        {
            Straight(1, new Vec2(0, 0), new Vec2(0, -350)),
            Straight(2, new Vec2(0, -350 - 300 * M), new Vec2(0, -1000 + 250)),   // 300 yds from green 1
        };
        var c = Run(holes);
        Assert.Equal(Severity.Warning, c["walks"].Severity);
        Assert.Equal([2], c["walks"].Holes);

        // A 30 m dip halfway along that walk.
        var dip = Run(holes, p => p.Z < -400 && p.Z > -500 ? 0 : 100);
        Assert.Equal(Severity.Warning, dip["walk-climb"].Severity);
        Assert.Equal(Severity.Pass, Run(holes)["walk-climb"].Severity);
    }

    [Fact]
    public void ShotsClimbingTooMuch_CliffSites_AndGorges_AreFound()
    {
        var hole = Straight(1, new Vec2(0, 0), new Vec2(0, -360));
        // Up 80 ft from the aim point to the green.
        var c = Run([hole], p => p.Z < -300 ? 180 : 100);
        Assert.Equal(Severity.Warning, c["shot-rise"].Severity);
        Assert.Contains("aim → green +80 ft", c["shot-rise"].Detail);

        var cliff = Run([hole], slope: p => Vec2.Distance(p, hole.Route[1]) < 1 ? 50 : 5);
        Assert.Equal(Severity.Warning, cliff["steep-sites"].Severity);
        Assert.Contains("aim point 50 %", cliff["steep-sites"].Detail);

        // 100 m of ground 100 ft below the tee shot's ends: a carry, reported, not judged.
        var gorge = Run([hole], p => p.Z < -40 && p.Z > -140 ? 0 : 100);
        Assert.Equal(Severity.Info, gorge["gorges"].Severity);
        Assert.Equal([1], gorge["gorges"].Holes);
        Assert.Contains("over ground 100 ft below", gorge["gorges"].Detail);
        Assert.Empty(Run([hole])["gorges"].Holes);
    }

    [Fact]
    public void PlayNearThePlotEdge_IsWarned()
    {
        var c = Run([Straight(1, new Vec2(900, 0), new Vec2(500, 0)), Straight(2, new Vec2(0, 0), new Vec2(0, -300))]);
        Assert.Equal(Severity.Warning, c["edge-setback"].Severity);
        Assert.Equal([1], c["edge-setback"].Holes);
    }

    [Fact]
    public void Doglegs_AllOneWay_AreWarned_AndAMixPasses()
    {
        Hole Bend(int n, double dx) => new(n, 4, [new Vec2(n * 100, 0), new Vec2(n * 100, -200), new Vec2(n * 100 + dx, -300)], [new Vec2(n * 100, 0)], []);
        var allLeft = Run(Enumerable.Range(1, 7).Select(n => Bend(n, 100)).ToList());
        Assert.Equal(Severity.Warning, allLeft["doglegs"].Severity);
        Assert.Contains("7 bend left, 0 right", allLeft["doglegs"].Detail);

        var mixed = Run(Enumerable.Range(1, 8).Select(n => Bend(n, n % 2 == 0 ? 100 : -100)).ToList());
        Assert.Equal(Severity.Pass, mixed["doglegs"].Severity);
    }
}
