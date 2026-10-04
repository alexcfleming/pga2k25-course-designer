using Pga2K25.CourseCard;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;
using Pga2K25.YardageBook;

namespace Pga2K25.Studio.Services;

/// <summary>
/// One printable product: the file that shows it exists, the page images and PDFs it writes (patterns with
/// "{0}" for the course's file stem; "folder/*.png" lists a folder), and how to build it with the CLI's options.
/// </summary>
public sealed record ProductDefinition(string Name, string FileSuffix, Func<BuildContext, string?> Build)
{
    public IReadOnlyList<string> Pages { get; init; } = [];
    public IReadOnlyList<string> Pdfs { get; init; } = [];
}

public sealed record BuildContext(string CoursePath, string OutputDir, StudioSettings Settings, CourseExtras Extras, IProgress<string> Log);

/// <summary>A page image of a built product.</summary>
public sealed record ProductPage(string Title, string Path);

public static class Products
{
    public static readonly IReadOnlyList<ProductDefinition> All =
    [
        new("Terrain map", " - Terrain Map.png", c =>
            TerrainMapBuilder.Build(c.CoursePath, c.OutputDir, new TerrainMapOptions { Credit = c.Settings.Credit }, c.Log).PngPath)
        {
            Pages = ["{0} - Terrain Map.png"],
        },
        new("Hole sheets", " - Hole Sheets.pdf", c =>
            HoleSheetBuilder.Build(c.CoursePath, c.OutputDir, new HoleSheetOptions { Map = new TerrainMapOptions { Credit = c.Settings.Credit } }, null, c.Log).PdfPath)
        {
            Pages = ["{0} - Hole Sheets/*.png"], Pdfs = ["{0} - Hole Sheets.pdf"],
        },
        new("Greens book", " - Greens Book.png", c =>
            GreensBookBuilder.Build(c.CoursePath, c.OutputDir, new GreensBookOptions { Credit = c.Settings.Credit },
                new GreensBookOutputs { GreenStudies = true, Pdf = true }, log: c.Log).SheetPath)
        {
            Pages = ["{0} - Greens Book.png", "{0} - Green Studies/*.png"], Pdfs = ["{0} - Greens Book.pdf"],
        },
        new("Yardage book", " - Yardage Book.pdf", c =>
        {
            var options = new YardageOptions { Credit = c.Settings.Credit };
            if (c.Extras.Workbook is { } wb && File.Exists(wb)) options = options with { TeeNames = HoleData.Load(wb).TeeNames };
            return YardageBookBuilder.Build(c.CoursePath, c.OutputDir, options, new YardageBookOutputs { Pdf = true, Pngs = true }, log: c.Log).PdfPath;
        })
        {
            Pages = ["{0} - Yardage Book/*.png"], Pdfs = ["{0} - Yardage Book.pdf"],
        },
        new("Scorecard map", " - Scorecard Map (Vintage).png", c =>
        {
            var data = c.Extras.Workbook is { } wb && File.Exists(wb) ? HoleData.Load(wb) : null;
            var options = new ScorecardMapOptions { Data = data, Credit = c.Settings.Credit };
            return ScorecardMapBuilder.Build(c.CoursePath, c.OutputDir, options, [MapThemeKind.Vintage, MapThemeKind.Modern], pdf: true, log: c.Log)
                .Files.FirstOrDefault();
        })
        {
            Pages = ["{0} - Scorecard Map (Vintage).png", "{0} - Scorecard Map (Modern).png"],
            Pdfs = ["{0} - Scorecard Map (Vintage).pdf", "{0} - Scorecard Map (Modern).pdf"],
        },
        new("Course cards", " - Course Data Card.png", c =>
        {
            var config = c.Extras.CardConfig is { } cfg && File.Exists(cfg) ? CourseCardConfig.Load(cfg) : new CourseCardConfig();
            var workbook = c.Extras.Workbook is { } wb && File.Exists(wb) ? wb : null;
            return CourseCardBuilder.Build(c.CoursePath, c.OutputDir, config, workbook, c.Log).Files
                .FirstOrDefault(f => f.EndsWith("Course Data Card.png", StringComparison.OrdinalIgnoreCase));
        })
        {
            Pages = ["{0} - Course Data Card.png", "{0} - Hole Data Card.png", "{0} - Compass Rose.png"],
            Pdfs = ["{0} - Course Data Card.pdf", "{0} - Hole Data Card.pdf", "{0} - Compass Rose.pdf"],
        },
    ];

    public static string OutputDir(StudioSettings settings, string courseName) =>
        Path.Combine(settings.OutputRoot, CourseLibrary.FileStem(courseName));

    public static string FileFor(ProductDefinition p, StudioSettings settings, string courseName) =>
        Path.Combine(OutputDir(settings, courseName), CourseLibrary.FileStem(courseName) + p.FileSuffix);

    /// <summary>The page images the product has written, in reading order; empty when it hasn't been built.</summary>
    public static List<ProductPage> PagesOf(ProductDefinition p, StudioSettings settings, string courseName)
    {
        string dir = OutputDir(settings, courseName), stem = CourseLibrary.FileStem(courseName);
        var pages = new List<ProductPage>();
        foreach (var pattern in p.Pages)
        {
            string rel = string.Format(pattern, stem);
            if (rel.EndsWith("/*.png", StringComparison.Ordinal))
            {
                string folder = Path.Combine(dir, rel[..^"/*.png".Length]);
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.GetFiles(folder, "*.png").Order(StringComparer.OrdinalIgnoreCase))
                    pages.Add(new ProductPage(PageTitle(Path.GetFileNameWithoutExtension(f)), f));
            }
            else if (File.Exists(Path.Combine(dir, rel)))
            {
                string name = Path.GetFileNameWithoutExtension(rel);
                pages.Add(new ProductPage(name.StartsWith(stem + " - ", StringComparison.Ordinal) ? name[(stem.Length + 3)..] : name, Path.Combine(dir, rel)));
            }
        }
        return pages;
    }

    /// <summary>The PDFs the product has written.</summary>
    public static List<string> PdfsOf(ProductDefinition p, StudioSettings settings, string courseName)
    {
        string dir = OutputDir(settings, courseName), stem = CourseLibrary.FileStem(courseName);
        return p.Pdfs.Select(x => Path.Combine(dir, string.Format(x, stem))).Where(File.Exists).ToList();
    }

    /// <summary>"Hole_07" → "Hole 7"; anything else unchanged.</summary>
    static string PageTitle(string fileStem) =>
        fileStem.StartsWith("Hole_", StringComparison.OrdinalIgnoreCase) && int.TryParse(fileStem[5..], out int n) ? $"Hole {n}" : fileStem.Replace('_', ' ');
}
