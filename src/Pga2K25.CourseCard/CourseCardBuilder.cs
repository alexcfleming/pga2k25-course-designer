using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed record CourseCardResult(CourseCardData Data, IReadOnlyList<CardIssue> Issues, string? OceanSide, IReadOnlyList<string> Files);

public static class CourseCardBuilder
{
    /// <summary>
    /// Validates, then writes the compass rose, hole data card, course data card and (when the course has drawn greens)
    /// the green size card in each configured format,
    /// plus "&lt;course&gt; - Card Validation.txt". ERRORs abort before anything is drawn; nothing is ever corrected.
    /// </summary>
    /// <param name="force">Draw even with ERRORs (drafts only): the errors are still reported and written to the validation file.</param>
    public static CourseCardResult Build(string coursePath, string outputDir, CourseCardConfig config,
        string? workbookPath = null, IProgress<string>? log = null, bool force = false)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        // The card settings file wins; the course's Place.json fills what it leaves unset.
        var place = CoursePlaces.Load(outputDir);
        config = config with
        {
            Description = string.IsNullOrWhiteSpace(config.Description) && place.Description.Trim().Length > 0 ? place.Description.Trim() : config.Description,
            OceanSide = config.OceanSide ?? place.SeaSide,
            WindFromDegrees = config.WindFromDegrees ?? place.WindFromDegrees,
        };
        var workbook = workbookPath is null ? null : HoleData.Load(workbookPath);
        var issues = new List<CardIssue>();
        var data = CourseCardData.From(course, workbook, config.TeeNames, issues);
        issues.AddRange(data.Validate(workbook));

        string? ocean = config.OceanSide switch
        {
            null => OceanFinder.SeaBearing(course) is int b ? CourseCardConfig.CompassName(b) : null,
            var s when s.Equals("none", StringComparison.OrdinalIgnoreCase) => null,
            var s => s.ToUpperInvariant(),
        };
        if (config.OceanSide is null)
            issues.Add(new(Severity.Info, ocean is null ? "No sea found on the plot; no ocean label." : $"Ocean side detected from the surf props: {ocean}."));
        bool greenCard = data.Greens.Built.Count > 0;
        if (!greenCard)
            issues.Add(new(Severity.Info, "No drawn green splines on the course; the Green Size Card is not drawn."));
        else if (data.Greens.Built.Count < data.Greens.Greens.Count)
            issues.Add(new(Severity.Info, $"Green Size Card: holes {string.Join(", ", data.Greens.Greens.Where(g => g.Estimated).Select(g => g.Hole))} " +
                                          "have no green spline (estimated outline); left out of the green size figures."));
        if (config.WindFromDegrees is null)
            issues.Add(new(Severity.Info, "No prevailing wind set (the course file has none); no wind arrow drawn."));

        foreach (var i in issues) log?.Report(i.ToString());
        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat(data.CourseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string report = Path.Combine(outputDir, $"{baseName} - Card Validation.txt");
        File.WriteAllLines(report, issues.Select(i => i.ToString()).DefaultIfEmpty("No issues."));
        if (issues.Any(i => i.Severity == Severity.Error) && !force)
            throw new InvalidDataException($"Card build stopped: {issues.Count(i => i.Severity == Severity.Error)} error(s); see {report}. Nothing was corrected.");

        var files = new List<string>();
        using var renderer = new CourseCardRenderer(data, config, ocean);
        var pages = new List<(string Name, SKSize Size, Action<SKCanvas> Draw)>
        {
            ("Compass Rose", CourseCardRenderer.RosePage, renderer.DrawRosePage),
            ("Hole Data Card", CourseCardRenderer.TablePage, renderer.DrawTablePage),
            ("Course Data Card", CourseCardRenderer.CardPage, renderer.DrawCoursePage),
        };
        if (greenCard) pages.Add(("Green Size Card", CourseCardRenderer.CardPage, renderer.DrawGreenSizePage));
        pages = pages.Select(p => (p.Name, p.Size, (Action<SKCanvas>)(c => { p.Draw(c); renderer.DrawAttribution(c, p.Size); }))).ToList();
        var formats = config.Formats.Select(f => f.ToLowerInvariant()).ToHashSet();
        foreach (var (name, size, draw) in pages)
        {
            string stem = Path.Combine(outputDir, $"{baseName} - {name}");
            if (formats.Contains("png"))
            {
                float scale = (float)(config.Dpi / 72);
                var info = new SKImageInfo((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
                using var surface = SKSurface.Create(info);
                surface.Canvas.Scale(scale);
                draw(surface.Canvas);
                using var image = surface.Snapshot();
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using (var f = File.Create(stem + ".png")) png.SaveTo(f);
                files.Add(stem + ".png");
            }
            if (formats.Contains("pdf"))
            {
                using var stream = File.Create(stem + ".pdf");
                using var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
                {
                    Title = $"{data.CourseName} — {name}", Creator = "PGA 2K25 Course Designer", RasterDpi = (float)config.Dpi,
                });
                draw(doc.BeginPage(size.Width, size.Height));
                doc.EndPage();
                doc.Close();
                files.Add(stem + ".pdf");
            }
            if (formats.Contains("svg"))
            {
                using (var stream = File.Create(stem + ".svg"))
                using (var svg = SKSvgCanvas.Create(SKRect.Create(size), stream))
                    draw(svg);
                files.Add(stem + ".svg");
            }
        }
        log?.Report($"Course cards ({files.Count} files, {renderer.FontFamily}): {outputDir}");
        return new CourseCardResult(data, issues, ocean, files);
    }
}
