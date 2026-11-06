using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 4c: the approach heat map, the leave map, the ideal angle and par 3 tee shots.</summary>
public class HoleSimApproachMapTests
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

    /// <summary>A green falling to the right (+lat): rise in feet per yard toward the left.</summary>
    sealed class FallsRight(double ftPerYd) : ITerrainModel
    {
        public string Id => "falls-right";
        public string Name => "Falls right";
        public double HeightFt(FramePoint p) => -ftPerYd * p.Lat;
        public FramePoint Slope(FramePoint p) => new(-ftPerYd / 3, 0);
    }

    [Fact]
    public void Heat_spots_lie_in_the_corridor_and_the_distance_window()
    {
        var c = Calibration();
        var pin = c.Hole.Pins[0].Frame;
        var spots = ApproachMaps.Spots(c.Hole, 1, 200, corridor: 30);
        Assert.NotEmpty(spots);
        Assert.All(spots, p =>
        {
            Assert.InRange(p.DistanceTo(pin), 40, 200);
            Assert.True(Math.Abs(p.Lat) <= 30.01);
            Assert.Contains(c.Grid.At(p), new[] { HoleSim.Ground.Fairway, HoleSim.Ground.Rough });
        });
    }

    [Fact]
    public void On_the_plain_hole_the_fairway_beats_the_rough_at_the_same_distance()
    {
        var c = Calibration();
        var spots = ApproachMaps.Spots(c.Hole, 1, 160, spacing: 10, minYards: 120, corridor: 35);
        var map = ApproachMaps.Heat(c, 1, spots, spacing: 10, samples: 120);
        var fairway = map.Spots.Where(s => s.Lie == HoleSim.Ground.Fairway && map.Relative.ContainsKey(s)).Select(s => map.Relative[s]).ToList();
        var rough = map.Spots.Where(s => s.Lie == HoleSim.Ground.Rough && map.Relative.ContainsKey(s)).Select(s => map.Relative[s]).ToList();
        Assert.NotEmpty(fairway); Assert.NotEmpty(rough);
        Assert.True(fairway.Average() > rough.Average() + 0.05, $"fairway {fairway.Average():+0.00} vs rough {rough.Average():+0.00}");
        // Every spot is valued against the baseline from its own lie and distance.
        Assert.All(map.Spots, s => Assert.NotNull(s.Gain));
    }

    [Fact]
    public void A_flat_round_green_plays_the_same_from_either_side()
    {
        // The calibration hole has fairway in front of the green and rough beside it, so wide angles cost a little;
        // with no contours, left and right mirror each other.
        var angles = ApproachMaps.Angles(Calibration(), 1, samples: 300);
        Assert.Equal(11, angles.Count);
        foreach (var a in angles.Where(a => a.Degrees > 0))
        {
            var mirror = angles.Single(b => Math.Abs(b.Degrees + a.Degrees) < 1e-9);
            Assert.True(Math.Abs(a.Expected - mirror.Expected) < 0.04, $"{a.Degrees}°: {a.Expected:0.000} vs {mirror.Expected:0.000}");
        }
    }

    [Fact]
    public void A_green_falling_right_is_best_approached_from_the_right()
    {
        var angles = ApproachMaps.Angles(Calibration(new FallsRight(0.09)), 1, samples: 300);   // 3% across the green
        var best = angles.MinBy(a => a.Expected)!;
        Assert.True(best.Degrees > 0, $"best from {best.Degrees}°: " + string.Join(" ", angles.Select(a => $"{a.Degrees:+0;-0;0} {a.Expected:0.00}")));
        double right = angles.Where(a => a.Degrees > 0).Average(a => a.Expected), left = angles.Where(a => a.Degrees < 0).Average(a => a.Expected);
        Assert.True(right < left, $"right {right:0.000}, left {left:0.000}");
    }

    [Fact]
    public void The_leave_map_is_cheapest_on_the_green()
    {
        var c = Calibration();
        var leave = ApproachMaps.Leave(c, 1);
        var on = leave.Cells.Where(x => c.Green.OnGreen(x.Where)).Select(x => x.Expected).ToList();
        var off = leave.Cells.Where(x => !c.Green.OnGreen(x.Where)).Select(x => x.Expected).ToList();
        Assert.True(on.Count > 400);                                   // a 26-yd green, every yard
        Assert.True(on.Average() < off.Average() - 0.3, $"on {on.Average():0.00}, off {off.Average():0.00}");
        var (lo, hi) = leave.Range;
        Assert.True(hi - lo >= 0.6 - 1e-9);
    }

    [PrivateSampleFact(Pilolcura)]
    public void Pilolcura_hole_3_tee_shots_from_every_tee()
    {
        var file = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, Pilolcura));
        var course = file.ReadModel();
        var m = HoleModelBuilder.Build(course, file.Description, 3);
        Assert.Equal(3, m.Par);
        var green = GreenSurface.Build(m, ReplayTerrain.Smoothed(course, m, 8));
        var (c, _) = ApproachSetup.Build(m, green, null, Sheet.Value, Anchors.Value, AssumptionSheet.Stimp(course.GreenSpeed)!.Value);
        var shots = ApproachMaps.TeeShots(c, 1, samples: 200);
        Assert.Equal(m.Tees.Count, shots.Count);
        Assert.All(shots, s => Assert.True(s.Best.Expected <= s.AtPin.Expected + 1e-9));
        Assert.All(shots, s => Assert.InRange(s.Best.Expected, 2.6, 4.2));
        Assert.True(shots[^1].Yards > shots[0].Yards);              // back tee further than the front
        Assert.All(shots, s => Assert.NotNull(s.StrokesGained));    // 100–240 yds is inside the tee rows
    }
}
