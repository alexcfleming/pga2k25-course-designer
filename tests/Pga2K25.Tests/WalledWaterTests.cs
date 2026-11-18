using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;

namespace Pga2K25.Tests;

/// <summary>
/// Walled water: a bunker outline filled with water planes, the way designers who skip the retaining-wall tool build a
/// stream (Alex's Berwick Coast stream). Every product draws it as water with a wall and plays it as water.
/// </summary>
[Trait("Area", "Water")]
public class WalledWaterTests
{
    static CourseModel Berwick => Samples.Course("berwick-coast.course");

    [Fact]
    public void Berwick_TheStreamIsOneWalledStream_HoldingEveryPlane()
    {
        var found = WalledWaterRules.Find(Berwick);
        var stream = Assert.Single(found);
        Assert.True(stream.IsStream);
        Assert.InRange(stream.Compactness, 0.01, 0.03);               // 850 m × 5 m
        Assert.Equal(8.51, stream.Level, 2);
        Assert.Equal(55, stream.PlaneIndices.Count);                   // 48 inside, 7 within 4.4 m of the edge
        Assert.Equal(Surfaces.Bunker, Berwick.SurfaceSplines[stream.SplineIndex].Surface);
    }

    [Fact]
    public void Berwick_LayersDrawTheStreamAsWaterWithAWall_NotAsABunker()
    {
        var course = Berwick;
        var layers = CourseLayers.From(course);
        var stream = WalledWaterRules.Find(course)[0];
        Assert.Single(layers.WaterWalls);
        Assert.Single(layers.WaterWallOver);
        // Drawn by the wall pass after the turf, not with the other water.
        Assert.DoesNotContain(layers.WaterAreas, a => ReferenceEquals(a, layers.WaterWalls[0]));
        // The outline is no longer a bunker: no bunker area covers the middle of the stream.
        var middle = stream.Outline[stream.Outline.Count / 4];
        Assert.DoesNotContain(layers.BunkerAreas, b => Geometry.Contains(b, middle));
        Assert.Empty(layers.BunkerAreas.Where(b => Math.Abs(Geometry.Area(b)) > 4000));
    }

    [Fact]
    public void Berwick_ThePlanesInTheWallAreNotTracedAgain()
    {
        // Traced on their own they spill past the wall wherever the ground dips below 8.51 m.
        var water = GreensBook.WaterTracer.Trace(Berwick, new GreensBook.TerrainModelOptions { CellMetres = 2 });
        Assert.Empty(water.Bodies);
    }

    [Fact]
    public void Berwick_Hole16_CarriesTheStreamAsWater()
    {
        var course = Berwick;
        var layers = CourseLayers.From(course);
        var a = YardageBook.HoleAnalysis.Analyze(course, layers, course.Holes.Single(h => h.Number == 16), new YardageBook.YardageOptions(), 0.4);
        Assert.Single(a.WaterWalls);
        Assert.Contains(a.Water, w => ReferenceEquals(w, a.WaterWalls[0]));
        // The crossing gets its carry numbers, as water does (a bunker that close to the tee would be skipped).
        Assert.Contains(a.BunkerLabels, l => Geometry.DistanceToEdge(a.WaterWalls[0], l.Point) < 1);
    }

    // A tester's course (private, git-ignored): holes 7–9 have ponds built as a bunker outline lined with retaining-wall
    // pieces, inside a water-tool body of water. No water planes.
    const string TesterFile = "9plmwuhnqid5.course";

    [PrivateSampleFact(TesterFile)]
    public void TesterCourse_Holes7To9_PondsInBodiesOfWater_AreWalledPonds()
    {
        var course = Samples.Course(TesterFile, privateDir: true);
        var found = WalledWaterRules.Find(course).OrderBy(w => w.SplineIndex).ToList();
        Assert.Equal([542, 543, 547], found.Select(w => w.SplineIndex));
        Assert.All(found, w => Assert.False(w.IsStream));
        Assert.All(found, w => Assert.Single(w.PathIndices));
        Assert.All(found, w => Assert.Empty(w.PlaneIndices));
        Assert.Equal([39.23, 37.70, 50.92], found.Select(w => Math.Round(w.Level, 2)));
        // The bodies round the walls aren't traced again; the course's other water still is.
        var claimed = WalledWaterRules.ClaimedPaths(course);
        Assert.Equal(3, claimed.Count);
        Assert.All(claimed, i => Assert.True(course.WaterPaths[i].IsBody));

        // Layering by drawing order: hole 9's fairway (spline 685, drawn after the wall) stays on top of that pond;
        // the heavy rough under hole 7's pond (spline 11, drawn before it) is under the water.
        var layers = CourseLayers.From(course);
        int Wall(int spline) => layers.WaterWalls.FindIndex(w => ReferenceEquals(w, found.Single(f => f.SplineIndex == spline).Outline));
        var fairway = Geometry.SampleSpline(course.SurfaceSplines[685], 12);
        Assert.Contains(layers.WaterWallOver[Wall(543)], o => o.SequenceEqual(fairway));
        var heavy = Geometry.SampleSpline(course.SurfaceSplines[11], 12);
        Assert.DoesNotContain(layers.WaterWallOver[Wall(542)], o => o.SequenceEqual(heavy));
    }

    /// <summary>A course with one closed bunker outline and water planes at the given centres.</summary>
    static CourseModel Synthetic(IReadOnlyList<Vec2> outline, IEnumerable<(Vec2 At, double Level)> planes)
    {
        var waypoints = new JsonArray(outline.Select(p => (JsonNode)new JsonObject
        {
            ["pointOne"] = Xz(p), ["pointTwo"] = Xz(p), ["waypoint"] = Xz(p), ["width"] = 1.7,
        }).ToArray());
        var desc = new JsonObject
        {
            ["name"] = "Synthetic",
            ["surfaceSplines2"] = new JsonArray(new JsonObject { ["surface"] = 0, ["state"] = 3, ["width"] = 1.7, ["waypoints"] = waypoints }),
            ["water"] = new JsonArray(planes.Select(w => (JsonNode)new JsonObject
            {
                ["surfaceCategory"] = 9, ["type"] = 72, ["value"] = w.Level,
                ["position"] = new JsonObject { ["x"] = w.At.X, ["y"] = w.Level, ["z"] = w.At.Z },
                ["scale"] = new JsonObject { ["x"] = 10, ["y"] = 1, ["z"] = 10 },
                ["rotation"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
            }).ToArray()),
            ["holes3"] = new JsonArray(),
            ["useV43AltSurfaceSplineScale"] = true,   // marks a PGA 2K25 course
        };
        return CourseModel.From(desc);
        static JsonObject Xz(Vec2 p) => new() { ["x"] = p.X, ["y"] = 0, ["z"] = p.Z };
    }

    static List<Vec2> Circle(double r) => Enumerable.Range(0, 24).Select(i => new Vec2(Math.Cos(i * Math.PI / 12) * r, Math.Sin(i * Math.PI / 12) * r)).ToList();

    [Fact]
    public void ARoundOutlineFullOfPlanes_IsAPond()
    {
        var course = Synthetic(Circle(30), [(new(0, 0), 5), (new(10, 0), 5), (new(-10, 5), 5)]);
        var pond = Assert.Single(WalledWaterRules.Find(course));
        Assert.False(pond.IsStream);
        Assert.InRange(pond.Compactness, 0.9, 1.0);
    }

    [Theory]
    [InlineData(2, 0)]     // two planes: an ordinary bunker beside a pond
    [InlineData(3, 12)]    // three planes, but 12 m outside the edge
    public void AnOrdinaryBunker_StaysABunker(int planes, double beyondEdge)
    {
        var at = Enumerable.Range(0, planes).Select(i => (new Vec2(30 + beyondEdge, i * 4.0), 5.0));
        var course = Synthetic(Circle(30), at);
        Assert.Empty(WalledWaterRules.Find(course));
        Assert.Empty(CourseLayers.From(course).WaterWalls);
    }

    [Fact]
    public void PlanesAtAnotherLevel_DontJoinTheWalledBody()
    {
        var course = Synthetic(Circle(30), [(new(0, 0), 5), (new(10, 0), 5), (new(-10, 5), 5), (new(5, 5), 9)]);
        var pond = Assert.Single(WalledWaterRules.Find(course));
        Assert.Equal(3, pond.PlaneIndices.Count);
        Assert.Equal(5, pond.Level, 3);
    }
}
