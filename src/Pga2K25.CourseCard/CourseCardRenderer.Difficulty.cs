using Pga2K25.HoleSim;
using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed partial class CourseCardRenderer
{
    /// <summary>The whole course played by the hole design simulator; null for no difficulty sheet.</summary>
    public CourseDifficultyResult? Difficulty { get; init; }

    static readonly SKColor Easier = SKColor.Parse("#4F8F5F"), Middling = SKColor.Parse("#D8A43A"), Harder = SKColor.Parse("#C2553D"),
        PhaseTee = SKColor.Parse("#3F6FA6"), PhaseApproach = SKColor.Parse("#7A5EA8"), PhaseShort = SKColor.Parse("#D08A3A"), PhasePutting = SKColor.Parse("#3F8F8A"),
        ParShare = SKColor.Parse("#C9D4CC");

    /// <summary>Green (easier) through amber to red (harder), for <paramref name="t"/> from 0 to 1.</summary>
    static SKColor Heat(double t)
    {
        t = Math.Clamp(t, 0, 1);
        static SKColor Mix(SKColor a, SKColor b, double u) => new((byte)(a.Red + (b.Red - a.Red) * u), (byte)(a.Green + (b.Green - a.Green) * u), (byte)(a.Blue + (b.Blue - a.Blue) * u));
        return t < 0.5 ? Mix(Easier, Middling, t * 2) : Mix(Middling, Harder, (t - 0.5) * 2);
    }

    static string Signed(double v, string format = "0.00") => (v < 0 ? "−" : "+") + Math.Abs(v).ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The Course Difficulty Estimate: how hard the course plays for each player (headline, round figures, where the
    /// strokes go) on the left, and a difficulty scorecard on the right (expected scores, the birdie / par / bogey split,
    /// the pin range, the difficulty rank and a suggested handicap).
    /// </summary>
    public void DrawDifficultyPage(SKCanvas canvas)
    {
        var r = Difficulty ?? throw new InvalidOperationException("No course difficulty to draw.");
        var (w, h) = (CardPage.Width, CardPage.Height);
        canvas.Clear(_salt);
        float x0 = (_logo is null ? 0.030f : 0.082f) * w;
        Text(canvas, Title, x0, (1 - 0.955f) * h, 30, _bold, _ink, tracking: 0.32f * 30);
        Text(canvas, $"Course difficulty estimate  ·  every hole and pin played by the hole design simulator  ·  {ParLine}", x0, (1 - 0.925f) * h, 12.5f, _italic, Muted);
        int doubtful = r.Holes.Count(x => x.Doubtful);
        Text(canvas, $"From the back tees  ·  every pin played {r.Samples:N0} times per player  ·  course saved {r.CourseSavedUtc.ToLocalTime():d MMM yyyy HH:mm}",
            x0, (1 - 0.902f) * h, 9, _serif, Faint);
        var (main, _, halves) = HeaderTotals();
        Text(canvas, main, 0.970f * w, (1 - 0.955f) * h, 14, _bold, _ink, SKTextAlign.Right);
        Text(canvas, halves, 0.970f * w, (1 - 0.925f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
        using (var rule = Stroke(_fog, 1.3f)) canvas.DrawLine(0.024f * w, (1 - 0.884f) * h, 0.970f * w, (1 - 0.884f) * h, rule);
        if (_logo is not null) DrawLogo(canvas, FigRect(0.024f, 0.898f, 0.046f, 0.0674f, CardPage));

        DrawDifficultySummary(canvas, FigRect(0.030f, 0.075f, 0.300f, 0.790f, CardPage), r, doubtful);
        DrawDifficultyCard(canvas, FigRect(0.365f, 0.075f, 0.605f, 0.790f, CardPage), r);

        Text(canvas, "The simulated golfer is fitted to the baseline's anchors on a flat round green and reads harder than the Tour, so compare holes and treat the totals as an " +
                     "estimate. Greens are the replayed ground, smoothed; outside them the ground is flat.", 0.030f * w, h - 34, 7.6f, _italic, Faint);
        Text(canvas, "Par 3s play the same for both players (one tee shot model). Hollow ring: the replay makes over a quarter of that green too steep to hold a ball, " +
                     "so check the hole in game.", 0.030f * w, h - 23, 7.6f, _italic, Faint);
    }

    void DrawDifficultySummary(SKCanvas canvas, SKRect r, CourseDifficultyResult d, int doubtful)
    {
        var power = d.Power; var normal = d.Normal;
        float y = r.Top + 10;
        Text(canvas, "EXPECTED SCORE", r.Left, y, 9, _serif, Muted, tracking: 1.2f);
        y += 52;
        float colB = r.Left + r.Width * 0.52f;
        Text(canvas, power.Score.ToString("0.0"), r.Left, y, 46, _bold, _ink);
        Text(canvas, normal.Score.ToString("0.0"), colB, y, 46, _bold, Muted);
        y += 18;
        Text(canvas, $"Power Drive  ·  {Signed(power.ToPar, "0.0")} to par", r.Left, y, 10.5f, _serif, _ink);
        Text(canvas, $"Normal  ·  {Signed(normal.ToPar, "0.0")} to par", colB, y, 10.5f, _serif, Muted);
        y += 18;
        if (doubtful > 0)
            Text(canvas, $"{doubtful} of {d.Holes.Count} greens look too steep in the replay: see the hollow rings.", r.Left, y, 9, _italic, Harder);

        // Round figures.
        y += 34;
        Text(canvas, "PER ROUND", r.Left, y, 9, _serif, Muted, tracking: 1.2f);
        float cP = r.Left + r.Width * 0.78f, cN = r.Right;
        Text(canvas, "POWER", cP, y, 8, _serif, Muted, SKTextAlign.Right, tracking: 0.8f);
        Text(canvas, "NORMAL", cN, y, 8, _serif, Muted, SKTextAlign.Right, tracking: 0.8f);
        y += 8;
        using (var rule = Stroke(_fog, 1)) canvas.DrawLine(r.Left, y, r.Right, y, rule);
        static string Pct(double v) => double.IsNaN(v) ? "—" : $"{v * 100:0} %";
        var rows = new (string Label, string P, string N)[]
        {
            ("Fairways hit", Pct(power.FairwayHit), Pct(normal.FairwayHit)),
            ("Greens in regulation", Pct(power.GreenInRegulation), Pct(normal.GreenInRegulation)),
            ("Scrambling", Pct(power.Scrambling), Pct(normal.Scrambling)),
            ("Birdies", power.Birdies.ToString("0.0"), normal.Birdies.ToString("0.0")),
            ("Bogeys or worse", power.BogeysOrWorse.ToString("0.0"), normal.BogeysOrWorse.ToString("0.0")),
            ("Holes with a penalty", power.HolesWithPenalty.ToString("0.0"), normal.HolesWithPenalty.ToString("0.0")),
        };
        foreach (var row in rows)
        {
            y += 22;
            Text(canvas, row.Label, r.Left, y, 11, _serif, _ink);
            Text(canvas, row.P, cP, y, 11, _serif, _ink, SKTextAlign.Right);
            Text(canvas, row.N, cN, y, 11, _serif, Muted, SKTextAlign.Right);
            using var line = Stroke(_fog.WithAlpha(120), 0.6f);
            canvas.DrawLine(r.Left, y + 8, r.Right, y + 8, line);
        }

        // Where the strokes go: strokes lost to the baseline in each phase, per round (gained shows to the left).
        y += 46;
        Text(canvas, "WHERE THE STROKES GO", r.Left, y, 9, _serif, Muted, tracking: 1.2f);
        y += 13;
        Text(canvas, "strokes lost to the baseline per round, by phase", r.Left, y, 8.5f, _italic, Faint);
        var phases = new (string Name, SKColor Colour, double P, double N)[]
        {
            ("Tee", PhaseTee, -power.Sg.Tee, -normal.Sg.Tee),
            ("Approach", PhaseApproach, -power.Sg.Approach, -normal.Sg.Approach),
            ("Short game", PhaseShort, -power.Sg.Short, -normal.Sg.Short),
            ("Putting", PhasePutting, -power.Sg.Putting, -normal.Sg.Putting),
        };
        // One scale from the most gained (or 0) to the most lost (at least half a stroke), with room for the labels.
        double lo = Math.Min(0, phases.Min(p => Math.Min(p.P, p.N))), hi = Math.Max(0.5, phases.Max(p => Math.Max(p.P, p.N)));
        float labelW = 70, barLeft = r.Left + labelW + (lo < 0 ? 26 : 4), barRight = r.Right - 30;
        float X(double v) => barLeft + (float)((v - lo) / (hi - lo)) * (barRight - barLeft);
        float zero = X(0);
        y += 10;
        foreach (var p in phases)
        {
            y += 8;
            Text(canvas, p.Name, r.Left, y + 13, 10.5f, _serif, _ink);
            foreach (var (v, k, faint) in new[] { (p.P, 0, false), (p.N, 1, true) })
            {
                float top = y + k * 11, a = X(Math.Min(0, v)), b = X(Math.Max(0, v));
                using (var f = Fill(faint ? p.Colour.WithAlpha(110) : p.Colour)) canvas.DrawRect(a, top, Math.Max(0.6f, b - a), 9, f);
                Text(canvas, v < 0 ? Signed(v, "0.0") : v.ToString("0.0"), (v >= 0 ? b + 4 : a - 4), top + 8, 8, _serif, faint ? Muted : _ink, v >= 0 ? SKTextAlign.Left : SKTextAlign.Right);
            }
            y += 26;
        }
        using (var axis = Stroke(_ink.WithAlpha(150), 0.8f)) canvas.DrawLine(zero, y - phases.Length * 34 - 2, zero, y + 2, axis);
        y += 14;
        Text(canvas, "upper bar Power Drive, lower bar Normal", r.Left + labelW, y, 8, _italic, Faint);
    }

    void DrawDifficultyCard(SKCanvas canvas, SKRect r, CourseDifficultyResult d)
    {
        var holes = d.Holes.OrderBy(x => x.Hole).ToList();
        var ranked = d.Ranked;
        var rank = ranked.Select((x, i) => (x.Hole, i + 1)).ToDictionary(t => t.Hole, t => t.Item2);
        var index = CourseDifficulty.StrokeIndex(ranked);
        double lo = holes.Min(x => x.ToPar), hi = Math.Max(lo + 1e-6, holes.Max(x => x.ToPar));
        bool halves = holes.Count == 18;

        // Columns (x positions as shares of the card's width).
        float W(double f) => r.Left + (float)f * r.Width;
        float cHole = W(0.025), cPar = W(0.085), cYds = W(0.165), cPow = W(0.245), cNor = W(0.325);
        float toParL = W(0.355), toParR = W(0.545), splitL = W(0.575), splitR = W(0.775), pinL = W(0.795), pinR = W(0.880), cRank = W(0.945), cHcp = r.Right;
        float head = r.Top + 10;
        void Head(string s, float x, SKTextAlign a = SKTextAlign.Left) => Text(canvas, s, x, head, 8, _serif, Muted, a, tracking: 0.8f);
        Head("HOLE", cHole, SKTextAlign.Center); Head("PAR", cPar, SKTextAlign.Center); Head("YDS", cYds, SKTextAlign.Right);
        Head("POWER", cPow, SKTextAlign.Right); Head("NORMAL", cNor, SKTextAlign.Right);
        Head("OVER PAR (both)", toParL); Head("BIRDIE · PAR · BOGEY+", splitL); Head("PIN RANGE", pinL);
        Head("RANK", cRank, SKTextAlign.Right); Head("HCP", cHcp, SKTextAlign.Right);
        using (var rule = Stroke(_ink, 1)) canvas.DrawLine(r.Left, head + 7, r.Right, head + 7, rule);

        int rows = holes.Count + (halves ? 3 : 1);
        float rowH = Math.Min(32, (r.Bottom - head - 12) / rows);
        float y = head + 7;

        // Over-par scale shared by every hole row; zero marked.
        double scaleLo = Math.Min(0, holes.Min(x => Math.Min(x.PowerToPar, x.NormalToPar))), scaleHi = Math.Max(0.1, holes.Max(x => Math.Max(x.PowerToPar, x.NormalToPar)));
        float Tp(double v) => toParL + (float)((v - scaleLo) / (scaleHi - scaleLo)) * (toParR - toParL);
        double pinLo = Math.Min(0, holes.Min(x => x.PinRange.Easiest)), pinHi = Math.Max(0.1, holes.Max(x => x.PinRange.Hardest));
        float Pn(double v) => pinL + (float)((v - pinLo) / (pinHi - pinLo)) * (pinR - pinL);

        void Totals(string label, IReadOnlyList<HoleDifficulty> part)
        {
            using (var band = Fill(_fog.WithAlpha(90))) canvas.DrawRect(r.Left, y, r.Width, rowH, band);
            float b = y + rowH * 0.66f;
            Text(canvas, label, cHole, b, 10, _bold, _ink, SKTextAlign.Center, tracking: 0.6f);
            Text(canvas, part.Sum(x => x.Par).ToString(), cPar, b, 11, _bold, _ink, SKTextAlign.Center);
            Text(canvas, part.Sum(x => x.Yards).ToString("N0"), cYds, b, 11, _bold, _ink, SKTextAlign.Right);
            Text(canvas, part.Sum(x => x.PowerScore).ToString("0.0"), cPow, b, 11.5f, _bold, _ink, SKTextAlign.Right);
            Text(canvas, part.Sum(x => x.NormalScore).ToString("0.0"), cNor, b, 11.5f, _bold, Muted, SKTextAlign.Right);
            double avg = part.Sum(x => x.ToPar);
            Text(canvas, $"{Signed(avg, "0.0")} over par, both players averaged", toParL, b, 9.5f, _italic, Muted);
            y += rowH;
        }

        for (int i = 0; i < holes.Count; i++)
        {
            var x = holes[i];
            float mid = y + rowH / 2, b = y + rowH * 0.66f;
            HoleDisc(canvas, cHole, mid, Math.Min(10, rowH * 0.36f), x.Hole, x.Par);
            Text(canvas, x.Par.ToString(), cPar, b, 11, _serif, _ink, SKTextAlign.Center);
            Text(canvas, x.Yards.ToString("N0"), cYds, b, 11, _serif, _ink, SKTextAlign.Right);
            Text(canvas, x.PowerScore.ToString("0.00"), cPow, b, 11, _serif, _ink, SKTextAlign.Right);
            Text(canvas, x.NormalScore.ToString("0.00"), cNor, b, 11, _serif, Muted, SKTextAlign.Right);

            // Over par: Power (solid) and Normal (lighter) bars in the hole's difficulty colour.
            var heat = Heat((x.ToPar - lo) / (hi - lo));
            float z = Tp(0);
            foreach (var (v, k, faint) in new[] { (x.PowerToPar, 0, false), (x.NormalToPar, 1, true) })
            {
                float a = Math.Min(z, Tp(v)), e = Math.Max(z, Tp(v)), top = mid - 7 + k * 7.5f;
                using var f = Fill(faint ? heat.WithAlpha(120) : heat);
                canvas.DrawRect(a, top, Math.Max(0.8f, e - a), 6, f);
            }
            using (var axis = Stroke(_ink.WithAlpha(110), 0.6f)) canvas.DrawLine(z, y + 4, z, y + rowH - 4, axis);

            // Birdie / par / bogey-or-worse shares, both players averaged over every pin.
            double birdie = (x.Power.Average(s => s.Birdie) + x.Normal.Average(s => s.Birdie)) / 2;
            double bogey = (x.Power.Average(s => s.BogeyOrWorse) + x.Normal.Average(s => s.BogeyOrWorse)) / 2;
            double parShare = Math.Max(0, 1 - birdie - bogey);
            float sx = splitL, sw = splitR - splitL;
            foreach (var (share, col, label) in new[] { (birdie, Easier, true), (parShare, ParShare, false), (bogey, Harder, true) })
            {
                float wv = (float)share * sw;
                using (var f = Fill(col)) canvas.DrawRect(sx, mid - 6, wv, 12, f);
                if (label && wv > 16) Text(canvas, $"{share * 100:0}", sx + wv / 2, mid + 3, 7.5f, _serif, _salt, SKTextAlign.Center);
                sx += wv;
            }

            // Pin range: easiest to hardest pin, over par.
            var (easy, hard) = x.PinRange;
            using (var line = Stroke(Muted, 1.6f)) canvas.DrawLine(Pn(easy), mid, Pn(hard), mid, line);
            using (var f = Fill(Easier)) canvas.DrawCircle(Pn(easy), mid, 3, f);
            using (var f = Fill(Harder)) canvas.DrawCircle(Pn(hard), mid, 3, f);

            if (x.Doubtful) using (var ring = Stroke(Harder, 1.4f)) canvas.DrawCircle(W(0.898), mid, 4.5f, ring);
            Text(canvas, rank[x.Hole].ToString(), cRank, b, 11, _serif, Muted, SKTextAlign.Right);
            int si = index[x.Hole];
            Text(canvas, si.ToString(), cHcp, b, 12, _bold, si <= 4 ? Harder : _ink, SKTextAlign.Right);
            y += rowH;
            using (var sep = Stroke(_fog.WithAlpha(140), 0.6f)) canvas.DrawLine(r.Left, y, r.Right, y, sep);

            if (halves && i == 8) Totals("OUT", holes.Take(9).ToList());
            if (halves && i == 17) Totals("IN", holes.Skip(9).ToList());
        }
        Totals("TOTAL", holes);

        // Scale labels under the bar columns.
        float sy = y + 13;
        foreach (double v in new[] { scaleLo, 0, scaleHi }.Distinct())
            Text(canvas, Signed(v, "0.0"), Tp(v), sy, 7.5f, _serif, Faint, SKTextAlign.Center);
        Text(canvas, "solid Power Drive, light Normal", toParL, sy + 11, 7.5f, _italic, Faint);
        Text(canvas, "% of play-outs, both players", splitL, sy, 7.5f, _italic, Faint);
        Text(canvas, "pins: easiest to hardest  ·  HCP: suggested handicap, odd numbers on the harder nine", cHcp, sy + 11, 7.5f, _italic, Faint, SKTextAlign.Right);
    }
}
