using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 4d: the par 5 second shot, go or lay up.</summary>
[Trait("Area", "HoleSim")]
public class HoleSimSecondShotTests
{
    const string Pilolcura = "pilolcura-analysis-2026-11-02.course";
    static readonly Lazy<TableBaseline> Anchors = new(() => TableBaseline.LoadShipped("pga-tour-anchors"));
    static readonly Lazy<AssumptionSheet> Sheet = new(() => AssumptionSheet.Load());
    static readonly Lazy<PuttingModel> Putter = new(() => PuttingModel.Create(10.3, Anchors.Value, Sheet.Value.Putting()));
    static GroundTable Ground => Sheet.Value.GroundRules(Putter.Value.Friction);
    static readonly Lazy<GolferFit> Fit = new(() =>
        GolferCalibration.Fit(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), Sheet.Value.GolferStart()));

    /// <summary>The calibration hole (pin at 600), optionally with water round the sides and back of the green.</summary>
    static ShotContext Hole(bool water, double skill)
    {
        var golfer = Fit.Value.Golfer with { Skill = skill };
        return GolferCalibration.Synthetic(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), golfer,
            extra: water ? W => [new PaintPolygon(HoleSim.Ground.Water, [W(-40, 595), W(40, 595), W(40, 650), W(-40, 650)])] : null);
    }

    static SecondShotPlan Plan(ShotContext c, double yards)
    {
        var heat = ApproachMaps.Heat(c, 1, ApproachMaps.Spots(c.Hole, 1, c.Golfer.MaxCarry, spacing: 10), spacing: 10, samples: 100);
        return SecondShot.Plan(c, heat, new FramePoint(0, 600 - yards), 1, 265, samples: 150);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void With_nothing_round_the_green_it_goes_for_it()
    {
        var plan = Plan(Hole(water: false, skill: 1), 230);
        Assert.True(plan.Go.InReach);
        Assert.True(plan.ShouldGo, $"go {plan.Go.Expected:0.00} vs lay-up {plan.BestLayup!.Expected:0.00}");
        Assert.Equal(0, plan.Go.HazardShare, 6);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Water_round_the_green_makes_it_lay_up()
    {
        var plan = Plan(Hole(water: true, skill: 1.5), 230);
        Assert.True(plan.Go.HazardShare > 0.2, $"go hazard {plan.Go.HazardShare:P0}");
        Assert.False(plan.ShouldGo, $"go {plan.Go.Expected:0.00} vs lay-up {plan.BestLayup!.Expected:0.00} to {plan.BestLayup.TargetYards:0}");
        Assert.True(plan.BestLayup!.HazardShare < plan.Go.HazardShare);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Lay_ups_are_in_reach_and_short_of_the_pin()
    {
        var c = Hole(water: false, skill: 1);
        var plan = Plan(c, 250);
        var pin = c.Hole.Pins[0].Frame;
        Assert.NotEmpty(plan.Layups);
        Assert.All(plan.Layups, l =>
        {
            Assert.True(l.Target.DistanceTo(plan.From) <= 265 + 1e-9);
            Assert.True(l.Target.Along < pin.Along, $"target at {l.Target.Along:0} is past the pin");
            Assert.InRange(l.TargetYards, 40, plan.Yards - 10);
        });
        Assert.True(plan.BestLayup!.Rechecked);
        // By distance: one best per 10-yd band, in order.
        var bands = plan.ByDistance();
        Assert.Equal(bands.OrderBy(b => b.TargetYards).Select(b => b.TargetYards), bands.Select(b => b.TargetYards));
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Out_of_reach_the_go_option_is_as_close_as_it_goes()
    {
        var plan = Plan(Hole(water: false, skill: 1), 300);
        Assert.False(plan.Go.InReach);
        Assert.True(plan.Go.GreenHit < 0.05, $"green {plan.Go.GreenHit:P0} from 300 with a 265 club");
    }

    [Trait("Speed", "Slow")]
    [PrivateSampleFact(Pilolcura)]
    public void Pilolcura_hole_10_power_drive_goes_for_it()
    {
        var m = Samples.PilolcuraHole(10);
        Assert.Equal(5, m.Par);
        var c = Samples.PilolcuraShots(10);
        var from = ApproachSetup.SpotOnRoute(m, 4, 200);
        var heat = ApproachMaps.Heat(c, 4, ApproachMaps.Spots(m, 4, c.Golfer.MaxCarry));
        var plan = SecondShot.Plan(c, heat, from, 4, Sheet.Value.Reach("power"));
        Assert.True(plan.ShouldGo);
        Assert.All(plan.Layups, l => Assert.True(l.Target.Along < m.Pins[3].Frame.Along + 5));
    }
}
