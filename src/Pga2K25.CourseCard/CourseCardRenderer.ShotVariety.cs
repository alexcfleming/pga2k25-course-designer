using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed partial class CourseCardRenderer
{
    /// <summary>Each golfer's round for the shot variety card (Power first), or empty for no card.</summary>
    public IReadOnlyList<VarietyRound> Variety { get; init; } = [];

    static readonly SKColor PassColour = SKColor.Parse("#3F6B4A"), FlagColour = SKColor.Parse("#B4471F");
    static readonly SKColor[] FamilyBand = [SKColor.Parse("#E6E3DB"), SKColor.Parse("#EDEBE5")];
    static readonly Dictionary<string, SKColor> FamilyColour = new()
    {
        ["Woods"] = SKColor.Parse("#4E5A5E"), ["Long irons"] = SKColor.Parse("#7C8A8F"), ["Mid irons"] = SKColor.Parse("#A7B3B7"),
        ["Short irons"] = SKColor.Parse("#C9B98F"), ["Wedges"] = SKColor.Parse("#C08A2E"),
    };

    /// <summary>
    /// Shot variety card: the club each hole asks for into the green, for a Power Drive golfer from the back tees and a
    /// Normal golfer one tee forward, with the families those clubs fall in, the approach yards on one line, and the
    /// variety checks. The golfers' club distances are placeholders for a typical player.
    /// </summary>
    public void DrawShotVarietyPage(SKCanvas canvas)
    {
        if (Variety.Count < 2) return;
        var (w, h) = (CardPage.Width, CardPage.Height);
        var (power, normal) = (Variety[0], Variety[1]);
        canvas.Clear(_salt);
        float x0 = (_logo is null ? 0.030f : 0.082f) * w;
        Text(canvas, Title, x0, (1 - 0.955f) * h, 30, _bold, _ink, tracking: 0.32f * 30);
        Text(canvas, "Shot variety card  ·  the club each hole asks for into the green, for two golfers", x0, (1 - 0.925f) * h, 12.5f, _italic, Muted);
        Text(canvas, $"{power.Golfer.Name} from the {power.TeeName} tees  ·  {normal.Golfer.Name} from the {normal.TeeName} tees  ·  " +
                     "club distances stand for a typical player of each kind", x0, (1 - 0.902f) * h, 9, _serif, Faint);
        int passP = power.Checks.Count(c => c.Passes == true), passN = normal.Checks.Count(c => c.Passes == true), of = power.Checks.Count(c => c.Passes is not null);
        Text(canvas, $"VARIETY  {passP} / {of}  ·  {passN} / {of}", 0.970f * w, (1 - 0.955f) * h, 14, _bold, _ink, SKTextAlign.Right);
        Text(canvas, $"checks passed, {power.Golfer.Short} · {normal.Golfer.Short}", 0.970f * w, (1 - 0.925f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
        using (var rule = Stroke(_fog, 1.3f)) canvas.DrawLine(0.024f * w, (1 - 0.884f) * h, 0.970f * w, (1 - 0.884f) * h, rule);
        if (_logo is not null) DrawLogo(canvas, FigRect(0.024f, 0.898f, 0.046f, 0.0674f, CardPage));

        DrawVarietyLegend(canvas, 0.030f * w, (1 - 0.872f) * h + 14, power, normal);
        DrawClubGrid(canvas, FigRect(0.030f, 0.055f, 0.590f, 0.775f, CardPage), power, normal);
        float rx = 0.660f * w, rw = 0.310f * w;
        DrawFamilyBars(canvas, new SKRect(rx, (1 - 0.830f) * h, rx + rw, (1 - 0.690f) * h), power, normal);
        DrawYardStrip(canvas, new SKRect(rx, (1 - 0.650f) * h, rx + rw, (1 - 0.440f) * h), power, normal);
        DrawVarietyChecks(canvas, new SKRect(rx, (1 - 0.400f) * h, rx + rw, (1 - 0.060f) * h), power, normal);
    }

    void DrawVarietyLegend(SKCanvas canvas, float x, float y, VarietyRound power, VarietyRound normal)
    {
        void Item(Action<float, float> mark, string label)
        {
            mark(x + 6, y - 4);
            Text(canvas, label, x + 16, y, 10.5f, _serif, _ink);
            x += 16 + Measure(label, 10.5f, _serif) + 24;
        }
        Item((cx, cy) => Mark(canvas, cx, cy, 0, true), $"{power.Golfer.Name} ({power.Golfer.CarryYards} yd carry)");
        Item((cx, cy) => Mark(canvas, cx, cy, 0, false), $"{normal.Golfer.Name} ({normal.Golfer.CarryYards} yd carry)");
        Item((cx, cy) => Mark(canvas, cx, cy, 3, false), "par 3 tee shot");
        Item((cx, cy) => Mark(canvas, cx, cy, 5, false), "wedge in after a par 5 lay-up");
    }

    /// <summary>A shot on the grid: a circle, a square on par 3s, a diamond after a par 5 lay-up; filled for the first golfer.</summary>
    void Mark(SKCanvas canvas, float x, float y, int kind, bool filled)
    {
        using var fill = Fill(filled ? _ink : _salt);
        using var edge = Stroke(_ink, 1.5f);
        float r = 4.6f;
        if (kind == 3) { var sq = new SKRect(x - r, y - r, x + r, y + r); canvas.DrawRect(sq, fill); canvas.DrawRect(sq, edge); return; }
        if (kind == 5)
        {
            using var b = new SKPathBuilder();
            b.MoveTo(x, y - r - 1); b.LineTo(x + r + 1, y); b.LineTo(x, y + r + 1); b.LineTo(x - r - 1, y); b.Close();
            using var d = b.Detach();
            canvas.DrawPath(d, fill); canvas.DrawPath(d, edge);
            return;
        }
        canvas.DrawCircle(x, y, r, fill);
        if (!filled) canvas.DrawCircle(x, y, r, edge);
    }

    static int Kind(HoleShot s) => s.Par == 3 ? 3 : s.Layup is not null ? 5 : 0;

    void DrawClubGrid(SKCanvas canvas, SKRect r, VarietyRound power, VarietyRound normal)
    {
        var rows = VarietyGolfer.Clubs.Append(VarietyGolfer.PartWedge).ToList();
        int n = power.Holes.Count;
        float labelW = 70, left = r.Left + labelW, colW = (r.Right - left) / n;
        float headH = 40, footH = 92, rowH = (r.Height - headH - footH) / rows.Count, top = r.Top + headH;
        float X(int i) => left + (i + 0.5f) * colW;
        float Yc(string club) => top + (rows.IndexOf(club) + 0.5f) * rowH;

        Text(canvas, "CLUB INTO THE GREEN, HOLE BY HOLE", r.Left, r.Top - 4, 8, _serif, Muted, tracking: 0.8f);
        // Family bands, labelled down the left.
        int at = 0, band = 0;
        foreach (var f in VarietyGolfer.Families)
        {
            int k = rows.Count(c => VarietyGolfer.Family(c) == f);
            float y = top + at * rowH;
            using (var b = Fill(FamilyBand[band++ % 2])) canvas.DrawRect(left, y, colW * n, k * rowH, b);
            canvas.Save();
            canvas.RotateDegrees(-90, r.Left + 8, y + k * rowH / 2);
            Text(canvas, f.ToUpperInvariant(), r.Left + 8, y + k * rowH / 2 + 3, 6.5f, _serif, Faint, SKTextAlign.Center, tracking: 0.6f);
            canvas.Restore();
            using (var line = Stroke(_fog, 0.8f)) canvas.DrawLine(r.Left + 16, y, left + colW * n, y, line);
            at += k;
        }
        using (var line = Stroke(_fog, 0.8f)) canvas.DrawLine(r.Left + 16, top + rows.Count * rowH, left + colW * n, top + rows.Count * rowH, line);
        foreach (var c in rows)
            Text(canvas, c == VarietyGolfer.PartWedge ? "½ wedge" : c, left - 6, Yc(c) + 3.5f, 9.5f, _serif, Muted, SKTextAlign.Right);

        // Hole chips in par colours, the back-tee yards under each, and the column lines (heavier at the turn).
        for (int i = 0; i < n; i++)
        {
            var hs = power.Holes[i];
            using (var chip = Fill(ParColour(hs.Par))) canvas.DrawCircle(X(i), r.Top + 12, 9, chip);
            Text(canvas, hs.Hole.ToString(), X(i), r.Top + 15.5f, 9.5f, _bold, OnPar(hs.Par), SKTextAlign.Center);
            Text(canvas, hs.Yards.ToString(), X(i), r.Top + 33, 7.5f, _serif, Faint, SKTextAlign.Center);
            if (i > 0)
                using (var col = Stroke(_fog, i == 9 && n == 18 ? 1.4f : 0.5f))
                    canvas.DrawLine(left + i * colW, top, left + i * colW, top + rows.Count * rowH, col);
        }
        for (int i = 0; i < n; i++)
        {
            float dx = Math.Min(7, colW * 0.18f);
            Mark(canvas, X(i) - dx, Yc(power.Holes[i].Into), Kind(power.Holes[i]), true);
            Mark(canvas, X(i) + dx, Yc(normal.Holes[i].Into), Kind(normal.Holes[i]), false);
        }

        // Off the tee (when not driver) and the yards into each green, Power above Normal.
        float y0 = top + rows.Count * rowH + 18;
        Text(canvas, "Off the tee", left - 6, y0, 9, _italic, Muted, SKTextAlign.Right);
        Text(canvas, "Into green, yds", left - 6, y0 + 42, 9, _italic, Muted, SKTextAlign.Right);
        for (int i = 0; i < n; i++)
        {
            var (p, q) = (power.Holes[i], normal.Holes[i]);
            // The club in words: driver is "Dr", quieter than a hole where it isn't the play. A par 3's tee shot is on
            // the grid above, so its cell is empty.
            if (p.Par != 3)
            {
                Text(canvas, p.Tee, X(i), y0, 8.5f, _bold, p.Tee == "Dr" ? Faint : _ink, SKTextAlign.Center);
                Text(canvas, q.Tee, X(i), y0 + 12, 8.5f, _serif, q.Tee == "Dr" ? Faint : Muted, SKTextAlign.Center);
            }
            Text(canvas, p.Approach.ToString(), X(i), y0 + 42, 8.5f, _bold, _ink, SKTextAlign.Center);
            Text(canvas, q.Approach.ToString(), X(i), y0 + 54, 8.5f, _serif, Muted, SKTextAlign.Center);
        }
        Text(canvas, "Both rows, upper line: " + power.Golfer.Short + "  ·  lower, grey: " + normal.Golfer.Short + "  ·  Dr: driver  ·  par 3 tee shots are on the grid  ·  yards played-like for height (1 yd per 3 ft)",
            left, y0 + 72, 8, _italic, Faint);
    }

    void DrawFamilyBars(SKCanvas canvas, SKRect r, VarietyRound power, VarietyRound normal)
    {
        Text(canvas, "CLUB FAMILIES INTO GREENS", r.Left, r.Top, 8, _serif, Muted, tracking: 0.8f);
        float y = r.Top + 12, barH = 20, lab = 56;
        foreach (var round in new[] { power, normal })
        {
            Text(canvas, round.Golfer.Short, r.Left, y + 14, 10.5f, _bold, _ink);
            float x = r.Left + lab, total = round.Holes.Count, bw = r.Width - lab;
            foreach (var (fam, count) in round.FamilyCounts)
            {
                if (count == 0) continue;
                float ww = bw * count / total;
                using (var f = Fill(FamilyColour[fam])) canvas.DrawRect(x, y, ww - 1.5f, barH, f);
                if (ww > 16) Text(canvas, count.ToString(), x + ww / 2, y + 14, 9.5f, _bold, fam is "Woods" or "Long irons" ? _salt : _ink, SKTextAlign.Center);
                x += ww;
            }
            y += barH + 10;
        }
        float kx = r.Left + lab;
        foreach (var fam in VarietyGolfer.Families)
        {
            using (var f = Fill(FamilyColour[fam])) canvas.DrawRect(kx, y + 2, 8, 8, f);
            Text(canvas, fam, kx + 11, y + 9.5f, 8.5f, _serif, Muted);
            kx += 11 + Measure(fam, 8.5f, _serif) + 12;
        }
    }

    void DrawYardStrip(SKCanvas canvas, SKRect r, VarietyRound power, VarietyRound normal)
    {
        Text(canvas, "YARDS INTO THE GREEN", r.Left, r.Top, 8, _serif, Muted, tracking: 0.8f);
        float lab = 56, x0 = r.Left + lab, x1 = r.Right;
        const float d0 = 25, d1 = 275;
        float XD(double d) => x0 + (float)((Math.Clamp(d, d0, d1) - d0) / (d1 - d0)) * (x1 - x0);
        float y = r.Top + 36;
        bool first = true;
        foreach (var round in new[] { power, normal })
        {
            Text(canvas, round.Golfer.Short, r.Left, y + 4, 10.5f, _bold, _ink);
            var (from, count) = round.Cluster;
            using (var shade = Fill(count > ShotVariety.MaxInCluster ? FlagColour.WithAlpha(45) : PassColour.WithAlpha(35)))
                canvas.DrawRect(XD(from), y - 18, XD(from + ShotVariety.ClusterYards) - XD(from), 36, shade);
            using (var axis = Stroke(_fog, 1)) canvas.DrawLine(x0, y, x1, y, axis);
            // Dots that would overlap stack above and below the line.
            var placed = new List<(float X, int Level)>();
            foreach (var s in round.Holes.OrderBy(s => s.Approach))
            {
                float x = XD(s.Approach);
                int level = 0;
                while (placed.Any(p => Math.Abs(p.X - x) < 8 && p.Level == level)) level = level <= 0 ? -level + 1 : -level;
                placed.Add((x, level));
                Mark(canvas, x, y - level * 9, Kind(s), first);
            }
            y += 56;
            first = false;
        }
        y -= 30;
        for (int d = 50; d <= 250; d += 50)
        {
            using (var tick = Stroke(Faint, 0.8f)) canvas.DrawLine(XD(d), y - 4, XD(d), y, tick);
            Text(canvas, d.ToString(), XD(d), y + 10, 8.5f, _serif, Muted, SKTextAlign.Center);
        }
        Text(canvas, "yds", x1, y + 10, 8.5f, _italic, Faint, SKTextAlign.Right);
        Text(canvas, $"Shaded: the tightest {ShotVariety.ClusterYards}-yd cluster, red when more than {ShotVariety.MaxInCluster} holes share it.",
            x0, y + 24, 8, _italic, Faint);
    }

    void DrawVarietyChecks(SKCanvas canvas, SKRect r, VarietyRound power, VarietyRound normal)
    {
        float c1 = r.Left + 0.38f * r.Width, c2 = r.Left + 0.70f * r.Width;
        Text(canvas, "VARIETY CHECKS", r.Left, r.Top, 8, _serif, Muted, tracking: 0.8f);
        Text(canvas, power.Golfer.Short.ToUpperInvariant(), c1, r.Top, 8, _serif, Muted, tracking: 0.8f);
        Text(canvas, normal.Golfer.Short.ToUpperInvariant(), c2, r.Top, 8, _serif, Muted, tracking: 0.8f);
        float rowH = Math.Min(24, (r.Height - 40) / power.Checks.Count), y = r.Top + 20;
        for (int i = 0; i < power.Checks.Count; i++)
        {
            Text(canvas, power.Checks[i].Name, r.Left, y, 10, _bold, _ink);
            foreach (var (check, x) in new[] { (power.Checks[i], c1), (normal.Checks[i], c2) })
            {
                float tx = x;
                if (check.Passes is bool ok)
                {
                    using (var dot = Fill(ok ? PassColour : FlagColour)) canvas.DrawCircle(x + 4.5f, y - 3.5f, 4.5f, dot);
                    using var mark = Stroke(_salt, 1.3f);
                    if (ok) { canvas.DrawLine(x + 2.4f, y - 3.6f, x + 4.0f, y - 2.0f, mark); canvas.DrawLine(x + 4.0f, y - 2.0f, x + 6.8f, y - 5.4f, mark); }
                    else { canvas.DrawLine(x + 4.5f, y - 6.2f, x + 4.5f, y - 3.0f, mark); canvas.DrawCircle(x + 4.5f, y - 1.4f, 0.5f, mark); }
                    tx += 13;
                }
                var (fit, size) = FitLine(check.Figure, 9.5f, 7f, (c2 - c1) - 18, _serif);
                Text(canvas, fit, tx, y, size, _serif, check.Passes == false ? FlagColour : _ink);
            }
            using (var line = Stroke(_fog, 0.6f)) canvas.DrawLine(r.Left, y + 7, r.Right, y + 7, line);
            y += rowH;
        }
        y += 8;
        Text(canvas, "Every hole's shot into the green counts, the wedge after a par 5 lay-up included. A first guess at the limits:",
            r.Left, y, 8, _italic, Faint);
        Text(canvas, $"{ShotVariety.MinClubs}+ clubs, none on more than {ShotVariety.MaxSameClub} holes, at most {ShotVariety.MaxInCluster} within {ShotVariety.ClusterYards} yds, " +
                     $"par 3s on different clubs {ShotVariety.MinPar3SpreadYards}+ yds apart.", r.Left, y + 11, 8, _italic, Faint);
    }
}
