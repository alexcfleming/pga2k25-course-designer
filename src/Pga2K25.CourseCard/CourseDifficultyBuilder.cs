using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;
using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed record CourseDifficultyBuild(CourseDifficultyResult Result, IReadOnlyList<string> Files);

/// <summary>
/// The Course Difficulty Estimate product: every hole played by the hole design simulator (<see cref="CourseDifficulty"/>),
/// drawn as one sheet in the course cards' style, "&lt;course&gt; - Course Difficulty" in the card formats.
/// </summary>
public static class CourseDifficultyBuilder
{
    public const string FileName = "Course Difficulty";

    /// <param name="measured">An earlier result for the same save, drawn again without replaying; null plays the course.</param>
    public static CourseDifficultyBuild Build(string coursePath, string outputDir, CourseCardConfig config, IProgress<string>? log = null,
        CancellationToken cancel = default, int samples = CourseDifficulty.DefaultSamples, CourseDifficultyResult? measured = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        config = config with
        {
            LogoPath = config.LogoPath ?? (CourseLogosFile.Load(outputDir).Pick(LogoKind.Seal, LogoTone.Colour) is { } seal
                ? CourseLogosFile.ImagePath(outputDir, seal.File) : null),
        };
        var data = CourseCardData.From(course, CourseHolesFile.Load(outputDir), config.TeeNames, []);
        var result = measured ?? CourseDifficulty.Measure(coursePath, samples, log, cancel);
        if (result.Holes.Count == 0) throw new InvalidDataException("No hole has a tee, a route and a pin to play, so there is no difficulty to estimate.");
        log?.Report($"Played {result.Holes.Count} holes for both players in {result.Seconds:0} s.");

        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat(data.CourseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string stem = Path.Combine(outputDir, $"{baseName} - {FileName}");
        var files = new List<string>();
        using var renderer = new CourseCardRenderer(data, config, null) { Difficulty = result };
        var size = CourseCardRenderer.CardPage;
        void Draw(SKCanvas c) { renderer.DrawDifficultyPage(c); renderer.DrawAttribution(c, size); }
        var formats = config.Formats.Select(f => f.ToLowerInvariant()).ToHashSet();
        if (formats.Contains("png"))
        {
            float scale = (float)(config.Dpi / 72);
            using var surface = SKSurface.Create(new SKImageInfo((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)));
            surface.Canvas.Scale(scale);
            Draw(surface.Canvas);
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
                Title = $"{data.CourseName} — {FileName}", Creator = "PGA 2K25 Course Designer", RasterDpi = (float)config.Dpi,
            });
            Draw(doc.BeginPage(size.Width, size.Height));
            doc.EndPage();
            doc.Close();
            files.Add(stem + ".pdf");
        }
        log?.Report($"Course difficulty ({files.Count} files): {outputDir}");
        return new CourseDifficultyBuild(result, files);
    }
}
