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
    static readonly Lazy<RockFootprints> Rocks = new(() => new RockFootprints(Samples.Cassimbo));
    static readonly IllustrationOptions Options = new() { Credit = "Test Designer", Year = 2026 };
    static readonly Dictionary<int, Lazy<HoleAnalysis>> Analyses = new[] { 1, 5, 7 }.ToDictionary(n => n, n => new Lazy<HoleAnalysis>(() =>
        HoleIllustrationBuilder.Analyze(Samples.Cassimbo, Layers.Value, Samples.Cassimbo.Holes.Single(h => h.Number == n), Options,
            HoleIllustrationBuilder.YardageFor(Options, new CourseHoles()), Rocks.Value)));
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
        Assert.Equal(["{0} - Hole Illustrations/*.png"], p.Pages);
        Assert.Equal(["{0} - Hole Illustrations.pdf"], p.Pdfs);
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
}
