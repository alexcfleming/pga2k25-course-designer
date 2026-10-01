using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Greens;
using Pga2K25.Finisher.Operations;
using Pga2K25.Finisher.Pins;
using Pga2K25.Finisher.Splines;
using Pga2K25.Finisher.Tees;

namespace Pga2K25.Finisher;

public enum SmartPinMode { Repair, Rebuild }

/// <summary>
/// Every option of the reference Finisher's form (<c>finish_course</c> keyword arguments). Defaults are the
/// reference UI's defaults. The legacy water switches (<c>elevation_plane_water</c>, <c>smart_compact_water</c>,
/// <c>merge_compatible_water</c>) are not carried over: the Python accepts them but REV10 ignores them.
/// </summary>
public sealed record FinisherOptions
{
    /// <summary>1. Designer Smooth Splines: categories from <see cref="SplineCategory.All"/>.</summary>
    public IReadOnlyCollection<string> DesignerSmooth { get; init; } = [];

    /// <summary>2. Optimize Splines (waypoint reduction): categories from <see cref="SplineCategory.All"/>.</summary>
    public IReadOnlyCollection<string> Optimize { get; init; } = [];

    // 3. Terrain finishing
    public bool CompleteTees { get; init; } = true;
    public int TargetTeesPerHole { get; init; } = TeeCreation.DefaultTotal;
    public bool FlattenTees { get; init; } = true;
    public bool HeavyRough { get; init; }
    public bool SmartPins { get; init; }
    public SmartPinMode PinMode { get; init; } = SmartPinMode.Repair;
    public bool UltraSafeGreens { get; init; }
    public GreenSafety UltraSafeSeverity { get; init; } = GreenSafety.MoreSafe;
    public bool TopographicWeathering { get; init; }
    public bool ExportGreensBook { get; init; } = true;
    public bool FlattenCartPaths { get; init; } = true;
    public bool ReplaceLegacyCartPaths { get; init; } = true;
    public bool PreserveManualCartPathEdits { get; init; } = true;

    // 4. Native water finishing
    public bool GenerateWater { get; init; } = true;
    public bool ReplaceWater { get; init; } = true;

    /// <summary>Shoreline clearance below the low bank, metres (0–2).</summary>
    public double WaterClearance { get; init; } = NativeWater.DefaultClearance;

    /// <summary>Largest level difference between ponds that may share one body, metres (0–2).</summary>
    public double WaterMergeSpread { get; init; } = NativeWater.DefaultMergeSpread;

    /// <summary>The reference UI's input checks; returns the message to show, or null when valid.</summary>
    public string? Validate()
    {
        if (!(WaterClearance >= 0 && WaterClearance <= 2)) return "Water shoreline clearance must be a number between 0 and 2 metres.";
        if (!(WaterMergeSpread >= 0 && WaterMergeSpread <= 2)) return "Water merge elevation spread must be a number between 0 and 2 metres.";
        if (TargetTeesPerHole < TeeCreation.MinTotal || TargetTeesPerHole > TeeCreation.MaxTotal) return "Target tees per hole must be from 1 to 5.";
        return null;
    }
}

public sealed record FinisherReport
{
    public required string OutputPath { get; init; }
    public required CourseAnalysis Before { get; init; }
    public CourseAnalysis? After { get; set; }
    public DesignerSmoothReport? DesignerSmooth { get; set; }
    public SplineOptimizeReport? SplineOptimization { get; set; }
    public HeavyRoughReport? HeavyRough { get; set; }
    public NativeWaterReport? Water { get; set; }
    public CartPathReport? CartPaths { get; set; }
    public TeeCreationReport? TeeCreation { get; set; }
    public TeeLevelingReport? Tees { get; set; }
    public UltraSafeGreensReport? UltraSafeGreens { get; set; }
    public SmartPinsReport? SmartPins { get; set; }
    public WeatheringReport? TopographicWeathering { get; set; }
    public FinisherGreensBookResult? GreensBook { get; set; }
}

/// <summary>
/// C# port of Mal's TGC 2K25 Course Finisher (v0.10 REV10E) — <c>finish_course</c>. Loads a course, runs the
/// selected passes in the reference order (spline edits before terrain sampling, tees before pins), and writes
/// a new course; the source file is never overwritten.
/// </summary>
public static class CourseFinisher
{
    public const string ReferenceVersion = "v0.10 REV10E";

    public static CourseAnalysis Analyze(string coursePath) => CourseAnalysis.Analyze(CourseFile.Load(coursePath));

    /// <summary>"&lt;stem&gt; - Finished.course" next to the input (the reference's suggested output name).</summary>
    public static string DefaultOutputPath(string inputPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath))!, Path.GetFileNameWithoutExtension(inputPath) + " - Finished.course");

    public static FinisherReport Finish(string inputPath, string outputPath, FinisherOptions options, IProgress<string>? log = null,
        CancellationToken cancellation = default)
    {
        if (options.Validate() is string problem) throw new ArgumentException(problem);
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must be a new file; the finisher will not overwrite the source course.");

        var file = CourseFile.Load(inputPath);
        var course = file.Description;
        if (CourseModel.DetectVersion(course) != GameVersion.PGA2K25)
            throw new NotSupportedException($"Course Finisher {ReferenceVersion} currently supports native 2K25 course files only.");

        if (options.FlattenTees)
        {
            TeeLeveling.RemovePrior(course, log);
            TeeLeveling.RemoveUntrackedRev2Tail(course, log);
        }
        if (options.TopographicWeathering) Operations.TopographicWeathering.RemovePrior(course, log);
        var report = new FinisherReport { OutputPath = outputPath, Before = CourseAnalysis.Analyze(course) };

        void Step() => cancellation.ThrowIfCancellationRequested();
        Step();
        if (options.DesignerSmooth.Count > 0) report.DesignerSmooth = Splines.DesignerSmooth.Apply(course, options.DesignerSmooth, log);
        Step();
        if (options.Optimize.Count > 0) report.SplineOptimization = SplineOptimizer.Apply(course, options.Optimize, log);
        Step();
        if (options.HeavyRough) report.HeavyRough = Operations.HeavyRough.Apply(course, log);
        Step();
        if (options.GenerateWater) report.Water = NativeWater.Apply(course, options.ReplaceWater, options.WaterClearance, options.WaterMergeSpread, log);
        Step();
        if (options.FlattenCartPaths) report.CartPaths = CartPathLeveling.Apply(course, options.ReplaceLegacyCartPaths, options.PreserveManualCartPathEdits, log);
        Step();
        if (options.CompleteTees) report.TeeCreation = Tees.TeeCreation.Apply(course, options.TargetTeesPerHole, log);
        Step();
        if (options.FlattenTees) report.Tees = TeeLeveling.Apply(course, log);
        Step();
        if (options.UltraSafeGreens) report.UltraSafeGreens = Greens.UltraSafeGreens.Apply(course, UltraSafeGreens.Profile(options.UltraSafeSeverity).Key, log);
        Step();
        if (options.SmartPins) report.SmartPins = Pins.SmartPins.Apply(course, options.PinMode == SmartPinMode.Rebuild ? "rebuild" : "repair", log);
        Step();
        if (options.TopographicWeathering) report.TopographicWeathering = Operations.TopographicWeathering.Apply(course, log);
        Step();
        if (options.ExportGreensBook) report.GreensBook = FinisherGreensBook.Export(course, outputPath, log);
        Step();

        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (dir is not null) Directory.CreateDirectory(dir);
        file.Save(outputPath);
        report.After = CourseAnalysis.Analyze(course);
        return report;
    }
}
