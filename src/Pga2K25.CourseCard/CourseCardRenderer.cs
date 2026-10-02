using SkiaSharp;

namespace Pga2K25.CourseCard;

/// <summary>
/// Draws the compass rose page, the hole data card and the composite course data card. Layouts follow the
/// reference matplotlib build (figure fractions and point sizes carried over); everything is drawn in points
/// on a canvas pre-scaled to the output resolution. Every number comes from <see cref="CourseCardData"/>.
/// </summary>
public sealed class CourseCardRenderer : IDisposable
{
    public static readonly SKSize RosePage = new(13 * 72, 14 * 72);
    public static readonly SKSize TablePage = new(16 * 72, 11.6f * 72);
    public static readonly SKSize CardPage = new(17 * 72, 11.6f * 72);

    static readonly SKColor Muted = SKColor.Parse("#5A5F61"), Faint = SKColor.Parse("#7C8488"),
        Note = SKColor.Parse("#6E7A80"), RingLabel = SKColor.Parse("#8A9296");

    readonly CourseCardData _d;
    readonly CourseCardConfig _c;
    readonly string? _oceanSide;
    readonly SKColor _ink, _salt, _fog;
    readonly Dictionary<int, SKColor> _par;
    readonly SKTypeface _serif, _bold, _italic;
    readonly SKImage? _logo;
    readonly int _roseMax, _barTop;

    public string FontFamily { get; }

    /// <param name="oceanSide">Resolved ocean side ("WEST"…), or null for none.</param>
    public CourseCardRenderer(CourseCardData data, CourseCardConfig config, string? oceanSide)
    {
        _d = data; _c = config; _oceanSide = oceanSide;
        var p = config.Palette;
        _ink = SKColor.Parse(p.Ink); _salt = SKColor.Parse(p.Salt); _fog = SKColor.Parse(p.Fog);
        _par = new() { [3] = SKColor.Parse(p.Par3), [4] = SKColor.Parse(p.Par4), [5] = SKColor.Parse(p.Par5) };
        // Bundled Lora first (fonts/lora next to the app), then an installed Lora, then serif fallbacks.
        string fonts = Path.Combine(AppContext.BaseDirectory, "fonts", "lora");
        var bundled = SKTypeface.FromFile(Path.Combine(fonts, "Lora[wght].ttf"));
        var bundledItalic = SKTypeface.FromFile(Path.Combine(fonts, "Lora-Italic[wght].ttf"));
        if (bundled is not null && bundledItalic is not null)
        {
            FontFamily = "Lora";
            _serif = bundled; _italic = bundledItalic;
        }
        else
        {
            bundled?.Dispose(); bundledItalic?.Dispose();
            FontFamily = new[] { "Lora", "Georgia", "Cambria", "Times New Roman" }
                .FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase))
                ?? SKTypeface.Default.FamilyName;
            _serif = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
            _italic = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Italic);
        }
        // The approved cards set titles, totals and markers in the regular weight (matplotlib never applied Lora bold),
        // so "bold" text uses the regular face; a heavier weight is a style change for Alex to approve.
        _bold = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        if (FontFamily == "Lora") { _bold.Dispose(); _bold = SKTypeface.FromFile(Path.Combine(fonts, "Lora[wght].ttf"))!; }
        if (config.LogoPath is { } logo && File.Exists(logo)) _logo = SKImage.FromEncodedData(logo);

        int longest = data.Holes.Max(h => h.Yards[data.RoseTee]);
        int step = Math.Max(10, config.RingStepYards);
        _roseMax = config.RoseMaxYards ?? (int)Math.Ceiling(longest / (double)step) * step;
        _barTop = (int)Math.Ceiling(longest / 100.0) * 100;
    }

    public void Dispose()
    {
        _serif.Dispose(); _bold.Dispose(); _italic.Dispose(); _logo?.Dispose();
    }

    SKColor ParColour(int par) => _par.TryGetValue(par, out var c) ? c : _ink;

    /// <summary>Text on a par colour: salt, or ink when the swatch is light (relative luminance over 0.55).</summary>
    SKColor OnPar(int par)
    {
        var c = ParColour(par);
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        double lum = 0.2126 * Lin(c.Red) + 0.7152 * Lin(c.Green) + 0.0722 * Lin(c.Blue);
        return lum > 0.55 ? _ink : _salt;
    }

    string Title => (_c.Title ?? _d.CourseName).ToUpperInvariant();
    string RoseTeeName => _d.TeeNames[_d.RoseTee];
    string ParLine => $"Par {_d.Total.Par} ({_d.Out.Par}–{_d.In.Par})";

    /// <summary>Header totals, always computed from the data (never literals).</summary>
    public (string Main, string? Others, string Halves) HeaderTotals()
    {
        int r = _d.RoseTee;
        string main = $"{RoseTeeName.ToUpperInvariant()}  {_d.Total.Yards[r]:N0} yds";
        string? others = _d.TeeNames.Count > 1
            ? string.Join("  ·  ", Enumerable.Range(0, r).Reverse().Select(i => $"{_d.TeeNames[i]} {_d.Total.Yards[i]:N0}"))
            : null;
        return (main, others, $"OUT {_d.Out.Yards[r]:N0}  ·  IN {_d.In.Yards[r]:N0}");
    }

    string? OceanPhrase => _oceanSide is null ? null : $"{Cap(_c.OceanLabel)} to the {_oceanSide}";
    string? WindPhrase => _c.WindFromDegrees is int w
        ? $"prevailing wind {CourseCardConfig.CompassName(w).Replace("-", "")}" + (_c.WindNote is { Length: > 0 } n ? $" ({n})" : "")
        : null;

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    // ───────────────────────────── pages ─────────────────────────────

    public void DrawRosePage(SKCanvas canvas)
    {
        var (w, h) = (RosePage.Width, RosePage.Height);
        canvas.Clear(_salt);
        Text(canvas, Title, 0.04f * w, (1 - 0.965f) * h, 29, _bold, _ink, tracking: 0.32f * 29);
        Text(canvas, $"Compass rose  ·  hole orientation by bearing, scaled to the {RoseTeeName} tees", 0.04f * w, (1 - 0.938f) * h, 12, _italic, Muted);
        Text(canvas, "Bearings in whole degrees, 0–359, clockwise from true North  ·  paths drawn from a common origin as cornered " +
                     "doglegs (tee bearing, then approach bearing)  ·  not a routing plan", 0.04f * w, (1 - 0.915f) * h, 8.8f, _serif, Faint);
        using (var rule = Stroke(_fog, 1.2f)) canvas.DrawLine(0.04f * w, (1 - 0.905f) * h, 0.96f * w, (1 - 0.905f) * h, rule);

        var axes = FigRect(0.04f, 0.035f, 0.92f, 0.855f, RosePage);
        DrawRose(canvas, axes, 1.0f);
        // Legend, upper left of the rose axes.
        float ly = axes.Top + 18;
        foreach (int par in new[] { 3, 4, 5 })
        {
            using var sw = Stroke(ParColour(par), 3);
            canvas.DrawLine(axes.Left + 12, ly - 4, axes.Left + 36, ly - 4, sw);
            Text(canvas, $"Par {par}", axes.Left + 46, ly, 11, _serif, _ink);
            ly += 17;
        }
    }

    public void DrawCoursePage(SKCanvas canvas)
    {
        var (w, h) = (CardPage.Width, CardPage.Height);
        canvas.Clear(_salt);
        float x0 = (_logo is null ? 0.030f : 0.082f) * w;
        Text(canvas, Title, x0, (1 - 0.955f) * h, 30, _bold, _ink, tracking: 0.32f * 30);
        Text(canvas, $"Course data card  ·  orientation and length distribution  ·  {ParLine}", x0, (1 - 0.925f) * h, 12.5f, _italic, Muted);
        Text(canvas, string.Join("  ·  ", new[] { $"{RoseTeeName} tees", "bearings in whole degrees, 0–359, clockwise from true North",
                OceanPhrase, WindPhrase }.Where(s => s is not null)), x0, (1 - 0.902f) * h, 9, _serif, Faint);
        var (main, others, halves) = HeaderTotals();
        Text(canvas, main, 0.970f * w, (1 - 0.955f) * h, 14, _bold, _ink, SKTextAlign.Right);
        if (others is not null)
        {
            Text(canvas, others, 0.970f * w, (1 - 0.925f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
            Text(canvas, halves, 0.970f * w, (1 - 0.902f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
        }
        else Text(canvas, halves, 0.970f * w, (1 - 0.925f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
        using (var rule = Stroke(_fog, 1.3f)) canvas.DrawLine(0.024f * w, (1 - 0.884f) * h, 0.970f * w, (1 - 0.884f) * h, rule);
        if (_logo is not null) DrawLogo(canvas, FigRect(0.024f, 0.898f, 0.046f, 0.0674f, CardPage));

        var rose = FigRect(0.020f, 0.045f, 0.435f, 0.815f, CardPage);
        var (centre, ppy) = DrawRose(canvas, rose, 0.9f);
        Text(canvas, "Paths drawn from a common origin as cornered doglegs (tee bearing, then approach bearing).  Not a routing plan.",
            centre.X, centre.Y + _roseMax * 1.17f * ppy, 8.2f, _italic, Faint, SKTextAlign.Center);

        var seq = FigRect(0.515f, 0.505f, 0.455f, 0.305f, CardPage);
        var order = Enumerable.Range(0, _d.Holes.Count).ToList();
        DrawBars(canvas, seq, order, "Sequential  ·  holes 1–" + _d.Holes.Count, divideAfter: _d.Holes.Count == 18 ? 9 : null);
        // Stable sort: ties keep hole order.
        var sorted = order.OrderByDescending(i => _d.Holes[i].Yards[_d.RoseTee]).ToList();
        DrawBars(canvas, FigRect(0.515f, 0.085f, 0.455f, 0.305f, CardPage), sorted, "Sorted by length  ·  longest to shortest", divideAfter: null);

        // Par legend above the right column.
        float lx = 0.515f * w + 10, lyy = (1 - 0.872f) * h + 16;
        foreach (int par in new[] { 3, 4, 5 })
        {
            using var f = Fill(ParColour(par));
            canvas.DrawRect(lx, lyy - 9, 22, 7, f);
            Text(canvas, $"Par {par}", lx + 28, lyy, 10.5f, _serif, _ink);
            lx += 28 + Measure($"Par {par}", 10.5f, _serif) + 26;
        }
    }

    public void DrawTablePage(SKCanvas canvas)
    {
        float W = TablePage.Width, H = TablePage.Height;
        float In(double inches) => (float)(inches * 72);
        float Y(double inchesFromBottom) => H - In(inchesFromBottom);   // the reference lays this page out in inches, y up
        double h = 11.6;
        canvas.Clear(_salt);

        double xTitle = _logo is null ? 0.5 : 1.95;
        if (_logo is not null) DrawLogo(canvas, new SKRect(In(1.03 - 0.46), Y(h - 0.86 + 0.46), In(1.03 + 0.46), Y(h - 0.86 - 0.46)));
        Text(canvas, Title, In(xTitle), Y(h - 0.62), 30, _bold, _ink, tracking: 0.32f * 30);
        string sub = string.Join("  ·  ", new[] { "Hole data card", _c.Description, ParLine }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Text(canvas, sub, In(xTitle), Y(h - 1.02), 12.5f, _italic, Muted);
        var (main, others, halves) = HeaderTotals();
        Text(canvas, main, In(15.5), Y(h - 0.62), 13.5f, _bold, _ink, SKTextAlign.Right);
        Text(canvas, others ?? halves, In(15.5), Y(h - 1.02), 10.5f, _serif, Muted, SKTextAlign.Right);
        using (var rule = Stroke(_fog, 1.3f)) canvas.DrawLine(In(0.5), Y(h - 1.25), In(15.5), Y(h - 1.25), rule);

        // Columns: tee columns follow the course's tee sets; the two text columns absorb the spare width.
        var cols = new List<(string Name, double W, bool Centre)> { ("Hole", .9, true), ("Par", .9, true) };
        cols.AddRange(_d.TeeNames.Select(n => (n, 1.15, true)));
        cols.Add(("Tee °", 1.0, true)); cols.Add(("Appr °", 1.0, true));
        double fixedW = cols.Sum(c => c.W), spare = 15 - fixedW - 6.6;
        cols.Add(("Hazards", 3.0 + spare * 3.0 / 6.6, false));
        cols.Add(("Green Contours & Hole Positions", 3.6 + spare * 3.6 / 6.6, false));
        double x0 = 0.5, rh = 0.375, totalW = cols.Sum(c => c.W);
        var xs = new List<double>();
        double acc = x0;
        foreach (var c in cols) { xs.Add(acc); acc += c.W; }
        int teeCols = _d.TeeNames.Count, firstTee = 2, teeB = 2 + teeCols, apprB = teeB + 1, hazards = apprB + 1, contours = hazards + 1;

        var rows = new List<(string Kind, CardHole? Hole, string Label, CardTotals? Totals)>();
        foreach (var hole in _d.Holes)
        {
            rows.Add(("hole", hole, "", null));
            if (hole.Number == 9 && _d.Holes.Count == 18) rows.Add(("sum", null, "OUT", _d.Out));
            if (hole.Number == _d.Holes[^1].Number)
            {
                if (_d.Holes.Count == 18) rows.Add(("sum", null, "IN", _d.In));
                rows.Add(("sum", null, "TOTAL", _d.Total));
            }
        }

        double ytop = h - 1.75;
        using (var bar = Fill(_ink)) canvas.DrawRect(new SKRect(In(x0), Y(ytop), In(x0 + totalW), Y(ytop - rh)), bar);
        for (int i = 0; i < cols.Count; i++)
        {
            var (name, cw, centre) = cols[i];
            float tx = In(cols[i].Centre ? xs[i] + cw / 2 : xs[i] + .14);
            Text(canvas, name, tx, Y(ytop - rh * .66), 9.8f, _bold, _salt, centre ? SKTextAlign.Center : SKTextAlign.Left);
        }

        double y = ytop - rh;
        using var rowRule = Stroke(_fog, .45f);
        for (int r = 0; r < rows.Count; r++)
        {
            y -= rh;
            var (kind, hole, label, totals) = rows[r];
            var band = new SKRect(In(x0), Y(y + rh), In(x0 + totalW), Y(y));
            string?[] vals = new string?[cols.Count];
            bool sum = kind == "sum";
            if (sum)
            {
                using (var f = Fill(_fog.WithAlpha((byte)(.55 * 255)))) canvas.DrawRect(band, f);
                vals[0] = label; vals[1] = totals!.Par.ToString();
                for (int t = 0; t < teeCols; t++) vals[firstTee + t] = totals.Yards[t].ToString("N0");
            }
            else
            {
                if (r % 2 == 1) using (var f = Fill(_fog.WithAlpha((byte)(.16 * 255)))) canvas.DrawRect(band, f);
                vals[0] = hole!.Number.ToString(); vals[1] = hole.Par.ToString();
                for (int t = 0; t < teeCols; t++) vals[firstTee + t] = hole.Yards[t].ToString("N0");
                vals[teeB] = hole.TeeBearing.ToString(); vals[apprB] = hole.ApproachBearing.ToString();
                vals[hazards] = string.Join(" · ", hole.Hazards);
                vals[contours] = hole.GreenNotes;
            }
            for (int i = 0; i < cols.Count; i++)
            {
                if (string.IsNullOrEmpty(vals[i])) continue;
                var (_, cw, centre) = cols[i];
                if (i == 1 && !sum)
                {
                    float cx = In(xs[i] + cw / 2), cy = Y(y + rh / 2);
                    using var pill = Fill(ParColour(hole!.Par));
                    canvas.DrawRoundRect(new SKRect(cx - In(.26), cy - In(.125), cx + In(.26), cy + In(.125)), In(.05), In(.05), pill);
                    Text(canvas, vals[i]!, cx, cy + 3.8f, 10, _bold, OnPar(hole.Par), SKTextAlign.Center);
                    continue;
                }
                float tx = In(centre ? xs[i] + cw / 2 : xs[i] + .14);
                Text(canvas, vals[i]!, tx, Y(y + rh * .32), i < hazards ? 10.2f : 9.2f, sum || i == 0 ? _bold : _serif, _ink,
                    centre ? SKTextAlign.Center : SKTextAlign.Left);
            }
            canvas.DrawLine(In(x0), Y(y), In(x0 + totalW), Y(y), rowRule);
        }
        using (var end = Stroke(_ink, 1.1f)) canvas.DrawLine(In(x0), Y(y), In(x0 + totalW), Y(y), end);

        Text(canvas, "Bearings in whole degrees, 0–359, clockwise from true North, measured from the course file's routing: tee to " +
                     "first aim point, last aim point to green; par 3s tee to green.", In(.5), Y(y - .36), 9, _serif, Note);
        string second = string.Join("  ·  ", new[] { OceanPhrase is null ? null : $"{OceanPhrase}", WindPhrase,
            "tee columns in in-game order, shortest to longest", "yardages are the game's scorecard lengths" }.Where(s => s is not null));
        Text(canvas, second, In(.5), Y(y - .64), 9, _serif, Note);
    }

    // ───────────────────────────── rose ─────────────────────────────

    static (double X, double Y) Vec(double deg)
    {
        double a = deg * Math.PI / 180;
        return (Math.Sin(a), Math.Cos(a));   // x east, y north
    }

    /// <summary>Draws the rose centred in <paramref name="axes"/>; returns its centre and points per yard.</summary>
    (SKPoint Centre, float PointsPerYard) DrawRose(SKCanvas canvas, SKRect axes, float k)
    {
        float R = _roseMax;
        float L = R * 1.19f;
        float ppy = Math.Min(axes.Width, axes.Height) / (2 * L);
        var c = new SKPoint(axes.MidX, axes.MidY);
        SKPoint P(double deg, double yards) { var v = Vec(deg); return new SKPoint(c.X + (float)(v.X * yards * ppy), c.Y - (float)(v.Y * yards * ppy)); }

        using (var ring = Stroke(_fog, 0.8f))
            for (int r = _c.RingStepYards; r <= R; r += _c.RingStepYards) canvas.DrawCircle(c, r * ppy, ring);
        using (var spoke = Stroke(_fog, 0.7f))
            for (int d = 0; d < 360; d += _c.SpokeStepDegrees)
            {
                canvas.DrawLine(c, P(d, R), spoke);
                var lp = P(d, R * 1.075);
                string lab = d switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => $"{d}°" };
                bool card = d % 90 == 0;
                float size = (card ? 12 : 9.5f) * k;
                Text(canvas, lab, lp.X, lp.Y + size * 0.36f, size, card ? _bold : _serif, card ? _ink : Faint, SKTextAlign.Center);
            }
        // Ring labels at 345°, boxed so spokes don't strike through the numbers.
        for (int r = _c.RingStepYards; r <= R; r += _c.RingStepYards)
        {
            var rp = P(345, r);
            float size = 8.5f * k, tw = Measure($"{r}", size, _serif);
            using var box = Fill(_salt.WithAlpha(230));
            canvas.DrawRoundRect(new SKRect(rp.X - tw / 2 - 2, rp.Y - size * 0.62f, rp.X + tw / 2 + 2, rp.Y + size * 0.55f), 2, 2, box);
            Text(canvas, $"{r}", rp.X, rp.Y + size * 0.36f, size, _serif, RingLabel, SKTextAlign.Center);
        }

        // Ocean label on its margin, set along the rim and kept upright.
        if (_oceanSide is not null && OceanBearing(_oceanSide) is double ob)
        {
            var at = P(ob, R * 1.155);
            // Tangent to the rim, always upright: west reads bottom → top, east top → bottom.
            float rot = (float)(ob <= 90 ? ob : ob < 270 ? ob - 180 : ob - 360);
            canvas.Save();
            canvas.RotateDegrees(rot, at.X, at.Y);
            float size = 10.5f * k;
            Text(canvas, Spaced(_c.OceanLabel.ToUpperInvariant()), at.X, at.Y + size * 0.36f, size, _italic, Note, SKTextAlign.Center);
            canvas.Restore();
        }

        // Prevailing wind: from outside the rose on the FROM bearing, pointing inward.
        if (_c.WindFromDegrees is int wf)
        {
            var a0 = P(wf, R * 1.045);
            var a1 = P(wf, R * 0.80);
            DrawArrow(canvas, a0, a1, Note, 1.6f, 9);
            var lp = P(wf, R * 1.045 * 1.045);
            Text(canvas, "prevailing", lp.X, lp.Y - 1, 9 * k, _italic, Note, SKTextAlign.Center);
            Text(canvas, $"wind  {CourseCardConfig.CompassAbbreviation(wf)}", lp.X, lp.Y + 10 * k, 9 * k, _italic, Note, SKTextAlign.Center);
        }

        // Hole paths: common origin; tee leg then approach leg; total path length = rose-tee yards.
        foreach (var h in _d.Holes)
        {
            float yards = h.Yards[_d.RoseTee];
            var pts = new List<SKPoint> { c };
            if (h.TeeBearing == h.ApproachBearing) pts.Add(P(h.TeeBearing, yards));
            else
            {
                var v1 = Vec(h.TeeBearing); var v2 = Vec(h.ApproachBearing);
                double f = h.CornerFraction;
                var corner = (v1.X * yards * f, v1.Y * yards * f);
                pts.Add(new SKPoint(c.X + (float)(corner.Item1 * ppy), c.Y - (float)(corner.Item2 * ppy)));
                pts.Add(new SKPoint(c.X + (float)((corner.Item1 + v2.X * yards * (1 - f)) * ppy), c.Y - (float)((corner.Item2 + v2.Y * yards * (1 - f)) * ppy)));
            }
            using var line = Stroke(ParColour(h.Par), 2.1f * k);
            line.StrokeCap = SKStrokeCap.Round;
            using var b = new SKPathBuilder();
            b.MoveTo(pts[0]);
            foreach (var pt in pts.Skip(1)) b.LineTo(pt);
            using var path = b.Detach();
            canvas.DrawPath(path, line);
        }
        foreach (var h in _d.Holes)
        {
            var end = EndPoint(h, c, ppy);
            float r = 7.16f * k;
            using (var f = Fill(ParColour(h.Par))) canvas.DrawCircle(end, r, f);
            using (var e = Stroke(_salt, 1.4f)) canvas.DrawCircle(end, r, e);
            float size = 9.5f * k;
            Text(canvas, h.Number.ToString(), end.X, end.Y + size * 0.36f, size, _bold, OnPar(h.Par), SKTextAlign.Center);
        }
        using (var dot = Fill(_ink)) canvas.DrawCircle(c, 4.2f * (float)Math.Sqrt(k), dot);
        return (c, ppy);
    }

    SKPoint EndPoint(CardHole h, SKPoint c, float ppy)
    {
        double yards = h.Yards[_d.RoseTee], f = h.TeeBearing == h.ApproachBearing ? 1 : h.CornerFraction;
        var v1 = Vec(h.TeeBearing); var v2 = Vec(h.ApproachBearing);
        double x = v1.X * yards * f + v2.X * yards * (1 - f), y = v1.Y * yards * f + v2.Y * yards * (1 - f);
        return new SKPoint(c.X + (float)(x * ppy), c.Y - (float)(y * ppy));
    }

    static double? OceanBearing(string side) => side.ToUpperInvariant() switch
    {
        "NORTH" => 0, "NORTH-EAST" => 45, "EAST" => 90, "SOUTH-EAST" => 135,
        "SOUTH" => 180, "SOUTH-WEST" => 225, "WEST" => 270, "NORTH-WEST" => 315, _ => null,
    };

    static string Spaced(string s) => string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace(" ", "  ");

    // ───────────────────────────── bars ─────────────────────────────

    void DrawBars(SKCanvas canvas, SKRect r, List<int> order, string title, int? divideAfter)
    {
        int n = order.Count;
        float yMax = _barTop + 20;
        float X(double i) => r.Left + (float)((i + 0.8) / (n + 0.6)) * r.Width;
        float Yv(double v) => r.Bottom - (float)(v / yMax) * r.Height;

        using (var bg = Fill(SKColors.White)) canvas.DrawRect(r, bg);
        using (var grid = Stroke(_fog.WithAlpha((byte)(.65 * 255)), .6f))
            for (int v = 0; v <= _barTop; v += 100)
            {
                canvas.DrawLine(r.Left, Yv(v), r.Right, Yv(v), grid);
                Text(canvas, v.ToString(), r.Left - 5, Yv(v) + 2.8f, 8, _serif, Faint, SKTextAlign.Right);
            }
        float slot = r.Width / (n + 0.6f), bw = slot * 0.68f;
        for (int k = 0; k < n; k++)
        {
            var h = _d.Holes[order[k]];
            int v = h.Yards[_d.RoseTee];
            using (var f = Fill(ParColour(h.Par))) canvas.DrawRect(new SKRect(X(k) - bw / 2, Yv(v), X(k) + bw / 2, r.Bottom), f);
            Text(canvas, v.ToString(), X(k), Yv(v + 12), 7.2f, _serif, Muted, SKTextAlign.Center);
            Text(canvas, h.Number.ToString(), X(k), r.Bottom + 13, 9, _serif, _ink, SKTextAlign.Center);
        }
        using (var axis = Stroke(_fog, 1)) canvas.DrawLine(r.Left, r.Bottom, r.Right, r.Bottom, axis);
        Text(canvas, title, r.Left, r.Top - 9, 12.5f, _bold, _ink);
        Text(canvas, "hole", r.MidX, r.Bottom + 25, 8.6f, _serif, Faint, SKTextAlign.Center);

        if (divideAfter is int d)
        {
            float x = X(d - 0.5);
            using var dash = SKPathEffect.CreateDash([4 * .9f, 3 * .9f], 0);
            using var line = Stroke(_ink, .9f);
            line.PathEffect = dash;
            canvas.DrawLine(x, r.Top, x, r.Bottom, line);
            Text(canvas, "OUT", x - 4, Yv(_barTop - 10), 8.2f, _italic, _ink, SKTextAlign.Right);
            Text(canvas, "IN", x + 4, Yv(_barTop - 10), 8.2f, _italic, _ink);
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    void DrawLogo(SKCanvas canvas, SKRect box)
    {
        if (_logo is null) return;
        float s = Math.Min(box.Width, box.Height) / Math.Max(_logo.Width, _logo.Height);
        var dest = SKRect.Create(box.MidX - _logo.Width * s / 2, box.MidY - _logo.Height * s / 2, _logo.Width * s, _logo.Height * s);
        canvas.DrawImage(_logo, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    static void DrawArrow(SKCanvas canvas, SKPoint from, SKPoint to, SKColor c, float width, float head)
    {
        var d = new SKPoint(to.X - from.X, to.Y - from.Y);
        float len = d.Length;
        if (len < 1e-3f) return;
        var u = new SKPoint(d.X / len, d.Y / len);
        var n = new SKPoint(-u.Y, u.X);
        var baseOfHead = new SKPoint(to.X - u.X * head, to.Y - u.Y * head);
        using var line = Stroke(c, width);
        canvas.DrawLine(from, baseOfHead, line);
        using var b = new SKPathBuilder();
        b.MoveTo(to);
        b.LineTo(baseOfHead.X + n.X * head * 0.4f, baseOfHead.Y + n.Y * head * 0.4f);
        b.LineTo(baseOfHead.X - n.X * head * 0.4f, baseOfHead.Y - n.Y * head * 0.4f);
        b.Close();
        using var path = b.Detach();
        using var f = Fill(c);
        canvas.DrawPath(path, f);
    }

    /// <summary>Figure fractions (left, bottom, width, height; matplotlib style) → page rectangle in points.</summary>
    static SKRect FigRect(float l, float b, float w, float h, SKSize page) =>
        new(l * page.Width, (1 - b - h) * page.Height, (l + w) * page.Width, (1 - b) * page.Height);

    static SKPaint Stroke(SKColor c, float width) =>
        new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round };

    static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };

    static float Measure(string text, float size, SKTypeface face)
    {
        using var font = new SKFont(face, size);
        return font.MeasureText(text);
    }

    /// <summary>Text at a baseline; <paramref name="tracking"/> (points) letter-spaces the glyphs (real tracking, not spaces).</summary>
    static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor color,
        SKTextAlign align = SKTextAlign.Left, float tracking = 0)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (tracking > 0)
        {
            var widths = text.Select(ch => font.MeasureText(ch.ToString())).ToArray();
            float total = widths.Sum() + tracking * (text.Length - 1);
            float cx = align switch { SKTextAlign.Right => x - total, SKTextAlign.Center => x - total / 2, _ => x };
            for (int i = 0; i < text.Length; i++)
            {
                canvas.DrawText(text[i].ToString(), cx, y, SKTextAlign.Left, font, paint);
                cx += widths[i] + tracking;
            }
            return;
        }
        canvas.DrawText(text, x, y, align, font, paint);
    }
}
