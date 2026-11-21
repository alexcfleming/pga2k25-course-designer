using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// Three hand-drawn plan styles after classic course architects' drawings (each credited on the sheet):
/// <list type="bullet">
/// <item><b>Travis Sketch</b>, after Walter J. Travis's ink plans: wobbly outlines on white, rough as scattered ticks, trees
/// as small rings, mounds with sand pits, hole numbers in circles at the tees, a hand-lettered title, ruled scorecard and
/// legend across the bottom, under the map.</item>
/// <item><b>MacKenzie Sketch</b>, after Alister MacKenzie's pen plans: heavy-outlined greens with flags, woods as clumps of
/// scalloped crowns, slope strokes, plain numerals on the shot lines, a boxed serif title, a hatched-frame scorecard and a
/// signed credit.</item>
/// <item><b>Hanse Design</b>, after Hanse Golf Course Design's coloured plans: the property painted as a cut-out on cream
/// (lime fairways, grey native, dark tree masses, salmon bunkers, pale blue water with depth rings), two framed scorecards
/// and an ornamental title.</item>
/// </list>
/// Always landscape.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    public const string TravisStyleCredit = "IN THE STYLE OF A WALTER J. TRAVIS PLAN";
    public const string MacKenzieStyleCredit = "In the style of an Alister MacKenzie plan";
    public const string HanseStyleCredit = "In the style of a Hanse Golf Course Design plan";

    bool IsTravis => _t.Kind == MapThemeKind.TravisSketch;
    bool IsMacKenzie => _t.Kind == MapThemeKind.MacKenzieSketch;
    bool IsHanse => _t.Kind == MapThemeKind.HanseDesign;
    bool IsSketchStyle => IsTravis || IsMacKenzie || IsHanse;

    /// <summary>Height of Travis's band (scale, legend, title, card, architect) under the map.</summary>
    const float TravisBand = 172;

    SKRect SketchMapRect(SKSize page) =>
        IsTravis ? new SKRect(30, 28, page.Width - 30, page.Height - TravisBand - 16)
        : IsHanse ? new SKRect(HanseColumn + 18, 46, page.Width - 46, page.Height - 46)
        : new SKRect(30, 30, page.Width - 30, page.Height - 30);

    const float HanseColumn = 300;

    const float MacKenzieSignW = 234;
    static readonly (float X, float Y)[] HanseTitleSpots = [(1, 1), (0, 1), (1, 0)];

    float MacKenzieTitleHeight(string courseName)
    {
        float nameSize = Math.Min(30, 30 * 250 / Math.Max(1, Measure(courseName.ToUpperInvariant(), 30, SerifBold)));
        return 34 + nameSize + 30 + PlaceList().Count * 15 + 14;
    }

    /// <summary>
    /// MacKenzie: title (top), card (bottom), and the scale and signature in the other bottom corner. Hanse: its title
    /// in the map (the cards have their own column). Travis has none: its band sits under the map.
    /// </summary>
    List<BoxAsk> SketchBoxes(string courseName) =>
        IsMacKenzie ? [new(290, MacKenzieTitleHeight(courseName), [(0, 0), (1, 0)]), new(230, 150, [(0, 1), (1, 1)]), new(MacKenzieSignW, 66, [(1, 1), (0, 1)])]
        : IsHanse ? [new(330, 190, HanseTitleSpots)]
        : [];

    // Faces bundled next to the app (fonts/sketches, all SIL Open Font License) when present, else installed lookalikes:
    // Architects Daughter for hand lettering, Caveat for signatures, Old Standard TT for the MacKenzie serif, Macondo for
    // the Hanse title.
    SKTypeface? _hand, _script, _serifBold, _serif, _serifItalic, _ornament;
    static SKTypeface? Sketchface(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fonts", "sketches", file);
        return File.Exists(path) ? SKTypeface.FromFile(path) : null;
    }
    SKTypeface Hand => _hand ??= Sketchface("ArchitectsDaughter-Regular.ttf") ?? SKTypeface.FromFamilyName(Resolve(["Architects Daughter", "Segoe Print", "Ink Free"]), SKFontStyle.Bold);
    SKTypeface Script => _script ??= Weight(Sketchface("Caveat[wght].ttf"), 600) ?? SKTypeface.FromFamilyName(Resolve(["Caveat", "Segoe Script", "Ink Free"]), SKFontStyle.Bold);
    SKTypeface SerifBold => _serifBold ??= Sketchface("OldStandard-Bold.ttf") ?? SKTypeface.FromFamilyName(Resolve(["Old Standard TT", "Century Schoolbook", "Georgia"]), SKFontStyle.Bold);
    SKTypeface Serif => _serif ??= Sketchface("OldStandard-Regular.ttf") ?? SKTypeface.FromFamilyName(Resolve(["Old Standard TT", "Century Schoolbook", "Georgia"]), SKFontStyle.Normal);
    SKTypeface SerifItalic => _serifItalic ??= Sketchface("OldStandard-Italic.ttf") ?? SKTypeface.FromFamilyName(Resolve(["Old Standard TT", "Century Schoolbook", "Georgia"]), SKFontStyle.Italic);
    SKTypeface Ornament => _ornament ??= Sketchface("Macondo-Regular.ttf") ?? SKTypeface.FromFamilyName(Resolve(["Macondo", "Federo", "Georgia"]), SKFontStyle.Normal);

    /// <summary>A pen line: small jitter along the path, rounded again so it never looks like noise.</summary>
    static SKPathEffect Wobble(float deviation = 0.55f, uint seed = 7) =>
        SKPathEffect.CreateCompose(SKPathEffect.CreateCorner(1.2f), SKPathEffect.CreateDiscrete(3.2f, deviation, seed));

    /// <summary>Feet to the inch at this map scale (100 page units an inch), rounded to the nearest 10.</summary>
    static int FeetPerInch(MapProjection p) => (int)(Math.Round(100 / p.PointsPerMetre / 0.3048 / 10) * 10);

    void DrawSketchStyle(SKCanvas canvas, string courseName, CourseModel course, CourseLayers layers, MapProjection proj, SKSize page, SKRect map)
    {
        _scenery ??= Scenery.Build(course, proj, map);
        if (!IsHanse) _landscape ??= Landscape.Build(course, proj, map);
        var parcel = ParcelOf(layers);
        using var parcelPath = parcel.Count >= 3 ? proj.Path(parcel, true) : null;
        if (IsTravis) DrawTravis(canvas, courseName, course, layers, proj, page, map, parcelPath);
        else if (IsMacKenzie) DrawMacKenzie(canvas, courseName, course, layers, proj, page, map, parcelPath);
        else DrawHanse(canvas, courseName, course, layers, proj, page, map, parcelPath);
    }

    // ───────────────────────────── shared ─────────────────────────────

    /// <summary>
    /// A seamless texture tile (<paramref name="size"/> page units square, drawn at 4 px a unit) used as a repeating
    /// shader: one small image in the PDF instead of thousands of dots. <paramref name="paint"/> draws one mark at a
    /// point; each mark is repeated across the tile's edges so the tile wraps without a seam.
    /// </summary>
    static SKShader TileShader(float size, int marks, int seed, Action<SKCanvas, SKPoint, Random> paint)
    {
        const float density = 4;
        int px = (int)Math.Ceiling(size * density);
        using var bmp = new SKBitmap(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            c.Scale(density);
            var rnd = new Random(seed);
            for (int i = 0; i < marks; i++)
            {
                var at = new SKPoint((float)rnd.NextDouble() * size, (float)rnd.NextDouble() * size);
                int state = rnd.Next();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        paint(c, new SKPoint(at.X + dx * size, at.Y + dy * size), new Random(state));
            }
        }
        using var image = SKImage.FromBitmap(bmp);
        return image.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScale(1 / density, 1 / density));
    }

    /// <summary>Fills <paramref name="area"/> with a texture tile.</summary>
    static void FillTexture(SKCanvas canvas, SKPath area, SKShader shader)
    {
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawPath(area, paint);
    }

    SKPath WaterPath(CourseLayers l, MapProjection p)
    {
        using var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        foreach (var a in l.WaterAreas) p.AddPolyline(b, a, true);
        foreach (var shore in l.WaterBodies.SelectMany(body => body)) p.AddPolyline(b, shore, true);
        return b.Detach();
    }

    /// <summary>The sea, from the landscape's water lines, as filled strips (for tests of "is this point sea").</summary>
    bool IsSea(SKPoint at)
    {
        if (_landscape is null) return false;
        foreach (var (y, x0, x1) in _landscape.WaterLines)
            if (Math.Abs(y - at.Y) <= 3.1f && at.X >= x0 && at.X <= x1) return true;
        return false;
    }

    static void DrawFlag(SKCanvas canvas, SKPoint at, float h, SKColor ink, SKColor? flag = null)
    {
        using var pole = Stroke(ink, 0.6f);
        canvas.DrawLine(at.X, at.Y, at.X, at.Y - h, pole);
        using var b = new SKPathBuilder();
        b.MoveTo(at.X, at.Y - h); b.LineTo(at.X + h * 0.55f, at.Y - h * 0.82f); b.LineTo(at.X, at.Y - h * 0.64f); b.Close();
        using var tri = b.Detach();
        using var f = Fill(flag ?? ink);
        canvas.DrawPath(tri, f);
    }

    /// <summary>Route points on the page and a spot at fraction f, pushed sideways off the line by <paramref name="side"/>.</summary>
    static SKPoint Beside(IReadOnlyList<SKPoint> route, float f, float side)
    {
        var at = At(route, f);
        var ahead = At(route, Math.Min(1, f + 0.02f));
        float dx = ahead.X - at.X, dy = ahead.Y - at.Y, len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
        return new SKPoint(at.X - dy / len * side, at.Y + dx / len * side);
    }

    /// <summary>The corner box (of those offered) with the least play under it.</summary>
    static SKRect QuietCorner(CourseLayers l, MapProjection p, SKRect map, float w, float h, params (float X, float Y)[] spots) =>
        spots.Select(f => SpotBox(map, f, w, h)).MinBy(b => PlayUnder(l, p, b));

    /// <summary>
    /// A two-nines card: Hole / Yards / Par for 1–9 and 10–18 side by side, Out and In, then the total. Returns the bottom.
    /// </summary>
    float DrawNinesCard(SKCanvas canvas, CourseModel course, SKRect area, int set, SKTypeface face, SKTypeface bold, float size,
        SKColor ink, bool grid, float rowH)
    {
        var (_, yards) = TeeColumns(course);
        var holes = course.Holes;
        var nines = new[] { holes.Where(h => h.Number <= 9).ToList(), holes.Where(h => h.Number > 9).ToList() };
        float gap = 6, colW = (area.Width - gap) / 2;
        using var rule = Stroke(ink, 0.6f);
        rule.StrokeCap = SKStrokeCap.Butt;
        float bottom = area.Top;
        for (int n = 0; n < 2; n++)
        {
            var block = nines[n];
            if (block.Count == 0) continue;
            float x0 = area.Left + n * (colW + gap), y = area.Top;
            float[] cols = [x0, x0 + colW * 0.34f, x0 + colW * 0.74f, x0 + colW];
            int rows = block.Count + 2;
            if (grid)
            {
                canvas.DrawRect(new SKRect(x0, y, x0 + colW, y + rows * rowH), rule);
                for (int r = 1; r < rows; r++) canvas.DrawLine(x0, y + r * rowH, x0 + colW, y + r * rowH, rule);
                for (int c = 1; c < 3; c++) canvas.DrawLine(cols[c], y, cols[c], y + rows * rowH, rule);
            }
            void Row(int r, string a, string b, string c, SKTypeface f)
            {
                float ty = y + r * rowH + rowH * 0.72f;
                Text(canvas, a, (cols[0] + cols[1]) / 2, ty, size, f, ink, SKTextAlign.Center);
                Text(canvas, b, (cols[1] + cols[2]) / 2, ty, size, f, ink, SKTextAlign.Center);
                Text(canvas, c, (cols[2] + cols[3]) / 2, ty, size, f, ink, SKTextAlign.Center);
            }
            Row(0, "Hole", "Yds", "Par", bold);
            for (int i = 0; i < block.Count; i++) Row(i + 1, block[i].Number.ToString(), yards(block[i], set).ToString(), block[i].Par.ToString(), face);
            Row(rows - 1, n == 0 ? "Out" : "In", block.Sum(h => yards(h, set)).ToString("N0"), block.Sum(h => h.Par).ToString(), bold);
            bottom = Math.Max(bottom, y + rows * rowH);
        }
        string total = $"{holes.Sum(h => yards(h, set)):N0} yds · par {holes.Sum(h => h.Par)}";
        Text(canvas, total, area.MidX, bottom + rowH * 0.95f, size, bold, ink, SKTextAlign.Center);
        return bottom + rowH * 1.3f;
    }

    // ───────────────────────────── Travis ─────────────────────────────

    void DrawTravis(SKCanvas canvas, string courseName, CourseModel course, CourseLayers l, MapProjection p, SKSize page, SKRect map, SKPath? parcel)
    {
        var ink = _t.Ink;
        float k = p.PointsPerMetre;
        using var grass = UnionPath(l.Grass, p);
        using var water = WaterPath(l, p);
        using var pen = Stroke(ink, 0.75f);
        pen.PathEffect = Wobble();

        canvas.Save();
        canvas.ClipRect(map);

        // The property line, ruled straight.
        if (parcel is not null) { using var line = Stroke(ink, 0.6f); canvas.DrawPath(parcel, line); }

        // Sea: ruled lines.
        using (var sea = Stroke(ink.WithAlpha(150), 0.35f))
            foreach (var (y, x0, x1) in _landscape!.WaterLines) canvas.DrawLine(x0, y, x1, y, sea);

        // Rough: a field of short ticks over the property, kept off the fairways, water and sea.
        using (var tick = Stroke(ink, 0.55f))
        using (var ticks = new SKPathBuilder())
        {
            var rnd = new Random(17);
            const float step = 8.5f;
            for (float y = map.Top + 4; y < map.Bottom; y += step)
                for (float x = map.Left + 4; x < map.Right; x += step)
                {
                    var at = new SKPoint(x + (float)(rnd.NextDouble() - 0.5) * step * 0.9f, y + (float)(rnd.NextDouble() - 0.5) * step * 0.9f);
                    if (parcel is not null && !parcel.Contains(at.X, at.Y)) continue;
                    if (grass.Contains(at.X, at.Y) || water.Contains(at.X, at.Y) || IsSea(at)) continue;
                    float lean = (float)(rnd.NextDouble() - 0.5) * 0.8f;
                    ticks.MoveTo(at.X, at.Y + 1.3f); ticks.LineTo(at.X + lean, at.Y - 1.3f);
                }
            using var all = ticks.Detach();
            canvas.DrawPath(all, tick);
        }

        // Trees as small rings, bushes as dots.
        using (var ring = Stroke(ink, 0.55f))
        using (var dot = Fill(ink))
        {
            using var rings = new SKPathBuilder();
            foreach (var t in _scenery!.Trees) rings.AddCircle(t.At.X, t.At.Y, Math.Clamp(t.Radius * 0.45f, 1.4f, 2.6f));
            using (var r = rings.Detach()) canvas.DrawPath(r, ring);
            using var dots = new SKPathBuilder();
            int i = 0;
            foreach (var (at, _) in _scenery.Bushes) if (i++ % 2 == 0 && !grass.Contains(at.X, at.Y)) dots.AddCircle(at.X, at.Y, 0.55f);
            using (var d = dots.Detach()) canvas.DrawPath(d, dot);
        }

        // Water: outline and short ripple dashes.
        if (!water.IsEmpty)
        {
            using (var paper = Fill(_t.Background)) canvas.DrawPath(water, paper);
            canvas.DrawPath(water, pen);
            canvas.Save();
            canvas.ClipPath(water, antialias: true);
            using var ripple = Stroke(ink, 0.45f);
            var rnd = new Random(4);
            var box = water.Bounds;
            for (float y = box.Top + 2; y < box.Bottom; y += 4)
                for (float x = box.Left; x < box.Right; x += 9) { float jx = x + (float)rnd.NextDouble() * 7; canvas.DrawLine(jx, y, jx + 3, y, ripple); }
            canvas.Restore();
        }
        foreach (var r in l.WaterRibbons)
        {
            using var path = p.Path(r.Points, false);
            using var bank = Stroke(ink, (float)(r.Width * k) + 1.4f); bank.PathEffect = Wobble(0.4f);
            canvas.DrawPath(path, bank);
            using var inner = Stroke(_t.Background, (float)(r.Width * k)); canvas.DrawPath(path, inner);
        }

        // Fairways: one wobbly outline round the mown ground.
        using (var paper = Fill(_t.Background)) canvas.DrawPath(grass, paper);
        canvas.DrawPath(grass, pen);

        // Paths: two fine lines.
        if (_o.ShowPaths)
            foreach (var r in l.TrimToPlay(l.Paths.Concat(l.Roads).ToList(), _o.PathReachMetres))
            {
                using var path = p.Path(r.Points, false);
                using var edge = Stroke(ink, Math.Max(1.6f, (float)(r.Width * k)) + 0.8f); canvas.DrawPath(path, edge);
                using var inner = Stroke(_t.Background, Math.Max(1.6f, (float)(r.Width * k))); canvas.DrawPath(path, inner);
            }

        // Mounds with sand pits: a heavy pen edge, stippled sand, and a dark mound on the far lip.
        using (var bunkers = BunkerPath(l, p))
        {
            canvas.Save(); canvas.Translate(0.9f, 0.9f);
            using (var mound = Stroke(ink, 1.6f)) { mound.PathEffect = Wobble(0.9f, 3); canvas.DrawPath(bunkers, mound); }
            canvas.Restore();
            using (var paper = Fill(_t.Background)) canvas.DrawPath(bunkers, paper);
            using (var edge = Stroke(ink, 0.7f)) { edge.PathEffect = Wobble(0.5f, 5); canvas.DrawPath(bunkers, edge); }
            using var sand = TileShader(24, 200, 31, (c, at, _) => { using var d = Fill(ink.WithAlpha(200)); c.DrawCircle(at, 0.3f, d); });
            FillTexture(canvas, bunkers, sand);
        }

        // Walled water over the fairways drawn before it (surfaces drawn after the wall stay on top).
        WaterWall.Draw(canvas, l, p, _t.Background, ink, _t.Background, 1.2f);
        DrawRocks(canvas, p, RockStyle.Ink(_t.Background, ink, 0.7f));

        // Greens: outlined, the hole number and a flag inside.
        using (var gp = Stroke(ink, 0.9f))
        {
            gp.PathEffect = Wobble(0.4f, 9);
            foreach (var g in l.Greens) { using var path = p.Path(g, true); using (var paper = Fill(_t.Background)) canvas.DrawPath(path, paper); canvas.DrawPath(path, gp); }
        }
        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees) DrawTeePad(canvas, p, tee, h.Direction);

        // Shot lines: plain ruled lines, tee to aim to green.
        using (var route = Stroke(ink, 0.6f))
            foreach (var h in l.Holes) { using var path = p.Path(h.Route, false); canvas.DrawPath(path, route); }
        canvas.Restore();

        // Numbers: in a circle behind the back tee, and again in the green beside its flag.
        foreach (var h in l.Holes)
        {
            var route = h.Route.Select(p.Map).ToList();
            var tee = route[0];
            var dir = p.MapDirection(h.Direction);
            var at = new SKPoint(tee.X - dir.X * 9, tee.Y - dir.Y * 9);
            at = new SKPoint(Math.Clamp(at.X, map.Left + 7, map.Right - 7), Math.Clamp(at.Y, map.Top + 7, map.Bottom - 7));
            using (var paper = Fill(_t.Background)) canvas.DrawCircle(at, 6.2f, paper);
            using (var ring = Stroke(ink, 0.8f)) { ring.PathEffect = Wobble(0.3f, (uint)h.Hole.Number); canvas.DrawCircle(at, 6.2f, ring); }
            Text(canvas, h.Hole.Number.ToString(), at.X, at.Y + 2.9f, 7.5f, Hand, ink, SKTextAlign.Center);
            var pin = route[^1];
            DrawFlag(canvas, new SKPoint(pin.X + 1.5f, pin.Y + 2.5f), 5.5f, ink);
            Text(canvas, h.Hole.Number.ToString(), pin.X - 1.2f, pin.Y + 2.4f, 5.5f, Hand, ink, SKTextAlign.Right);
        }

        // Across the bottom, under the map: north arrow and scale, the legend, then the title, the card and the architect.
        // Laid out as a band 30-180 units down, moved down under the map.
        canvas.Save();
        canvas.Translate(0, map.Bottom + 6 - 30);
        var north = p.MapDirection(MapProjection.North);
        var arrowAt = new SKPoint(map.Left + 330, 92);
        using (var ap = Stroke(ink, 1.1f))
        {
            ap.PathEffect = Wobble(0.3f);
            var tail = new SKPoint(arrowAt.X - north.X * 34, arrowAt.Y - north.Y * 34);
            var tip = new SKPoint(arrowAt.X + north.X * 34, arrowAt.Y + north.Y * 34);
            canvas.DrawLine(tail, tip, ap);
            var side = new SKPoint(-north.Y, north.X);
            canvas.DrawLine(tip, new SKPoint(tip.X - north.X * 9 + side.X * 4, tip.Y - north.Y * 9 + side.Y * 4), ap);
            canvas.DrawLine(tip, new SKPoint(tip.X - north.X * 9 - side.X * 4, tip.Y - north.Y * 9 - side.Y * 4), ap);
            canvas.DrawLine(new SKPoint(arrowAt.X + side.X * 5, arrowAt.Y + side.Y * 5), new SKPoint(arrowAt.X - side.X * 5, arrowAt.Y - side.Y * 5), ap);
            Text(canvas, "N", tip.X + north.X * 8, tip.Y + north.Y * 8 + 3.5f, 10, Hand, ink, SKTextAlign.Center);
        }
        Text(canvas, "SCALE", map.Left + 330, 140, 9, Hand, ink, SKTextAlign.Center, tracking: 1.5f);
        Text(canvas, $"1\" = {FeetPerInch(p)}'", map.Left + 330, 153, 8.5f, Hand, ink, SKTextAlign.Center);

        // Legend.
        float lx = map.Left + 430, ly = 46;
        Text(canvas, "LEGEND", lx + 40, ly, 9.5f, Hand, ink, SKTextAlign.Center, tracking: 1.6f);
        using (var u = Stroke(ink, 0.6f)) canvas.DrawLine(lx + 10, ly + 3, lx + 70, ly + 3, u);
        void Key(float y, string label, Action<SKPoint> glyph) { glyph(new SKPoint(lx + 8, y - 3)); Text(canvas, label, lx + 26, y, 7.2f, Hand, ink); }
        Key(ly + 20, "TEE", c => { using var s = Stroke(ink, 0.7f); canvas.DrawRoundRect(new SKRect(c.X - 2.5f, c.Y - 5, c.X + 2.5f, c.Y + 5), 1.2f, 1.2f, s); });
        Key(ly + 37, "MOUNDS WITH SAND PITS", c =>
        {
            using var s = Stroke(ink, 1.5f); s.PathEffect = Wobble(0.6f); canvas.DrawOval(c.X + 0.6f, c.Y + 0.6f, 7, 4, s);
            using var paper = Fill(_t.Background); canvas.DrawOval(c.X, c.Y, 7, 4, paper);
            using var d = Fill(ink); for (int i = -2; i <= 2; i++) canvas.DrawCircle(c.X + i * 2.2f, c.Y + (i % 2) * 1.2f, 0.4f, d);
        });
        Key(ly + 54, "GREEN", c => { using var s = Stroke(ink, 0.9f); s.PathEffect = Wobble(0.3f); canvas.DrawOval(c.X, c.Y, 7, 4.5f, s); });
        Key(ly + 71, "ROUGH", c => { using var s = Stroke(ink, 0.55f); for (int i = -1; i <= 1; i++) canvas.DrawLine(c.X + i * 4, c.Y + 1.5f, c.X + i * 4 + 0.3f, c.Y - 1.5f, s); });
        Key(ly + 88, "TREES", c => { using var s = Stroke(ink, 0.55f); canvas.DrawCircle(c.X - 3, c.Y, 1.8f, s); canvas.DrawCircle(c.X + 3, c.Y, 1.8f, s); });
        Key(ly + 105, "LINE OF PLAY", c => { using var s = Stroke(ink, 0.6f); canvas.DrawLine(c.X - 7, c.Y, c.X + 7, c.Y, s); });

        // Title, card and credit.
        float tx = map.Left + 600, tw = map.Right - tx;
        string title = courseName.ToUpperInvariant();
        float ts = Math.Min(15, 15 * tw / Math.Max(1, Measure(title, 15, Hand) + title.Length * 2.2f));
        Text(canvas, title, tx + tw / 2, 44, ts, Hand, ink, SKTextAlign.Center, tracking: 2.2f);
        float by = DrawNinesCard(canvas, course, new SKRect(tx + 30, 54, tx + tw - 30, 54), TeeColumns(course).Names.Count - 1, Hand, Hand, 6.2f, ink, grid: true, rowH: 8.2f);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
        {
            Text(canvas, _o.Credit.ToUpperInvariant(), tx + tw / 2, by + 12, 8.5f, Hand, ink, SKTextAlign.Center, tracking: 1.4f);
            Text(canvas, "GOLF COURSE ARCHITECT", tx + tw / 2, by + 22, 7, Hand, ink, SKTextAlign.Center, tracking: 1.2f);
            using var u = Stroke(ink, 0.6f); canvas.DrawLine(tx + tw / 2 - 60, by + 25, tx + tw / 2 + 60, by + 25, u);
        }
        canvas.Restore();
        Text(canvas, TravisStyleCredit, map.Left, page.Height - 12, 6, Hand, _t.MutedInk);
        Text(canvas, Attribution.Text, map.Right, page.Height - 12, 6, _body, _t.MutedInk, SKTextAlign.Right);
    }

    // ───────────────────────────── MacKenzie ─────────────────────────────

    void DrawMacKenzie(SKCanvas canvas, string courseName, CourseModel course, CourseLayers l, MapProjection p, SKSize page, SKRect map, SKPath? parcel)
    {
        var ink = _t.Ink;
        float k = p.PointsPerMetre;
        using var grass = UnionPath(l.Grass, p);
        using var water = WaterPath(l, p);

        canvas.Save();
        canvas.ClipRect(map);

        // The property: a long-dash boundary.
        if (parcel is not null)
        {
            using var line = Stroke(ink, 0.7f);
            line.PathEffect = SKPathEffect.CreateDash([14, 3, 2, 3], 0);
            canvas.DrawPath(parcel, line);
        }

        // Slopes: the engraved landscape's strokes, fainter and finer, as pen shading.
        using (var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round })
            foreach (var hc in _landscape!.Hachures)
            {
                if (grass.Contains(hc.From.X, hc.From.Y)) continue;
                stroke.StrokeWidth = hc.Width * 0.7f;
                stroke.Color = ink.WithAlpha((byte)(hc.Alpha * 0.55f));
                canvas.DrawLine(hc.From, hc.To, stroke);
            }
        using (var sea = Stroke(ink.WithAlpha(120), 0.3f))
            foreach (var (y, x0, x1) in _landscape.WaterLines) canvas.DrawLine(x0, y, x1, y, sea);

        // Fairways: a fine edge only; the woods do the rest.
        using (var edge = Stroke(ink.WithAlpha(150), 0.45f)) { edge.PathEffect = Wobble(0.4f); canvas.DrawPath(grass, edge); }

        // Water and creeks: heavy pen banks.
        if (!water.IsEmpty)
        {
            using (var paper = Fill(_t.Background)) canvas.DrawPath(water, paper);
            using var bank = Stroke(ink, 1.5f); bank.PathEffect = Wobble(0.5f);
            canvas.DrawPath(water, bank);
        }
        foreach (var r in l.WaterRibbons)
        {
            using var path = p.Path(r.Points, false);
            using var bank = Stroke(ink, Math.Max(1.4f, (float)(r.Width * k) * 0.6f)); bank.PathEffect = Wobble(0.6f, 11);
            canvas.DrawPath(path, bank);
        }
        WaterWall.Draw(canvas, l, p, _t.Background, ink, _t.Background, 1.4f);
        DrawRocks(canvas, p, RockStyle.Ink(_t.Background, ink, 0.8f));
        if (_o.ShowPaths)
            foreach (var r in l.TrimToPlay(l.Roads.Concat(l.Paths).ToList(), _o.PathReachMetres))
            {
                using var path = p.Path(r.Points, false);
                using var s = Stroke(ink.WithAlpha(170), 0.5f); s.PathEffect = SKPathEffect.CreateDash([4, 2.5f], 0);
                canvas.DrawPath(path, s);
            }

        // Woods: every tree a few scalloped crowns, every bush one, outlined over paper so they pile into clumps.
        using (var paper = Fill(_t.Background))
        using (var crown = Stroke(ink, 0.32f))
        {
            var rnd = new Random(23);
            var crowns = new List<(SKPoint At, float R)>();
            foreach (var t in _scenery!.Trees)
            {
                float r = Math.Clamp(t.Radius * 0.5f, 1.3f, 3.4f);
                for (int i = 0; i < 3; i++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2;
                    crowns.Add((new SKPoint(t.At.X + (float)Math.Cos(a) * r * 0.6f, t.At.Y + (float)Math.Sin(a) * r * 0.6f), r * (0.7f + 0.3f * (float)rnd.NextDouble())));
                }
            }
            foreach (var (at, br) in _scenery.Bushes)
                if (!grass.Contains(at.X, at.Y)) crowns.Add((at, Math.Clamp(br * 0.9f, 0.9f, 1.8f)));
            foreach (var (at, r) in crowns.OrderBy(c => c.At.Y))
            {
                canvas.DrawCircle(at, r, paper);
                canvas.DrawCircle(at, r, crown);
                canvas.DrawArc(new SKRect(at.X - r * 0.6f, at.Y - r * 0.6f, at.X + r * 0.6f, at.Y + r * 0.6f), 20, 70, false, crown);
            }
        }

        // Bunkers: pen outline, hatched on the shaded side.
        using (var bunkers = BunkerPath(l, p))
        {
            using (var paper = Fill(_t.Background)) canvas.DrawPath(bunkers, paper);
            // The shaded face: the part of each bunker its own outline, nudged up and left, doesn't cover.
            using var nudged = new SKPath(bunkers);
            nudged.Offset(-1.8f, -1.8f);
            using var face = bunkers.Op(nudged, SKPathOp.Difference);
            if (face is not null)
            {
                canvas.Save();
                canvas.ClipPath(face, antialias: true);
                var fb = face.Bounds;
                using var hatch = Stroke(ink, 0.35f);
                for (float x = fb.Left - fb.Height; x < fb.Right; x += 1.3f) canvas.DrawLine(x, fb.Bottom, x + fb.Height, fb.Top, hatch);
                canvas.Restore();
            }
            using var edge = Stroke(ink, 0.65f); edge.PathEffect = Wobble(0.5f, 5);
            canvas.DrawPath(bunkers, edge);
        }

        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees) DrawTeePad(canvas, p, tee, h.Direction);

        // Greens: heavy black outline round white, a flag on the pin.
        using (var gp = Stroke(ink, 1.9f))
        {
            gp.PathEffect = Wobble(0.45f, 9);
            foreach (var g in l.Greens) { using var path = p.Path(g, true); using (var white = Fill(_t.Background)) canvas.DrawPath(path, white); canvas.DrawPath(path, gp); }
        }
        using (var route = Stroke(ink, 0.5f))
            foreach (var h in l.Holes) { using var path = p.Path(h.Route, false); canvas.DrawPath(path, route); }
        foreach (var h in l.Holes) { var pin = p.Map(h.Route[^1]); DrawFlag(canvas, new SKPoint(pin.X, pin.Y + 2), 5, ink); }
        canvas.Restore();

        // Numbers: plain bold numerals beside the shot line.
        var placed = new List<SKPoint>();
        foreach (var h in l.Holes)
        {
            var route = h.Route.Select(p.Map).ToList();
            var best = Beside(route, 0.5f, 11);
            foreach (var f in new[] { 0.5f, 0.4f, 0.6f, 0.32f, 0.68f })
            {
                best = Beside(route, f, 11);
                if (placed.All(q => SKPoint.Distance(q, best) > 20)) break;
            }
            placed.Add(best);
            Text(canvas, h.Hole.Number.ToString(), best.X, best.Y + 5, 14, SerifBold, ink, SKTextAlign.Center, halo: _t.Background);
        }

        // Title block, boxed, in the quietest top corner.
        string name = courseName.ToUpperInvariant();
        float nameSize = Math.Min(30, 30 * 250 / Math.Max(1, Measure(name, 30, SerifBold)));
        var place = PlaceList();
        var tb = Reserved(0, () => QuietCorner(l, p, map, 290, MacKenzieTitleHeight(courseName), (0, 0), (1, 0)));
        using (var paper = Fill(_t.Background)) canvas.DrawRect(tb, paper);
        using (var rule = Stroke(ink, 0.7f)) { canvas.DrawLine(tb.Left, tb.Bottom, tb.Right, tb.Bottom, rule); canvas.DrawLine(tb.Right, tb.Top, tb.Right, tb.Bottom, rule); }
        float ty = tb.Top + 26;
        Text(canvas, "PLAN OF", tb.MidX, ty, 17, SerifBold, ink, SKTextAlign.Center, tracking: 1); ty += nameSize + 2;
        Text(canvas, name, tb.MidX, ty, nameSize, SerifBold, ink, SKTextAlign.Center); ty += 26;
        Text(canvas, "GOLF COURSE", tb.MidX, ty, 21, SerifBold, ink, SKTextAlign.Center, tracking: 0.6f);
        foreach (var line in place) { ty += 16; Text(canvas, line.ToUpperInvariant() + ".", tb.MidX, ty, 10.5f, SerifBold, ink, SKTextAlign.Center, tracking: 1.4f); }

        // Scorecard in a hatched double frame, in the quietest bottom corner; the scale line and signature opposite.
        var cb = Reserved(1, () => QuietCorner(l, p, map, 230, 150, (0, 1), (1, 1)));
        using (var paper = Fill(_t.Background)) canvas.DrawRect(cb, paper);
        using (var outer = Stroke(ink, 1.4f)) canvas.DrawRect(cb, outer);
        var inner = SKRect.Inflate(cb, -6, -6);
        using (var thin = Stroke(ink, 0.6f)) canvas.DrawRect(inner, thin);
        canvas.Save();
        using (var frame = new SKPath { FillType = SKPathFillType.EvenOdd }) { frame.AddRect(cb); frame.AddRect(inner); canvas.ClipPath(frame); }
        using (var hatch = Stroke(ink, 0.5f)) for (float x = cb.Left - cb.Height; x < cb.Right; x += 2.4f) canvas.DrawLine(x, cb.Bottom, x + cb.Height, cb.Top, hatch);
        canvas.Restore();
        DrawNinesCard(canvas, course, SKRect.Inflate(inner, -6, -6), TeeColumns(course).Names.Count - 1, Serif, SerifBold, 7f, ink, grid: false, rowH: 10.2f);

        bool cardLeft = cb.MidX < map.MidX;
        var sb = Reserved(2, () => cardLeft ? new SKRect(map.Right - 10 - MacKenzieSignW, map.Bottom - 70, map.Right - 6, map.Bottom - 4)
            : new SKRect(map.Left + 6, map.Bottom - 70, map.Left + 10 + MacKenzieSignW, map.Bottom - 4));
        bool signRight = sb.MidX > map.MidX;
        float sx = signRight ? sb.Right - 4 : sb.Left + 4, sTop = sb.Top;
        var align = signRight ? SKTextAlign.Right : SKTextAlign.Left;
        using (var paper = Fill(_t.Background.WithAlpha(230))) canvas.DrawRect(sb, paper);
        Text(canvas, $"Scale – 1 inch to {FeetPerInch(p)} feet", sx, sTop + 16, 11, SerifBold, ink, align);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
        {
            Text(canvas, _o.Credit, sx, sTop + 36, 13, Script, ink, align);
            Text(canvas, "Golf Course Architect · " + DateTime.Now.ToString("MMMM yyyy"), sx, sTop + 50, 9.5f, Script, ink, align);
        }
        Text(canvas, MacKenzieStyleCredit, sx, sTop + 62, 7f, SerifItalic, _t.MutedInk, align);
        Text(canvas, Attribution.Text, page.Width / 2, page.Height - 12, 6, _body, _t.MutedInk, SKTextAlign.Center);
    }

    // ───────────────────────────── Hanse ─────────────────────────────

    static readonly SKColor HNative = SKColor.Parse("#e6e2d6"), HNativeFleck = SKColor.Parse("#9c9a90");
    static readonly SKColor HRough = SKColor.Parse("#c3d26a"), HFairway = SKColor.Parse("#d7e47c"), HMottleLight = SKColor.Parse("#eef3a6"), HMottleDark = SKColor.Parse("#a7bd55");
    static readonly SKColor HGreen = SKColor.Parse("#86b85a"), HGreenEdge = SKColor.Parse("#4c7a35");
    static readonly SKColor HTree = SKColor.Parse("#2f5a2c"), HTreeLit = SKColor.Parse("#4d7b3c"), HTreeDark = SKColor.Parse("#1f3f20");
    static readonly SKColor HSand = SKColor.Parse("#e8b994"), HSandEdge = SKColor.Parse("#8c5a3a"), HSandLit = SKColor.Parse("#f6dcc4");
    static readonly SKColor HWater = SKColor.Parse("#bcd8e4"), HWaterDeep = SKColor.Parse("#9fc3d6"), HWaterEdge = SKColor.Parse("#56788f");
    static readonly SKColor HGold = SKColor.Parse("#cdb27a"), HGoldEdge = SKColor.Parse("#5b4a2c");

    const float HanseCardsTop = 100, HanseCardsBottom = ShortSide - 96;

    /// <summary>
    /// The Hanse column's cards: one per tee set, stacked between the top and the scale. Returns each card's slot height,
    /// its row height (smaller for more sets, up to 10.2) and the card's own height, which always fits its slot.
    /// </summary>
    public static (float Slot, float RowH, float CardH) HanseCards(int sets)
    {
        float slot = (HanseCardsBottom - HanseCardsTop) / Math.Max(1, sets);
        float rowH = Math.Clamp((slot - 44) / 12.3f, 4.5f, 10.2f);
        return (slot, rowH, rowH * 12.3f + 14);
    }

    void DrawHanse(SKCanvas canvas, string courseName, CourseModel course, CourseLayers l, MapProjection p, SKSize page, SKRect map, SKPath? parcel)
    {
        var ink = _t.Ink;
        float k = p.PointsPerMetre;
        using var grass = UnionPath(l.Grass, p);
        using var fairways = UnionPath(l.Fairways, p);
        using var water = WaterPath(l, p);

        // Border: a ruled double line round the page, with corner boxes.
        using (var frame = Stroke(ink.WithAlpha(170), 0.8f))
        {
            canvas.DrawRect(new SKRect(16, 16, page.Width - 16, page.Height - 16), frame);
            canvas.DrawRect(new SKRect(22, 22, page.Width - 22, page.Height - 22), frame);
            foreach (var (cx, cy) in new[] { (22f, 22f), (page.Width - 82, 22f), (22f, page.Height - 82), (page.Width - 82, page.Height - 82) })
            {
                var box = new SKRect(cx, cy, cx + 60, cy + 60);
                canvas.DrawRect(box, frame);
                canvas.DrawRect(SKRect.Inflate(box, -4, -4), frame);
                if (_logo is { } seal) GreensBook.CourseLogoArt.Draw(canvas, seal, SKRect.Inflate(box, -9, -9));
                else DrawFlag(canvas, new SKPoint(box.MidX - 4, box.MidY + 12), 22, HGreenEdge, HSand);
            }
        }

        canvas.Save();
        canvas.ClipRect(map);
        if (parcel is not null) canvas.ClipPath(parcel, antialias: true);

        // Native ground: pale grey with flecks and scrub.
        using (var native = Fill(HNative)) canvas.DrawRect(map, native);
        using (var mapPath = new SKPath())
        {
            mapPath.AddRect(map);
            using var flecks = TileShader(48, 520, 41, (c, at, r) => { using var d = Fill(HNativeFleck.WithAlpha((byte)r.Next(90, 170))); c.DrawCircle(at, 0.3f + (float)r.NextDouble() * 0.25f, d); });
            FillTexture(canvas, mapPath, flecks);
        }

        // Rough and fairways: lime washes, mottled.
        void Mottle(SKPath area, SKColor light, SKColor dark, float density, int seed)
        {
            const float tile = 120;
            using var shader = TileShader(tile, (int)(tile * tile * density), seed, (c, at, r) =>
            {
                using var paint = Fill((r.Next(2) == 0 ? light : dark).WithAlpha((byte)r.Next(40, 90)));
                c.DrawCircle(at, 1.2f + (float)r.NextDouble() * 3.2f, paint);
            });
            FillTexture(canvas, area, shader);
        }
        using (var f = Fill(HRough)) canvas.DrawPath(grass, f);
        Mottle(grass, HMottleLight, HMottleDark, 0.05f, 51);
        using (var f = Fill(HFairway)) canvas.DrawPath(fairways, f);
        Mottle(fairways, HMottleLight, HRough, 0.06f, 52);
        using (var edge = Stroke(HMottleDark.WithAlpha(160), 0.5f)) canvas.DrawPath(grass, edge);

        // Water: pale blue, deepening inward in rings, a dark bank.
        if (!water.IsEmpty)
        {
            // Deep in the middle, paler towards the bank in three bands, each band edge a fine depth line.
            using (var deep = Fill(HWaterDeep)) canvas.DrawPath(water, deep);
            canvas.Save();
            canvas.ClipPath(water, antialias: true);
            using var ring = Stroke(HWaterEdge.WithAlpha(110), 0.4f);
            foreach (var (w, c) in new[] { (26f, SKColor.Parse("#acccdc")), (14f, HWater), (6f, SKColor.Parse("#cfe4ec")) })
            {
                using var band = Stroke(c, w);
                band.StrokeCap = SKStrokeCap.Round;
                canvas.DrawPath(water, band);
                using var edge = new SKPath();
                band.GetFillPath(water, edge);
                canvas.DrawPath(edge, ring);
            }
            canvas.Restore();
            using var bank = Stroke(HWaterEdge, 1.1f);
            canvas.DrawPath(water, bank);
        }
        foreach (var r in l.WaterRibbons)
        {
            using var path = p.Path(r.Points, false);
            using var s = Stroke(HWater, Math.Max(1.2f, (float)(r.Width * k))); canvas.DrawPath(path, s);
        }
        WaterWall.Draw(canvas, l, p, HWater, SKColor.Parse("#4a4a40"), _t.Background, 1.2f);
        DrawRocks(canvas, p, RockStyle.Painted(0.6f));

        // Cart paths: thin grey lines.
        if (_o.ShowPaths)
            foreach (var r in l.TrimToPlay(l.Paths.Concat(l.Roads).ToList(), _o.PathReachMetres))
            {
                using var path = p.Path(r.Points, false);
                using var s = Stroke(SKColor.Parse("#9b9a90"), 0.9f); canvas.DrawPath(path, s);
            }

        // Tree masses: dark, with lit tops and darker undersides.
        using (var dark = Fill(HTreeDark)) using (var body = Fill(HTree)) using (var lit = Fill(HTreeLit))
        {
            // One path per tone, so thousands of plants are three shapes in the PDF.
            void Pass(SKPaint paint, Func<SKPoint, float, (float X, float Y, float R)> treeAt, Func<SKPoint, float, (float X, float Y, float R)> bushAt)
            {
                using var b = new SKPathBuilder();
                foreach (var t in _scenery!.Trees) { var (x, y, r) = treeAt(t.At, t.Radius); b.AddCircle(x, y, r); }
                foreach (var (at, br) in _scenery.Bushes) { var (x, y, r) = bushAt(at, br); b.AddCircle(x, y, r); }
                using var path = b.Detach();
                canvas.DrawPath(path, paint);
            }
            Pass(dark, (a, r) => (a.X + r * 0.25f, a.Y + r * 0.25f, r * 0.9f), (a, r) => (a.X + 0.4f, a.Y + 0.4f, r * 1.7f));
            Pass(body, (a, r) => (a.X, a.Y, r * 0.85f), (a, r) => (a.X, a.Y, r * 1.55f));
            Pass(lit, (a, r) => (a.X - r * 0.25f, a.Y - r * 0.25f, r * 0.4f), (a, r) => (a.X - r * 0.4f, a.Y - r * 0.4f, r * 0.6f));
        }

        // Bunkers: salmon sand with a brown lip and a lit face.
        using (var bunkers = BunkerPath(l, p))
        {
            using (var sand = Fill(HSand)) canvas.DrawPath(bunkers, sand);
            canvas.Save();
            canvas.ClipPath(bunkers, antialias: true);
            canvas.Translate(0.9f, 0.9f);
            using (var lit = Fill(HSandLit)) canvas.DrawPath(bunkers, lit);
            canvas.Restore();
            using var lip = Stroke(HSandEdge, 0.6f);
            canvas.DrawPath(bunkers, lip);
        }

        // Tees and greens.
        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees)
            {
                var c = p.Map(tee);
                using var f = Fill(HGreen); canvas.DrawCircle(c, Math.Max(1.8f, 4 * k), f);
                using var s = Stroke(HGreenEdge, 0.4f); canvas.DrawCircle(c, Math.Max(1.8f, 4 * k), s);
            }
        foreach (var g in l.Greens)
        {
            using var path = p.Path(g, true);
            using (var f = Fill(HGreen)) canvas.DrawPath(path, f);
            using (var s = Stroke(HGreenEdge, 0.7f)) canvas.DrawPath(path, s);
        }
        canvas.Restore();

        // The property's edge, softly darkened.
        if (parcel is not null) { using var edge = Stroke(HNativeFleck.WithAlpha(150), 0.7f); canvas.DrawPath(parcel, edge); }

        // Shot lines and numbers.
        using (var route = Stroke(ink.WithAlpha(200), 0.45f))
            foreach (var h in l.Holes) { using var path = p.Path(h.Route, false); canvas.DrawPath(path, route); }
        foreach (var h in l.Holes) { var pin = p.Map(h.Route[^1]); DrawFlag(canvas, new SKPoint(pin.X, pin.Y + 1.5f), 4.2f, ink, SKColor.Parse("#b8483a")); }
        var placed = new List<SKPoint>();
        foreach (var h in l.Holes)
        {
            var route = h.Route.Select(p.Map).ToList();
            var best = Beside(route, 0.45f, 8);
            foreach (var f in new[] { 0.45f, 0.35f, 0.55f, 0.28f, 0.62f })
            {
                best = Beside(route, f, 8);
                if (placed.All(q => SKPoint.Distance(q, best) > 16)) break;
            }
            placed.Add(best);
            Text(canvas, h.Hole.Number.ToString(), best.X, best.Y + 3.5f, 9, Hand, ink, SKTextAlign.Center);
        }

        // Left column: a framed card per tee set, longest first, sized so every set fits above the scale.
        var (names, _) = TeeColumns(course);
        int sets = names.Count;
        var (slot, rowH, cardH) = HanseCards(sets);
        float cardText = rowH * 0.66f, y = HanseCardsTop + Math.Max(0, (slot - cardH - 26) / 2);
        for (int set = sets - 1; set >= 0; set--)
        {
            Text(canvas, sets > 1 ? names[set] : "Yards", (40 + HanseColumn) / 2, y + rowH, rowH * 1.15f, Hand, ink, SKTextAlign.Center);
            var box = new SKRect(52, y + rowH + 6, HanseColumn - 12, y + rowH + 6 + cardH);
            using (var s = Stroke(ink.WithAlpha(190), 0.7f)) { canvas.DrawRect(box, s); canvas.DrawRect(SKRect.Inflate(box, -3, -3), s); }
            DrawNinesCard(canvas, course, SKRect.Inflate(box, -8, -6), set, Hand, Hand, cardText, ink, grid: true, rowH: rowH);
            y += slot;
        }

        // Title, place and credits: in the quietest bottom corner of the map.
        string titleLine = courseName.Contains("Golf", StringComparison.OrdinalIgnoreCase) || courseName.Contains("Links", StringComparison.OrdinalIgnoreCase) || courseName.Contains("Club", StringComparison.OrdinalIgnoreCase)
            ? courseName : courseName + " Golf Course";
        var words = titleLine.Split(' ');
        int cut = Math.Max(1, words.Length / 2);
        var lines = words.Length > 1 ? new[] { "The " + string.Join(' ', words[..cut]), string.Join(' ', words[cut..]) } : ["The " + titleLine];
        var tb = Reserved(0, () => QuietCorner(l, p, map, 330, 190, HanseTitleSpots));
        float size = Math.Min(40, lines.Min(t => 40 * 320 / Math.Max(1, Measure(t, 40, Ornament))));
        float ty = tb.Top + size;
        using var goldPaint = new SKPaint { IsAntialias = true, Color = HGold };
        using var goldEdge = new SKPaint { IsAntialias = true, Color = HGoldEdge, Style = SKPaintStyle.Stroke, StrokeWidth = 0.9f };
        using var font = new SKFont(Ornament, size) { Subpixel = true };
        foreach (var t in lines)
        {
            canvas.DrawText(t, tb.MidX, ty, SKTextAlign.Center, font, goldPaint);
            canvas.DrawText(t, tb.MidX, ty, SKTextAlign.Center, font, goldEdge);
            ty += size * 1.05f;
        }
        var place = string.IsNullOrWhiteSpace(_o.Location) ? _o.PlaceLines : _o.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in place) { Text(canvas, line, tb.MidX, ty, 17, Ornament, HGoldEdge, SKTextAlign.Center); ty += 20; }

        // Credits under the title, slanted a touch as if signed; date, scale and north at the bottom of the column.
        canvas.Save();
        canvas.Translate(tb.MidX, ty + 6);
        canvas.RotateDegrees(-4);
        if (!string.IsNullOrWhiteSpace(_o.Credit)) Text(canvas, "Designed by " + _o.Credit, 0, 0, 13, Script, ink, SKTextAlign.Center);
        Text(canvas, HanseStyleCredit, 0, 15, 8, Script, _t.MutedInk, SKTextAlign.Center);
        canvas.Restore();
        Text(canvas, DateTime.Now.ToString("MMMM yyyy"), HanseColumn - 70, page.Height - 70, 10, Script, ink, SKTextAlign.Center);
        Text(canvas, $"Scale 1\" = {FeetPerInch(p)}'", HanseColumn - 70, page.Height - 56, 10, Script, ink, SKTextAlign.Center);
        var north = p.MapDirection(MapProjection.North);
        var na = new SKPoint(HanseColumn - 2, page.Height - 70);
        using (var np = Stroke(ink, 0.9f))
        {
            np.PathEffect = Wobble(0.3f);
            canvas.DrawLine(na.X - north.X * 14, na.Y - north.Y * 14, na.X + north.X * 14, na.Y + north.Y * 14, np);
            Text(canvas, "N", na.X + north.X * 22, na.Y + north.Y * 22 + 4, 11, Script, ink, SKTextAlign.Center);
        }
        Text(canvas, Attribution.Text, page.Width / 2, page.Height - 28, 6, _body, _t.MutedInk, SKTextAlign.Center);
    }
}
