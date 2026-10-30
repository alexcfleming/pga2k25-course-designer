using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Plan Sheet theme: the Illustrated map in a framed panel, with a title column on the left (name, location,
/// scorecard, golf hole direction dial, credits), red ball-flight arrows and a bar scale. After the hand-drawn course
/// plans of the RGD Artworks Course Graphics Dept. (Renaissance Golf Design), credited on the sheet.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    const float ColumnLeft = 21, ColumnRight = 300, ColumnMid = (ColumnLeft + ColumnRight) / 2;
    public const string PlanSheetStyleCredit = "RGD ARTWORKS COURSE GRAPHICS DEPT.";
    public const string PlanSheetStyleSource = "Renaissance Golf Design, Inc.";

    bool IsPlanSheet => _t.Kind == MapThemeKind.PlanSheet;

    /// <summary>The first place line, which names the watermark's land.</summary>
    string? PlaceName => (string.IsNullOrWhiteSpace(_o.Location) ? _o.PlaceLines : _o.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).FirstOrDefault();

    static SKRect PlanSheetMapRect(SKSize page) => new(ColumnRight + 12, 30, page.Width - 30, page.Height - 38);

    /// <summary>
    /// Display and label faces: Federo and Josefin Sans bundled next to the app (fonts/plansheet) when present,
    /// else installed, else Bahnschrift.
    /// </summary>
    static (SKTypeface Display, SKTypeface Label) PlanSheetFaces()
    {
        string fonts = Path.Combine(AppContext.BaseDirectory, "fonts", "plansheet");
        SKTypeface? Bundled(string file) => File.Exists(Path.Combine(fonts, file)) ? SKTypeface.FromFile(Path.Combine(fonts, file)) : null;
        return (Bundled("Federo-Regular.ttf") ?? SKTypeface.FromFamilyName(Resolve(["Federo", "Bahnschrift", "Georgia"])),
                Weight(Bundled("JosefinSans[wght].ttf"), 600) ?? SKTypeface.FromFamilyName(Resolve(["Josefin Sans", "Bahnschrift", "Georgia"]), SKFontStyle.Bold));
    }

    /// <summary>A variable font set to <paramref name="weight"/>; its default instance (Josefin's is Thin) is too faint for small labels.</summary>
    static SKTypeface? Weight(SKTypeface? variable, float weight)
    {
        if (variable is null) return null;
        const uint Wght = ('w' << 24) | ('g' << 16) | ('h' << 8) | 't';
        if (!variable.VariationDesignParameters.Any(a => a.Tag == Wght)) return variable;
        var set = variable.Clone([new SKFontVariationPositionCoordinate { Axis = Wght, Value = weight }]);
        if (set is null) return variable;
        variable.Dispose();
        return set;
    }

    void DrawPlanSheet(SKCanvas canvas, string courseName, CourseModel course, CourseLayers layers, MapProjection proj, SKSize page, SKRect map)
    {
        DrawBarScale(canvas, layers, proj, map);
        Text(canvas, Attribution.Text, map.Right, page.Height - 26, 6.5f, _body, _t.MutedInk, SKTextAlign.Right);

        // Title column.
        float y = 58;
        Text(canvas, "A PLAN", ColumnMid, y, 18.5f, _display, _t.Ink, SKTextAlign.Center, tracking: 3.3f); y += 20;
        Text(canvas, "OF THE", ColumnMid, y, 13, _display, _t.Ink, SKTextAlign.Center, tracking: 2.6f); y += 12;
        var (lines, size) = NameLines(courseName.ToUpperInvariant(), ColumnRight - ColumnLeft - 34);
        foreach (var line in lines) { y += size * 0.98f; Text(canvas, line, ColumnMid, y, size, _display, _t.Ink, SKTextAlign.Center, tracking: size * 0.02f); }
        y += 4;
        var place = string.IsNullOrWhiteSpace(_o.Location) ? _o.PlaceLines : _o.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in place)
        {
            y += 16;
            Text(canvas, line.ToUpperInvariant(), ColumnMid, y, 11, _display, _t.Ink, SKTextAlign.Center, tracking: 2.4f);
        }
        y = DrawTable(canvas, course, new SKRect(ColumnLeft + 16, y + 18, ColumnRight - 16, y + 18), splitNines: false, rowH: 12, size: 8f);

        DrawDirectionDial(canvas, layers, proj, y + 14, PlanCreditsTop - 14);
        DrawPlanCredits(canvas, page);

        DrawPlanFrame(canvas, page);
    }

    /// <summary>Breaks the name at spaces and hyphens into at most three lines, as large as fits the column.</summary>
    (List<string> Lines, float Size) NameLines(string name, float width)
    {
        var words = name.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        for (float size = 50; size > 18; size -= 1)
        {
            var lines = new List<string>();
            foreach (var w in words)
            {
                if (lines.Count > 0 && Measure(lines[^1] + " " + w, size, _display) <= width) lines[^1] += " " + w;
                else lines.Add(w);
            }
            if (lines.Count <= 3 && lines.All(l => Measure(l, size, _display) * 1.02f <= width)) return (lines, size);
        }
        return ([name], 18);
    }

    /// <summary>Red arrows along each shot: back tee → aim points → green, a small gap before each arrowhead's target.</summary>
    void DrawFlightArrows(SKCanvas canvas, List<SKPoint> route)
    {
        using var line = Stroke(_t.RouteColour, 1.1f);
        using var head = Fill(_t.RouteColour);
        for (int i = 1; i < route.Count; i++)
        {
            SKPoint a = route[i - 1], b = route[i];
            float len = SKPoint.Distance(a, b);
            float gap = i == route.Count - 1 ? 4 : 2;
            if (len < gap + 8) continue;
            var d = new SKPoint((b.X - a.X) / len, (b.Y - a.Y) / len);
            var n = new SKPoint(-d.Y, d.X);
            var tip = new SKPoint(b.X - d.X * gap, b.Y - d.Y * gap);
            var start = i == 1 ? a : new SKPoint(a.X + d.X * 2, a.Y + d.Y * 2);
            const float headLen = 5.5f, headHalf = 2.1f;
            var baseC = new SKPoint(tip.X - d.X * headLen, tip.Y - d.Y * headLen);
            canvas.DrawLine(start, baseC, line);
            using var b2 = new SKPathBuilder();
            b2.MoveTo(tip);
            b2.LineTo(baseC.X + n.X * headHalf, baseC.Y + n.Y * headHalf);
            b2.LineTo(baseC.X - n.X * headHalf, baseC.Y - n.Y * headHalf);
            b2.Close();
            using var path = b2.Detach();
            canvas.DrawPath(path, head);
        }
    }

    /// <summary>
    /// Golf hole direction dial: one ray per hole from the back tee towards its green, in the map's own page
    /// directions, with the four cardinal arrows. A hole label near a cardinal arrow starts past its word; labels of
    /// holes that point the same way step outwards. The dial is sized to fit between <paramref name="top"/> and
    /// <paramref name="bottom"/>, its title included.
    /// </summary>
    void DrawDirectionDial(SKCanvas canvas, CourseLayers layers, MapProjection proj, float top, float bottom)
    {
        const float LabelSize = 7.5f, WordSize = 10, Arrow = 18, Step = 27, TitleGap = 14;
        static double Angle(SKPoint d) => (Math.Atan2(d.X, -d.Y) * 180 / Math.PI + 360) % 360;
        static double Gap(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

        var cardinals = new[] { ("NORTH", MapProjection.North), ("EAST", new Vec2(-1, 0)), ("SOUTH", new Vec2(0, 1)), ("WEST", new Vec2(1, 0)) }
            .Select(c => (Name: c.Item1, Dir: proj.MapDirection(c.Item2)))
            .Select(c => (c.Name, c.Dir, Angle: Angle(c.Dir), Width: Measure(c.Name, WordSize, _display) + 4, Beyond: 0f)).ToList();
        // Word centre beyond the arrow tip: half its width along the arrow when sideways, half its height when upright.
        cardinals = cardinals.Select(c => c with { Beyond = Arrow + 6 + Math.Abs(c.Dir.X) * c.Width / 2 + Math.Abs(c.Dir.Y) * 4 }).ToList();

        var rays = layers.Holes
            .Select(h => (h.Hole.Number, Dir: proj.MapDirection(h.Route[^1] - h.Route[0])))
            .Select(x => (x.Number, x.Dir, Angle: Angle(x.Dir), Text: $"No. {x.Number}", Offset: 0f))
            .OrderBy(x => x.Angle).ToList();
        // Walk round the dial from just after its widest empty gap, so crowds that straddle north stay together.
        int first = rays.Count == 0 ? 0 : Enumerable.Range(0, rays.Count)
            .MaxBy(i => (rays[i].Angle - rays[(i + rays.Count - 1) % rays.Count].Angle + 360) % 360);
        rays = rays.Skip(first).Concat(rays.Take(first)).ToList();
        double prev = double.NegativeInfinity; int lift = 0;
        for (int i = 0; i < rays.Count; i++)
        {
            var x = rays[i];
            lift = (x.Angle - prev + 360) % 360 < 8 ? lift + 1 : 0;
            prev = x.Angle;
            var near = cardinals.Where(c => Gap(c.Angle, x.Angle) < 10).ToList();
            float start = near.Count > 0 ? near.Max(c => c.Beyond + Math.Abs(c.Dir.X) * c.Width / 2) + 4 : 6;
            rays[i] = x with { Offset = start + lift * Step };
        }

        // Page extent above and below the centre is a + r for every label; pick the largest r (≤ 56) that fits.
        var reach = rays.Select(x => (x.Dir, Far: x.Offset + Measure(x.Text, LabelSize, _display), Half: 3f))
            .Concat(cardinals.Select(c => (c.Dir, Far: c.Beyond, Half: 6f))).ToList();
        float Up(float r) => reach.Max(q => -q.Dir.Y * (r + q.Far) + q.Half);
        float Down(float r) => reach.Max(q => q.Dir.Y * (r + q.Far) + q.Half);
        float height = bottom - top - TitleGap;
        float radius = 56;
        while (radius > 26 && Up(radius) + Down(radius) > height) radius -= 1;
        var c0 = new SKPoint(ColumnMid, top + Up(radius) + Math.Max(0, (height - Up(radius) - Down(radius)) / 2));
        float r = radius;

        using var thin = Stroke(_t.Ink, 0.5f);
        using var dashed = Stroke(_t.Ink.WithAlpha(150), 0.4f);
        dashed.PathEffect = SKPathEffect.CreateDash([4.5f, 3.5f], 0);
        canvas.DrawCircle(c0, r * 0.55f, dashed);

        using var shaft = Stroke(_t.Ink, 0.9f);
        using var ink = Fill(_t.Ink);
        foreach (var c in cardinals)
        {
            var d = c.Dir;
            var tip = new SKPoint(c0.X + d.X * (r + Arrow), c0.Y + d.Y * (r + Arrow));
            canvas.DrawLine(c0, new SKPoint(tip.X - d.X * 4, tip.Y - d.Y * 4), shaft);
            using (var b = new SKPathBuilder())
            {
                var n = new SKPoint(-d.Y, d.X);
                b.MoveTo(tip);
                b.LineTo(tip.X - d.X * 6 + n.X * 2.4f, tip.Y - d.Y * 6 + n.Y * 2.4f);
                b.LineTo(tip.X - d.X * 6 - n.X * 2.4f, tip.Y - d.Y * 6 - n.Y * 2.4f);
                b.Close();
                using var path = b.Detach();
                canvas.DrawPath(path, ink);
            }
            Text(canvas, c.Name, c0.X + d.X * (r + c.Beyond), c0.Y + d.Y * (r + c.Beyond) + 3.5f, WordSize, _display, _t.Ink, SKTextAlign.Center, tracking: 1f);
        }

        foreach (var (_, d, _, text, offset) in rays)
        {
            canvas.DrawLine(c0, new SKPoint(c0.X + d.X * r, c0.Y + d.Y * r), thin);
            if (offset > 8)
            {
                using var lead = Stroke(_t.Ink, 0.35f);
                lead.PathEffect = SKPathEffect.CreateDash([1.5f, 1.7f], 0);
                canvas.DrawLine(new SKPoint(c0.X + d.X * r, c0.Y + d.Y * r), new SKPoint(c0.X + d.X * (r + offset - 2), c0.Y + d.Y * (r + offset - 2)), lead);
            }
            var p = new SKPoint(c0.X + d.X * (r + offset), c0.Y + d.Y * (r + offset));
            bool left = d.X < 0;
            float deg = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI) + (left ? 180 : 0);
            canvas.Save();
            canvas.Translate(p.X, p.Y);
            canvas.RotateDegrees(deg);
            Text(canvas, text, 0, 2.6f, LabelSize, _display, _t.Ink, left ? SKTextAlign.Right : SKTextAlign.Left);
            canvas.Restore();
        }
        canvas.DrawCircle(c0, 3, ink);
        Text(canvas, "GOLF HOLE DIRECTION DIAL", ColumnMid, bottom - 2, 8, _display, _t.Ink, SKTextAlign.Center, tracking: 1.3f);
    }

    /// <summary>Top of the credit block, which sits at the foot of the title column.</summary>
    float PlanCreditsTop => string.IsNullOrWhiteSpace(_o.Credit) ? 776 : 746;

    /// <summary>Style credit first, then the designer credit (left out when blank).</summary>
    void DrawPlanCredits(SKCanvas canvas, SKSize page)
    {
        bool credit = !string.IsNullOrWhiteSpace(_o.Credit);
        float y = PlanCreditsTop + 8;
        Text(canvas, "IN THE STYLE OF THE", ColumnMid, y, 8, _label, _t.Ink, SKTextAlign.Center, tracking: 1.7f); y += 14;
        Text(canvas, PlanSheetStyleCredit, ColumnMid, y, 11, _display, _t.Ink, SKTextAlign.Center, tracking: 0.6f); y += 13;
        Text(canvas, PlanSheetStyleSource, ColumnMid, y, 8.5f, _italic, _t.Ink, SKTextAlign.Center);
        if (!credit) return;
        y += 10;
        using (var rule = Stroke(_t.Ink, 0.5f)) canvas.DrawLine(ColumnMid - 60, y, ColumnMid + 60, y, rule);
        y += 14;
        Text(canvas, "DESIGNED BY", ColumnMid, y, 8, _label, _t.Ink, SKTextAlign.Center, tracking: 1.7f); y += 15;
        Text(canvas, _o.Credit.ToUpperInvariant(), ColumnMid, y, 13, _display, _t.Ink, SKTextAlign.Center, tracking: 1f);
    }

    /// <summary>Print ratio of a bar scale: 100 yd (3,600 in) over its page length at 100 units per inch, to the nearest 100.</summary>
    public static int BarScaleRatio(float unitsPer100Yards) => (int)Math.Round(3600 * 100 / unitsPer100Yards / 100) * 100;

    /// <summary>Two-row bar scale in yards, alternate segments hatched, on a cartouche in the map corner with the least of the course under it.</summary>
    void DrawBarScale(SKCanvas canvas, CourseLayers layers, MapProjection p, SKRect map)
    {
        float hundred = (float)(100 * Yard * p.PointsPerMetre);
        int step = hundred > 110 ? 50 : 100;
        float seg = hundred * step / 100;
        int count = Math.Clamp((int)(190 / seg), 2, 4);
        float barW = seg * count;
        float w = barW + 64, h = 48;
        var play = layers.Grass.Concat(layers.Greens).SelectMany(s => s).Concat(layers.Holes.SelectMany(x => x.Route)).Select(p.Map).ToList();
        var box = new[]
        {
            new SKRect(map.Left + 14, map.Bottom - 8 - h, map.Left + 14 + w, map.Bottom - 8),
            new SKRect(map.Right - 14 - w, map.Bottom - 8 - h, map.Right - 14, map.Bottom - 8),
            new SKRect(map.Left + 14, map.Top + 8, map.Left + 14 + w, map.Top + 8 + h),
            new SKRect(map.Right - 14 - w, map.Top + 8, map.Right - 14, map.Top + 8 + h),
        }.MinBy(b => play.Count(q => q.X > b.Left - 10 && q.X < b.Right + 10 && q.Y > b.Top - 10 && q.Y < b.Bottom + 10))!;
        using (var bg = Fill(_t.Background)) canvas.DrawRect(box, bg);
        using (var edge = Stroke(_t.Ink, 0.7f)) canvas.DrawRect(box, edge);
        using (var inner = Stroke(_t.Ink, 0.35f)) canvas.DrawRect(new SKRect(box.Left + 2.5f, box.Top + 2.5f, box.Right - 2.5f, box.Bottom - 2.5f), inner);
        Text(canvas, "BAR SCALE", box.MidX, box.Top + 14, 10, _display, _t.Ink, SKTextAlign.Center, tracking: 1.3f);

        float x0 = box.Left + 14, y0 = box.Top + 25;
        using var outline = Stroke(_t.Ink, 0.5f);
        using var hatch = Stroke(_t.Ink, 0.6f);
        for (int i = 0; i < count; i++)
        {
            var top = new SKRect(x0 + i * seg, y0, x0 + (i + 1) * seg, y0 + 4);
            var bottom = new SKRect(top.Left, y0 + 4, top.Right, y0 + 8);
            var hatched = i % 2 == 0 ? top : bottom;
            canvas.Save();
            canvas.ClipRect(hatched);
            for (float hx = hatched.Left - 8; hx < hatched.Right; hx += 2.2f) canvas.DrawLine(hx, hatched.Bottom, hx + 4, hatched.Top, hatch);
            canvas.Restore();
            canvas.DrawRect(top, outline);
            canvas.DrawRect(bottom, outline);
        }
        using var tick = Stroke(_t.Ink, 0.7f);
        for (int i = 0; i <= count; i++)
        {
            float x = x0 + i * seg;
            canvas.DrawLine(x, y0 - 3, x, y0 + 11, tick);
            Text(canvas, (i * step).ToString(), x, i % 2 == 0 ? y0 - 5 : y0 + 19, 7, _label, _t.Ink, SKTextAlign.Center);
        }
        Text(canvas, "YARDS", x0 + barW + 6, y0 + 7, 7, _label, _t.Ink, tracking: 0.6f);
        Text(canvas, $"1 : {BarScaleRatio(hundred):N0}", box.Right - 10, box.Bottom - 6, 7, _body, _t.Ink, SKTextAlign.Right);
    }

    /// <summary>Double neatline, a double rule beside the title column, and a soft charcoal vignette.</summary>
    void DrawPlanFrame(SKCanvas canvas, SKSize page)
    {
        var rect = new SKRect(0, 0, page.Width, page.Height);
        using (var vignette = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(page.Width / 2, page.Height / 2), page.Width * 0.72f,
                [_t.Ink.WithAlpha(0), _t.Ink.WithAlpha(0), _t.Ink.WithAlpha(70)], [0f, 0.62f, 1f], SKShaderTileMode.Clamp),
        })
            canvas.DrawRect(rect, vignette);
        DrawFrame(canvas, page);
        using var thin = Stroke(_t.Ink, 0.6f);
        using var thick = Stroke(_t.Ink, 1.7f);
        thin.StrokeCap = thick.StrokeCap = SKStrokeCap.Butt;
        canvas.DrawLine(ColumnRight, 21, ColumnRight, page.Height - 21, thin);
        canvas.DrawLine(ColumnRight + 5, 21, ColumnRight + 5, page.Height - 21, thick);
    }
}
