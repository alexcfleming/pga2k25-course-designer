using System.Text;
using Pga2K25.CourseFormat;
using Pga2K25.HoleIllustrations;
using Pga2K25.Maps;
using Pga2K25.Studio.Services;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.Tests;

[Trait("Area", "Products")]
public class HoleIllustrationTests
{
    static readonly string CassimboPath = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
    static readonly Lazy<CourseLayers> Layers = new(() => CourseLayers.From(Samples.Cassimbo));
    static readonly IllustrationOptions Options = new() { Credit = "Test Designer", Year = 2026 };
    static readonly Dictionary<int, Lazy<HoleAnalysis>> Analyses = new[] { 1, 5, 7 }.ToDictionary(n => n, n => new Lazy<HoleAnalysis>(() =>
        HoleIllustrationBuilder.Analyze(Samples.Cassimbo, Layers.Value, Samples.Cassimbo.Holes.Single(h => h.Number == n), Options,
            HoleIllustrationBuilder.YardageFor(Options, new CourseHoles()))));
    static readonly Lazy<Plate> Hole5 = new(() => { using var r = new HoleIllustrationRenderer(Options); return r.Render(Analyses[5].Value); });

    [Theory]
    [InlineData(218, 250)]   // Cassimbo H5
    [InlineData(374, 450)]   // H1
    [InlineData(629, 700)]   // H7
    [InlineData(420, 450)]
    [InlineData(421, 500)]
    public void RulerTop_IsBackTeeYardsPlus30RoundedUpToTheNext50(int yards, int top) =>
        Assert.Equal(top, PlateLayout.RulerTop(yards));

    [Fact]
    public void Studio_ListsHoleIllustrationsAfterTheYardageBook()
    {
        var names = Products.All.Select(p => p.Name).ToList();
        Assert.Equal(names.IndexOf("Yardage book") + 1, names.IndexOf("Hole illustrations"));
        var p = Products.All.Single(x => x.Name == "Hole illustrations");
        Assert.Equal(["{0} - Hole Illustrations/*.png", "{0} - Hole Illustrations (Coore & Crenshaw)/*.png", "{0} - Hole Illustrations (Fried Egg)/*.png"], p.Pages);
        Assert.Equal(["{0} - Hole Illustrations.pdf", "{0} - Hole Illustrations (Coore & Crenshaw).pdf", "{0} - Hole Illustrations (Fried Egg).pdf"], p.Pdfs);
    }

    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public void Plate_PutsTheBackTeeOnZeroAndTheGreenAtItsRulerYards(int hole)
    {
        var a = Analyses[hole].Value;
        int top = PlateLayout.RulerTop(a.LengthYards);
        float upy = PlateLayout.UnitsPerYard(top);
        var proj = new PlateProjection(a.RotationDegrees, a.Route, upy);
        Assert.InRange(proj.Page(a.BackTee).Y, PlateLayout.ZeroY - 2, PlateLayout.ZeroY + 2);
        // Ticks measure straight up the page from the back tee: the green sits at its up-page distance.
        double upPage = ((a.Green - a.BackTee).X * a.Forward.X + (a.Green - a.BackTee).Z * a.Forward.Z) / HoleAnalysis.Yard;
        double onRuler = (PlateLayout.ZeroY - proj.Page(a.Green).Y) / upy;
        Assert.InRange(onRuler, upPage - 3, upPage + 3);
        // The green is inside the art zone, and below the top tick.
        Assert.InRange(proj.Page(a.Green).Y, PlateLayout.TopTickY, PlateLayout.ZeroY);
        Assert.InRange(proj.Page(a.Green).X, PlateLayout.ArtZone.Left, PlateLayout.ArtZone.Right);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Plate_Hole5GreenSitsAtItsStraightLineYardsFromTheBackTee()
    {
        // 218 yds along the line of play, which bends at an aim point; 207 yds straight up the page.
        var p = Hole5.Value;
        Assert.Equal(250, p.RulerTop);
        Assert.Equal(218, p.Analysis.LengthYards);
        double straight = Vec2.Distance(p.Analysis.BackTee, p.Analysis.Green) / HoleAnalysis.Yard;
        double onRuler = (PlateLayout.ZeroY - p.Green.Y) / p.UnitsPerYard;
        Assert.InRange(onRuler, straight - 3, straight + 3);
        Assert.InRange(onRuler, 204, 210);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Plate_ArtFadesToPaperAtTheZoneEdges()
    {
        var p = Hole5.Value;
        int w = p.Art.Width, h = p.Art.Height;
        float worst = 0;
        for (int x = 0; x < w; x++) worst = Math.Max(worst, Math.Max(p.Darkness(x, 0), p.Darkness(x, h - 1)));
        for (int y = 0; y < h; y++) worst = Math.Max(worst, Math.Max(p.Darkness(0, y), p.Darkness(w - 1, y)));
        Assert.True(worst < 0.02f, $"art reaches the zone edge at {worst:P1} darkness");
        // ...and there is art inside it.
        Assert.True(p.Darkness(w / 2, (int)((p.Green.Y - PlateLayout.ArtZone.Top) * PlateLayout.RasterScale)) > 0.05f);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Plate_RendersTheSamePixelsTwice()
    {
        using var r = new HoleIllustrationRenderer(Options);
        using var again = r.Render(Analyses[5].Value);
        using var a = Hole5.Value.Art.Encode(SKEncodedImageFormat.Png, 100);
        using var b = again.Art.Encode(SKEncodedImageFormat.Png, 100);
        Assert.True(a.ToArray().AsSpan().SequenceEqual(b.ToArray()), "two renders of hole 5 differ");
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Page_PrintsTheHoleNameOnlyWhenOneIsGiven()
    {
        using var r = new HoleIllustrationRenderer(Options);
        var p = Hole5.Value;
        SKBitmap Draw(string? name)
        {
            var plate = new Plate
            {
                Analysis = p.Analysis, Projection = p.Projection, RulerTop = p.RulerTop, UnitsPerYard = p.UnitsPerYard,
                HoleName = name, Art = p.Art, Darkness = p.Darkness,
            };
            var bmp = new SKBitmap(850, 1100);
            using var c = new SKCanvas(bmp);
            r.DrawPage(c, "Cassimbo", plate);
            return bmp;
        }
        static int Inked(SKBitmap b)
        {
            int n = 0;
            for (int y = 106; y < 130; y++) for (int x = 64; x < 140; x++) if (b.GetPixel(x, y).Red < 200) n++;
            return n;
        }
        using var none = Draw(null);
        using var empty = Draw("   ");
        using var named = Draw("Shaman Rock");
        Assert.Equal(0, Inked(none));
        Assert.Equal(0, Inked(empty));
        Assert.True(Inked(named) > 20);
    }

    [Fact]
    public void IllustrationStyles_AreTickedPerCourse_NoneToStart()
    {
        var extras = new CourseExtras();
        Assert.Empty(extras.IllustrationLookKinds());
        extras.SetIllustrationLook(IllustrationLook.CooreCrenshaw, true);
        extras.SetIllustrationLook(IllustrationLook.Watercolour, true);
        Assert.Equal([IllustrationLook.Watercolour, IllustrationLook.CooreCrenshaw], extras.IllustrationLookKinds());   // menu order
        var back = System.Text.Json.JsonSerializer.Deserialize<CourseExtras>(System.Text.Json.JsonSerializer.Serialize(extras))!;
        Assert.Equal([IllustrationLook.Watercolour, IllustrationLook.CooreCrenshaw], back.IllustrationLookKinds());
        back.SetIllustrationLook(IllustrationLook.Watercolour, false);
        back.SetIllustrationLook(IllustrationLook.CooreCrenshaw, false);
        Assert.Empty(back.IllustrationLookKinds());
        Assert.Null(back.IllustrationLooks);
        Assert.Empty(new CourseExtras { IllustrationLooks = ["Retired"] }.IllustrationLookKinds());
        Assert.StartsWith("IN THE STYLE OF COORE & CRENSHAW", SketchPlateRenderer.StyleCredit);
        // The two looks write their own files, so a course can have both.
        var cc = new IllustrationOptions { Look = IllustrationLook.CooreCrenshaw };
        Assert.Equal("Hole Illustrations (Coore & Crenshaw)", cc.ProductName);
        Assert.Equal("Hole_05 (Coore & Crenshaw)", cc.PngName(5));
        Assert.Equal(("Hole Illustrations", "Hole_05"), (new IllustrationOptions().ProductName, new IllustrationOptions().PngName(5)));
        var fe = new IllustrationOptions { Look = IllustrationLook.FriedEgg };
        Assert.Equal(("Hole Illustrations (Fried Egg)", "Hole_05 (Fried Egg)"), (fe.ProductName, fe.PngName(5)));
        Assert.Equal("Inspired by Fried Egg Golf", FriedEggPlateRenderer.StyleCredit);
    }

    sealed class LogLines : IProgress<string>
    {
        public List<string> Lines { get; } = [];
        public void Report(string value) => Lines.Add(value);
    }

    [Fact]
    public void HoleIllustrations_WithNoStyleTicked_BuildsNothingAndSaysWhy()
    {
        var product = Products.All.Single(p => p.Name == "Hole illustrations");
        var log = new LogLines();
        string dir = Path.Combine(Path.GetTempPath(), $"illustrations-noticks-{Guid.NewGuid():N}");
        Assert.Null(product.Build(new BuildContext(Path.Combine(dir, "missing.course"), dir, new StudioSettings(), new CourseExtras(), log)));
        Assert.Equal([Products.NoIllustrationStyles], log.Lines);
        Assert.False(Directory.Exists(dir));
    }

    /// <summary>
    /// The green is drawn over everything, so nothing drawn before it shows through: on Cassimbo hole 7 the fairway's
    /// outline runs well inside the green, and neither look may draw that line there. (The pencil look shaded the green
    /// with translucent strokes only, so the fairway's heavy line showed across it; found on Nodens hole 6, 21 Nov 2026.)
    /// </summary>
    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData(IllustrationLook.Watercolour)]
    [InlineData(IllustrationLook.CooreCrenshaw)]
    [InlineData(IllustrationLook.FriedEgg)]
    public void Green_CoversTheFairwayOutlineUnderIt(IllustrationLook look)
    {
        var a = Analyses[7].Value;
        IPlateRenderer r = look switch
        {
            IllustrationLook.CooreCrenshaw => new SketchPlateRenderer(Options with { Look = look }),
            IllustrationLook.FriedEgg => new FriedEggPlateRenderer(Options with { Look = look }),
            _ => new HoleIllustrationRenderer(Options),
        };
        using var plate = r.Render(a);
        r.Dispose();
        var green = a.Greens.Where(g => !a.Foreign.Contains(g) && g.Count >= 3).OrderBy(g => Vec2.Distance(new Vec2(g.Average(p => p.X), g.Average(p => p.Z)), a.Green)).First();
        static double Edge(Vec2 p, List<Vec2> ring) => ring.Select((s, i) =>
        {
            var e = ring[(i + 1) % ring.Count]; double dx = e.X - s.X, dz = e.Z - s.Z, l = dx * dx + dz * dz;
            double t = l == 0 ? 0 : Math.Clamp(((p.X - s.X) * dx + (p.Z - s.Z) * dz) / l, 0, 1);
            return Vec2.Distance(p, new Vec2(s.X + t * dx, s.Z + t * dz));
        }).Min();
        // Points on the fairway's outline at least 3 m inside the green, walked every 0.5 m.
        var under = new List<Vec2>();
        foreach (var fw in a.Fairways)
            for (int i = 0; i < fw.Count; i++)
            {
                var (s, e) = (fw[i], fw[(i + 1) % fw.Count]);
                int n = Math.Max(1, (int)(Vec2.Distance(s, e) / 0.5));
                for (int k = 0; k < n; k++)
                {
                    var p = s + (e - s) * ((double)k / n);
                    if (Geometry.Contains(green, p) && Edge(p, green) > 3) under.Add(p);
                }
            }
        Assert.True(under.Count >= 20, $"hole 7's fairway runs inside its green ({under.Count} points)");
        int w = plate.Art.Width, h = plate.Art.Height;
        float At(Vec2 p) { var px = plate.Projection.Pixel(p); return plate.Darkness(Math.Clamp((int)px.X, 0, w - 1), Math.Clamp((int)px.Y, 0, h - 1)); }
        // The green's own shading, sampled on a grid inside it away from the fairway line.
        var inside = new List<float>();
        double minX = green.Min(p => p.X), maxX = green.Max(p => p.X), minZ = green.Min(p => p.Z), maxZ = green.Max(p => p.Z);
        for (double x = minX; x <= maxX; x += 0.7)
            for (double z = minZ; z <= maxZ; z += 0.7)
            {
                var p = new Vec2(x, z);
                if (Geometry.Contains(green, p) && Edge(p, green) > 3 && under.All(q => Vec2.Distance(q, p) > 2)) inside.Add(At(p));
            }
        inside.Sort();
        float typical = inside[inside.Count / 2], loud = inside[(int)(inside.Count * 0.9)];
        double onLine = under.Select(At).OrderBy(d => d).ElementAt(under.Count / 2);
        Assert.True(onLine <= loud + 0.08, $"{look}: the fairway line shows through the green (darkness {onLine:0.00} on the line, green {typical:0.00}, its 90th percentile {loud:0.00})");
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void CooreCrenshaw_PencilPlate_IsGraphiteOnCreamAndTheSameTwice()
    {
        var o = Options with { Look = IllustrationLook.CooreCrenshaw };
        using var r = new SketchPlateRenderer(o);
        using var plate = r.Render(Analyses[5].Value);
        using var again = r.Render(Analyses[5].Value);
        using (var a = plate.Art.Encode(SKEncodedImageFormat.Png, 100))
        using (var b = again.Art.Encode(SKEncodedImageFormat.Png, 100))
            Assert.True(a.ToArray().AsSpan().SequenceEqual(b.ToArray()), "two pencil renders of hole 5 differ");
        // No colour anywhere: pencil and cream paper only; and the hole is drawn (dark outline pixels).
        using var bmp = SKBitmap.FromImage(plate.Art);
        var px = bmp.Pixels;
        Assert.True(px.All(p => Math.Abs(p.Red - p.Blue) < 24 && Math.Abs(p.Green - p.Blue) < 20), "pencil on cream only");
        Assert.True(px.Count(p => p.Red < 90) > 5_000, "pencil lines drawn");
        Assert.Equal(px[0], new SKColor(0xf2, 0xed, 0xe1));   // paper in the corner
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Build_CooreCrenshaw_WritesItsOwnFilesInPencil()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"illustrations-{Guid.NewGuid():N}");
        try
        {
            var result = HoleIllustrationBuilder.Build(CassimboPath, dir, Options with { Look = IllustrationLook.CooreCrenshaw }, new IllustrationOutputs { Pngs = true }, [5]);
            var png = Assert.Single(result.PngPaths);
            Assert.EndsWith(Path.Combine("Cassimbo - Hole Illustrations (Coore & Crenshaw)", "Hole_05 (Coore & Crenshaw).png"), png);
            Assert.EndsWith("Cassimbo - Hole Illustrations (Coore & Crenshaw).pdf", result.PdfPath);
            using (var img = SKBitmap.Decode(png)) Assert.Equal((2550, 3300), (img.Width, img.Height));
            Assert.InRange(new FileInfo(result.PdfPath!).Length, 100_000, 8_000_000);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Build_WritesAPdfInLoraWithTheJimRyanCreditAndAPngPerHole()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"illustrations-{Guid.NewGuid():N}");
        try
        {
            var result = HoleIllustrationBuilder.Build(CassimboPath, dir, Options, new IllustrationOutputs { Pngs = true }, [5]);
            Assert.NotNull(result.PdfPath);
            var png = Assert.Single(result.PngPaths);
            Assert.EndsWith(Path.Combine("Cassimbo - Hole Illustrations", "Hole_05.png"), png);
            using (var img = SKBitmap.Decode(png)) Assert.Equal((2550, 3300), (img.Width, img.Height));
            var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(result.PdfPath!));
            Assert.Contains("Lora", pdf);
            Assert.StartsWith("%PDF", pdf);
            Assert.Contains("JIM RYAN", HoleIllustrationRenderer.StyleCredit);
            Assert.Contains("TYLER J. RAE", HoleIllustrationRenderer.StyleCredit);
            Assert.InRange(new FileInfo(result.PdfPath!).Length, 100_000, 8_000_000);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>The Fried Egg look turns the hole a quarter turn: play runs left to right, the back tee left of the green.</summary>
    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void FriedEgg_PlaysLeftToRightAndRendersTheSameTwice(int hole)
    {
        var a = Analyses[hole].Value;
        using var r = new FriedEggPlateRenderer(Options with { Look = IllustrationLook.FriedEgg });
        using var plate = r.Render(a);
        var tee = plate.Projection.Page(a.BackTee);
        var green = plate.Projection.Page(a.Green);
        Assert.True(green.X - tee.X > 600, $"hole {hole}: tee at {tee.X:0}, green at {green.X:0}");
        Assert.True(Math.Abs(green.Y - tee.Y) < 300, "the hole runs across the page");
        Assert.True(FriedEggPlateRenderer.ArtZone.Contains(tee) && FriedEggPlateRenderer.ArtZone.Contains(green), "tee and green inside the art");
        using var again = r.Render(a);
        using (var x = plate.Art.Encode(SKEncodedImageFormat.Png, 100))
        using (var y = again.Art.Encode(SKEncodedImageFormat.Png, 100))
            Assert.True(x.ToArray().AsSpan().SequenceEqual(y.ToArray()), $"two Fried Egg renders of hole {hole} differ");
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Build_FriedEgg_WritesItsOwnLandscapeFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"illustrations-{Guid.NewGuid():N}");
        try
        {
            var result = HoleIllustrationBuilder.Build(CassimboPath, dir, Options with { Look = IllustrationLook.FriedEgg }, new IllustrationOutputs { Pngs = true }, [5]);
            var png = Assert.Single(result.PngPaths);
            Assert.EndsWith(Path.Combine("Cassimbo - Hole Illustrations (Fried Egg)", "Hole_05 (Fried Egg).png"), png);
            Assert.EndsWith("Cassimbo - Hole Illustrations (Fried Egg).pdf", result.PdfPath);
            using (var img = SKBitmap.Decode(png))
            {
                Assert.Equal((4500, 3000), (img.Width, img.Height));   // 15 × 10 in, landscape
                Assert.Equal(new SKColor(0xf8, 0xe9, 0xb3), img.GetPixel(10, 10));   // yellow paper
            }
            var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(result.PdfPath!));
            Assert.Contains("/MediaBox [0 0 1080 720]", pdf);
            Assert.InRange(new FileInfo(result.PdfPath!).Length, 100_000, 10_000_000);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}