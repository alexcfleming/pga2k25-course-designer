using System.Text.Json.Nodes;
using Pga2K25.Finisher;
using Pga2K25.Finisher.Operations;
using Pga2K25.Finisher.Support;

namespace Pga2K25.Tests;

/// <summary>Byte-exact Python behaviour for everything the Finisher persists or hashes (expected values from CPython 3.14).</summary>
public class FinisherPythonCompatTests
{
    [Theory]
    [InlineData(2.675, 3, "2.675")]
    [InlineData(2.675, 2, "2.67")]     // binary 2.67499999…
    [InlineData(0.125, 2, "0.12")]     // exact tie → even
    [InlineData(0.375, 2, "0.38")]
    [InlineData(1234.5678, 3, "1234.568")]
    [InlineData(1e-05, 3, "0.0")]
    [InlineData(-0.00049, 3, "-0.0")]
    [InlineData(0.30000000000000004, 3, "0.3")]
    [InlineData(1e16, 3, "1e+16")]
    public void Round_MatchesPython(double x, int digits, string expected) => Assert.Equal(expected, Py.Repr(Py.Round(x, digits)));

    [Theory]
    [InlineData(2.675, "2.675")]
    [InlineData(1e-05, "1e-05")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(123456789012345680.0, "1.2345678901234568e+17")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(-0.0, "-0.0")]
    [InlineData(100.0, "100.0")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    public void Repr_MatchesPython(double x, string expected) => Assert.Equal(expected, Py.Repr(x));

    [Theory]
    [InlineData(0.0625, 3, "0.062")]
    [InlineData(-0.0001, 3, "-0.000")]
    [InlineData(2.0005, 3, "2.001")]
    [InlineData(1.0005, 3, "1.000")]
    [InlineData(-2.5, 0, "-2")]
    [InlineData(3.5, 0, "4")]
    [InlineData(-20.0004999, 3, "-20.000")]
    public void Fixed_MatchesPython(double x, int digits, string expected) => Assert.Equal(expected, Py.Fixed(x, digits));

    [Fact]
    public void HeavyRoughTailSignature_MatchesThePythonTool()
    {
        var items = new[] { HeavyRough.Brush(512.3456789, -20.0004999, 10.0), HeavyRough.Brush(-0.00049, 3.14159265, 13.0) };
        Assert.Equal(
            """[{"holeId":-1,"position":{"x":512.346,"y":"-Infinity","z":-20.0},"rotation":{"x":0.0,"y":0.0,"z":0.0},"scale":{"x":10.0,"y":1.0,"z":10.0},"surfaceCategory":4,"type":8,"value":1.0},{"holeId":-1,"position":{"x":-0.0,"y":"-Infinity","z":3.142},"rotation":{"x":0.0,"y":0.0,"z":0.0},"scale":{"x":13.0,"y":1.0,"z":13.0},"surfaceCategory":4,"type":8,"value":1.0}]""",
            Py.Dumps(Json.Array(items.Select(i => (JsonNode?)i.DeepClone())), sortKeys: true, roundFloats: 3));
        Assert.Equal("56fd0849715d7afd1c0d7120cce3108fa801409a31ed9f624b6ca90010195545", Provenance.TailSignature(items));
    }

    [Fact]
    public void WeatheringRandom_MatchesThePythonTool() =>
        Assert.Equal(0.17753396500161173, TopographicWeathering.Rand01("green", 7, 512.3456789, -20.0004999, "type"), 15);

    [Fact]
    public void GeneratedFloatsKeepTheGamesDecimalPoint()
    {
        var o = new JsonObject { ["a"] = Json.F(1.0), ["b"] = Json.F(-50), ["c"] = Json.I(3) };
        Assert.Equal("""{"a":1.0,"b":-50.0,"c":3}""", o.ToJsonString());
    }
}

public class FinisherSupportTests
{
    [Fact]
    public void KdTree_AgreesWithBruteForce()
    {
        var rng = new Random(7);
        var pts = Enumerable.Range(0, 3000).Select(_ => (rng.NextDouble() * 500, rng.NextDouble() * 300)).ToList();
        pts.AddRange(Enumerable.Range(0, 50).Select(i => (100.0, 100.0 + (i % 5))));   // duplicates and ties
        var tree = new KdTree(pts);
        Span<double> d = stackalloc double[12];
        Span<int> idx = stackalloc int[12];
        for (int q = 0; q < 300; q++)
        {
            double x = rng.NextDouble() * 520 - 10, z = rng.NextDouble() * 320 - 10;
            int k = 1 + q % 12;
            int found = tree.Query(x, z, k, d, idx);
            var brute = pts.Select((p, i) => (Dist: Math.Sqrt((p.Item1 - x) * (p.Item1 - x) + (p.Item2 - z) * (p.Item2 - z)), i))
                .OrderBy(e => e.Dist).ThenBy(e => e.i).Take(k).ToList();
            Assert.Equal(k, found);
            for (int j = 0; j < k; j++) Assert.Equal(brute[j].Dist, d[j], 9);
        }
    }

    [Fact]
    public void NumPyHelpers_MatchNumPy()
    {
        Assert.Equal(new double[] { 0.0, 0.5, 1.0, 1.5 }, NumPy.Arange(0, 1.5 + 1e-6, 0.5));
        Assert.Equal(2.5, NumPy.Percentile([1, 2, 3, 4], 50));
        Assert.Equal(1.3, NumPy.Percentile([1, 2, 3, 4], 10), 12);
        Assert.Equal(15.0, NumPy.Interp(1.5, [1.0, 2.0], [10.0, 20.0]));
        Assert.Equal(10.0, NumPy.Interp(0.0, [1.0, 2.0], [10.0, 20.0]));
        var smoothed = NumPy.GaussianFilter1D([0, 0, 0, 1, 0, 0, 0], 1.0);
        Assert.Equal(1 / (1 + 2 * (Math.Exp(-0.5) + Math.Exp(-2) + Math.Exp(-4.5) + Math.Exp(-8))), smoothed[3], 12);   // kernel truncated at 4σ
        Assert.Equal(smoothed[2], smoothed[4], 12);   // scipy.ndimage.gaussian_filter1d([0,0,0,1,0,0,0], 1)[3]
    }

    [Fact]
    public void PlaneFit_RecoversAPlaneFarFromTheOrigin()
    {
        double[] xs = [500, 501, 502, 500, 501, 502], zs = [-300, -300, -300, -299, -299, -299];
        var vs = xs.Zip(zs, (x, z) => 0.03 * x - 0.02 * z + 7).ToArray();
        Assert.True(LinAlg.FitPlane(xs, zs, vs, ReadOnlySpan<double>.Empty, out double a, out double b, out double c));
        Assert.Equal(0.03, a, 9);
        Assert.Equal(-0.02, b, 9);
        Assert.Equal(7, c, 6);
    }
}
