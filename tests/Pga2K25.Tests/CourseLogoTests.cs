using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.Tests;

[Trait("Area", "Products")]
public class CourseLogoTests
{
    static readonly SKColor LogoRed = new(0xE0, 0x10, 0x30);

    /// <summary>A temporary output folder; deleted when the test ends.</summary>
    sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pga2k25-logo-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }

    /// <summary>A square PNG with a solid red disc on a transparent ground, standing in for a designer's seal.</summary>
    static string LogoFile(string dir, string name = "seal.png")
    {
        string path = Path.Combine(dir, name);
        using var surface = SKSurface.Create(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using (var red = new SKPaint { Color = LogoRed, IsAntialias = true }) surface.Canvas.DrawCircle(100, 100, 96, red);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    static int Reds(string png)
    {
        using var bmp = SKBitmap.Decode(png);
        return bmp.Pixels.Count(c => Math.Abs(c.Red - LogoRed.Red) + Math.Abs(c.Green - LogoRed.Green) + Math.Abs(c.Blue - LogoRed.Blue) < 30);
    }

    [Fact]
    public void Pick_KeepsTheToneThenTheKind()
    {
        var set = new CourseLogos { Files = { ["seal-colour"] = "a.png", ["mark-ink"] = "b.png" } };
        Assert.Equal((LogoKind.Seal, LogoTone.Colour, "a.png"), set.Pick(LogoKind.Seal, LogoTone.Colour));
        Assert.Equal((LogoKind.Seal, LogoTone.Colour, "a.png"), set.Pick(LogoKind.Seal, LogoTone.Ink));     // no ink seal: the colour seal
        Assert.Equal((LogoKind.Mark, LogoTone.Ink, "b.png"), set.Pick(LogoKind.Mark, LogoTone.Ink));
        Assert.Equal((LogoKind.Mark, LogoTone.Ink, "b.png"), set.Pick(LogoKind.Mark, LogoTone.Colour));     // the mark's own tone before the seal
        Assert.Equal((LogoKind.Seal, LogoTone.Colour, "a.png"), set.Pick(LogoKind.Lockup, LogoTone.Colour)); // no lockup: the seal
        Assert.Equal((LogoKind.Seal, LogoTone.Colour, "a.png"), set.Pick(LogoKind.Seal, LogoTone.Light));   // light never falls back to ink
        Assert.Null(new CourseLogos().Pick(LogoKind.Seal, LogoTone.Colour));
    }

    [Fact]
    public void Import_CopiesIntoTheLogosFolder_AndRemoveDeletes()
    {
        using var dir = new TempDir();
        string source = LogoFile(dir.Path, "My Seal.PNG");
        var set = CourseLogosFile.Import(dir.Path, LogoKind.Seal, LogoTone.Ink, source);
        Assert.Equal("seal-ink.png", set.File(LogoKind.Seal, LogoTone.Ink));
        Assert.True(File.Exists(Path.Combine(dir.Path, "Logos", "seal-ink.png")));
        Assert.Equal("seal-ink.png", CourseLogosFile.Load(dir.Path).File(LogoKind.Seal, LogoTone.Ink));

        CourseLogosFile.Remove(dir.Path, LogoKind.Seal, LogoTone.Ink);
        Assert.False(File.Exists(Path.Combine(dir.Path, "Logos", "seal-ink.png")));
        Assert.True(CourseLogosFile.Load(dir.Path).IsEmpty);
    }

    [Fact]
    public void Load_DropsSlotsWhoseFileHasGone()
    {
        using var dir = new TempDir();
        CourseLogosFile.Import(dir.Path, LogoKind.Mark, LogoTone.Colour, LogoFile(dir.Path));
        File.Delete(Path.Combine(dir.Path, "Logos", "mark-colour.png"));
        Assert.True(CourseLogosFile.Load(dir.Path).IsEmpty);
        using var art = CourseLogoArt.Load(dir.Path);
        Assert.Null(art.Get(LogoKind.Mark, LogoTone.Colour));
    }

    /// <summary>The logo on built products (scorecard maps, yardage and greens books). A nested class runs in parallel with the rest of CourseLogoTests.</summary>
    [Trait("Area", "Products")]
    public class OnProducts
    {
        [Trait("Speed", "Slow")]
        [Fact]
        public void ScorecardMaps_PrintTheSealOnEveryTheme()
        {
            using var dir = new TempDir();
            string course = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
            var themes = new[] { MapThemeKind.Vintage, MapThemeKind.Modern, MapThemeKind.PlanSheet };
            var plain = ScorecardMapBuilder.Build(course, Path.Combine(dir.Path, "plain"), new ScorecardMapOptions(), themes, scale: 1);
            // Only a colour seal: the engraved styles, asking for ink, fall back to it.
            string withLogo = Path.Combine(dir.Path, "logo");
            CourseLogosFile.Import(withLogo, LogoKind.Seal, LogoTone.Colour, LogoFile(dir.Path));
            var logo = ScorecardMapBuilder.Build(course, withLogo, new ScorecardMapOptions(), themes, scale: 1);
            for (int i = 0; i < themes.Length; i++)
            {
                Assert.Equal(0, Reds(plain.Files[i]));
                Assert.True(Reds(logo.Files[i]) > 1500, $"{themes[i]}: seal drawn");
            }
        }

        [Trait("Speed", "Slow")]
        [Fact]
        public void YardageAndGreensBooks_LeadWithTheCourseLogo()
        {
            using var dir = new TempDir();
            CourseLogosFile.Import(dir.Path, LogoKind.Mark, LogoTone.Colour, LogoFile(dir.Path));
            var yardage = YardageBookBuilder.Build(Path.Combine(TestPaths.SamplesDir, "cassimbo.course"), dir.Path, new YardageOptions(),
                new YardageBookOutputs { Pdf = false, Pngs = true }, [1]);
            Assert.True(Reds(yardage.PngPaths[0]) > 2000, "yardage header mark");
            var greens = GreensBookBuilder.Build(Path.Combine(TestPaths.SamplesDir, "shinnecockhills.course"), dir.Path, new GreensBookOptions(),
                new GreensBookOutputs { SheetPng = true, SheetScale = 1 }, [1, 2]);
            // The greens sheet asks for the seal and gets the mark in its place.
            Assert.True(Reds(greens.SheetPath!) > 1000, "greens sheet seal");
        }
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void ReadinessRecord_EmbedsTheMark()
    {
        string html = Readiness.ReadinessRecord.Html(SampleSnapshot(), "data:image/png;base64,AAAA");
        Assert.Contains("<img class=\"mark\" src=\"data:image/png;base64,AAAA\"", html);
        Assert.DoesNotContain("class=\"mark\"", Readiness.ReadinessRecord.Html(SampleSnapshot()).Replace(".mark{", ""));
    }

    static Readiness.ReadinessRecord.Snapshot SampleSnapshot()
    {
        var report = Samples.CassimboReadiness();   // measured once per run, shared with the readiness tests
        var state = new Readiness.ReadinessState();
        return new Readiness.ReadinessRecord.Snapshot(report.CourseName, "cassimbo.course", "stamp", new DateTime(2026, 11, 1), false, [], [], report, state);
    }
}
