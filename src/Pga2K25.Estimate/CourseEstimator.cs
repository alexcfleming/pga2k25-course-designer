namespace Pga2K25.Estimate;

/// <summary>One scenario over the chosen holes: hole estimates, course-wide lines and the roll-up.</summary>
public sealed record CourseRun(string Scenario, IReadOnlyList<HoleEstimate> Holes, IReadOnlyList<LineItem> CourseWide, CourseSummary Summary)
{
    public double CutCuYd => Holes.Sum(h => h.Earthwork.CutCuYd);
    public double FillCuYd => Holes.Sum(h => h.Earthwork.FillCuYd);
}

/// <summary>Total budget at one fidelity offset (likely prices and quantities otherwise).</summary>
public sealed record SweepPoint(double FidelityOffset, double CutCuYd, double FillCuYd, double Earthwork, double Total);

/// <summary>
/// Runs scenarios over prepared holes. Terrain and masks are built once per hole (<see cref="HoleSite"/>), so the
/// low / likely / high cases and the fidelity sweep only redo the earthwork maths and the pricing.
/// </summary>
public sealed class CourseEstimator(IReadOnlyList<HoleSite> sites, ZonePresetFile presets, UnitCostFile costs,
    QuantityRules rules, EstimateInput input, IReadOnlyDictionary<string, Setting> project)
{
    public IReadOnlyList<HoleSite> Sites => sites;

    public CourseRun Run(Scenario scenario)
    {
        var holes = sites.Select(s => HoleEstimator.Estimate(s, presets, costs, rules, input, scenario)).ToList();
        var settings = scenario.Apply(project);
        var holeLines = holes.SelectMany(e => e.Lines).ToList();
        double holeDirect = holeLines.Sum(l => l.Cost ?? 0);
        var courseWide = RollUp.CourseWideLines(holeDirect, holes.Sum(e => e.ClearingAcres),
            "manufactured acres × each hole class's clearing share", costs, settings, input.CourseWide, scenario);
        var summary = RollUp.Summarize(holeLines, courseWide, settings, holes.Count, holes.Sum(e => e.ManufacturedAcres),
            "manufactured ground (play splines, paths, tee pads)");
        return new CourseRun(scenario.Name, holes, courseWide, summary);
    }

    /// <summary>The fidelity sweep: likely case with the given offsets added to every zone's natural fidelity.</summary>
    public IReadOnlyList<SweepPoint> Sweep(IEnumerable<double> offsets) => offsets.Select(o =>
    {
        var run = Run(Scenario.Likely with { Name = FormattableString.Invariant($"fidelity {o:+0.##;-0.##;0}"), FidelityOffset = o });
        double earthwork = run.Holes.SelectMany(h => h.Lines).Where(l => l.CostKey.StartsWith("earthwork")).Sum(l => l.Cost ?? 0);
        return new SweepPoint(o, run.CutCuYd, run.FillCuYd, earthwork, run.Summary.Total);
    }).ToList();
}
