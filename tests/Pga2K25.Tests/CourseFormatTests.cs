using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class CourseFormatTests
{
    public static IEnumerable<object[]> SampleCourses() =>
        Directory.GetFiles(TestPaths.SamplesDir, "*.course").Select(p => new object[] { Path.GetFileName(p) });

    [Theory]
    [MemberData(nameof(SampleCourses))]
    public void RoundTrip_PreservesEveryDocument(string fileName)
    {
        var original = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, fileName));
        var reloaded = CourseFile.Load(original.ToBytes());

        Assert.Equal(original.Blobs.Select(b => b.Key), reloaded.Blobs.Select(b => b.Key));
        foreach (var (name, node) in original.Blobs)
            Assert.True(JsonNode.DeepEquals(node, reloaded.GetBlob(name)), $"{name} changed on round-trip");
        Assert.True(JsonNode.DeepEquals(original.Envelope, reloaded.Envelope));
    }

    [Theory]
    [MemberData(nameof(SampleCourses))]
    public void RoundTrip_KeepsNegativeInfinitySentinelAsString(string fileName)
    {
        var reloaded = CourseFile.Load(CourseFile.Load(Path.Combine(TestPaths.SamplesDir, fileName)).ToBytes());
        var y = reloaded.Description["height"]![0]!["position"]!["y"]!;
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
        Assert.Equal(CourseModel.Num(meta["length"]), model.Holes.Sum(h => h.RouteLength), 1);

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
                Assert.Equal(CourseModel.Num(teeSets[set]!["holeLengths"]![i]), hole.TeeSetLength(set), 2);
        }
    }

    static void AssertPoints(IReadOnlyList<Vec2> actual, JsonArray expected)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(CourseModel.Num(expected[i]!["x"]), actual[i].X, 2);
            Assert.Equal(CourseModel.Num(expected[i]!["z"]), actual[i].Z, 2);
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
