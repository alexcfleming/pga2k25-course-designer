using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

public class GreenSizeTests
{
    static CourseModel Load(string name) => CourseFile.Load(Path.Combine(TestPaths.SamplesDir, name)).ReadModel();

    [Fact]
    public void Perimeter_OfASquare()
    {
        var square = new List<Vec2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        Assert.Equal(40, Geometry.Perimeter(square), 9);
    }

    [Theory]
    [InlineData(91.6, -8.4)]
    [InlineData(100, 0)]
    [InlineData(120.04, 0)]      // prints as 120.0, so it's on target
    [InlineData(138.2, 18.2)]
    public void PerimeterOffTarget_IsMeasuredFromTheNearestEdge(double yards, double expected) =>
        Assert.Equal(expected, GreenSizes.PerimeterOffTarget(yards), 6);

    [Fact]
    public void Shinnecock_OnlyDrawnGreensAreCounted()
    {
        var sizes = GreenSizes.From(Load("shinnecockhills.course"));
        Assert.Equal(18, sizes.Greens.Count);
        Assert.Equal(new[] { 1, 18 }, sizes.Built.Select(g => g.Hole));        // the other 16 are game-generated
        Assert.All(sizes.Greens.Where(g => g.Estimated), g => Assert.Null(sizes.Rank(g.Hole)));
        Assert.Equal(sizes.Built.Average(g => g.SquareFeet), sizes.MeanSquareFeet, 6);
        Assert.Equal(sizes.MeanSquareFeet, sizes.MedianSquareFeet, 6);           // two greens: median = mean
    }

    [Fact]
    public void Cassimbo_StatisticsAndRanks()
    {
        var sizes = GreenSizes.From(Load("cassimbo.course"));
        Assert.Equal(18, sizes.Built.Count);
        var areas = sizes.Built.Select(g => g.SquareFeet).OrderBy(a => a).ToList();
        Assert.Equal((areas[8] + areas[9]) / 2, sizes.MedianSquareFeet, 6);
        double sd = Math.Sqrt(areas.Sum(a => Math.Pow(a - areas.Average(), 2)) / areas.Count);
        Assert.Equal(sd, sizes.StdDevSquareFeet, 6);
        var largest = sizes.Built.MaxBy(g => g.SquareFeet)!;
        var smallest = sizes.Built.MinBy(g => g.SquareFeet)!;
        Assert.Equal(1, sizes.Rank(largest.Hole));
        Assert.Equal(18, sizes.Rank(smallest.Hole));
        Assert.Equal(Enumerable.Range(1, 18), sizes.Built.Select(g => sizes.Rank(g.Hole)!.Value).Order());
    }

    [Fact]
    public void Sizes_MatchTheGreensBookOutline()
    {
        // The card, the sheet captions and the study panel must all quote the same area and perimeter.
        var course = Load("shinnecockhills.course");
        var sizes = GreenSizes.From(course);
        var a = GreenAnalyzer.Analyze(course, course.Holes[0], new GreensBookOptions());
        Assert.Equal(a.AreaSquareMetres, sizes.For(1)!.AreaSquareMetres, 6);
        Assert.Equal(a.PerimeterMetres, sizes.For(1)!.PerimeterMetres, 6);
    }

    [Fact]
    public void ArrowsAt_ThinsTheLattice()
    {
        var course = Load("shinnecockhills.course");
        var options = new GreensBookOptions();
        var a = GreenAnalyzer.Analyze(course, course.Holes[0], options);
        Assert.Same(a.Arrows, GreenAnalyzer.ArrowsAt(a, options.ArrowSpacingMetres, options));
        var sparse = GreenAnalyzer.ArrowsAt(a, options.ArrowSpacingMetres * 2, options);
        Assert.NotEmpty(sparse);
        Assert.True(sparse.Count < a.Arrows.Count * 0.5, $"{sparse.Count} vs {a.Arrows.Count}");
    }

    [Fact]
    public void CourseCards_DrawTheGreenSizeCard()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-green-card-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new CourseCardConfig { Dpi = 30, Formats = ["png"] };
            var sagan = CourseCardBuilder.Build(Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course"), dir, config);
            Assert.Contains(sagan.Files, f => f.EndsWith("Green Size Card.png"));

            // One drawn green (hole 1) on a routed plot: the card is still drawn, with the other 17 hatched.
            var murray = CourseCardBuilder.Build(Path.Combine(TestPaths.SamplesDir, "murray-estate.course"), dir, config, force: true);
            Assert.Equal(new[] { 1 }, murray.Data.Greens.Built.Select(g => g.Hole));
            Assert.Equal(0, murray.Data.Greens.StdDevSquareFeet);
            Assert.Contains(murray.Files, f => f.EndsWith("Green Size Card.png"));
            Assert.Contains(murray.Issues, i => i.Message.StartsWith("Green Size Card: holes 2, 3,"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
