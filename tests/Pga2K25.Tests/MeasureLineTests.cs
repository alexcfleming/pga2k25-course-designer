using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;
using Pga2K25.Studio.Services;

namespace Pga2K25.Tests;

/// <summary>
/// Measure lines saved in the course file (<c>distanceMarkers[]</c>): read into lines, put to the Measurements page's
/// route legs, and put to the hole simulator's live-check spots.
/// </summary>
[Trait("Area", "Core")]
public class MeasureLineTests
{
    const double Ft = 0.3048;

    /// <summary>The file's own layout: each leg is two entries pointing at each other; a point shared by two legs is repeated.</summary>
    static JsonArray Markers(params (double X, double Y, double Z)[][] lines)
    {
        static JsonObject V((double X, double Y, double Z) p) => new() { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z };
        var a = new JsonArray();
        foreach (var line in lines)
            for (int i = 0; i + 1 < line.Length; i++)
            {
                a.Add(new JsonObject { ["position"] = V(line[i]), ["adjacentMarkers"] = new JsonArray(V(line[i + 1])) });
                a.Add(new JsonObject { ["position"] = V(line[i + 1]), ["adjacentMarkers"] = new JsonArray(V(line[i])) });
            }
        return a;
    }

    static IReadOnlyList<MeasureLine> Lines(params (double X, double Y, double Z)[][] lines) => MeasureLines.Read(Markers(lines));

    [Fact]
    public void Legs_join_into_lines_in_click_order()
    {
        // Halvoy's first line (three legs) and a single leg, as the game saved them.
        var lines = Lines(
            [(65.48, 14.084, -262.36), (214.34, 3.472, -33.79), (304.52, 9.205, -111.65), (95.48, 14.028, -284.42)],
            [(97.0, 13.433, -352.26), (245.98, 6.746, -508.75)]);
        Assert.Equal(2, lines.Count);
        var three = lines.Single(l => l.Points.Count == 4);
        var xs = three.Points.Select(p => p.Position.X).ToList();
        Assert.True(xs.SequenceEqual([65.48, 214.34, 304.52, 95.48]) || xs.SequenceEqual([95.48, 304.52, 214.34, 65.48]));
        Assert.Equal(3.472, three.Points.Single(p => p.Position.X == 214.34).Height, 6);
        Assert.Equal(3, three.Legs().Count());
    }

    [Fact]
    public void Berwick_coasts_three_measure_legs_are_read()
    {
        var course = Samples.Course("berwick-coast.course");
        Assert.Equal(3, course.MeasureLines.Count);
        Assert.All(course.MeasureLines, l => Assert.Equal(2, l.Points.Count));
        Assert.Equal(13.1686859, course.MeasureLines.SelectMany(l => l.Points).First(p => Math.Abs(p.Position.X - 648.283936) < 1e-3).Height, 5);
    }

    [Fact]
    public void A_course_without_measure_lines_has_none() => Assert.Empty(Samples.Cassimbo.MeasureLines);

    static Hole TestHole => new(7, 4, [new Vec2(0, 0), new Vec2(0, 250), new Vec2(30, 380)], [new Vec2(0, 0)], [new Vec2(30, 380)]);

    [Fact]
    public void Route_legs_come_from_a_line_through_the_markers()
    {
        // Tee 2 m off its marker, the aim exactly, the green 3 m off; heights 10, 13.048 and 11.524 m.
        var lines = Lines([(1.5, 10, -1.2), (0, 13.048, 250), (32, 11.524, 382)]);
        var legs = Measurements.FromLines([TestHole], lines);
        Assert.Equal(2, legs.Count);
        var teeAim = legs.Single(l => l.TeeToAim);
        Assert.Equal(10, teeAim.Feet, 6);           // 3.048 m up
        Assert.Equal(-5, legs.Single(l => !l.TeeToAim).Feet, 6);
        Assert.All(legs, l => { Assert.True(l.FromCourseFile); Assert.Equal(7, l.Hole); });
        Assert.Equal((int)Math.Round(Vec2.Distance(new Vec2(1.5, -1.2), new Vec2(0, 250)) / 0.9144), teeAim.Yards);
    }

    [Fact]
    public void A_leg_clicked_green_first_still_reads_uphill_from_the_aim()
    {
        var legs = Measurements.FromLines([TestHole], Lines([(30, 11.524, 380), (0, 13.048, 250)]));
        Assert.Equal(-5, Assert.Single(legs).Feet, 6);
    }

    [Fact]
    public void Points_off_the_markers_are_not_route_legs()
    {
        // 8 m from the aim marker.
        Assert.Empty(Measurements.FromLines([TestHole], Lines([(0, 10, 0), (8, 13, 250)])));
    }

    [Fact]
    public void Other_measure_legs_are_listed_by_their_nearest_hole()
    {
        var lines = Lines(
            [(0, 10, 0), (0, 13.048, 250)],                  // the tee → aim leg: a route leg, not listed here
            [(20, 10, 100), (40, 11.524, 120)],              // beside hole 7's line
            [(900, 5, 900), (950, 6, 950)]);                 // nowhere near a hole
        var other = Measurements.OtherLegs([TestHole], lines).OrderBy(l => l.From.X).ToList();
        Assert.Equal(2, other.Count);
        Assert.Equal(7, other[0].Hole);
        Assert.Equal(5, other[0].Feet, 6);
        Assert.Equal(0, other[1].Hole);
    }

    [Fact]
    public void The_course_files_leg_replaces_a_typed_one_and_is_never_saved()
    {
        var file = new MeasuredLeg(7, true, 251, 10.0, FromCourseFile: true);
        var typed = new[] { new MeasuredLeg(7, true, 250, 9.5), new MeasuredLeg(7, false, 140, -5.2) };
        var all = Measurements.Combine([file], typed);
        Assert.Equal(2, all.Count);
        Assert.Contains(file, all);
        Assert.Contains(typed[1], all);

        string path = Path.Combine(Path.GetTempPath(), $"measure-{Guid.NewGuid():N}.csv");
        try
        {
            Measurements.Save(path, all);
            Assert.Equal([typed[1]], Measurements.Load(path));
        }
        finally { File.Delete(path); }
    }

    // ───────── live checks ─────────

    static readonly Lazy<HoleModel> Cassimbo3 = new(() =>
    {
        var file = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "cassimbo.course"));
        return HoleModelBuilder.Build(file.ReadModel(), file.Description, 3);
    });

    /// <summary>A measure line through <paramref name="frame"/> points on the hole, with height (m) from <paramref name="height"/>.</summary>
    static IReadOnlyList<MeasureLine> LineThrough(HoleModel hole, IEnumerable<FramePoint> frame, Func<FramePoint, double> height) =>
        Lines(frame.Select(p => { var w = hole.Frame.ToWorld(p); return (w.X, height(p), w.Z); }).ToArray());

    [Fact]
    public void Live_check_spots_fill_from_a_line_laid_through_them()
    {
        var hole = Cassimbo3.Value;
        var spots = TerrainGate.SuggestSpots(hole);
        // The pattern: the tee, then each spot clicked a yard or two off, then one more point by the line, then one far away.
        var off = new FramePoint(1.5, -1);
        var clicks = new List<FramePoint> { new(0.5, 0.5) };
        clicks.AddRange(spots.Select(s => s.Name == "G" ? s.Where + new FramePoint(0.5, 0.5) : s.Where + off));
        var extra = new FramePoint(25, hole.GreenFrame.Along / 2 + 7);
        clicks.Add(extra);
        clicks.Add(new FramePoint(200, 100));
        double H(FramePoint p) => 20 + p.Along * 0.01 + p.Lat * 0.02;   // metres
        var file = FileReadings.Match(LineThrough(hole, clicks, H), hole, spots);

        Assert.NotNull(file.Tee);
        Assert.Equal(spots.Select(s => s.Name), file.Spots.Select(s => s.Spot));
        Assert.Equal(H(new FramePoint(0.5, 0.5)) / Ft, file.Tee!.HeightFt, 4);
        // Each reading is where the point is, not where the spot is.
        var m1 = file.Spots.Single(s => s.Spot == "M1");
        Assert.Equal(spots.Single(s => s.Name == "M1").Where + off, m1.Where, new FrameNear());
        Assert.Equal(H(m1.Where) / Ft, m1.HeightFt, 3);
        var d = Assert.Single(file.Extra);
        Assert.Equal("D1", d.Spot);
        Assert.Equal(extra, d.Where, new FrameNear());
        Assert.NotNull(file.Green);
    }

    [Fact]
    public void Green_spots_take_only_points_close_by()
    {
        var hole = Cassimbo3.Value;
        var greenSpot = new MeasureSpot("P1a", hole.GreenFrame + new FramePoint(3, 0), 0, 0, false);
        // 1.5 yds off fills it; 3 yds off doesn't (and, being by the line, is an extra reading instead).
        var near = FileReadings.Match(LineThrough(hole, [new(0, 0), greenSpot.Where + new FramePoint(0, 1.5)], _ => 10), hole, [greenSpot]);
        Assert.Equal("P1a", Assert.Single(near.Spots).Spot);
        var far = FileReadings.Match(LineThrough(hole, [new(0, 0), greenSpot.Where + new FramePoint(0, 3)], _ => 10), hole, [greenSpot]);
        Assert.Empty(far.Spots);
        Assert.Single(far.Extra);
    }

    [Fact]
    public void One_point_fills_one_spot_and_the_nearest_pairs_go_first()
    {
        var hole = Cassimbo3.Value;
        var a = new MeasureSpot("M1", new FramePoint(0, 100), 100, 0, false);
        var b = new MeasureSpot("M2", new FramePoint(0, 108), 108, 0, false);
        // One point 1 yd from M2 and 7 from M1: it goes to M2, and M1 stays empty.
        var file = FileReadings.Match(LineThrough(hole, [new(0, 0), new(0, 107)], _ => 10), hole, [a, b]);
        Assert.Equal("M2", Assert.Single(file.Spots).Spot);
        Assert.Empty(file.Extra);
    }

    [Fact]
    public void Without_measure_lines_there_is_nothing_to_read()
    {
        var hole = Cassimbo3.Value;
        var file = FileReadings.Match(Samples.Cassimbo, hole, TerrainGate.SuggestSpots(hole));
        Assert.False(file.Any);
        Assert.Equal(0, file.PointsInFile);
        Assert.Null(file.Tee);
    }

    sealed class FrameNear : IEqualityComparer<FramePoint>
    {
        public bool Equals(FramePoint x, FramePoint y) => x.DistanceTo(y) < 0.01;
        public int GetHashCode(FramePoint p) => 0;
    }
}
