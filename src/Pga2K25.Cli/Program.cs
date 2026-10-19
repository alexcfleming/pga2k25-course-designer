using System.Globalization;
using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;
using Pga2K25.Estimate;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;
using Pga2K25.YardageBook;

const string Usage = """
    pga2k25 info <file.course>
    pga2k25 greensbook <file.course> [-o <dir>] [--studies] [--pdf] [--no-sheet] [--mono]
                       [--holes 1,2,3] [--hardness 0.85] [--blur 1.0] [--credit "NAME"]
    pga2k25 yardage <file.course> [-o <dir>] [--holes 1,2] [--data holes.xlsx] [--png] [--no-pdf]
                    [--verbose] [--credit "NAME"]
      default output: one PDF workbook, a portrait 4.25 x 11 in page per hole
      --png       also write a 1275 x 3300 px PNG per hole
    pga2k25 terrain <file.course> [-o <dir>] [--full-plot|--buildable] [--margin 150] [--interval 5] [--index 5]
                    [--shading relief|slope|none] [--ppm 4] [--cell 1.0] [--grid 100] [--no-features]
                    [--hardness 0.85] [--large-hardness 0.3] [--smooth 0] [--sea-level <m>] [--no-water]
                    [--holes 7,12|all [--sheet-grid 50] [--no-pdf] [--no-png]]
      --holes     instead, one 8.5 x 11 in terrain sketch sheet per hole, oriented like the
                  yardage book (PNGs at 300 dpi + one PDF)
                    [--no-summits] [--credit "NAME"]
      one north-up PNG of the plot: contours, relief or slope shading, lettered grid and
      the routing as sticks and dots ("<course> - Terrain Map.png")
      --interval  contour interval in feet (default: smallest of 1/2/5/10… that fits)
      --ppm       output pixels per metre of ground (default 4)
    pga2k25 sketches <file.course> <scan.pdf|.jpg|.png>... [-o <dir>] [--sheets <built Hole Sheets folder>]
      imports scanned hole sheets with pencil sketching: finds each page's hole by matching it
      against the course's sheets and writes "<course> - Sketches/" (pencil PNG, straightened
      scan and sketches.json, tied to world coordinates) for the Studio's terrain map
    pga2k25 cards <file.course> [-o <dir>] [--config course-card.json] [--data holes.xlsx]
                  [--formats png,pdf,svg] [--dpi 300] [--wind 225] [--ocean WEST|none] [--force]
      compass rose, hole data card and course data card from the course file; a workbook only
      adds tee names, hazard tags and green notes (its yardages/bearings are checked, not used);
      ERRORs stop the build unless --force (drafts only; errors are still reported)
    pga2k25 estimate <file.course> [-o <dir>] [--holes 3,7|all] [--cell 1.0]
      construction-estimate slice: cut/fill against an assumed starting ground (play splines
      are manufactured, everything else natural), a cut/fill PNG per hole and line items
      ("<course> - Estimate.json/.csv"); per-course choices from <dir>/Estimate.json
    pga2k25 calibrate <file.course> <captures.json> [--hardness 0.85] [--blur 1.0]
    pga2k25 scorecard <file.course> [-o <dir>] [--theme vintage|modern|both] [--landscape|--portrait]
                      [--north-up] [--data holes.xlsx|.csv] [--waste] [--raw-bunkers] [--no-paths]
                      [--pdf] [--credit "NAME"] [--subtitle "TEXT"]

      default output: one PNG page with every green ("<course> - Greens Book.png")
      --studies   also write a high-res study PNG per green (3300 x 2550 px)
      --pdf       also write a PDF (sheet page + one page per green)
      --mono      black-and-white linework, no slope colour

      calibrate compares modelled elevations with in-game spot labels read off green
      screenshots (see samples/calibration/cassimbo/captures.json for the format)

    """;

if (args.Length < 2) { Console.Error.WriteLine(Usage); return 2; }

try
{
    switch (args[0])
    {
        case "info": return Info(args[1]);
        case "greensbook": return GreensBook(args[1], args[2..]);
        case "scorecard": return Scorecard(args[1], args[2..]);
        case "yardage": return Yardage(args[1], args[2..]);
        case "terrain": return Terrain(args[1], args[2..]);
        case "cards": return Cards(args[1], args[2..]);
        case "sketches": return Sketches(args[1], args[2..]);
        case "estimate": return Estimate(args[1], args[2..]);
        case "calibrate" when args.Length >= 3: return Calibrate(args[1], args[2], args[3..]);
        default: Console.Error.WriteLine(Usage); return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(Environment.GetEnvironmentVariable("PGA2K25_DEBUG") is null ? $"error: {ex.Message}" : ex.ToString());
    return 1;
}

static int Estimate(string path, string[] rest)
{
    var options = new EstimateOptions();
    List<int>? holes = null;
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--holes":
                var h = Next();
                holes = h.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : h.Split(',').Select(int.Parse).ToList();
                break;
            case "--cell": options = options with { Terrain = options.Terrain with { CellMetres = double.Parse(Next(), CultureInfo.InvariantCulture) } }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    var result = EstimateBuilder.Build(path, outDir, holes, options, new SyncProgress(Console.WriteLine));
    foreach (var e in result.Holes)
    {
        Console.WriteLine($"Hole {e.Hole.Number} (par {e.Hole.Par})");
        foreach (var q in e.Quantities)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-28} {1,12:N1} {2,-6} {3,-9} {4}", q.Item, q.Qty, q.Unit, q.Provenance, q.Basis));
        foreach (var l in e.Lines)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  ${0,-27} {1,12} {2}", l.Quantity.Item,
                l.Cost is { } c ? c.ToString("N0", CultureInfo.InvariantCulture) : "not priced", l.IsPlaceholder ? "PLACEHOLDER" : l.PriceStatus));
    }
    return 0;
}

static int Info(string path)
{
    var file = CourseFile.Load(path);
    var c = file.ReadModel();
    Console.WriteLine($"{c.Name}  ({c.Version}, schema v{c.SchemaVersion})");
    Console.WriteLine($"holes {c.Holes.Count}, par {c.Holes.Sum(h => h.Par)}, length {c.Holes.Sum(h => h.RouteLength):0.0} m");
    Console.WriteLine($"height stamps {c.HeightStamps.Count}, surface splines {c.SurfaceSplines.Count}");
    foreach (var h in c.Holes)
        Console.WriteLine($"  hole {h.Number,2}  par {h.Par}  {h.RouteLength,6:0.0} m  tees {h.Tees.Count}  pins {h.Pins.Count}");
    return 0;
}

static int GreensBook(string path, string[] rest)
{
    var options = new GreensBookOptions();
    var terrain = options.Terrain;
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var outputs = new GreensBookOutputs();
    List<int>? holes = null;

    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        double NextDouble() => double.Parse(Next(), CultureInfo.InvariantCulture);
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--studies": outputs = outputs with { GreenStudies = true }; break;
            case "--pdf": outputs = outputs with { Pdf = true }; break;
            case "--no-sheet": outputs = outputs with { SheetPng = false }; break;
            case "--mono": options = options with { ColourSlopes = false }; break;
            case "--holes": holes = Next().Split(',').Select(int.Parse).ToList(); break;
            case "--hardness": terrain = terrain with { Hardness = NextDouble() }; break;
            case "--blur": terrain = terrain with { BlurSigmaMetres = NextDouble() }; break;
            case "--credit": options = options with { Credit = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    options = options with { Terrain = terrain };

    GreensBookBuilder.Build(path, outDir, options, outputs, holes, new SyncProgress(Console.WriteLine));
    return 0;
}

static int Calibrate(string coursePath, string capturesPath, string[] rest)
{
    var terrain = new TerrainModelOptions();
    for (int i = 0; i < rest.Length; i++)
    {
        double Next() => double.Parse(i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value"), CultureInfo.InvariantCulture);
        switch (rest[i])
        {
            case "--hardness": terrain = terrain with { Hardness = Next() }; break;
            case "--blur": terrain = terrain with { BlurSigmaMetres = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    var course = CourseFile.Load(coursePath).ReadModel();
    var options = new GreensBookOptions { Terrain = terrain };
    var results = Calibration.Evaluate(course, Calibration.LoadCaptures(capturesPath), options);

    Console.WriteLine($"{course.Name}: hardness {terrain.Hardness:0.00}, blur {terrain.BlurSigmaMetres:0.00} m");
    foreach (var r in results)
    {
        string band = r.PercentOfRange < 10 ? "trustworthy" : r.PercentOfRange <= 18 ? "indicative" : "CHECK";
        Console.WriteLine($"Hole {r.Hole,2}: pin fit {r.PinFitMetres:0.00} m, {r.MetresPerPixel * 100:0.00} cm/px, " +
                          $"spot RMS {r.RmsInches:0.0} in = {r.PercentOfRange:0}% of {r.RangeInches:0} in  [{band}]");
        foreach (var s in r.Spots)
            Console.WriteLine($"   label {GreenAnalyzer.FormatFeetInches(s.Label.Inches),6}  model {GreenAnalyzer.FormatFeetInches(s.ModelInches),6}" +
                              $"  edge {s.EdgeDistance,5:0.0} m{(s.Used ? "" : "  (not scored: edge/outside)")}");
    }
    var scored = results.Where(r => double.IsFinite(r.PercentOfRange)).ToList();
    if (scored.Count > 0)
        Console.WriteLine($"Mean {scored.Average(r => r.PercentOfRange):0.0}% of range, worst {scored.Max(r => r.PercentOfRange):0.0}%.");
    return 0;
}

static int Yardage(string path, string[] rest)
{
    var options = new YardageOptions();
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    List<int>? holes = null;
    var outputs = new YardageBookOutputs();
    bool verbose = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--holes": holes = Next().Split(',').Select(int.Parse).ToList(); break;
            case "--data": options = options with { TeeNames = HoleData.Load(Next()).TeeNames }; break;   // names only
            case "--verbose": verbose = true; break;
            case "--png": outputs = outputs with { Pngs = true }; break;
            case "--no-pdf": outputs = outputs with { Pdf = false }; break;
            case "--pdf": break;   // default; accepted for compatibility
            case "--credit": options = options with { Credit = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (!outputs.Pdf && !outputs.Pngs) throw new ArgumentException("--no-pdf needs --png, or there is nothing to write");
    YardageBookBuilder.Build(path, outDir, options, outputs, holes, new SyncProgress(Console.WriteLine), verbose);
    return 0;
}

static int Sketches(string path, string[] rest)
{
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var scans = new List<string>();
    string? sheets = null;
    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i] is "-o" or "--out" && i + 1 < rest.Length) outDir = rest[++i];
        else if (rest[i] == "--sheets" && i + 1 < rest.Length) sheets = rest[++i];
        else scans.Add(rest[i]);
    }
    if (scans.Count == 0) throw new ArgumentException("give at least one scan (PDF or image)");
    var course = CourseFile.Load(path).ReadModel();
    string stem = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(path) : course.Name)
        .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
    var log = new Progress<string>(Console.WriteLine);
    // The Hole sheets build's PNGs, when beside the output, are usually what was printed.
    string built = sheets ?? Path.Combine(outDir, $"{stem} - Hole Sheets");
    var results = HoleSketches.Import(path, scans, HoleSketches.FolderFor(outDir, stem), new HoleSheetOptions(), log,
        Directory.Exists(built) ? built : null);
    foreach (var r in results) Console.WriteLine($"{Path.GetFileName(r.Source)} p{r.Page}: {r.Message}");
    return 0;
}

static int Terrain(string path, string[] rest)
{
    var options = new TerrainMapOptions();
    var sheet = new HoleSheetOptions();
    string? holes = null;
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        double NextDouble() => double.Parse(Next(), CultureInfo.InvariantCulture);
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--holes": holes = Next(); break;
            case "--sheet-grid": sheet = sheet with { GridYards = NextDouble() }; break;
            case "--no-pdf": sheet = sheet with { Pdf = false }; break;
            case "--no-png": sheet = sheet with { Png = false }; break;
            case "--full-plot": options = options with { Extent = MapExtent.FullPlot }; break;
            case "--buildable": options = options with { Extent = MapExtent.Buildable }; break;
            case "--margin": options = options with { MarginMetres = NextDouble() }; break;
            case "--interval": options = options with { ContourIntervalFeet = NextDouble() }; break;
            case "--index": options = options with { IndexEvery = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--shading":
                options = options with
                {
                    Shading = Next().ToLowerInvariant() switch
                    {
                        "relief" => ReliefShading.Relief,
                        "slope" => ReliefShading.Slope,
                        "none" => ReliefShading.None,
                        var t => throw new ArgumentException($"unknown shading {t}"),
                    },
                };
                break;
            case "--ppm": options = options with { PixelsPerMetre = NextDouble() }; break;
            case "--cell": options = options with { Terrain = options.Terrain with { CellMetres = NextDouble() } }; break;
            case "--hardness": options = options with { Terrain = options.Terrain with { Hardness = NextDouble() } }; break;
            case "--large-hardness": options = options with { Terrain = options.Terrain with { LargeBrushHardness = NextDouble() } }; break;
            case "--smooth": options = options with { SmoothMetres = NextDouble() }; break;
            case "--grid": options = options with { GridYards = NextDouble() }; break;
            case "--no-features": options = options with { ShowFeatures = false }; break;
            case "--no-summits": options = options with { ShowSummits = false }; break;
            case "--sea-level": options = options with { SeaLevelMetres = NextDouble() }; break;
            case "--no-water": options = options with { ShowWater = false }; break;
            case "--credit": options = options with { Credit = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (holes is null)
    {
        TerrainMapBuilder.Build(path, outDir, options, new SyncProgress(Console.WriteLine));
        return 0;
    }
    // Per-hole sketch sheets; the plot map's --interval would be too coarse here, so it applies only if given.
    var numbers = holes.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : holes.Split(',').Select(int.Parse).ToList();
    HoleSheetBuilder.Build(path, outDir, sheet with { Map = options }, numbers, new SyncProgress(Console.WriteLine));
    return 0;
}

static int Cards(string path, string[] rest)
{
    var config = new CourseCardConfig();
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    string? workbook = null;
    bool force = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--config": config = CourseCardConfig.Load(Next()) with { }; break;
            case "--data": workbook = Next(); break;
            case "--formats": config = config with { Formats = Next().Split(',') }; break;
            case "--dpi": config = config with { Dpi = double.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--wind": config = config with { WindFromDegrees = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--ocean": config = config with { OceanSide = Next() }; break;
            case "--force": force = true; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    CourseCardBuilder.Build(path, outDir, config, workbook, new SyncProgress(Console.WriteLine), force);
    return 0;
}

static int Scorecard(string path, string[] rest)
{
    var options = new ScorecardMapOptions();
    IEnumerable<MapThemeKind> themes = [MapThemeKind.Vintage, MapThemeKind.Modern];
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    bool pdf = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--theme":
                themes = Next().ToLowerInvariant() switch
                {
                    "vintage" => [MapThemeKind.Vintage],
                    "modern" => [MapThemeKind.Modern],
                    "both" => [MapThemeKind.Vintage, MapThemeKind.Modern],
                    var t => throw new ArgumentException($"unknown theme {t}"),
                };
                break;
            case "--landscape": options = options with { Orientation = PageOrientation.Landscape }; break;
            case "--portrait": options = options with { Orientation = PageOrientation.Portrait }; break;
            case "--north-up": options = options with { AutoRotate = false }; break;
            case "--data": options = options with { Data = HoleData.Load(Next()) }; break;
            case "--waste": options = options with { ShowWaste = true }; break;
            case "--raw-bunkers": options = options with { MergeBunkers = false }; break;
            case "--bunker-merge": options = options with { BunkerMergeMetres = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) }; break;
            case "--no-paths": options = options with { ShowPaths = false }; break;
            case "--pdf": pdf = true; break;
            case "--credit": options = options with { Credit = Next() }; break;
            case "--subtitle": options = options with { Subtitle = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    ScorecardMapBuilder.Build(path, outDir, options, themes, pdf, log: new SyncProgress(Console.WriteLine));
    return 0;
}

sealed class SyncProgress(Action<string> write) : IProgress<string>
{
    public void Report(string value) => write(value);
}
