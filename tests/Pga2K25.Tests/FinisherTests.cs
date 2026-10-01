using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher;
using Pga2K25.Finisher.Greens;
using Pga2K25.Finisher.Operations;
using Pga2K25.Finisher.Pins;
using Pga2K25.Finisher.Splines;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Tees;

namespace Pga2K25.Tests;

public class FinisherAnalysisTests
{
    static readonly string CassimboPath = Path.Combine(TestPaths.SamplesDir, "cassimbo.course");

    [Fact]
    public void HandBuiltCourse_HasNoFineLatticeButIsStillAnalysed()
    {
        var a = CourseFinisher.Analyze(CassimboPath);
        Assert.Equal(GameVersion.PGA2K25, a.Version);
        Assert.Null(a.FineBrush);
        Assert.Contains("high-resolution absolute terrain lattice", a.FineTerrainError);
        Assert.Equal(18, a.SplineFeatures[SplineCategory.Green]);
        Assert.True(a.SplineFeatures[SplineCategory.TeeBox] > 0);
        Assert.True(a.CartPaths > 0);
        Assert.Equal(72, a.TeeTotal);
        Assert.Contains("Fine terrain: none", a.Summary());
    }

    [Fact]
    public void LidarFixture_ReportsItsLattice()
    {
        var a = CourseFinisher.Analyze(FinisherFixture.Cassimbo);
        Assert.Equal(72, a.FineBrush);
        Assert.Equal(2.0, a.FineSpacing);
        Assert.True(a.FineStamps > 50_000);
        Assert.Equal(1, a.WaterPondLakeCandidates);
    }

    [Fact]
    public void Options_ValidateLikeTheReferenceForm()
    {
        Assert.Null(new FinisherOptions().Validate());
        Assert.Contains("clearance", new FinisherOptions { WaterClearance = 2.5 }.Validate());
        Assert.Contains("spread", new FinisherOptions { WaterMergeSpread = -1 }.Validate());
        Assert.Contains("1 to 5", new FinisherOptions { TargetTeesPerHole = 6 }.Validate());
    }

    [Fact]
    public void Finish_RefusesToOverwriteTheSource()
    {
        var ex = Assert.Throws<ArgumentException>(() => CourseFinisher.Finish(CassimboPath, CassimboPath, new FinisherOptions()));
        Assert.Contains("will not overwrite", ex.Message);
    }

    [Fact]
    public void DesignerSmooth_RewritesHandlesAlongTheNeighbourTangent()
    {
        var course = CourseFile.Load(CassimboPath).Description;
        var report = Finisher.Splines.DesignerSmooth.Apply(course, ["green"]);
        Assert.Equal(18, report.Inspected);
        var greenIndex = FeatureClassifier.Classify(course).First(kv => kv.Value == SplineCategory.Green).Key;
        var wps = Json.Arr((JsonObject)Json.Arr(course, "surfaceSplines2")[greenIndex]!, "waypoints");
        int n = wps.Count;
        for (int i = 0; i < n; i++)
        {
            Vec2 P(JsonNode? p) => new(Json.Num(p?["x"]), Json.Num(p?["z"]));
            var p = P(wps[i]!["waypoint"]);
            var prev = P(wps[(i - 1 + n) % n]!["waypoint"]);
            var next = P(wps[(i + 1) % n]!["waypoint"]);
            var tangent = (next - prev).Normalized();
            var outHandle = P(wps[i]!["pointTwo"]) - p;
            double expected = Math.Clamp(0.375 * Vec2.Distance(next, p), 1.0, 30.0);
            Assert.Equal(expected, outHandle.Length, 6);
            Assert.Equal(1.0, (outHandle.X * tangent.X + outHandle.Z * tangent.Z) / outHandle.Length, 9);
        }
    }

    [Fact]
    public void SplineOptimizer_RemovesOnlyRedundantPoints()
    {
        var course = CourseFile.Load(CassimboPath).Description;
        var before = Json.Arr(course, "surfaceSplines2").Sum(s => Json.Arr(s as JsonObject, "waypoints").Count);
        var report = SplineOptimizer.Apply(course, ["fairway", "rough", "bunker"]);
        var after = Json.Arr(course, "surfaceSplines2").Sum(s => Json.Arr(s as JsonObject, "waypoints").Count);
        Assert.Equal(before - after, report.Removed);
        Assert.True(report.MaxError <= 0.10 + 1e-9);
        Assert.Equal(report.Removed, Json.Int(Provenance.Existing(course, "splineOptimization")?["removed"], -1));
    }
}

public class FinisherPipelineTests
{
    static readonly FinisherOptions Everything = new()
    {
        TargetTeesPerHole = 5,
        HeavyRough = true,
        SmartPins = true,
        PinMode = SmartPinMode.Rebuild,
        UltraSafeGreens = true,
        TopographicWeathering = true,
        ExportGreensBook = false,
    };

    static readonly Lazy<(string Dir, string Output, FinisherReport Report, List<string> Log)> FirstRun = new(() =>
    {
        string dir = FinisherFixture.TempDir();
        string output = Path.Combine(dir, "finished.course");
        var log = new List<string>();
        var report = CourseFinisher.Finish(FinisherFixture.Cassimbo, output, Everything, new SyncLog(log));
        return (dir, output, report, log);
    });

    sealed class SyncLog(List<string> lines) : IProgress<string>
    {
        public void Report(string value) { lock (lines) lines.Add(value); }
    }

    [Fact]
    public void Finish_WritesANewLoadableCourseAndLeavesTheSourceAlone()
    {
        byte[] sourceBefore = File.ReadAllBytes(FinisherFixture.Cassimbo);
        var (_, output, report, _) = FirstRun.Value;
        Assert.True(File.Exists(output));
        var model = CourseFile.Load(output).ReadModel();
        Assert.Equal(18, model.Holes.Count);
        Assert.Equal(sourceBefore, File.ReadAllBytes(FinisherFixture.Cassimbo));
        Assert.NotNull(report.After);
        Assert.Equal(Provenance.FinisherVersion, Json.Int(Provenance.Meta(CourseFile.Load(output).Description)["version"], 0));
    }

    [Fact]
    public void Water_CoversThePondWithNoExposedDryLand()
    {
        var report = FirstRun.Value.Report.Water!;
        Assert.Equal(1, report.PondLakeCandidates);
        var body = Assert.Single(report.Bodies);
        Assert.InRange(body.Waypoints, 4, 10);
        Assert.Equal("adaptive-plane", body.Kind);
        Assert.True(body.MinimumDryClearance > NativeWater.DryMargin);

        var course = CourseFile.Load(FirstRun.Value.Output).Description;
        var group = Json.Arr(course, "placedObjects4").OfType<JsonObject>().Single(g => Json.Str(g["Key"]?["path"]) == NativeWater.GroupPath);
        var path = Assert.Single(Json.Arr(Json.Obj(group, "Value"), "objectPaths2"));
        Assert.True(Json.Truthy(path!["isWater"]));
        Assert.Equal(body.Level, Json.Num(path["height"]), 9);
        // Bank = the plateau; level = 10th-percentile bank − 0.15 m clearance − 0.15 m extra REV10 clearance.
        double plateau = new Finisher.Terrain.MultiScaleTerrain(course).Sample(FinisherFixture.PondCenter.X + 20, FinisherFixture.PondCenter.Z);
        Assert.InRange(body.Level, plateau - 0.33, plateau - 0.29);
    }

    [Fact]
    public void Tees_AreToppedUpAndLevelledWithATrackedTail()
    {
        var (_, output, report, _) = FirstRun.Value;
        var course = CourseFile.Load(output).Description;
        Assert.All(TeeGeometry.Holes(course).OfType<JsonObject>(), h => Assert.Equal(5, Json.Arr(h, "tees").Count));
        Assert.Equal(18, report.TeeCreation!.Added);
        Assert.True(report.Tees!.Stamps > 0);
        var tail = Provenance.PriorTeeFlattenTail(course);
        Assert.NotNull(tail);
        var height = Json.Arr(course, "height");
        Assert.Equal(height.Count, tail.Value.Start + tail.Value.Count);
        Assert.All(height.Skip(tail.Value.Start), op =>
        {
            Assert.Equal(TeeLeveling.Tool, Json.Int(op!["tool"], -1));
            Assert.Equal(TeeLeveling.BrushType, Json.Int(op["type"], -1));
            Assert.Equal(0.0, Json.Num(op["value"]));
        });
    }

    [Fact]
    public void CartPaths_GetBrush10StampsOnlyWhereTheLatticeReaches()
    {
        var (_, _, report, log) = FirstRun.Value;
        var cart = report.CartPaths!;
        Assert.True(cart.Stamps > 100);
        Assert.InRange(cart.MaxDelta, 0, 0.2 + 1e-9);
        Assert.Contains(log, l => l.Contains("insufficient fine terrain coverage"));
    }

    [Fact]
    public void Pins_FourPerGreenInsideTheGreenAndApart()
    {
        var (_, output, report, _) = FirstRun.Value;
        var course = CourseFile.Load(output).Description;
        var (greens, _) = SmartPins.GreenGeometries(course);
        var model = CourseFile.Load(output).ReadModel();
        foreach (var hole in model.Holes.Where(h => h.Number <= 3))
        {
            Assert.Equal(4, hole.Pins.Count);
            var green = greens.Single(g => FinisherGeometry.Contains(g.Poly, hole.GreenAnchor));
            Assert.All(hole.Pins, p => Assert.True(FinisherGeometry.Contains(green.Poly, p) && FinisherGeometry.EdgeClearance(p, green.Poly) >= 1.0 - 1e-6));
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++) Assert.True(Vec2.Distance(hole.Pins[i], hole.Pins[j]) >= 1.0);
        }
        Assert.True(report.SmartPins!.Pins >= 12);
    }

    [Fact]
    public void HeavyRoughAndWeathering_AreRecordedTails()
    {
        var (_, output, report, _) = FirstRun.Value;
        var course = CourseFile.Load(output).Description;
        var meta = Provenance.Existing(course, HeavyRough.MetaKey)!;
        int start = Json.Int(meta["start"], -1), count = Json.Int(meta["count"], -1);
        Assert.Equal(report.HeavyRough!.Created, count);
        var brushes = Json.Arr(course, "surfaceBrushes2");
        Assert.Equal(Json.Str(meta["signature"]), Provenance.TailSignature(brushes.Skip(start).Take(count)));
        var weather = Provenance.Existing(course, TopographicWeathering.MetaKey)!;
        Assert.Equal(report.TopographicWeathering!.Count, Json.Arr(weather, "brushKeys").Count);
        Assert.True(report.TopographicWeathering.Green > 0);
    }

    [Fact]
    public void UltraSafeGreens_StoresOriginalsAndLowersRelief()
    {
        var (_, output, report, _) = FirstRun.Value;
        var ultra = report.UltraSafeGreens!;
        Assert.True(ultra.ChangedNodes > 0);
        // Holes 1–3 sit on the imported lattice; elsewhere only coarse Designer stamps exist and nothing can be compressed.
        var compressed = ultra.Details.Where(d => d.Hole <= 3 && d.Factor < 1 && d.WeightedRows > 0).ToList();
        Assert.NotEmpty(compressed);
        Assert.All(compressed, d => Assert.True(d.After!.LocalP95Pct < d.Before.LocalP95Pct));
        var meta = Provenance.Existing(CourseFile.Load(output).Description, UltraSafeGreens.MetaKey)!;
        Assert.Equal(ultra.ChangedNodes, Json.Arr(meta, "originalValues").Count);
    }

    [Fact]
    public void RunningAgainOnItsOwnOutput_ReplacesRatherThanStacks()
    {
        var (dir, output, first, _) = FirstRun.Value;
        string second = Path.Combine(dir, "finished twice.course");
        var report = CourseFinisher.Finish(output, second, Everything);
        var a = CourseFile.Load(output).Description;
        var b = CourseFile.Load(second).Description;
        Assert.Equal(Json.Arr(a, "height").Count, Json.Arr(b, "height").Count);
        // Heavy Rough can grow a little: it classifies tee boxes before tee creation (pass 3 vs 6), so the second run sees the
        // first run's extra tee markers. Weathering may move slightly because cart paths (pass 5) sample the terrain before
        // Ultra Speed-Safe Greens (pass 8) restores it. The Python behaves the same; what matters is that nothing stacks.
        var hrA = Provenance.Existing(a, HeavyRough.MetaKey)!;
        var hrB = Provenance.Existing(b, HeavyRough.MetaKey)!;
        Assert.Equal(Json.Int(hrA["start"], -1), Json.Int(hrB["start"], -2));
        Assert.Equal(report.HeavyRough!.Created, Json.Int(hrB["count"], -1));
        Assert.Equal(Json.Arr(a, "surfaceBrushes2").Count - first.HeavyRough!.Created - first.TopographicWeathering!.Count
                     + report.HeavyRough.Created + report.TopographicWeathering!.Count, Json.Arr(b, "surfaceBrushes2").Count);
        Assert.Equal(first.Water!.Created, report.Water!.Created);
        Assert.Equal(first.UltraSafeGreens!.ChangedNodes, report.UltraSafeGreens!.ChangedNodes);
        Assert.Equal(0, report.TeeCreation!.Added);   // already five per hole
        Assert.Equal(first.CartPaths!.Stamps, report.CartPaths!.Stamps);
        Assert.Equal(first.Tees!.Stamps, report.Tees!.Stamps);
    }

    [Fact]
    public void EditedHeavyRoughTail_IsPreserved()
    {
        var course = CourseFile.Load(FirstRun.Value.Output).Description;
        // Weathering is appended after Heavy Rough; the finisher removes it first, which puts the tail back at the end.
        Assert.Equal(FirstRun.Value.Report.TopographicWeathering!.Count, TopographicWeathering.RemovePrior(course));
        var meta = Provenance.Existing(course, HeavyRough.MetaKey)!;
        var brushes = Json.Arr(course, "surfaceBrushes2");
        var edited = brushes[Json.Int(meta["start"], 0)]!.AsObject();
        edited["value"] = Json.F(0.5);   // the designer touched one brush
        var log = new List<string>();
        Assert.Equal(0, HeavyRough.RemovePrior(course, new SyncLog(log)));
        Assert.Contains(log, l => l.Contains("signature changed; preserving it"));
    }

    [Fact]
    public void GreensBook_WritesAPagePerHoleAndAPdf()
    {
        string dir = FinisherFixture.TempDir();
        try
        {
            var course = CourseFile.Load(FinisherFixture.Cassimbo).Description;
            var result = FinisherGreensBook.Export(course, Path.Combine(dir, "book.course"));
            Assert.Equal(18, result.Pngs.Count);
            Assert.True(File.Exists(result.Pdf));
            using var codec = SkiaSharp.SKCodec.Create(result.Pngs[0]);
            Assert.Equal(FinisherGreensBook.PageW, codec.Info.Width);
            Assert.Equal(FinisherGreensBook.PageH, codec.Info.Height);
            Assert.EndsWith(Path.Combine("book - Greens Book", "book - Greens Book.pdf"), result.Pdf);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
