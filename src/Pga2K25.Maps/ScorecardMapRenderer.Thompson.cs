using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Thompson style: a blueprint after Stanley Thompson's course plans (credited on the sheet). White chalk on
/// blueprint blue: a ruled property line; fairways as white outlines with a stippled rim; tree masses as clumps of
/// stippled white crowns; greens as white circles holding the hole number and a flag; tee boxes and bunkers in white
/// line; roads as double lines. No shot lines, as on the original. A hand-lettered "PROPOSED" title, the scale, an
/// underlined credit block and a small card in the quiet corners. Landscape or portrait, whichever draws the course larger.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    public const string ThompsonStyleCredit = "IN THE STYLE OF A STANLEY THOMPSON PLAN";

    bool IsThompson => _t.Kind == MapThemeKind.Thompson;

    static readonly SKColor TBlue = SKColor.Parse("#1d3f9e"), TBlueDeep = SKColor.Parse("#16307d"), TBlueLight = SKColor.Parse("#2c56c0");
    static readonly SKColor TChalk = SKColor.Parse("#eef3ff");

    static SKRect ThompsonMapRect(SKSize page) => new(30, 30, page.Width - 30, page.Height - 30);

    const float ThompsonCardW = 224, ThompsonCardH = 146;
    static readonly (float X, float Y)[] ThompsonCardSpots = [(1f, 0.6f), (1f, 1f), (0f, 1f), (0f, 0f), (1f, 0f), (0.5f, 1f)];

    float ThompsonNameSize(string courseName) => Math.Min(24, 24 * 300 / Math.Max(1, Measure(courseName.ToUpperInvariant(), 24, Hand)));
    float ThompsonTitleHeight(string courseName) => 30 + ThompsonNameSize(courseName) + 8 + PlaceList().Count * 20 + 24 + 70;

    /// <summary>Title, then card; the card goes where the title isn't.</summary>
    List<BoxAsk> ThompsonBoxes(string courseName) =>
        [new(320, ThompsonTitleHeight(courseName), [(1, 0), (0, 0)]), new(ThompsonCardW, ThompsonCardH, ThompsonCardSpots)];

    /// <summary>A patch of plain blueprint under chalk lettering, so no tree stipple or line runs through it.</summary>
    static void ThompsonBacking(SKCanvas canvas, SKRect box)
    {
        using var blue = new SKPaint { IsAntialias = true, Color = TBlue.WithAlpha(235), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
        canvas.DrawRoundRect(SKRect.Inflate(box, 4, 4), 4, 4, blue);
    }

    void DrawThompson(SKCanvas canvas, string courseName, CourseModel course, CourseLayers l, MapProjection p, SKSize page, SKRect map)
    {
        float k = p.PointsPerMetre;
        _scenery ??= Scenery.Build(course, p, map);
        var parcel = ParcelOf(l);
        using var property = parcel.Count >= 3 ? p.Path(parcel, true) : null;
        using var grass = UnionPath(l.Grass, p);
        using var water = WaterPath(l, p);
        var pageRect = new SKRect(0, 0, page.Width, page.Height);
        SKPaint Chalk(float w, byte a = 235) { var s = Stroke(TChalk.WithAlpha(a), w); s.PathEffect = Wobble(0.25f, 91); return s; }

        // Blueprint ground: deep blue, lighter in the middle, with a fine paper grain.
        using (var ground = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(new SKPoint(page.Width * 0.45f, page.Height * 0.45f), Math.Max(page.Width, page.Height) * 0.75f,
                [TBlueLight, TBlue, TBlueDeep], [0, 0.55f, 1], SKShaderTileMode.Clamp),
        })
            canvas.DrawRect(pageRect, ground);
        using (var pagePath = new SKPath())
        {
            pagePath.AddRect(pageRect);
            using var grain = TileShader(60, 700, 93, (c, at, r) => { using var d = Fill((r.Next(3) == 0 ? TBlueDeep : TChalk).WithAlpha((byte)r.Next(6, 22))); c.DrawCircle(at, 0.3f, d); });
            FillTexture(canvas, pagePath, grain);
        }

        canvas.Save();
        canvas.ClipRect(map);
        if (property is not null) { using var line = Stroke(TChalk.WithAlpha(220), 0.7f); canvas.DrawPath(property, line); }

        // A stipple of chalk dots, as a tile, for crowns and rims.
        using var stipple = TileShader(30, 420, 95, (c, at, r) => { using var d = Fill(TChalk.WithAlpha((byte)r.Next(140, 245))); c.DrawCircle(at, 0.25f + (float)r.NextDouble() * 0.3f, d); });

        // Fairways: a white edge with a stippled rim inside it; the blue left open in the middle.
        using (var rim = new SKPath())
        {
            using (var band = Stroke(SKColors.Black, 5f)) band.GetFillPath(grass, rim);
            using var inside = rim.Op(grass, SKPathOp.Intersect);
            if (inside is not null) FillTexture(canvas, inside, stipple);
        }
        using (var edge = Chalk(0.8f)) canvas.DrawPath(grass, edge);

        // Water: a chalk bank with level dashes.
        if (!water.IsEmpty)
        {
            using (var bank = Chalk(0.9f)) canvas.DrawPath(water, bank);
            canvas.Save(); canvas.ClipPath(water, antialias: true);
            using var dash = Stroke(TChalk.WithAlpha(150), 0.4f);
            var wb = water.Bounds;
            for (float wy = wb.Top; wy < wb.Bottom; wy += 3.2f) canvas.DrawLine(wb.Left, wy, wb.Right, wy, dash);
            canvas.Restore();
        }
        // The wall in chalk on the blueprint: a chalk line with blue gaps between its stones.
        WaterWall.Draw(canvas, l, p, _t.Background, TChalk, _t.Background, 1.1f);
        DrawRocks(canvas, p, RockStyle.Ink(TBlue, TChalk, 0.6f));

        // Roads as double lines.
        if (_o.ShowPaths)
            foreach (var r in l.TrimToPlay(l.Roads.Concat(l.Paths).ToList(), _o.PathReachMetres))
            {
                using var path = p.Path(r.Points, false);
                using var road = new SKPath();
                using (var width = Stroke(SKColors.Black, Math.Max(2.2f, (float)(r.Width * k)))) { width.StrokeCap = SKStrokeCap.Butt; width.GetFillPath(path, road); }
                using var sides = Stroke(TChalk.WithAlpha(210), 0.5f); canvas.DrawPath(road, sides);
            }

        // Tree masses: clumps of crowns, each a chalk ring filled with stipple, drawn over the blue so they pile up.
        using (var rings = new SKPathBuilder())
        using (var crowns = new SKPathBuilder())
        {
            foreach (var t in _scenery!.Trees) { float r = Math.Max(1.8f, t.Radius * 0.9f); rings.AddCircle(t.At.X, t.At.Y, r); crowns.AddCircle(t.At.X, t.At.Y, r); }
            foreach (var (at, br) in _scenery.Bushes)
                if (!grass.Contains(at.X, at.Y)) { float r = Math.Max(1.3f, br * 1.5f); rings.AddCircle(at.X, at.Y, r); crowns.AddCircle(at.X, at.Y, r); }
            using var ringPath = rings.Detach();
            using var crownPath = crowns.Detach();
            FillTexture(canvas, crownPath, stipple);
            using var ringPen = Stroke(TChalk.WithAlpha(200), 0.45f); canvas.DrawPath(ringPath, ringPen);
        }

        // Bunkers in white line.
        using (var bunkers = BunkerPath(l, p))
        {
            using var edge = Chalk(0.7f); canvas.DrawPath(bunkers, edge);
        }

        // Tee boxes: small white rectangles.
        using (var tee = Chalk(0.7f))
            foreach (var h in l.Holes)
            {
                var d = p.MapDirection(h.Direction);
                float angle = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI) + 90;
                foreach (var t in h.Hole.Tees)
                {
                    var c = p.Map(t);
                    float len = Math.Max(3.2f, 9 * k), wid = Math.Max(2.4f, 6 * k);
                    canvas.Save(); canvas.Translate(c); canvas.RotateDegrees(angle);
                    canvas.DrawRect(-wid / 2, -len / 2, wid, len, tee);
                    canvas.Restore();
                }
            }

        // Greens: their outline in chalk, a circle round each with the hole number and a flag.
        using (var greens = UnionPath(l.Greens, p))
        {
            using var edge = Chalk(0.8f); canvas.DrawPath(greens, edge);
        }
        foreach (var h in l.Holes)
        {
            var pin = p.Map(h.Route[^1]);
            float r = 9;
            using (var circle = Chalk(0.8f)) canvas.DrawCircle(pin, r, circle);
            Text(canvas, h.Hole.Number.ToString(), pin.X - 2.5f, pin.Y + 3.5f, 8.5f, Hand, TChalk, SKTextAlign.Center);
            using var pole = Stroke(TChalk, 0.6f);
            canvas.DrawLine(pin.X + 3.5f, pin.Y + 4, pin.X + 3.5f, pin.Y - 5, pole);
            using var flag = new SKPath();
            flag.MoveTo(pin.X + 3.5f, pin.Y - 5); flag.LineTo(pin.X + 7, pin.Y - 3.6f); flag.LineTo(pin.X + 3.5f, pin.Y - 2.2f); flag.Close();
            using (var f = Fill(TChalk)) canvas.DrawPath(flag, f);
        }
        canvas.Restore();

        // Title in the quietest top corner: PROPOSED / NAME / place / scale, hand-lettered in chalk.
        var place = PlaceList();
        string name = courseName.ToUpperInvariant();
        float nameSize = ThompsonNameSize(courseName);
        var tb = Reserved(0, () => QuietCorner(l, p, map, 320, ThompsonTitleHeight(courseName), (1, 0), (0, 0)));
        ThompsonBacking(canvas, tb);
        float cx = tb.MidX, y = tb.Top + 24;
        Text(canvas, "PROPOSED", cx, y, 18, Hand, TChalk, SKTextAlign.Center, tracking: 2); y += nameSize + 6;
        Text(canvas, name, cx, y, nameSize, Hand, TChalk, SKTextAlign.Center, tracking: 1.2f);
        foreach (var line in place) { y += 20; Text(canvas, "- " + line.ToUpperInvariant() + " -", cx, y, 15, Hand, TChalk, SKTextAlign.Center, tracking: 1.4f); }
        y += 16; Text(canvas, $"SCALE 1\" = {FeetPerInch(p)}'", cx, y, 8, Hand, TChalk, SKTextAlign.Center, tracking: 1);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
        {
            y += 28;
            string c1 = _o.Credit.ToUpperInvariant(), c2 = "GOLF & LANDSCAPE ARCHITECT";
            Text(canvas, c1, cx, y, 9.5f, Hand, TChalk, SKTextAlign.Center, tracking: 1);
            Text(canvas, c2, cx, y + 12, 8, Hand, TChalk, SKTextAlign.Center, tracking: 1);
            using var u = Stroke(TChalk, 0.6f);
            float w = Math.Max(Measure(c1, 9.5f, Hand), Measure(c2, 8, Hand)) / 2 + 14;
            canvas.DrawLine(cx - w, y - 11, cx + w, y - 11, u);
            canvas.DrawLine(cx - w, y + 16, cx + w, y + 16, u);
        }

        // A small card in chalk, and the style credit bottom right like the original's "Plan drawn by" note.
        var taken = SKRect.Inflate(tb, 6, 6);
        var cb = Reserved(1, () =>
        {
            var spots = ThompsonCardSpots.Where(f => !SpotBox(map, f, ThompsonCardW, ThompsonCardH).IntersectsWith(taken)).ToArray();
            return QuietCorner(l, p, map, ThompsonCardW, ThompsonCardH, spots.Length > 0 ? spots : [(0.5f, 1f)]);
        });
        ThompsonBacking(canvas, cb);
        DrawNinesCard(canvas, course, SKRect.Inflate(cb, -6, -4), TeeColumns(course).Names.Count - 1, Hand, Hand, 7.4f, TChalk, grid: true, rowH: 10.2f);
        Text(canvas, ThompsonStyleCredit, page.Width - 36, page.Height - 20, 6.5f, Hand, TChalk.WithAlpha(220), SKTextAlign.Right, tracking: 0.6f);
        Text(canvas, Attribution.Text, page.Width - 36, page.Height - 11, 5.5f, _body, TChalk.WithAlpha(190), SKTextAlign.Right);
    }
}
