using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

public sealed record TerrainMapResult(string PngPath, SKSizeI Size, TerrainSurvey Survey);

public static class TerrainMapBuilder
{
    /// <summary>Writes "&lt;course&gt; - Terrain Map.png" to <paramref name="outputDir"/>.</summary>
    public static TerrainMapResult Build(string coursePath, string outputDir, TerrainMapOptions options, IProgress<string>? log = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();

        var survey = TerrainSurvey.Build(course, options);
        log?.Report($"Terrain: {course.HeightStamps.Count:N0} stamps replayed over {(survey.Max.X - survey.Min.X):0} × {(survey.Max.Z - survey.Min.Z):0} m " +
                    $"at {options.Terrain.CellMetres:0.##} m; relief {survey.MaxFeet:0} ft.");
        log?.Report($"Contours: every {survey.IntervalFeet:0.##} ft ({survey.Contours.Count(c => c.Index)} index / {survey.Contours.Count:N0} lines), " +
                    $"{survey.Summits.Count} hilltop spot heights.");
        log?.Report(survey.SeaLevelMetres is { } sea
            ? $"Sea level {sea:0.##} m (ocean props); shoreline traced in {survey.Shoreline.Count} piece(s){ShoreCheck(survey)}; elevations above sea level."
            : "No sea on the sheet; elevations above the lowest point.");
        if (course.WaterBrushes.Count > 0) log?.Report(PaintedWaterNote(survey));
        if (survey.Holes.Count == 0)
            log?.Report($"No holes routed: whole plot mapped; landmarks: {string.Join(", ", survey.Landmarks.Where(l => !l.Minor).Select(l => l.Name).DefaultIfEmpty("none"))}.");
        foreach (var h in survey.Holes)
            log?.Report($"Hole {h.Hole.Number,2}: par {h.Hole.Par}, {h.Yards} yds, tee {h.TeeFeet:0} ft → green {h.GreenFeet:0} ft " +
                        $"({h.ChangeFeet:+0;-0;0} ft), {SiteNote(h)}.");

        using var renderer = new TerrainMapRenderer(options);
        var size = renderer.PageSize(survey);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Could not allocate a {size.Width} × {size.Height} px image; lower the resolution.");
        renderer.Draw(surface.Canvas, survey);

        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(outputDir, $"{baseName} - Terrain Map.png");
        // Lossless, maximum zlib effort: these sheets are large and mostly smooth tint and fine lines.
        using (var pixmap = surface.PeekPixels())
        using (var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9)))
        using (var f = File.Create(path))
            data.SaveTo(f);
        log?.Report($"Terrain map ({size.Width:N0} × {size.Height:N0} px): {path}");
        return new TerrainMapResult(path, size, survey);
    }

    internal static string SiteNote(HoleRouting h) =>
        $"{(h.GreenSiteIsDrawn ? "drawn green" : "green site")} {h.GreenSiteGradePct:0.0}% tilt, ±{h.GreenSiteReliefFeet:0.0} ft";

    /// <summary>The painted water's level(s) and how much of it is wet: a check that the brushes sit below the terrain.</summary>
    static string PaintedWaterNote(TerrainSurvey s)
    {
        var levels = s.Course.WaterBrushes.Select(b => Math.Round((b.Value - s.DatumMetres) * TerrainSurvey.FeetPerMetre)).Distinct().Order().ToList();
        double area = s.PaintedWater.Count(w => w) * s.Field.Cell * s.Field.Cell;
        string at = $"{s.Course.WaterBrushes.Count} water brush(es) at {string.Join(", ", levels.Select(l => $"{l:0}"))} ft";
        return area > 0
            ? $"Painted water: {at}; {area:N0} m² below that level drawn as water."
            : $"Painted water: {at}, but the modelled ground under them is all above that level, so none is drawn.";
    }

    /// <summary>How far the on-sheet surf props sit from the modelled shoreline: a check on the terrain replay.</summary>
    static string ShoreCheck(TerrainSurvey s)
    {
        var pts = s.Shoreline.SelectMany(l => l).Where((_, i) => i % 4 == 0).ToList();
        var props = s.Course.PlacedObjects
            .Where(p => p.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase)
                        && p.Position.X >= s.Min.X && p.Position.X <= s.Max.X && p.Position.Z >= s.Min.Z && p.Position.Z <= s.Max.Z)
            .Select(p => (Distance: pts.Count == 0 ? 0 : pts.Min(q => Vec2.Distance(p.Position, q)), Wet: s.IsWater(s.Field.Sample(p.Position))))
            .OrderBy(p => p.Distance).ToList();
        return props.Count == 0 || pts.Count == 0 ? ""
            : $" ({props.Count} surf props on the sheet, {props.Count(p => p.Wet)} offshore; median {props[props.Count / 2].Distance:0} m " +
              $"from the line, worst {props[^1].Distance:0} m)";
    }
}

public sealed record HoleSheetResult(IReadOnlyList<string> PngPaths, string? PdfPath, IReadOnlyList<HoleSheet> Sheets);

public static class HoleSheetBuilder
{
    /// <summary>
    /// Writes one Letter-size terrain sketch sheet per selected hole: PNGs in "&lt;course&gt; - Hole Sheets/"
    /// and a PDF with one page per hole ("&lt;course&gt; - Hole Sheets.pdf"), both print at true scale.
    /// </summary>
    public static HoleSheetResult Build(string coursePath, string outputDir, HoleSheetOptions options,
        IEnumerable<int>? holeNumbers = null, IProgress<string>? log = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        var selected = holeNumbers?.ToHashSet();
        var holes = course.Holes.Where(h => selected is null || selected.Contains(h.Number)).ToList();
        if (holes.Count == 0) throw new InvalidOperationException("No matching holes found in the course.");
        var layers = Maps.CourseLayers.From(course);

        // Same 0 ft as the course terrain map: sea level, or the lowest ground of the routing window.
        var plot = TerrainSurvey.Build(course, options.Map with
        {
            Terrain = options.Map.Terrain with { CellMetres = 4 }, ShowSummits = false, ContourIntervalFeet = 100,
        });
        double datum = plot.DatumMetres;

        var sheets = new List<HoleSheet>();
        foreach (var h in holes)
        {
            var sheet = HoleSheet.Build(course, layers, h, options, datum, datumIsSeaLevel: plot.SeaLevelMetres is not null);
            sheets.Add(sheet);
            var r = sheet.Routing;
            log?.Report($"Hole {h.Number,2}: par {h.Par}, {r.Yards} yds, sheet {(sheet.Along.To - sheet.Along.From) * TerrainSurvey.YardsPerMetre:0} × " +
                        $"{(sheet.Across.To - sheet.Across.From) * TerrainSurvey.YardsPerMetre:0} yds, {sheet.Survey.IntervalFeet:0.##} ft contours, " +
                        $"tee {r.TeeFeet:0} → green {r.GreenFeet:0} ft, {TerrainMapBuilder.SiteNote(r)}.");
        }

        string baseName = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        Directory.CreateDirectory(outputDir);
        using var renderer = new HoleSheetRenderer(options);

        var pngs = new List<string>();
        if (options.Png)
        {
            string dir = Path.Combine(outputDir, $"{baseName} - Hole Sheets");
            Directory.CreateDirectory(dir);
            var size = renderer.PixelSize;
            foreach (var sheet in sheets)
            {
                using var surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                renderer.Draw(surface.Canvas, course.Name, sheet);
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                string path = Path.Combine(dir, $"Hole_{sheet.Routing.Hole.Number:00}.png");
                using (var f = File.Create(path)) data.SaveTo(f);
                pngs.Add(path);
            }
            log?.Report($"Hole sheet PNGs ({pngs.Count}, {size.Width} × {size.Height} px = 8.5 × 11 in at {options.Dpi:0} dpi): {dir}");
        }

        string? pdfPath = null;
        if (options.Pdf)
        {
            pdfPath = Path.Combine(outputDir, $"{baseName} - Hole Sheets.pdf");
            using var stream = File.Create(pdfPath);
            using var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            {
                Title = $"{course.Name} — Hole terrain sheets",
                Author = options.Map.Credit,
                Creator = "PGA 2K25 Course Designer",
                RasterDpi = (float)options.Dpi,
                EncodingQuality = 100,
            });
            foreach (var sheet in sheets)
            {
                var canvas = doc.BeginPage(HoleSheet.PageWidth, HoleSheet.PageHeight);
                canvas.Scale((float)(72 / options.Dpi));   // the renderer works in pixels at Dpi
                renderer.Draw(canvas, course.Name, sheet);
                doc.EndPage();
            }
            doc.Close();
            log?.Report($"Hole sheet PDF ({sheets.Count} Letter page(s)): {pdfPath}");
        }
        return new HoleSheetResult(pngs, pdfPath, sheets);
    }
}
