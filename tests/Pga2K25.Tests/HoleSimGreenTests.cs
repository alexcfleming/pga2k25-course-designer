using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 4a: green roll from Stimp, the putting model fitted to the anchors, putting maps and the green gate.</summary>
[Trait("Area", "HoleSim")]
public class HoleSimGreenTests
{
    static string Cassimbo => Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
    const string Pilolcura = "pilolcura-analysis-2026-11-02.course";
    static readonly Lazy<TableBaseline> Anchors = new(() => TableBaseline.LoadShipped("pga-tour-anchors"));
    static readonly Lazy<PuttingModel> Model = new(() => PuttingModel.Create(10.3, Anchors.Value));

    static readonly Lazy<(CourseModel Course, HoleModel Hole, ReplayTerrain Terrain, GreenSurface Green)> Hole3 = new(() =>
    {
        var file = CourseFile.Load(Cassimbo);
        var course = file.ReadModel();
        var m = HoleModelBuilder.Build(course, file.Description, 3);
        var t = ReplayTerrain.Smoothed(course, m, 8);
        return (course, m, t, GreenSurface.Build(m, t));
    });

    sealed class Plane(double slopeLat, double slopeAlong) : PuttingModel.ISlopeField
    {
        public FramePoint Slope(FramePoint p) => new(slopeLat, slopeAlong);
        public bool Inside(FramePoint p) => p.Length < 200;
    }

    [Fact]
    public void Stimp_sets_the_rolling_friction()
    {
        Assert.Equal(0.0544, GreenPhysics.RollingFriction(10.3), 4);
        // A ball released at the Stimpmeter's speed on a flat green rolls the Stimp distance.
        double mu = GreenPhysics.RollingFriction(10.3);
        double feet = GreenPhysics.StimpReleaseSpeed * GreenPhysics.StimpReleaseSpeed / (2 * mu * GreenPhysics.Gravity) / HoleFrame.Foot;
        Assert.Equal(10.3, feet, 6);
        Assert.True(GreenPhysics.RollingFriction(13) < GreenPhysics.RollingFriction(8));    // faster greens roll further
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void The_putter_is_fitted_to_the_baselines_green_rows()
    {
        var m = Model.Value;
        Assert.Equal(2, m.Skill.Fit.Count);                                       // the two published rows, nothing interpolated
        Assert.Equal(1.04, m.ExpectedFlat(3), 1);
        Assert.InRange(m.ExpectedFlat(3), 1.01, 1.07);
        Assert.InRange(m.ExpectedFlat(10), 1.56, 1.66);
        Assert.InRange(m.Skill.AimSdDegrees, 0.5, 3.1);
        // Longer putts never get easier on a flat green.
        double last = 0;
        foreach (var ft in new[] { 2.0, 5, 10, 20, 40, 60 }) { double e = m.ExpectedFlat(ft); Assert.True(e >= last - 0.02, $"{ft} ft {e:0.00}"); last = e; }
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Slopes_change_putts_the_way_greens_do()
    {
        var m = Model.Value;
        var start = new FramePoint(0, 0);
        double E(double sl, double sa, double ft) => m.Expected(new Plane(sl, sa), start, new FramePoint(0, ft / 3), 120, 3).Expected;
        double up50 = E(0, 0.02, 50), down50 = E(0, -0.02, 50);
        Assert.True(down50 > up50, $"50 ft downhill {down50:0.00} vs uphill {up50:0.00}");
        double straight = E(0, 0.02, 15), side = E(0.04, 0, 15);
        Assert.True(side > straight, $"15 ft across a 4% slope {side:0.00} vs straight up 2% {straight:0.00}");
        // The read aims above the hole on a side slope (into the slope: the ball falls toward −Lat here).
        var read = m.Expected(new Plane(0.03, 0), start, new FramePoint(0, 10), 60, 1);
        Assert.True(read.AimOffsetM > 0.1, $"aim {read.AimOffsetM:0.00} m");
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void The_green_is_the_surface_touching_the_green_point()
    {
        var (_, m, _, green) = Hole3.Value;
        Assert.InRange(green.SquareYards, 300, 1500);
        Assert.True(green.OnGreen(m.Pins[0].Frame));
        Assert.All(green.Cells, c => Assert.Equal(Ground.Green, m.Grid.At(c)));
        Assert.Equal(0, green.HeightFt(m.GreenFrame), 1);
        var (p50, p90, steep) = green.SlopeStats(GreenPhysics.RollingFriction(10.9));
        Assert.True(p90 >= p50 && steep is >= 0 and <= 1);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void A_putting_map_is_deterministic_and_easiest_at_the_pin()
    {
        var (_, m, _, green) = Hole3.Value;
        var pin = m.Pins[0];
        var a = PuttingMap.Build(green, Model.Value, 1, pin.Frame, samples: 16);
        var b = PuttingMap.Build(green, Model.Value, 1, pin.Frame, samples: 16);
        Assert.Equal(a.Cells.Count, green.Cells.Count);
        Assert.All(a.Cells, kv => Assert.Equal(kv.Value.Expected, b.Cells[kv.Key].Expected));
        Assert.InRange(a.At(pin.Frame, Model.Value), 1.0, 1.3);
        var near = green.Cells.Where(c => c.DistanceTo(pin.Frame) < 3).Average(c => a.Cells[GreenSurface.Key(c)].Expected);
        var far = green.Cells.Where(c => c.DistanceTo(pin.Frame) > 12).Average(c => a.Cells[GreenSurface.Key(c)].Expected);
        Assert.True(far > near + 0.4, $"near {near:0.00}, far {far:0.00}");
        Assert.All(a.Cells.Values, v => Assert.InRange(v.Expected, 1.0, 4.5));
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Green_spots_sit_round_each_pin_and_hold_one_back()
    {
        var (_, m, _, green) = Hole3.Value;
        var spots = GreenGate.SuggestSpots(m, green);
        Assert.Equal(3 * m.Pins.Count, spots.Count);
        Assert.All(spots, s => Assert.True(green.OnGreen(s.Where), s.Name));
        Assert.Equal(m.Pins.Count, spots.Count(s => s.HeldOut));
        Assert.All(spots.Where(s => s.HeldOut), s => Assert.EndsWith("c", s.Name));
        Assert.Equal("P1a", spots[0].Name);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void The_green_gate_fits_how_much_the_game_smooths_the_contours()
    {
        var (_, m, terrain, green) = Hole3.Value;
        // Pretend the game halves the replay's relief: readings from a ×0.5 green, relative to G.
        var truth = new ScaledGreenTerrain(terrain, green.OnGreen, green.GreenPoint, 0.5, "t", "truth");
        var spots = GreenGate.SuggestSpots(m, green);
        var gSpot = m.GreenFrame;
        var readings = spots.Select(s => new HeightReading(s.Name, s.Where, new FramePoint(0, 0), truth.HeightFt(s.Where) - truth.HeightFt(new FramePoint(0, 0)), "", s.HeldOut))
            .Append(new HeightReading("G", gSpot, new FramePoint(0, 0), truth.HeightFt(gSpot) - truth.HeightFt(new FramePoint(0, 0)), "")).ToList();
        var options = GreenGate.Evaluate(terrain, green, readings);
        var scaled = Assert.Single(options, o => o.Model.Id == "green-scaled");
        Assert.InRange(scaled.K, 0.45, 0.55);
        var replay = options.Single(o => o.Model.Id == "green-replay");
        Assert.True(scaled.Gate.RmsInches < replay.Gate.RmsInches, $"scaled {scaled.Gate.RmsInches:0.0} in vs replay {replay.Gate.RmsInches:0.0} in");
        // Readings against G: the tee's height drops out.
        var rel = GreenGate.RelativeToGreen(readings, green);
        Assert.All(rel, r => Assert.Equal(gSpot, r.Reference));
    }

    [Fact]
    public void Green_roll_comes_from_stimp_and_putting_rules_from_the_sheet()
    {
        var sheet = AssumptionSheet.Load();
        var rules = sheet.GroundRules(0.06);
        Assert.Equal(0.06, rules.For(Ground.Green).Friction, 9);
        Assert.Equal(1.5, rules.For(Ground.Green).HopMetres, 9);
        var put = sheet.Putting();
        Assert.Equal(1.3, put.CaptureSpeed, 9);
        Assert.Equal(0.45, put.PastM, 9);
        Assert.Contains("Greens", sheet.Groups);
        Assert.All(sheet.Rows.Where(r => r.Group == "Greens"), r => Assert.Equal(SourceTag.Placeholder, r.Source));
    }

    [Trait("Speed", "Slow")]
    [PrivateSampleFact(Pilolcura)]
    public void Pilolcura_hole_12s_green_is_too_steep_where_hole_8s_is_not()
    {
        var course = Samples.PilolcuraCourse;
        double mu = GreenPhysics.RollingFriction(AssumptionSheet.Stimp(course.GreenSpeed)!.Value);
        GreenSurface Green(int hole) => Samples.PilolcuraGreen(hole);
        var g8 = Green(8); var g12 = Green(12);
        Assert.True(g8.SlopeStats(mu).TooSteep < 0.05);
        Assert.True(g12.SlopeStats(mu).TooSteep > 0.2, $"hole 12 too steep share {g12.SlopeStats(mu).TooSteep:P0}");
        var m12 = Samples.PilolcuraHole(12);
        Assert.True(g12.Slope(m12.Pins[3].Frame).Length > mu * 1.15, "hole 12 pin 4 sits where a ball can't stop");
    }
}
