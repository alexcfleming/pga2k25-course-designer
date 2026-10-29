using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class GreenOutlineTests
{
    static List<Vec2> Square(double x0, double z0, double x1, double z1) =>
        [new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1)];

    [Fact]
    public void Union_OfTwoOverlappingSquares_IsTheirOuterEdge()
    {
        var a = Square(0, 0, 10, 10);
        var b = Square(5, 5, 15, 15);
        Assert.True(Geometry.Overlaps(a, b));
        var u = Geometry.Union([a, b]);
        Assert.Equal(175, Geometry.Area(u), 6);                 // 100 + 100 − 25
        Assert.Equal(60, Geometry.Perimeter(u), 6);
        Assert.False(Geometry.Overlaps(a, Square(20, 20, 30, 30)));
    }

    [Fact]
    public void Union_KeepsTheWindingOfEitherInput()
    {
        var a = Square(0, 0, 10, 10);
        var b = Square(5, -5, 15, 5);
        b.Reverse();
        Assert.Equal(175, Geometry.Area(Geometry.Union([a, b])), 6);
    }

    const string DevilsPerch = "devils-perch.course";

    [PrivateSampleFact(DevilsPerch)]
    public void DevilsPerch_Par3s_HaveAnApproach()
    {
        // The routing puts each par 3's aim point on the green: tee, green, green. The repeat is dropped.
        var course = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, DevilsPerch)).ReadModel();
        foreach (var h in course.Holes.Where(h => h.Par == 3))
        {
            Assert.Equal(2, h.Route.Count);
            Assert.Equal(1, GreenOutline.Approach(h).Length, 6);
        }
    }

    [PrivateSampleFact(DevilsPerch)]
    public void DevilsPerch_EveryPin_IsOnItsGreen()
    {
        // Greens 2 and 8 are each drawn as two overlapping splines; the book used to show only the smaller one.
        var course = CourseFile.Load(Path.Combine(TestPaths.PrivateDir, DevilsPerch)).ReadModel();
        foreach (var h in course.Holes)
        {
            var outline = GreenOutline.Resolve(course, h);
            Assert.All(h.Pins, p => Assert.True(Geometry.Contains(outline.Points, p), $"hole {h.Number}: pin off the green"));
        }
        var sizes = GreenSizes.From(course);
        Assert.InRange(sizes.For(2)!.SquareFeet, 10_500, 11_200);   // was 6,225 (one spline)
    }
}
