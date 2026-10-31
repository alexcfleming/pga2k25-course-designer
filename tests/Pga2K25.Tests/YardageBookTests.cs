using System.Text.RegularExpressions;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.Tests;

public class YardageBookTests
{
    static readonly string CassimboPath = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");
    static readonly Lazy<CourseModel> Course = new(() => CourseFile.Load(CassimboPath).ReadModel());
    static readonly Lazy<CourseLayers> Layers = new(() => CourseLayers.From(Course.Value));
    static readonly Lazy<RockFootprints> Rocks = new(() => new RockFootprints(Course.Value));

    static HoleAnalysis Analyze(int number) =>
        HoleAnalysis.Analyze(Course.Value, Layers.Value, Course.Value.Holes.Single(h => h.Number == number), new YardageOptions(),
            YardagePageRenderer.MapRect.Width / YardagePageRenderer.MapRect.Height, Rocks.Value);

    static void AssertLabel(HoleAnalysis a, bool front, int fromTee, int toGreen) =>
        Assert.True(a.BunkerLabels.Any(l => l.IsFront == front && Math.Abs(l.FromTeeYards - fromTee) <= 2 && Math.Abs(l.ToGreenYards - toGreen) <= 2),
            $"no {(front ? "front" : "carry")} label at {fromTee}/{toGreen}; have " +
            string.Join(", ", a.BunkerLabels.Select(l => $"{(l.IsFront ? "front" : "carry")} {l.FromTeeYards:0}/{l.ToGreenYards:0}")) +
            "; " + string.Join("; ", a.Diagnostics));

    [Fact]
    public void RockFootprints_AreEstimatedFromTheCourseAndOrderedBySize()
    {
        var report = Rocks.Value.Report;
        Assert.True(report["cluster"].Measured && report["stone"].Measured && report["rock"].Measured);
        Assert.True(report["cluster"].Radius > report["small"].Radius);
        Assert.True(report["small"].Radius > report["stone"].Radius);
        Assert.True(report["stone"].Radius > report["rock"].Radius);
        Assert.Equal("cluster", RockFootprints.Family("Assets/Rocks/Desert_Rock_Cluster_03.prefab"));
    }

    [Fact]
    public void Hole4_InPlayDividerGetsFrontAndCarry()
    {
        var a = Analyze(4);
        Assert.Equal(4, a.Hole.Number);
        Assert.Equal(380, a.LengthYards);                  // back tee length straight from the course file
        Assert.NotEmpty(a.InPlay);
        AssertLabel(a, front: true, 231, 123);
        AssertLabel(a, front: false, 308, 45);
    }

    [Fact]
    public void Hole11_OwnFairwayIsNotFadedAndCrossHazardIsLabelled()
    {
        var a = Analyze(11);
        Assert.Contains(a.Fairways, f => !a.Foreign.Contains(f));
        Assert.Contains(a.BunkerLabels, l => l.IsFront && Math.Abs(l.FromTeeYards - 272) <= 2);
        Assert.Contains(a.BunkerLabels, l => !l.IsFront && Math.Abs(l.FromTeeYards - 326) <= 2);
    }

    [Fact]
    public void EveryHole_HasTeeSetsLabelsAndAGreen()
    {
        foreach (var h in Course.Value.Holes)
        {
            var a = Analyze(h.Number);
            Assert.Equal(h.Tees.Count, a.TeeSets.Count);
            for (int i = 0; i < h.Tees.Count; i++)   // yardages come from the course file, front → back
                Assert.InRange(a.TeeSets[i].Yards - h.TeeSetLength(i) / HoleAnalysis.Yard, -1, 1);
            Assert.InRange(a.GreenDepthYards, 8, 60);
            if (a.LengthYards >= 200) Assert.NotEmpty(a.Spots);   // spot heights start 125 yds out, so short par 3s have none
        }
    }

    [Fact]
    public void Builder_WritesPortraitPdfWorkbookAndOptionalPngs()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-yardage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = YardageBookBuilder.Build(CassimboPath, dir, new YardageOptions(),
                new YardageBookOutputs { Pdf = true, Pngs = true, PngScale = 1 }, [1, 4, 17]);

            Assert.NotNull(result.PdfPath);
            string pdf = File.ReadAllText(result.PdfPath!, System.Text.Encoding.Latin1);
            Assert.Equal(3, Regex.Matches(pdf, @"/Type\s*/Page[^s]").Count);
            Assert.All(Regex.Matches(pdf, @"/MediaBox\s*\[([^\]]*)\]"), m => Assert.Equal("0 0 306 792", m.Groups[1].Value.Trim()));
            Assert.InRange(new FileInfo(result.PdfPath!).Length, 10_000, 3 * 2_000_000);   // vectors, simplified: well under 2 MB a page

            Assert.Equal(3, result.PngPaths.Count);
            Assert.All(result.PngPaths, p =>
            {
                using var codec = SKCodec.Create(p);
                Assert.Equal(425, codec.Info.Width);
                Assert.Equal(1100, codec.Info.Height);
            });
            Assert.EndsWith("Hole_17.png", result.PngPaths[^1]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Builder_PngOnly_WritesNoPdf()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-yardage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = YardageBookBuilder.Build(CassimboPath, dir, new YardageOptions(),
                new YardageBookOutputs { Pdf = false, Pngs = true, PngScale = 0.5f }, [2]);
            Assert.Null(result.PdfPath);
            Assert.Single(result.PngPaths);
            Assert.Empty(Directory.GetFiles(dir, "*.pdf"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Projection_SimplifiesDensePolylinesWithoutMovingCorners()
    {
        var proj = MapProjection.Fit([new(0, 0), new(100, 100)], new SKRect(0, 0, 100, 100), 0, 1);
        var line = Enumerable.Range(0, 1001).Select(i => new Vec2(i * 0.1, 0)).Append(new Vec2(100, 100)).ToList();
        using var path = proj.Path(line, false);
        Assert.Equal(3, path.PointCount);                  // start, the corner, the end
        Assert.Equal(proj.Map(new Vec2(100, 0)), path.GetPoint(1));
    }
    [Fact]
    public void PlantedAreas_JoinTheVegetationMasses_AndThePageHasRelief()
    {
        // Murray's trees are all planted areas (Eastern White Pine, no single trees): the holes beside them still
        // get canopy masses covering the areas, and every page keeps its terrain for the relief.
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "murray-estate.course")).ReadModel();
        var layers = CourseLayers.From(course);
        var analyses = course.Holes.Select(h => HoleAnalysis.Analyze(course, layers, h, new YardageOptions(),
            YardagePageRenderer.MapRect.Width / YardagePageRenderer.MapRect.Height)).ToList();
        Assert.All(analyses, a => Assert.NotNull(a.Field));
        var centres = course.PlacedAreas.Select(a => new Vec2(a.Outline.Average(q => q.X), a.Outline.Average(q => q.Z))).ToList();
        Assert.Contains(analyses, a => centres.Any(c => a.Vegetation.Any(v => Geometry.Contains(v, c))));
        Assert.All(analyses, a => Assert.Empty(a.Conifers));   // planted areas are masses, not crowns

        using var renderer = new YardagePageRenderer(new YardageOptions());
        using var bitmap = new SKBitmap((int)YardagePageRenderer.PageWidth, (int)YardagePageRenderer.PageHeight);
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, course.Name, analyses[0]);
    }
}
