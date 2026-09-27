using System.Globalization;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

const string Usage = """
    pga2k25 info <file.course>
    pga2k25 greensbook <file.course> [-o <dir>] [--studies] [--pdf] [--no-sheet] [--mono]
                       [--holes 1,2,3] [--hardness 0.7] [--blur 1.0] [--credit "NAME"]

      default output: one PNG page with every green ("<course> - Greens Book.png")
      --studies   also write a high-res study PNG per green (3300 x 2550 px)
      --pdf       also write a PDF (sheet page + one page per green)
      --mono      black-and-white linework, no slope colour
    """;

if (args.Length < 2) { Console.Error.WriteLine(Usage); return 2; }

try
{
    switch (args[0])
    {
        case "info": return Info(args[1]);
        case "greensbook": return GreensBook(args[1], args[2..]);
        default: Console.Error.WriteLine(Usage); return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
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

sealed class SyncProgress(Action<string> write) : IProgress<string>
{
    public void Report(string value) => write(value);
}
