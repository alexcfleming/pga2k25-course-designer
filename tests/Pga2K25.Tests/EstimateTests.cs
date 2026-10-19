using Pga2K25.CourseFormat;
using Pga2K25.Estimate;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

public class EstimateTests
{
    // 200 m × 200 m of flat ground at 1 m cells with a 20 m × 20 m box mound 2 m high in the middle: 800 m³ of mound.
    static Heightfield MoundField(double mound = 2)
    {
        var f = new Heightfield(new Vec2(0, 0), 1, 200, 200);
        for (int z = 90; z < 110; z++)
            for (int x = 90; x < 110; x++) f[x, z] = mound;
        return f;
    }

    static ZoneRaster Raster(Heightfield f, ZoneParams zp, Func<int, int, bool>? worked = null) => new()
    {
        Finished = f,
        Zone = Enumerable.Range(0, f.Values.Length).Select(k => worked is null || worked(k % f.Width, k / f.Width) ? 0 : -1).ToArray(),
        Counted = Enumerable.Repeat(true, f.Values.Length).ToArray(),
        Classes = [zp],
    };

    static readonly ZoneParams Plane = new("plane", 0, 0, 1);

    [Fact]
    public void PlaneBaseline_MoundVolume_MatchesHandCalculation()
    {
        // Best-fit plane is flat at the mean height 800 / 40,000 = 0.02 m.
        // Fill = (2 − 0.02) × 400 = 792 m³ over the mound; cut = 0.02 × 39,600 = 792 m³ everywhere else.
        var r = Earthwork.Compute(Raster(MoundField(), Plane), featherMetres: 0);
        Assert.Equal(792, r.FillCuM, 6);
        Assert.Equal(792, r.CutCuM, 6);
        Assert.Equal(40000, r.WorkedAreaSqM, 6);
        // 792 m³ = 1,035.9 cu yd (1 cu yd = 0.764554857984 m³ exactly).
        Assert.Equal(792 / 0.764554857984, r.FillCuYd, 3);
    }

    [Fact]
    public void Fidelity_ScalesEarthworkLinearly()
    {
        var half = Earthwork.Compute(Raster(MoundField(), Plane with { Fidelity = 0.5 }), 0);
        Assert.Equal(396, half.FillCuM, 6);
        var given = Earthwork.Compute(Raster(MoundField(), Plane with { Fidelity = 1 }), 0);
        Assert.Equal(0, given.FillCuM + given.CutCuM, 9);
        var offset = Earthwork.Compute(Raster(MoundField(), Plane), 0, fidelityOffset: 0.25);
        Assert.Equal(594, offset.FillCuM, 6);
    }

    [Fact]
    public void NaturalGround_HasNoEarthwork()
    {
        var r = Earthwork.Compute(Raster(MoundField(), Plane, (_, _) => false), 10);
        Assert.Equal(0, r.CutCuM + r.FillCuM);
        Assert.Equal(0, r.WorkedAreaSqM);
    }

    [Fact]
    public void FlatGround_HasNoEarthwork_AtAnyWavelength()
    {
        var r = Earthwork.Compute(Raster(MoundField(0), new ZoneParams("w", 0, 50, 1)), 10);
        Assert.Equal(0, r.CutCuM + r.FillCuM, 9);
    }

    [Fact]
    public void Feather_MeetsNaturalGroundWithoutAStep()
    {
        // Only a 60 m square around the mound is worked; the starting ground must equal the finished ground at its edge.
        var f = MoundField();
        bool Worked(int x, int z) => x >= 70 && x < 130 && z >= 70 && z < 130;
        var r = Earthwork.Compute(Raster(f, Plane, Worked), featherMetres: 10);
        for (int x = 80; x < 120; x++)
        {
            Assert.Equal(f[x, 69], r.Baseline[69 * f.Width + x]);    // natural ground untouched
            // The first worked row is 1 m in, so only 1/10 of its built relief counts; 10 m in, all of it does.
            Assert.Equal(0.1 * r.Dz[80 * f.Width + x], r.Dz[70 * f.Width + x], 9);
        }
        Assert.True(r.FillCuM > 0);
    }

    [Fact]
    public void Wavelength_KeepsBroadLandformsAsNatural()
    {
        // A broad hill (σ = 60 m) mostly survives a 100 yd wavelength; a sharp 20 m box mound mostly counts as built.
        var broad = new Heightfield(new Vec2(0, 0), 1, 200, 200);
        for (int z = 0; z < 200; z++)
            for (int x = 0; x < 200; x++)
                broad[x, z] = 5 * Math.Exp(-((x - 100) * (x - 100) + (z - 100) * (z - 100)) / (2.0 * 60 * 60));
        var zp = new ZoneParams("w", 0, 100 / Units.YardsPerMetre, 1);
        var hill = Earthwork.Compute(Raster(broad, zp), 0);
        var box = Earthwork.Compute(Raster(MoundField(), zp), 0);
        double hillVolume = broad.Values.Sum();
        Assert.True(hill.FillCuM < 0.05 * hillVolume, $"hill fill {hill.FillCuM:0} of {hillVolume:0} m³");
        Assert.True(box.FillCuM > 0.6 * 800, $"box fill {box.FillCuM:0} of 800 m³");
    }

    [Fact]
    public void RockFactor_WeightsTheCut()
    {
        var r = Earthwork.Compute(Raster(MoundField(), Plane with { RockFactor = 3 }), 0);
        Assert.Equal(3 * r.CutCuM, r.RockWeightedCutCuM, 6);
    }

    [Theory]
    [InlineData(100, 90, 0.1, 0.25, 100, 0, 0)]
    [InlineData(100, 45, 0.1, 0.25, 50, 0, 62.5)]
    [InlineData(50, 90, 0.1, 0.25, 50, 50, 0)]
    public void Balance_AppliesShrinkAndSwell(double cut, double fill, double shrink, double swell, double balanced, double import, double export)
    {
        var b = Earthwork.Balance(cut, fill, shrink, swell);
        Assert.Equal(balanced, b.BalancedCuM, 6);
        Assert.Equal(import, b.ImportCuM, 6);
        Assert.Equal(export, b.ExportCuM, 6);
    }

    [Fact]
    public void CellMask_FillsPolygonsAndDiscsByCellCentre()
    {
        var f = new Heightfield(new Vec2(-50, -50), 1, 100, 100);
        var m = new CellMask(f);
        m.Polygon([new(0, 0), new(10, 0), new(10, 10), new(0, 10)]);
        Assert.Equal(100, m.Count);
        var d = new CellMask(f);
        d.Disc(new Vec2(0, 0), 10);
        Assert.InRange(d.Count, Math.PI * 100 * 0.97, Math.PI * 100 * 1.03);
    }

    [Fact]
    public void Distance_IsZeroOnNaturalCells_AndGrowsInward()
    {
        var zones = new int[10 * 10];
        for (int k = 0; k < zones.Length; k++) zones[k] = (k % 10) < 5 ? -1 : 0;
        var d = Earthwork.DistanceInside(zones, 10, 10, 1);
        Assert.Equal(0, d[5 * 10 + 4]);
        Assert.Equal(1, d[5 * 10 + 5], 6);
        Assert.Equal(3, d[5 * 10 + 7], 6);
    }

    [Fact]
    public void ShippedData_LabelsEveryPrice()
    {
        var costs = EstimateData.LoadUnitCosts();
        var presets = EstimateData.LoadPresets();
        Assert.All(costs.Items.Values, c => Assert.False(string.IsNullOrWhiteSpace(c.Source)));
        Assert.All(costs.Items.Values.Where(c => !c.IsPlaceholder), c => Assert.NotNull(c.Year));
        Assert.Equal("sq ft", costs.Get("bunkers").Unit);
        Assert.Equal("head", costs.Get("irrigation").Unit);
        Assert.True(costs.Get("earthwork_onsite").IsPlaceholder);
        Assert.Contains(presets.Defaults.ManufacturedClass, presets.Presets.Keys);
        Assert.Equal(1, presets.Get(presets.Defaults.NaturalClass).NaturalFidelity);
        Assert.All(presets.Presets.Values, p => Assert.Equal("PLACEHOLDER", p.Source));
    }
}
