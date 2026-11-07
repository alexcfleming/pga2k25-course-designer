using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Routing Plan theme, in the manner of a Beau Welling Design routing plan (credited on the sheet): the land as a
/// grey aerial (<see cref="Aerial"/>) with the golf lifted out in soft greens and white contours, tree canopy in
/// colour near the golf and grey beyond, black shot lines with target dots, labelled facilities, a title box and a
/// white scorecard box of up to four tee sets. Only the golf the holes play is drawn. Always landscape.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    public const string RoutingPlanStyleCredit = "In the style of a Beau Welling Design routing plan";

    /// <summary>Tee set columns the scorecard box has room for (the longest ones).</summary>
    public const int RoutingTeeColumns = 4;

    /// <summary>Golf shapes with a point this close (m) to a hole route are drawn; the rest of the plot is left out.</summary>
    const double RoutedReachMetres = 40;
    /// <summary>The property: the golf grown by this much (m), small pockets between holes filled in.</summary>
    const double PropertyMetres = 26;
    /// <summary>Pockets inside the property smaller than this (square page units) are filled; bigger open land stays aerial.</summary>
    const double PocketArea = 9000;
    /// <summary>Trees within this distance (m) of the property are in colour; further out they turn grey with the aerial.</summary>
    const double ColourTreeMetres = 34;
    /// <summary>Crowns are drawn this much wider than the planted canopy, so stands of trees read as masses.</summary>
    const float CrownSpread = 1.45f;
    const float RoutingMargin = 24, CardRowH = 11.2f;

    static readonly SKColor RpFairway = SKColor.Parse("#c8e4ae"), RpCanopy = SKColor.Parse("#2d5528"), RpCanopyDeep = SKColor.Parse("#22421f");
    static readonly SKColor RpCanopyLit = SKColor.Parse("#4a7a39"), RpCanopySpeck = SKColor.Parse("#6b9a4f"), RpCanopyShadow = SKColor.Parse("#14200f");
    static readonly SKColor RpGreyCanopy = SKColor.Parse("#8d8f88"), RpGreyDeep = SKColor.Parse("#7a7c75"), RpGreyLit = SKColor.Parse("#a6a8a1"), RpGreySpeck = SKColor.Parse("#b8bab3");
    static readonly SKColor RpWaterEdge = SKColor.Parse("#5f8f8e"), RpLabel = SKColor.Parse("#2c4a24"), RpCardEdge = SKColor.Parse("#8b8b85");
    static readonly SKColor[] RpScrub = [SKColor.Parse("#8a7d4c"), SKColor.Parse("#6e7a3f"), SKColor.Parse("#9a8a5c"), SKColor.Parse("#7c6a45")];

    bool IsRoutingPlan => _t.Kind == MapThemeKind.RoutingPlan;

    Aerial? _aerial;
    CourseLayers? _routedFrom, _routed;

    /// <summary>The scorecard box's width: wider for each tee set column.</summary>
    float RoutingCardWidth => Math.Max(200, 24 + 26 + 22 + 50 * Math.Min(_teeSets, RoutingTeeColumns));

    /// <summary>The golf the holes play (<see cref="CourseLayers.RoutedOnly"/>), worked out once per layer set.</summary>
    CourseLayers Routed(CourseLayers l)
    {
        if (!ReferenceEquals(l, _routedFrom)) { _routedFrom = l; _routed = l.RoutedOnly(RoutedReachMetres); }
        return _routed!;
    }

    void DrawRoutingPlan(SKCanvas canvas, string courseName, CourseModel course, CourseLayers all, MapProjection p, SKSize size)
    {
        var page = new SKRect(0, 0, size.Width, size.Height);
        var l = Routed(all);
        float k = p.PointsPerMetre;
        _aerial ??= Aerial.Build(course, l, p, page);
        _scenery ??= Scenery.Build(course, p, page);
        var scenery = _scenery;
        using var property = RoutingProperty(l, p);
        using var grass = UnionPath(l.Grass, p);
        var zone = Zones(property, grass, page);
        _contours ??= _aerial.Zoned(3, zone);

        // Ground: the grey aerial with faint contours, the property in soft green with brighter ones.
        _aerial.DrawGrey(canvas);
        Aerial.DrawContours(canvas, _contours[0], 46);
        _aerial.DrawLand(canvas, property);
        Aerial.DrawContours(canvas, _contours[1], 140);

        using (var road = Stroke(_t.RoadColour, 1))
            foreach (var r in l.Roads) { road.StrokeWidth = Math.Max(1.2f, (float)(r.Width * k)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, road); }

        DrawRoutingWater(canvas, l, p);
        if (_o.ShowWaste) DrawWaste(canvas, l, p);

        // Corridors: one light field with a darker edge line, the fairways a touch lighter, the contours carrying on.
        {
            using (var shadow = Blurred(SKColor.Parse("#3c5a2a").WithAlpha(60), 2.2f))
            { canvas.Save(); canvas.Translate(0.8f, 1.1f); canvas.DrawPath(grass, shadow); canvas.Restore(); }
            using (var fill = Fill(_t.GrassFill)) canvas.DrawPath(grass, fill);
            using (var fairways = UnionPath(l.Fairways, p))
            using (var fill = Fill(RpFairway.WithAlpha(150))) canvas.DrawPath(fairways, fill);
            Aerial.DrawContours(canvas, _contours[2], 70);
            using (var edge = Stroke(_t.GrassOutline!.Value, 1.25f)) canvas.DrawPath(grass, edge);
        }

        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees) RoutingTeePad(canvas, p, tee, h.Direction);

        var putting = PuttingGreens(all, l, p, page);
        foreach (var g in l.Greens.Concat(putting.Where(g => !l.Greens.Contains(g))))
        {
            using var path = p.Path(g, true);
            using (var shadow = Blurred(SKColor.Parse("#2a4a20").WithAlpha(70), 1.4f))
            { canvas.Save(); canvas.Translate(0.6f, 0.8f); canvas.DrawPath(path, shadow); canvas.Restore(); }
            using (var fill = Fill(_t.GreenFill)) canvas.DrawPath(path, fill);
            using (var edge = Stroke(_t.GreenOutline!.Value, 0.7f)) canvas.DrawPath(path, edge);
        }

        // Bunkers: cream with a fine edge and a shaded lip on the near side.
        using (var bunkers = BunkerPath(l, p))
        {
            using (var edge = Stroke(_t.TeeOutline!.Value, 0.9f)) canvas.DrawPath(bunkers, edge);
            using (var sand = Fill(_t.BunkerFill)) canvas.DrawPath(bunkers, sand);
            if (!bunkers.IsEmpty)
            {
                canvas.Save();
                canvas.ClipPath(bunkers, antialias: true);
                canvas.Translate(-0.7f, -0.7f);
                using var ob = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
                ob.AddRect(SKRect.Inflate(bunkers.Bounds, 4, 4));
                ob.AddPath(bunkers);
                using var outside = ob.Detach();
                using (var lip = Blurred(SKColor.Parse("#a3946c").WithAlpha(120), 0.6f)) canvas.DrawPath(outside, lip);
                canvas.Restore();
            }
        }

        if (_o.ShowPaths)
            using (var cart = Stroke(_t.PathColour, 1))
                foreach (var r in l.TrimToPlay(l.Paths, _o.PathReachMetres)) { cart.StrokeWidth = Math.Max(0.7f, (float)(r.Width * k * 0.9f)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, cart); }

        DrawRoutingPlanting(canvas, scenery, property, zone, k);
        var labels = FacilityLabels(course, putting, p, page);

        // Shot lines: back tee → aim points → green, a circled cross at every waypoint.
        using (var line = Stroke(_t.RouteColour.WithAlpha(220), 0.55f))
            foreach (var h in l.Holes) { using var path = p.Path(h.Route, false); canvas.DrawPath(path, line); }
        foreach (var h in l.Holes)
        {
            var pts = h.Route.Select(p.Map).ToList();
            for (int i = 0; i < pts.Count; i++) Target(canvas, pts[i], i == 0 ? 2.2f : 2.8f);
        }

        var title = RoutingTitleRect(courseName, size);
        var card = RoutingCardRect(course, size);
        var numbers = RoutingHoleNumbers(canvas, l, p, page, [title, card]);
        DrawFacilityLabels(canvas, labels, numbers);
        if (_o.Wind is { } wind) DrawRoutingWind(canvas, l, p, SKRect.Inflate(new SKRect(0, 0, size.Width, size.Height), -16, -16), wind, [title, card]);

        DrawRoutingTitle(canvas, courseName, title);
        DrawRoutingCard(canvas, course, p, card);
        using (var margin = Stroke(_t.Background, 10)) canvas.DrawRect(SKRect.Inflate(page, -5, -5), margin);
        using (var frame = Stroke(SKColor.Parse("#6b6b66"), 0.6f)) canvas.DrawRect(SKRect.Inflate(page, -10, -10), frame);
    }

    // ───────────────────────────── ground ─────────────────────────────

    (SKPath Minor, SKPath Major)[]? _contours;

    /// <summary>
    /// Which zone a page point is in: the aerial (0), the property's soft green (1) or a corridor (2), read off a
    /// small mask of the two paths. Contours and scrub are sorted by it, so each is drawn once.
    /// </summary>
    static Func<SKPoint, int> Zones(SKPath property, SKPath grass, SKRect page)
    {
        const float d = 1.5f;
        using var mask = new SKBitmap((int)Math.Ceiling(page.Width * d), (int)Math.Ceiling(page.Height * d), SKColorType.Gray8, SKAlphaType.Opaque);
        using (var c = new SKCanvas(mask))
        {
            c.Clear(SKColors.Black);
            c.Scale(d);
            using (var f = new SKPaint { Color = new SKColor(128, 128, 128) }) c.DrawPath(property, f);
            using (var f = new SKPaint { Color = SKColors.White }) c.DrawPath(grass, f);
        }
        var bytes = mask.Bytes;
        int stride = mask.RowBytes, w = mask.Width, h = mask.Height;
        return q =>
        {
            int x = Math.Clamp((int)(q.X * d), 0, w - 1), y = Math.Clamp((int)(q.Y * d), 0, h - 1);
            byte v = bytes[y * stride + x];
            return v < 64 ? 0 : v < 192 ? 1 : 2;
        };
    }

    /// <summary>
    /// The property the soft green shows through: grass, greens and tees grown by <see cref="PropertyMetres"/> and
    /// merged, with pockets smaller than <see cref="PocketArea"/> filled.
    /// </summary>
    SKPath RoutingProperty(CourseLayers l, MapProjection p)
    {
        using var grow = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(2 * PropertyMetres * p.PointsPerMetre), StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        var union = new SKPath();
        void Add(SKPath one)
        {
            using var fat = grow.GetFillPath(one);
            if (fat is not null && union.Op(fat, SKPathOp.Union) is { } a) { union.Dispose(); union = a; }
            if (union.Op(one, SKPathOp.Union) is { } b) { union.Dispose(); union = b; }
        }
        foreach (var s in l.Grass.Concat(l.Greens)) { using var one = p.Path(s, true); Add(one); }
        foreach (var t in l.Holes.SelectMany(h => h.Hole.Tees)) { using var tb = new SKPathBuilder(); var c = p.Map(t); tb.AddCircle(c.X, c.Y, 2); using var one = tb.Detach(); Add(one); }
        using var b = new SKPathBuilder();
        foreach (var ring in Rings(union, PocketArea)) b.AddPoly(ring, true);
        union.Dispose();
        return b.Detach();
    }

    /// <summary>A path's rings, as polygons: every outer ring, and the holes larger than <paramref name="keepHolesOver"/>.</summary>
    static List<SKPoint[]> Rings(SKPath path, double keepHolesOver)
    {
        using var it = path.CreateRawIterator();
        var pts = new SKPoint[4];
        var all = new List<SKPoint[]>();
        var current = new List<SKPoint>();
        SKPathVerb verb;
        while ((verb = it.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move: if (current.Count > 2) all.Add(current.ToArray()); current = [pts[0]]; break;
                case SKPathVerb.Line: current.Add(pts[1]); break;
                case SKPathVerb.Quad: case SKPathVerb.Conic: current.Add(pts[1]); current.Add(pts[2]); break;
                case SKPathVerb.Cubic: current.Add(pts[1]); current.Add(pts[2]); current.Add(pts[3]); break;
            }
        }
        if (current.Count > 2) all.Add(current.ToArray());
        var rings = all.Select(a => (Points: a, Area: SignedArea(a))).Where(r => Math.Abs(r.Area) > 40).ToList();
        if (rings.Count == 0) return [];
        // Path ops wind outer rings one way and holes the other; the largest ring is an outer one.
        double outer = Math.Sign(rings.MaxBy(r => Math.Abs(r.Area)).Area);
        return rings.Where(r => Math.Sign(r.Area) == outer || Math.Abs(r.Area) > keepHolesOver).Select(r => r.Points).ToList();
    }

    static double SignedArea(SKPoint[] a)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) { var p = a[i]; var q = a[(i + 1) % a.Length]; s += p.X * q.Y - q.X * p.Y; }
        return s / 2;
    }

    void DrawRoutingWater(SKCanvas canvas, CourseLayers l, MapProjection p)
    {
        float k = p.PointsPerMetre;
        using var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        foreach (var a in l.WaterAreas) p.AddPolyline(b, a, true);
        foreach (var shore in l.WaterBodies.SelectMany(body => body)) p.AddPolyline(b, shore, true);
        using var water = b.Detach();
        using (var edge = Stroke(RpWaterEdge, 1.2f)) canvas.DrawPath(water, edge);
        using (var fill = Fill(_t.WaterFill)) canvas.DrawPath(water, fill);
        using var bank = Stroke(RpWaterEdge, 1);
        using var stream = Stroke(_t.WaterFill, 1);
        foreach (var r in l.WaterRibbons)
        {
            using var path = p.Path(r.Points, false);
            bank.StrokeWidth = (float)(r.Width * k) + 1.2f; canvas.DrawPath(path, bank);
            stream.StrokeWidth = (float)(r.Width * k); canvas.DrawPath(path, stream);
        }
    }

    // ───────────────────────────── planting ─────────────────────────────

    /// <summary>
    /// Scrub (grasses, flowers, bushes) in olive inside the property and grey outside it, then the trees: in colour on
    /// and near the property, grey further out, so the canopy rings the golf.
    /// </summary>
    static void DrawRoutingPlanting(SKCanvas canvas, Scenery s, SKPath property, Func<SKPoint, int> zone, float k)
    {
        // Flecks are tiny squares gathered into one path per colour: a grass area of 170,000 plants stays a light
        // texture in the PDF as well as on the page.
        var flecks = new Dictionary<SKColor, SKPathBuilder>();
        void Fleck(SKPoint at, SKColor colour, float half)
        {
            if (!flecks.TryGetValue(colour, out var b)) flecks[colour] = b = new SKPathBuilder();
            b.AddRect(new SKRect(at.X - half, at.Y - half, at.X + half, at.Y + half));
        }
        var rnd = new Random(5);
        var grey = SKColor.Parse("#8a8a84");
        // Every other grass tuft, so a dense meadow reads as scrub, not a smudge.
        for (int i = 0; i < s.Tufts.Count; i += 2)
        {
            var colour = RpScrub[rnd.Next(RpScrub.Length)];
            Fleck(s.Tufts[i], (zone(s.Tufts[i]) > 0 ? colour : grey).WithAlpha(110), 0.55f);
        }
        foreach (var (at, _) in s.Flowers)
        {
            var colour = RpScrub[rnd.Next(RpScrub.Length)];
            Fleck(at, (zone(at) > 0 ? colour : grey).WithAlpha(150), 0.62f);
        }
        foreach (var (colour, b) in flecks)
        {
            using var path = b.Detach();
            using var paint = new SKPaint { Color = colour, IsAntialias = true };
            canvas.DrawPath(path, paint);
            b.Dispose();
        }

        using var bush = Fill(SKColor.Parse("#5d7a3c").WithAlpha(200));
        using var bushLit = Fill(SKColor.Parse("#7f9a55").WithAlpha(200));
        using var greyBush = Fill(SKColor.Parse("#7b7b75").WithAlpha(200));
        using var greyBushLit = Fill(SKColor.Parse("#a3a39d").WithAlpha(200));
        foreach (var (at, r) in s.Bushes)
        {
            bool inside = zone(at) > 0;
            canvas.DrawCircle(at, r * 0.8f, inside ? bush : greyBush);
            canvas.DrawCircle(at.X - r * 0.2f, at.Y - r * 0.2f, r * 0.4f, inside ? bushLit : greyBushLit);
        }
        using var near = Grow(property, (float)(ColourTreeMetres * k));
        DrawCanopy(canvas, s.Trees.Where(t => !near.Contains(t.At.X, t.At.Y)).ToList(), colour: false);
        DrawCanopy(canvas, s.Trees.Where(t => near.Contains(t.At.X, t.At.Y)).ToList(), colour: true);
    }

    static SKPath Grow(SKPath path, float by)
    {
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = by * 2, StrokeJoin = SKStrokeJoin.Round };
        using var fat = stroke.GetFillPath(path);
        return (fat is null ? null : path.Op(fat, SKPathOp.Union)) ?? new SKPath(path);
    }

    /// <summary>Overlapping lobed crowns with one shadow layer under them, dappled dark on the lower right and lit on the upper left.</summary>
    static void DrawCanopy(SKCanvas canvas, List<Scenery.Tree> trees, bool colour)
    {
        if (trees.Count == 0) return;
        // One blurred layer for all the shadows (a blur per tree would be a picture per tree in the PDF).
        using (var layer = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(2.2f, 2.2f), Color = SKColors.Black.WithAlpha(colour ? (byte)135 : (byte)70) })
        using (var shadow = Fill(RpCanopyShadow))
        {
            canvas.SaveLayer(layer);
            foreach (var t in trees) canvas.DrawCircle(t.At.X + t.Radius * 0.9f, t.At.Y + t.Radius, t.Radius * CrownSpread, shadow);
            canvas.Restore();
        }
        using var body = Fill(colour ? RpCanopy : RpGreyCanopy);
        using var deep = Fill((colour ? RpCanopyDeep : RpGreyDeep).WithAlpha(170));
        using var lit = Fill((colour ? RpCanopyLit : RpGreyLit).WithAlpha(200));
        using var speck = Fill((colour ? RpCanopySpeck : RpGreySpeck).WithAlpha(180));
        foreach (var t in trees)
        {
            float r = t.Radius * CrownSpread;
            uint turn = (uint)(t.At.X * 7919 + t.At.Y * 104729) % 7;
            for (int i = 0; i < 6; i++)
            {
                double a = (i + turn / 7.0) * Math.PI / 3;
                canvas.DrawCircle(t.At.X + (float)Math.Cos(a) * r * 0.38f, t.At.Y + (float)Math.Sin(a) * r * 0.38f, r * 0.6f, body);
            }
        }
        foreach (var t in trees) { float r = t.Radius * CrownSpread; canvas.DrawCircle(t.At.X + r * 0.3f, t.At.Y + r * 0.32f, r * 0.42f, deep); }
        foreach (var t in trees) { float r = t.Radius * CrownSpread; canvas.DrawCircle(t.At.X - r * 0.25f, t.At.Y - r * 0.28f, r * 0.4f, lit); }
        foreach (var t in trees) { float r = t.Radius * CrownSpread; if (r > 2.5f) canvas.DrawCircle(t.At.X - r * 0.38f, t.At.Y - r * 0.4f, r * 0.15f, speck); }
    }

    // ───────────────────────────── marks and labels ─────────────────────────────

    void RoutingTeePad(SKCanvas canvas, MapProjection p, Vec2 tee, Vec2 dir)
    {
        float k = p.PointsPerMetre;
        var c = p.Map(tee);
        var d = p.MapDirection(dir);
        float angle = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI) + 90;
        float len = Math.Max(3.4f, 10 * k), wid = Math.Max(2.4f, 6 * k), corner = wid * 0.3f;
        canvas.Save();
        canvas.Translate(c.X, c.Y);
        canvas.RotateDegrees(angle);
        var rect = new SKRect(-wid / 2, -len / 2, wid / 2, len / 2);
        using (var shadow = Blurred(SKColors.Black.WithAlpha(50), 0.8f)) canvas.DrawRoundRect(new SKRect(rect.Left + 0.5f, rect.Top + 0.6f, rect.Right + 0.5f, rect.Bottom + 0.6f), corner, corner, shadow);
        using (var fill = Fill(_t.TeeFill)) canvas.DrawRoundRect(rect, corner, corner, fill);
        using (var edge = Stroke(_t.TeeOutline!.Value, 0.4f)) canvas.DrawRoundRect(rect, corner, corner, edge);
        canvas.Restore();
    }

    void Target(SKCanvas canvas, SKPoint at, float r)
    {
        using (var fill = Fill(SKColors.White.WithAlpha(150))) canvas.DrawCircle(at, r, fill);
        using var s = Stroke(_t.Ink, 0.45f);
        canvas.DrawCircle(at, r, s);
        canvas.DrawLine(at.X - r * 0.6f, at.Y, at.X + r * 0.6f, at.Y, s);
        canvas.DrawLine(at.X, at.Y - r * 0.6f, at.X, at.Y + r * 0.6f, s);
    }

    /// <summary>
    /// White circled numbers beside each shot line, about a third of the way along: the first spot (either side, then
    /// further along) clear of the other numbers and of the title and scorecard boxes. Returns the centres.
    /// </summary>
    List<SKPoint> RoutingHoleNumbers(SKCanvas canvas, CourseLayers l, MapProjection p, SKRect page, SKRect[] avoid)
    {
        const float r = 9.5f;
        var placed = new List<SKPoint>();
        foreach (var h in l.Holes)
        {
            var line = h.Route.Select(p.Map).ToList();
            SKPoint? first = null, best = null;
            foreach (var f in new[] { 0.32f, 0.42f, 0.24f, 0.52f, 0.6f, 0.16f, 0.7f })
            {
                var (at, dir) = AlongWithDirection(line, f);
                foreach (int side in new[] { 1, -1 })
                {
                    var c = new SKPoint(at.X - dir.Y * side * (r + 3), at.Y + dir.X * side * (r + 3));
                    c = new SKPoint(Math.Clamp(c.X, page.Left + 16 + r, page.Right - 16 - r), Math.Clamp(c.Y, page.Top + 16 + r, page.Bottom - 16 - r));
                    first ??= c;
                    if (placed.All(q => SKPoint.Distance(q, c) > 2.3f * r) && avoid.All(a => !SKRect.Inflate(a, r, r).Contains(c))) { best = c; break; }
                }
                if (best is not null) break;
            }
            var spot = best ?? first ?? line[0];
            placed.Add(spot);
            using (var shadow = Blurred(SKColors.Black.WithAlpha(70), 1.2f)) canvas.DrawCircle(spot.X + 0.6f, spot.Y + 0.9f, r, shadow);
            using (var fill = Fill(SKColors.White)) canvas.DrawCircle(spot, r, fill);
            using (var ring = Stroke(_t.Ink, 1)) canvas.DrawCircle(spot, r, ring);
            Text(canvas, h.Hole.Number.ToString(), spot.X, spot.Y + 4.3f, 12, _bodyBold, _t.Ink, SKTextAlign.Center);
        }
        return placed;
    }

    /// <summary>The point at fraction <paramref name="f"/> of a polyline's length and the unit direction there.</summary>
    static (SKPoint At, SKPoint Dir) AlongWithDirection(IReadOnlyList<SKPoint> line, float f)
    {
        float total = 0;
        for (int i = 1; i < line.Count; i++) total += SKPoint.Distance(line[i - 1], line[i]);
        float target = total * f, run = 0;
        for (int i = 1; i < line.Count; i++)
        {
            float seg = SKPoint.Distance(line[i - 1], line[i]);
            if (seg > 0 && run + seg >= target)
            {
                float t = (target - run) / seg;
                var dir = new SKPoint((line[i].X - line[i - 1].X) / seg, (line[i].Y - line[i - 1].Y) / seg);
                return (new SKPoint(line[i - 1].X + (line[i].X - line[i - 1].X) * t, line[i - 1].Y + (line[i].Y - line[i - 1].Y) * t), dir);
            }
            run += seg;
        }
        return (line[^1], new SKPoint(1, 0));
    }

    /// <summary>
    /// Up to two greens that no hole plays, near the first tee or the last green: the putting greens. They are drawn
    /// with the golf even when they lie out of reach of the routes.
    /// </summary>
    static List<List<Vec2>> PuttingGreens(CourseLayers all, CourseLayers routed, MapProjection p, SKRect page)
    {
        if (routed.Holes.Count == 0) return [];
        float k = p.PointsPerMetre;
        var anchors = routed.Holes.Select(h => p.Map(h.Hole.GreenAnchor)).ToList();
        var home = new[] { p.Map(routed.Holes[0].BackTee), p.Map(routed.Holes[^1].Hole.GreenAnchor) };
        var spare = new List<(List<Vec2> Shape, SKPoint Mid)>();
        foreach (var g in all.Greens)
        {
            using var path = p.Path(g, true);
            var box = path.Bounds;
            var mid = new SKPoint(box.MidX, box.MidY);
            if (anchors.Any(a => path.Contains(a.X, a.Y) || SKPoint.Distance(a, mid) < 40 * k)) continue;
            if (box.Width * box.Height < 150 * k * k || !page.Contains(mid)) continue;
            if (spare.Any(s => SKPoint.Distance(s.Mid, mid) < 50)) continue;
            spare.Add((g, mid));
        }
        float Home(SKPoint m) => home.Min(q => SKPoint.Distance(q, m));
        return spare.Where(s => Home(s.Mid) < 160 * k).OrderBy(s => Home(s.Mid)).Take(2).Select(s => s.Shape).ToList();
    }

    /// <summary>
    /// Facility labels: Putting Green under each putting green, and the clubhouse, maintenance and range buildings
    /// (drawn as small roofs; one label of each kind).
    /// </summary>
    List<(string Text, SKPoint At)> FacilityLabels(CourseModel course, List<List<Vec2>> putting, MapProjection p, SKRect page)
    {
        float k = p.PointsPerMetre;
        var labels = new List<(string, SKPoint)>();
        foreach (var g in putting)
        {
            using var path = p.Path(g, true);
            labels.Add(("PUTTING GREEN", new SKPoint(path.Bounds.MidX, path.Bounds.Bottom + 8)));
        }
        foreach (var o in course.PlacedObjects.Where(o => o.AssetPath.Contains("/Buildings/")))
        {
            var at = p.Map(o.Position);
            if (!page.Contains(at)) continue;
            string? kind = o.AssetPath.Contains("/Clubhouses/") ? "CLUBHOUSE"
                : o.AssetPath.Contains("Maintenance", StringComparison.OrdinalIgnoreCase) ? "MAINTENANCE"
                : o.AssetPath.Contains("RangeShelter", StringComparison.OrdinalIgnoreCase) ? "RANGE" : null;
            float s = (float)Math.Max(5, 9 * k * o.Scale);
            _roofs.Add(new SKRect(at.X - s / 2, at.Y - s / 2, at.X + s / 2, at.Y - s / 2 + s * 0.7f));
            if (kind is not null && !labels.Any(q => q.Item1 == kind)) labels.Add((kind, new SKPoint(at.X, at.Y - s / 2 - 4)));
        }
        return labels;
    }

    readonly List<SKRect> _roofs = [];

    /// <summary>Building roofs, then the labels in small spaced capitals, stepped clear of hole numbers and each other.</summary>
    void DrawFacilityLabels(SKCanvas canvas, List<(string Text, SKPoint At)> labels, List<SKPoint> numbers)
    {
        foreach (var roof in _roofs)
        {
            using (var shadow = Blurred(SKColors.Black.WithAlpha(80), 1.2f)) canvas.DrawRect(new SKRect(roof.Left + 1.2f, roof.Top + 1.4f, roof.Right + 1.2f, roof.Bottom + 1.4f), shadow);
            using (var fill = Fill(SKColor.Parse("#f4f2ec"))) canvas.DrawRect(roof, fill);
            using (var edge = Stroke(SKColor.Parse("#8c8a82"), 0.5f)) canvas.DrawRect(roof, edge);
        }
        _roofs.Clear();
        const float size = 6.2f, tracking = 0.6f;
        var taken = numbers.Select(n => new SKRect(n.X - 11, n.Y - 11, n.X + 11, n.Y + 11)).ToList();
        foreach (var (text, at) in labels)
        {
            float half = MeasureTracked(text, size, _bodyBold, tracking) / 2 + 2;
            SKRect Box(SKPoint q) => new(q.X - half, q.Y - size - 1, q.X + half, q.Y + 2);
            // Try the spot, then step down and up in turn until the label is clear of numbers and other labels.
            var spot = at;
            foreach (float dy in new[] { 0f, 9, -9, 18, -18, 27, -27 })
            {
                var q = new SKPoint(at.X, at.Y + dy);
                if (taken.All(t => !t.IntersectsWith(Box(q)))) { spot = q; break; }
            }
            taken.Add(Box(spot));
            LabelText(canvas, text, spot, size, tracking);
        }
    }

    /// <summary>Spaced small capitals centred on <paramref name="at"/>, on a white halo so they read over canopy and contours.</summary>
    void LabelText(SKCanvas canvas, string text, SKPoint at, float size, float tracking)
    {
        using var font = new SKFont(_bodyBold, size) { Subpixel = true };
        using var halo = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.4f, StrokeJoin = SKStrokeJoin.Round };
        using var ink = new SKPaint { Color = RpLabel, IsAntialias = true };
        var widths = text.Select(ch => font.MeasureText(ch.ToString())).ToArray();
        float x0 = at.X - (widths.Sum() + tracking * (text.Length - 1)) / 2;
        foreach (var paint in new[] { halo, ink })
        {
            float x = x0;
            for (int i = 0; i < text.Length; i++) { canvas.DrawText(text[i].ToString(), x, at.Y, SKTextAlign.Left, font, paint); x += widths[i] + tracking; }
        }
    }

    void DrawRoutingWind(SKCanvas canvas, CourseLayers l, MapProjection p, SKRect map, WindRose wind, SKRect[] avoid)
    {
        const float w = 150, h = 132;
        var box = RoseSpots.Select(f => SpotBox(map, f, w, h))
            .Where(b => avoid.All(a => SKRect.Intersect(SKRect.Inflate(a, 6, 6), b).IsEmpty))
            .DefaultIfEmpty(SpotBox(map, (0, 1), w, h))
            .MinBy(b => PlayUnder(l, p, b));
        RoutingCard(canvas, box);
        WindRoseDrawing.Draw(canvas, SKRect.Inflate(box, -6, -6), wind, _body, _t.Ink, _t.Background, 0.7f);
    }

    // ───────────────────────────── title and scorecard ─────────────────────────────

    const float TitleSize = 17, TitleTracking = 2.2f, TitleSeal = 44;

    SKRect RoutingTitleRect(string name, SKSize page)
    {
        float seal = _logo is null ? 0 : TitleSeal + 12;
        float maxName = page.Width * 0.55f;
        float nameW = Math.Min(maxName, MeasureTracked(name.ToUpperInvariant(), TitleSize, _title, TitleTracking));
        return new SKRect(RoutingMargin, RoutingMargin, RoutingMargin + 28 + seal + Math.Max(nameW, 170), RoutingMargin + 58);
    }

    void DrawRoutingTitle(SKCanvas canvas, string courseName, SKRect r)
    {
        RoutingCard(canvas, r);
        float x = r.Left + 14;
        if (_logo is { } logo)
        {
            GreensBook.CourseLogoArt.Draw(canvas, logo, new SKRect(x, r.MidY - TitleSeal / 2, x + TitleSeal, r.MidY + TitleSeal / 2));
            x += TitleSeal + 12;
        }
        string name = courseName.ToUpperInvariant();
        float room = r.Right - 14 - x, size = TitleSize;
        while (size > 9 && MeasureTracked(name, size, _title, TitleTracking * size / TitleSize) > room) size -= 0.25f;
        Text(canvas, name, x, r.Top + 25, size, _title, _t.Ink, tracking: TitleTracking * size / TitleSize);
        using (var rule = Stroke(_t.Ink, 0.5f)) canvas.DrawLine(x, r.Top + 32, r.Right - 14, r.Top + 32, rule);
        string sub = $"ROUTING PLAN  ·  {_o.Subtitle.ToUpperInvariant()}";
        float subSize = 7.2f;
        while (subSize > 5 && MeasureTracked(sub, subSize, _body, 1.4f) > room) subSize -= 0.2f;
        Text(canvas, sub, x, r.Top + 45, subSize, _body, _t.MutedInk, tracking: 1.4f);
    }

    /// <summary>Card rows: a row per hole, then Total on nine holes, or Out, In and Total on more.</summary>
    static int CardRows(int holes) => holes + (holes > 9 ? 3 : 1);

    SKRect RoutingCardRect(CourseModel course, SKSize page) => RoutingCardRect(course.Holes.Count, page);

    SKRect RoutingCardRect(int holes, SKSize page)
    {
        float footer = 20 + 32 + (string.IsNullOrWhiteSpace(_o.Credit) ? 0 : 10) + 14;
        float h = 46 + CardRows(holes) * CardRowH + 3 + footer;
        return new SKRect(page.Width - RoutingMargin - RoutingCardWidth, page.Height - RoutingMargin - h, page.Width - RoutingMargin, page.Height - RoutingMargin);
    }

    SKRect _routingRect;

    /// <summary>
    /// Where the course goes: left of the scorecard box at full height, the same below the title box, or the full width
    /// below the title box. Each is tried at its best rotation and the largest map wins whose course stays clear of the
    /// title and scorecard boxes (the title box is taken at a typical width, as the name is not known yet).
    /// </summary>
    (SKRect Rect, double Rotation) RoutingFit(CourseLayers layers, List<Vec2> pts, SKSize page)
    {
        var card = SKRect.Inflate(RoutingCardRect(layers.Holes.Count, page), 6, 6);
        var title = new SKRect(RoutingMargin, RoutingMargin, RoutingMargin + page.Width * 0.4f, RoutingMargin + 64);
        float left = 36, right = page.Width - RoutingMargin - RoutingCardWidth - 18, bottom = page.Height - 36, below = title.Bottom + 8;
        var candidates = new[]
        {
            new SKRect(left, 36, right, bottom),
            new SKRect(left, below, right, bottom),
            new SKRect(left, below, page.Width - 36, bottom),
            new SKRect(left, 36, page.Width - 36, bottom),
        };
        (SKRect, double)? best = null;
        double bestScore = -1;
        foreach (var rect in candidates)
        {
            double rot = _o.AutoRotate ? MapProjection.BestRotation(pts, rect) : MapProjection.NorthUpRotation;
            var proj = MapProjection.Fit(pts, rect, rot);
            int hits = pts.Select(proj.Map).Count(q => card.Contains(q) || title.Contains(q));
            // A few points under a box can be lived with (a tee tucked in a corner); many cannot.
            double score = proj.PointsPerMetre * (hits == 0 ? 1 : hits <= pts.Count / 200 ? 0.9 : 0.5);
            if (score > bestScore) { bestScore = score; best = (rect, rot); }
        }
        return best!.Value;
    }

    /// <summary>
    /// The scorecard box: Hole, a yardage column per tee set (the longest four, longest first, headed by the Holes
    /// card's names, else Back and Front on two sets or Tee n), Par; then a bar scale, the contour interval, a north
    /// arrow and the credits.
    /// </summary>
    void DrawRoutingCard(SKCanvas canvas, CourseModel course, MapProjection p, SKRect r)
    {
        RoutingCard(canvas, r);
        var (names, yards) = TeeColumns(course);
        var sets = StripTeeSets(names.Count).Take(RoutingTeeColumns).ToList();
        bool named = (_o.Holes?.Tees ?? []).Count > 0;
        string Head(int set) => named || names.Count > 2 ? names[set].ToUpperInvariant()
            : names.Count == 2 ? (set == 1 ? "BACK TEE" : "FRONT TEE") : "";

        float x = r.Left + 12, w = r.Width - 24, y = r.Top + 18;
        Text(canvas, "SCORECARD", x, y, 8.5f, _bodyBold, _t.Ink, tracking: 1.6f);
        y += 6;
        using var rule = Stroke(_t.Ink, 0.6f);
        using var thin = Stroke(_t.Ink.WithAlpha(90), 0.35f);
        canvas.DrawLine(x, y, x + w, y, rule);

        const float holeW = 26, parW = 22, head = 5.6f, figure = 7.4f;
        float teeW = (w - holeW - parW) / sets.Count;
        float Col(int i) => x + holeW + teeW * (i + 1) - 4;
        float parRight = x + w;
        y += 10;
        Text(canvas, "HOLE", x + 2, y + 4, head, _body, _t.Ink, tracking: 0.4f);
        for (int i = 0; i < sets.Count; i++)
        {
            string top = Head(sets[i]);
            float hs = head;
            while (hs > 4 && MeasureTracked(top, hs, _body, 0.4f) > teeW - 6) hs -= 0.2f;
            if (top.Length == 0) Text(canvas, "YARDAGE", Col(i), y + 4, head, _body, _t.Ink, SKTextAlign.Right, tracking: 0.4f);
            else
            {
                Text(canvas, top, Col(i), y, hs, _body, _t.Ink, SKTextAlign.Right, tracking: 0.4f);
                Text(canvas, "YARDAGE", Col(i), y + 7, head, _body, _t.Ink, SKTextAlign.Right, tracking: 0.4f);
                if (TeeColour(names[sets[i]]) is { } colour)
                {
                    var dot = new SKPoint(Col(i) - MeasureTracked(top, hs, _body, 0.4f) - 4.5f, y - 2);
                    using (var f = Fill(colour)) canvas.DrawCircle(dot, 2.2f, f);
                    using (var ring = Stroke(_t.Ink, 0.4f)) canvas.DrawCircle(dot, 2.2f, ring);
                }
            }
        }
        Text(canvas, "PAR", parRight, y + 4, head, _body, _t.Ink, SKTextAlign.Right, tracking: 0.4f);
        y += 12;
        canvas.DrawLine(x, y - 3, x + w, y - 3, thin);

        void Row(string label, Func<int, int> yardsFor, int par, bool bold)
        {
            var face = bold ? _bodyBold : _body;
            y += CardRowH;
            Text(canvas, label, bold ? x + 2 : x + 10, y - 3, figure, face, _t.Ink, bold ? SKTextAlign.Left : SKTextAlign.Center);
            for (int i = 0; i < sets.Count; i++) Text(canvas, yardsFor(sets[i]).ToString("N0"), Col(i), y - 3, figure, face, _t.Ink, SKTextAlign.Right);
            Text(canvas, par.ToString(), parRight, y - 3, figure, face, _t.Ink, SKTextAlign.Right);
        }
        void Block(IReadOnlyList<Hole> holes, string label)
        {
            foreach (var h in holes) Row(h.Number.ToString(), s => yards(h, s), h.Par, false);
            canvas.DrawLine(x, y + 0.5f, x + w, y + 0.5f, thin);
            Row(label, s => holes.Sum(h => yards(h, s)), holes.Sum(h => h.Par), true);
        }
        var front = course.Holes.Take(9).ToList();
        var back = course.Holes.Skip(9).ToList();
        if (back.Count == 0) Block(front, "TOTAL");
        else
        {
            Block(front, "OUT");
            Block(back, "IN");
            canvas.DrawLine(x, y + 0.5f, x + w, y + 0.5f, rule);
            Row("TOTAL", s => course.Holes.Sum(h => yards(h, s)), course.Holes.Sum(h => h.Par), true);
        }
        canvas.DrawLine(x, y + 3, x + w, y + 3, rule);

        // Bar scale: three segments of the largest round step that fits.
        y += 20;
        float yard = (float)(Yard * p.PointsPerMetre);
        int step = new[] { 100, 50, 25, 10 }.FirstOrDefault(s => 3 * s * yard <= w - 70, 10);
        float unit = step * yard;
        using (var dark = Fill(_t.Ink))
        using (var edge = Stroke(_t.Ink, 0.5f))
            for (int i = 0; i < 3; i++)
            {
                var seg = new SKRect(x + i * unit, y, x + (i + 1) * unit, y + 3);
                if (i % 2 == 0) canvas.DrawRect(seg, dark);
                canvas.DrawRect(seg, edge);
                Text(canvas, (i * step).ToString(), x + i * unit, y + 10, 5.4f, _body, _t.Ink, SKTextAlign.Center);
            }
        Text(canvas, $"{3 * step} YDS", x + 3 * unit, y + 10, 5.4f, _body, _t.Ink, SKTextAlign.Center);
        double interval = _aerial?.Interval ?? 1;
        Text(canvas, $"Contours at {interval:0.#} m", x, y + 20, 5.6f, _italic, _t.MutedInk);
        RoutingNorthArrow(canvas, p, new SKPoint(r.Right - 22, y + 4));

        y += 32;
        Text(canvas, RoutingPlanStyleCredit, x, y, 5.8f, _italic, _t.MutedInk);
        if (!string.IsNullOrWhiteSpace(_o.Credit)) { y += 10; Text(canvas, $"Designed by {_o.Credit}", x, y, 6.6f, _italic, _t.Ink); }
        y += 10;
        float attrSize = 5.2f;
        while (attrSize > 4 && Measure(Attribution.Text, attrSize, _body) > w) attrSize -= 0.2f;
        Text(canvas, Attribution.Text, x, y, attrSize, _body, _t.MutedInk);
    }

    void RoutingNorthArrow(SKCanvas canvas, MapProjection p, SKPoint at)
    {
        var d = p.MapDirection(MapProjection.North);
        float degrees = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI + 90);
        canvas.Save();
        canvas.Translate(at);
        canvas.RotateDegrees(degrees);
        using var ab = new SKPathBuilder();
        ab.MoveTo(0, -11); ab.LineTo(4, 6); ab.LineTo(0, 3); ab.LineTo(-4, 6); ab.Close();
        using var arrow = ab.Detach();
        using (var fill = Fill(_t.Ink)) canvas.DrawPath(arrow, fill);
        Text(canvas, "N", 0, -14, 7, _bodyBold, _t.Ink, SKTextAlign.Center);
        canvas.Restore();
    }

    /// <summary>A white box with a soft shadow and a fine inner rule, laid on the map.</summary>
    void RoutingCard(SKCanvas canvas, SKRect r)
    {
        using (var shadow = Blurred(SKColors.Black.WithAlpha(60), 3)) canvas.DrawRect(new SKRect(r.Left + 1.5f, r.Top + 2, r.Right + 1.5f, r.Bottom + 2), shadow);
        using (var fill = Fill(_t.Background)) canvas.DrawRect(r, fill);
        using (var edge = Stroke(RpCardEdge, 0.4f)) canvas.DrawRect(SKRect.Inflate(r, -3, -3), edge);
    }

    static SKPaint Blurred(SKColor c, float sigma) => new() { Color = c, IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) };

    static float MeasureTracked(string text, float size, SKTypeface face, float tracking)
    {
        using var font = new SKFont(face, size);
        return text.Sum(ch => font.MeasureText(ch.ToString())) + tracking * Math.Max(0, text.Length - 1);
    }
}
