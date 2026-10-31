using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

public sealed record ScorecardMapResult(IReadOnlyList<string> Files, double RotationDegrees, bool Landscape);

public static class ScorecardMapBuilder
{
    /// <summary>
    /// Writes "&lt;course&gt; - Scorecard Map (&lt;theme name&gt;).png" (and optionally a PDF) for each theme.
    /// PNG resolution: 300 dpi on an 11 × 8.5 in page (3300 × 2550 px) at the default scale.
    /// </summary>
    public static ScorecardMapResult Build(string coursePath, string outputDir, ScorecardMapOptions options,
        IEnumerable<MapThemeKind>? themes = null, bool pdf = false, float scale = 3, IProgress<string>? log = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        course.NotWater = NotWaterMarks.Points(outputDir);
        var place = CoursePlaces.Load(outputDir);
        if (options.PlaceLines.Count == 0) options = options with { PlaceLines = place.PlaceLines };
        if (options.Watermark is null && place.ShowsRegion && place.Watermark)
            options = options with { Watermark = RegionGenerator.Generate(RegionGenerator.Parse(place.Shape), place.Seed, OceanFinder.SeaBearing(place, course), place.SizeKm) };
        var layers = CourseLayers.From(course);
        if (course.HasWater)
            layers.AddInlandWater(GreensBook.WaterTracer.Trace(course, new GreensBook.TerrainModelOptions { CellMetres = 0.5 }));
        log?.Report($"{course.Name}: {layers.Grass.Count} grass areas, {layers.Greens.Count} greens, " +
                    $"{layers.BunkerAreas.Count + layers.BunkerRibbons.Count} bunker shapes + {layers.BunkerBrushes.Count} bunker brushes, " +
                    $"{layers.WasteAreas.Count + layers.WasteRibbons.Count} waste shapes + {layers.WasteBrushes.Count} waste brushes, " +
                    $"{layers.WaterAreas.Count + layers.WaterRibbons.Count} water shapes + {layers.WaterBodies.Count} ponds/creeks from water entries, " +
                    $"{layers.Paths.Count + layers.Roads.Count} paths/roads.");

        if (options.Data is { } holeData)
        {
            int fileSets = course.Holes.Max(h => Math.Max(1, h.Tees.Count));
            log?.Report($"Hole data: {holeData.Rows.Count} holes, tees {string.Join(" / ", holeData.TeeNames)}.");
            foreach (var h in course.Holes)
            {
                var row = holeData.For(h.Number);
                if (row is null) { log?.Report($"  hole {h.Number}: not in hole data (using course file)."); continue; }
                if (row.Par is int par && par != h.Par) log?.Report($"  hole {h.Number}: par {par} in hole data vs {h.Par} in course file (using course file).");
                var diffs = row.Yards.Select((y, i) => (y, i, file: (int)Math.Round(h.TeeSetLength(Math.Min(i, fileSets - 1)) / 0.9144)))
                    .Where(t => t.y is int v && Math.Abs(v - t.file) > 1)
                    .Select(t => $"{holeData.TeeNames.ElementAtOrDefault(t.i)} {t.y} vs {t.file}").ToList();
                if (diffs.Count > 0) log?.Report($"  hole {h.Number}: hole-data yards differ from course file: {string.Join(", ", diffs)} (using course file).");
            }
        }

        Directory.CreateDirectory(outputDir);
        string baseName = SafeFileName(string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name);
        var files = new List<string>();
        double rotation = 0; bool landscape = true;

        foreach (var theme in themes ?? [options.Theme])
        {
            var themed = options with { Theme = theme };
            using var renderer = new ScorecardMapRenderer(themed);
            var (page, land, rot) = renderer.Plan(layers);
            rotation = rot; landscape = land;

            string png = Path.Combine(outputDir, $"{baseName} - Scorecard Map ({MapTheme.For(theme).Name}).png");
            var info = new SKImageInfo((int)Math.Round(page.Width * scale), (int)Math.Round(page.Height * scale));
            using (var surface = SKSurface.Create(info))
            {
                surface.Canvas.Scale(scale);
                renderer.Draw(surface.Canvas, course.Name, course, layers);
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var f = File.Create(png);
                data.SaveTo(f);
            }
            files.Add(png);
            log?.Report($"{theme}: {(land ? "landscape" : "portrait")}, rotated {rot:0}°  →  {png}");

            if (pdf)
            {
                string pdfPath = Path.ChangeExtension(png, ".pdf");
                using var stream = File.Create(pdfPath);
                using var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
                {
                    Title = $"{course.Name} — Scorecard Map", Author = options.Credit, Creator = "PGA 2K25 Course Designer",
                });
                renderer.Draw(doc.BeginPage(page.Width * 72 / 100, page.Height * 72 / 100).Also(c => c.Scale(0.72f)), course.Name, course, layers);
                doc.EndPage();
                doc.Close();
                files.Add(pdfPath);
            }
        }
        return new ScorecardMapResult(files, rotation, landscape);
    }

    static SKCanvas Also(this SKCanvas canvas, Action<SKCanvas> act) { act(canvas); return canvas; }

    static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
