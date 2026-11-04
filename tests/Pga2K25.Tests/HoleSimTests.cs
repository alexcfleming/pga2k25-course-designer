using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Strokes-gained method: baseline loading, interpolation, coverage and the acceptance tests from the method prompt.</summary>
public class StrokesGainedTests
{
    static readonly TableBaseline Anchors = TableBaseline.LoadShipped("pga-tour-anchors");

    [Theory]
    [InlineData(Lie.Tee, 100, 2.92)] [InlineData(Lie.Tee, 240, 3.25)] [InlineData(Lie.Tee, 400, 3.99)] [InlineData(Lie.Tee, 440, 4.08)] [InlineData(Lie.Tee, 540, 4.65)]
    [InlineData(Lie.Fairway, 20, 2.40)] [InlineData(Lie.Fairway, 100, 2.80)] [InlineData(Lie.Fairway, 240, 3.45)] [InlineData(Lie.Fairway, 540, 4.78)]
    [InlineData(Lie.Rough, 20, 2.59)] [InlineData(Lie.Rough, 100, 3.02)] [InlineData(Lie.Rough, 200, 3.42)] [InlineData(Lie.Rough, 240, 3.64)] [InlineData(Lie.Rough, 540, 4.97)]
    [InlineData(Lie.Sand, 20, 2.53)] [InlineData(Lie.Sand, 100, 3.23)] [InlineData(Lie.Sand, 240, 3.84)] [InlineData(Lie.Sand, 540, 5.36)]
    [InlineData(Lie.Recovery, 100, 3.80)] [InlineData(Lie.Recovery, 240, 3.97)] [InlineData(Lie.Recovery, 540, 5.42)]
    [InlineData(Lie.Green, 3, 1.04)] [InlineData(Lie.Green, 10, 1.61)]
    public void Anchors_return_their_table_values(Lie lie, double distance, double expected)
    {
        var e = Anchors.ExpectedStrokes(lie, distance);
        Assert.Equal(expected, e.Value, 9);
        Assert.Equal(Coverage.Covered, e.Coverage);
    }

    [Fact]
    public void Shipped_anchor_baseline_is_labelled_secondary_and_not_estimated()
    {
        Assert.Equal("pga-tour-anchors", Anchors.Id);
        Assert.True(Anchors.Secondary);
        Assert.False(Anchors.Estimated);
        Assert.Empty(Anchors.Anomalies);
        Assert.Contains("pga-tour-anchors", TableBaseline.Shipped());
    }

    [Fact]
    public void Test_C_interpolation_is_linear_between_points_and_flags_out_of_range()
    {
        var mid = Anchors.ExpectedStrokes(Lie.Fairway, 60);
        Assert.Equal(2.60, mid.Value, 9);                       // halfway between 2.40 and 2.80
        Assert.True(mid.Covered);
        var q = Anchors.ExpectedStrokes(Lie.Rough, 220);
        Assert.InRange(q.Value, 3.42, 3.64);

        var shortRow = Anchors.ExpectedStrokes(Lie.Fairway, 10);
        Assert.Equal(Coverage.BelowTable, shortRow.Coverage);
        Assert.Equal(2.40, shortRow.Value, 9);                  // nearest row, not an extrapolation
        Assert.Equal(Coverage.AboveTable, Anchors.ExpectedStrokes(Lie.Green, 20).Coverage);
        Assert.Equal(Coverage.BelowTable, Anchors.ExpectedStrokes(Lie.Recovery, 50).Coverage);
    }

    [Fact]
    public void Test_A_four_shot_birdie_on_540_yards()
    {
        double E(Lie l, double d) => Anchors.ExpectedStrokes(l, d).Value;
        var shots = new[]
        {
            StrokesGained.Shot(E(Lie.Tee, 540), E(Lie.Fairway, 240)),
            StrokesGained.Shot(E(Lie.Fairway, 240), E(Lie.Fairway, 20)),
            StrokesGained.Shot(E(Lie.Fairway, 20), E(Lie.Green, 10)),
            StrokesGained.Shot(E(Lie.Green, 10), 0, holed: true),
        };
        Assert.Equal(0.20, shots[0], 9);
        Assert.Equal(0.05, shots[1], 9);
        Assert.Equal(-0.21, shots[2], 9);
        Assert.Equal(0.61, shots[3], 9);
        Assert.Equal(0.65, shots.Sum(), 9);
        Assert.True(StrokesGained.InvariantHolds(shots, E(Lie.Tee, 540), 4));
    }

    [Fact]
    public void Test_B_out_of_bounds_off_the_tee_costs_two()
    {
        double tee = Anchors.ExpectedStrokes(Lie.Tee, 400).Value;
        Assert.Equal(-2.00, StrokesGained.Shot(tee, tee, penaltyStrokes: 1), 9);
    }

    [Fact]
    public void Baseline_csv_rejects_mixed_baselines_and_wrong_units_and_reports_reversals()
    {
        const string head = "# baseline_id: t\nbaseline_id,lie,distance,unit,expected,source,source_type,retrieved\n";
        Assert.Throws<FormatException>(() => TableBaseline.Parse(head + "t,green,3,yd,1.04,x,primary,2026\n"));
        Assert.Throws<FormatException>(() => TableBaseline.Parse(head + "other,fairway,20,yd,2.4,x,primary,2026\n"));
        var b = TableBaseline.Parse(head + "t,fairway,20,yd,2.40,x,primary,2026\nt,fairway,40,yd,2.38,x,primary,2026\nt,fairway,60,yd,2.60,x,primary,2026\n");
        Assert.Single(b.Anomalies);
        Assert.False(b.Secondary);
    }
}

public class HoleSimTests
{
    static string Cassimbo => Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
    const string Pilolcura = "pilolcura-analysis-2026-11-02.course";

    [Fact]
    public void Cassimbo_hole_3_reads_back_the_regression_fixture()
    {
        var m = HoleModelBuilder.Build(Cassimbo, 3);
        Assert.Equal(5, m.Par);
        Assert.Equal(new[] { 499, 512, 531, 537 }, m.Tees.Select(t => t.Yards));
        Assert.Equal(4, m.Pins.Count);
        Assert.Equal(3, m.TeeIndex);                               // back tee by default
        Assert.InRange(m.AimFromTeeYards, 266, 268);               // 267 in the proof of concept
        Assert.Equal(Ground.Green, m.Grid.At(m.Pins[0].Frame));
        Assert.Contains(m.Hazards, h => h.Ground == Ground.Bunker);
        Assert.Contains(ReadBack.Rows(m), r => r.Id == "elev" && r.Source == SourceTag.Placeholder);
    }

    [Fact]
    public void A_forward_tee_moves_the_frame_and_the_yardages()
    {
        var back = HoleModelBuilder.Build(Cassimbo, 3);
        var front = HoleModelBuilder.Build(Cassimbo, 3, new HoleModelOptions { TeeIndex = 0 });
        Assert.Equal("T1", front.Tee.Name);
        Assert.True(front.GreenFrame.Along < back.GreenFrame.Along - 30);
        Assert.True(front.GreenFrontYards < back.GreenFrontYards - 30);
    }

    [PrivateSampleFact(Pilolcura)]
    public void Pilolcura_hole_8_matches_the_yardage_book()
    {
        var m = HoleModelBuilder.Build(Path.Combine(TestPaths.PrivateDir, Pilolcura), 8);
        Assert.Equal(4, m.Par);
        Assert.Equal(new[] { 460, 483, 500, 515 }, m.Tees.Select(t => t.Yards));
        Assert.Equal(286, m.AimFromTeeYards);
        Assert.Equal(new[] { (250, 78), (300, 79), (325, 71), (350, 68) }, m.Widths.Select(w => (w.Station, w.Yards)));
        Assert.Equal(38, m.GreenDepthYards);
        var bunker = Assert.Single(m.Hazards, h => h.Ground == Ground.Bunker);
        Assert.Equal("Right", bunker.Side);
        Assert.InRange(bunker.FromYards, 293, 303);
        Assert.InRange(bunker.ToYards, 323, 335);
        Assert.Contains(m.Hazards, h => h.Ground == Ground.Scrub && h.Side == "Across the line");
        Assert.Empty(m.UnknownSurfaces);                           // the id 15/16 brushes are 60+ yds off the line
    }

    [Fact]
    public void Unknown_surfaces_are_listed_and_play_as_told()
    {
        var m = HoleModelBuilder.Build(Cassimbo, 3);
        Assert.Contains(m.UnknownSurfaces, u => u.SurfaceId == Surfaces.Waste && u.Name == "Waste");
        var asBunker = HoleModelBuilder.Build(Cassimbo, 3, new HoleModelOptions { Treatments = new Dictionary<int, Ground?> { [Surfaces.Waste] = Ground.Bunker } });
        Assert.True(asBunker.Grid.Counts()[(int)Ground.Bunker] > m.Grid.Counts()[(int)Ground.Bunker]);
    }

    [Fact]
    public void Same_save_shows_no_change_and_edits_near_the_hole_are_named()
    {
        var file = CourseFile.Load(Cassimbo);
        var before = HoleModelBuilder.Build(file.ReadModel(), file.Description, 3);
        var again = HoleModelBuilder.Build(file.ReadModel(), file.Description, 3);
        var none = HoleChanges.Compare(before, again);
        Assert.False(none.Any);
        Assert.StartsWith("Nothing changed on hole 3", Assert.Single(none.Lines));

        var desc = (JsonObject)JsonNode.Parse(file.Description.ToJsonString())!;
        var hole = desc["holes3"]!.AsArray()[2]!;
        var pinPos = hole["pins"]!.AsArray()[1]!["position"]!;
        pinPos["x"] = pinPos["x"]!.GetValue<double>() + 3;
        var green = hole["waypoints"]!.AsArray()[^1]!;
        var stamp = (JsonObject)JsonNode.Parse(desc["height"]!.AsArray()[0]!.ToJsonString())!;
        stamp["position"]!["x"] = green["x"]!.GetValue<double>() + 20;
        stamp["position"]!["z"] = green["z"]!.GetValue<double>() + 5;
        stamp["scale"]!["x"] = 6.0;
        desc["height"]!.AsArray().Add(stamp);

        var after = HoleModelBuilder.Build(CourseModel.From(desc), desc, 3);
        var changes = HoleChanges.Compare(before, after);
        Assert.True(changes.TeesOrPins);
        Assert.True(changes.Terrain);
        Assert.True(changes.InvalidatesResults);
        Assert.Contains(changes.Lines, l => l.StartsWith("Pin 2 moved"));
        Assert.Contains(changes.Lines, l => l.StartsWith("Height brushes near the hole: 1 added at "));
    }

    [Fact]
    public void Save_watcher_reports_a_settled_new_save_once_until_dismissed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"holesim-watch-{Guid.NewGuid():N}.course");
        try
        {
            File.WriteAllText(path, "first");
            var w = new CourseSaveWatcher(FileStamp.Read(path));
            Assert.Equal(SaveCheckState.Unchanged, w.Check().State);

            File.WriteAllText(path, "second save");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
            Assert.Equal(SaveCheckState.Settling, w.Check().State);    // first sight of the change
            var check = w.Check();
            Assert.Equal(SaveCheckState.NewSave, check.State);
            Assert.Equal(SaveCheckState.NewSave, w.Check().State);    // keeps reporting until handled
            w.Dismiss();
            Assert.Equal(SaveCheckState.Unchanged, w.Check().State);

            File.WriteAllText(path, "third save");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            w.Check();
            var third = w.Check();
            Assert.Equal(SaveCheckState.NewSave, third.State);
            w.Accept(third.Stamp!);
            Assert.Equal(SaveCheckState.Unchanged, w.Check().State);

            // Touched but identical contents: not a new save.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(20));
            w.Check();
            Assert.Equal(SaveCheckState.Unchanged, w.Check().State);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Assumption_sheet_needs_every_row_confirmed_and_keeps_placeholders_visible()
    {
        var sheet = AssumptionSheet.Load();
        Assert.False(sheet.AllConfirmed);
        Assert.Contains(sheet.Rows, r => r.Id == "aim.reference" && r.Source == SourceTag.Alex);
        Assert.True(sheet.Placeholders.Count > 10);

        var now = new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc);
        var carry = sheet["player.power.carry"];
        Assert.NotNull(carry.TrySet("three hundred", now));
        Assert.False(carry.Confirmed);
        Assert.Null(carry.TrySet("335", now));
        Assert.Equal(SourceTag.Alex, carry.Source);
        Assert.True(carry.Confirmed);
        Assert.NotNull(sheet["ground.fairway"].TrySet("0.1, 8", now));       // needs three numbers
        foreach (var g in sheet.Groups.ToList()) sheet.ConfirmGroup(g, now);
        Assert.True(sheet.AllConfirmed);
        Assert.Equal(335, sheet.Profile("power").Carry);

        var priors = Path.Combine(Path.GetTempPath(), $"holesim-priors-{Guid.NewGuid():N}.json");
        try
        {
            sheet.SavePriors(priors);
            var next = AssumptionSheet.Load(priors);
            Assert.False(next.AllConfirmed);                                     // prefilled, never pre-confirmed
            var c = next["player.power.carry"];
            Assert.Equal("335", c.Value);
            Assert.Equal(SourceTag.Prior, c.Source);
            Assert.Equal(now, c.PriorConfirmed!.Value.ToUniversalTime());
            var sd = next["player.power.carrySd"];
            Assert.Equal(SourceTag.Placeholder, sd.Source);                      // reused placeholder stays a placeholder
            Assert.NotNull(sd.PriorConfirmed);
        }
        finally { File.Delete(priors); }
    }

    [Fact]
    public void A_run_depends_only_on_its_own_players_placeholders()
    {
        var sheet = AssumptionSheet.Load();
        var now = new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc);
        Assert.Null(sheet["player.power.carry"].TrySet("330", now));          // Alex's own value: no longer a placeholder
        var power = sheet.PlaceholdersFor("power", terrain: false).Select(r => r.Id).ToList();
        Assert.DoesNotContain(power, id => id.StartsWith("player.normal."));
        Assert.DoesNotContain("player.power.carry", power);
        Assert.Contains("player.power.carrySd", power);
        Assert.DoesNotContain("ground.elevationCarry", power);                 // flat-ground run
        Assert.Contains("scoring.baseline", power);
        var normal = sheet.PlaceholdersFor("normal", terrain: true).Select(r => r.Id).ToList();
        Assert.Contains("player.normal.carry", normal);
        Assert.DoesNotContain(normal, id => id.StartsWith("player.power."));
        Assert.Contains("ground.elevationCarry", normal);
        Assert.Contains("terrain.smoothing", normal);
    }

    [Fact]
    public void Course_settings_rows_come_from_the_file_with_the_stimp_line()
    {
        var file = CourseFile.Load(Cassimbo);
        var m = HoleModelBuilder.Build(file.ReadModel(), file.Description, 3);
        var sheet = AssumptionSheet.Load();
        sheet.SetCourseSettings(m);
        var green = sheet["course.greenSpeed"];
        Assert.Contains("Stimp 10.9", green.Value);                          // Cassimbo 0.97 reads 10.9 in game
        Assert.Equal(HoleModelBuilder.SettingKeys.Length, sheet.Rows.Count(r => r.Group == AssumptionSheet.CourseGroup));
        Assert.False(green.Editable);
    }

    [Fact]
    public void Geometry_run_is_deterministic_paired_and_consistent_with_the_baseline()
    {
        var m = HoleModelBuilder.Build(Cassimbo, 3);
        var sheet = AssumptionSheet.Load();
        var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
        var settings = new RunSettings { SamplesPerAim = 300, AimMin = -20, AimMax = 20, AimStep = 10 };
        var a = GeometryRun.Run(m, sheet.Profile("power"), 1, sheet.GroundRules(), sheet.Lies(), baseline, settings);
        var b = GeometryRun.Run(m, sheet.Profile("power"), 1, sheet.GroundRules(), sheet.Lies(), baseline, settings);
        Assert.Equal(5, a.Aims.Count);
        Assert.Equal(a.Aims.Select(x => x.MeanSg), b.Aims.Select(x => x.MeanSg));
        foreach (var aim in a.Aims)
        {
            Assert.Equal(1.0, aim.ShareByGround.Sum(), 9);
            Assert.Equal(a.TeeExpected - aim.MeanSg, aim.ExpectedStrokes, 9);  // SG telescopes: E(tee) − SG
        }
        // Paired draws: the same shot number lands at the same carry whatever the aim.
        var dir = m.AimFrame * (1 / m.AimFrame.Length);
        double Carry(FramePoint p) => p.Lat * dir.Lat + p.Along * dir.Along;
        var l0 = a.Aims[0].Samples[0].Landing; var l1 = a.Aims[^1].Samples[0].Landing;
        Assert.Equal(Carry(l0), Carry(l1), 6);
        Assert.Equal(40, (l1 - l0).Length, 6);                                 // and sits exactly 40 yds further right
        Assert.Contains(a.Notes, n => n.StartsWith("Geometry only"));
    }

    [Fact]
    public void Flat_roll_stops_in_sand_and_runs_further_on_fairway_than_rough()
    {
        var m = HoleModelBuilder.Build(Cassimbo, 3);
        var table = AssumptionSheet.Load().GroundRules();
        FramePoint Find(Ground g) { for (int y = 0; y < m.Grid.Height; y++) for (int x = 0; x < m.Grid.Width; x++) if (m.Grid[x, y] == g) return m.Grid.CellCentre(x, y); throw new Exception(); }
        var sand = Find(Ground.Bunker);
        Assert.Equal(sand, GeometryRun.Roll(m.Grid, table, sand, new FramePoint(0, 1), 6));
        var flat = m.Grid.With((x, y, g) => Ground.Fairway);
        var rough = m.Grid.With((x, y, g) => Ground.Rough);
        double onFairway = GeometryRun.Roll(flat, table, new FramePoint(0, 200), new FramePoint(0, 1), 6).Along - 200;
        double onRough = GeometryRun.Roll(rough, table, new FramePoint(0, 200), new FramePoint(0, 1), 6).Along - 200;
        Assert.True(onFairway > onRough + 10, $"fairway {onFairway:0.0} vs rough {onRough:0.0}");
    }

    [Fact]
    public void Map_renders_a_png()
    {
        var m = HoleModelBuilder.Build(Cassimbo, 3);
        var png = HoleMapRenderer.RenderPng(m, new MapView { PixelsPerYard = 1 });
        Assert.True(png.Length > 1000);
        Assert.Equal(0x89, png[0]);
    }
}
