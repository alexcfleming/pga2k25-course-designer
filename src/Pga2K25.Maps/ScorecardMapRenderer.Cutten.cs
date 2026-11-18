using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Cutten Map: an aged parchment course map in the manner of Keith Cutten's (credited on the sheet). The property
/// as a cut-out with a ragged wooded edge over mottled sepia paper; faint brown contours inside; tree masses in a
/// darker wash with ink edges; the corridor and fairways lighter; ink greens with a pin, bunkers and tee pads; fine
/// shot lines with small-caps numbers; a small-caps title block, a "Card of the Course" table, a compass rose and a
/// bar scale in the quiet corners. Landscape or portrait, whichever draws the course larger.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    public const string CuttenStyleCredit = "IN THE STYLE OF KEITH CUTTEN";

    bool IsCutten => _t.Kind == MapThemeKind.CuttenMap;

    static readonly SKColor CMPaper = SKColor.Parse("#e4d9b4"), CMPaperDark = SKColor.Parse("#b9a777"), CMPaperLight = SKColor.Parse("#efe7c8");
    static readonly SKColor CMLand = SKColor.Parse("#d7cb9d"), CMWoods = SKColor.Parse("#bfae7c"), CMCorridor = SKColor.Parse("#e2d8b0"), CMFairway = SKColor.Parse("#ebe2bf");
    static readonly SKColor CMInk = SKColor.Parse("#2f2619"), CMBrown = SKColor.Parse("#6d5838"), CMContour = SKColor.Parse("#8a7650");

    static SKRect CuttenMapRect(SKSize page) => new(40, 40, page.Width - 40, page.Height - 40);

    /// <summary>
    /// A torn edge for an outline (page units): walked every 1.8 units and pushed in and out along its normal by three
    /// waves of noise (up to about 6 units) and a few deeper bites into the land, like the edge of an old chart. Seeded,
    /// so a course tears the same way every time.
    /// </summary>
    static SKPath TornOutline(IReadOnlyList<SKPoint> outline, int seed)
    {
        var rnd = new Random(seed);
        var ring = outline.ToList();
        if (ring.Count < 3) { using var empty = new SKPathBuilder(); return empty.Detach(); }
        // Orientation, so "inward" points into the land.
        double area = 0;
        for (int i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; area += a.X * b.Y - b.X * a.Y; }
        float inSign = area > 0 ? 1 : -1;
        double perimeter = 0;
        for (int i = 0; i < ring.Count; i++) perimeter += SKPoint.Distance(ring[i], ring[(i + 1) % ring.Count]);
        double[] ph = Enumerable.Range(0, 3).Select(_ => rnd.NextDouble() * Math.PI * 2).ToArray();
        int waves = Math.Max(3, (int)(perimeter / 140));
        var bites = Enumerable.Range(0, Math.Max(4, (int)(perimeter / 260))).Select(_ => (At: rnd.NextDouble(), Depth: 3 + rnd.NextDouble() * 6, Width: 4 / perimeter + rnd.NextDouble() * 14 / perimeter)).ToList();
        var pts = new List<SKPoint>();
        double run = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            float len = SKPoint.Distance(a, b);
            if (len < 0.01f) continue;
            var dir = new SKPoint((b.X - a.X) / len, (b.Y - a.Y) / len);
            var inward = new SKPoint(-dir.Y * inSign, dir.X * inSign);
            for (float d = 0; d < len; d += 1.8f)
            {
                double t = (run + d) / perimeter;
                double depth = 1.6 * Math.Sin(t * 2 * Math.PI * waves + ph[0]) + 1.2 * Math.Sin(t * 2 * Math.PI * waves * 3.3 + ph[1])
                    + 0.7 * Math.Sin(t * 2 * Math.PI * waves * 9.1 + ph[2]) + (rnd.NextDouble() - 0.5) * 1.4;
                foreach (var bite in bites)
                {
                    double off = Math.Abs(t - bite.At); off = Math.Min(off, 1 - off);
                    if (off < bite.Width) depth += bite.Depth * (1 - off / bite.Width);
                }
                pts.Add(new SKPoint(a.X + dir.X * d + inward.X * (float)depth, a.Y + dir.Y * d + inward.Y * (float)depth));
            }
            run += len;
        }
        using var b2 = new SKPathBuilder();
        b2.AddPoly(pts.ToArray(), close: true);
        return b2.Detach();
    }

    void DrawCuttenMap(SKCanvas canvas, string courseName, CourseModel course, CourseLayers l, MapProjection p, SKSize page, SKRect map)
    {
        float k = p.PointsPerMetre;
        _scenery ??= Scenery.Build(course, p, map);
        _aerial ??= Aerial.Build(course, l, p, map, maxContours: 30);
        var contours = _aerial.Zoned(1, _ => 0)[0];
        var parcel = PortraitParcel.Build(l);
        using var property = parcel.Count >= 3 ? TornOutline(parcel.Select(p.Map).ToList(), course.Name.GetHashCode() & 0x7fffffff) : null;
        using var grass = UnionPath(l.Grass, p);
        using var fairways = UnionPath(l.Fairways, p);
        using var water = WaterPath(l, p);
        var pageRect = new SKRect(0, 0, page.Width, page.Height);

        // Aged paper: sepia, mottled, darker towards the edges.
        using (var paper = Fill(CMPaper)) canvas.DrawRect(pageRect, paper);
        using (var pagePath = new SKPath())
        {
            pagePath.AddRect(pageRect);
            using var mottle = TileShader(520, 260, 71, (c, at, r) =>
            {
                using var blot = new SKPaint { IsAntialias = true, Color = (r.Next(2) == 0 ? CMPaperDark : CMPaperLight).WithAlpha((byte)r.Next(8, 26)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9) };
                c.DrawCircle(at, 4 + (float)r.NextDouble() * 34, blot);
            });
            FillTexture(canvas, pagePath, mottle);
            using var grain = TileShader(40, 260, 73, (c, at, r) => { using var d = Fill(CMBrown.WithAlpha((byte)r.Next(10, 35))); c.DrawCircle(at, 0.25f, d); });
            FillTexture(canvas, pagePath, grain);
        }
        using (var vignette = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(page.Width / 2, page.Height / 2), Math.Max(page.Width, page.Height) * 0.72f,
                [SKColors.Transparent, SKColors.Transparent, CMPaperDark.WithAlpha(150)], [0, 0.62f, 1], SKShaderTileMode.Clamp),
        })
            canvas.DrawRect(pageRect, vignette);

        canvas.Save();
        canvas.ClipRect(map);

        // The property: a cut-out in a slightly deeper wash, faint contours inside, a ragged wooded edge.
        if (property is not null)
        {
            using (var land = Fill(CMLand.WithAlpha(200))) canvas.DrawPath(property, land);
            canvas.Save();
            canvas.ClipPath(property, antialias: true);
        }
        using (var thin = Stroke(CMContour.WithAlpha(85), 0.3f)) canvas.DrawPath(contours.Minor, thin);
        using (var heavy = Stroke(CMContour.WithAlpha(110), 0.45f)) canvas.DrawPath(contours.Major, heavy);
        contours.Minor.Dispose(); contours.Major.Dispose();

        // The corridor and fairways lighter, soft-edged.
        using (var corridor = new SKPaint { IsAntialias = true, Color = CMCorridor.WithAlpha(200), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.2f) }) canvas.DrawPath(grass, corridor);
        using (var fw = new SKPaint { IsAntialias = true, Color = CMFairway.WithAlpha(220), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1f) }) canvas.DrawPath(fairways, fw);

        // Woods: every tree and bush a disc; the union outlined in ink (stroke all, then fill all) and washed darker.
        using (var outline = new SKPathBuilder())
        using (var body = new SKPathBuilder())
        {
            foreach (var t in _scenery!.Trees) { float r = Math.Max(2.2f, t.Radius * 1.25f); outline.AddCircle(t.At.X, t.At.Y, r); body.AddCircle(t.At.X, t.At.Y, r); }
            foreach (var (at, br) in _scenery.Bushes)
                if (!grass.Contains(at.X, at.Y)) { float r = Math.Max(1.6f, br * 1.9f); outline.AddCircle(at.X, at.Y, r); body.AddCircle(at.X, at.Y, r); }
            using var o = outline.Detach();
            using var b = body.Detach();
            using (var edge = Stroke(CMInk.WithAlpha(210), 1.3f)) { edge.PathEffect = Wobble(0.5f, 81); canvas.DrawPath(o, edge); }
            using (var wash = Fill(CMWoods)) canvas.DrawPath(b, wash);
            using var tex = TileShader(60, 140, 83, (c, at, r) => { using var d = Fill(CMBrown.WithAlpha((byte)r.Next(25, 70))); c.DrawCircle(at, 0.5f + (float)r.NextDouble() * 1.4f, d); });
            FillTexture(canvas, b, tex);
        }

        // Water: an ink bank with a dotted fill; creeks as dotted brown lines.
        if (!water.IsEmpty)
        {
            using (var f = Fill(CMPaperLight)) canvas.DrawPath(water, f);
            using var dots = TileShader(16, 22, 85, (c, at, _) => { using var d = Fill(CMBrown.WithAlpha(120)); c.DrawCircle(at, 0.3f, d); });
            FillTexture(canvas, water, dots);
            using var bank = Stroke(CMInk, 0.6f); canvas.DrawPath(water, bank);
        }
        using (var creek = Stroke(CMBrown, 0.8f))
        {
            creek.PathEffect = SKPathEffect.CreateDash([0.8f, 1.6f], 0);
            foreach (var r in l.WaterRibbons) { using var path = p.Path(r.Points, false); canvas.DrawPath(path, creek); }
        }
        WaterWall.Draw(canvas, l, p, CMPaperLight, CMInk, CMPaperLight, 1.1f);
        if (_o.ShowPaths)
            using (var dashed = Stroke(CMBrown.WithAlpha(190), 0.55f))
            {
                dashed.PathEffect = SKPathEffect.CreateDash([1.6f, 1.4f], 0);
                foreach (var r in l.TrimToPlay(l.Paths.Concat(l.Roads).ToList(), _o.PathReachMetres)) { using var path = p.Path(r.Points, false); canvas.DrawPath(path, dashed); }
            }

        // Bunkers: ink outlines round pale sand.
        using (var bunkers = BunkerPath(l, p))
        {
            using (var sand = Fill(CMPaperLight)) canvas.DrawPath(bunkers, sand);
            using var edge = Stroke(CMInk, 0.6f); edge.PathEffect = Wobble(0.3f, 87); canvas.DrawPath(bunkers, edge);
        }
        if (property is not null) canvas.Restore();

        // The property's torn, burnt edge, like an old chart: soft scorched bands inside the tear, then a dark rim.
        if (property is not null)
        {
            canvas.Save();
            canvas.ClipPath(property, antialias: true);
            foreach (var (width, alpha, blur) in new[] { (30f, (byte)60, 10f), (14f, (byte)100, 5f), (5f, (byte)160, 2f) })
            {
                using var burn = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, Color = SKColor.Parse("#5a3b18").WithAlpha(alpha), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur) };
                canvas.DrawPath(property, burn);
            }
            canvas.Restore();
            using var rim = Stroke(SKColor.Parse("#3a2410").WithAlpha(215), 1.1f);
            canvas.DrawPath(property, rim);
        }

        // Tees, greens with an inner ring and a pin, and fine shot lines.
        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees) DrawTeePad(canvas, p, tee, h.Direction);
        using (var paperFill = Fill(CMFairway)) using (var ring = Stroke(CMInk, 0.75f)) using (var inner = Stroke(CMInk.WithAlpha(160), 0.4f))
            foreach (var g in l.Greens)
            {
                using var path = p.Path(g, true);
                canvas.DrawPath(path, paperFill);
                canvas.DrawPath(path, ring);
                var c = path.Bounds;
                float rr = Math.Min(c.Width, c.Height) * 0.28f;
                if (rr > 1) canvas.DrawCircle(c.MidX, c.MidY, rr, inner);
            }
        using (var route = Stroke(CMInk.WithAlpha(200), 0.45f)) using (var pinDot = Fill(CMInk))
            foreach (var h in l.Holes)
            {
                using var path = p.Path(h.Route, false); canvas.DrawPath(path, route);
                foreach (var at in h.Route.Skip(1).SkipLast(1)) canvas.DrawCircle(p.Map(at), 0.7f, pinDot);
                canvas.DrawCircle(p.Map(h.Route[^1]), 0.9f, pinDot);
            }
        canvas.Restore();

        // Hole numbers beside the line, small serif.
        var placed = new List<SKPoint>();
        foreach (var h in l.Holes)
        {
            var route = h.Route.Select(p.Map).ToList();
            var best = Beside(route, 0.5f, 7);
            foreach (var f in new[] { 0.5f, 0.4f, 0.6f, 0.32f, 0.68f })
            {
                best = Beside(route, f, 7);
                if (placed.All(q => SKPoint.Distance(q, best) > 16)) break;
            }
            placed.Add(best);
            Text(canvas, h.Hole.Number.ToString(), best.X, best.Y + 4, 11, Serif, CMInk, SKTextAlign.Center, halo: CMLand.WithAlpha(200));
        }

        // Title block in the quietest top corner: the name in large and small capitals, place, seal, credits.
        var place = (string.IsNullOrWhiteSpace(_o.Location) ? _o.PlaceLines : _o.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        bool seal = _logo is not null;
        float tbH = 46 + place.Count * 16 + (seal ? 96 : 10) + 50;
        var tb = QuietCorner(l, p, map, 300, tbH, (0, 0), (1, 0));
        float x = tb.Left + 6, y = tb.Top + 34;
        float nameW = SmallCaps(canvas, courseName, x, y, 30, 0, measureOnly: true);
        float nameSize = nameW > 290 ? 30 * 290 / nameW : 30;
        SmallCaps(canvas, courseName, x, y, nameSize, 0);
        foreach (var line in place) { y += 17; SmallCaps(canvas, line, x + 18, y, 11.5f, 0); }
        float cx = x + 110;
        if (_logo is { } logo) { GreensBook.CourseLogoArt.Draw(canvas, logo, new SKRect(cx - 38, y + 14, cx + 38, y + 90)); y += 96; } else y += 14;
        if (!string.IsNullOrWhiteSpace(_o.Credit))
        {
            y += 18; SmallCaps(canvas, "Designed by", cx, y, 10.5f, 0, SKTextAlign.Center);
            y += 15; SmallCaps(canvas, _o.Credit, cx, y, 12, 0, SKTextAlign.Center);
        }

        // Card of the Course in the quietest bottom spot.
        var cardBox = QuietCorner(l, p, map, 300, 150, (0.5f, 1f), (0f, 1f), (1f, 1f), (0.25f, 1f), (0.75f, 1f));
        DrawCuttenCard(canvas, course, cardBox);

        // Compass rose in a free corner, bar scale bottom left, the style credit bottom right.
        var taken = new[] { SKRect.Inflate(tb, 6, 6), SKRect.Inflate(cardBox, 6, 6) };
        float side = 2 * CompassRadius + 30;
        var roseSpots = new[] { (1f, 0.55f), (1f, 0f), (0f, 0.55f), (1f, 1f), (0f, 1f) }.Where(f => taken.All(t => !SpotBox(map, f, side, side).IntersectsWith(t))).DefaultIfEmpty((1f, 0.5f)).ToArray();
        var rb = QuietCorner(l, p, map, side, side, roseSpots);
        DrawCompassRose(canvas, p, new SKPoint(rb.MidX, rb.MidY), CompassRadius);
        DrawCuttenScale(canvas, p, new SKPoint(map.Left + 26, page.Height - 44));
        SmallCaps(canvas, CuttenStyleCredit, page.Width - 52, page.Height - 44, 7, 0.6f, SKTextAlign.Right);
        Text(canvas, Attribution.Text, page.Width - 52, page.Height - 35, 5.5f, _body, CMBrown, SKTextAlign.Right);
    }

    /// <summary>Large-and-small capitals in the serif: a word's first letter at full size, the rest at 76%. Returns the width.</summary>
    float SmallCaps(SKCanvas canvas, string text, float x, float y, float size, float tracking, SKTextAlign align = SKTextAlign.Left, bool measureOnly = false)
    {
        // Small words stay in small capitals, as on an engraved map ("Card of the Course").
        var minor = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "of", "the", "by", "and", "at", "in", "on", "a" };
        var runs = new List<(string S, float Size)>();
        var words = text.Split(' ');
        for (int wi = 0; wi < words.Length; wi++)
        {
            string word = words[wi];
            bool lead = wi == 0 || !minor.Contains(word);
            for (int ci = 0; ci < word.Length; ci++)
            {
                char ch = word[ci];
                bool big = (ci == 0 && lead && char.IsLetter(ch)) || !char.IsLetter(ch);
                runs.Add((char.ToUpperInvariant(ch).ToString(), big ? size : size * 0.76f));
            }
            if (wi < words.Length - 1) runs.Add((" ", size));
        }
        float width = runs.Sum(r => Measure(r.S, r.Size, Serif) + tracking);
        if (measureOnly) return width;
        float cx = align switch { SKTextAlign.Right => x - width, SKTextAlign.Center => x - width / 2, _ => x };
        foreach (var (s, sz) in runs) { Text(canvas, s, cx, y, sz, Serif, CMInk); cx += Measure(s, sz, Serif) + tracking; }
        return width;
    }

    /// <summary>"Card of the Course": Hole / Par / Yards for each nine side by side, Front, Back and Total, no rules but the title's.</summary>
    void DrawCuttenCard(SKCanvas canvas, CourseModel course, SKRect box)
    {
        var (names, yards) = TeeColumns(course);
        int set = names.Count - 1;
        var nines = new[] { course.Holes.Where(h => h.Number <= 9).ToList(), course.Holes.Where(h => h.Number > 9).ToList() };
        float y = box.Top + 12;
        float w = SmallCaps(canvas, "Card of the Course", box.MidX, y, 12, 0.3f, SKTextAlign.Center);
        using (var u = Stroke(CMInk, 0.6f)) canvas.DrawLine(box.MidX - w / 2, y + 2.5f, box.MidX + w / 2, y + 2.5f, u);
        float rowH = Math.Min(10.5f, (box.Height - 34) / 12f), half = box.Width / 2;
        for (int n = 0; n < 2; n++)
        {
            var block = nines[n];
            if (block.Count == 0) continue;
            float x0 = box.Left + n * half;
            float[] cols = [x0 + half * 0.18f, x0 + half * 0.45f, x0 + half * 0.78f];
            float ry = y + 15;
            SmallCaps(canvas, "Hole", cols[0], ry, 7.5f, 0, SKTextAlign.Center);
            SmallCaps(canvas, "Par", cols[1], ry, 7.5f, 0, SKTextAlign.Center);
            SmallCaps(canvas, "Yards", cols[2], ry, 7.5f, 0, SKTextAlign.Center);
            foreach (var h in block)
            {
                ry += rowH;
                Text(canvas, h.Number.ToString(), cols[0], ry, 7.8f, Serif, CMInk, SKTextAlign.Center);
                Text(canvas, h.Par.ToString(), cols[1], ry, 7.8f, Serif, CMInk, SKTextAlign.Center);
                Text(canvas, yards(h, set).ToString(), cols[2], ry, 7.8f, Serif, CMInk, SKTextAlign.Center);
            }
            ry += rowH;
            SmallCaps(canvas, n == 0 ? "Front" : "Back", cols[0], ry, 7.5f, 0, SKTextAlign.Center);
            Text(canvas, block.Sum(h => h.Par).ToString(), cols[1], ry, 7.8f, Serif, CMInk, SKTextAlign.Center);
            Text(canvas, block.Sum(h => yards(h, set)).ToString("N0"), cols[2], ry, 7.8f, Serif, CMInk, SKTextAlign.Center);
        }
        float ty = y + 15 + rowH * 11;
        SmallCaps(canvas, $"Total   {course.Holes.Sum(h => h.Par)}   {course.Holes.Sum(h => yards(h, set)):N0}", box.MidX, ty, 8, 0.3f, SKTextAlign.Center);
    }

    /// <summary>A bar scale in yards: 0, 150 and 300 with a bar alternating dark and light.</summary>
    void DrawCuttenScale(SKCanvas canvas, MapProjection p, SKPoint at)
    {
        float per = (float)(150 * Yard * p.PointsPerMetre);
        using var dark = Fill(CMInk); using var light = Fill(CMPaperLight); using var edge = Stroke(CMInk, 0.5f);
        var bar1 = new SKRect(at.X, at.Y, at.X + per, at.Y + 2.4f);
        var bar2 = new SKRect(at.X + per, at.Y, at.X + 2 * per, at.Y + 2.4f);
        canvas.DrawRect(bar1, dark); canvas.DrawRect(bar2, light); canvas.DrawRect(SKRect.Union(bar1, bar2), edge);
        Text(canvas, "0", at.X, at.Y - 3, 8, Serif, CMInk, SKTextAlign.Center);
        Text(canvas, "150", at.X + per, at.Y - 3, 8, Serif, CMInk, SKTextAlign.Center);
        SmallCaps(canvas, "300 Yds", at.X + 2 * per - 6, at.Y - 3, 8, 0);
    }
}
