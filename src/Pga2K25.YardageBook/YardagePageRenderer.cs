using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.YardageBook;

/// <summary>
/// One tall yardage-book page per hole, after the StrackaLine layout: the hole plays up the page,
/// grey distance arcs from the green (right-hand labels) and from the back tee (left-hand labels),
/// fairway bunker carries as tee-yards over green-yards, circled fairway widths, spot elevations
/// relative to the green centre, and a tee-set footer.
/// </summary>
public sealed class YardagePageRenderer : IDisposable
{
    public const float PageWidth = 425, PageHeight = 1100;   // 4.25 × 11 in at 100 units/in
    public static readonly SKRect MapRect = new(14, 56, PageWidth - 14, PageHeight - 104);
    const double Yard = HoleAnalysis.Yard;

    static readonly SKColor Paper = SKColor.Parse("#ffffff");
    static readonly SKColor Ground = SKColor.Parse("#f5f2e9");
    static readonly SKColor RoughFill = SKColor.Parse("#e3eed8");
    static readonly SKColor FairwayFill = SKColor.Parse("#c3e0ad");
    static readonly SKColor FairwayEdge = SKColor.Parse("#9fca88");
    static readonly SKColor GreenFill = SKColor.Parse("#98cf80");
    static readonly SKColor GreenEdge = SKColor.Parse("#4f9443");
    static readonly SKColor BunkerFill = SKColor.Parse("#f0e4d2");
    static readonly SKColor BunkerEdge = SKColor.Parse("#c5ab88");
    static readonly SKColor WaterFill = SKColor.Parse("#cfe3ec");
    static readonly SKColor ScrubFill = SKColor.Parse("#7f9b5c");
    static readonly SKColor TreeFill = SKColor.Parse("#2f6b3a");
    static readonly SKColor RockFill = SKColor.Parse("#a79f93");
    static readonly SKColor RockEdge = SKColor.Parse("#7e776c");
    static readonly SKColor LineOfPlay = SKColor.Parse("#5f6b64");
    static readonly SKColor PathColour = SKColor.Parse("#d9cfb8");
    static readonly SKColor ArcGreen = SKColor.Parse("#a7afb5");
    static readonly SKColor ArcTee = SKColor.Parse("#b9c4d6");
    static readonly SKColor Ink = SKColor.Parse("#222222");
    static readonly SKColor Muted = SKColor.Parse("#7d868d");
    static readonly SKColor GreenText = SKColor.Parse("#2e8b3e");
    static readonly SKColor RedText = SKColor.Parse("#c0392b");

    readonly YardageOptions _o;
    readonly SKTypeface _regular, _bold;

    public YardagePageRenderer(YardageOptions options)
    {
        _o = options;
        string family = new[] { "Segoe UI", "Arial" }
            .FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase)) ?? SKTypeface.Default.FamilyName;
        _regular = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
        _bold = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);
    }

    public void Dispose() { _regular.Dispose(); _bold.Dispose(); }

    public void Draw(SKCanvas canvas, string courseName, HoleAnalysis a)
    {
        canvas.Clear(Paper);
        var proj = MapProjection.Fit(a.Window, MapRect, a.RotationDegrees, padding: 1.0);
        float k = proj.PointsPerMetre;

        canvas.Save();
        canvas.ClipRect(MapRect);
        using (var g = Fill(Ground)) canvas.DrawRect(MapRect, g);

        FillAll(canvas, proj, a.Rough, RoughFill, null, a.Foreign);
        foreach (var r in a.Paths) { using var s = Stroke(PathColour, Math.Max(0.8f, (float)(r.Width * k))); using var path = proj.Path(r.Points, false); canvas.DrawPath(path, s); }
        FillAll(canvas, proj, a.Water, WaterFill, null);
        foreach (var body in a.WaterBodies)
        {
            // One path per body, even-odd, so islands stay dry.
            using var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
            foreach (var shore in body) proj.AddPolyline(b, shore, true);
            using var path = b.Detach();
            using var f = Fill(WaterFill);
            canvas.DrawPath(path, f);
        }
        FillAll(canvas, proj, a.Fairways, FairwayFill, FairwayEdge, a.Foreign);
        FillAll(canvas, proj, a.Bunkers, BunkerFill, BunkerEdge, a.Foreign);

        // Background vegetation and rock: kept off this hole's fairways, tees and greens (a tee complex set
        // inside a rocky surround stays visible). In-play hazards are drawn on top, unclipped.
        using (var play = new SKPathBuilder())
        {
            foreach (var shape in a.Fairways.Concat(a.Greens).Where(f => !a.Foreign.Contains(f) && f.Count >= 3))
                proj.AddPolyline(play, shape, true);
            foreach (var t in a.TeeSets.Select(t => t.Tee).Distinct())
                play.AddCircle(proj.Map(t).X, proj.Map(t).Y, (float)(7 * k), SKPathDirection.Clockwise);
            using var playPath = play.Detach();
            canvas.Save();
            canvas.ClipPath(playPath, SKClipOperation.Difference, antialias: true);
            DrawMasses(canvas, proj, a.Vegetation.Where(l => !a.InPlay.Contains(l)), ScrubFill.WithAlpha(72), ScrubFill.WithAlpha(120), 0.6f);
            DrawMasses(canvas, proj, a.Outcrops.Where(m => !a.InPlay.Contains(m)), RockFill.WithAlpha(90), RockEdge.WithAlpha(110), 0.5f);
            canvas.Restore();
        }
        // In-play hazards (inside this hole's fairway) mix rock and scrub: a strong olive-grey.
        DrawMasses(canvas, proj, a.Outcrops.Where(a.InPlay.Contains), SKColor.Parse("#9c9a7c").WithAlpha(200), SKColor.Parse("#6f6d55"), 0.9f);

        foreach (var t in a.TeeSets.Select(t => t.Tee).Distinct()) DrawTeePad(canvas, proj, t, a.Forward);
        FillAll(canvas, proj, a.Greens, GreenFill, GreenEdge, a.Foreign);

        DrawLineOfPlay(canvas, proj, a);

        DrawArcs(canvas, proj, a);
        DrawWidths(canvas, proj, a);
        DrawSpots(canvas, proj, a);
        DrawBunkerLabels(canvas, proj, a);
        DrawGreenDepth(canvas, proj, a);
        DrawTeeLabels(canvas, proj, a);
        DrawAimPoints(canvas, proj, a);
        canvas.Restore();

        using (var frame = Stroke(SKColor.Parse("#9aa3a8"), 0.8f)) canvas.DrawRect(MapRect, frame);
        DrawHeader(canvas, courseName, a, proj);
        DrawFooter(canvas, a);
    }

    // ───────────────────────────── features ─────────────────────────────

    /// <summary>
    /// Fills shapes (edge first, so the union gets one outline). Shapes owned by another hole are
    /// drawn in a faded pass underneath, so the neighbour is visible but clearly not this hole.
    /// </summary>
    static void FillAll(SKCanvas canvas, MapProjection p, IEnumerable<List<Vec2>> shapes, SKColor fill, SKColor? edge,
        IReadOnlySet<List<Vec2>>? foreign = null)
    {
        var all = shapes.Where(s => s.Count >= 3).ToList();
        void Pass(IEnumerable<List<Vec2>> set, byte alpha)
        {
            var paths = set.Select(s => p.Path(s, true)).ToList();
            if (edge is { } e) { using var s = Stroke(e.WithAlpha((byte)(e.Alpha * alpha / 255)), 1.4f); foreach (var path in paths) canvas.DrawPath(path, s); }
            using var f = Fill(Blend(fill, alpha));
            foreach (var path in paths) canvas.DrawPath(path, f);
            foreach (var path in paths) path.Dispose();
        }
        Pass(all.Where(s => foreign?.Contains(s) == true), 95);
        Pass(all.Where(s => foreign?.Contains(s) != true), 255);
    }

    /// <summary>Fades a colour toward the ground tone (opaque, so overlaps don't darken).</summary>
    static SKColor Blend(SKColor c, byte alpha)
    {
        if (alpha == 255) return c;
        float t = alpha / 255f;
        byte Mix(byte a, byte g) => (byte)Math.Round(g + (a - g) * t);
        return new SKColor(Mix(c.Red, Ground.Red), Mix(c.Green, Ground.Green), Mix(c.Blue, Ground.Blue));
    }

    static void DrawMasses(SKCanvas canvas, MapProjection p, IEnumerable<List<Vec2>> loops, SKColor fill, SKColor edge, float edgeWidth)
    {
        using var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        foreach (var loop in loops.Where(l => l.Count >= 3)) p.AddPolyline(b, loop, true);
        using var path = b.Detach();
        using (var f = Fill(fill)) canvas.DrawPath(path, f);
        using (var s = Stroke(edge, edgeWidth)) canvas.DrawPath(path, s);
    }

    /// <summary>Faint dashed line of play: back tee → aim points → green.</summary>
    static void DrawLineOfPlay(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        using var s = Stroke(LineOfPlay.WithAlpha(150), 0.9f);
        s.PathEffect = SKPathEffect.CreateDash([5f, 4f], 0);
        using var path = p.Path(a.Route, false);
        canvas.DrawPath(path, s);
    }

    void DrawAimPoints(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        foreach (var aim in a.AimPoints)
        {
            var c = p.Map(aim.Point);
            using (var f = Fill(SKColors.White)) canvas.DrawCircle(c, 4.2f, f);
            using (var s = Stroke(LineOfPlay, 1.2f)) canvas.DrawCircle(c, 4.2f, s);
            using (var d = Fill(LineOfPlay)) canvas.DrawCircle(c, 1.4f, d);
            // Label on the side with more room.
            bool right = c.X < MapRect.MidX;
            var align = right ? SKTextAlign.Left : SKTextAlign.Right;
            float x = c.X + (right ? 8 : -8);
            Label(canvas, aim.FromTeeYards.ToString(), x, c.Y - 1, 10.5f, _bold, Ink, align, halo: true);
            Label(canvas, aim.ToGreenYards.ToString(), x, c.Y + 10, 9.5f, _bold, GreenText, align, halo: true);
        }
    }

    static void DrawTeePad(SKCanvas canvas, MapProjection p, Vec2 tee, Vec2 fwd)
    {
        float k = p.PointsPerMetre;
        var c = p.Map(tee);
        var d = p.MapDirection(fwd);
        float angle = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI) + 90;
        float len = Math.Max(4f, 10 * k), wid = Math.Max(3f, 6 * k);
        canvas.Save();
        canvas.Translate(c.X, c.Y);
        canvas.RotateDegrees(angle);
        var rect = new SKRect(-wid / 2, -len / 2, wid / 2, len / 2);
        using (var f = Fill(FairwayFill)) canvas.DrawRoundRect(rect, 1.5f, 1.5f, f);
        using (var s = Stroke(FairwayEdge.WithAlpha(255), 0.8f)) canvas.DrawRoundRect(rect, 1.5f, 1.5f, s);
        canvas.Restore();
    }

    /// <summary>Distance arcs: from the green centre (labels on the right) and from the back tee (labels on the left).</summary>
    void DrawArcs(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        float k = p.PointsPerMetre;
        int hole = a.LengthYards;
        var green = p.Map(a.Green);
        var tee = p.Map(a.BackTee);
        using var gs = Stroke(ArcGreen.WithAlpha(190), 0.7f);
        using var ts = Stroke(ArcTee.WithAlpha(190), 0.7f);
        ts.PathEffect = SKPathEffect.CreateDash([4f, 3f], 0);

        for (int y = 50; y <= hole - 25; y += 25)
        {
            float r = (float)(y * Yard * k);
            DrawVisibleArc(canvas, green, r, gs);
            // Label where the arc meets the right edge, below the green.
            float x = MapRect.Right - 13, dx = x - green.X;
            if (Math.Abs(dx) < r && green.Y + MathF.Sqrt(r * r - dx * dx) is var yy && yy < MapRect.Bottom - 8 && yy > MapRect.Top + 8)
                Label(canvas, y.ToString(), x, yy + 3.5f, 9, _bold, ArcGreen.WithAlpha(255), SKTextAlign.Center, halo: true);
        }
        for (int y = 150; y <= hole - 40; y += 25)
        {
            float r = (float)(y * Yard * k);
            DrawVisibleArc(canvas, tee, r, ts);
            float x = MapRect.Left + 13, dx = x - tee.X;
            if (Math.Abs(dx) < r && tee.Y - MathF.Sqrt(r * r - dx * dx) is var yy && yy > MapRect.Top + 8 && yy < MapRect.Bottom - 8)
                Label(canvas, y.ToString(), x, yy + 3.5f, 9, _bold, SKColor.Parse("#7f93b3"), SKTextAlign.Center, halo: true);
        }
    }

    /// <summary>
    /// Strokes only the parts of a circle that fall inside the map. A distance arc can be many times the
    /// page's size; drawn whole (and dashed) it would put hundreds of invisible segments into the PDF.
    /// </summary>
    static void DrawVisibleArc(SKCanvas canvas, SKPoint c, float r, SKPaint paint)
    {
        var box = MapRect;
        box.Inflate(4, 4);
        const int Steps = 720;
        bool Inside(int i)
        {
            double t = i * 2 * Math.PI / Steps;
            return box.Contains(c.X + r * (float)Math.Cos(t), c.Y + r * (float)Math.Sin(t));
        }
        var inside = Enumerable.Range(0, Steps).Select(Inside).ToArray();
        if (inside.All(x => x)) { canvas.DrawCircle(c, r, paint); return; }
        if (!inside.Any(x => x)) return;
        int start = Array.FindIndex(inside, x => !x);   // begin scanning just after an outside sample
        using var b = new SKPathBuilder();
        var oval = new SKRect(c.X - r, c.Y - r, c.X + r, c.Y + r);
        for (int n = 0; n < Steps; n++)
        {
            int i = (start + n) % Steps;
            if (!inside[i]) continue;
            int run = 0;
            while (n + run < Steps && inside[(start + n + run) % Steps]) run++;
            // Pad one step each side so the arc reaches the clip edge.
            b.AddArc(oval, (i - 1) * 360f / Steps, (run + 1) * 360f / Steps);
            n += run;
        }
        using var path = b.Detach();
        canvas.DrawPath(path, paint);
    }

    void DrawWidths(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        foreach (var w in a.Widths)
        {
            using (var line = Stroke(Muted.WithAlpha(150), 0.6f)) { line.PathEffect = SKPathEffect.CreateDash([2f, 2f], 0); canvas.DrawLine(p.Map(w.Left), p.Map(w.Right), line); }
            var c = p.Map(w.Centre);
            using (var f = Fill(SKColors.White)) canvas.DrawCircle(c, 8.5f, f);
            using (var s = Stroke(Muted, 0.8f)) canvas.DrawCircle(c, 8.5f, s);
            Label(canvas, w.Yards.ToString(), c.X, c.Y + 3.3f, 8.5f, _bold, Ink, SKTextAlign.Center);
        }
    }

    void DrawSpots(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        // Labels that take priority over a spot height sitting in the same place.
        var busy = a.AimPoints.Select(x => p.Map(x.Point)).Concat(a.BunkerLabels.Select(x => p.Map(x.Point)))
            .Concat(a.Widths.Select(x => p.Map(x.Centre))).ToList();
        var tees = a.TeeSets.Select(t => p.Map(t.Tee)).ToList();   // tee labels already carry the elevation
        foreach (var s in a.Spots)
        {
            int ft = (int)Math.Round(s.Feet);
            var c = p.Map(s.Point);
            if (busy.Any(q => Math.Abs(q.X - (c.X - 16)) < 30 && Math.Abs(q.Y - c.Y) < 20)) continue;
            if (tees.Any(q => SKPoint.Distance(q, c) < 28)) continue;
            string text = ft == 0 ? "-0-" : ft > 0 ? $"+{ft}" : ft.ToString();
            var colour = ft == 0 ? Muted : ft > 0 ? GreenText : RedText;
            Label(canvas, text, c.X - 16, c.Y + 3, 9, _bold, colour, SKTextAlign.Center, halo: true);
        }
    }

    void DrawBunkerLabels(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        var placed = new List<SKPoint>();
        var axisX = (p.Map(a.BackTee).X + p.Map(a.Green).X) / 2;
        foreach (var b in a.BunkerLabels.OrderBy(b => b.FromTeeYards))
        {
            var c = p.Map(b.Point);
            // Text sits on the outside of the bunker, away from the line of play.
            bool right = c.X >= axisX;
            // Flip to the other side if the text would run off the map.
            float textW = Measure(Math.Max(b.FromTeeYards, b.ToGreenYards).ToString(), 10.5f, _bold) + 8;
            if (right && c.X + textW > MapRect.Right - 4) right = false;
            else if (!right && c.X - textW < MapRect.Left + 4) right = true;
            var anchor = new SKPoint(c.X + (right ? 5 : -5), Math.Clamp(c.Y, MapRect.Top + 12, MapRect.Bottom - 12));
            if (placed.Any(q => SKPoint.Distance(q, anchor) < 24)) continue;
            placed.Add(anchor);
            using (var dot = Fill(Ink)) canvas.DrawCircle(c, 1.8f, dot);
            var align = right ? SKTextAlign.Left : SKTextAlign.Right;
            Label(canvas, b.FromTeeYards.ToString(), anchor.X, anchor.Y - 1, 10.5f, _bold, Ink, align, halo: true);
            Label(canvas, b.ToGreenYards.ToString(), anchor.X, anchor.Y + 10, 9.5f, _bold, GreenText, align, halo: true);
        }
    }

    void DrawGreenDepth(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        if (a.GreenDepthYards <= 0) return;
        var c = p.Map(a.Green);
        using (var f = Fill(SKColors.White.WithAlpha(235))) canvas.DrawCircle(c, 8.5f, f);
        using (var s = Stroke(GreenEdge, 0.8f)) canvas.DrawCircle(c, 8.5f, s);
        Label(canvas, a.GreenDepthYards.ToString(), c.X, c.Y + 3.3f, 8.5f, _bold, Ink, SKTextAlign.Center);
    }

    void DrawTeeLabels(SKCanvas canvas, MapProjection p, HoleAnalysis a)
    {
        var placed = new List<SKPoint>();
        foreach (var t in a.TeeSets.GroupBy(t => t.Tee).Select(g => g.Last()))
        {
            var c = p.Map(t.Tee);
            var at = new SKPoint(c.X + 10, c.Y + 3);
            while (placed.Any(q => Math.Abs(q.Y - at.Y) < 11 && Math.Abs(q.X - at.X) < 60)) at.Y += 11;
            placed.Add(at);
            int ft = (int)Math.Round(t.ElevationFt);
            Label(canvas, t.Name, at.X, at.Y, 8.5f, _bold, Ink, SKTextAlign.Left, halo: true);
            float w = Measure(t.Name + " ", 8.5f, _bold);
            Label(canvas, ft == 0 ? "0" : ft > 0 ? $"+{ft}" : ft.ToString(), at.X + w, at.Y, 8.5f, _bold,
                ft < 0 ? RedText : ft > 0 ? GreenText : Muted, SKTextAlign.Left, halo: true);
        }
    }

    // ───────────────────────────── page furniture ─────────────────────────────

    void DrawHeader(SKCanvas canvas, string courseName, HoleAnalysis a, MapProjection p)
    {
        Label(canvas, courseName.ToUpperInvariant(), 16, 24, 11, _bold, Ink);
        Label(canvas, $"Hole {a.Hole.Number}  ·  Par {a.Hole.Par}  ·  {a.LengthYards} yds", 16, 40, 9.5f, _regular, Muted);
        Label(canvas, _o.Credit, PageWidth - 16, 24, 8, _regular, Muted, SKTextAlign.Right);

        // North arrow, top right.
        var d = p.MapDirection(MapProjection.North);
        var at = new SKPoint(PageWidth - 30, 40);
        var tip = new SKPoint(at.X + d.X * 9, at.Y + d.Y * 9);
        var tail = new SKPoint(at.X - d.X * 9, at.Y - d.Y * 9);
        using (var s = Stroke(Ink, 1.1f)) canvas.DrawLine(tail, tip, s);
        var n = new SKPoint(-d.Y, d.X);
        using var b = new SKPathBuilder();
        b.MoveTo(tip);
        b.LineTo(tip.X - d.X * 5 + n.X * 3, tip.Y - d.Y * 5 + n.Y * 3);
        b.LineTo(tip.X - d.X * 5 - n.X * 3, tip.Y - d.Y * 5 - n.Y * 3);
        b.Close();
        using var head = b.Detach();
        using (var f = Fill(Ink)) canvas.DrawPath(head, f);
        Label(canvas, "N", tip.X + d.X * 7, tip.Y + d.Y * 7 + 3, 7.5f, _bold, Ink, SKTextAlign.Center);
    }

    void DrawFooter(SKCanvas canvas, HoleAnalysis a)
    {
        float top = MapRect.Bottom + 12;
        // Par / length badge.
        var badge = new SKPoint(40, top + 32);
        using (var f = Fill(SKColors.White)) canvas.DrawCircle(badge, 24, f);
        using (var s = Stroke(GreenEdge, 1.6f)) canvas.DrawCircle(badge, 24, s);
        Label(canvas, $"Par {a.Hole.Par}", badge.X, badge.Y - 3, 9.5f, _bold, Ink, SKTextAlign.Center);
        Label(canvas, a.LengthYards.ToString(), badge.X, badge.Y + 10, 11, _bold, Ink, SKTextAlign.Center);

        // Tee sets: name / yards / elevation change to the green (+ = plays uphill).
        float x0 = 78, colW = Math.Min(62, (PageWidth - 78 - 70) / Math.Max(1, a.TeeSets.Count));
        Label(canvas, "Tee", x0, top + 14, 8, _regular, Muted);
        Label(canvas, "Yards", x0, top + 30, 8, _regular, Muted);
        Label(canvas, "Elev.", x0, top + 46, 8, _regular, Muted);
        for (int i = 0; i < a.TeeSets.Count; i++)
        {
            var t = a.TeeSets[i];
            float cx = x0 + 42 + i * colW;
            int rise = -(int)Math.Round(t.ElevationFt);   // green minus tee
            Label(canvas, t.Name, cx, top + 14, 9, _bold, Ink, SKTextAlign.Center);
            Label(canvas, t.Yards.ToString(), cx, top + 30, 9.5f, _bold, Ink, SKTextAlign.Center);
            Label(canvas, rise == 0 ? "0" : rise > 0 ? $"+{rise}'" : $"{rise}'", cx, top + 46, 9, _bold,
                rise > 0 ? RedText : rise < 0 ? GreenText : Muted, SKTextAlign.Center);
        }
        Label(canvas, $"Green {a.GreenDepthYards} deep  ·  front {a.GreenFrontYards}  ·  back {a.GreenBackYards}", x0, top + 66, 8, _regular, Muted);
        Label(canvas, "Black = yds from back tee · green = yds to green centre · spot heights ft vs green", x0, top + 79, 7, _regular, Muted);
        Label(canvas, Attribution.Text, PageWidth - 16, top + 66, 6, _regular, Muted, SKTextAlign.Right);

        // Hole number.
        var hn = new SKPoint(PageWidth - 34, top + 32);
        using (var f = Fill(SKColors.White)) canvas.DrawCircle(hn, 20, f);
        using (var s = Stroke(GreenEdge, 1.8f)) canvas.DrawCircle(hn, 20, s);
        Label(canvas, a.Hole.Number.ToString(), hn.X, hn.Y + 7, 20, _bold, GreenEdge, SKTextAlign.Center);
    }

    // ───────────────────────────── primitives ─────────────────────────────

    static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };

    static SKPaint Stroke(SKColor c, float w) => new()
    {
        Color = c, StrokeWidth = w, IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };

    static float Measure(string text, float size, SKTypeface face)
    {
        using var font = new SKFont(face, size);
        return font.MeasureText(text);
    }

    static void Label(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor colour,
        SKTextAlign align = SKTextAlign.Left, bool halo = false)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        if (halo)
        {
            using var h = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.32f, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawText(text, x, y, align, font, h);
        }
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }
}
