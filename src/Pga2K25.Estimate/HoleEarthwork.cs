using System.Globalization;
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
}

/// <summary>
/// The one-hole earthwork slice. Ground inside play splines (fairway, rough, heavy rough, greens, bunkers, waste,
/// cart paths) and the assumed tee pads is manufactured and gets the hole's zone class; everything else is natural
/// ground with no earthwork, unless a drawn zone in Estimate.json says otherwise.
/// </summary>
public static class HoleEarthwork
{
    public static HoleEstimate Build(CourseModel course, int holeNumber, ZonePresetFile presets, UnitCostFile costs,
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

        // Manufactured ground: every play spline and painted feature, plus a round pad at each tee.
        var made = new CellMask(field);
        foreach (var poly in layers.Grass.Concat(layers.Greens).Concat(layers.BunkerAreas).Concat(layers.WasteAreas)) made.Polygon(poly);
        foreach (var r in layers.BunkerRibbons.Concat(layers.WasteRibbons).Concat(layers.Paths)) made.Ribbon(r);
        foreach (var d in layers.BunkerBrushes.Concat(layers.WasteBrushes)) made.Disc(d.Centre, d.Radius);
        var teePads = new CellMask(field);
        foreach (var h in course.Holes) foreach (var t in h.Tees) teePads.Disc(t, teeRadius);
        for (int k = 0; k < made.Cells.Length; k++) made.Cells[k] |= teePads.Cells[k];

        // Each cell belongs to the hole whose route or tees are nearest.
        var counted = NearestHole(field, layers, holeNumber, options.ReachMetres);

        // Zones: −1 natural; index into classes otherwise. Drawn zones win over the spline default, later over earlier.
        var classes = new List<ZoneParams>();
        int ClassIndex(string name)
        {
            var p = presets.Get(name);
            if (p.NaturalFidelity + input.FidelityOffset >= 1) return -1;   // fully natural: no earthwork
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
        var ew = Earthwork.Compute(raster, feather, input.FidelityOffset);
        var bal = Earthwork.Balance(ew.CutCuM, ew.FillCuM, defaults.Shrink, defaults.Swell);

        // Measured areas inside this hole's cells (greens and bunkers from splines; tees are assumed pads).
        double cellSqFt = field.Cell * field.Cell * Units.SquareFeetPerSquareMetre;
        double Area(CellMask m) => Enumerable.Range(0, m.Cells.Length).Count(k => m.Cells[k] && counted[k]) * cellSqFt;
        var greens = new CellMask(field); foreach (var g in layers.Greens) greens.Polygon(g);
        var bunkers = new CellMask(field);
        foreach (var b in layers.BunkerAreas) bunkers.Polygon(b);
        foreach (var r in layers.BunkerRibbons) bunkers.Ribbon(r);
        foreach (var d in layers.BunkerBrushes) bunkers.Disc(d.Centre, d.Radius);
        double madeSqFt = Area(made);

        string cls = holeClassName;
        var p0 = presets.Get(cls);
        string shape = p0.BaselineWavelengthYd > 0
            ? FormattableString.Invariant($"wavelength {p0.BaselineWavelengthYd:0} yd")
            : "best-fit plane";
        string basis = FormattableString.Invariant(
            $"finished surface vs {cls} baseline, g={Math.Clamp(p0.NaturalFidelity + input.FidelityOffset, 0, 1):0.##}, {shape}, {defaults.FeatherYd:0} yd feather");
        var cy = Units.CubicYardsPerCubicMetre;
        var rows = new List<QuantityRow>
        {
            new(holeNumber, "Manufactured ground", madeSqFt / 43560, "acre", Provenance.Measured, null, "inside play splines, paths and tee pads"),
            new(holeNumber, "Green area", Area(greens), "sq ft", Provenance.Measured, null, "green splines"),
            new(holeNumber, "Bunker area", Area(bunkers), "sq ft", Provenance.Measured, null, "bunker splines, ribbons and painted bunker"),
            new(holeNumber, "Tee pads", hole.Tees.Count * Math.PI * teeRadius * teeRadius * Units.SquareFeetPerSquareMetre, "sq ft",
                Provenance.Assumed, Confidence.Low, $"{hole.Tees.Count} tees × {defaults.TeePadRadiusYd:0} yd radius pad (tees are points in the file)"),
            new(holeNumber, "Cut", ew.CutCuM * cy, "cu yd", Provenance.Computed, Confidence.Low, basis),
            new(holeNumber, "Fill", ew.FillCuM * cy, "cu yd", Provenance.Computed, Confidence.Low, basis),
        };
        var balanced = new QuantityRow(holeNumber, "Earthwork balanced on site", bal.BalancedCuM * cy, "cu yd", Provenance.Computed, Confidence.Low,
            $"cut placed as fill, shrink {defaults.Shrink:P0}");
        var import = new QuantityRow(holeNumber, "Imported fill", bal.ImportCuM * cy, "cu yd", Provenance.Computed, Confidence.Low, "fill not met by cut");
        var export = new QuantityRow(holeNumber, "Surplus cut hauled off", bal.ExportCuM * cy, "cu yd", Provenance.Computed, Confidence.Low,
            $"loose volume, swell {defaults.Swell:P0}");
        rows.AddRange([balanced, import, export]);

        // The cut's rock factor scales the earthwork price (1 unless a rock or cliff class is used).
        double rock = ew.CutCuM > 0 ? ew.RockWeightedCutCuM / ew.CutCuM : 1;
        var lines = new List<LineItem>
        {
            LineItem.Price(balanced, "earthwork_onsite", costs.Get("earthwork_onsite"), rock),
            LineItem.Price(import, "earthwork_import", costs.Get("earthwork_import")),
            LineItem.Price(export, "earthwork_export", costs.Get("earthwork_export")),
        };

        var assumptions = new List<Assumption>
        {
            new($"Hole {holeNumber} play ground", $"{cls}: natural fidelity {p0.NaturalFidelity:0.##}, " +
                (p0.BaselineWavelengthYd > 0 ? $"wavelength {p0.BaselineWavelengthYd:0} yd" : "plane baseline") + $", rock ×{p0.RockFactor:0.#}",
                input.ForHole(holeNumber).ZoneClass is null ? $"preset default ({p0.Source})" : "Estimate.json", Confidence.Low),
            new("Outside play splines", "natural ground, no earthwork", "Alex, 2026-10-19", Confidence.Medium),
            new("Tees", $"round pads of {defaults.TeePadRadiusYd:0} yd radius", defaults.Source, Confidence.Low),
            new("Shrink / swell", $"{defaults.Shrink:P0} / {defaults.Swell:P0}", defaults.Source, Confidence.Low),
            new("Feature footprints", "greens, tees and bunkers are included in bulk cut/fill; their unit prices cover the construction profile only",
                "estimate module design", Confidence.Medium),
        };
        if (input.FidelityOffset != 0)
            assumptions.Add(new("All zones", $"fidelity offset {input.FidelityOffset:+0.##;-0.##}", "Estimate.json", Confidence.Low));
        foreach (var z in input.Zones)
            assumptions.Add(new($"Drawn zone{(z.Note.Length > 0 ? $" ({z.Note})" : "")}", z.Class, "Estimate.json", Confidence.Low));

        return new HoleEstimate
        {
            Hole = hole, Raster = raster, Earthwork = ew, Balance = bal, Quantities = rows, Lines = lines,
            Assumptions = assumptions, Manufactured = made.Cells,
        };
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
