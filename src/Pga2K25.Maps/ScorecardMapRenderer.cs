using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

public enum PageOrientation { Auto, Landscape, Portrait }

public sealed record ScorecardMapOptions
{
    public MapThemeKind Theme { get; init; } = MapThemeKind.Vintage;
    public PageOrientation Orientation { get; init; } = PageOrientation.Auto;

    /// <summary>Rotate the course to fill the page (north arrow shows true north). Off = north-up.</summary>
    public bool AutoRotate { get; init; } = true;

    public bool ShowPaths { get; init; } = true;

    /// <summary>Paths and roads are drawn only within this many metres of a hole.</summary>
    public double PathReachMetres { get; init; } = 45;

    /// <summary>Stippled waste areas. Off by default: on desert courses everything off the fairway is waste.</summary>
    public bool ShowWaste { get; init; }

    /// <summary>Fuse nearby bunker pieces into smooth outlines (off = raw shapes and brush dabs).</summary>
    public bool MergeBunkers { get; init; } = true;

    /// <summary>Gap (m) up to which neighbouring bunker pieces fuse when <see cref="MergeBunkers"/> is on.</summary>
    public double BunkerMergeMetres { get; init; } = 10;

    /// <summary>
    /// Designer hole data. Supplies only what the course file lacks (tee set names); the course
    /// file is always treated as the most correct source for pars and yardages.
    /// </summary>
    public HoleData? Data { get; init; }
    public string Credit { get; init; } = "";
    public string Subtitle { get; init; } = "PGA TOUR 2K25";

    /// <summary>Plan Sheet only: where the course is, printed under its name (e.g. "Olkhon Island, Lake Baikal"). Blank prints nothing.</summary>
    public string Location { get; init; } = "";
}

/// <summary>Draws a one-page scorecard map: traced course features, hole routes and numbers, scorecard table.</summary>
public sealed partial class ScorecardMapRenderer : IDisposable
{
    public const float LongSide = 1100, ShortSide = 850;   // 11 × 8.5 in at 100 units/in
    const double Yard = 0.9144;

    readonly MapTheme _t;
    readonly ScorecardMapOptions _o;
    readonly SKTypeface _title, _body, _bodyBold, _italic, _display, _label;
    // The Illustrated landscape, built on the first draw and reused (PNG and PDF share a renderer).
    Landscape? _landscape;

    public ScorecardMapRenderer(ScorecardMapOptions options)
    {
        _o = options;
        _t = MapTheme.For(options.Theme);
        string titleFamily = Resolve(_t.TitleFonts), bodyFamily = Resolve(_t.BodyFonts);
        _title = SKTypeface.FromFamilyName(titleFamily, SKFontStyle.Bold);
        _body = SKTypeface.FromFamilyName(bodyFamily, SKFontStyle.Normal);
        _bodyBold = SKTypeface.FromFamilyName(bodyFamily, SKFontStyle.Bold);
        _italic = SKTypeface.FromFamilyName(bodyFamily, SKFontStyle.Italic);
        (_display, _label) = PlanSheetFaces();
    }

    static string Resolve(string[] families) =>
        families.FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase))
        ?? SKTypeface.Default.FamilyName;

    public void Dispose() { _title.Dispose(); _body.Dispose(); _bodyBold.Dispose(); _italic.Dispose(); _display.Dispose(); _label.Dispose(); _landscape?.Dispose(); }

    /// <summary>Chooses page orientation and rotation, returning the page size in logical units.</summary>
    public (SKSize Page, bool Landscape, double Rotation) Plan(CourseLayers layers)
    {
        var pts = layers.ExtentPoints().ToList();
        (double scale, double rot) Try(bool landscape)
        {
            var rect = MapRect(landscape ? new SKSize(LongSide, ShortSide) : new SKSize(ShortSide, LongSide), landscape, IsPlanSheet);
            double rot = _o.AutoRotate ? MapProjection.BestRotation(pts, rect) : MapProjection.NorthUpRotation;
            return (MapProjection.FitScale(pts, rect, rot), rot);
        }
        var land = Try(true);
        var port = Try(false);
        bool useLandscape = IsPlanSheet || _o.Orientation switch
        {
            PageOrientation.Landscape => true,
            PageOrientation.Portrait => false,
            _ => land.scale >= port.scale,
        };
        return useLandscape ? (new SKSize(LongSide, ShortSide), true, land.rot) : (new SKSize(ShortSide, LongSide), false, port.rot);
    }

    static SKRect MapRect(SKSize page, bool landscape, bool planSheet = false) =>
        planSheet ? PlanSheetMapRect(page)
        : landscape ? new SKRect(40, 40, page.Width - 40 - 250 - 16, page.Height - 40)
        : new SKRect(40, 118, page.Width - 40, page.Height - 40 - 262);

    public void Draw(SKCanvas canvas, string courseName, CourseModel course, CourseLayers layers)
    {
        var (page, landscape, rotation) = Plan(layers);
        canvas.Clear(_t.Background);
        var mapRect = MapRect(page, landscape, IsPlanSheet);
        var proj = MapProjection.Fit(layers.ExtentPoints().ToList(), mapRect, rotation);
        if (_t.Landscape) _landscape ??= Landscape.Build(course, proj, mapRect);

        canvas.Save();
        canvas.ClipRect(mapRect);
        _landscape?.Draw(canvas, _t.Ink, _t.CrownFill);
        DrawCourse(canvas, layers, proj);
        canvas.Restore();
        DrawHoleNumbers(canvas, layers, proj, mapRect);
        if (IsPlanSheet)
        {
            DrawPlanSheet(canvas, courseName, course, layers, proj, page, mapRect);
            return;
        }
        if (_t.Landscape) DrawCompassRose(canvas, proj, RoseCorner(layers, proj, mapRect), 34);
        else DrawNorthArrow(canvas, proj, new SKPoint(mapRect.Left + 24, mapRect.Bottom - 34));
        DrawScaleBar(canvas, proj, new SKPoint(mapRect.Left + 56, mapRect.Bottom - 12));

        if (landscape)
        {
            var panel = new SKRect(page.Width - 40 - 250, 40, page.Width - 40, page.Height - 40);
            float y = DrawTitle(canvas, courseName, panel.Left, panel.Top + 30, panel.Width);
            y = DrawTable(canvas, course, new SKRect(panel.Left, y + 14, panel.Right, y + 14), splitNines: false);
            DrawFooter(canvas, course, new SKRect(panel.Left, y + 16, panel.Right, panel.Bottom));
        }
        else
        {
            DrawTitle(canvas, courseName, 40, 76, page.Width - 80);
            var bottom = new SKRect(40, page.Height - 40 - 246, page.Width - 40, page.Height - 40);
            float tableW = bottom.Width * 0.62f;
            DrawTable(canvas, course, new SKRect(bottom.Left, bottom.Top, bottom.Left + tableW, bottom.Top), splitNines: true);
            DrawFooter(canvas, course, new SKRect(bottom.Left + tableW + 24, bottom.Top, bottom.Right, bottom.Bottom));
        }
        if (_t.Frame) DrawFrame(canvas, page);
    }

    // ───────────────────────────── course ─────────────────────────────

    void DrawCourse(SKCanvas canvas, CourseLayers l, MapProjection p)
    {
        float k = p.PointsPerMetre;

        // Waste: filled, then stippled only where the fill landed.
        if (_o.ShowWaste)
        {
        canvas.SaveLayer();
        using (var fill = Fill(_t.WasteFill))
        {
            foreach (var a in l.WasteAreas) { using var path = p.Path(a, true); canvas.DrawPath(path, fill); }
            foreach (var d in l.WasteBrushes) canvas.DrawCircle(p.Map(d.Centre), (float)(d.Radius * k), fill);
            using var stroke = Stroke(_t.WasteFill, 1);
            foreach (var r in l.WasteRibbons) { stroke.StrokeWidth = (float)(r.Width * k); using var path = p.Path(r.Points, false); canvas.DrawPath(path, stroke); }
        }
        Stipple(canvas, canvas.DeviceClipBounds, _t.WasteStipple, spacing: 3.2f, radius: 0.45f);
        canvas.Restore();
        }

        // Water.
        using (var fill = Fill(_t.WaterFill))
        {
            canvas.SaveLayer();
            foreach (var a in l.WaterAreas) { using var path = p.Path(a, true); canvas.DrawPath(path, fill); }
            foreach (var body in l.WaterBodies)
            {
                // One even-odd path per pond so its islands stay dry.
                using var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
                foreach (var shore in body) p.AddPolyline(b, shore, true);
                using var path = b.Detach();
                canvas.DrawPath(path, fill);
            }
            using var stroke = Stroke(_t.WaterFill, 1);
            foreach (var r in l.WaterRibbons) { stroke.StrokeWidth = (float)(r.Width * k); using var path = p.Path(r.Points, false); canvas.DrawPath(path, stroke); }
            if (_t.WaterHatch is { } hatch) Hatch(canvas, canvas.DeviceClipBounds, hatch);
            canvas.Restore();
        }

        // Grass corridor: outline the union by stroking every shape, then filling every shape on top.
        FillUnion(canvas, l.Grass, p, _t.GrassFill, _t.GrassOutline, 1.1f);

        // Tee boxes: small pads at each tee marker, aligned with the hole.
        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees)
                DrawTeePad(canvas, p, tee, h.Direction);

        FillUnion(canvas, l.Greens, p, _t.GreenFill, _t.GreenOutline, 1.2f);

        // Bunkers: merged smooth outlines, or the raw areas, ribbons and painted brushes.
        if (_o.MergeBunkers)
        {
            using var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
            foreach (var loop in l.MergedBunkers(_o.BunkerMergeMetres))
            {
                b.MoveTo(p.Map(loop[0]));
                for (int i = 1; i < loop.Count; i++) b.LineTo(p.Map(loop[i]));
                b.Close();
            }
            using var merged = b.Detach();
            using var bunkerFill = Fill(_t.BunkerFill);
            canvas.DrawPath(merged, bunkerFill);
        }
        else
        using (var fill = Fill(_t.BunkerFill))
        {
            foreach (var a in l.BunkerAreas) { using var path = p.Path(a, true); canvas.DrawPath(path, fill); }
            // Brushes are painted as overlapping dabs; a slight inflate merges them into solid shapes.
            foreach (var d in l.BunkerBrushes) canvas.DrawCircle(p.Map(d.Centre), Math.Max(0.6f, (float)(d.Radius * k * 1.25)), fill);
            using var stroke = Stroke(_t.BunkerFill, 1);
            foreach (var r in l.BunkerRibbons) { stroke.StrokeWidth = Math.Max(0.8f, (float)(r.Width * k)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, stroke); }
        }

        if (_o.ShowPaths)
        {
            using var road = Stroke(_t.RoadColour, 1);
            foreach (var r in l.TrimToPlay(l.Roads, _o.PathReachMetres)) { road.StrokeWidth = Math.Max(1f, (float)(r.Width * k)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, road); }
            using var cart = Stroke(_t.PathColour, 1);
            if (_t.Kind != MapThemeKind.Modern) cart.PathEffect = SKPathEffect.CreateDash([1.5f, 1.5f], 0);
            foreach (var r in l.TrimToPlay(l.Paths, _o.PathReachMetres)) { cart.StrokeWidth = Math.Max(0.6f, (float)(r.Width * k)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, cart); }
        }

        // Ball-flight routes: back tee → aim points → green.
        using var route = Stroke(_t.RouteColour, _t.Kind != MapThemeKind.Modern ? 0.8f : 0.7f);
        if (_t.RouteDash is { } dash) route.PathEffect = SKPathEffect.CreateDash(dash, 0);
        if (IsPlanSheet) { foreach (var h in l.Holes) DrawFlightArrows(canvas, h.Route.Select(p.Map).ToList()); return; }
        foreach (var h in l.Holes) { using var path = p.Path(h.Route, false); canvas.DrawPath(path, route); }
    }

    void FillUnion(SKCanvas canvas, List<List<Vec2>> shapes, MapProjection p, SKColor fill, SKColor? outline, float width)
    {
        var paths = shapes.Select(s => p.Path(s, true)).ToList();
        if (outline is { } o)
        {
            using var stroke = Stroke(o, width * 2);
            foreach (var path in paths) canvas.DrawPath(path, stroke);
        }
        using var paint = Fill(fill);
        foreach (var path in paths) canvas.DrawPath(path, paint);
        foreach (var path in paths) path.Dispose();
    }

    void DrawTeePad(SKCanvas canvas, MapProjection p, Vec2 tee, Vec2 dir)
    {
        float k = p.PointsPerMetre;
        var c = p.Map(tee);
        var d = p.MapDirection(dir);
        float angle = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI) + 90;
        float len = Math.Max(3f, 9 * k), wid = Math.Max(2f, 5 * k);
        canvas.Save();
        canvas.Translate(c.X, c.Y);
        canvas.RotateDegrees(angle);
        var rect = new SKRect(-wid / 2, -len / 2, wid / 2, len / 2);
        using (var f = Fill(_t.TeeFill)) canvas.DrawRoundRect(rect, wid * 0.25f, wid * 0.25f, f);
        if (_t.TeeOutline is { } o) { using var s = Stroke(o, 0.7f); canvas.DrawRoundRect(rect, wid * 0.25f, wid * 0.25f, s); }
        canvas.Restore();
    }

    /// <summary>
    /// Hole numbers sit on the middle of each hole (halfway along the back-tee → green route).
    /// If a spot collides with another number, the label slides along its own hole until clear.
    /// </summary>
    void DrawHoleNumbers(SKCanvas canvas, CourseLayers l, MapProjection p, SKRect bounds)
    {
        float r = _t.NumberCircles ? 9.5f : 8.5f;
        var placed = new List<SKPoint>();
        foreach (var h in l.Holes)
        {
            var page = h.Route.Select(p.Map).ToList();
            SKPoint best = At(page, 0.5f);
            foreach (var f in new[] { 0.5f, 0.42f, 0.58f, 0.34f, 0.66f, 0.26f, 0.74f })
            {
                var c = At(page, f);
                c = new SKPoint(Math.Clamp(c.X, bounds.Left + r, bounds.Right - r), Math.Clamp(c.Y, bounds.Top + r, bounds.Bottom - r));
                best = c;
                if (placed.All(q => SKPoint.Distance(q, c) > 2.3f * r)) break;
            }
            placed.Add(best);

            string label = h.Hole.Number.ToString();
            if (_t.NumberCircles)
            {
                using (var fl = Fill(_t.Background)) canvas.DrawCircle(best, r, fl);
                using (var s = Stroke(_t.Ink, 1.1f)) canvas.DrawCircle(best, r, s);
                Text(canvas, label, best.X, best.Y + 4.2f, 11.5f, _bodyBold, _t.Ink, SKTextAlign.Center);
            }
            else
            {
                using (var fl = Fill(_t.Background.WithAlpha(235))) canvas.DrawCircle(best, r, fl);
                Text(canvas, label, best.X, best.Y + 4.6f, 13f, _bodyBold, _t.Ink, SKTextAlign.Center);
            }
        }
    }

    /// <summary>Point at fraction <paramref name="f"/> of a polyline's length.</summary>
    static SKPoint At(IReadOnlyList<SKPoint> line, float f)
    {
        float total = 0;
        for (int i = 1; i < line.Count; i++) total += SKPoint.Distance(line[i - 1], line[i]);
        float target = total * f, run = 0;
        for (int i = 1; i < line.Count; i++)
        {
            float seg = SKPoint.Distance(line[i - 1], line[i]);
            if (run + seg >= target && seg > 0)
            {
                float t = (target - run) / seg;
                return new SKPoint(line[i - 1].X + (line[i].X - line[i - 1].X) * t, line[i - 1].Y + (line[i].Y - line[i - 1].Y) * t);
            }
            run += seg;
        }
        return line[^1];
    }

    // ───────────────────────────── furniture ─────────────────────────────

    float DrawTitle(SKCanvas canvas, string name, float x, float y, float width)
    {
        bool vintage = _t.Kind != MapThemeKind.Modern;
        string title = vintage ? name.ToUpperInvariant() : name;
        float size = vintage ? 26 : 28;
        // Long names break onto a second line at the space nearest the middle, then shrink if still too wide.
        var lines = new List<string> { title };
        if (Measure(title, size, _title) > width && title.Contains(' '))
        {
            int mid = title.Length / 2;
            int cut = Enumerable.Range(0, title.Length).Where(i => title[i] == ' ').MinBy(i => Math.Abs(i - mid));
            lines = [title[..cut], title[(cut + 1)..]];
        }
        size = Math.Max(14, Math.Min(size, lines.Min(l => size * width / Math.Max(1, Measure(l, size, _title)))));
        for (int i = 0; i < lines.Count; i++) Text(canvas, lines[i], x, y + i * size * 1.12f, size, _title, _t.Ink);
        y += (lines.Count - 1) * size * 1.12f;

        if (vintage)
        {
            using var rule = Stroke(_t.Ink, 1.2f);
            canvas.DrawLine(x, y + 8, x + Math.Min(width, lines.Max(l => Measure(l, size, _title)) + 6), y + 8, rule);
            Text(canvas, _o.Subtitle.ToUpperInvariant(), x, y + 28, 12, _body, _t.MutedInk, tracking: 1.6f);
            return y + 34;
        }
        Text(canvas, _o.Subtitle, x, y + 22, 12, _body, _t.MutedInk);
        return y + 30;
    }

    /// <summary>
    /// Tee set columns (shortest → longest). Sets and yardages always come from the course file
    /// (the game's rule: tee → waypoints[1] → green); hole data only names the columns.
    /// </summary>
    (List<string> Names, Func<Hole, int, int> Yards) TeeColumns(CourseModel course)
    {
        int sets = course.Holes.Count == 0 ? 1 : course.Holes.Max(h => Math.Max(1, h.Tees.Count));
        var names = Enumerable.Range(0, sets)
            .Select(i => _o.Data?.TeeNames.ElementAtOrDefault(i) is { Length: > 0 } n ? n : sets == 1 ? "Yards" : $"Tee {i + 1}").ToList();
        int Yards(Hole h, int set) => (int)Math.Round(h.TeeSetLength(set) / Yard);
        return (names, Yards);
    }

    static SKColor? TeeColour(string name) => name.Trim().ToLowerInvariant() switch
    {
        "red" => new SKColor(0xB8, 0x3A, 0x2E), "green" => new SKColor(0x3E, 0x7A, 0x3A), "white" => SKColors.White,
        "blue" => new SKColor(0x2F, 0x5C, 0x9C), "black" => new SKColor(0x22, 0x22, 0x22), "gold" => new SKColor(0xC9, 0xA2, 0x27),
        "yellow" => new SKColor(0xE3, 0xC5, 0x2C), "silver" => new SKColor(0xB0, 0xB4, 0xB8), _ => null,
    };

    float DrawTable(SKCanvas canvas, CourseModel course, SKRect area, bool splitNines, float rowH = 15.5f, float size = 9.5f)
    {
        var (names, yards) = TeeColumns(course);
        var holes = course.Holes;

        float DrawBlock(IReadOnlyList<Hole> block, string totalLabel, SKRect r, bool grandTotal)
        {
            float y = r.Top, w = r.Width;
            float holeW = 34, parW = 30, teeW = (w - holeW - parW) / names.Count;
            float ColRight(int i) => r.Left + holeW + teeW * (i + 1) - 6;
            float parRight = r.Right - 8;
            int rows = block.Count + (grandTotal ? 3 : 2);
            using var line = Stroke(_t.Kind != MapThemeKind.Modern ? _t.Ink : _t.MutedInk.WithAlpha(160), 0.7f);
            if (_t.Kind == MapThemeKind.Modern)
            {
                using var box = Fill(_t.Background);
                canvas.DrawRect(new SKRect(r.Left, y - 2, r.Right, y + rowH * rows + 4), box);
            }
            Text(canvas, "Hole", r.Left + 5, y + rowH * 0.71f, size, _bodyBold, _t.Ink);
            for (int i = 0; i < names.Count; i++)
            {
                Text(canvas, names[i], ColRight(i), y + rowH * 0.71f, size, _bodyBold, _t.Ink, SKTextAlign.Right);
                if (TeeColour(names[i]) is { } tc)
                {
                    float tw = Measure(names[i], size, _bodyBold);
                    var dot = new SKPoint(ColRight(i) - tw - 6, y + rowH * 0.48f);
                    using (var f = Fill(tc)) canvas.DrawCircle(dot, 3.2f, f);
                    using (var ring = Stroke(_t.Ink, 0.6f)) canvas.DrawCircle(dot, 3.2f, ring);
                }
            }
            Text(canvas, "Par", parRight, y + rowH * 0.71f, size, _bodyBold, _t.Ink, SKTextAlign.Right);
            y += rowH;
            canvas.DrawLine(r.Left, y - 2, r.Right, y - 2, line);

            void Row(string label, Func<int, int> yardsFor, int par, bool bold)
            {
                var face = bold ? _bodyBold : _body;
                Text(canvas, label, bold ? r.Left + 5 : r.Left + holeW / 2, y + rowH * 0.68f, size, face, _t.Ink, bold ? SKTextAlign.Left : SKTextAlign.Center);
                for (int i = 0; i < names.Count; i++)
                    Text(canvas, yardsFor(i).ToString("N0"), ColRight(i), y + rowH * 0.68f, size, face, _t.Ink, SKTextAlign.Right);
                Text(canvas, par.ToString(), parRight, y + rowH * 0.68f, size, face, _t.Ink, SKTextAlign.Right);
                y += rowH;
            }
            foreach (var h in block) Row(h.Number.ToString(), i => yards(h, i), h.Par, false);
            canvas.DrawLine(r.Left, y - 2, r.Right, y - 2, line);
            Row(totalLabel, i => block.Sum(h => yards(h, i)), block.Sum(h => h.Par), true);
            if (grandTotal) Row("Total", i => holes.Sum(h => yards(h, i)), holes.Sum(h => h.Par), true);

            using var frame = Stroke(_t.Kind != MapThemeKind.Modern ? _t.Ink : _t.MutedInk, 0.9f);
            canvas.DrawRect(new SKRect(r.Left, r.Top - 2, r.Right, y + 2), frame);
            canvas.DrawLine(r.Left + holeW, r.Top - 2, r.Left + holeW, y + 2, line);
            canvas.DrawLine(r.Right - parW, r.Top - 2, r.Right - parW, y + 2, line);
            return y;
        }

        var front = holes.Take(9).ToList();
        var backNine = holes.Skip(9).ToList();
        if (splitNines && backNine.Count > 0)
        {
            float half = (area.Width - 16) / 2;
            DrawBlock(front, "Out", new SKRect(area.Left, area.Top, area.Left + half, area.Top), false);
            return DrawBlock(backNine, "In", new SKRect(area.Right - half, area.Top, area.Right, area.Top), true);
        }
        float yEnd = DrawBlock(front, "Out", area, false);
        if (backNine.Count > 0)
            yEnd = DrawBlock(backNine, "In", new SKRect(area.Left, yEnd + 10, area.Right, yEnd + 10), true);
        return yEnd;
    }

    void DrawFooter(SKCanvas canvas, CourseModel course, SKRect r)
    {
        var (names, yards) = TeeColumns(course);
        int longest = names.Count - 1;
        int total = course.Holes.Sum(h => yards(h, longest));
        float y = r.Top + 12;
        Text(canvas, $"Par {course.Holes.Sum(h => h.Par)}  ·  {total:N0} yards ({names[longest]})", r.Left, y, 10.5f, _body, _t.Ink); y += 16;
        if (!string.IsNullOrWhiteSpace(_o.Credit)) { Text(canvas, $"Designed by {_o.Credit}", r.Left, y, 10.5f, _italic, _t.MutedInk); y += 14; }
        Text(canvas, Attribution.Text, r.Left, y, 7.5f, _body, _t.MutedInk); y += 20;

        // Legend.
        void Swatch(Action<SKRect> draw, string label)
        {
            var box = new SKRect(r.Left, y - 9, r.Left + 22, y + 1);
            draw(box);
            Text(canvas, label, r.Left + 30, y, 9.5f, _body, _t.Ink);
            y += 15;
        }
        Swatch(b => { using var f = Fill(_t.GrassFill); canvas.DrawRoundRect(b, 3, 3, f); if (_t.GrassOutline is { } o) { using var s = Stroke(o, 0.9f); canvas.DrawRoundRect(b, 3, 3, s); } }, "Fairway / tee");
        Swatch(b => { using var f = Fill(_t.GreenFill); canvas.DrawOval(b, f); if (_t.GreenOutline is { } o) { using var s = Stroke(o, 0.9f); canvas.DrawOval(b, s); } }, "Green");
        Swatch(b => { using var f = Fill(_t.BunkerFill); canvas.DrawOval(new SKRect(b.Left + 4, b.Top + 1, b.Right - 4, b.Bottom - 1), f); }, "Bunker");
        if (_o.ShowWaste)
            Swatch(b => { canvas.Save(); canvas.ClipRect(b); canvas.SaveLayer(); using var f = Fill(_t.WasteFill); canvas.DrawRect(b, f); Stipple(canvas, b, _t.WasteStipple, 2.6f, 0.5f); canvas.Restore(); canvas.Restore(); }, "Waste area");
        Swatch(b => { using var s = Stroke(_t.RouteColour, 0.9f); if (_t.RouteDash is { } d) s.PathEffect = SKPathEffect.CreateDash(d, 0); canvas.DrawLine(b.Left, b.MidY, b.Right, b.MidY, s); }, "Ball flight");
        if (_landscape is { Trees.Count: > 0 })
            Swatch(b =>
            {
                using var shadow = Fill(_t.Ink.WithAlpha(150));
                using var crown = Fill(_t.CrownFill);
                using var edge = Stroke(_t.Ink, 0.5f);
                foreach (var (x, r) in new[] { (b.Left + 5f, 4.2f), (b.Left + 13f, 5f) })
                {
                    canvas.DrawCircle(x + r * 0.35f, b.MidY + r * 0.4f, r, shadow);
                    canvas.DrawCircle(x, b.MidY, r, crown);
                    canvas.DrawCircle(x, b.MidY, r, edge);
                }
            }, "Trees");
        if (_landscape is { HasHills: true })
            Swatch(b =>
            {
                using var s = Stroke(_t.Ink, 0.45f);
                for (float x = b.Left + 2; x < b.Right - 1; x += 2.6f) canvas.DrawLine(x, b.Top + 1, x + 0.6f, b.Bottom - 1, s);
            }, "Hills");
    }

    /// <summary>
    /// The map corner (top left, top right or bottom right; the scale bar has bottom left) with the least of the
    /// course near it, for the compass rose.
    /// </summary>
    static SKPoint RoseCorner(CourseLayers l, MapProjection p, SKRect map)
    {
        const float inset = 50;
        SKPoint[] corners = [new(map.Left + inset, map.Top + inset), new(map.Right - inset, map.Top + inset), new(map.Right - inset, map.Bottom - inset)];
        var play = l.Grass.Concat(l.Greens).SelectMany(s => s).Concat(l.Holes.SelectMany(h => h.Route)).Select(p.Map).ToList();
        return corners.MinBy(c => play.Count(q => Math.Abs(q.X - c.X) < inset + 20 && Math.Abs(q.Y - c.Y) < inset + 20));
    }

    /// <summary>A 32-point engraved compass rose turned to game north, on a parchment disc.</summary>
    void DrawCompassRose(SKCanvas canvas, MapProjection p, SKPoint at, float r)
    {
        var d = p.MapDirection(MapProjection.North);
        float north = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI + 90);
        canvas.Save();
        canvas.Translate(at);
        using var ring = Stroke(_t.Ink, 0.6f);
        ring.StrokeCap = SKStrokeCap.Butt;
        using (var disc = Fill(_t.Background)) canvas.DrawCircle(0, 0, r * 0.98f, disc);
        canvas.DrawCircle(0, 0, r * 0.98f, ring);
        canvas.DrawCircle(0, 0, r * 0.9f, ring);
        for (int i = 0; i < 32; i++)
        {
            float a = (float)(i * Math.PI / 16);
            float inner = i % 8 == 0 ? 0.8f : i % 4 == 0 ? 0.84f : 0.87f;
            canvas.DrawLine(r * inner * MathF.Sin(a), -r * inner * MathF.Cos(a), r * 0.9f * MathF.Sin(a), -r * 0.9f * MathF.Cos(a), ring);
        }
        canvas.RotateDegrees(north);
        using var dark = Fill(_t.Ink);
        using var light = Fill(_t.Background);
        using var edge = Stroke(_t.Ink, 0.4f);
        // Long cardinal points over short ordinal ones; each point half light, half dark.
        foreach (var (len, wid, deg) in new[] { (0.62f, 0.12f, 45f), (0.62f, 0.12f, 135f), (0.62f, 0.12f, 225f), (0.62f, 0.12f, 315f),
                                                (0.82f, 0.17f, 0f), (0.82f, 0.17f, 90f), (0.82f, 0.17f, 180f), (0.82f, 0.17f, 270f) })
        {
            canvas.Save();
            canvas.RotateDegrees(deg);
            using var left = new SKPath(); left.MoveTo(0, -r * len); left.LineTo(-r * wid, 0); left.LineTo(0, 0); left.Close();
            using var right = new SKPath(); right.MoveTo(0, -r * len); right.LineTo(r * wid, 0); right.LineTo(0, 0); right.Close();
            canvas.DrawPath(left, light);
            canvas.DrawPath(left, edge);
            canvas.DrawPath(right, dark);
            canvas.Restore();
        }
        Text(canvas, "N", 0, -r * 1.06f, r * 0.28f, _bodyBold, _t.Ink, SKTextAlign.Center);
        canvas.Restore();
    }

    void DrawNorthArrow(SKCanvas canvas, MapProjection p, SKPoint at)
    {
        var d = p.MapDirection(MapProjection.North);      // page direction of compass north
        var n = new SKPoint(-d.Y, d.X);
        const float len = 26, head = 8;
        var tip = new SKPoint(at.X + d.X * len / 2, at.Y + d.Y * len / 2);
        var tail = new SKPoint(at.X - d.X * len / 2, at.Y - d.Y * len / 2);
        var headBase = new SKPoint(tip.X - d.X * head, tip.Y - d.Y * head);
        using (var shaft = Stroke(_t.Ink, 1.3f)) canvas.DrawLine(tail, headBase, shaft);
        using var b = new SKPathBuilder();
        b.MoveTo(tip);
        b.LineTo(headBase.X + n.X * head * 0.45f, headBase.Y + n.Y * head * 0.45f);
        b.LineTo(headBase.X - n.X * head * 0.45f, headBase.Y - n.Y * head * 0.45f);
        b.Close();
        using var path = b.Detach();
        using (var f = Fill(_t.Ink)) canvas.DrawPath(path, f);
        Text(canvas, "N", tip.X + d.X * 9, tip.Y + d.Y * 9 + 4, 10, _bodyBold, _t.Ink, SKTextAlign.Center);
    }

    void DrawScaleBar(SKCanvas canvas, MapProjection p, SKPoint at)
    {
        float hundred = (float)(100 * Yard * p.PointsPerMetre);
        // On the Illustrated landscape the bar sits on a parchment panel so trees and hachures don't swallow it.
        if (_t.Landscape)
        {
            var panel = new SKRect(at.X - 8, at.Y - 16, at.X + 2 * hundred + 40, at.Y + 8);
            using var bg = Fill(_t.Background);
            canvas.DrawRoundRect(panel, 3, 3, bg);
            using var edge = Stroke(_t.Ink, 0.5f);
            canvas.DrawRoundRect(panel, 3, 3, edge);
        }
        using var s = Stroke(_t.Ink, 0.9f);
        canvas.DrawLine(at.X, at.Y, at.X + 2 * hundred, at.Y, s);
        for (int i = 0; i <= 2; i++) canvas.DrawLine(at.X + i * hundred, at.Y - 3, at.X + i * hundred, at.Y + 3, s);
        Text(canvas, "0", at.X, at.Y - 6, 8, _body, _t.MutedInk, SKTextAlign.Center);
        Text(canvas, "100", at.X + hundred, at.Y - 6, 8, _body, _t.MutedInk, SKTextAlign.Center);
        Text(canvas, "200 yds", at.X + 2 * hundred + 4, at.Y + 3, 8, _body, _t.MutedInk);
    }

    void DrawFrame(SKCanvas canvas, SKSize page)
    {
        using var outer = Stroke(_t.Ink, 1.6f);
        using var inner = Stroke(_t.Ink, 0.6f);
        canvas.DrawRect(new SKRect(16, 16, page.Width - 16, page.Height - 16), outer);
        canvas.DrawRect(new SKRect(21, 21, page.Width - 21, page.Height - 21), inner);
    }

    // ───────────────────────────── primitives ─────────────────────────────

    static void Stipple(SKCanvas canvas, SKRectI bounds, SKColor colour, float spacing, float radius) =>
        Stipple(canvas, new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), colour, spacing, radius);

    /// <summary>Deterministic jittered dot field, composited only onto existing layer content.</summary>
    static void Stipple(SKCanvas canvas, SKRect bounds, SKColor colour, float spacing, float radius)
    {
        using var dot = new SKPaint { Color = colour, IsAntialias = true, BlendMode = SKBlendMode.SrcATop };
        var inv = canvas.TotalMatrix.TryInvert(out var m) ? m : SKMatrix.Identity;
        var r = inv.MapRect(bounds);
        uint seed = 2166136261;
        for (float y = r.Top; y < r.Bottom; y += spacing)
            for (float x = r.Left; x < r.Right; x += spacing)
            {
                seed = (seed ^ (uint)(x * 73856093) ^ (uint)(y * 19349663)) * 16777619;
                float jx = ((seed & 0xFF) / 255f - 0.5f) * spacing * 0.8f;
                float jy = (((seed >> 8) & 0xFF) / 255f - 0.5f) * spacing * 0.8f;
                canvas.DrawCircle(x + jx, y + jy, radius, dot);
            }
    }

    static void Hatch(SKCanvas canvas, SKRectI bounds, SKColor colour)
    {
        using var line = new SKPaint { Color = colour, IsAntialias = true, StrokeWidth = 0.5f, Style = SKPaintStyle.Stroke, BlendMode = SKBlendMode.SrcATop };
        var inv = canvas.TotalMatrix.TryInvert(out var m) ? m : SKMatrix.Identity;
        var r = inv.MapRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
        for (float x = r.Left - r.Height; x < r.Right; x += 4)
        {
            canvas.DrawLine(x, r.Bottom, x + r.Height, r.Top, line);
            canvas.DrawLine(x, r.Top, x + r.Height, r.Bottom, line);
        }
    }

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

    static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor colour,
        SKTextAlign align = SKTextAlign.Left, SKColor? halo = null, float tracking = 0)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        if (tracking > 0)
        {
            var widths = text.Select(ch => font.MeasureText(ch.ToString())).ToArray();
            float total = widths.Sum() + tracking * (text.Length - 1);
            float cx = align switch { SKTextAlign.Right => x - total, SKTextAlign.Center => x - total / 2, _ => x };
            using var p = new SKPaint { Color = colour, IsAntialias = true };
            for (int i = 0; i < text.Length; i++) { canvas.DrawText(text[i].ToString(), cx, y, SKTextAlign.Left, font, p); cx += widths[i] + tracking; }
            return;
        }
        if (halo is { } h)
        {
            using var hp = new SKPaint { Color = h, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.3f, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawText(text, x, y, align, font, hp);
        }
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }
}
