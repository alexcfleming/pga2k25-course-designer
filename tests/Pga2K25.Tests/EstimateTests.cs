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

    // A flat synthetic par 4: one tee at the origin, green centred 300 m north (+Z), fairway 40 m wide over both,
    // a 10 m square bunker and (optionally) a 300 m cart path beside the hole.
    static SurfaceSpline Square(int index, int surface, double x0, double z0, double x1, double z1) =>
        new(index, surface, true, 0, new[] { new Vec2(x0, z0), new Vec2(x1, z0), new Vec2(x1, z1), new Vec2(x0, z1) }
            .Select(p => new SplinePoint(p, p, p, 0)).ToList());

    static CourseModel FlatHole(bool path)
    {
        var splines = new List<SurfaceSpline>
        {
            Square(0, Surfaces.Fairway, -20, -10, 20, 320),
            Square(1, Surfaces.Green, -10, 290, 10, 310),
            Square(2, Surfaces.Bunker, 12, 150, 22, 160),
        };
        if (path)
            splines.Add(new SurfaceSpline(3, Surfaces.CartPath, false, 2,
                [new(new(30, 0), new(30, 0), new(30, 0), 2), new(new(30, 300), new(30, 300), new(30, 300), 2)]));
        return new CourseModel
        {
            Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49,
            Holes = [new Hole(1, 4, [new(0, 0), new(0, 300)], [new(0, 0)], [new(0, 300)])],
            SurfaceSplines = splines, HeightStamps = [], SurfaceBrushes = [], PlacedObjects = [],
        };
    }

    static HoleEstimate Estimate(bool path) => HoleEstimator.Build(FlatHole(path), 1, EstimateData.LoadPresets(),
        EstimateData.LoadUnitCosts(), EstimateData.LoadRules(), new EstimateInput());

    static QuantityRow Row(HoleEstimate e, string item) => e.Quantities.Single(q => q.Item == item);

    const double SqFtPerSqM = Units.SquareFeetPerSquareMetre;

    [Fact]
    public void HoleQuantities_MeasureAreasFromTheSplines()
    {
        var e = Estimate(path: true);
        Assert.InRange(Row(e, "Greens").Qty, 400 * SqFtPerSqM * 0.95, 400 * SqFtPerSqM * 1.05);
        Assert.InRange(Row(e, "Bunkers").Qty, 100 * SqFtPerSqM * 0.9, 100 * SqFtPerSqM * 1.1);
        Assert.Equal(Provenance.Measured, Row(e, "Greens").Provenance);
        // Flat ground: nothing to cut or fill.
        Assert.Equal(0, Row(e, "Cut").Qty + Row(e, "Fill").Qty, 6);
        // Drawn path: 300 m = 984.25 ft, measured.
        Assert.Equal(300 * Units.FeetPerMetre, Row(e, "Cart path").Qty, 3);
        Assert.Equal(Provenance.Measured, Row(e, "Cart path").Provenance);
    }

    [Fact]
    public void HoleQuantities_RuleBasedItemsFollowTheRules()
    {
        var e = Estimate(path: true);
        var rules = EstimateData.LoadRules();
        double sod = Row(e, "Grassing, sod").Qty, seed = Row(e, "Grassing, seed or sprigs").Qty;
        double grassed = sod + seed;
        // Fairway 40 × 330 m less the bunker; the tee pad sits inside the fairway.
        Assert.InRange(grassed * Units.SquareMetresPerAcre, (40 * 330 - 100) * 0.97, (40 * 330 - 100) * 1.03);
        // Sod: a band about 4 yd (3.66 m) round a 20 m square green ≈ 80 × 3.66 + π × 3.66² ≈ 335 m².
        Assert.InRange(sod * Units.SquareMetresPerAcre, 335 * 0.85, 335 * 1.15);
        Assert.Equal(Math.Ceiling(grassed * rules.HeadsPerAcre.Value), Row(e, "Irrigation").Qty);
        double turf = grassed - Row(e, "Greens").Qty / 43560;
        Assert.Equal(turf * rules.DrainageLfPerAcre.Value, Row(e, "Drainage").Qty, 3);
        Assert.Equal(Provenance.Assumed, Row(e, "Irrigation").Provenance);
        Assert.Equal(Provenance.Assumed, Row(e, "Tees").Provenance);
    }

    [Fact]
    public void HoleWithoutPath_AssumesRouteLength()
    {
        var e = Estimate(path: false);
        var path = Row(e, "Cart path");
        Assert.Equal(Provenance.Assumed, path.Provenance);
        Assert.Equal(300 * Units.FeetPerMetre * EstimateData.LoadRules().PathFtPerRouteFt.Value, path.Qty, 3);
        Assert.Contains(e.Assumptions, a => a.Scope == "Cart path");
    }

    [Fact]
    public void HoleLines_PriceQuantitiesAndReportShares()
    {
        var e = Estimate(path: true);
        var costs = EstimateData.LoadUnitCosts();
        var greens = e.Lines.Single(l => l.CostKey == "greens");
        Assert.Equal(greens.Quantity.Qty * costs.Get("greens").Price!.Value, greens.Cost!.Value, 6);
        Assert.Equal(e.Lines.Sum(l => l.Cost ?? 0), e.Total, 6);
        Assert.InRange(e.PlaceholderShare, 0.01, 0.99);
        Assert.Equal(1, e.ShareByProvenance.Values.Sum(), 6);
        Assert.All(e.Lines, l => Assert.Equal(costs.Get(l.CostKey).Unit, l.Quantity.Unit));
    }

    // golf_course_estimating_model_template.xlsx (2026-10-18): typical quantity per hole by par (3, 4, 5) and the
    // template's unit price, then each hole's par and terrain multiplier from Hole_Qty.
    static readonly (string Item, string Unit, double P3, double P4, double P5, double Price)[] WorkbookTypicals =
    [
        ("Earthwork", "cu yd", 4000, 9000, 14000, 6),
        ("Shaping", "acre", 3, 7, 10, 9000),
        ("Finish grading", "acre", 3, 7, 10, 3500),
        ("Greens", "sq ft", 6000, 6000, 6500, 9),
        ("Tees", "sq ft", 4500, 6000, 7000, 4.5),
        ("Bunkers", "each", 4, 6, 8, 4500),
        ("Grassing", "acre", 4, 9, 13, 6000),
        ("Drainage", "lin ft", 600, 1200, 1800, 18),
        ("Cart path", "lin ft", 500, 900, 1300, 35),
        ("Irrigation", "lin ft", 1800, 3200, 4500, 22),
    ];

    static readonly (int Par, double Multiplier)[] WorkbookHoles =
    [
        (4, 1), (4, 1), (5, 1.1), (3, 1), (4, 1), (4, 1.05), (3, 1), (4, 1), (5, 1.1),
        (4, 1), (3, 1), (4, 1), (5, 1.15), (4, 1), (4, 1), (3, 1.05), (4, 1), (5, 1),
    ];

    [Fact]
    public void RollUp_ReproducesTheWorkbookExample()
    {
        // Hole-direct lines priced exactly as Hole_Cost does: multiplier × typical quantity for the par × unit price.
        var holeLines = new List<LineItem>();
        for (int i = 0; i < WorkbookHoles.Length; i++)
        {
            var (par, mult) = WorkbookHoles[i];
            foreach (var t in WorkbookTypicals)
            {
                double typical = par == 3 ? t.P3 : par == 4 ? t.P4 : t.P5;
                var row = new QuantityRow(i + 1, t.Item, mult * typical, t.Unit, Provenance.Assumed, Confidence.Low, "workbook typical");
                holeLines.Add(LineItem.Price(row, t.Item, new UnitCost { Price = t.Price, Unit = t.Unit }));
            }
        }
        // Course-wide items and project settings come from the shipped data, which keep the workbook's placeholders.
        var project = EstimateData.LoadProject();
        double holeDirect = holeLines.Sum(l => l.Cost!.Value);
        var courseWide = RollUp.CourseWideLines(holeDirect, 40, "workbook", EstimateData.LoadUnitCosts(), project);
        var s = RollUp.Summarize(holeLines, courseWide, project, 18, 150, "workbook");

        // Values cached in the workbook's Summary sheet.
        Assert.Equal(7852297.5, s.HoleDirect, 4);
        Assert.Equal(2484091.9, s.CourseWide, 4);
        Assert.Equal(10336389.4, s.Hard, 4);
        Assert.Equal(11824829.4736, s.Construction, 3);
        Assert.Equal(1934972.09568, s.Soft, 3);
        Assert.Equal(13759801.56928, s.Total, 3);
        Assert.Equal(656934.970755556, s.ConstructionPerHole, 3);
        Assert.Equal(11824829.4736 / 150, s.ConstructionPerAcre, 3);
        Assert.Equal("Within band", s.BenchmarkCheck);
    }

    [Fact]
    public void RollUp_AppliesRegionalFactorBeforeEscalation()
    {
        var project = EstimateData.LoadProject(new EstimateInput
        {
            Project = new() { ["regional_factor"] = 1.2, ["escalation_per_yr"] = 0.05, ["years_to_midpoint"] = 2, ["contingency"] = 0 },
        });
        var line = LineItem.Price(new QuantityRow(1, "x", 1000, "lump", Provenance.Measured, null, ""), "x", new UnitCost { Price = 1000 });
        var s = RollUp.Summarize([line], [], project, 1, 1, "test");
        Assert.Equal(1_000_000 * 1.2 * 1.05 * 1.05, s.Escalated, 6);
        Assert.Equal(0, s.Contingency);
        Assert.Equal("Estimate.json", s.Settings["regional_factor"].Status);
        Assert.Throws<KeyNotFoundException>(() => EstimateData.LoadProject(new EstimateInput { Project = new() { ["nope"] = 1 } }));
    }

    [Fact]
    public void CourseWide_OverridesQuantities()
    {
        var project = EstimateData.LoadProject();
        var lines = RollUp.CourseWideLines(100_000, 10, "test", EstimateData.LoadUnitCosts(), project,
            new Dictionary<string, double> { ["practice_area"] = 0 });
        Assert.Equal(0, lines.Single(l => l.CostKey == "practice_area").Cost);
        Assert.Equal(10 * 5000, lines.Single(l => l.CostKey == "clearing").Cost);
        Assert.Equal(4000, lines.Single(l => l.CostKey == "mobilization").Cost!.Value, 6);
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
