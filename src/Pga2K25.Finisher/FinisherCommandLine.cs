using System.Globalization;
using Pga2K25.Finisher.Greens;

namespace Pga2K25.Finisher;

/// <summary>Command-line front end shared by <c>pga2k25 finish</c> and the standalone <c>course-finisher</c>.</summary>
public static class FinisherCommandLine
{
    public const string Usage = """
        finish <file.course> [-o <finished.course>] [--smooth all|teebox,green,fairway,rough,bunker,cartpath]
               [--optimize all|LIST] [--tees N] [--no-tees] [--no-level-tees] [--heavy-rough]
               [--pins repair|rebuild] [--ultra-safe safe|more_safe|most_safe] [--weathering]
               [--no-greens-book] [--no-cartpaths] [--keep-legacy-cartpaths] [--relevel-edited-cartpaths]
               [--no-water] [--keep-water] [--water-clearance 0.15] [--water-merge 0.5]
        analyze <file.course>

          finish is the C# port of Mal's TGC 2K25 Course Finisher (v0.10 REV10E). It needs an imported
          (TGC Tool / LiDAR) fine-terrain lattice for the terrain passes; default output is
          "<course> - Finished.course" next to the input, and the input is never overwritten.
        """;

    /// <summary>Parses the options after the input path; returns the options and output path.</summary>
    public static (FinisherOptions Options, string Output) Parse(string path, IReadOnlyList<string> rest)
    {
        var options = new FinisherOptions();
        string output = CourseFinisher.DefaultOutputPath(path);
        static IReadOnlyCollection<string> Categories(string list) =>
            list.Equals("all", StringComparison.OrdinalIgnoreCase) ? SplineCategory.All : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < rest.Count; i++)
        {
            string Next() => i + 1 < rest.Count ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
            double NextDouble() => double.Parse(Next(), CultureInfo.InvariantCulture);
            switch (rest[i])
            {
                case "-o": case "--out": output = Next(); break;
                case "--smooth": options = options with { DesignerSmooth = Categories(Next()) }; break;
                case "--optimize": options = options with { Optimize = Categories(Next()) }; break;
                case "--tees": options = options with { TargetTeesPerHole = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                case "--no-tees": options = options with { CompleteTees = false }; break;
                case "--no-level-tees": options = options with { FlattenTees = false }; break;
                case "--heavy-rough": options = options with { HeavyRough = true }; break;
                case "--pins":
                    options = options with
                    {
                        SmartPins = true,
                        PinMode = Next().ToLowerInvariant() switch
                        {
                            "repair" => SmartPinMode.Repair,
                            "rebuild" => SmartPinMode.Rebuild,
                            var m => throw new ArgumentException($"unknown pin mode {m}"),
                        },
                    };
                    break;
                case "--ultra-safe":
                    options = options with { UltraSafeGreens = true, UltraSafeSeverity = (GreenSafety)Array.IndexOf(UltraSafeGreens.Profiles, UltraSafeGreens.Profile(Next())) };
                    break;
                case "--weathering": options = options with { TopographicWeathering = true }; break;
                case "--no-greens-book": options = options with { ExportGreensBook = false }; break;
                case "--no-cartpaths": options = options with { FlattenCartPaths = false }; break;
                case "--keep-legacy-cartpaths": options = options with { ReplaceLegacyCartPaths = false }; break;
                case "--relevel-edited-cartpaths": options = options with { PreserveManualCartPathEdits = false }; break;
                case "--no-water": options = options with { GenerateWater = false }; break;
                case "--keep-water": options = options with { ReplaceWater = false }; break;
                case "--water-clearance": options = options with { WaterClearance = NextDouble() }; break;
                case "--water-merge": options = options with { WaterMergeSpread = NextDouble() }; break;
                default: throw new ArgumentException($"unknown option {rest[i]}");
            }
        }
        return (options, output);
    }

    /// <summary>Analyze, finish and log, the way the reference UI does.</summary>
    public static FinisherReport Run(string path, IReadOnlyList<string> rest, IProgress<string> log)
    {
        var (options, output) = Parse(path, rest);
        log.Report(CourseFinisher.Analyze(path).Summary());
        log.Report("");
        log.Report("=== FINISHING COURSE ===");
        var report = CourseFinisher.Finish(path, output, options, log);
        log.Report("=== COMPLETE ===");
        log.Report($"Saved: {report.OutputPath}");
        return report;
    }
}
