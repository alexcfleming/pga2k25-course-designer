using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>What to write: the PDF (one plate per page) is the primary deliverable.</summary>
public sealed record IllustrationOutputs
{
    public bool Pdf { get; init; } = true;

    /// <summary>One 2550 × 3300 px PNG per hole ("&lt;course&gt; - Hole Illustrations/Hole_NN.png").</summary>
    public bool Pngs { get; init; }

    /// <summary>JPEG quality of the art raster embedded in the PDF (the type stays vector).</summary>
    public int PdfArtQuality { get; init; } = 90;
}

public sealed record IllustrationResult(string? PdfPath, IReadOnlyList<string> PngPaths, IReadOnlyList<HoleAnalysis> Holes);

public static class HoleIllustrationBuilder
{
    /// <summary>PDF points per page unit (page units are 1/100 in; PDF points are 1/72 in).</summary>
    const float PointsPerUnit = 0.72f;

    /// <summary>The yardage book's options for a plate: more room behind the tee and beyond the green, and tee names.</summary>
    public static YardageOptions YardageFor(IllustrationOptions o, CourseHoles? holes) =>
        new() { MetresBehindTee = 30, MetresBeyondGreen = 45, Holes = holes, Credit = o.Credit };

    /// <summary>
    /// The hole analysis at the plate's own scale: the window is widened (and lengthened past the green) to cover the
    /// whole art zone, so neighbouring shapes fade out instead of stopping at a window edge.
    /// </summary>
    public static HoleAnalysis Analyze(CourseModel course, CourseLayers layers, Hole hole, IllustrationOptions o, YardageOptions yardage,
        RockFootprints? rocks = null, GreensBook.WaterMap? water = null)
    {
        int set = Math.Max(0, hole.Tees.Count - 1);
        int yards = (int)Math.Round(hole.TeeSetLength(set) / HoleAnalysis.Yard);
        float upm = (float)(PlateLayout.UnitsPerYard(PlateLayout.RulerTop(yards, o.RulerMarginYards, o.RulerStepYards)) / HoleAnalysis.Yard);
        var back = hole.Tees.Count > 0 ? hole.Tees[^1] : hole.Route[0];
        double length = Vec2.Distance(back, hole.GreenAnchor);
        var zone = PlateLayout.ArtZone;
        double beyond = Math.Max(yardage.MetresBeyondGreen, (PlateLayout.ZeroY - zone.Top) / upm - length + 10);
        double behind = Math.Max(yardage.MetresBehindTee, (zone.Bottom - PlateLayout.ZeroY) / upm + 10);
        var fwd = (hole.GreenAnchor - back).Normalized();
        var side = new Vec2(fwd.Z, -fwd.X);
        var lateral = new[] { back }.Concat(hole.Route.Skip(1)).Select(p => (p - back).X * side.X + (p - back).Z * side.Z).ToList();
        double spread = lateral.Max() - lateral.Min();
        double windowWidth = zone.Width / upm + spread + 20;
        double aspect = windowWidth / (length + beyond + behind);
        yardage = yardage with { MetresBeyondGreen = beyond, MetresBehindTee = behind };
        return HoleAnalysis.Analyze(course, layers, hole, yardage, aspect, rocks, water);
    }

    public static IllustrationResult Build(string coursePath, string outputDir, IllustrationOptions options,
        IllustrationOutputs? outputs = null, IEnumerable<int>? holeNumbers = null, IProgress<string>? log = null, bool verbose = false)
    {
        outputs ??= new IllustrationOutputs();
        if (options.Holes is null) options = options with { Holes = CourseHolesFile.Load(outputDir) };
        var course = CourseFile.Load(coursePath).ReadModel();
        course.NotWater = NotWaterMarks.Points(outputDir);
        var layers = CourseLayers.From(course);
        var selected = holeNumbers?.ToHashSet();
        var holes = course.Holes.Where(h => selected is null || selected.Contains(h.Number)).ToList();
        if (holes.Count == 0) throw new InvalidOperationException("No matching holes found in the course.");

        var yardage = YardageFor(options, options.Holes);
        var rocks = new RockFootprints(course);
        var water = course.HasWater ? GreensBook.WaterTracer.Trace(course, yardage.Terrain) : null;
        var waste = options.Waste ? new WasteShapes(layers.WasteAreas, layers.WasteRibbons, layers.WasteBrushes) : null;

        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        using var renderer = new HoleIllustrationRenderer(options, log);

        string? pdfPath = outputs.Pdf ? Path.Combine(outputDir, $"{baseName} - Hole Illustrations.pdf") : null;
        string pngDir = Path.Combine(outputDir, $"{baseName} - Hole Illustrations");
        if (outputs.Pngs) Directory.CreateDirectory(pngDir);
        var pngs = new List<string>();
        var analyses = new List<HoleAnalysis>();

        // Plates are rendered one at a time (each art raster is ~20 MB of floats) and written as they're done.
        Stream? stream = pdfPath is null ? null : File.Create(pdfPath);
        SKDocument? doc = stream is null ? null : SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Title = $"{course.Name} — Hole Illustrations",
            Author = options.Credit,
            Subject = $"{holes.Count} hole plate(s), par {holes.Sum(h => h.Par)}",
            Creator = "PGA 2K25 Course Designer",
            RasterDpi = 300,
            EncodingQuality = outputs.PdfArtQuality,
        });
        try
        {
            foreach (var h in holes)
            {
                var a = Analyze(course, layers, h, options, yardage, rocks, water);
                analyses.Add(a);
                using var plate = renderer.Render(a, options.Holes?.NameOf(h.Number), waste);
                log?.Report($"Hole {h.Number,2}: par {h.Par}, {a.LengthYards} yds, ruler to {plate.RulerTop} yds" +
                            (plate.HoleName is { } n ? $", \"{n}\"" : "") + $", tees {string.Join(" / ", a.TeeSets.Select(t => $"{t.Name} {t.Yards}"))}.");
                if (verbose) log?.Report($"    back tee at y {plate.BackTee.Y:0.0} (0 YDS. at {PlateLayout.ZeroY}), green at y {plate.Green.Y:0.0}");

                if (doc is not null)
                {
                    // The art goes in as a JPEG so an 18-plate book stays a sensible size; the type stays vector.
                    using var jpeg = plate.Art.Encode(SKEncodedImageFormat.Jpeg, outputs.PdfArtQuality);
                    using var art = SKImage.FromEncodedData(jpeg);
                    var canvas = doc.BeginPage(PlateLayout.PageWidth * PointsPerUnit, PlateLayout.PageHeight * PointsPerUnit);
                    canvas.Scale(PointsPerUnit);
                    renderer.DrawPage(canvas, course.Name, new Plate
                    {
                        Analysis = plate.Analysis, Projection = plate.Projection, RulerTop = plate.RulerTop, UnitsPerYard = plate.UnitsPerYard,
                        HoleName = plate.HoleName, Art = art, Darkness = plate.Darkness,
                    });
                    doc.EndPage();
                }

                if (outputs.Pngs)
                {
                    var info = new SKImageInfo((int)(PlateLayout.PageWidth * PlateLayout.RasterScale), (int)(PlateLayout.PageHeight * PlateLayout.RasterScale));
                    using var surface = SKSurface.Create(info);
                    surface.Canvas.Scale(PlateLayout.RasterScale);
                    renderer.DrawPage(surface.Canvas, course.Name, plate);
                    using var image = surface.Snapshot();
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    string path = Path.Combine(pngDir, $"Hole_{h.Number:00}.png");
                    using (var f = File.Create(path)) data.SaveTo(f);
                    pngs.Add(path);
                }
            }
            doc?.Close();
        }
        finally
        {
            doc?.Dispose();
            stream?.Dispose();
        }
        if (pdfPath is not null) log?.Report($"Hole illustrations PDF ({analyses.Count} plates, {new FileInfo(pdfPath).Length / 1048576.0:0.0} MB): {pdfPath}");
        if (outputs.Pngs) log?.Report($"Hole illustration PNGs ({pngs.Count}, 2550 × 3300 px): {pngDir}");
        return new IllustrationResult(pdfPath, pngs, analyses);
    }
}
