using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.Estimate;

public sealed record EstimateOptions
{
    /// <summary>Terrain replay for estimates: plot-scale settings as the terrain map uses, on a 1 m raster.</summary>
    public TerrainModelOptions Terrain { get; init; } = new() { CellMetres = 1.0, LargeBrushHardness = 0.3 };
    /// <summary>Ground kept around a hole's route and tees when cutting its window.</summary>
    public double ReachMetres { get; init; } = 120;
}

/// <summary>Everything worked out for one hole: the raster, the earthwork, quantities, priced lines and assumptions.</summary>
public sealed class HoleEstimate
{
    public required Hole Hole { get; init; }
    public required ZoneRaster Raster { get; init; }
    public required EarthworkResult Earthwork { get; init; }
    public required EarthBalance Balance { get; init; }
    public required IReadOnlyList<QuantityRow> Quantities { get; init; }
    public required IReadOnlyList<LineItem> Lines { get; init; }
    public required IReadOnlyList<Assumption> Assumptions { get; init; }
    /// <summary>Per cell: true where the ground is inside a play spline, tee pad or path (manufactured).</summary>
    public required bool[] Manufactured { get; init; }
    public required string Scenario { get; init; }
    public required double ManufacturedAcres { get; init; }
    /// <summary>Manufactured acres × the hole class's clearing share (drawn zones use the hole class too, for now).</summary>
    public required double ClearingAcres { get; init; }
    public required string ClearingBasis { get; init; }

    /// <summary>Sum of the priced lines (unpriced lines count as nothing; see <see cref="UnpricedLines"/>).</summary>
    public double Total => Lines.Sum(l => l.Cost ?? 0);
    public IEnumerable<LineItem> UnpricedLines => Lines.Where(l => l.Cost is null && l.Quantity.Qty > 0);
    /// <summary>Share of <see cref="Total"/> priced with a placeholder unit price.</summary>
    public double PlaceholderShare => Total > 0 ? Lines.Where(l => l.IsPlaceholder).Sum(l => l.Cost ?? 0) / Total : 0;
    /// <summary>Share of <see cref="Total"/> whose quantity has each provenance.</summary>
    public IReadOnlyDictionary<Provenance, double> ShareByProvenance =>
        Enum.GetValues<Provenance>().ToDictionary(p => p,
            p => Total > 0 ? Lines.Where(l => l.Quantity.Provenance == p).Sum(l => l.Cost ?? 0) / Total : 0);
}

/// <summary>
/// Hole-direct quantities and costs. Ground inside play splines (fairway, rough, heavy rough, greens, bunkers, waste,
/// cart paths) and the assumed tee pads is manufactured and gets the hole's zone class; everything else is natural
/// ground with no earthwork, unless a drawn zone in Estimate.json says otherwise. Areas and path lengths are read off
/// the course file; drainage, irrigation heads, sod and tee pads come from the rules in quantity-rules.json.
/// </summary>
/// <summary>
/// The scenario-independent part of a hole: terrain, zones and measured areas. Built once, then estimated for each
/// scenario and fidelity offset (only the earthwork and the prices change).
/// </summary>
public sealed class HoleSite
{
    public required Hole Hole { get; init; }
    public required ZoneRaster Raster { get; init; }
    public required bool[] Manufactured { get; init; }
    public required string HoleClassName { get; init; }
    public required double FeatherMetres { get; init; }
    public required double MadeSqFt { get; init; }
    public required double GreenSqFt { get; init; }
    public required double BunkerSqFt { get; init; }
    /// <summary>Painted bunker sand (outside drawn bunkers) and waste surfaces: priced as sand / waste area.</summary>
    public required double SandWasteSqFt { get; init; }
    public required double TeeSqFt { get; init; }
    public required double GrassedSqFt { get; init; }
    public required double SodSqFt { get; init; }
    public required QuantityRow CartPath { get; init; }
}

public static class HoleEstimator
{
    public static HoleEstimate Build(CourseModel course, int holeNumber, ZonePresetFile presets, UnitCostFile costs,
        QuantityRules rules, EstimateInput input, EstimateOptions? options = null) =>
        Estimate(Prepare(course, holeNumber, presets, rules, input, options), presets, costs, rules, input, Scenario.Likely);

    public static HoleSite Prepare(CourseModel course, int holeNumber, ZonePresetFile presets, QuantityRules rules,
        EstimateInput input, EstimateOptions? options = null)
    {
        options ??= new EstimateOptions();
        var hole = course.Holes.FirstOrDefault(h => h.Number == holeNumber)
            ?? throw new ArgumentException($"Course has no hole {holeNumber}.");
        var layers = CourseLayers.From(course);
        var defaults = presets.Defaults;
        double teeRadius = defaults.TeePadRadiusYd / Units.YardsPerMetre;
        double feather = defaults.FeatherYd / Units.YardsPerMetre;

        // Window: the hole's route and tees plus reach, plus room for the widest smoothing kernel.
        var holeClassName = input.ForHole(holeNumber).ZoneClass ?? defaults.ManufacturedClass;
        var classNames = new List<string> { holeClassName };
        classNames.AddRange(input.Zones.Select(z => z.Class).Where(c => !classNames.Contains(c)));
        double maxSigma = classNames.Select(c => presets.Get(c).BaselineWavelengthYd / Units.YardsPerMetre / Earthwork.WavelengthPerSigma).Max();
        var pts = hole.Route.Concat(hole.Tees).ToList();
        double pad = options.ReachMetres + 3 * maxSigma;
        var min = new Vec2(pts.Min(p => p.X) - pad, pts.Min(p => p.Z) - pad);
        var max = new Vec2(pts.Max(p => p.X) + pad, pts.Max(p => p.Z) + pad);
        var field = TerrainReplay.Build(course, min, max, options.Terrain);

        // Feature masks. Manufactured ground is all of them plus a round pad at each tee.
        var grass = new CellMask(field); foreach (var g in layers.Grass) grass.Polygon(g);
        // Greens far from every hole's green are practice greens: left out here, covered by the practice-area lump.
        var greenAnchors = course.Holes.Select(h => h.Route[^1]).ToList();
        double practiceReach = rules.PracticeGreenYd.Value / Units.YardsPerMetre;
        var greens = new CellMask(field);
        foreach (var g in layers.Greens)
        {
            var c = new Vec2(g.Average(p => p.X), g.Average(p => p.Z));
            if (greenAnchors.Count == 0 || greenAnchors.Min(a => Vec2.Distance(a, c)) <= practiceReach) greens.Polygon(g);
        }
        // Drawn bunkers (splines and ribbons) are built bunkers; painted bunker sand outside them counts with waste
        // surfaces as sand / waste area at its own, lower rate.
        var bunkers = new CellMask(field);
        foreach (var b in layers.BunkerAreas) bunkers.Polygon(b);
        foreach (var r in layers.BunkerRibbons) bunkers.Ribbon(r);
        var waste = new CellMask(field);
        foreach (var d in layers.BunkerBrushes) waste.Disc(d.Centre, d.Radius);
        foreach (var w in layers.WasteAreas) waste.Polygon(w);
        foreach (var r in layers.WasteRibbons) waste.Ribbon(r);
        foreach (var d in layers.WasteBrushes) waste.Disc(d.Centre, d.Radius);
        for (int k = 0; k < waste.Cells.Length; k++) waste.Cells[k] &= !bunkers.Cells[k];
        var paths = new CellMask(field); foreach (var r in layers.Paths) paths.Ribbon(r);
        var tees = new CellMask(field); foreach (var h in course.Holes) foreach (var t in h.Tees) tees.Disc(t, teeRadius);
        var made = CellMask.Union(field, grass, greens, bunkers, waste, paths, tees);

        // Each cell belongs to the hole whose route or tees are nearest.
        var counted = NearestHole(field, layers, holeNumber, options.ReachMetres);

        // Zones: −1 natural; index into classes otherwise. Drawn zones win over the spline default, later over earlier.
        var classes = new List<ZoneParams>();
        int ClassIndex(string name)
        {
            var p = presets.Get(name);
            if (p.NaturalFidelity >= 1) return -1;   // natural class: never any earthwork, whatever the offset
            int i = classes.FindIndex(c => c.Name == name);
            if (i >= 0) return i;
            classes.Add(new ZoneParams(name, p.NaturalFidelity, p.BaselineWavelengthYd / Units.YardsPerMetre, p.RockFactor));
            return classes.Count - 1;
        }
        var zone = new int[field.Values.Length];
        int holeClass = ClassIndex(holeClassName);
        for (int k = 0; k < zone.Length; k++) zone[k] = made.Cells[k] ? holeClass : -1;
        foreach (var z in input.Zones)
        {
            var m = new CellMask(field);
            m.Polygon(z.Points());
            int c = ClassIndex(z.Class);
            for (int k = 0; k < zone.Length; k++) if (m.Cells[k]) zone[k] = c;
        }

        var raster = new ZoneRaster { Finished = field, Zone = zone, Counted = counted, Classes = classes };

        // Areas inside this hole's cells. Grassed = manufactured ground that isn't sand, waste or path; sod goes on a
        // band round each green, the rest is seeded. Greens' grassing is in the seeded area.
        double cellSqFt = field.Cell * field.Cell * Units.SquareFeetPerSquareMetre;
        double SqFt(Func<int, bool> pick) => Enumerable.Range(0, made.Cells.Length).Count(k => counted[k] && pick(k)) * cellSqFt;
        bool Grassed(int k) => made.Cells[k] && !bunkers.Cells[k] && !waste.Cells[k] && !paths.Cells[k];
        double sodBand = rules.GreenSurroundSodYd.Value / Units.YardsPerMetre;
        var toGreen = Earthwork.DistanceInside(greens.Cells.Select(g => g ? -1 : 0).ToArray(), field.Width, field.Height, field.Cell);
        // Cell-centre distance less half a cell ≈ distance from the cell's centre to the green's edge.
        bool Sod(int k) => Grassed(k) && !greens.Cells[k] && toGreen[k] - field.Cell / 2 <= sodBand;

        double madeSqFt = SqFt(k => made.Cells[k]);
        double greenSqFt = SqFt(k => greens.Cells[k]);
        double bunkerSqFt = SqFt(k => bunkers.Cells[k]);
        double sandWasteSqFt = SqFt(k => waste.Cells[k]);
        double teeSqFt = SqFt(k => tees.Cells[k] && !greens.Cells[k]);
        double grassedSqFt = SqFt(Grassed);
        double sodSqFt = SqFt(Sod);

        return new HoleSite
        {
            Hole = hole, Raster = raster, Manufactured = made.Cells, HoleClassName = holeClassName, FeatherMetres = feather,
            MadeSqFt = madeSqFt, GreenSqFt = greenSqFt, BunkerSqFt = bunkerSqFt, SandWasteSqFt = sandWasteSqFt, TeeSqFt = teeSqFt,
            GrassedSqFt = grassedSqFt, SodSqFt = sodSqFt, CartPath = CartPath(holeNumber, layers, field, counted, hole, rules),
        };
    }

    /// <summary>Earthwork, quantities and priced lines for one scenario on a prepared hole.</summary>
    public static HoleEstimate Estimate(HoleSite site, ZonePresetFile presets, UnitCostFile costs, QuantityRules rules,
        EstimateInput input, Scenario scenario)
    {
        var defaults = presets.Defaults;
        var hole = site.Hole;
        string holeClassName = site.HoleClassName;
        double offset = input.FidelityOffset + scenario.FidelityOffset;
        var ew = Earthwork.Compute(site.Raster, site.FeatherMetres, offset);
        var bal = Earthwork.Balance(ew.CutCuM, ew.FillCuM, defaults.Shrink, defaults.Swell);
        double madeSqFt = site.MadeSqFt, greenSqFt = site.GreenSqFt, bunkerSqFt = site.BunkerSqFt, teeSqFt = site.TeeSqFt,
            grassedSqFt = site.GrassedSqFt, sodSqFt = site.SodSqFt;
        double turfAcres = (grassedSqFt - greenSqFt) / 43560;   // greens drain through their own profile
        double irrigatedAcres = grassedSqFt / 43560;

        var cls = presets.Get(holeClassName);
        string shape = cls.BaselineWavelengthYd > 0 ? FormattableString.Invariant($"wavelength {cls.BaselineWavelengthYd:0} yd") : "best-fit plane";
        string basis = FormattableString.Invariant(
            $"finished surface vs {holeClassName} baseline, g={Math.Clamp(cls.NaturalFidelity + offset, 0, 1):0.##}, {shape}, {defaults.FeatherYd:0} yd feather");
        var cy = Units.CubicYardsPerCubicMetre;
        int n = hole.Number;
        string I(FormattableString s) => FormattableString.Invariant(s);

        var cut = new QuantityRow(n, "Cut", ew.CutCuM * cy, "cu yd", Provenance.Computed, Confidence.Low, basis);
        var fill = new QuantityRow(n, "Fill", ew.FillCuM * cy, "cu yd", Provenance.Computed, Confidence.Low, basis);
        var balanced = new QuantityRow(n, "Earthwork balanced on site", bal.BalancedCuM * cy, "cu yd", Provenance.Computed, Confidence.Low,
            I($"cut placed as fill, shrink {defaults.Shrink:P0}"));
        var import = new QuantityRow(n, "Imported fill", bal.ImportCuM * cy, "cu yd", Provenance.Computed, Confidence.Low, "fill not met by cut");
        var export = new QuantityRow(n, "Surplus cut hauled off", bal.ExportCuM * cy, "cu yd", Provenance.Computed, Confidence.Low,
            I($"loose volume, swell {defaults.Swell:P0}"));
        var shaping = new QuantityRow(n, "Shaping", madeSqFt / 43560, "acre", Provenance.Measured, null, "manufactured ground: play splines, paths, tee pads");
        var grading = shaping with { Item = "Finish grading" };
        var greenRow = new QuantityRow(n, "Greens", greenSqFt, "sq ft", Provenance.Measured, null, "green splines");
        var teeRow = new QuantityRow(n, "Tees", teeSqFt, "sq ft", Provenance.Assumed, Confidence.Low,
            I($"{hole.Tees.Count} tees as {defaults.TeePadRadiusYd:0} yd radius pads, overlaps counted once (tees are points in the file)"));
        var bunkerRow = new QuantityRow(n, "Bunkers", bunkerSqFt, "sq ft", Provenance.Measured, null, "drawn bunker splines and ribbons");
        var sandRow = new QuantityRow(n, "Sand and waste areas", site.SandWasteSqFt, "sq ft", Provenance.Measured, null,
            "painted bunker sand outside drawn bunkers, and waste surfaces");
        var sodRow = new QuantityRow(n, "Grassing, sod", sodSqFt / 43560, "acre", Provenance.Computed, Confidence.Low,
            I($"{rules.GreenSurroundSodYd.Value:0} yd band round greens"));
        var seedRow = new QuantityRow(n, "Grassing, seed or sprigs", (grassedSqFt - sodSqFt) / 43560, "acre", Provenance.Measured, null,
            "fairway, rough, greens and tee pads, less sod");
        var drainRow = new QuantityRow(n, "Drainage", turfAcres * rules.DrainageLfPerAcre.Value * cls.DrainageNeed, "lin ft", Provenance.Assumed, Confidence.Low,
            I($"{turfAcres:0.0} turf acres × {rules.DrainageLfPerAcre.Value:0} LF/acre × drainage need {cls.DrainageNeed:0.##}"));
        var pathRow = site.CartPath;
        var headsRow = new QuantityRow(n, "Irrigation", Math.Ceiling(irrigatedAcres * rules.HeadsPerAcre.Value), "head", Provenance.Assumed, Confidence.Low,
            I($"{irrigatedAcres:0.0} grassed acres × {rules.HeadsPerAcre.Value:0} heads/acre"));

        (teeRow, drainRow, pathRow, headsRow) = (scenario.Scale(teeRow), scenario.Scale(drainRow), scenario.Scale(pathRow), scenario.Scale(headsRow));
        var rows = new List<QuantityRow> { cut, fill, balanced, import, export, shaping, grading, greenRow, teeRow, bunkerRow, sandRow, sodRow, seedRow, drainRow, pathRow, headsRow };

        // The cut's rock factor scales the earthwork price (1 unless a rock or cliff class is used).
        double rock = ew.CutCuM > 0 ? ew.RockWeightedCutCuM / ew.CutCuM : 1;
        LineItem P(QuantityRow q, string key, double factor = 1) => LineItem.Price(q, key, costs.Get(key), factor, scenario);
        var lines = new List<LineItem>
        {
            P(balanced, "earthwork_onsite", rock), P(import, "earthwork_import"), P(export, "earthwork_export"),
            P(shaping, "shaping"), P(grading, "finish_grading"),
            P(greenRow, "greens"), P(teeRow, "tees"), P(bunkerRow, "bunkers"), P(sandRow, "sand_waste"),
            P(sodRow, "grassing_sod"), P(seedRow, "grassing_seed"),
            P(drainRow, "drainage"), P(pathRow, "cart_path"), P(headsRow, "irrigation"),
        };

        var assumptions = new List<Assumption>
        {
            new($"Hole {n} play ground", I($"{holeClassName}: natural fidelity {cls.NaturalFidelity:0.##}, {shape}, rock ×{cls.RockFactor:0.#}, drainage need {cls.DrainageNeed:0.##}"),
                input.ForHole(n).ZoneClass is null ? $"preset default ({cls.Source})" : "Estimate.json", Confidence.Low),
            new("Outside play splines", "natural ground, no earthwork", "Alex, 2026-10-19", Confidence.Medium),
            new("Tees", I($"round pads of {defaults.TeePadRadiusYd:0} yd radius"), defaults.Source, Confidence.Low),
            new("Shrink / swell", I($"{defaults.Shrink:P0} / {defaults.Swell:P0}"), defaults.Source, Confidence.Low),
            new("Sand", "drawn bunkers are built bunkers; painted bunker sand and waste surfaces are sand / waste area at a lower rate",
                "Alex, 2026-10-19", Confidence.Medium),
            new("Practice greens", I($"greens more than {rules.PracticeGreenYd.Value:0} yd from every hole's green are left to the practice-area item"),
                rules.PracticeGreenYd.Source, Confidence.Medium),
            new("Feature footprints", "greens, tees and bunkers are included in bulk cut/fill; their unit prices cover the construction profile only",
                "estimate module design", Confidence.Medium),
            new("Sod", I($"{rules.GreenSurroundSodYd.Value:0} yd band round each green; all other turf seeded or sprigged"), rules.GreenSurroundSodYd.Source, Confidence.Low),
            new("Drainage", I($"{rules.DrainageLfPerAcre.Value:0} LF per turf acre"), rules.DrainageLfPerAcre.Source, Confidence.Low),
            new("Irrigation", I($"{rules.HeadsPerAcre.Value:0} heads per grassed acre"), rules.HeadsPerAcre.Source, Confidence.Low),
        };
        if (pathRow.Provenance == Provenance.Assumed)
            assumptions.Add(new("Cart path", I($"no path drawn on this hole: route length × {rules.PathFtPerRouteFt.Value:0.##}"), rules.PathFtPerRouteFt.Source, Confidence.Low));
        if (input.FidelityOffset != 0)
            assumptions.Add(new("All zones", I($"fidelity offset {input.FidelityOffset:+0.##;-0.##}"), "Estimate.json", Confidence.Low));
        if (scenario.FidelityOffset != 0)
            assumptions.Add(new("All zones", I($"fidelity offset {scenario.FidelityOffset:+0.##;-0.##} ({scenario.Name} case)"), "scenarios.json", Confidence.Low));
        foreach (var z in input.Zones)
            assumptions.Add(new($"Drawn zone{(z.Note.Length > 0 ? $" ({z.Note})" : "")}", z.Class, "Estimate.json", Confidence.Low));

        return new HoleEstimate
        {
            Hole = hole, Raster = site.Raster, Earthwork = ew, Balance = bal, Quantities = rows, Lines = lines,
            Assumptions = assumptions, Manufactured = site.Manufactured, Scenario = scenario.Name,
            ManufacturedAcres = madeSqFt / 43560, ClearingAcres = madeSqFt / 43560 * cls.ClearingPct,
            ClearingBasis = I($"{holeClassName} clearing {cls.ClearingPct:P0} of manufactured ground"),
        };
    }

    /// <summary>
    /// Drawn cart path whose segment midpoints fall in the hole's cells; where none is drawn, the tee-to-green route
    /// length × the path rule, tagged as assumed.
    /// </summary>
    static QuantityRow CartPath(int n, CourseLayers layers, Heightfield f, bool[] counted, Hole hole, QuantityRules rules)
    {
        double metres = 0;
        foreach (var r in layers.Paths)
            for (int i = 1; i < r.Points.Count; i++)
            {
                var mid = (r.Points[i - 1] + r.Points[i]) * 0.5;
                int ix = (int)Math.Floor((mid.X - f.Origin.X) / f.Cell), iz = (int)Math.Floor((mid.Z - f.Origin.Z) / f.Cell);
                if (ix >= 0 && iz >= 0 && ix < f.Width && iz < f.Height && counted[iz * f.Width + ix])
                    metres += Vec2.Distance(r.Points[i - 1], r.Points[i]);
            }
        if (metres > 0)
            return new QuantityRow(n, "Cart path", metres * Units.FeetPerMetre, "lin ft", Provenance.Measured, null, "cart path splines");
        var layout = layers.Holes.First(h => h.Hole.Number == hole.Number);
        double route = layout.Route.Zip(layout.Route.Skip(1), Vec2.Distance).Sum() * Units.FeetPerMetre;
        return new QuantityRow(n, "Cart path", route * rules.PathFtPerRouteFt.Value, "lin ft", Provenance.Assumed, Confidence.Low,
            FormattableString.Invariant($"none drawn: {route / 3:0} yd tee-to-green route × {rules.PathFtPerRouteFt.Value:0.##}"));
    }

    /// <summary>Cells whose nearest hole (by distance to route and tees) is <paramref name="holeNumber"/>, within reach.</summary>
    static bool[] NearestHole(Heightfield f, CourseLayers layers, int holeNumber, double reach)
    {
        var holes = layers.Holes;
        var result = new bool[f.Values.Length];
        Parallel.For(0, f.Height, iz =>
        {
            for (int ix = 0; ix < f.Width; ix++)
            {
                var p = f.CellCentre(ix, iz);
                double best = double.MaxValue; int bestHole = -1;
                foreach (var h in holes)
                {
                    double d = double.MaxValue;
                    foreach (var t in h.Hole.Tees) d = Math.Min(d, Vec2.Distance(p, t));
                    for (int i = 1; i < h.Route.Count; i++) d = Math.Min(d, CellMask.SegmentDistance(p, h.Route[i - 1], h.Route[i]));
                    if (d < best) { best = d; bestHole = h.Hole.Number; }
                }
                result[iz * f.Width + ix] = bestHole == holeNumber && best <= reach;
            }
        });
        return result;
    }
}
