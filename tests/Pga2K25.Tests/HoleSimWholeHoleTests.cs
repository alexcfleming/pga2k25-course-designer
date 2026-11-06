using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 4e: whole holes tee to cup, strokes gained by phase, and design experiments.</summary>
public class HoleSimWholeHoleTests
{
    const string Pilolcura = "pilolcura-analysis-2026-11-02.course";
    static readonly Lazy<TableBaseline> Anchors = new(() => TableBaseline.LoadShipped("pga-tour-anchors"));
    static readonly Lazy<AssumptionSheet> Sheet = new(() => AssumptionSheet.Load());
    static readonly Lazy<PuttingModel> Putter = new(() => PuttingModel.Create(10.3, Anchors.Value, Sheet.Value.Putting()));
    static GroundTable Ground => Sheet.Value.GroundRules(Putter.Value.Friction);
    static readonly Lazy<GolferFit> Fit = new(() =>
        GolferCalibration.Fit(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), Sheet.Value.GolferStart()));

    /// <summary>The calibration hole (600 yds, pin at the centre of a round green) played as a par 4 by Power Drive.</summary>
    static HolePlan Plan(Func<Func<double, double, Vec2>, IEnumerable<PaintShape>>? extra = null)
    {
        var c = GolferCalibration.Synthetic(Anchors.Value, Putter.Value, Ground, Sheet.Value.ShotRules(), Sheet.Value.Lies(), Fit.Value.Golfer, extra: extra);
        return new HolePlan { Context = c, Player = Sheet.Value.Profile("power"), Reach = Sheet.Value.Reach("power"), DriveAim = 0 };
    }

    [Fact]
    public void The_phases_add_up_to_the_hole()
    {
        var s = HolePlay.Play(Plan(), 1, samples: 150);
        // Every shot is scored against the baseline, so the phases telescope to E(tee) less the strokes taken.
        Assert.Equal(s.BaselineTee - s.Expected, s.Sg.Total, 9);
        Assert.Equal(150, s.Scores.Count);
        Assert.InRange(s.Expected, 3.4, 5.5);
        Assert.InRange(s.FairwayHit, 0.3, 1.0);
        Assert.True(s.Birdie + (s.ParOrBetter - s.Birdie) + s.BogeyOrWorse > 0.999);
    }

    [Fact]
    public void Play_outs_are_repeatable()
    {
        var plan = Plan();
        var a = HolePlay.Play(plan, 1, samples: 80, seed: 5);
        var b = HolePlay.Play(plan, 1, samples: 80, seed: 5);
        Assert.Equal(a.Expected, b.Expected);
        Assert.Equal(a.Sg, b.Sg);
    }

    [Fact]
    public void Widening_and_narrowing_change_only_the_landing_zone()
    {
        var grid = Plan().Context.Grid;
        int Fairway(SurfaceGrid g, double from, double to)
        {
            int n = 0;
            for (int y = 0; y < g.Height; y++)
            {
                double along = g.AlongMin + y + 0.5;
                if (along < from || along > to) continue;
                for (int x = 0; x < g.Width; x++) if (g[x, y] == HoleSim.Ground.Fairway) n++;
            }
            return n;
        }
        var wider = Experiments.WidenFairway(grid, 5, 250, 350);
        var narrower = Experiments.WidenFairway(grid, -5, 250, 350);
        int rows = 100;
        Assert.Equal(Fairway(grid, 250, 350) + 2 * 5 * rows, Fairway(wider, 250, 350));       // 5 yds each side over 100 yds
        Assert.Equal(Fairway(grid, 250, 350) - 2 * 5 * rows, Fairway(narrower, 250, 350));
        Assert.Equal(Fairway(grid, 0, 240), Fairway(wider, 0, 240));                           // nothing outside the window
        Assert.Equal(Fairway(grid, 360, 580), Fairway(narrower, 360, 580));
    }

    [Fact]
    public void A_hazard_taken_out_becomes_rough()
    {
        var plan = Plan(W => [new PaintPolygon(HoleSim.Ground.Bunker, [W(20, 300), W(30, 300), W(30, 320), W(20, 320)])]);
        var grid = plan.Context.Grid;
        Assert.Equal(HoleSim.Ground.Bunker, grid.At(new FramePoint(25, 310)));
        var hazard = new HazardInfo(HoleSim.Ground.Bunker, "Right", 300, 320, 200, new FramePoint(25.5, 310.5));
        var edited = Experiments.RemoveHazard(grid, hazard);
        Assert.Equal(HoleSim.Ground.Rough, edited.At(new FramePoint(25, 310)));
        Assert.Equal(HoleSim.Ground.Rough, edited.At(new FramePoint(21, 301)));
        Assert.Equal(grid.At(new FramePoint(0, 310)), edited.At(new FramePoint(0, 310)));    // the fairway is untouched
    }

    [Fact]
    public void An_experiment_against_itself_changes_nothing_and_a_wider_fairway_helps()
    {
        var plan = Plan();
        var same = Experiments.Compare("same", "", plan, Experiments.OnGrid(plan, plan.Context.Grid), samples: 120);
        Assert.All(same.Pins, p => Assert.Equal(0, p.Delta, 9));
        Assert.Equal(0, same.CellsChanged);
        var (from, to) = Experiments.LandingZone(plan);
        Assert.True(from < to);
        var wider = Experiments.Compare("wider", "", plan, Experiments.OnGrid(plan, Experiments.WidenFairway(plan.Context.Grid, 10, from, to)), samples: 200);
        Assert.True(wider.CellsChanged > 0);
        Assert.True(wider.Delta < 0.005, $"wider fairway {wider.Delta:+0.000} ± {wider.Ci95:0.000}");
    }

    [PrivateSampleFact(Pilolcura)]
    public void Pilolcura_hole_8_pin_4_is_the_hardest()
    {
        var file = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, Pilolcura));
        var course = file.ReadModel();
        var m = HoleModelBuilder.Build(course, file.Description, 8);
        var green = GreenSurface.Build(m, ReplayTerrain.Smoothed(course, m, 8));
        var (c, _) = ApproachSetup.Build(m, green, null, Sheet.Value, Anchors.Value, AssumptionSheet.Stimp(course.GreenSpeed)!.Value);
        var plan = new HolePlan { Context = c, Player = Sheet.Value.Profile("power"), Reach = Sheet.Value.Reach("power"), DriveAim = 45 };
        var pins = m.Pins.Select(p => HolePlay.Play(plan, p.Number, 300)).ToList();
        Assert.Equal(4, pins.MaxBy(s => s.Expected)!.Pin);
        Assert.All(pins, s => Assert.Equal(s.BaselineTee - s.Expected, s.Sg.Total, 9));
    }
}
