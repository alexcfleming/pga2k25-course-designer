using System.Globalization;
using SkiaSharp;
using static Pga2K25.Estimate.ReportStyle;

namespace Pga2K25.Estimate;

/// <summary>Everything the printed estimate shows.</summary>
public sealed record EstimateReport
{
    public required string CourseName { get; init; }
    public required DateOnly Date { get; init; }
    public required CourseRun Likely { get; init; }
    public required IReadOnlyDictionary<string, CourseRun> Scenarios { get; init; }
    public required ScenarioFile ScenarioSettings { get; init; }
    public required IReadOnlyList<SweepPoint> Sweep { get; init; }
    public required UnitCostFile Costs { get; init; }
    public required bool Partial { get; init; }
    public required int CourseHoles { get; init; }
}

/// <summary>One page: a title for the viewer and a draw call (run once for the PDF, once for the PNG).</summary>
public sealed record ReportPage(string Name, Action<SKCanvas> Draw);

/// <summary>
/// Letter-portrait report pages: summary with the low/likely/high range, per-hole table, scenarios and the fidelity
/// sweep, the assumption register with price sources, and a cut/fill map per hole.
/// </summary>
public static class ReportRenderer
{
    public const float W = 612, H = 792, M = 44;
    const float Top = 118, Bottom = H - 58;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static List<ReportPage> Pages(EstimateReport r)
    {
        var pages = new List<ReportPage>
        {
            new("01 Summary", c => Summary(c, r)),
            new("02 Holes", c => HoleTable(c, r)),
            new("03 Scenarios", c => Scenarios(c, r)),
        };
        var register = RegisterRows(r);
        var chunks = Paginate(register);
        for (int i = 0; i < chunks.Count; i++)
        {
            var rows = chunks[i];
            int n = i;
            pages.Add(new($"{4 + i:00} Assumptions", c =>
            {
                Frame(c, r, "Assumption register", n == 0 ? "What the land, quantities and prices rest on" : "continued");
                float y = Top;
                foreach (var row in rows) y = row.Draw(c, y);
            }));
        }
        foreach (var e in r.Likely.Holes)
        {
            var hole = e;
            pages.Add(new($"Hole_{hole.Hole.Number:00}", c => MapPage(c, r, hole)));
        }
        int total = pages.Count;
        return pages.Select((p, i) => p with { Draw = c => { p.Draw(c); PageNumber(c, i + 1, total); } }).ToList();
    }

    // ---------- page furniture ----------

    static void Frame(SKCanvas c, EstimateReport r, string title, string subtitle)
    {
        c.Clear(Cream);
        using var gold = Paint(Gold);
        using var ink = Paint(Ink);
        using var gray = Paint(Gray);
        using var small = Font(9);
        using var big = Font(24);
        using var sub = Font(11, italic: true);
        c.DrawText($"{r.CourseName.ToUpperInvariant()}  ·  CONSTRUCTION ESTIMATE", M, M + 6, SKTextAlign.Left, small, gold);
        c.DrawText(r.Date.ToString("d MMMM yyyy", Inv), W - M, M + 6, SKTextAlign.Right, small, gray);
        c.DrawText(title, M, M + 40, SKTextAlign.Left, big, ink);
        c.DrawText(subtitle, M, M + 58, SKTextAlign.Left, sub, gray);
        using var rule = Paint(Gold, stroke: true, width: 0.8f);
        c.DrawLine(M, M + 68, W - M, M + 68, rule);
        using var foot = Font(7.5f);
        c.DrawLine(M, H - 40, W - M, H - 40, rule);
        c.DrawText("Illustrative estimate for a virtual course: the starting ground is assumed, PLACEHOLDER values are marked. Not a bid.",
            M, H - 28, SKTextAlign.Left, foot, gray);
        c.DrawText("Heights in feet, distances in yards, volumes in cubic yards. Excludes land, clubhouse, maintenance building, equipment, financing.",
            M, H - 18, SKTextAlign.Left, foot, gray);
    }

    static void PageNumber(SKCanvas c, int n, int total)
    {
        using var f = Font(7.5f);
        using var p = Paint(Gray);
        c.DrawText($"{n} / {total}", W - M, H - 18, SKTextAlign.Right, f, p);
    }

    static float Heading(SKCanvas c, string text, float x, float y)
    {
        using var f = Font(12);
        using var p = Paint(Ink);
        c.DrawText(text, x, y, SKTextAlign.Left, f, p);
        return y + 14;
    }

    static List<string> Wrap(string text, SKFont font, float width)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string trial = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(trial) <= width || line.Length == 0) line = trial;
                else { lines.Add(line); line = word; }
            }
            lines.Add(line);
        }
        return lines;
    }

    static float Paragraph(SKCanvas c, string text, float x, float y, float width, float size = 9.5f, SKColor? colour = null, bool italic = false)
    {
        using var f = Font(size, italic);
        using var p = Paint(colour ?? Ink);
        foreach (var line in Wrap(text, f, width))
        {
            c.DrawText(line, x, y, SKTextAlign.Left, f, p);
            y += size * 1.35f;
        }
        return y;
    }

    // ---------- page 1: summary ----------

    static void Summary(SKCanvas c, EstimateReport r)
    {
        var s = r.Likely.Summary;
        string scope = r.Partial ? $"{s.Holes} of {r.CourseHoles} holes only (partial)" : $"{s.Holes} holes";
        Frame(c, r, "Summary", $"{scope} · total project budget, low / likely / high");
        double low = r.Scenarios.TryGetValue("low", out var lo) ? lo.Summary.Total : s.Total;
        double high = r.Scenarios.TryGetValue("high", out var hi) ? hi.Summary.Total : s.Total;

        using var ink = Paint(Ink);
        using var gray = Paint(Gray);
        using var headline = Font(34);
        using var label = Font(10);
        c.DrawText(Money(s.Total), M, Top + 26, SKTextAlign.Left, headline, ink);
        c.DrawText("likely", M + headline.MeasureText(Money(s.Total)) + 8, Top + 26, SKTextAlign.Left, label, gray);
        c.DrawText($"range {Money(low)} to {Money(high)}", M, Top + 44, SKTextAlign.Left, label, gray);

        RangeBar(c, M, Top + 62, W - 2 * M, low, s.Total, high);

        // Roll-up (left) and where the money goes (right).
        float y0 = Top + 140, colW = (W - 2 * M - 24) / 2;
        float y = Heading(c, "Roll-up", M, y0);
        var rows = new (string, double, bool)[]
        {
            ("Hole-direct", s.HoleDirect, false), ("Course-wide items", s.CourseWide, false), ("Hard cost", s.Hard, true),
            ("Regional adjustment", s.RegionalAdjustment, false), ("Escalation to midpoint", s.Escalation, false),
            ("Contingency", s.Contingency, false), ("Construction budget", s.Construction, true),
            ("Design fee", s.DesignFee, false), ("Field rep", s.FieldRep, false), ("Permits, engineering", s.Permits, false),
            ("Owner costs", s.OwnerCosts, false), ("Total project budget", s.Total, true),
        };
        using var row = Font(9.5f);
        using var rule = Paint(RuleLine, stroke: true, width: 0.6f);
        foreach (var (name, v, bold) in rows)
        {
            y += 15;
            if (bold) c.DrawLine(M, y - 11, M + colW, y - 11, rule);
            using var p = Paint(bold ? Ink : Gray);
            c.DrawText(name, M, y, SKTextAlign.Left, row, p);
            c.DrawText(Money(v), M + colW, y, SKTextAlign.Right, row, p);
        }

        float x2 = M + colW + 24;
        float yb = Heading(c, "Where the money goes", x2, y0);
        var cats = s.ByCategory.Where(k => k.Cost > 0).Select(k => (Item: k.Item, Cost: k.Cost))
            .Append((Item: "Course-wide items", Cost: s.CourseWide)).OrderByDescending(k => k.Cost).ToList();
        double max = cats.Max(k => k.Cost);
        using var small = Font(8.5f);
        using var bar = Paint(Gold);
        foreach (var (item, cost) in cats)
        {
            yb += 14;
            c.DrawText(item.Replace("Earthwork balanced on site", "Earthwork"), x2, yb, SKTextAlign.Left, small, gray);
            float bx = x2 + 104, bw = (float)(cost / max * (colW - 104 - 38));
            c.DrawRect(bx, yb - 7.5f, Math.Max(bw, 0.5f), 8, bar);
            c.DrawText(Money(cost), bx + bw + 4, yb, SKTextAlign.Left, small, gray);
        }

        // What it rests on.
        y = Math.Max(y, yb) + 30;
        y = Heading(c, "What it rests on", M, y);
        y = Stacked(c, M, y + 6, W - 2 * M, "Quantities (share of hard cost)",
            [("measured", s.ShareByProvenance[Provenance.Measured], Ink), ("computed", s.ShareByProvenance[Provenance.Computed], Gold),
             ("assumed", s.ShareByProvenance[Provenance.Assumed], SKColor.Parse("#b9b3a6"))]);
        y = Stacked(c, M, y + 8, W - 2 * M, "Unit prices (share of hard cost)",
            [("researched or derived", 1 - s.PlaceholderShare, Ink), ("PLACEHOLDER", s.PlaceholderShare, SKColor.Parse("#b9b3a6"))]);

        y += 14;
        string notes = string.Format(Inv,
            "Construction {0} per hole ({1}: the {2} to {3} sanity band is itself a placeholder) and {4} per acre over {5:0} acres of {6}. " +
            "Natural ground outside the play splines is assumed untouched; inside, the starting ground is a smoothed version of the finished ground set by each zone class. " +
            "Displayed figures are rounded: the range matters more than the digits.",
            Money(s.ConstructionPerHole), s.BenchmarkCheck.ToLowerInvariant(), Money(s.BenchmarkLow), Money(s.BenchmarkHigh),
            Money(s.ConstructionPerAcre), s.Acres, s.AcresBasis);
        Paragraph(c, notes, M, y, W - 2 * M, 9, Gray);
    }

    static void RangeBar(SKCanvas c, float x, float y, float width, double low, double likely, double high)
    {
        double step = NiceStep(high * 1.1 / 5);
        double top = Math.Ceiling(high * 1.08 / step) * step;
        float X(double v) => x + (float)(v / top * width);
        using var track = Paint(Pale);
        using var band = Paint(Mix(Cream, Gold, 0.55));
        using var ink = Paint(Ink);
        using var gray = Paint(Gray);
        using var tick = Paint(RuleLine, stroke: true, width: 0.6f);
        using var f = Font(8);
        using var fb = Font(9.5f);
        c.DrawRect(x, y + 10, width, 14, track);
        c.DrawRect(X(low), y + 10, X(high) - X(low), 14, band);
        for (double v = 0; v <= top + 1e-6; v += step)
        {
            c.DrawLine(X(v), y + 26, X(v), y + 30, tick);
            c.DrawText(Money(v), X(v), y + 40, SKTextAlign.Center, f, gray);
        }
        foreach (var (v, name) in new[] { (low, "low"), (high, "high") })
            c.DrawText($"{name} {Money(v)}", X(v), y + 6, SKTextAlign.Center, f, gray);
        float lx = X(likely);
        using var b = new SKPathBuilder();
        b.MoveTo(new SKPoint(lx, y + 8)); b.LineTo(new SKPoint(lx + 6, y + 17)); b.LineTo(new SKPoint(lx, y + 26)); b.LineTo(new SKPoint(lx - 6, y + 17)); b.Close();
        using var path = b.Detach();
        c.DrawPath(path, ink);
        c.DrawText($"likely {Money(likely)}", lx, y + 52, SKTextAlign.Center, fb, ink);
    }

    static double NiceStep(double raw)
    {
        double mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1))));
        double n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag;
    }

    static float Stacked(SKCanvas c, float x, float y, float width, string label, (string Name, double Share, SKColor Colour)[] parts)
    {
        using var f = Font(8.5f);
        using var gray = Paint(Gray);
        c.DrawText(label, x, y + 8, SKTextAlign.Left, f, gray);
        float bx = x + 150, bw = width - 150, cx = bx;
        foreach (var part in parts)
        {
            float w = (float)(Math.Clamp(part.Share, 0, 1) * bw);
            using var p = Paint(part.Colour);
            c.DrawRect(cx, y, w, 10, p);
            cx += w;
        }
        float lx = bx;
        foreach (var part in parts)
        {
            using var p = Paint(part.Colour);
            c.DrawRect(lx, y + 15, 7, 7, p);
            string t = string.Format(Inv, "{0} {1:P0}", part.Name, part.Share);
            c.DrawText(t, lx + 10, y + 22, SKTextAlign.Left, f, gray);
            lx += f.MeasureText(t) + 26;
        }
        return y + 30;
    }

    // ---------- page 2: holes ----------

    static void HoleTable(SKCanvas c, EstimateReport r)
    {
        Frame(c, r, "Holes", "Hole-direct quantities and cost, likely case");
        var cols = new (string Head, float Right)[]
        {
            ("Hole", M + 30), ("Par", M + 54), ("Acres", M + 92), ("Cut cy", M + 140), ("Fill cy", M + 188), ("Greens sf", M + 240),
            ("Bunkers sf", M + 296), ("Path lf", M + 342), ("Heads", M + 378), ("Total", M + 430),
        };
        float barX = M + 440, barW = W - M - barX;
        using var head = Font(8);
        using var cell = Font(8.5f);
        using var gray = Paint(Gray);
        using var ink = Paint(Ink);
        using var rule = Paint(RuleLine, stroke: true, width: 0.6f);
        float y = Top + 8;
        foreach (var (h, right) in cols) c.DrawText(h, right, y, SKTextAlign.Right, head, gray);
        c.DrawLine(M, y + 5, W - M, y + 5, rule);

        var holes = r.Likely.Holes;
        double max = holes.Max(e => e.Total);
        double Q(HoleEstimate e, string item) => e.Quantities.First(q => q.Item == item).Qty;
        var unbuilt = new List<int>();
        float rowH = Math.Min(24, (Bottom - 150 - y) / Math.Max(1, holes.Count + 1));
        foreach (var e in holes)
        {
            y += rowH;
            if (Q(e, "Greens") <= 0) unbuilt.Add(e.Hole.Number);
            using var par = Paint(ParColour(e.Hole.Par));
            c.DrawRoundRect(M, y - 10, 32, 13, 3, 3, par);
            string[] values =
            [
                e.Hole.Number.ToString(Inv), e.Hole.Par.ToString(Inv), e.ManufacturedAcres.ToString("0.0", Inv),
                e.Earthwork.CutCuYd.ToString("N0", Inv), e.Earthwork.FillCuYd.ToString("N0", Inv),
                Q(e, "Greens").ToString("N0", Inv), Q(e, "Bunkers").ToString("N0", Inv), Q(e, "Cart path").ToString("N0", Inv),
                Q(e, "Irrigation").ToString("N0", Inv), Money(e.Total),
            ];
            for (int i = 0; i < cols.Length; i++)
            {
                using var p = Paint(i == 0 ? SKColors.White : Ink);
                c.DrawText(values[i], i == 0 ? M + 16 : cols[i].Right, y, i == 0 ? SKTextAlign.Center : SKTextAlign.Right, cell, p);
            }
            using var bar = Paint(Mix(Cream, Gold, 0.8));
            c.DrawRect(barX, y - 8, (float)(e.Total / max * barW), 8, bar);
            if (e.Lines.Any(l => l.Quantity.Item == "Cart path" && l.Quantity.Provenance == Provenance.Assumed))
                c.DrawText("†", cols[7].Right + 3, y, SKTextAlign.Left, cell, gray);
        }
        y += 8;
        c.DrawLine(M, y, W - M, y, rule);
        y += 14;
        string[] totals =
        [
            "All", "", holes.Sum(e => e.ManufacturedAcres).ToString("0", Inv), holes.Sum(e => e.Earthwork.CutCuYd).ToString("N0", Inv),
            holes.Sum(e => e.Earthwork.FillCuYd).ToString("N0", Inv), holes.Sum(e => Q(e, "Greens")).ToString("N0", Inv),
            holes.Sum(e => Q(e, "Bunkers")).ToString("N0", Inv), holes.Sum(e => Q(e, "Cart path")).ToString("N0", Inv),
            holes.Sum(e => Q(e, "Irrigation")).ToString("N0", Inv), Money(holes.Sum(e => e.Total)),
        ];
        for (int i = 0; i < cols.Length; i++)
            c.DrawText(totals[i], i == 0 ? M + 16 : cols[i].Right, y, i == 0 ? SKTextAlign.Center : SKTextAlign.Right, cell, ink);

        y += 26;
        string note = "Acres are manufactured ground: play splines, paths and tee pads. Greens, bunkers and paths are measured from the course file; " +
            "tee pads, drainage and irrigation heads come from placeholder rules (quantity-rules.json). † no cart path drawn on the hole, so its length is assumed from the route.";
        if (unbuilt.Count > 0)
            note += $" Holes {string.Join(", ", unbuilt)} have no green yet and look unbuilt; their figures are mostly assumptions.";
        Paragraph(c, note, M, y, W - 2 * M, 8.5f, Gray);
    }

    // ---------- page 3: scenarios and sweep ----------

    static void Scenarios(SKCanvas c, EstimateReport r)
    {
        Frame(c, r, "Scenarios", "How the total moves with prices, quantities and the land assumption");
        using var head = Font(8);
        using var cell = Font(9);
        using var gray = Paint(Gray);
        using var ink = Paint(Ink);
        using var rule = Paint(RuleLine, stroke: true, width: 0.6f);
        float y = Top + 8;
        var cols = new (string Head, float Right)[]
        {
            ("Case", M + 50), ("Fidelity", M + 110), ("Prices", M + 170), ("No-range spread", M + 250), ("Assumed qty", M + 320),
            ("Contingency", M + 390), ("Construction", M + 460), ("Total", W - M),
        };
        foreach (var (h, right) in cols) c.DrawText(h, right, y, SKTextAlign.Right, head, gray);
        c.DrawLine(M, y + 5, W - M, y + 5, rule);
        foreach (var (name, run) in r.Scenarios)
        {
            y += 18;
            var sc = r.ScenarioSettings.Get(name);
            string[] v =
            [
                name, sc.FidelityOffset.ToString("+0.00;-0.00;0", Inv), sc.Prices.ToString().ToLowerInvariant(), sc.NoRangeSpread.ToString("+0%;-0%;0%", Inv),
                sc.AssumedQuantityFactor.ToString("×0.00", Inv), run.Summary.Settings["contingency"].Value.ToString("0%", Inv),
                Money(run.Summary.Construction), Money(run.Summary.Total),
            ];
            for (int i = 0; i < cols.Length; i++) c.DrawText(v[i], cols[i].Right, y, SKTextAlign.Right, cell, name == "likely" ? ink : gray);
        }
        y += 18;
        y = Paragraph(c, "Fidelity is added to every zone's natural fidelity (more natural ground, less earthwork). Prices use each item's low or high where " +
            "research gave a range; items without one move by the no-range spread. Assumed quantities (tee pads, drainage, heads, undrawn paths) scale by their factor. " +
            "All scenario settings are placeholders (scenarios.json).", M, y, W - 2 * M, 8.5f, Gray);

        // Fidelity sweep: earth moved per offset, total above each bar.
        y += 18;
        y = Heading(c, "Fidelity sweep", M, y);
        var sweep = r.Sweep;
        if (sweep.Count == 0) return;
        float chartTop = y + 22, chartH = 190, chartW = W - 2 * M;
        double maxVol = Math.Max(1, sweep.Max(p => p.CutCuYd + p.FillCuYd));
        float slot = chartW / sweep.Count, bw = slot * 0.5f;
        using var cutPaint = Paint(Cut);
        using var fillPaint = Paint(Fill);
        using var small = Font(8);
        for (int i = 0; i < sweep.Count; i++)
        {
            var p = sweep[i];
            float cx = M + slot * (i + 0.5f), baseY = chartTop + chartH;
            float hc = (float)(p.CutCuYd / maxVol * chartH), hf = (float)(p.FillCuYd / maxVol * chartH);
            c.DrawRect(cx - bw / 2, baseY - hc, bw, hc, cutPaint);
            c.DrawRect(cx - bw / 2, baseY - hc - hf, bw, hf, fillPaint);
            c.DrawText(Money(p.Total), cx, baseY - hc - hf - 6, SKTextAlign.Center, small, Math.Abs(p.FidelityOffset) < 1e-9 ? ink : gray);
            c.DrawText(p.FidelityOffset.ToString("+0.0;-0.0;0", Inv), cx, baseY + 12, SKTextAlign.Center, small, gray);
            c.DrawText($"{(p.CutCuYd + p.FillCuYd) / 1000:0}k cy", cx, baseY + 23, SKTextAlign.Center, small, gray);
        }
        c.DrawLine(M, chartTop + chartH, W - M, chartTop + chartH, rule);
        y = chartTop + chartH + 40;
        c.DrawRect(M, y - 7, 7, 7, cutPaint); c.DrawText("cut", M + 10, y, SKTextAlign.Left, small, gray);
        c.DrawRect(M + 40, y - 7, 7, 7, fillPaint); c.DrawText("fill", M + 50, y, SKTextAlign.Left, small, gray);
        c.DrawText("bar label: total project budget at that offset", M + 80, y, SKTextAlign.Left, small, gray);
        double swing = sweep.Max(p => p.Total) - sweep.Min(p => p.Total);
        double mid = r.Likely.Summary.Total;
        Paragraph(c, string.Format(Inv,
            "Across the sweep the earth moved changes {0:0}× while the total changes by {1} ({2:P1} of the likely total). " +
            "At these prices earthwork is a small share of the budget, so the land assumption matters less than the unit prices; " +
            "imported fill and haul-off are not priced yet, which understates the swing.",
            (sweep.Max(p => p.CutCuYd + p.FillCuYd) / Math.Max(1, sweep.Min(p => p.CutCuYd + p.FillCuYd))), Money(swing), mid > 0 ? swing / mid : 0),
            M, y + 18, W - 2 * M, 8.5f, Gray);
    }

    // ---------- assumption register ----------

    sealed record FlowRow(float Height, Func<SKCanvas, float, float> Draw, bool KeepWithNext = false);

    static List<FlowRow> RegisterRows(EstimateReport r)
    {
        var rows = new List<FlowRow>();
        float colScope = M, colStatement = M + 120, colSetBy = M + 360, colConf = W - M;
        float wScope = 112, wStatement = 232, wSetBy = 122;

        rows.Add(HeadingRow("Land and quantities"));
        rows.Add(HeaderRow([("Applies to", colScope, false), ("Assumption", colStatement, false), ("Set by", colSetBy, false), ("Confidence", colConf, true)]));

        // Same assumption on several holes → one row listing the holes.
        var grouped = r.Likely.Holes
            .SelectMany(e => e.Assumptions.Select(a => (e.Hole.Number, a)))
            .GroupBy(t => (Scope: System.Text.RegularExpressions.Regex.Replace(t.a.Scope, @"^Hole \d+ ", ""), t.a.Statement, t.a.SetBy, t.a.Confidence))
            .Select(g => (g.Key, Holes: g.Select(t => t.Number).Distinct().Order().ToList()));
        int all = r.Likely.Holes.Count;
        foreach (var (key, holes) in grouped)
        {
            string scope = key.Scope + (holes.Count == all ? "" : $" (holes {Ranges(holes)})");
            rows.Add(TextRow([(scope, colScope, wScope), (key.Statement, colStatement, wStatement), (key.SetBy, colSetBy, wSetBy)],
                key.Confidence.ToString().ToLowerInvariant(), colConf));
        }
        foreach (var l in r.Likely.CourseWide.Where(l => l.Quantity.Provenance != Provenance.Measured))
            rows.Add(TextRow([("Course-wide", colScope, wScope), ($"{l.Quantity.Item}: {l.Quantity.Basis}", colStatement, wStatement), ("defaults", colSetBy, wSetBy)],
                (l.Quantity.Confidence ?? Confidence.Low).ToString().ToLowerInvariant(), colConf));

        rows.Add(new FlowRow(16, (c, y) => y + 16));
        rows.Add(HeadingRow("Unit prices"));
        float cItem = M, cPrice = M + 200, cRange = M + 270, cStatus = M + 282, cSource = M + 350;
        rows.Add(HeaderRow([("Item", cItem, false), ("Likely", cPrice, true), ("Low–high", cRange, true), ("Status", cStatus, false), ("Source", cSource, false)]));
        var used = r.Likely.Holes.SelectMany(e => e.Lines).Concat(r.Likely.CourseWide).Select(l => l.CostKey).Distinct().ToHashSet();
        foreach (var (key, cost) in r.Costs.Items.Where(k => used.Contains(k.Key)))
        {
            string price = cost.Price is { } p ? PriceText(p) + " / " + cost.Unit : "not priced";
            string range = cost.Low is { } lo && cost.High is { } hi ? $"{PriceText(lo)}–{PriceText(hi)}" : "";
            string status = cost.IsPlaceholder ? "PLACEHOLDER" : cost.Status + (cost.Year is { } yr ? $" {yr}" : "");
            rows.Add(PriceRow(cost.Label, price, range, status, cost.Source, cItem, cPrice, cRange, cStatus, cSource));
        }
        foreach (var (key, setting) in r.Likely.Summary.Settings)
            rows.Add(PriceRow(key.Replace('_', ' '), setting.Value < 2 ? setting.Value.ToString("0.###", Inv) : Money(setting.Value), "",
                setting.IsPlaceholder ? "PLACEHOLDER" : setting.Status, setting.Source, cItem, cPrice, cRange, cStatus, cSource));
        return rows;
    }

    static string PriceText(double p) => p >= 1000 ? Money(p) : p.ToString(p < 10 ? "$0.##" : "$0", Inv);

    static string Ranges(List<int> n)
    {
        var parts = new List<string>();
        for (int i = 0; i < n.Count;)
        {
            int j = i;
            while (j + 1 < n.Count && n[j + 1] == n[j] + 1) j++;
            parts.Add(j > i ? $"{n[i]}–{n[j]}" : $"{n[i]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    static FlowRow HeadingRow(string text) => new(22, (c, y) => Heading(c, text, M, y + 10) + 8, KeepWithNext: true);

    static FlowRow HeaderRow((string Text, float X, bool Right)[] cols) => new(16, (c, y) =>
    {
        using var f = Font(8);
        using var p = Paint(Gray);
        using var rule = Paint(RuleLine, stroke: true, width: 0.6f);
        foreach (var (t, x, right) in cols) c.DrawText(t, x, y + 8, right ? SKTextAlign.Right : SKTextAlign.Left, f, p);
        c.DrawLine(M, y + 12, W - M, y + 12, rule);
        return y + 16;
    }, KeepWithNext: true);

    static FlowRow TextRow((string Text, float X, float Width)[] cols, string right, float rightX)
    {
        using var f = Font(8.5f);
        var wrapped = cols.Select(col => Wrap(col.Text, f, col.Width)).ToList();
        float h = wrapped.Max(w => w.Count) * 11.5f + 5;
        return new FlowRow(h, (c, y) =>
        {
            using var font = Font(8.5f);
            using var ink = Paint(Ink);
            using var gray = Paint(Gray);
            for (int i = 0; i < cols.Length; i++)
                for (int k = 0; k < wrapped[i].Count; k++)
                    c.DrawText(wrapped[i][k], cols[i].X, y + 10 + k * 11.5f, SKTextAlign.Left, font, i == 1 ? ink : gray);
            c.DrawText(right, rightX, y + 10, SKTextAlign.Right, font, gray);
            return y + h;
        });
    }

    static FlowRow PriceRow(string item, string price, string range, string status, string source,
        float cItem, float cPrice, float cRange, float cStatus, float cSource)
    {
        using var f = Font(8);
        var src = Wrap(source, f, W - M - cSource);
        var it = Wrap(item, f, cPrice - cItem - 85);
        float h = Math.Max(src.Count, it.Count) * 10.5f + 5;
        bool placeholder = status == "PLACEHOLDER";
        return new FlowRow(h, (c, y) =>
        {
            using var font = Font(8);
            using var ink = Paint(Ink);
            using var gray = Paint(Gray);
            using var flag = Paint(placeholder ? Cut : Fill);
            for (int k = 0; k < it.Count; k++) c.DrawText(it[k], cItem, y + 10 + k * 10.5f, SKTextAlign.Left, font, ink);
            c.DrawText(price, cPrice, y + 10, SKTextAlign.Right, font, ink);
            c.DrawText(range, cRange, y + 10, SKTextAlign.Right, font, gray);
            c.DrawText(status, cStatus, y + 10, SKTextAlign.Left, font, flag);
            for (int k = 0; k < src.Count; k++) c.DrawText(src[k], cSource, y + 10 + k * 10.5f, SKTextAlign.Left, font, gray);
            return y + h;
        });
    }

    static List<List<FlowRow>> Paginate(List<FlowRow> rows)
    {
        var pages = new List<List<FlowRow>> { new() };
        float y = Top;
        foreach (var row in rows)
        {
            // Headings and column heads need room for a few rows after them.
            float need = row.Height + (row.KeepWithNext ? 60 : 0);
            if (y + need > Bottom && pages[^1].Count > 0) { pages.Add([]); y = Top; }
            pages[^1].Add(row);
            y += row.Height;
        }
        return pages;
    }

    // ---------- hole maps ----------

    static void MapPage(SKCanvas c, EstimateReport r, HoleEstimate e)
    {
        Frame(c, r, $"Hole {e.Hole.Number} · par {e.Hole.Par}", "Cut and fill against the assumed starting ground, likely case");
        using var img = EarthworkRenderer.RenderImage(e, r.CourseName, pixelsPerMetre: 3);
        float maxW = W - 2 * M, maxH = Bottom - 70 - Top;
        float scale = Math.Min(maxW / img.Width, maxH / img.Height);
        var dest = SKRect.Create(M + (maxW - img.Width * scale) / 2, Top, img.Width * scale, img.Height * scale);
        c.DrawImage(img, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        double Q(string item) => e.Quantities.First(q => q.Item == item).Qty;
        string text = string.Format(Inv,
            "Cut {0:N0} cu yd, fill {1:N0} cu yd; {2:N0} cu yd balanced on site, {3:N0} cu yd imported. Hole-direct {4} " +
            "({5:P0} on placeholder prices). Greens {6:N0} sq ft, bunkers {7:N0} sq ft, {8:0.0} acres manufactured.",
            e.Earthwork.CutCuYd, e.Earthwork.FillCuYd, Q("Earthwork balanced on site"), Q("Imported fill"), Money(e.Total), e.PlaceholderShare,
            Q("Greens"), Q("Bunkers"), e.ManufacturedAcres);
        Paragraph(c, text, M, dest.Bottom + 18, W - 2 * M, 9, Gray);
    }
}
