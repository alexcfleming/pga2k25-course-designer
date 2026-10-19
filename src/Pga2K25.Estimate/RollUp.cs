namespace Pga2K25.Estimate;

/// <summary>
/// The workbook's Summary sheet: hard cost → regional factor → escalation → contingency → soft costs → total,
/// plus per-hole and per-acre metrics and the sanity band. Excludes land, clubhouse, maintenance building,
/// equipment, financing and pre-opening costs.
/// </summary>
public sealed record CourseSummary
{
    public required double HoleDirect { get; init; }
    public required double CourseWide { get; init; }
    public double Hard => HoleDirect + CourseWide;
    public required double RegionalAdjustment { get; init; }
    public required double Escalation { get; init; }
    public double Escalated => Hard + RegionalAdjustment + Escalation;
    public required double Contingency { get; init; }
    public double Construction => Escalated + Contingency;
    public required double DesignFee { get; init; }
    public required double FieldRep { get; init; }
    public required double Permits { get; init; }
    public required double OwnerCosts { get; init; }
    public double Soft => DesignFee + FieldRep + Permits + OwnerCosts;
    public double Total => Construction + Soft;

    public required int Holes { get; init; }
    public required double Acres { get; init; }
    public required string AcresBasis { get; init; }
    public double TotalPerHole => Holes > 0 ? Total / Holes : 0;
    public double ConstructionPerHole => Holes > 0 ? Construction / Holes : 0;
    public double ConstructionPerAcre => Acres > 0 ? Construction / Acres : 0;
    public double SoftShareOfConstruction => Construction > 0 ? Soft / Construction : 0;

    public required double BenchmarkLow { get; init; }
    public required double BenchmarkHigh { get; init; }
    public string BenchmarkCheck => ConstructionPerHole < BenchmarkLow ? "Below band" : ConstructionPerHole > BenchmarkHigh ? "Above band" : "Within band";

    /// <summary>Hole-direct cost by line item, summed over holes, in the order the items first appear.</summary>
    public required IReadOnlyList<(string Item, double Cost)> ByCategory { get; init; }
    public required IReadOnlyList<LineItem> CourseWideLines { get; init; }
    /// <summary>Share of hard cost priced with placeholder unit prices.</summary>
    public required double PlaceholderShare { get; init; }
    /// <summary>Share of hard cost whose quantity is measured, computed or assumed.</summary>
    public required IReadOnlyDictionary<Provenance, double> ShareByProvenance { get; init; }
    /// <summary>The project settings used, with their status and source.</summary>
    public required IReadOnlyDictionary<string, Setting> Settings { get; init; }
}

public static class RollUp
{
    /// <summary>
    /// Course-wide items (workbook Course_Wide): clearing on the computed acreage, one each of the lump items unless
    /// Estimate.json says otherwise, and mobilization as a share of hole-direct cost.
    /// </summary>
    public static List<LineItem> CourseWideLines(double holeDirect, double clearingAcres, string clearingBasis,
        UnitCostFile costs, IReadOnlyDictionary<string, Setting> project, IReadOnlyDictionary<string, double>? overrides = null)
    {
        overrides ??= new Dictionary<string, double>();
        var lines = new List<LineItem>();
        var clearing = new QuantityRow(null, "Clearing and grubbing",
            overrides.TryGetValue("clearing", out var ca) ? ca : clearingAcres, "acre",
            overrides.ContainsKey("clearing") ? Provenance.Assumed : Provenance.Computed, Confidence.Low,
            overrides.ContainsKey("clearing") ? "set in Estimate.json" : clearingBasis);
        lines.Add(LineItem.Price(clearing, "clearing", costs.Get("clearing")));
        foreach (var key in new[] { "pump_station", "pond_liner", "central_control", "practice_area", "specialty", "grow_in" })
        {
            var c = costs.Get(key);
            bool set = overrides.TryGetValue(key, out var q);
            var row = new QuantityRow(null, c.Label, set ? q : 1, c.Unit, Provenance.Assumed, Confidence.Low,
                set ? "set in Estimate.json" : "one per course (default)");
            lines.Add(LineItem.Price(row, key, c));
        }
        var mob = project["mobilization"];
        var mobRow = new QuantityRow(null, "Mobilization and general conditions", holeDirect, "$ hole-direct", Provenance.Computed, null,
            FormattableString.Invariant($"{mob.Value:P0} of hole-direct cost"));
        lines.Add(new LineItem(mobRow, "mobilization", mob.Value, mob.Value * holeDirect, mob.Status, mob.Source));
        return lines;
    }

    public static CourseSummary Summarize(IReadOnlyList<LineItem> holeLines, IReadOnlyList<LineItem> courseWideLines,
        IReadOnlyDictionary<string, Setting> project, int holes, double acres, string acresBasis)
    {
        double V(string key) => project[key].Value;
        double holeDirect = holeLines.Sum(l => l.Cost ?? 0);
        double courseWide = courseWideLines.Sum(l => l.Cost ?? 0);
        double hard = holeDirect + courseWide;
        double regional = hard * (V("regional_factor") - 1);
        double escalation = (hard + regional) * (Math.Pow(1 + V("escalation_per_yr"), V("years_to_midpoint")) - 1);
        double escalated = hard + regional + escalation;

        var all = holeLines.Concat(courseWideLines).ToList();
        double Share(Func<LineItem, bool> pick) => hard > 0 ? all.Where(pick).Sum(l => l.Cost ?? 0) / hard : 0;

        return new CourseSummary
        {
            HoleDirect = holeDirect,
            CourseWide = courseWide,
            RegionalAdjustment = regional,
            Escalation = escalation,
            Contingency = escalated * V("contingency"),
            DesignFee = escalated * V("design_fee"),
            FieldRep = escalated * V("field_rep"),
            Permits = escalated * V("permits"),
            OwnerCosts = escalated * V("owner_costs"),
            Holes = holes,
            Acres = acres,
            AcresBasis = acresBasis,
            BenchmarkLow = V("benchmark_low_per_hole"),
            BenchmarkHigh = V("benchmark_high_per_hole"),
            ByCategory = holeLines.GroupBy(l => l.Quantity.Item).Select(g => (g.Key, g.Sum(l => l.Cost ?? 0))).ToList(),
            CourseWideLines = courseWideLines,
            PlaceholderShare = Share(l => l.IsPlaceholder),
            ShareByProvenance = Enum.GetValues<Provenance>().ToDictionary(p => p, p => Share(l => l.Quantity.Provenance == p)),
            Settings = new Dictionary<string, Setting>(project),
        };
    }
}
