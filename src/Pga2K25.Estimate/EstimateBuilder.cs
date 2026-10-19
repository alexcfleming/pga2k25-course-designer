using System.Globalization;
using System.Text;
using System.Text.Json;
using Pga2K25.CourseFormat;

namespace Pga2K25.Estimate;

public sealed record EstimateOutputs(string JsonPath, string CsvPath, IReadOnlyList<string> Pngs, IReadOnlyList<HoleEstimate> Holes,
    CourseSummary Summary, bool Partial, IReadOnlyDictionary<string, CourseRun> Scenarios, IReadOnlyList<SweepPoint> Sweep);

/// <summary>
/// Construction estimate: <c>&lt;course&gt; - Estimate.json</c> and <c>.csv</c> (hole and course-wide line items, quantities
/// with provenance, assumption register, the roll-up to a total budget) and a cut/fill PNG per hole. With only some
/// holes, the roll-up covers just those holes and is marked partial. Per-course choices are read from <c>Estimate.json</c>
/// in the output folder when present.
/// </summary>
public static class EstimateBuilder
{
    public static string InputPath(string outputDir) => Path.Combine(outputDir, "Estimate.json");

    public static EstimateOutputs Build(string coursePath, string outputDir, IReadOnlyList<int>? holes = null,
        EstimateOptions? options = null, IProgress<string>? log = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        var presets = EstimateData.LoadPresets();
        var costs = EstimateData.LoadUnitCosts();
        var rules = EstimateData.LoadRules();
        var input = EstimateData.LoadInput(InputPath(outputDir));
        var project = EstimateData.LoadProject(input);
        var scenarioFile = EstimateData.LoadScenarios();
        Directory.CreateDirectory(outputDir);

        var numbers = holes ?? course.Holes.Select(h => h.Number).ToList();
        var sites = new List<HoleSite>();
        foreach (int n in numbers)
        {
            log?.Report($"Hole {n}: terrain and features");
            sites.Add(HoleEstimator.Prepare(course, n, presets, rules, input, options));
        }
        var estimator = new CourseEstimator(sites, presets, costs, rules, input, project);
        var runs = scenarioFile.Scenarios.Keys.ToDictionary(k => k, k => estimator.Run(scenarioFile.Get(k)));
        var likely = runs.TryGetValue("likely", out var lk) ? lk : estimator.Run(Scenario.Likely);
        log?.Report("Fidelity sweep");
        var sweep = estimator.Sweep(scenarioFile.FidelitySweep);

        var results = likely.Holes;
        var courseWide = likely.CourseWide;
        var summary = likely.Summary;
        bool partial = results.Count < course.Holes.Count;
        var pngs = new List<string>();
        foreach (var e in results)
        {
            string png = Path.Combine(outputDir, $"{course.Name} - Earthwork Hole {e.Hole.Number:00}.png");
            EarthworkRenderer.RenderPng(e, course.Name, png);
            pngs.Add(png);
            log?.Report(string.Format(CultureInfo.InvariantCulture, "Hole {0}: cut {1:N0} cu yd, fill {2:N0} cu yd, priced ${3:N0} ({4:P0} on placeholder prices), {5}",
                e.Hole.Number, e.Earthwork.CutCuYd, e.Earthwork.FillCuYd, e.Total, e.PlaceholderShare, Path.GetFileName(png)));
        }

        string json = Path.Combine(outputDir, $"{course.Name} - Estimate.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new
        {
            course = course.Name,
            units = new { height = "ft", distance = "yd", volume = "cu yd" },
            status = "Illustrative estimate for a virtual course: assumed starting ground, many PLACEHOLDER values. Not a bid.",
            holes = results.Select(e => new
            {
                hole = e.Hole.Number,
                par = e.Hole.Par,
                quantities = e.Quantities,
                lines = e.Lines.Select(l => new
                {
                    item = l.Quantity.Item, qty = l.Quantity.Qty, unit = l.Quantity.Unit, unit_price = l.UnitPrice, cost = l.Cost,
                    price_status = l.PriceStatus, price_source = l.PriceSource,
                }),
                total = e.Total,
                placeholder_share = e.PlaceholderShare,
                share_by_provenance = e.ShareByProvenance,
                unpriced = e.UnpricedLines.Select(l => l.Quantity.Item),
                assumptions = e.Assumptions,
            }),
            course_wide = courseWide.Select(l => new
            {
                item = l.Quantity.Item, qty = l.Quantity.Qty, unit = l.Quantity.Unit, provenance = l.Quantity.Provenance, basis = l.Quantity.Basis,
                unit_price = l.UnitPrice, cost = l.Cost, price_status = l.PriceStatus, price_source = l.PriceSource,
            }),
            summary = new
            {
                partial,
                holes = summary.Holes,
                hole_direct = summary.HoleDirect, course_wide = summary.CourseWide, hard = summary.Hard,
                regional_adjustment = summary.RegionalAdjustment, escalation = summary.Escalation, escalated = summary.Escalated,
                contingency = summary.Contingency, construction = summary.Construction,
                design_fee = summary.DesignFee, field_rep = summary.FieldRep, permits = summary.Permits, owner_costs = summary.OwnerCosts,
                soft = summary.Soft, total = summary.Total,
                total_per_hole = summary.TotalPerHole, construction_per_hole = summary.ConstructionPerHole,
                acres = summary.Acres, acres_basis = summary.AcresBasis, construction_per_acre = summary.ConstructionPerAcre,
                benchmark = new { low = summary.BenchmarkLow, high = summary.BenchmarkHigh, check = summary.BenchmarkCheck },
                by_category = summary.ByCategory.Select(c => new { item = c.Item, cost = c.Cost }),
                placeholder_share = summary.PlaceholderShare,
                share_by_provenance = summary.ShareByProvenance,
                settings = summary.Settings,
                excludes = "land, clubhouse, maintenance building, equipment, financing, pre-opening costs",
            },
            scenarios = runs.ToDictionary(r => r.Key, r => new
            {
                settings = scenarioFile.Get(r.Key),
                cut_cu_yd = r.Value.CutCuYd, fill_cu_yd = r.Value.FillCuYd,
                hole_direct = r.Value.Summary.HoleDirect, course_wide = r.Value.Summary.CourseWide,
                construction = r.Value.Summary.Construction, soft = r.Value.Summary.Soft, total = r.Value.Summary.Total,
                construction_per_hole = r.Value.Summary.ConstructionPerHole,
            }),
            fidelity_sweep = sweep.Select(p => new
            {
                fidelity_offset = p.FidelityOffset, cut_cu_yd = p.CutCuYd, fill_cu_yd = p.FillCuYd, earthwork = p.Earthwork, total = p.Total,
            }),
        }, EstimateData.Json));

        string csv = Path.Combine(outputDir, $"{course.Name} - Estimate.csv");
        File.WriteAllText(csv, Csv(results, courseWide), new UTF8Encoding(true));
        log?.Report($"Wrote {Path.GetFileName(json)}, {Path.GetFileName(csv)}");
        return new EstimateOutputs(json, csv, pngs, results, summary, partial, runs, sweep);
    }

    static string Csv(IEnumerable<HoleEstimate> holes, IEnumerable<LineItem> courseWide)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("hole,item,qty,unit,provenance,confidence,basis,unit_price,cost,price_status\n");
        foreach (var e in holes)
        {
            var priced = e.Lines.ToDictionary(l => l.Quantity.Item);
            foreach (var q in e.Quantities)
            {
                priced.TryGetValue(q.Item, out var l);
                sb.Append(inv, $"{q.Hole},{Q(q.Item)},{q.Qty:0.##},{Q(q.Unit)},{q.Provenance},{q.Confidence},{Q(q.Basis)},")
                  .Append(inv, $"{l?.UnitPrice:0.##},{l?.Cost:0},{(l is null ? "" : l.IsPlaceholder ? "PLACEHOLDER" : l.PriceStatus)}\n");
            }
        }
        foreach (var l in courseWide)
        {
            var q = l.Quantity;
            sb.Append(inv, $"course,{Q(q.Item)},{q.Qty:0.##},{Q(q.Unit)},{q.Provenance},{q.Confidence},{Q(q.Basis)},")
              .Append(inv, $"{l.UnitPrice:0.####},{l.Cost:0},{(l.IsPlaceholder ? "PLACEHOLDER" : l.PriceStatus)}\n");
        }
        return sb.ToString();
    }

    static string Q(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
