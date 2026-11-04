using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 3: in-game height readings, terrain candidates, the gate, and slope and elevation in the run.</summary>
public class HoleSimTerrainTests
{
    static string Cassimbo => Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
    static readonly Lazy<(Pga2K25.CourseFormat.CourseModel Course, HoleModel Hole)> Hole3 = new(() =>
    {
        var file = Pga2K25.CourseFormat.CourseFile.Load(Cassimbo);
        var course = file.ReadModel();
        return (course, HoleModelBuilder.Build(course, file.Description, 3));
    });

    /// <summary>A test ground: height = a·lat + b·along (feet per yard), zero at the frame origin.</summary>
    sealed class Plane(double a, double b) : ITerrainModel
    {
        public string Id => "plane";
        public string Name => "test plane";
        public double HeightFt(FramePoint p) => a * p.Lat + b * p.Along;
        public FramePoint Slope(FramePoint p) => new(a / 3, b / 3);
    }

    [Theory]
    [InlineData("+1'4\"", 16)] [InlineData("+1'", 12)] [InlineData("1'4", 16)] [InlineData("-0'7", -7)] [InlineData("−2'0\"", -24)]
    [InlineData("-5.5", -66)] [InlineData("+0'3\"", 3)] [InlineData("1 ft 4 in", 16)] [InlineData("+1’4”", 16)]
    public void Height_labels_read_as_the_game_means_them(string label, double inches)
    {
        Assert.True(HeightLabel.TryParse(label, out var ft), label);
        Assert.Equal(inches, ft * 12, 6);
    }

    [Theory] [InlineData("1'12\"")] [InlineData("abc")] [InlineData("")] [InlineData("++1'")]
    public void Unreadable_labels_are_refused(string label) => Assert.False(HeightLabel.TryParse(label, out _));

    [Fact]
    public void Parsed_values_are_said_back_in_feet_and_inches()
    {
        Assert.Equal("+1 ft 4 in (+16 in)", HeightLabel.Describe(16 / 12.0));
        Assert.Equal("−7 in (−7 in)", HeightLabel.Describe(-7 / 12.0));
    }

    static List<HeightReading> Readings(ITerrainModel truth, int n, double noiseInches, int heldEvery = 4)
    {
        var rng = new Random(3);
        return Enumerable.Range(0, n).Select(i =>
        {
            var p = new FramePoint(rng.Next(-30, 30), 40 + i * 40);
            double ft = truth.HeightFt(p) - truth.HeightFt(new FramePoint(0, 500)) + (rng.NextDouble() * 2 - 1) * noiseInches / 12;
            return new HeightReading($"M{i + 1}", p, new FramePoint(0, 500), ft, "", HeldOut: i % heldEvery == heldEvery - 1);
        }).ToList();
    }

    [Fact]
    public void Gate_bands_follow_the_share_of_the_range()
    {
        var truth = new Plane(0, 0.02);                         // 10 ft over 500 yds
        var good = TerrainGate.Evaluate(truth, Readings(truth, 10, 2));
        Assert.Equal(GateBand.Trustworthy, good.Band);
        var tilted = TerrainGate.Evaluate(new Plane(0, 0.015), Readings(truth, 10, 2));
        Assert.Equal(GateBand.Indicative, tilted.Band);
        var wrong = TerrainGate.Evaluate(new Plane(0.1, 0), Readings(truth, 10, 2));
        Assert.Equal(GateBand.Check, wrong.Band);
        var few = TerrainGate.Evaluate(truth, Readings(truth, 4, 0));
        Assert.Equal(GateBand.Check, few.Band);
        Assert.Contains("at least 5", few.Reason);
        Assert.InRange(good.RangeInches, 100, 140);              // the zero reference counts toward the range
    }

    [Fact]
    public void A_fitted_correction_is_judged_on_held_out_readings_only()
    {
        var truth = new Plane(0, 0.02);
        var all = Readings(truth, 12, 1);
        var flat = new Plane(0, 0);
        var fitted = new CorrectedTerrain(flat, all.Where(r => !r.HeldOut), lengthYards: 60);
        var gate = TerrainGate.Evaluate(fitted, all, fitted: true);
        var plain = TerrainGate.Evaluate(flat, all);
        Assert.True(gate.RmsInches < plain.RmsInches / 2, $"fitted {gate.RmsInches:0.0} in vs flat {plain.RmsInches:0.0} in");
        Assert.Equal(all.Count(r => r.HeldOut), all.Count(r => r.HeldOut));
        Assert.Equal(gate.HeldOutRmsInches!.Value, gate.RmsInches, 9);
        // Far from every reading the correction fades away.
        Assert.InRange(fitted.Correction(new FramePoint(400, -400)), -0.01, 0.01);
    }

    [Fact]
    public void Suggested_spots_run_along_the_line_and_hold_back_every_fourth()
    {
        var (_, m) = Hole3.Value;
        var spots = TerrainGate.SuggestSpots(m);
        Assert.InRange(spots.Count, 8, 13);
        Assert.Equal(spots.Count / 4 + (spots.Count % 4 >= 3 ? 1 : 0), spots.Count(s => s.HeldOut));
        Assert.All(spots, s => Assert.True(Polyline.Nearest(m.Route, s.Where).Distance < 25));
        Assert.Equal(Ground.Green, m.Grid.At(spots[^1].Where));
        Assert.Equal(Enumerable.Range(1, spots.Count).Select(i => $"M{i}"), spots.Select(s => s.Name));
    }

    [Fact]
    public void Smoothed_replay_keeps_the_greens_and_calms_the_fairway()
    {
        var (course, m) = Hole3.Value;
        var field = ReplayTerrain.BuildField(course, m);
        var replay = ReplayTerrain.FromField(m, field);
        var smooth = ReplayTerrain.Smoothed(m, field, 8);
        Assert.Equal(0, replay.HeightFt(m.GreenFrame), 6);
        Assert.Equal(0, smooth.HeightFt(m.GreenFrame), 6);
        var pin = m.Pins[0].Frame;
        Assert.Equal(replay.HeightFt(pin), smooth.HeightFt(pin), 1);           // on the green the replay stands
        var r = TerrainStudy.FairwaySlopes(m, replay);
        var s = TerrainStudy.FairwaySlopes(m, smooth);
        Assert.True(s.P99 < r.P99, $"smoothed p99 {s.P99:0.0}% vs replay {r.P99:0.0}%");
    }

    [Fact]
    public void Slope_rolls_the_ball_downhill_and_uphill_slopes_hold_it()
    {
        var (_, m) = Hole3.Value;
        var table = AssumptionSheet.Load().GroundRules();
        var fairway = m.Grid.With((x, y, g) => Ground.Fairway);
        double Roll(ITerrainModel? t) => GeometryRun.Roll(fairway, table, new FramePoint(0, 200), new FramePoint(0, 1), 6, t).Along - 200;
        double flat = Roll(null), down = Roll(new Plane(0, -0.06)), up = Roll(new Plane(0, 0.06));
        Assert.True(down > flat + 3 && up < flat - 3, $"down {down:0.0}, flat {flat:0.0}, up {up:0.0}");
        // A side slope pulls the ball sideways.
        var side = GeometryRun.Roll(fairway, table, new FramePoint(0, 200), new FramePoint(0, 1), 6, new Plane(-0.06, 0));
        Assert.True(side.Lat > 2, $"drift {side.Lat:0.0}");
    }

    [Fact]
    public void Terrain_runs_change_carry_for_height_and_say_so()
    {
        var (_, m) = Hole3.Value;
        var sheet = AssumptionSheet.Load();
        var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
        var settings = new RunSettings { SamplesPerAim = 200, AimMin = 0, AimMax = 0 };
        var flat = GeometryRun.Run(m, sheet.Profile("power"), 1, sheet.GroundRules(), sheet.Lies(), baseline, settings);
        var downhill = GeometryRun.Run(m, sheet.Profile("power"), 1, sheet.GroundRules(), sheet.Lies(), baseline, settings,
            terrain: new TerrainUse(new Plane(0, -0.1), "terrain unverified"));
        double Carry(GeometryRunResult r) => r.Aims[0].Samples.Average(s => s.Landing.Length);
        Assert.True(Carry(downhill) > Carry(flat) + 8, $"{Carry(downhill):0} vs {Carry(flat):0}");   // ~32 ft below the tee at 320: ~11 yds more
        Assert.Contains(downhill.Notes, n => n.Contains("terrain unverified"));
        Assert.Null(flat.Terrain);
        Assert.Contains(flat.Notes, n => n.StartsWith("Geometry only"));
    }

    [Fact]
    public void Live_checks_save_and_reload_in_world_coordinates()
    {
        var (_, m) = Hole3.Value;
        var dir = Path.Combine(Path.GetTempPath(), $"holesim-live-{Guid.NewGuid():N}");
        try
        {
            var path = LiveChecks.PathFor(dir, 3);
            var spot = TerrainGate.SuggestSpots(m)[1];
            var w = m.Frame.ToWorld(spot.Where); var z = m.Green;
            var live = new LiveChecks { Hole = 3, Sightline = "Most", SightlineTee = "T4" };
            live.Readings.Add(new LiveChecks.SavedReading(spot.Name, w.X, w.Z, z.X, z.Z, -4.5, "-4'6\"", false, "green centre", 0, DateTime.Now));
            live.Drives.Add(new LiveChecks.Drive(318, 341, "L", DateTime.Now));
            live.Save(path);
            Assert.EndsWith(Path.Combine("Hole Sim", "H03 live checks.json"), path);

            var back = LiveChecks.Load(path, 3);
            Assert.Equal("Most", back.Sightline);
            Assert.Equal(318, back.Drives.Single().Carry);
            var r = back.ToReadings(m).Single();
            Assert.Equal(spot.Where.Lat, r.Where.Lat, 6);
            Assert.Equal(m.GreenFrame.Along, r.Reference.Along, 6);
            Assert.Equal(-54, r.Inches, 6);

            // From another tee the same reading lands on the same ground.
            var front = HoleModelBuilder.Build(Cassimbo, 3, new HoleModelOptions { TeeIndex = 0 });
            var moved = back.ToReadings(front).Single();
            Assert.Equal(0, Pga2K25.CourseFormat.Vec2.Distance(front.Frame.ToWorld(moved.Where), w), 6);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
