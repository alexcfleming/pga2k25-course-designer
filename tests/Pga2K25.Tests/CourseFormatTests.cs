using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class CourseFormatTests
{
    public static IEnumerable<object[]> SampleCourses() =>
        Directory.GetFiles(TestPaths.SamplesDir, "*.course").Select(p => new object[] { Path.GetFileName(p) });

    [Theory]
    [MemberData(nameof(SampleCourses))]
    public void Load_DecodesEveryDocument(string fileName)
    {
        var file = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, fileName));

        Assert.Contains(file.Blobs, b => b.Key == CourseFile.DescriptionKey);
        Assert.Contains(file.Blobs, b => b.Key == CourseFile.MetadataKey);
        Assert.All(file.Blobs, b => Assert.NotNull(b.Value));
        Assert.False(file.Envelope.ContainsKey("binaryData"));   // decoded into Blobs
    }

    [Theory]
    [MemberData(nameof(SampleCourses))]
    public void Load_KeepsNegativeInfinitySentinelAsString(string fileName)
    {
        var file = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, fileName));
        var y = file.Description["height"]![0]!["position"]!["y"]!;
        Assert.Equal("-Infinity", y.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(SampleCourses))]
    public void Model_MatchesMetadataDerivations(string fileName)
    {
        var file = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, fileName));
        var model = file.ReadModel();
        var meta = file.Metadata!;

        Assert.Equal(GameVersion.PGA2K25, model.Version);
        Assert.Equal((int)CourseModel.Num(meta["holes"]), model.Holes.Count);
        Assert.Equal((int)CourseModel.Num(meta["par"]), model.Holes.Sum(h => h.Par));
        // CourseMetadata.length is the sum of back-tee route polylines.
        Assert.Equal(CourseModel.Num(meta["length"]), model.Holes.Sum(h => h.RouteLength), tolerance: 0.1);

        // The game's metadata key names are swapped: "teePositions" holds absolute pins
        // and "pinPositions" holds tees. Our model reads pins as offsets from the green anchor.
        var metaPins = meta["teePositions"]!.AsArray();
        var metaTees = meta["pinPositions"]!.AsArray();
        var teeSets = meta["teeSetInfos"]!.AsArray();
        for (int i = 0; i < model.Holes.Count; i++)
        {
            var hole = model.Holes[i];
            AssertPoints(hole.Pins, metaPins[i]!.AsArray());
            AssertPoints(hole.Tees, metaTees[i]!.AsArray());
            for (int set = 0; set < teeSets.Count; set++)
                Assert.Equal(CourseModel.Num(teeSets[set]!["holeLengths"]![i]), hole.TeeSetLength(set), tolerance: 0.01);
        }
    }

    static void AssertPoints(IReadOnlyList<Vec2> actual, JsonArray expected)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(CourseModel.Num(expected[i]!["x"]), actual[i].X, tolerance: 0.01);
            Assert.Equal(CourseModel.Num(expected[i]!["z"]), actual[i].Z, tolerance: 0.01);
        }
    }
}

static class TestPaths
{
    public static string RepoRoot { get; } = FindRoot();
    public static string SamplesDir => Path.Combine(RepoRoot, "samples", "courses");

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pga2K25.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate Pga2K25.sln above the test output.");
    }
}
