using Pga2K25.CourseCard;
using Pga2K25.Estimate;
using Pga2K25.GreensBook;
using Pga2K25.HoleIllustrations;
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

    /// <summary>False for a product too slow for "Build all products": it is built from its own page.</summary>
    public bool InBuildAll { get; init; } = true;
}

public sealed record BuildContext(string CoursePath, string OutputDir, StudioSettings Settings, CourseExtras Extras, IProgress<string> Log);

/// <summary>Whether a product's files are there, and whether the course file has been saved since they were built.</summary>
public enum ProductState { NotBuilt, Built, OlderThanSave }

/// <summary>A page image of a built product.</summary>
public sealed record ProductPage(string Title, string Path);

public static class Products
{
    /// <summary>The build log's line when a course has no scorecard map style ticked.</summary>
    public const string NoScorecardStyles = "No scorecard map styles are ticked, so none were built. Tick the styles you want on the Scorecard map page.";

    /// <summary>The build log's line when a course has no hole illustration style ticked.</summary>
    public const string NoIllustrationStyles = "No hole illustration styles are ticked, so none were built. Tick the styles you want on the Hole illustrations page.";

    /// <summary>The Course Difficulty Estimate's product name.</summary>
    public const string DifficultyProduct = "Course difficulty";

    /// <summary>The build log's line for products "Build all" leaves out.</summary>
    public const string LeftOutOfBuildAll = "{0} takes a minute or more, so Build all leaves it out. Build it from its own page.";

    /// <summary>Every product, in menu and "Build all" order (Alex's order, 4 Nov 2026).</summary>
    public static readonly IReadOnlyList<ProductDefinition> All =
    [
        new("Terrain map", " - Terrain Map.png", c =>
            TerrainMapBuilder.Build(c.CoursePath, c.OutputDir, c.Extras.Terrain.ToOptions(c.Settings.Credit) with { Pdf = true }, c.Log).PngPath)
        {
            Pages = ["{0} - Terrain Map.png"], Pdfs = ["{0} - Terrain Map.pdf"],
        },
        new("Hole sheets", " - Hole Sheets.pdf", c =>
            // Area (for the shared 0 ft), shading, surfaces, hilltops and water carry over. Each sheet picks its own
            // finer contour interval, window and resolution. The notes column prints the hole design notes from Readiness.
            HoleSheetBuilder.Build(c.CoursePath, c.OutputDir,
                new HoleSheetOptions
                {
                    Map = c.Extras.Terrain.ToSheetOptions(c.Settings.Credit),
                    DesignNotes = Pga2K25.Readiness.SheetNotes.Load(c.OutputDir, c.CoursePath),
                }, null, c.Log).PdfPath)
        {
            Pages = ["{0} - Hole Sheets/*.png"], Pdfs = ["{0} - Hole Sheets.pdf"],
        },
        new("Course cards", " - Course Data Card.png", c =>
        {
            var config = c.Extras.CardConfig is { } cfg && File.Exists(cfg) ? CourseCardConfig.Load(cfg) : new CourseCardConfig();
            // force: an in-progress course still gets its cards, each marked DRAFT with the first validation error.
            return CourseCardBuilder.Build(c.CoursePath, c.OutputDir, config, c.Log, force: true).Files
                .FirstOrDefault(f => f.EndsWith("Course Data Card.png", StringComparison.OrdinalIgnoreCase));
        })
        {
            Pages = ["{0} - Course Data Card.png", "{0} - Course Details Card.png", "{0} - Green Size Card.png", "{0} - Wind Card.png", "{0} - Shot Variety Card.png", "{0} - Hole Data Card.png", "{0} - Compass Rose.png"],
            Pdfs = ["{0} - Course Cards.pdf", "{0} - Course Data Card.pdf", "{0} - Course Details Card.pdf", "{0} - Green Size Card.pdf", "{0} - Wind Card.pdf", "{0} - Shot Variety Card.pdf", "{0} - Hole Data Card.pdf", "{0} - Compass Rose.pdf"],
        },
        // Every hole and pin played by the hole design simulator for both players: a minute or more, so not in Build all.
        new(DifficultyProduct, " - Course Difficulty.png", c =>
        {
            var config = c.Extras.CardConfig is { } cfg && File.Exists(cfg) ? CourseCardConfig.Load(cfg) : new CourseCardConfig();
            return CourseDifficultyBuilder.Build(c.CoursePath, c.OutputDir, config, c.Log).Files
                .FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        })
        {
            Pages = ["{0} - Course Difficulty.png"], Pdfs = ["{0} - Course Difficulty.pdf"], InBuildAll = false,
        },
        new("Greens book", " - Greens Book.png", c =>
            GreensBookBuilder.Build(c.CoursePath, c.OutputDir, new GreensBookOptions { Credit = c.Settings.Credit },
                new GreensBookOutputs { GreenStudies = true, Pdf = true }, log: c.Log).SheetPath)
        {
            Pages = ["{0} - Greens Book.png", "{0} - Green Studies/*.png"], Pdfs = ["{0} - Greens Book.pdf"],
        },
        new("Yardage book", " - Yardage Book.pdf", c =>
        {
            // Width stations follow the course's design tee shot (Publish readiness limits, 300–350 yds by default).
            var tee = Pga2K25.Readiness.ReadinessStore.Load(Pga2K25.Readiness.ReadinessStore.PathFor(c.OutputDir)).Limits;
            var options = new YardageOptions { Credit = c.Settings.Credit, WidthStationsYards = YardageOptions.StationsFor(tee.DesignDriveMinYards, tee.DesignDriveMaxYards) };
            return YardageBookBuilder.Build(c.CoursePath, c.OutputDir, options, new YardageBookOutputs { Pdf = true, Pngs = true }, log: c.Log).PdfPath;
        })
        {
            Pages = ["{0} - Yardage Book/*.png"], Pdfs = ["{0} - Yardage Book.pdf"],
        },
        // One watercolour plate per hole, after the hole plates of Jim Ryan (illustrated by Tyler J. Rae); tee and hole names from Holes.json.
        new("Hole illustrations", " - Hole Illustrations.pdf", c =>
        {
            // Only the styles ticked for this course on the Hole illustrations page; none are ticked to start with.
            var looks = c.Extras.IllustrationLookKinds();
            if (looks.Count == 0)
            {
                c.Log.Report(NoIllustrationStyles);
                return null;
            }
            return looks.Select(look => HoleIllustrationBuilder.Build(c.CoursePath, c.OutputDir, new IllustrationOptions { Credit = c.Settings.Credit, Look = look },
                new IllustrationOutputs { Pdf = true, Pngs = true }, log: c.Log).PdfPath).ToList().FirstOrDefault();
        })
        {
            Pages = ["{0} - Hole Illustrations/*.png", "{0} - Hole Illustrations (Coore & Crenshaw)/*.png"],
            Pdfs = ["{0} - Hole Illustrations.pdf", "{0} - Hole Illustrations (Coore & Crenshaw).pdf"],
        },
        // Reads Estimate.json from the course's output folder for per-course choices (zones, overrides).
        new("Construction estimate", " - Estimate.pdf", c => EstimateBuilder.Build(c.CoursePath, c.OutputDir, log: c.Log).PdfPath)
        {
            Pages = ["{0} - Estimate/*.png"], Pdfs = ["{0} - Estimate.pdf"],
        },
        new("Scorecard map", " - Scorecard Map (Vintage).png", c =>
        {
            var options = new ScorecardMapOptions { Credit = c.Settings.Credit, BunkerMergeMetres = c.Extras.BunkerMergeMetres };
            // Only the styles ticked for this course on the Scorecard map page; none are ticked to start with.
            var styles = c.Extras.ScorecardThemeKinds();
            if (styles.Count == 0)
            {
                c.Log.Report(NoScorecardStyles);
                return null;
            }
            return ScorecardMapBuilder.Build(c.CoursePath, c.OutputDir, options, styles, pdf: true, log: c.Log)
                .Files.FirstOrDefault();
        })
        {
            Pages = ["{0} - Scorecard Map (Vintage).png", "{0} - Scorecard Map (Modern).png", "{0} - Scorecard Map (Illustrated).png", "{0} - Scorecard Map (RGD Style 1).png", "{0} - Scorecard Map (Modern Illustrated).png", "{0} - Scorecard Map (Routing Plan).png", "{0} - Scorecard Map (Course Portrait).png", "{0} - Scorecard Map (Travis Sketch).png", "{0} - Scorecard Map (MacKenzie Sketch).png", "{0} - Scorecard Map (Hanse Design).png", "{0} - Scorecard Map (RGD Style 2).png", "{0} - Scorecard Map (Cutten Map).png", "{0} - Scorecard Map (Thompson).png"],
            Pdfs = ["{0} - Scorecard Map (Vintage).pdf", "{0} - Scorecard Map (Modern).pdf", "{0} - Scorecard Map (Illustrated).pdf", "{0} - Scorecard Map (RGD Style 1).pdf", "{0} - Scorecard Map (Modern Illustrated).pdf", "{0} - Scorecard Map (Routing Plan).pdf", "{0} - Scorecard Map (Course Portrait).pdf", "{0} - Scorecard Map (Travis Sketch).pdf", "{0} - Scorecard Map (MacKenzie Sketch).pdf", "{0} - Scorecard Map (Hanse Design).pdf", "{0} - Scorecard Map (RGD Style 2).pdf", "{0} - Scorecard Map (Cutten Map).pdf", "{0} - Scorecard Map (Thompson).pdf"],
        },
    ];

    public static string OutputDir(StudioSettings settings, string courseName) =>
        Path.Combine(settings.OutputRoot, CourseLibrary.FileStem(courseName));

    public static string FileFor(ProductDefinition p, StudioSettings settings, string courseName) => FileFor(p, ProductPlace.Named(settings, courseName));

    public static string FileFor(ProductDefinition p, ProductPlace place) => Path.Combine(place.Dir, place.Stem + p.FileSuffix);

    /// <summary>
    /// Built, not built, or built before the course file's last save (<paramref name="courseSaved"/>), going by the
    /// newest of its files on disk, so a product rebuilt in part (one scorecard style) counts from its latest build.
    /// </summary>
    public static ProductState StateOf(ProductDefinition p, StudioSettings settings, string courseName, DateTime courseSaved) =>
        StateOf(p, ProductPlace.Named(settings, courseName), courseSaved);

    public static ProductState StateOf(ProductDefinition p, ProductPlace place, DateTime courseSaved)
    {
        var files = PagesOf(p, place).Select(x => x.Path).Concat(PdfsOf(p, place)).ToList();
        if (BuiltFile(p, place) is { } main) files.Add(main);
        if (files.Count == 0) return ProductState.NotBuilt;
        return files.Max(File.GetLastWriteTime) < courseSaved ? ProductState.OlderThanSave : ProductState.Built;
    }

    /// <summary>
    /// The file that shows the product has been built, or null: its main file, else its first page on disk (a
    /// scorecard map built without the Vintage style still counts as built).
    /// </summary>
    public static string? BuiltFile(ProductDefinition p, StudioSettings settings, string courseName) => BuiltFile(p, ProductPlace.Named(settings, courseName));

    public static string? BuiltFile(ProductDefinition p, ProductPlace place)
    {
        string main = FileFor(p, place);
        return File.Exists(main) ? main : PagesOf(p, place).FirstOrDefault()?.Path;
    }

    /// <summary>The page images the product has written, in reading order; empty when it hasn't been built.</summary>
    public static List<ProductPage> PagesOf(ProductDefinition p, StudioSettings settings, string courseName) => PagesOf(p, ProductPlace.Named(settings, courseName));

    public static List<ProductPage> PagesOf(ProductDefinition p, ProductPlace place)
    {
        string dir = place.Dir, stem = place.Stem;
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
    public static List<string> PdfsOf(ProductDefinition p, StudioSettings settings, string courseName) => PdfsOf(p, ProductPlace.Named(settings, courseName));

    public static List<string> PdfsOf(ProductDefinition p, ProductPlace place) =>
        p.Pdfs.Select(x => Path.Combine(place.Dir, string.Format(x, place.Stem))).Where(File.Exists).ToList();

    /// <summary>"Hole_07" → "Hole 7"; anything else unchanged.</summary>
    static string PageTitle(string fileStem) =>
        fileStem.StartsWith("Hole_", StringComparison.OrdinalIgnoreCase) && int.TryParse(fileStem[5..], out int n) ? $"Hole {n}" : fileStem.Replace('_', ' ');
}
