using Avalonia;
using Avalonia.Media;
using Pga2K25.CourseFormat;
using Pga2K25.Studio.Views;
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

    const double Yd = RoutingArrow.MetresPerYard;

    [Fact]
    public void Bends_SitAtTheParsBendPoints_AndKeepTheYardsAlongTheLegs()
    {
        Assert.Equal([275], RoutingPar.For(4).BendYards);
        Assert.Equal([218, 436], RoutingPar.For(5).BendYards);
        Assert.Empty(RoutingPar.For(3).BendYards);

        var a = new RoutingArrow { Par = 5, X = 10, Z = 20, Bearing = 30 };
        a.SetTurn(0, 25);
        a.SetTurn(1, -40);
        var pts = a.Points;
        Assert.Equal(4, pts.Count);
        double along = 0;
        for (int k = 1; k < pts.Count; k++) along += Vec2.Distance(pts[k - 1], pts[k]);
        Assert.Equal(545 * Yd, along, 6);
        Assert.True(Vec2.Distance(a.Tee, a.Green) < 545 * Yd);
        Assert.Equal(218 * Yd, Vec2.Distance(a.Tee, pts[1]), 6);
        Assert.Equal(pts[2], a.PointAlong(436));
        Assert.Equal(55, a.LegBearing(1));
        Assert.Equal(15, a.LegBearing(2));
    }

    [Fact]
    public void Turn_RightIsClockwise_OnTheGameCompass()
    {
        // North off the tee (−Z), then right 80° turns toward east (−X).
        var a = new RoutingArrow { Par = 4, Bearing = 0 };
        a.SetTurn(0, 80);
        var leg = a.Green - a.Points[1];
        Assert.True(leg.X < 0 && Math.Abs(leg.Z) < Math.Abs(leg.X));
        Assert.Equal("R 80°", a.ShapeShort);
        Assert.Equal("Dogleg right 80° at 275 yd.", a.ShapeLong);
    }

    [Theory]
    [InlineData(23, 25)]
    [InlineData(-2, 0)]
    [InlineData(92, 80)]
    [InlineData(-130, -80)]
    [InlineData(350, -10)]
    public void Turns_SnapToFiveDegrees_WithinEightyEitherWay(double asked, double kept) =>
        Assert.Equal(kept, RoutingArrow.SnapTurn(asked));

    [Fact]
    public void Par3s_NeverBend_AndShapesRead()
    {
        var p3 = new RoutingArrow { Par = 3 };
        p3.SetTurn(0, 30);
        Assert.True(p3.IsStraight);
        Assert.Equal(185 * Yd, Vec2.Distance(p3.Tee, p3.Green), 6);

        var p5 = new RoutingArrow { Par = 5 };
        p5.SetTurn(0, 20);
        p5.SetTurn(1, -15);
        Assert.Equal("R 20° · L 15°", p5.ShapeShort);
        Assert.Equal("Double dogleg: right 20° at 218 yd, then left 15° at 436 yd.", p5.ShapeLong);
        p5.Straighten();
        Assert.Equal("Straight", p5.ShapeShort);
    }

    [Fact]
    public void Bends_AreSaved_AndA17FileLoadsStraight()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"studio-routing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(RoutingTest.PathFor(dir), """{ "arrows": [ { "par": 4, "x": 1, "z": 2, "bearing": 90 } ], "updated": "2026-10-24T10:00:00" }""");
        var old = RoutingTest.Load(dir);
        Assert.True(old.Single().IsStraight);

        old[0].SetTurn(0, -35);
        RoutingTest.Save(dir, old);
        Assert.Equal(-35, RoutingTest.Load(dir).Single().Turn(0));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Zones_FollowTheLineOfPlay_WithTheirRise()
    {
        var a = new RoutingArrow { Par = 5, Bearing = 0 };
        a.SetTurn(0, 30);
        a.SetTurn(1, 30);
        // Ground rising 1 ft per metre northward (−Z).
        var zones = RoutingTally.Of([a], w => -w.Z).Holes[0].Zones;
        Assert.Equal(["Drive zone", "Layup zone"], zones.Select(z => z.Zone.Name));
        Assert.Equal(a.PointAlong(275), zones[0].Centre);
        Assert.Equal(a.PointAlong(445), zones[1].Centre);
        Assert.Equal(-zones[1].Centre.Z, zones[1].RiseFeet!.Value, 6);
        Assert.Empty(RoutingTally.Of([new RoutingArrow { Par = 3 }]).Holes[0].Zones);
        Assert.Single(RoutingTally.Of([new RoutingArrow { Par = 4 }]).Holes[0].Zones);
    }

    /// <summary>A straight par 4 heading north from <paramref name="eastYards"/> east of the origin.</summary>
    static RoutingArrow North(double eastYards, double northYards = 0) =>
        new() { Par = 4, X = -eastYards * Yd, Z = -northYards * Yd, Bearing = 0 };

    [Theory]
    [InlineData(120, null)]
    [InlineData(85, false)]
    [InlineData(55, true)]
    public void Corridors_FlagHolesWithinTheWidthPlusClearance(double apartYards, bool? overlap)
    {
        // Holes 1 and 3 run side by side; hole 2 is far off, so they aren't back to back.
        var conflicts = RoutingCorridors.Conflicts([North(0), North(5000), North(apartYards)]);
        if (overlap is null) { Assert.Empty(conflicts); return; }
        var c = Assert.Single(conflicts);
        Assert.Equal((1, 3), (c.HoleA, c.HoleB));
        Assert.Equal(apartYards, c.Yards, 0);
        Assert.Equal(overlap, c.Overlap);
    }

    [Fact]
    public void Corridors_LetAGreenSitNearTheNextTee()
    {
        // The next tee 30 yd past the green, playing on: fine back to back, flagged when another hole comes between.
        var first = North(0);
        var next = North(0, 450);
        Assert.Empty(RoutingCorridors.Conflicts([first, next]));
        var c = Assert.Single(RoutingCorridors.Conflicts([first, North(5000), next]));
        Assert.Equal(30, c.Yards);
        Assert.True(c.Overlap);
    }

    [Fact]
    public void MapArrow_BendsClockwiseOnTheBitmap_AndAimsALegWithSnapping()
    {
        // North up the bitmap for 100 px, then right 90° for 50 px.
        var a = new MapArrow("route:0", new Point(0, 0), new Vector(0, -1), [100, 50], [90], 0, 0, Colors.Orange, "1", [], 10, 5);
        Assert.Equal(150, a.Length);
        Assert.Equal(new Point(50, -100), a.Head, PointComparer);
        Assert.Equal(new Point(70, -100), a.At(170), PointComparer);
        Assert.Equal(3, a.Span(80, 120).Count);
        Assert.Equal(2, a.Span(10, 60).Count);

        var bent = a.AimedAt(1, new Point(-50, -103));  // nearly left 90°: held to 80°
        Assert.Equal(-80, bent.Turns[0], 6);
        var aimed = a.AimedAt(0, new Point(10, 0));     // the first leg turns the whole arrow
        Assert.Equal(new Vector(1, 0), aimed.Direction);
        Assert.Equal(new Point(100, 50), aimed.Head, PointComparer);
    }

    static readonly IEqualityComparer<Point> PointComparer =
        EqualityComparer<Point>.Create((p, q) => Math.Abs(p.X - q.X) < 1e-6 && Math.Abs(p.Y - q.Y) < 1e-6);

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
