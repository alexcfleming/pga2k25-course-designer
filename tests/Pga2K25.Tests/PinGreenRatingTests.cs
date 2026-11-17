using Pga2K25.HoleSim;
using Pga2K25.Readiness;

namespace Pga2K25.Tests;

/// <summary>"Rate pins from the greens": the across-the-course thirds, steep cups, and the designer's own pins kept.</summary>
[Trait("Area", "Readiness")]
public class PinGreenRatingTests
{
    static PinGreenResult Pin(int hole, int pin, double avg, bool steep = false) => new(hole, pin, avg, 0.5, steep ? 9 : 1, steep);

    [Fact]
    public void Rate_SplitsTheWholeCourseIntoThirdsByAveragePutts()
    {
        // Two holes, six pins: the two with the most putts are Hard, the two with the fewest Easy, wherever they are.
        var pins = new[] { Pin(1, 1, 2.60), Pin(1, 2, 2.50), Pin(1, 3, 2.30), Pin(2, 1, 2.20), Pin(2, 2, 2.05), Pin(2, 3, 1.95) };
        var r = PinGreenRatings.Rate(pins);
        Assert.Equal(["R", "R", "A", "A", "G", "G"], pins.Select(p => r[(p.Hole, p.Pin)]));
    }

    [Fact]
    public void Rate_AGreenCanBeAllHard_AndASteepCupIsAlwaysHard()
    {
        // Across the course, not within a hole: hole 1's big green takes all three Hard places.
        var pins = Enumerable.Range(1, 3).Select(n => Pin(1, n, 2.6 + n * 0.01))
            .Concat(Enumerable.Range(1, 6).Select(n => Pin(2, n, 2.0 + n * 0.01))).ToList();
        pins[^1] = Pin(2, 6, 1.5, steep: true);   // the easiest by putts, but no ball stops by the cup
        var r = PinGreenRatings.Rate(pins);
        Assert.All(Enumerable.Range(1, 3), n => Assert.Equal("R", r[(1, n)]));
        Assert.Equal("R", r[(2, 6)]);
    }

    [Fact]
    public void Fill_KeepsTheDesignersOwnPins_AndReplacesItsOwnRatings()
    {
        var state = new ReadinessState();
        state.Pins["1.1"] = new PinState { Difficulty = "G" };                     // set by hand
        state.Pins["1.2"] = new PinState { Difficulty = "A", FromGreens = true };  // an earlier rating
        var ratings = new[]
        {
            new GreenPinRating("1.1", "R", 2.6, 0.8, 1.0, false),
            new GreenPinRating("1.2", "R", 2.5, 0.7, 1.0, false),
            new GreenPinRating("1.3", "G", 2.0, 0.1, 0.4, false),
        };
        int filled = state.FillPinsFromGreens(ratings, "STAMP", 80);
        Assert.Equal(2, filled);
        Assert.Equal(("G", false), (state.Pins["1.1"].Difficulty, state.Pins["1.1"].FromGreens));   // yours, untouched
        Assert.Equal(2.6, state.Pins["1.1"].AveragePutts);                                          // but its figures are kept
        Assert.Equal(("R", true), (state.Pins["1.2"].Difficulty, state.Pins["1.2"].FromGreens));
        Assert.Equal(("G", true), (state.Pins["1.3"].Difficulty, state.Pins["1.3"].FromGreens));
        Assert.Equal(("STAMP", 3), (state.PinRatings!.Stamp, state.PinRatings.Pins));

        // Saved and read back with the rest of Readiness.json.
        var back = System.Text.Json.JsonSerializer.Deserialize<ReadinessState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        Assert.True(back.Pins["1.3"].FromGreens);
        Assert.Equal("STAMP", back.PinRatings!.Stamp);
    }

    [Trait("Speed", "Slow")]
    [Fact]
    public void Measure_PlaysEveryPinOnEveryHole()
    {
        var results = PinGreenRatings.Measure(Path.Combine(TestPaths.SamplesDir, "cassimbo.course"));
        Assert.Equal(72, results.Count);   // Cassimbo: 18 holes, four pins each
        Assert.All(results, p => Assert.InRange(p.AveragePutts, 1.5, 3.5));
        // Hole 2's small green (174 sq yd) putts easier than hole 7's big one (1,369 sq yd).
        Assert.True(results.Where(p => p.Hole == 2).Average(p => p.AveragePutts) < results.Where(p => p.Hole == 7).Average(p => p.AveragePutts));
        var rated = PinGreenRatings.Rate(results);
        Assert.Equal(24, rated.Values.Count(v => v == "R"));
        Assert.Equal(24, rated.Values.Count(v => v == "G"));
    }
}
