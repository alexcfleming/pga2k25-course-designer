using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.GreensBook;

/// <summary>What to write. The one-page sheet is the primary deliverable.</summary>
public sealed record GreensBookOutputs
{
    /// <summary>All greens on a single PNG page.</summary>
    public bool SheetPng { get; init; } = true;

    /// <summary>Pixels per logical unit for the sheet (1600×1000 logical → 4800×3000 px at 3).</summary>
    public float SheetScale { get; init; } = 3;

    /// <summary>One high-resolution study PNG per green.</summary>
    public bool GreenStudies { get; init; }

    /// <summary>Pixels per point for studies (792×612 pt → 3300×2550 px, i.e. 11×8.5 in at 300 dpi).</summary>
    public float StudyScale { get; init; } = 300f / 72f;

    /// <summary>PDF: sheet page followed by one study page per green.</summary>
    public bool Pdf { get; init; }
}

public sealed record GreensBookResult(string? SheetPath, IReadOnlyList<string> StudyPaths, string? PdfPath, IReadOnlyList<GreenAnalysis> Greens);

public static class GreensBookBuilder
{
    public static GreensBookResult Build(string coursePath, string outputDir, GreensBookOptions options,
        GreensBookOutputs? outputs = null, IEnumerable<int>? holeNumbers = null, IProgress<string>? log = null)
    {
        outputs ??= new GreensBookOutputs();
        var course = CourseFile.Load(coursePath).ReadModel();
        var selected = holeNumbers?.ToHashSet();
        var holes = course.Holes.Where(h => selected is null || selected.Contains(h.Number)).ToList();
        if (holes.Count == 0) throw new InvalidOperationException("No matching holes found in the course.");

        log?.Report($"{course.Name}: {holes.Count} hole(s), {course.HeightStamps.Count} terrain stamps " +
                    $"(flattenGreens={course.FlattenGreens}, blurHeight={course.BlurHeight}, hardness={options.Terrain.Hardness:0.00}).");

        var greens = new List<GreenAnalysis>();
        foreach (var hole in holes)
        {
            var g = GreenAnalyzer.Analyze(course, hole, options);
            greens.Add(g);
            log?.Report($"Hole {hole.Number,2}: {(g.OutlineEstimated ? "estimated" : "spline")} outline, " +
                        $"falls {g.FallInches:0.#} in., median {g.MedianSlopePct:0.0}% / p95 {g.P95SlopePct:0.0}%, " +
                        $"{g.Arrows.Count} arrows, {g.Pins.Count} pin(s)" +
                        (g.Warnings.Any(w => w.StartsWith("Implausibly")) ? "  ⚠ implausibly steep" : ""));
        }

        Directory.CreateDirectory(outputDir);
        string baseName = SafeFileName(string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name);
        using var renderer = new GreensBookRenderer(options);
        log?.Report($"Font: {renderer.FontFamily}");

        string? sheetPath = null;
        if (outputs.SheetPng)
        {
            sheetPath = Path.Combine(outputDir, $"{baseName} - Greens Book.png");
            WritePng(sheetPath, GreensBookRenderer.SheetWidth, GreensBookRenderer.SheetHeight, outputs.SheetScale,
                c => renderer.DrawSheet(c, course.Name, greens));
            log?.Report($"Sheet: {sheetPath}");
        }

        var studies = new List<string>();
        if (outputs.GreenStudies)
        {
            string dir = Path.Combine(outputDir, $"{baseName} - Green Studies");
            Directory.CreateDirectory(dir);
            foreach (var g in greens)
            {
                string path = Path.Combine(dir, $"Hole_{g.Hole.Number:00}.png");
                WritePng(path, GreensBookRenderer.PageWidth, GreensBookRenderer.PageHeight, outputs.StudyScale,
                    c => renderer.DrawHolePage(c, course.Name, g));
                studies.Add(path);
            }
            log?.Report($"Green studies: {dir}");
        }

        string? pdfPath = null;
        if (outputs.Pdf)
        {
            pdfPath = Path.Combine(outputDir, $"{baseName} - Greens Book.pdf");
            using var stream = File.Create(pdfPath);
            using var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            {
                Title = $"{course.Name} — Greens Book",
                Author = options.Credit,
                Creator = "PGA 2K25 Course Designer",
                RasterDpi = 300,
            });
            renderer.DrawSheet(doc.BeginPage(GreensBookRenderer.PageWidth, GreensBookRenderer.PageHeight), course.Name, greens,
                GreensBookRenderer.PageWidth, GreensBookRenderer.PageHeight);
            doc.EndPage();
            foreach (var g in greens)
            {
                renderer.DrawHolePage(doc.BeginPage(GreensBookRenderer.PageWidth, GreensBookRenderer.PageHeight), course.Name, g);
                doc.EndPage();
            }
            doc.Close();
            log?.Report($"PDF: {pdfPath}");
        }

        return new GreensBookResult(sheetPath, studies, pdfPath, greens);
    }

    static void WritePng(string path, float width, float height, float scale, Action<SKCanvas> draw)
    {
        var info = new SKImageInfo((int)Math.Round(width * scale), (int)Math.Round(height * scale));
        using var surface = SKSurface.Create(info);
        surface.Canvas.Scale(scale);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
