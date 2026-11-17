using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

/// <summary>
/// The designer's bunker depth sliders (<c>bunkerSettings2</c>, <c>bunkerOverrideParameters</c>) and the cut they make
/// in the replayed ground when <see cref="TerrainModelOptions.BunkerDepths"/> is on.
/// </summary>
[Trait("Area", "Core")]
public class BunkerCutTests
{
    static readonly TerrainModelOptions Cut = new() { BunkerDepths = true };

    /// <summary>A flat plot at 0 m with the given bunkers; the course's own sliders are all 0.</summary>
    static CourseModel Flat(IReadOnlyList<SurfaceBrush>? brushes = null, IReadOnlyList<SurfaceSpline>? splines = null,
        BunkerSettings? settings = null) => new()
    {
        Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49, Holes = [], HeightStamps = [], PlacedObjects = [],
        SurfaceBrushes = brushes ?? [], SurfaceSplines = splines ?? [], Bunkers = settings ?? BunkerSettings.None,
    };

    static SurfaceBrush Dab(int index, double x, double z, double scale, BunkerOverride? own) =>
        new(index, Surfaces.Bunker, 0, new Vec2(x, z), scale, 0, 1) { Bunker = own };

    // Lip 0.2 m with a near-vertical face, then a floor 0.3 m deeper: 0.5 m in all, reached 1.84 m in from the edge.
    static readonly BunkerOverride Deep = new(0.3, -1, 0.2, 1, 0);

    static Heightfield Replay(CourseModel course, double half = 15, TerrainModelOptions? options = null) =>
        TerrainReplay.Build(course, new Vec2(-half, -half), new Vec2(half, half), options ?? Cut);

    [Fact]
    public void Settings_AreReadFromTheCourseFile()
    {
        var murray = Samples.Course("murray-estate.course");
        Assert.True(murray.Bunkers.HighResStamping);
        Assert.False(murray.Bunkers.FlatFloor);
        Assert.Equal(0.15, murray.Bunkers.Regular.BaseDepth, 3);
        Assert.Equal(0.15, murray.Bunkers.Regular.LipDepth, 3);
        Assert.Equal(0.75, murray.Bunkers.Regular.LipSteepness, 3);
        Assert.Equal(0.3, murray.Bunkers.Regular.Depth, 3);          // the file's own stampDepth
        Assert.Equal(0.55, murray.Bunkers.Pot.LipStart, 3);
        Assert.True(murray.HasBunkerDepths);

        // Drawn bunkers carry their own sliders: some set a depth, some only the flat floor (−1 = the course's value).
        var drawn = murray.SurfaceSplines.Where(s => s.Surface == Surfaces.Bunker).ToList();
        Assert.Equal(28, drawn.Count);
        Assert.Contains(drawn, s => s.Bunker is null);
        Assert.Contains(drawn, s => s.Bunker is { BaseDepth: < 0, LipDepth: < 0, FlatFloor: 1 });
        Assert.Contains(drawn, s => s.Bunker is { FlatFloor: 0 } o && Math.Abs(o.BaseDepth - 0.1) < 1e-3);
    }

    [Fact]
    public void PaintedBunkers_CarryTheirOwnSliders()
    {
        // Cassimbo's course sliders are 0, so only its 211 dabs and its drawn bunkers with their own depth are cut.
        var cassimbo = Samples.Course("cassimbo.course");
        Assert.Equal(0, cassimbo.Bunkers.Regular.Depth, 6);
        var dabs = cassimbo.SurfaceBrushes.Where(b => b.Category == Surfaces.Bunker).ToList();
        Assert.Equal(1620, dabs.Count);
        Assert.Equal(211, dabs.Count(b => b.Bunker is not null));
        Assert.Equal(92, dabs.Count(b => b.Bunker is { } o && Math.Abs(o.BaseDepth - 0.15) < 1e-3 && Math.Abs(o.LipSteepness - 0.45) < 1e-3));
        Assert.True(cassimbo.HasBunkerDepths);
        Assert.DoesNotContain(Samples.Course("sagan-khushun.course").SurfaceBrushes, b => b.Bunker is not null);
    }

    [Fact]
    public void Resolve_UsesTheBunkersOwnSlidersWhereSet()
    {
        var course = new BunkerSettings(true, new BunkerDepression(0.15, 0.5, 0.1, 0.75), BunkerDepression.None, FlatFloor: false);
        Assert.Equal((course.Regular, false), course.Resolve(null));

        var (d, flat) = course.Resolve(new BunkerOverride(0.3, -1, -1, 0.4, 1));
        Assert.Equal(new BunkerDepression(0.3, 0.5, 0.1, 0.4), d);
        Assert.True(flat);
        Assert.Equal(0.4, d.Depth, 6);

        // Flat floor is a three-way switch: 0 the course's, 1 on, 2 off.
        Assert.False(course.Resolve(new BunkerOverride(-1, -1, -1, -1, 0)).FlatFloor);
        Assert.False((course with { FlatFloor = true }).Resolve(new BunkerOverride(-1, -1, -1, -1, 2)).FlatFloor);
        Assert.True((course with { FlatFloor = true }).Resolve(new BunkerOverride(-1, -1, -1, -1, 0)).FlatFloor);
    }

    [Fact]
    public void Depth_RunsDownTheLipThenEasesToTheFloor()
    {
        var d = new BunkerDepression(0.3, 0.5, 0.2, 0.5);             // a 50° face: 0.17 m of run for 0.2 m of lip
        double lipRun = BunkerCut.LipRun(d);
        Assert.Equal(0.2 / Math.Tan(50 * Math.PI / 180), lipRun, 6);
        Assert.Equal(lipRun + 1.8, BunkerCut.Reach(d), 6);

        Assert.Equal(0, BunkerCut.DepthAt(d, 0));
        Assert.Equal(0, BunkerCut.DepthAt(d, -1));
        Assert.Equal(0.1, BunkerCut.DepthAt(d, lipRun / 2), 6);       // half way down the face
        Assert.Equal(0.2, BunkerCut.DepthAt(d, lipRun), 6);           // its foot
        Assert.Equal(0.35, BunkerCut.DepthAt(d, lipRun + 0.9), 6);    // half way along the floor's fall
        Assert.Equal(0.5, BunkerCut.DepthAt(d, BunkerCut.Reach(d)), 6);
        Assert.Equal(0.5, BunkerCut.DepthAt(d, 40), 6);
        Assert.Equal(0.5, BunkerCut.DepthAt(d, double.PositiveInfinity), 6);

        // Deeper the further in, never past lip + base.
        double last = 0;
        for (double x = 0; x <= 3; x += 0.01)
        {
            double depth = BunkerCut.DepthAt(d, x);
            Assert.InRange(depth, last, 0.5 + 1e-9);
            last = depth;
        }
        // A steeper lip is a shorter face; no lip, no face.
        Assert.True(BunkerCut.LipRun(d with { LipSteepness = 1 }) < lipRun);
        Assert.Equal(0, BunkerCut.LipRun(d with { LipDepth = 0 }));
    }

    [Fact]
    public void OffByDefault_TheReplayIsUntouched()
    {
        Assert.False(new TerrainModelOptions().BunkerDepths);
        var course = Flat([Dab(0, 0, 0, 20, Deep)]);
        Assert.All(Replay(course, options: new TerrainModelOptions()).Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void PaintedBunker_IsCutBelowItsEdge()
    {
        var f = Replay(Flat([Dab(0, 0, 0, 20, Deep)]));                // sand out to 10 m from the middle
        Assert.Equal(-0.5, f.Sample(new Vec2(0, 0)), 6);
        Assert.Equal(-0.5, f.Sample(new Vec2(5, 0)), 6);
        Assert.InRange(f.Sample(new Vec2(9, 0)), -0.43, -0.30);        // 1 m in: the lip and about half the floor's fall (0.37)
        Assert.Equal(0, f.Sample(new Vec2(10.5, 0)), 6);               // grass
        Assert.Equal(0, f.Sample(new Vec2(-12, 9)), 6);
        Assert.Equal(-0.5, f.Values.Min(), 6);
        Assert.Equal(0, f.Values.Max(), 6);
    }

    [Fact]
    public void BunkerWithNoDepth_IsLeftLevel()
    {
        // The course's sliders are 0 and the dab has none of its own; a faint dab is not sand at all.
        Assert.All(Replay(Flat([Dab(0, 0, 0, 20, null)])).Values, v => Assert.Equal(0, v));
        var faint = new SurfaceBrush(0, Surfaces.Bunker, 0, new Vec2(0, 0), 20, 0, 0.2) { Bunker = Deep };
        Assert.All(Replay(Flat([faint])).Values, v => Assert.Equal(0, v));
        // The course's own sliders apply to a dab without any.
        var settings = new BunkerSettings(true, new BunkerDepression(0.3, 0.5, 0.2, 1), BunkerDepression.None, false);
        Assert.Equal(-0.5, Replay(Flat([Dab(0, 0, 0, 20, null)], settings: settings)).Sample(new Vec2(0, 0)), 6);
    }

    [Fact]
    public void TouchingDabs_AreOneBunker()
    {
        // Two 3 m dabs 4 m apart. Half way between them a point is 1 m inside either dab, but 2.2 m from the edge of
        // the sand as a whole: past the 1.84 m the cut needs, so it is at full depth.
        var one = Replay(Flat([Dab(0, 0, 0, 6, Deep)]));
        Assert.InRange(one.Sample(new Vec2(2, 0)), -0.43, -0.30);
        var two = Replay(Flat([Dab(0, 0, 0, 6, Deep), Dab(1, 4, 0, 6, Deep)]));
        Assert.Equal(-0.5, two.Sample(new Vec2(2, 0)), 6);
    }

    [Fact]
    public void DrawnBunkers_AreCutToo()
    {
        static SplinePoint At(double x, double z) => new(new Vec2(x, z), new Vec2(x, z), new Vec2(x, z), 0);
        var area = new SurfaceSpline(0, Surfaces.Bunker, true, 0, [At(-8, -8), At(8, -8), At(8, 8), At(-8, 8)]) { Bunker = Deep };
        var f = Replay(Flat(splines: [area]));
        Assert.Equal(-0.5, f.Sample(new Vec2(0, 0)), 6);
        Assert.Equal(-0.5, f.Sample(new Vec2(-5, 5)), 6);
        Assert.Equal(0, f.Sample(new Vec2(9, 0)), 6);
        Assert.Equal(0, f.Sample(new Vec2(-9, -9)), 6);

        // An open line of sand 6 m wide: cut along its length, grass beyond its ends and sides.
        var ribbon = new SurfaceSpline(0, Surfaces.Bunker, false, 6, [At(-8, 0), At(8, 0)]) { Bunker = Deep };
        f = Replay(Flat(splines: [ribbon]));
        Assert.Equal(-0.5, f.Sample(new Vec2(0, 0)), 6);
        Assert.Equal(-0.5, f.Sample(new Vec2(-7, 0.5)), 6);
        Assert.InRange(f.Sample(new Vec2(0, 2.5)), -0.3, -0.19);       // half a metre in from the side
        Assert.Equal(0, f.Sample(new Vec2(0, 3.6)), 6);
        Assert.Equal(0, f.Sample(new Vec2(12, 0)), 6);
    }

    [Fact]
    public void EveryWindow_GivesTheSameCut()
    {
        // A window that takes in the whole bunker and one that sees a corner of it agree cell for cell.
        var course = Flat([Dab(0, 0, 0, 20, Deep), Dab(1, 7, 3, 9, new BunkerOverride(0.1, -1, 0.1, 0.3, 0))]);
        var whole = Replay(course, 15);
        var corner = TerrainReplay.Build(course, new Vec2(4, -2), new Vec2(11, 6), Cut);
        for (int iz = 0; iz < corner.Height; iz++)
            for (int ix = 0; ix < corner.Width; ix++)
                Assert.Equal(whole.Sample(corner.CellCentre(ix, iz)), corner[ix, iz], 9);
        Assert.Contains(corner.Values, v => v < -0.4);
        Assert.Contains(corner.Values, v => v == 0);
    }

    [Fact]
    public void MurrayEstate_BunkersAreCutByNoMoreThanTheirDepth()
    {
        var course = Samples.Course("murray-estate.course");
        var deep = course.SurfaceSplines.Where(s => s.Surface == Surfaces.Bunker && course.Bunkers.Resolve(s.Bunker).Depression.Depth > 0).ToList();
        Assert.NotEmpty(deep);
        double deepest = deep.Max(s => course.Bunkers.Resolve(s.Bunker).Depression.Depth);
        var bunker = deep[0];
        var pad = new Vec2(6, 6);
        var min = new Vec2(bunker.Points.Min(p => p.Anchor.X), bunker.Points.Min(p => p.Anchor.Z)) - pad;
        var max = new Vec2(bunker.Points.Max(p => p.Anchor.X), bunker.Points.Max(p => p.Anchor.Z)) + pad;
        var plain = TerrainReplay.Build(course, min, max, new TerrainModelOptions());
        var cut = TerrainReplay.Build(course, min, max, Cut);
        var drop = plain.Values.Zip(cut.Values, (a, b) => a - b).ToList();
        Assert.All(drop, d => Assert.InRange(d, 0, deepest + 1e-9));
        Assert.True(drop.Max() > 0.05, $"deepest cut {drop.Max():0.000} m");
        Assert.Contains(drop, d => d == 0);                            // the grass round it is untouched
    }
}
