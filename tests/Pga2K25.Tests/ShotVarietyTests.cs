using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

/// <summary>
/// The shot variety card (3.3): each hole played by a Power Drive golfer from the back tees and a Normal golfer one
/// tee forward, and the checks on the clubs they take into the greens.
/// </summary>
[Trait("Area", "Products")]
public class ShotVarietyTests
{
    const double Y = 1.0936133;
    static readonly Func<Vec2, double> Flat = _ => 0;

    /// <summary>A hole up the page (game north, −Z): tees at the given yards behind the first, then aim points and green.</summary>
    static Hole Straight(int par, double yards, double[]? teesBack = null, params (double Across, double Along)[] aims)
    {
        var tees = (teesBack ?? [0]).Select(b => new Vec2(0, b / Y)).OrderBy(t => t.Z).ToList();   // shortest first, back last
        var route = new List<Vec2> { tees[^1] };
        route.AddRange(aims.Select(a => new Vec2(-a.Across / Y, -a.Along / Y)));
        route.Add(new Vec2(-(aims.Length > 0 ? aims[^1].Across : 0) / Y, -yards / Y));
        return new Hole(1, par, route, tees, []);
    }

    [Fact]
    public void Par3_IsOneTeeShot_PlayedLikeForHeight()
    {
        var hole = Straight(3, 160);
        var flat = ShotVariety.Play(hole, VarietyGolfer.Power, 0, Flat);
        Assert.Equal((160, "PW", "PW"), (flat.Approach, flat.Tee, flat.Into));   // PW goes 158, within 4 yds
        // A green 30 ft above the tee plays 10 yds longer.
        var uphill = ShotVariety.Play(hole, VarietyGolfer.Power, 0, p => p.Z < -100 ? 30 : 0);
        Assert.Equal(170, uphill.Approach);
        Assert.Equal("9i", uphill.Into);
    }

    [Fact]
    public void Par4_DriverThenWhatsLeft_ThePowerPlayerHasAPartWedge()
    {
        var hole = Straight(4, 420, null, (0, 280));
        var power = ShotVariety.Play(hole, VarietyGolfer.Power, 0, Flat);
        var normal = ShotVariety.Play(hole, VarietyGolfer.Normal, 0, Flat);
        Assert.Equal(("Dr", 75, VarietyGolfer.PartWedge), (power.Tee, power.Approach, power.Into));   // 420 − 345, under a full lob wedge
        Assert.Equal(("Dr", 130, "PW"), (normal.Tee, normal.Approach, normal.Into));
        Assert.False(power.ClubbedDown);
    }

    [Fact]
    public void Par4_ADoglegCorner_ClubsDownToStopAtIt()
    {
        // Aim point 250 yds out, then a 60° turn: driver (345) would run 95 yds through the corner.
        var hole = Straight(4, 430, null, (0, 250));
        hole = hole with { Route = [hole.Route[0], hole.Route[1], new Vec2(-150 / Y, -(250 + 90) / Y)] };
        var power = ShotVariety.Play(hole, VarietyGolfer.Power, 0, Flat);
        Assert.True(power.ClubbedDown);
        Assert.Equal("5W", power.Tee);   // the longest club that stops within 20 yds of the corner (270 ≤ 270)
    }

    [Fact]
    public void Par5_GoesForItWithinReach_OtherwiseLaysUpToAFullSandWedge()
    {
        var hole = Straight(5, 600, null, (0, 300));
        var power = ShotVariety.Play(hole, VarietyGolfer.Power, 0, Flat);    // 600 − 345 = 255, within 265
        Assert.True(power.ReachedInTwo);
        Assert.Null(power.Layup);
        Assert.Equal((255, "3H"), (power.Approach, power.Into));
        var normal = ShotVariety.Play(hole, VarietyGolfer.Normal, 0, Flat);  // 310 left: lay up, a full sand wedge in
        Assert.False(normal.ReachedInTwo);
        Assert.Equal((100, "SW"), (normal.Approach, normal.Into));
        Assert.Equal("3H", normal.Layup);                                     // 210 to leave 100
    }

    [Fact]
    public void TheNormalGolfer_PlaysOneTeeForward()
    {
        // Two tee sets: one forward at 400, the back 30 yds behind it at 430.
        var hole = Straight(4, 400, [0, 30], (0, 260));
        Assert.Equal(1, VarietyGolfer.Normal.TeesUp);
        var fromForward = ShotVariety.Play(hole, VarietyGolfer.Normal, 0, Flat);
        var fromBack = ShotVariety.Play(hole, VarietyGolfer.Normal, 1, Flat);
        Assert.Equal(400, fromForward.Yards);
        Assert.Equal(430, fromBack.Yards);
        Assert.Equal(fromBack.Approach - 30, fromForward.Approach);
    }

    [Fact]
    public void Checks_CountClubsClustersRunsFamiliesAndPar3s()
    {
        HoleShot S(int hole, int par, int yards, string into, string tee = "Dr") => new(hole, par, 0, tee, null, yards, into);
        var holes = new List<HoleShot>
        {
            S(1, 4, 120, "GW"), S(2, 3, 150, "PW"), S(3, 4, 121, "GW"), S(4, 4, 122, "GW"), S(5, 3, 155, "PW"),
            S(6, 4, 123, "GW"), S(7, 5, 100, "SW", "3W"), S(8, 4, 124, "GW"), S(9, 4, 200, "7i"),
        };
        var c = ShotVariety.Checks(holes).ToDictionary(x => x.Name);
        Assert.Equal(("4 different", false), (c["Clubs into greens"].Figure, c["Clubs into greens"].Passes));
        Assert.Equal(("GW on 5 holes", false), (c["Most-used club"].Figure, c["Most-used club"].Passes));
        Assert.Equal(("5 within 120–135 yds", false), (c["Tightest cluster"].Figure, c["Tightest cluster"].Passes));
        Assert.Equal(("2 holes running", true), (c["Back to back"].Figure, c["Back to back"].Passes));   // holes 3 and 4
        Assert.Equal(("no woods, long irons, short irons", false), (c["Club families"].Figure, c["Club families"].Passes));
        Assert.Equal(("1 clubs, 5 yds apart", false), (c["Par 3s"].Figure, c["Par 3s"].Passes));
        Assert.Equal(("not driver on 1 of 7", false), (c["Off the tee"].Figure, c["Off the tee"].Passes));
        Assert.Null(c["Par 5s in two"].Passes);
    }

    [Fact]
    public void Cassimbo_EveryHoleIsPlayedByBothGolfers()
    {
        var course = Samples.Cassimbo;
        string[] tees = ["Red", "White", "Blue", "Black"];
        var rounds = ShotVariety.For(course, tees);
        Assert.Equal(2, rounds.Count);
        Assert.Equal(("Black", "Blue"), (rounds[0].TeeName, rounds[1].TeeName));   // back, and one forward
        foreach (var round in rounds)
        {
            Assert.Equal(18, round.Holes.Count);
            Assert.Equal(course.Holes.Select(h => h.Par), round.Holes.Select(h => h.Par));
            Assert.Equal(8, round.Checks.Count);
            Assert.All(round.Holes, h => Assert.InRange(h.Approach, 0, 330));
            // A lay-up leaves a full sand wedge, or more where the longest fairway club can't get that close (Cassimbo 7, 629 yds).
            Assert.All(round.Holes.Where(h => h.Layup is not null), h => Assert.True(h.Approach >= round.Golfer.Bag["SW"] - 1, $"hole {h.Hole}"));
        }
        // From the back tees the Power golfer's scorecard yards are the course file's.
        Assert.Equal(course.Holes.Select(h => (int)Math.Round(h.TeeSetLength(h.Tees.Count - 1) * Y)), rounds[0].Holes.Select(h => h.Yards));
        // A longer hitter is left shorter shots in on every par 4.
        var p4 = rounds[0].Holes.Zip(rounds[1].Holes).Where(x => x.First.Par == 4 && !x.First.ClubbedDown && !x.Second.ClubbedDown);
        Assert.All(p4, x => Assert.True(x.First.Approach < x.Second.Approach, $"hole {x.First.Hole}"));
    }
}
