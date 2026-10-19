using System.Globalization;
using System.Text;
using System.Text.Json;
using Pga2K25.CourseFormat;

namespace Pga2K25.Estimate;

public sealed record EstimateOutputs(string JsonPath, string CsvPath, IReadOnlyList<string> Pngs, IReadOnlyList<HoleEstimate> Holes);

/// <summary>
/// Earthwork estimate for chosen holes: <c>&lt;course&gt; - Estimate.json</c> and <c>.csv</c> (line items, quantities with
/// provenance, assumption register) and a cut/fill PNG per hole. Per-course choices are read from <c>Estimate.json</c>
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
        Directory.CreateDirectory(outputDir);

        var numbers = holes ?? course.Holes.Select(h => h.Number).ToList();
        var results = new List<HoleEstimate>();
        var pngs = new List<string>();
        foreach (int n in numbers)
        {
            log?.Report($"Hole {n}: quantities");
            var e = HoleEstimator.Build(course, n, presets, costs, rules, input, options);
            results.Add(e);
            string png = Path.Combine(outputDir, $"{course.Name} - Earthwork Hole {n:00}.png");
            EarthworkRenderer.RenderPng(e, course.Name, png);
            pngs.Add(png);
            log?.Report(string.Format(CultureInfo.InvariantCulture, "  cut {0:N0} cu yd, fill {1:N0} cu yd, priced ${2:N0} ({3:P0} on placeholder prices), {4}",
                e.Earthwork.CutCuYd, e.Earthwork.FillCuYd, e.Total, e.PlaceholderShare, Path.GetFileName(png)));
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
            total = results.Sum(e => e.Total),
        }, EstimateData.Json));

        string csv = Path.Combine(outputDir, $"{course.Name} - Estimate.csv");
        File.WriteAllText(csv, Csv(results), new UTF8Encoding(true));
        log?.Report($"Wrote {Path.GetFileName(json)}, {Path.GetFileName(csv)}");
        return new EstimateOutputs(json, csv, pngs, results);
    }

    static string Csv(IEnumerable<HoleEstimate> holes)
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
        return sb.ToString();
    }

    static string Q(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
