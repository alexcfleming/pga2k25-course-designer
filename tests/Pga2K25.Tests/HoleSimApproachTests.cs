using System.Text.Json;
using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 4b: the shot model, the golfer fitted to the anchors, the short game and approach runs.</summary>
[Trait("Area", "HoleSim")]
public class HoleSimApproachTests
{
    const string Pilolcura = "pilolcura-analysis-2026-11-02.course";
    static readonly Lazy<TableBaseline> Anchors = new(() => TableBaseline.LoadShipped("pga-tour-anchors"));
    static readonly Lazy<AssumptionSheet> Sheet = new(() => AssumptionSheet.Load());
    static readonly Lazy<PuttingModel> Putter = new(() => PuttingModel.Create(10.3, Anchors.Value, Sheet.Value.Putting()));
    static GroundTable Ground => Sheet.Value.GroundRules(Putter.Value.Friction);
    static readonly Lazy<GolferFit> Fit = new(() =>
        GolferCalibration.Fit(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), Sheet.Value.GolferStart()));

    static ShotContext Calibration(ITerrainModel? terrain = null) =>
        GolferCalibration.Synthetic(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), Fit.Value.Golfer, terrain);

    /// <summary>A plane tilted along the hole: rise in feet per yard toward the green.</summary>
    sealed class Tilt(double riseFtPerYd) : ITerrainModel
    {
        public string Id => "tilt";
        public string Name => "Tilt";
        public double HeightFt(FramePoint p) => riseFtPerYd * p.Along;
        public FramePoint Slope(FramePoint p) => new(0, riseFtPerYd / 3);
    }

    [Fact]
    public void Release_is_planned_from_the_shot_length()
    {
        var r = new ShotRules();
        Assert.Equal(7.5, r.Release(150), 6);                  // 5% of a full shot
        Assert.Equal(5, r.Release(20), 6);                      // a 20-yd chip rolls a quarter of its length
        Assert.Equal(14, r.Release(400), 6);                    // never more than 14
        Assert.Equal(1, r.Release(1.5), 6);                     // never less than 1
    }

    [Fact]
    public void Spreads_grow_with_distance_lie_and_skill()
    {
        var g = new GolferParams();
        var (d100, s100) = g.Spread(100, 1);
        var (d250, _) = g.Spread(250, 1);
        Assert.True(d250 / 250 > d100 / 100, "long shots spread more than their share");
        Assert.True(g.Spread(100, g.Mult(HoleSim.Ground.Rough, false)).Dist > d100);
        Assert.True((g with { Skill = 1.5 }).Spread(100, 1).Side > s100);
        Assert.True(g.Spread(5, 1).Dist >= g.FloorYds * 0.99, "even a short chip has the floor spread");
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void The_golfer_is_fitted_to_the_anchors_off_the_green()
    {
        var fit = Fit.Value;
        Assert.Equal(21, fit.Rows.Count);                                      // every published row off the green, nothing between them
        Assert.DoesNotContain(fit.Rows, r => r.Lie == Lie.Green);
        Assert.True(fit.RmsError < 0.16, $"RMS {fit.RmsError:0.000}");
        // Approach range: within 0.16 of every anchor from 100 to 240 yds off the fairway, rough and tee.
        foreach (var r in fit.Rows.Where(r => r.Lie is Lie.Fairway or Lie.Rough or Lie.Tee && r.Yards is >= 100 and <= 240))
            Assert.True(Math.Abs(r.Error) < 0.16, $"{r.Lie} {r.Yards}: {r.Model:0.00} vs {r.Anchor:0.00}");
        // The fit stays plausible.
        var g = fit.Golfer;
        Assert.InRange(g.DistPct, 0.02, 0.1);
        Assert.InRange(g.RoughMult, 1, 3.1);
        Assert.InRange(g.RecoveryMult, 1.4, 4.2);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void A_fit_is_cached_on_disk_by_everything_it_depends_on()
    {
        var dir = Path.Combine(Path.GetTempPath(), "holesim-fit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var start = Sheet.Value.GolferStart() with { FloorYds = 1.6 };      // a key of its own, so this test fits afresh
            var fit = GolferCalibration.Fit(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), start, dir, samples: 60);
            var file = Path.Combine(dir, $"golfer-fit-{fit.Key}.json");
            Assert.True(File.Exists(file));
            var back = JsonSerializer.Deserialize<GolferFit>(File.ReadAllText(file))!;
            Assert.Equal(fit.Golfer, back.Golfer);
            Assert.Equal(fit.Rows.Count, back.Rows.Count);
            // Change anything the fit depends on and the key changes.
            var other = GolferCalibration.KeyFor(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules() with { ReleasePct = 0.06 }, start, 60);
            Assert.NotEqual(fit.Key, other);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Approach_runs_are_repeatable_and_lies_cost_strokes()
    {
        var c = Calibration();
        var pin = c.Hole.Pins[0].Frame;
        var fw = new FramePoint(0, pin.Along - 150);
        var rough = new FramePoint(30, pin.Along - 147);
        var a = ApproachSim.Run(c, fw, pin, 1, 400, seed: 5);
        var b = ApproachSim.Run(c, fw, pin, 1, 400, seed: 5);
        Assert.Equal(a.Expected, b.Expected);
        Assert.Equal(HoleSim.Ground.Fairway, a.Lie);
        var fromRough = ApproachSim.Run(c, rough, pin, 1, 400, seed: 5);
        Assert.Equal(HoleSim.Ground.Rough, fromRough.Lie);
        Assert.True(fromRough.Expected > a.Expected + 0.05, $"rough {fromRough.Expected:0.000} vs fairway {a.Expected:0.000}");
        Assert.True(fromRough.GreenHit < a.GreenHit);
        Assert.NotNull(a.StrokesGained);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void On_a_flat_round_green_the_best_aim_is_at_the_pin()
    {
        var c = Calibration();
        var pin = c.Hole.Pins[0].Frame;
        var aims = ApproachSim.Sweep(c, new FramePoint(0, pin.Along - 140), 1, n: 300);
        Assert.Equal(25, aims.Count);
        Assert.True(aims[0].Offset.Length <= 3.01, $"best aim {aims[0].Offset}");
        var atPin = aims.First(o => o.Offset.Length < 1e-9);
        Assert.True(atPin.Expected - aims[0].Expected < 0.03);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void A_green_sloping_toward_the_golfer_holds_and_one_falling_away_runs_on()
    {
        double Run(double rise)
        {
            var c = Calibration(new Tilt(rise));
            var pin = c.Hole.Pins[0].Frame;
            var from = new FramePoint(0, pin.Along - 120);
            var r = new Random(4); double roll = 0; int n = 0;
            for (int i = 0; i < 300; i++)
            {
                var s = ShotEngine.Shoot(c, from, pin, 1, r, value: false);
                if (c.Green.OnGreen(s.Landing)) { roll += s.Finish.Along - s.Landing.Along; n++; }
            }
            return roll / n;
        }
        double into = Run(0.09), away = Run(-0.09), flat = Run(0);   // 3% up or down the line of play
        Assert.True(into < flat && flat < away, $"roll into {into:0.0}, flat {flat:0.0}, away {away:0.0}");
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Spots_on_the_route_are_measured_back_from_the_pin()
    {
        var c = Calibration();
        var spot = ApproachSetup.SpotOnRoute(c.Hole, 1, 153, 5);
        Assert.Equal(153, new FramePoint(0, spot.Along).DistanceTo(c.Hole.Pins[0].Frame), 0);
        Assert.Equal(5, spot.Lat, 1);
    }

    [Trait("Speed", "Slow")]
    [PrivateSampleFact(Pilolcura)]
    public void Pilolcura_hole_8_approach_from_153_yds()
    {
        var m = Samples.PilolcuraHole(8);
        var c = Samples.PilolcuraShots(8);
        var from = ApproachSetup.SpotOnRoute(m, 1, 153);
        var r = ApproachSim.Run(c, from, m.Pins[0].Frame, 1, 400);
        Assert.Equal(HoleSim.Ground.Fairway, r.Lie);
        Assert.InRange(r.GreenHit, 0.5, 0.95);
        Assert.InRange(r.Expected, 2.85, 3.45);
    }
}
