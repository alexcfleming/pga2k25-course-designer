using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed record CourseCardResult(CourseCardData Data, IReadOnlyList<CardIssue> Issues, string? OceanSide, IReadOnlyList<string> Files)
{
    /// <summary>The prevailing wind the course data card drew (see <see cref="CourseCardBuilder.WindFrom"/>), or null for none.</summary>
    public int? WindFromDegrees { get; init; }
}

public static class CourseCardBuilder
{
    /// <summary>
    /// Validates, then writes the compass rose, hole data card, course data card and (when the course has drawn greens)
    /// the green size card in each configured format,
    /// plus "&lt;course&gt; - Card Validation.txt". ERRORs abort before anything is drawn; nothing is ever corrected.
    /// </summary>
    /// <param name="force">Draw even with ERRORs, as drafts: every card carries a DRAFT line naming the first error, and the errors are still reported and written to the validation file. Studio always builds this way, so an in-progress course still gets its cards.</param>
    public static CourseCardResult Build(string coursePath, string outputDir, CourseCardConfig config,
        IProgress<string>? log = null, bool force = false)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        // The card settings file wins; the course's Place.json fills what it leaves unset.
        var place = CoursePlaces.Load(outputDir);
        var wind = place.Rose(OceanFinder.SeaBearing(place, course));
        int? handWind = config.WindFromDegrees ?? place.WindFromDegrees;
        config = config with
        {
            Description = string.IsNullOrWhiteSpace(config.Description) && place.Description.Trim().Length > 0 ? place.Description.Trim() : config.Description,
            OceanSide = config.OceanSide ?? place.SeaSide,
            WindFromDegrees = WindFrom(config, place, wind),
            // The card settings' logo wins; otherwise the colour seal from the course's logo set.
            LogoPath = config.LogoPath ?? (CourseLogosFile.Load(outputDir).Pick(LogoKind.Seal, LogoTone.Colour) is { } seal
                ? CourseLogosFile.ImagePath(outputDir, seal.File) : null),
        };
        // Tee names, hole names and hand-written notes from the Studio's Holes card.
        var entries = CourseHolesFile.Load(outputDir);
        var issues = new List<CardIssue>();
        IReadOnlyList<HoleNote>? notes = null;
        if (config.AutoHoleNotes)
        {
            log?.Report("Reading hazards and greens from the course file for the hole data card…");
            notes = HoleNotes.For(course, log);
        }
        var data = CourseCardData.From(course, entries, config.TeeNames, issues, notes);
        if (notes is not null)
        {
            int hand = course.Holes.Count(h => entries.For(h.Number) is { } e && (e.Hazards.Any(z => z.Trim().Length > 0) || e.GreenNotes.Trim().Length > 0));
            issues.Add(new(Severity.Info, hand == 0
                ? "Hazards and green notes written from the course file."
                : $"Hazards and green notes written from the course file, except where written by hand on the Holes card ({hand} hole{(hand == 1 ? "" : "s")})."));
        }
        issues.AddRange(data.Validate());

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
        else if (wind is not null && handWind is int hand && WindRose.SectorOf(hand) != WindRose.SectorOf(wind.PrevailingFrom))
            issues.Add(new(Severity.Info, $"Wind set by hand ({WindRose.SectorNames[WindRose.SectorOf(hand)]}) differs from the wind rose's prevailing " +
                                          $"{WindRose.SectorNames[WindRose.SectorOf(wind.PrevailingFrom)]}; the rose is used, as on the Wind Card."));
        if (wind is null)
            issues.Add(new(Severity.Info, "No wind rose on the Place card; the Wind Card is not drawn."));

        foreach (var i in issues) log?.Report(i.ToString());
        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat(data.CourseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string report = Path.Combine(outputDir, $"{baseName} - Card Validation.txt");
        File.WriteAllLines(report, issues.Select(i => i.ToString()).DefaultIfEmpty("No issues."));
        var errors = issues.Where(i => i.Severity == Severity.Error).Select(i => i.Message).ToList();
        if (errors.Count > 0 && !force)
            throw new InvalidDataException($"Card build stopped: {errors.Count} error(s); see {report}. Nothing was corrected.");
        if (errors.Count > 0)
        {
            log?.Report($"[INFO] Drawn as DRAFT cards: {errors.Count} error(s) are printed on every card. Nothing was corrected.");
            File.AppendAllLines(report, [$"[INFO] Drawn as DRAFT cards despite {errors.Count} error(s); each card carries a DRAFT line."]);
        }

        var files = new List<string>();
        using var renderer = new CourseCardRenderer(data, config, ocean)
        {
            Wind = wind,
            HoleWinds = wind is null ? [] : WindExposure.For(course, wind),
        };
        var pages = new List<(string Name, SKSize Size, Action<SKCanvas> Draw)>
        {
            ("Compass Rose", CourseCardRenderer.RosePage, renderer.DrawRosePage),
            ("Hole Data Card", CourseCardRenderer.TablePage, renderer.DrawTablePage),
            ("Course Data Card", CourseCardRenderer.CardPage, renderer.DrawCoursePage),
        };
        if (greenCard) pages.Add(("Green Size Card", CourseCardRenderer.CardPage, renderer.DrawGreenSizePage));
        if (wind is not null) pages.Add(("Wind Card", CourseCardRenderer.CardPage, renderer.DrawWindPage));
        pages = pages.Select(p => (p.Name, p.Size, (Action<SKCanvas>)(c => { p.Draw(c); renderer.DrawAttribution(c, p.Size); renderer.DrawDraftNote(c, p.Size, errors); }))).ToList();
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
        return new CourseCardResult(data, issues, ocean, files) { WindFromDegrees = config.WindFromDegrees };
    }

    /// <summary>
    /// The prevailing wind the course data card draws. With a wind rose on the Place card it is the rose's most
    /// frequent direction, the one the Wind Card prints; only without a rose does the wind set by hand count
    /// (--wind or the card settings, then the Place card's prevailing wind).
    /// </summary>
    public static int? WindFrom(CourseCardConfig config, CoursePlace place, WindRose? rose) =>
        rose?.PrevailingFrom ?? config.WindFromDegrees ?? place.WindFromDegrees;
}
