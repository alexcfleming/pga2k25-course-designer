using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.YardageBook;

/// <summary>What to write. The multi-page PDF workbook is the primary deliverable.</summary>
public sealed record YardageBookOutputs
{
    /// <summary>One PDF, one portrait 4.25 × 11 in page per hole, drawn as vectors (raster effects at 300 dpi).</summary>
    public bool Pdf { get; init; } = true;

    /// <summary>One PNG per hole ("&lt;course&gt; - Yardage Book/Hole_NN.png").</summary>
    public bool Pngs { get; init; }

    /// <summary>PNG resolution: pixels per page unit (100 units/in, so 3 = 300 dpi → 1275 × 3300 px).</summary>
    public float PngScale { get; init; } = 3;
}

public sealed record YardageBookResult(string? PdfPath, IReadOnlyList<string> PngPaths, IReadOnlyList<HoleAnalysis> Holes);

public static class YardageBookBuilder
{
    /// <summary>PDF points per page unit (page units are 1/100 in; PDF points are 1/72 in).</summary>
    const float PointsPerUnit = 0.72f;

    public static YardageBookResult Build(string coursePath, string outputDir, YardageOptions options,
        YardageBookOutputs? outputs = null, IEnumerable<int>? holeNumbers = null, IProgress<string>? log = null, bool verbose = false)
    {
        outputs ??= new YardageBookOutputs();
        var course = CourseFile.Load(coursePath).ReadModel();
        course.NotWater = NotWaterMarks.Points(outputDir);
        var layers = CourseLayers.From(course);
        var selected = holeNumbers?.ToHashSet();
        var holes = course.Holes.Where(h => selected is null || selected.Contains(h.Number)).ToList();
        if (holes.Count == 0) throw new InvalidOperationException("No matching holes found in the course.");

        double aspect = YardagePageRenderer.MapRect.Width / YardagePageRenderer.MapRect.Height;
        var rockSizes = new RockFootprints(course);
        log?.Report("Rock footprints (radius per unit scale): " + string.Join(", ", rockSizes.Report.Select(kv =>
            $"{kv.Key} {kv.Value.Radius:0.0} m ({(kv.Value.Measured ? $"from {kv.Value.Samples} rocks" : "default")})")));
        // Ponds and creeks once for the whole course: every page shares the same shorelines.
        var water = course.Water.Count > 0 ? GreensBook.WaterTracer.Trace(course, options.Terrain) : null;
        if (water is not null)
            log?.Report($"Water: {water.Bodies.Count} bod{(water.Bodies.Count == 1 ? "y" : "ies")} from {course.Water.Count} water entr{(course.Water.Count == 1 ? "y" : "ies")}.");
        var analyses = new List<HoleAnalysis>();
        foreach (var h in holes)
        {
            var a = HoleAnalysis.Analyze(course, layers, h, options, aspect, rockSizes, water);
            analyses.Add(a);
            log?.Report($"Hole {h.Number,2}: par {h.Par}, {a.LengthYards} yds, {a.BunkerLabels.Count} hazard numbers, " +
                        $"{a.Widths.Count} widths, {a.Spots.Count} spot heights, green {a.GreenDepthYards} deep, " +
                        $"tees {string.Join(" / ", a.TeeSets.Select(t => $"{t.Name} {t.Yards}"))}.");
            if (verbose) foreach (var d in a.Diagnostics) log?.Report("    " + d);
        }

        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        using var renderer = new YardagePageRenderer(options);

        string? pdfPath = null;
        if (outputs.Pdf)
        {
            pdfPath = Path.Combine(outputDir, $"{baseName} - Yardage Book.pdf");
            using (var stream = File.Create(pdfPath))
            using (var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            {
                Title = $"{course.Name} — Yardage Book",
                Author = options.Credit,
                Subject = $"{analyses.Count} hole(s), par {analyses.Sum(a => a.Hole.Par)}",
                Creator = "PGA 2K25 Course Designer",
                RasterDpi = 300,
                EncodingQuality = 100,
            }))
            {
                foreach (var a in analyses)
                {
                    // Portrait 4.25 × 11 in (306 × 792 pt).
                    var canvas = doc.BeginPage(YardagePageRenderer.PageWidth * PointsPerUnit, YardagePageRenderer.PageHeight * PointsPerUnit);
                    canvas.Scale(PointsPerUnit);
                    renderer.Draw(canvas, course.Name, a);
                    doc.EndPage();
                }
                doc.Close();
            }
            log?.Report($"Yardage book PDF ({analyses.Count} pages): {pdfPath}");
        }

        var pngs = new List<string>();
        if (outputs.Pngs)
        {
            string dir = Path.Combine(outputDir, $"{baseName} - Yardage Book");
            Directory.CreateDirectory(dir);
            var info = new SKImageInfo((int)(YardagePageRenderer.PageWidth * outputs.PngScale), (int)(YardagePageRenderer.PageHeight * outputs.PngScale));
            foreach (var a in analyses)
            {
                using var surface = SKSurface.Create(info);
                surface.Canvas.Scale(outputs.PngScale);
                renderer.Draw(surface.Canvas, course.Name, a);
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                string path = Path.Combine(dir, $"Hole_{a.Hole.Number:00}.png");
                using (var f = File.Create(path)) data.SaveTo(f);
                pngs.Add(path);
            }
            log?.Report($"Yardage book PNGs ({pngs.Count}, {info.Width} × {info.Height} px): {dir}");
        }
        return new YardageBookResult(pdfPath, pngs, analyses);
    }
}
