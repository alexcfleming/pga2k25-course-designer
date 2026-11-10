using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.Tests;

/// <summary>The Course Portrait scorecard map style: parcel, coast and the print (docs/scorecard-maps.md).</summary>
[Trait("Area", "Products")]
public class CoursePortraitTests
{
    static readonly Lazy<(CourseLayers Layers, List<Vec2> Parcel)> CassimboParcel = new(() =>
    {
        var layers = CourseLayers.From(Samples.Cassimbo);
        return (layers, PortraitParcel.Build(layers));
    });

    [Fact]
    public void Parcel_IsOneSimplePolygonHoldingEveryTeeGreenAndRoutePoint()
    {
        var (layers, parcel) = CassimboParcel.Value;
        Assert.InRange(parcel.Count, 8, 80);   // long straight property-line facets, not a traced wiggle
        foreach (var h in layers.Holes)
        {
            foreach (var p in h.Route) Assert.True(Geometry.Contains(parcel, p), $"hole {h.Hole.Number} route point outside the parcel");
            foreach (var t in h.Hole.Tees) Assert.True(Geometry.Contains(parcel, t), $"hole {h.Hole.Number} tee outside the parcel");
        }
        foreach (var g in layers.Greens) Assert.All(g, p => Assert.True(Geometry.Contains(parcel, p)));

        // Simple: no two edges cross.
        int n = parcel.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                Assert.False(Cross(parcel[i], parcel[(i + 1) % n], parcel[j], parcel[(j + 1) % n]), $"edges {i} and {j} cross");
            }
    }

    static bool Cross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        static double O(Vec2 p, Vec2 q, Vec2 r) => (q.X - p.X) * (r.Z - p.Z) - (q.Z - p.Z) * (r.X - p.X);
        return O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0;
    }

    [Fact]
    public void Coast_ComesOnlyFromOceanFoam()
    {
        var (_, parcel) = CassimboParcel.Value;
        Assert.Equal(345, CoursePortraitRenderer.Foam(Samples.Cassimbo).Count);
        Assert.NotNull(CoursePortraitRenderer.CoastLand(Samples.Cassimbo, parcel));
        // A course without foam gets no sea.
        var berwick = Samples.Course("berwick-coast.course");
        Assert.Empty(CoursePortraitRenderer.Foam(berwick));
        Assert.Null(CoursePortraitRenderer.CoastLand(berwick, PortraitParcel.Build(CourseLayers.From(berwick))));
    }

    [Fact]
    public void SimplifyClosed_KeepsCornersAndDropsWiggles()
    {
        var ring = new List<Vec2>();
        for (int i = 0; i < 100; i++) ring.Add(new Vec2(i, i % 2 == 0 ? 0 : 0.5));   // a wiggly bottom edge
        ring.AddRange([new Vec2(100, 0), new Vec2(100, 100), new Vec2(0, 100)]);
        var simple = PortraitParcel.SimplifyClosed(ring, 2);
        Assert.Equal(4, simple.Count);
        Assert.Contains(new Vec2(100, 100), simple);
    }

    [Fact]
    public void Theme_IsBuiltWithTheOthers()
    {
        Assert.Equal("Course Portrait", MapTheme.For(MapThemeKind.CoursePortrait).Name);
        Assert.Contains(MapThemeKind.CoursePortrait, Enum.GetValues<MapThemeKind>());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Print_IsNorthUpWithSeaWestNorthAndSouth_AndRendersTheSameTwice()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"portrait-{Guid.NewGuid():N}");
        try
        {
            string course = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            var options = new ScorecardMapOptions { Credit = "Test Designer", Year = 2026 };
            var first = ScorecardMapBuilder.Build(course, Path.Combine(dir, "a"), options, [MapThemeKind.CoursePortrait], pdf: true, scale: 0.75f);
            var second = ScorecardMapBuilder.Build(course, Path.Combine(dir, "b"), options, [MapThemeKind.CoursePortrait], scale: 0.75f);
            string png = first.Files.Single(f => f.EndsWith("(Course Portrait).png"));
            Assert.Contains(first.Files, f => f.EndsWith("(Course Portrait).pdf"));
            Assert.Equal(File.ReadAllBytes(png), File.ReadAllBytes(second.Files[0]));

            using var bmp = SKBitmap.Decode(png);
            Assert.Equal((900, 1200), (bmp.Width, bmp.Height));   // 12 × 16 in, portrait
            // Blue-grey sea wash: blue above red. Cassimbo has sea west, north and south, and is open to the east.
            bool Sea(int x0, int y0, int x1, int y1)
            {
                for (int y = y0; y < y1; y += 3)
                    for (int x = x0; x < x1; x += 3)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.Blue - c.Red > 18 && c.Red < 235) return true;
                    }
                return false;
            }
            Assert.True(Sea(120, 300, 270, 900), "no sea to the west");
            Assert.True(Sea(150, 0, 450, 80), "no sea to the north");
            Assert.True(Sea(150, 900, 750, 1020), "no sea to the south");
            Assert.False(Sea(780, 450, 900, 800), "sea to the east");
            // The title band stays white paper apart from the type.
            var paper = bmp.GetPixel(40, 1150);
            Assert.True(paper.Red > 245 && paper.Green > 245 && paper.Blue > 245);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
