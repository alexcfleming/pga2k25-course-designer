using Pga2K25.CourseCard;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;
using Pga2K25.YardageBook;

namespace Pga2K25.Studio.Services;

/// <summary>One printable product: the file that shows it exists, and how to build it with the same options as the CLI.</summary>
public sealed record ProductDefinition(string Name, string FileSuffix, Func<BuildContext, string?> Build);

public sealed record BuildContext(string CoursePath, string OutputDir, StudioSettings Settings, CourseExtras Extras, IProgress<string> Log);

public static class Products
{
    public static readonly IReadOnlyList<ProductDefinition> All =
    [
        new("Terrain map", " - Terrain Map.png", c =>
            TerrainMapBuilder.Build(c.CoursePath, c.OutputDir, new TerrainMapOptions { Credit = c.Settings.Credit }, c.Log).PngPath),
        new("Hole sheets", " - Hole Sheets.pdf", c =>
            HoleSheetBuilder.Build(c.CoursePath, c.OutputDir, new HoleSheetOptions { Map = new TerrainMapOptions { Credit = c.Settings.Credit } }, null, c.Log).PdfPath),
        new("Greens book", " - Greens Book.png", c =>
            GreensBookBuilder.Build(c.CoursePath, c.OutputDir, new GreensBookOptions { Credit = c.Settings.Credit },
                new GreensBookOutputs { GreenStudies = true, Pdf = true }, log: c.Log).SheetPath),
        new("Yardage book", " - Yardage Book.pdf", c =>
        {
            var options = new YardageOptions { Credit = c.Settings.Credit };
            if (c.Extras.Workbook is { } wb && File.Exists(wb)) options = options with { TeeNames = HoleData.Load(wb).TeeNames };
            return YardageBookBuilder.Build(c.CoursePath, c.OutputDir, options, new YardageBookOutputs { Pdf = true, Pngs = true }, log: c.Log).PdfPath;
        }),
        new("Scorecard map", " - Scorecard Map (Vintage).png", c =>
        {
            var data = c.Extras.Workbook is { } wb && File.Exists(wb) ? HoleData.Load(wb) : null;
            var options = new ScorecardMapOptions { Data = data, Credit = c.Settings.Credit };
            return ScorecardMapBuilder.Build(c.CoursePath, c.OutputDir, options, [MapThemeKind.Vintage, MapThemeKind.Modern], pdf: true, log: c.Log)
                .Files.FirstOrDefault();
        }),
        new("Course cards", " - Course Data Card.png", c =>
        {
            var config = c.Extras.CardConfig is { } cfg && File.Exists(cfg) ? CourseCardConfig.Load(cfg) : new CourseCardConfig();
            var workbook = c.Extras.Workbook is { } wb && File.Exists(wb) ? wb : null;
            return CourseCardBuilder.Build(c.CoursePath, c.OutputDir, config, workbook, c.Log).Files
                .FirstOrDefault(f => f.EndsWith("Course Data Card.png", StringComparison.OrdinalIgnoreCase));
        }),
    ];

    public static string OutputDir(StudioSettings settings, string courseName) =>
        Path.Combine(settings.OutputRoot, CourseLibrary.FileStem(courseName));

    public static string FileFor(ProductDefinition p, StudioSettings settings, string courseName) =>
        Path.Combine(OutputDir(settings, courseName), CourseLibrary.FileStem(courseName) + p.FileSuffix);
}
