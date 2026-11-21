using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// RGD Style 2: a grassing plan after Renaissance Golf Design's (credited on the sheet). Light survey contours in rose
/// across the whole sheet; the playing corridor hatched in fine horizontal lines (fairway / rough); fairways in a green
/// wash with a dark edge; greens checkered (putting surfaces); tees as pink ovals; ochre bunkers; red shot lines with
/// dots and red italic hole numbers; an italic "A Grassing Plan for …" title block and a grassing legend in the quietest
/// corners, and a small ruled card. Landscape or portrait, whichever draws the course larger.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    public const string GrassingStyleCredit = "Inspired by RGD";

    bool IsGrassing => _t.Kind == MapThemeKind.GrassingPlan;

    static readonly SKColor GPContour = SKColor.Parse("#b9849f"), GPContourMajor = SKColor.Parse("#9a5f82");
    static readonly SKColor GPHatch = SKColor.Parse("#8f8a96"), GPProperty = SKColor.Parse("#8a6fb0");
    static readonly SKColor GPFairway = SKColor.Parse("#9cc271"), GPFairwayLight = SKColor.Parse("#c9df9f"), GPFairwayEdge = SKColor.Parse("#2f6a2c");
    static readonly SKColor GPGreen = SKColor.Parse("#2f6a33"), GPGreenLight = SKColor.Parse("#5f9a52"), GPGreenEdge = SKColor.Parse("#173d1b");
    static readonly SKColor GPTee = SKColor.Parse("#ecc7d2"), GPTeeEdge = SKColor.Parse("#5b2a3d");
    static readonly SKColor GPSand = SKColor.Parse("#dcb565"), GPSandEdge = SKColor.Parse("#8c6a2a");
    static readonly SKColor GPWater = SKColor.Parse("#d6dff0"), GPSea = SKColor.Parse("#c9b4dc");
    static readonly SKColor GPRed = SKColor.Parse("#b3312c"), GPPlum = SKColor.Parse("#6b2c5a");

    static SKRect GrassingMapRect(SKSize page) => new(26, 26, page.Width - 26, page.Height - 26);

    static readonly (float X, float Y)[] GrassingCorners = [(0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f)];
    static readonly (float X, float Y)[] GrassingCardSpots = [(0f, 1f), (1f, 1f), (0f, 0f), (1f, 0f), (0.5f, 1f), (0.5f, 0f)];
    float GrassingNameSize(string courseName) => Math.Min(28, 28 * 240 / Math.Max(1, Measure(courseName, 28, Serif)));
    float GrassingTitleHeight(string courseName) => 30 + 22 + 16 + GrassingNameSize(courseName) + 10 + PlaceList().Count * 16 + 18 + 26 + 30;

    /// <summary>Title, legend, card (each on a paper patch 4 units bigger all round, inside the boxes' gap).</summary>
    List<BoxAsk> GrassingBoxes(string courseName) =>
        [new(260, GrassingTitleHeight(courseName), GrassingCorners), new(230, 150, GrassingCorners), new(210, 132, GrassingCardSpots)];

    void DrawGrassingPlan(SKCanvas canvas, string courseName, CourseModel course, CourseLayers l, MapProjection p, SKSize page, SKRect map)
    {
        float k = p.PointsPerMetre;
        _aerial ??= Aerial.Build(course, l, p, map, maxContours: 45);
        _landscape ??= Landscape.Build(course, p, map);
        var contours = _aerial.Zoned(1, _ => 0)[0];
        using var grass = UnionPath(l.Grass, p);
        using var fairways = UnionPath(l.Fairways, p);
        using var water = WaterPath(l, p);
        var parcel = ParcelOf(l);
        using var property = parcel.Count >= 3 ? p.Path(parcel, true) : null;

        canvas.Save();
        canvas.ClipRect(map);

        // The survey: rose contours over everything, every fifth heavier; the sea in pale violet strokes.
        using (var sea = Stroke(GPSea, 0.9f))
            foreach (var (sy, x0, x1) in _landscape.WaterLines) canvas.DrawLine(x0, sy, x1, sy, sea);
        using (var thin = Stroke(GPContour.WithAlpha(120), 0.3f)) canvas.DrawPath(contours.Minor, thin);
        using (var heavy = Stroke(GPContourMajor.WithAlpha(150), 0.5f)) canvas.DrawPath(contours.Major, heavy);
        contours.Minor.Dispose(); contours.Major.Dispose();
        if (property is not null) { using var line = Stroke(GPProperty.WithAlpha(200), 0.8f); canvas.DrawPath(property, line); }

        // Fairway / rough: the corridor hatched in fine horizontal lines on white, with a thin edge.
        using (var paper = Fill(_t.Background.WithAlpha(215))) canvas.DrawPath(grass, paper);
        canvas.Save();
        canvas.ClipPath(grass, antialias: true);
        using (var hatch = Stroke(GPHatch.WithAlpha(170), 0.3f))
        using (var lines = new SKPathBuilder())
        {
            var gb = SKRect.Intersect(grass.Bounds, map);
            for (float hy = gb.Top; hy < gb.Bottom; hy += 2.1f) { lines.MoveTo(gb.Left, hy); lines.LineTo(gb.Right, hy); }
            using var all = lines.Detach();
            canvas.DrawPath(all, hatch);
        }
        canvas.Restore();
        using (var edge = Stroke(GPHatch.WithAlpha(200), 0.4f)) canvas.DrawPath(grass, edge);

        // Water.
        if (!water.IsEmpty)
        {
            using (var f = Fill(GPWater)) canvas.DrawPath(water, f);
            using var bank = Stroke(GPProperty, 0.7f); canvas.DrawPath(water, bank);
        }

        // Fairways: a green wash, lighter in the middle, a dark pen edge and a fine stipple.
        using (var f = Fill(GPFairway)) canvas.DrawPath(fairways, f);
        canvas.Save();
        canvas.ClipPath(fairways, antialias: true);
        using (var shade = Stroke(GPFairwayEdge.WithAlpha(70), 7f)) canvas.DrawPath(fairways, shade);
        using (var light = new SKPaint { IsAntialias = true, Color = GPFairwayLight.WithAlpha(150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) })
        {
            canvas.Save(); canvas.Translate(-0.6f, -0.6f);
            using var inner = new SKPath();
            using (var inset = Stroke(SKColors.Black, 9f)) inset.GetFillPath(fairways, inner);
            using var middle = fairways.Op(inner, SKPathOp.Difference);
            if (middle is not null) canvas.DrawPath(middle, light);
            canvas.Restore();
        }
        canvas.Restore();
        using (var speck = TileShader(30, 160, 61, (c, at, r) => { using var d = Fill(GPFairwayEdge.WithAlpha((byte)r.Next(60, 140))); c.DrawCircle(at, 0.28f, d); }))
            FillTexture(canvas, fairways, speck);
        using (var edge = Stroke(GPFairwayEdge, 0.9f)) { edge.PathEffect = Wobble(0.35f, 13); canvas.DrawPath(fairways, edge); }

        // Walled water over the fairways drawn before it (surfaces drawn after the wall stay on top).
        WaterWall.Draw(canvas, l, p, GPWater, GPProperty, _t.Background, 1.1f);
        DrawRocks(canvas, p, RockStyle.Flat(SKColor.Parse("#ddd8cc"), GPProperty, 0.55f));

        // Bunkers: ochre with a brown edge.
        using (var bunkers = BunkerPath(l, p))
        {
            using (var sand = Fill(GPSand)) canvas.DrawPath(bunkers, sand);
            using (var speck = TileShader(20, 90, 67, (c, at, _) => { using var d = Fill(GPSandEdge.WithAlpha(110)); c.DrawCircle(at, 0.25f, d); }))
                FillTexture(canvas, bunkers, speck);
            using var lip = Stroke(GPSandEdge, 0.55f); canvas.DrawPath(bunkers, lip);
        }

        // Putting surfaces: checkered dark and mid green, a dark edge.
        using (var greens = UnionPath(l.Greens, p))
        {
            using (var f = Fill(GPGreen)) canvas.DrawPath(greens, f);
            // A checker tile: two lighter squares on the dark ground, 1.6 page units each.
            using (var bmp = new SKBitmap(16, 16))
            {
                using (var c = new SKCanvas(bmp))
                {
                    c.Clear(SKColors.Transparent);
                    using var sq = Fill(GPGreenLight);
                    c.DrawRect(0, 0, 8, 8, sq); c.DrawRect(8, 8, 8, 8, sq);
                }
                using var img = SKImage.FromBitmap(bmp);
                using var shader = img.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateScale(0.2f, 0.2f));
                FillTexture(canvas, greens, shader);
            }
            using var edge = Stroke(GPGreenEdge, 0.9f); canvas.DrawPath(greens, edge);
        }

        // Teeing grounds: a pink oval at each tee, along the line of play.
        using (var tf = Fill(GPTee)) using (var te = Stroke(GPTeeEdge, 0.6f))
            foreach (var h in l.Holes)
            {
                var d = p.MapDirection(h.Direction);
                float angle = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI) + 90;
                foreach (var tee in h.Hole.Tees)
                {
                    var c = p.Map(tee);
                    float rx = Math.Max(1.6f, 3.2f * k), ry = Math.Max(2.4f, 5 * k);
                    canvas.Save(); canvas.Translate(c); canvas.RotateDegrees(angle);
                    var oval = new SKRect(-rx, -ry, rx, ry);
                    canvas.DrawOval(oval, tf); canvas.DrawOval(oval, te);
                    canvas.Restore();
                }
            }

        // Shot lines: thin red, a dot at each aim point.
        using (var route = Stroke(GPRed, 0.55f)) using (var dot = Fill(GPRed))
            foreach (var h in l.Holes)
            {
                using var path = p.Path(h.Route, false); canvas.DrawPath(path, route);
                foreach (var at in h.Route.Skip(1).SkipLast(1)) canvas.DrawCircle(p.Map(at), 1.1f, dot);
            }
        canvas.Restore();

        // Hole numbers: red italic beside the line, past its middle.
        var placed = new List<SKPoint>();
        foreach (var h in l.Holes)
        {
            var route = h.Route.Select(p.Map).ToList();
            var best = Beside(route, 0.55f, 9);
            foreach (var f in new[] { 0.55f, 0.45f, 0.65f, 0.38f, 0.72f })
            {
                best = Beside(route, f, 9);
                if (placed.All(q => SKPoint.Distance(q, best) > 18)) break;
            }
            placed.Add(best);
            Text(canvas, h.Hole.Number.ToString(), best.X, best.Y + 5, 14, SerifItalic, GPRed, SKTextAlign.Center, halo: _t.Background.WithAlpha(220));
        }

        // Title block in the quietest top corner.
        var place = PlaceList();
        float nameSize = GrassingNameSize(courseName);
        var tb = Reserved(0, () => QuietCorner(l, p, map, 260, GrassingTitleHeight(courseName), GrassingCorners));
        using (var paper = Fill(_t.Background.WithAlpha(235))) canvas.DrawRect(SKRect.Inflate(tb, 4, 4), paper);
        float x = tb.Left + 12, y = tb.Top + 22;
        Text(canvas, "A", x, y, 15, SerifItalic, GPPlum); y += 22;
        Text(canvas, "Grassing Plan", x + 4, y, 15, SerifItalic, GPPlum); y += 18;
        Text(canvas, "for", x + 2, y, 13, SerifItalic, GPPlum); y += nameSize + 6;
        Text(canvas, courseName, x, y, nameSize, Serif, GPPlum); y += 6;
        foreach (var line in place) { y += 16; Text(canvas, line, x + 2, y, 11.5f, SerifItalic, GPPlum); }
        y += 20;
        Text(canvas, DateTime.Now.ToString("MMMM yyyy"), x + 2, y, 10.5f, SerifItalic, GPPlum);
        y += 22;
        if (!string.IsNullOrWhiteSpace(_o.Credit))
        {
            string designed = "Designed by " + _o.Credit;
            Text(canvas, designed, x + 2, y, 10.5f, SerifItalic, GPPlum);
            using var u = Stroke(GPPlum, 0.5f); canvas.DrawLine(x + 2, y + 2.5f, x + 2 + Measure(designed, 10.5f, SerifItalic), y + 2.5f, u);
            y += 13;
        }
        Text(canvas, GrassingStyleCredit, x + 2, y, 7.5f, SerifItalic, _t.MutedInk);

        // Grassing legend and note in another quiet corner.
        var lb = Reserved(1, () => QuietCorner(l, p, map, 230, 150, GrassingCorners
            .Where(f => !SpotBox(map, f, 230, 150).IntersectsWith(SKRect.Inflate(tb, 6, 6))).DefaultIfEmpty((1f, 1f)).ToArray()));
        using (var paper = Fill(_t.Background.WithAlpha(235))) canvas.DrawRect(SKRect.Inflate(lb, 4, 4), paper);
        float lx = lb.Left + 14, ly = lb.Top + 20;
        Text(canvas, "Grassing Legend :", lx, ly, 13, SerifItalic, GPPlum);
        using (var u = Stroke(GPPlum, 0.6f)) canvas.DrawLine(lx, ly + 3, lx + Measure("Grassing Legend :", 13, SerifItalic), ly + 3, u);
        void Swatch(float yy, string label, Action<SKRect> draw)
        {
            var box = new SKRect(lx + 110, yy - 9, lx + 150, yy + 3);
            draw(box);
            using var frame = Stroke(GPTeeEdge, 0.6f); canvas.DrawRect(box, frame);
            Text(canvas, label, lx + 8, yy, 8.5f, SerifItalic, _t.MutedInk);
        }
        Swatch(ly + 26, "Teeing Grounds", b =>
        {
            using var tf = Fill(GPTee); using var te = Stroke(GPTeeEdge, 0.6f);
            canvas.DrawOval(new SKRect(b.Left + 6, b.Top + 2, b.Left + 18, b.Bottom - 2), tf); canvas.DrawOval(new SKRect(b.Left + 6, b.Top + 2, b.Left + 18, b.Bottom - 2), te);
            canvas.DrawOval(new SKRect(b.Left + 24, b.Top + 3, b.Left + 31, b.Bottom - 3), tf); canvas.DrawOval(new SKRect(b.Left + 24, b.Top + 3, b.Left + 31, b.Bottom - 3), te);
        });
        Swatch(ly + 50, "Fairways / Rough", b =>
        {
            using var hatch = Stroke(GPHatch, 0.4f);
            for (float yy = b.Top + 1.5f; yy < b.Bottom; yy += 2.1f) canvas.DrawLine(b.Left, yy, b.Right, yy, hatch);
            using var f = Fill(GPFairway); canvas.DrawRect(new SKRect(b.MidX - 6, b.Top + 2.5f, b.MidX + 6, b.Bottom - 2.5f), f);
        });
        Swatch(ly + 74, "Putting Surfaces", b =>
        {
            using var dark = Fill(GPGreen); using var light = Fill(GPGreenLight);
            canvas.DrawRect(b, dark);
            for (int i = 0; i < 10; i++) for (int j = 0; j < 3; j++) if ((i + j) % 2 == 0) canvas.DrawRect(b.Left + i * 4, b.Top + j * 4, 4, 4, light);
        });
        Swatch(ly + 98, "Sand", b => { using var f = Fill(GPSand); canvas.DrawRect(b, f); });
        Text(canvas, $"Note: drawn from the course file; contours at {_aerial.Interval:0.#} m;", lx, ly + 118, 7, SerifItalic, GPRed);
        Text(canvas, $"scale 1\" = {FeetPerInch(p)}'.", lx, ly + 127, 7, SerifItalic, GPRed);

        // A small ruled card in a remaining quiet corner.
        var taken = new[] { SKRect.Inflate(tb, 6, 6), SKRect.Inflate(lb, 6, 6) };
        var cb = Reserved(2, () => QuietCorner(l, p, map, 210, 132, GrassingCardSpots
            .Where(f => taken.All(t => !SpotBox(map, f, 210, 132).IntersectsWith(t))).DefaultIfEmpty((0.5f, 1f)).ToArray()));
        using (var paper = Fill(_t.Background.WithAlpha(235))) canvas.DrawRect(SKRect.Inflate(cb, 4, 4), paper);
        DrawNinesCard(canvas, course, SKRect.Inflate(cb, -4, -2), TeeColumns(course).Names.Count - 1, SerifItalic, Serif, 6.6f, GPPlum, grid: true, rowH: 9.3f);

        // The sheet's border.
        using (var border = Stroke(GPProperty, 0.9f)) canvas.DrawRect(SKRect.Inflate(map, 4, 4), border);
        Text(canvas, Attribution.Text, page.Width - 28, page.Height - 12, 6, _body, _t.MutedInk, SKTextAlign.Right);
    }
}
