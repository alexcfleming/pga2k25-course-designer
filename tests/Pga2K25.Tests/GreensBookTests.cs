using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using SkiaSharp;

namespace Pga2K25.Tests;

public class GreensBookTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(6, "+6\"")]
    [InlineData(-12, "-1'")]
    [InlineData(12, "+1'")]
    [InlineData(-16, "-1'4\"")]
    [InlineData(43, "+3'7\"")]
    [InlineData(0.4, "0")]
    public void FormatFeetInches_MatchesGameLabels(double inches, string expected) =>
        Assert.Equal(expected, GreenAnalyzer.FormatFeetInches(inches));

    [Fact]
    public void MarchingSquares_OnTiltedPlane_GivesOneStraightLine()
    {
        var field = new Heightfield(new Vec2(0, 0), 1, 20, 20);
        var values = new double[field.Values.Length];
        for (int z = 0; z < 20; z++)
            for (int x = 0; x < 20; x++)
                values[z * 20 + x] = x;   // rises along +X
        var mask = Enumerable.Repeat(true, values.Length).ToArray();

        var lines = MarchingSquares.Trace(field, values, mask, 7.5);

        var line = Assert.Single(lines);
        Assert.All(line, p => Assert.Equal(8.0, p.X, 6));   // value 7.5 lies between cell centres 7.5 and 8.5 m
        Assert.Equal(20, line.Count);                        // one point per cell-centre row

    }

    [Fact]
    public void FourierSmooth_KeepsACircleACircle()
    {
        var circle = Geometry.Ellipse(new Vec2(10, -5), new Vec2(1, 0), 8, 8, 200);
        var smooth = Geometry.FourierSmooth(circle, 8);
        Assert.All(smooth, p => Assert.Equal(8, Vec2.Distance(p, new Vec2(10, -5)), 2));
    }

    [Fact]
    public void Analyze_SplineGreen_IsPlausible()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "shinnecockhills.course")).ReadModel();
        var g = GreenAnalyzer.Analyze(course, course.Holes[0], new GreensBookOptions());

        Assert.False(g.OutlineEstimated);
        Assert.Equal(4, g.Pins.Count);
        Assert.InRange(g.AreaSquareMetres, 200, 1500);
        Assert.NotEmpty(g.Arrows);
        Assert.NotEmpty(g.Contours);
        Assert.InRange(g.MedianSlopePct, 0.1, 6);
    }

    [Fact]
    public void Build_WritesSheetStudiesAndPdf()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = GreensBookBuilder.Build(Path.Combine(TestPaths.SamplesDir, "shinnecockhills.course"), dir,
                new GreensBookOptions(), new GreensBookOutputs { GreenStudies = true, Pdf = true, SheetScale = 1 },
                holeNumbers: [1, 18]);

            Assert.Equal(2, result.Greens.Count);
            Assert.Equal("%PDF-"u8.ToArray(), File.ReadAllBytes(result.PdfPath!).Take(5).ToArray());
            using (var sheet = SKBitmap.Decode(result.SheetPath!))
                Assert.Equal(((int)GreensBookRenderer.SheetWidth, (int)GreensBookRenderer.SheetHeight), (sheet.Width, sheet.Height));
            Assert.Equal(2, result.StudyPaths.Count);
            using (var study = SKBitmap.Decode(result.StudyPaths[0]))
                Assert.Equal((3300, 2550), (study.Width, study.Height));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SlopePalette_ClampsAndInterpolates()
    {
        Assert.Equal(new SKColor(35, 112, 64), SlopePalette.Colour(-1));
        Assert.Equal(new SKColor(161, 35, 43), SlopePalette.Colour(20));
        Assert.Equal(new SKColor(160, 187, 63), SlopePalette.Colour(3.3));
        var mid = SlopePalette.Colour(0.625);   // halfway between the first two stops
        Assert.Equal(new SKColor(42, 126, 66), mid);
    }
}
