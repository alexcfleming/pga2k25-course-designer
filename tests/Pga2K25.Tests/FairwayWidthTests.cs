using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.Readiness;
using Pga2K25.YardageBook;

namespace Pga2K25.Tests;

/// <summary>
/// The fairway width every product prints (yardage book, Publish readiness, Hole Data Card), measured as in game: edge
/// to edge, square to the line of play, at a station placed by straight-line distance from the back tee.
/// </summary>
[Trait("Area", "Products")]
public class FairwayWidthTests
{
    static List<Vec2> Strip(Vec2 from, Vec2 to, double width)
    {
        var d = (to - from).Normalized();
        var n = new Vec2(-d.Z, d.X) * (width / 2);
        return [from + n, to + n, to - n, from - n];
    }

    [Fact]
    public void Station_OnADogleg_IsAtItsStraightLineDistanceFromTheTee()
    {
        // 200 m out along X, then a 60° turn: the 250 m station is 250 m from the tee, not 250 m along the line.
        var bend = new Vec2(200, 0);
        var line = new List<Vec2> { new(0, 0), bend, bend + new Vec2(Math.Cos(Math.PI / 3), Math.Sin(Math.PI / 3)) * 200 };
        var st = FairwayArea.Station(line, 250)!.Value;
        Assert.Equal(250, Vec2.Distance(line[0], st.Point), 3);
        Assert.True(Vec2.Distance(bend, st.Point) > 50, "past the bend by more than the 50 m left along the line");
        Assert.Equal(Math.Cos(Math.PI / 3), st.Direction.X, 3);   // on the second leg
        Assert.Null(FairwayArea.Station(line, 1000));            // the line never gets that far from the tee
    }

    [Fact]
    public void Width_OnAnAngledLeg_IsSquareToTheLine()
    {
        // A 30 m fairway along a leg 40° off the tee's first heading; across the page it would read 30 / cos 40° ≈ 39 m.
        var bend = new Vec2(150, 0);
        var far = bend + new Vec2(Math.Cos(0.7), Math.Sin(0.7)) * 250;
        var area = new FairwayArea([(Strip(bend - new Vec2(Math.Cos(0.7), Math.Sin(0.7)) * 40, far, 30), 1)], []);
        var w = area.WidthAt([new Vec2(0, 0), bend, far], 260)!.Value;
        Assert.Equal(30, Vec2.Distance(w.Left, w.Right), 0);
        Assert.Equal(0, w.Gap);
    }

    [Fact]
    public void Width_RunsEdgeToEdge_ThroughABunker()
    {
        // The fairway spline runs under a bunker painted on it: the width is the fairway's, edge to edge.
        var area = new FairwayArea([(Strip(new Vec2(0, 0), new Vec2(400, 0), 36), 1)], []);
        var w = area.WidthAt([new Vec2(0, 0), new Vec2(400, 0)], 280)!.Value;
        Assert.Equal(36, Vec2.Distance(w.Left, w.Right), 0);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void YardageBook_AndReadiness_PrintTheSameWidths()
    {
        // Cassimbo: every driver-distance station both products measure agrees to the yard, except where the yardage
        // book leaves out another hole's fairway (readiness counts shared fairways).
        var course = Samples.Cassimbo;
        var layers = CourseLayers.From(course);
        double aspect = YardagePageRenderer.MapRect.Width / YardagePageRenderer.MapRect.Height;
        var ready = ReadinessChecker.FairwayWidths(course).ToDictionary(h => h.Number);
        int compared = 0, same = 0;
        foreach (var h in course.Holes.Where(h => h.Par >= 4))
        {
            var a = HoleAnalysis.Analyze(course, layers, h, new YardageOptions(), aspect);
            foreach (var w in a.Widths)
            {
                var r = ready[h.Number].Driver.FirstOrDefault(s => s.FromTeeYards == w.StationYards);
                if (r?.WidthYards is not double rw) continue;
                compared++;
                if (Math.Abs(rw - w.Yards) <= 1) same++;
            }
        }
        Assert.True(compared >= 20, $"compared {compared}");
        Assert.True(same >= compared * 0.8, $"{same} of {compared} agree");
    }
}
