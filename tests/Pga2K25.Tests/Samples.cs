using System.Collections.Concurrent;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.HoleSim;
using Pga2K25.Readiness;

namespace Pga2K25.Tests;

/// <summary>
/// Expensive sample data built once per test run and shared by every test class: course models, the Cassimbo readiness
/// measuring and traced water maps. Everything here is read-only to the tests. A test that changes a course (adds a
/// not-water mark, edits a hole) loads its own copy with <see cref="Fresh"/> instead.
/// </summary>
internal static class Samples
{
    static readonly ConcurrentDictionary<string, Lazy<CourseModel>> CourseCache = new();
    static readonly ConcurrentDictionary<(string, TerrainModelOptions), Lazy<WaterMap>> WaterCache = new();

    /// <summary>A sample course from samples/courses, or samples/private when <paramref name="privateDir"/>; shared, so don't change it.</summary>
    public static CourseModel Course(string file, bool privateDir = false)
    {
        string path = Path.Combine(privateDir ? TestPaths.PrivateDir : TestPaths.SamplesDir, file);
        return CourseCache.GetOrAdd(path, p => new Lazy<CourseModel>(() => CourseFile.Load(p).ReadModel())).Value;
    }

    /// <summary>A course model of its own, for a test that changes it.</summary>
    public static CourseModel Fresh(string file, bool privateDir = false) =>
        CourseFile.Load(Path.Combine(privateDir ? TestPaths.PrivateDir : TestPaths.SamplesDir, file)).ReadModel();

    /// <summary>The traced water of a shared course with these options, traced once per run.</summary>
    public static WaterMap Water(string file, bool privateDir, TerrainModelOptions options)
    {
        string path = Path.Combine(privateDir ? TestPaths.PrivateDir : TestPaths.SamplesDir, file);
        return WaterCache.GetOrAdd((path, options), _ => new Lazy<WaterMap>(() => WaterTracer.Trace(Course(file, privateDir), options))).Value;
    }

    public static CourseModel Cassimbo => Course("cassimbo.course");

    // ───────── Pilolcura Analysis for the hole design simulator (a private sample) ─────────

    public const string Pilolcura = "pilolcura-analysis-2026-11-02.course";
    static readonly Lazy<CourseFile> PilolcuraFile = new(() => CourseFile.Load(Path.Combine(TestPaths.PrivateDir, Pilolcura)));
    static readonly Lazy<CourseModel> PilolcuraModel = new(() => PilolcuraFile.Value.ReadModel());
    static readonly ConcurrentDictionary<int, Lazy<HoleModel>> PilolcuraHoles = new();
    static readonly ConcurrentDictionary<int, Lazy<GreenSurface>> PilolcuraGreens = new();
    static readonly ConcurrentDictionary<int, Lazy<ShotContext>> PilolcuraShotContexts = new();

    public static CourseModel PilolcuraCourse => PilolcuraModel.Value;

    /// <summary>A Pilolcura hole from its back tee, built once per run.</summary>
    public static HoleModel PilolcuraHole(int hole) =>
        PilolcuraHoles.GetOrAdd(hole, n => new Lazy<HoleModel>(() => HoleModelBuilder.Build(PilolcuraCourse, PilolcuraFile.Value.Description, n))).Value;

    /// <summary>The hole's green on the replay smoothed 8 m outside greens (the green read's default), built once per run.</summary>
    public static GreenSurface PilolcuraGreen(int hole) =>
        PilolcuraGreens.GetOrAdd(hole, n => new Lazy<GreenSurface>(() => GreenSurface.Build(PilolcuraHole(n), ReplayTerrain.Smoothed(PilolcuraCourse, PilolcuraHole(n), 8)))).Value;

    /// <summary>
    /// Shots on the hole with the shipped assumptions and the fitted golfer, flat outside the green. Shared: its putting
    /// and short-game maps fill in as tests use them (built once each, under a lock), so tests only read from it.
    /// </summary>
    public static ShotContext PilolcuraShots(int hole) =>
        PilolcuraShotContexts.GetOrAdd(hole, n => new Lazy<ShotContext>(() => ApproachSetup.Build(PilolcuraHole(n), PilolcuraGreen(n), null, AssumptionSheet.Load(),
            TableBaseline.LoadShipped("pga-tour-anchors"), AssumptionSheet.Stimp(PilolcuraCourse.GreenSpeed)!.Value).Context)).Value;

    static readonly Lazy<ReadinessChecker.Inputs> CassimboInputs = new(() => ReadinessChecker.Prepare(Cassimbo));

    /// <summary>The Cassimbo readiness report with these limits and overrides; the course is measured once per run and only the limits are applied each time.</summary>
    public static ReadinessReport CassimboReadiness(ReadinessLimits? limits = null, IReadOnlyDictionary<int, HoleOverride>? overrides = null) =>
        ReadinessChecker.Run(Cassimbo, limits, overrides: overrides, inputs: CassimboInputs.Value);
}
