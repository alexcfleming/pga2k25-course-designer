using System.Globalization;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

const string Usage = """
    pga2k25 info <file.course>
    pga2k25 greensbook <file.course> [-o <dir>] [--studies] [--pdf] [--no-sheet] [--mono]
                       [--holes 1,2,3] [--hardness 0.85] [--blur 1.0] [--credit "NAME"]
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
        case "calibrate" when args.Length >= 3: return Calibrate(args[1], args[2], args[3..]);
        default: Console.Error.WriteLine(Usage); return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(Environment.GetEnvironmentVariable("PGA2K25_DEBUG") is null ? $"error: {ex.Message}" : ex.ToString());
    return 1;
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
