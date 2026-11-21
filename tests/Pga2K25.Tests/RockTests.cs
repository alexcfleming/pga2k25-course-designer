using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.Tests;

/// <summary>Rocks: the measured shape catalogue, each rock's outline as it shows above the ground, and drawing (docs/rocks.md).</summary>
[Trait("Area", "Products")]
public class RockTests
{
    static PlacedObject Rock(string name, double scale = 1, double yaw = 0, double x = 0, double z = 0) =>
        new($"Assets/CourseGen/Detail/Rocks/Test/{name}Prefab", new Vec2(x, z), scale, null, yaw);

    [Fact]
    public void Catalogue_UsesMeasuredSizes_AndRaisesHeightToHowDeepDesignersSinkTheShape()
    {
        var stone = RockCatalogue.For("Assets/Rocks/Stone01Prefab");
        Assert.True(stone.Measured);
        Assert.InRange(stone.Width, 2.5, 2.9);                    // measured 2.68 m across at scale 1
        // SinkHole Medium_03_AC measured 16.8 m tall, but 90% of its uses sink it to 18 m: it must be taller than that.
        var pillar = RockCatalogue.For("Assets/Rocks/SinkHole_Rocks_Medium_03_ACPrefab");
        Assert.True(pillar.Height >= 18.05 * 1.1 - 0.01, $"height {pillar.Height}");
        Assert.True(pillar.Rough);
        Assert.False(stone.Rough);
    }

    [Fact]
    public void Catalogue_AnUnmeasuredShapeBorrowsItsFamily_AndAnUnknownOneIsAStone()
    {
        // Medium_02_BB wasn't measured; its family (SinkHole_Rocks_Medium_*) are 8-17 m wide.
        var family = RockCatalogue.For("Assets/Rocks/SinkHole_Rocks_Medium_02_BBPrefab");
        Assert.False(family.Measured);
        Assert.InRange(family.Width, 8, 17);
        var unknown = RockCatalogue.For("Assets/Rocks/Unheardof_BoulderPrefab");
        Assert.Equal(RockCatalogue.Stone.Width, unknown.Width);
    }

    [Fact]
    public void Shape_NarrowsAsItSinks_AndIsGoneAtItsTop()
    {
        var s = new RockShape(10, 20, 10, 5, true, true);
        Assert.Equal(10, s.WidthAt(0));
        Assert.Equal(7.5, s.WidthAt(5), 6);       // halfway to the measured sunk point
        Assert.Equal(5, s.WidthAt(10), 6);        // the measured sunk width
        Assert.Equal(2.5, s.WidthAt(15), 6);      // on to nothing at the top
        Assert.Equal(0, s.WidthAt(20));
    }

    [Fact]
    public void Outline_IsTurnedByYaw_ScaledAndCutByTheGround()
    {
        var flat = RockOutlines.Of(Rock("Stone01"), 0)!;
        var big = RockOutlines.Of(Rock("Stone01", scale: 3), 0)!;
        Assert.Equal(flat.VisibleWidth * 3, big.VisibleWidth, 6);
        // A quarter turn swaps the outline's east-west and north-south reach.
        var turned = RockOutlines.Of(Rock("Stone01", yaw: 90), 0)!;
        double SpanX(RockOutline o) => o.Outline.Max(p => p.X) - o.Outline.Min(p => p.X);
        double SpanZ(RockOutline o) => o.Outline.Max(p => p.Z) - o.Outline.Min(p => p.Z);
        Assert.Equal(SpanX(flat), SpanZ(turned), 6);
        Assert.Equal(SpanZ(flat), SpanX(turned), 6);
        // Every copy of a shape is the same rock.
        Assert.Equal(flat.Outline, RockOutlines.Of(Rock("Stone01"), 0)!.Outline);
        // Sunk past its top, nothing shows.
        Assert.Null(RockOutlines.Of(Rock("Stone01"), 5));
        Assert.True(RockOutlines.Of(Rock("Stone01"), 0.3)!.VisibleWidth < flat.VisibleWidth);
    }

    [Fact]
    public void Course_ReadsEachRocksYaw()
    {
        var rocks = Samples.Course("sagan-khushun.course").PlacedObjects.Where(RockCatalogue.IsRock).ToList();
        Assert.NotEmpty(rocks);
        Assert.Contains(rocks, r => Math.Abs(r.Yaw) > 1);
    }

    /// <summary>Sagan-Khushun's shoreline boulders are placed at scale 30-50: they show as big rocks, not dots.</summary>
    [Trait("Speed", "Slow")]
    [Fact]
    public void SaganKhushun_ShorelineBouldersShowLarge()
    {
        var shown = RockOutlines.For(Samples.Course("sagan-khushun.course"));
        Assert.True(shown.Count >= 20, $"{shown.Count} rocks show");
        Assert.True(shown.Count(r => r.VisibleWidth > 20) >= 5, "several boulders over 20 m across");
    }

    /// <summary>
    /// A tester's course, hole 7: the pond's far bank is a barrier of rocks. They show along it (Alex's screenshot), and
    /// none sits on a tree, bush or plant (which can't grow inside a rock).
    /// </summary>
    [PrivateSampleFact("9plmwuhnqid5.course")]
    public void TesterCourse_Hole7_RockBarrierShowsAndKeepsOffThePlants()
    {
        var course = Samples.Course("9plmwuhnqid5.course", privateDir: true);
        var pond = WalledWaterRules.Find(course).Single(w => w.SplineIndex == 543);
        var centre = new Vec2(pond.Outline.Average(p => p.X), pond.Outline.Average(p => p.Z));
        var near = RockOutlines.For(course).Where(r => Vec2.Distance(r.Rock.Position, centre) < 120).ToList();
        Assert.True(near.Count >= 15, $"{near.Count} rocks show round the pond");
        var plants = course.PlacedObjects.Where(o => o.AssetPath.Contains("/Trees/") || o.AssetPath.Contains("/Bushes/") || o.AssetPath.Contains("/Plants/"))
            .Select(o => o.Position).ToList();
        int overPlants = near.Count(r => plants.Any(p => Geometry.Contains(r.Outline, p)));
        Assert.True(overPlants <= 2, $"{overPlants} rocks cover a plant");
    }

    [Fact]
    public void Art_DrawsEachLookInsideTheRocksOutline()
    {
        var outline = RockOutlines.Of(Rock("Stone01", scale: 10, x: 0, z: 0), 0)!;
        foreach (var style in new[] { RockStyle.Painted(), RockStyle.Flat(SKColors.Gray, SKColors.Black), RockStyle.Ink(SKColors.White, SKColors.Black) })
        {
            using var bmp = new SKBitmap(200, 200);
            using (var c = new SKCanvas(bmp))
            {
                c.Clear(SKColors.Transparent);
                RockArt.Draw(c, [outline], o =>
                {
                    using var b = new SKPathBuilder();
                    b.MoveTo((float)(100 + o[0].X * 4), (float)(100 + o[0].Z * 4));
                    foreach (var p in o.Skip(1)) b.LineTo((float)(100 + p.X * 4), (float)(100 + p.Z * 4));
                    b.Close();
                    return b.Detach();
                }, style);
            }
            Assert.True(bmp.GetPixel(100, 100).Alpha > 200, $"{style.Look}: the rock's middle is painted");
            Assert.Equal(0, bmp.GetPixel(2, 2).Alpha);
        }
    }
}
