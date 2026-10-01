using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>
/// Draws a hole's terrain sketch sheet on US Letter portrait: the hole runs up the page as in the yardage
/// book, over fine contours and shading, with a grid laid on the hole (yards from the back tee, and left /
/// right of the tee→green line) so hand sketches carry their own scale. Neighbouring holes are faint.
/// Sizes are in points, drawn at <see cref="HoleSheetOptions.Dpi"/>.
/// </summary>
public sealed class HoleSheetRenderer : TerrainInk
{
    static readonly SKColor Neighbour = SKColor.Parse("#8f8a80");
    readonly HoleSheetOptions _sheet;

    public HoleSheetRenderer(HoleSheetOptions options)
        : base(options.Map with { TickYards = options.TickYards, GridYards = options.GridYards }, (float)(options.Dpi / 72))
    {
        _sheet = options;
    }

    protected override bool FillGreenSites => false;
    protected override bool ShowBadges => false;   // the header carries hole, par, yards and rise

    public SKSizeI PixelSize => new((int)Math.Round(HoleSheet.PageWidth * _u), (int)Math.Round(HoleSheet.PageHeight * _u));

    SKRect Scaled(SKRect r) => new(r.Left * _u, r.Top * _u, r.Right * _u, r.Bottom * _u);

    public void Draw(SKCanvas canvas, string courseName, HoleSheet sheet)
    {
        float u = _u;
        var s = sheet.Survey;
        var map = Scaled(HoleSheet.MapRectPoints);
        var p = sheet.Projection(map);
        canvas.Clear(Paper);

        var all = s.Holes.Select(h => h.Route.Select(p.Map).ToArray()).ToList();
        int self = s.Holes.IndexOf(sheet.Routing);
        var route = all[self];

        canvas.Save();
        canvas.ClipRect(map);
        DrawShading(canvas, s, p);
        if (_o.ShowFeatures) DrawFeatures(canvas, s, p);
        DrawShoreline(canvas, s, p);
        DrawGridLines(canvas, sheet, p);

        var obstacles = new List<SKRect>();
        // Reserve the hole's own furniture so contour labels keep clear of it.
        foreach (var pt in route) obstacles.Add(new SKRect(pt.X - 14 * u, pt.Y - 14 * u, pt.X + 14 * u, pt.Y + 14 * u));
        var spots = _sheet.GridSpotHeights ? PlaceGridSpots(sheet, p, map, route, obstacles) : [];
        // Hilltop markers on the line itself would read as course furniture; leave those off.
        var summits = _o.ShowSummits ? PlaceSummits(s, p, map, obstacles).Where(t => DistanceToPolyline(t.At, route) > 12 * u).ToList() : [];
        DrawContours(canvas, s, p, map, all, obstacles, labelSpacing: 120);
        DrawGridSpots(canvas, spots);
        DrawSummits(canvas, summits);
        DrawNeighbours(canvas, s, all, self, map);
        obstacles.RemoveRange(0, route.Length);
        DrawRouting(canvas, [sheet.Routing], p, [route], obstacles);
        DrawRouteHeights(canvas, sheet, route, obstacles);
        canvas.Restore();

        using (var frame = Stroke(Ink, 0.8f * u)) canvas.DrawRect(map, frame);
        DrawGridLabels(canvas, sheet, p, map);
        DrawHeader(canvas, courseName, sheet);
        DrawFooter(canvas, sheet, p, map);
    }

    // ───────────────────────────── hole grid ─────────────────────────────

    double GridMetres => _sheet.GridYards / TerrainSurvey.YardsPerMetre;

    IEnumerable<double> AlongLines(HoleSheet h)
    {
        double g = GridMetres;
        for (double a = Math.Ceiling(h.Along.From / g) * g; a <= h.Along.To; a += g) yield return a;
    }

    IEnumerable<double> AcrossLines(HoleSheet h)
    {
        double g = GridMetres;
        for (double c = Math.Ceiling(h.Across.From / g) * g; c <= h.Across.To; c += g) yield return c;
    }

    void DrawGridLines(SKCanvas canvas, HoleSheet h, MapProjection p)
    {
        float u = _u;
        using var line = Stroke(GridLine.WithAlpha(90), 0.35f * u);
        using var centre = Stroke(GridLine.WithAlpha(150), 0.5f * u);
        using var dash = SKPathEffect.CreateDash([3 * u, 2 * u], 0);
        centre.PathEffect = dash;
        foreach (var a in AlongLines(h))
            canvas.DrawLine(p.Map(h.At(a, h.Across.From)), p.Map(h.At(a, h.Across.To)), line);
        foreach (var c in AcrossLines(h))
            canvas.DrawLine(p.Map(h.At(h.Along.From, c)), p.Map(h.At(h.Along.To, c)), Math.Abs(c) < 0.01 ? centre : line);
    }

    void DrawGridLabels(SKCanvas canvas, HoleSheet h, MapProjection p, SKRect map)
    {
        float u = _u, size = 6.4f * u;
        foreach (var a in AlongLines(h))
        {
            float y = p.Map(h.At(a, 0)).Y;
            if (y < map.Top + 3 * u || y > map.Bottom - 3 * u) continue;
            string text = Feet(a * TerrainSurvey.YardsPerMetre);
            Text(canvas, text, map.Left - 4 * u, y + size * 0.36f, size, _sansBold, GridLine, SKTextAlign.Right);
            Text(canvas, text, map.Right + 4 * u, y + size * 0.36f, size, _sansBold, GridLine);
        }
        // Which page side is "left" of the line of play (walking from tee to green, the page's left).
        float sideX = p.MapDirection(h.Side).X;
        foreach (var c in AcrossLines(h))
        {
            float x = p.Map(h.At(0, c)).X;
            if (x < map.Left + 6 * u || x > map.Right - 6 * u) continue;
            int yards = (int)Math.Round(Math.Abs(c) * TerrainSurvey.YardsPerMetre);
            bool pageLeft = c * sideX < 0;
            string text = yards == 0 ? "C" : $"{(pageLeft ? "L" : "R")}{yards}";
            Text(canvas, text, x, map.Top - 4 * u, size, _sansBold, GridLine, SKTextAlign.Center);
            Text(canvas, text, x, map.Bottom + 4 * u + size * 0.8f, size, _sansBold, GridLine, SKTextAlign.Center);
        }
    }

    /// <summary>Spot heights at grid crossings, kept clear of the route (its ticks and height labels sit there).</summary>
    List<(SKPoint At, string Text)> PlaceGridSpots(HoleSheet h, MapProjection p, SKRect map, SKPoint[] route, List<SKRect> obstacles)
    {
        float u = _u, size = 5f * u;
        using var font = new SKFont(_sansItalic, size);
        var result = new List<(SKPoint, string)>();
        foreach (var a in AlongLines(h))
            foreach (var c in AcrossLines(h))
            {
                var w = h.At(a, c);
                if (h.Survey.IsWater(h.Survey.Field.Sample(w))) continue;
                var at = p.Map(w);
                if (DistanceToPolyline(at, route) < 18 * u) continue;
                string text = Feet(h.Survey.Feet(w));
                var box = new SKRect(at.X - 1 * u, at.Y - 6.5f * u, at.X + 2.5f * u + font.MeasureText(text), at.Y + 1 * u);
                if (!map.Contains(box) || obstacles.Any(o => o.IntersectsWith(box))) continue;
                obstacles.Add(box);
                result.Add((at, text));
            }
        return result;
    }

    void DrawGridSpots(SKCanvas canvas, List<(SKPoint At, string Text)> spots)
    {
        float u = _u;
        using var dot = Fill(GridLine);
        foreach (var (at, text) in spots)
        {
            canvas.DrawCircle(at, 0.8f * u, dot);
            Text(canvas, text, at.X + 1.6f * u, at.Y - 1.6f * u, 5f * u, _sansItalic, GridLine, halo: Paper);
        }
    }

    // ───────────────────────────── routing extras ─────────────────────────────

    void DrawNeighbours(SKCanvas canvas, TerrainSurvey s, List<SKPoint[]> routes, int self, SKRect map)
    {
        float u = _u;
        using var line = Stroke(Neighbour.WithAlpha(200), 0.9f * u);
        using var dash = SKPathEffect.CreateDash([4 * u, 2.5f * u], 0);
        line.PathEffect = dash;
        using var dot = Fill(Neighbour);
        for (int i = 0; i < routes.Count; i++)
        {
            if (i == self) continue;
            var r = routes[i];
            using var b = new SKPathBuilder();
            b.MoveTo(r[0]);
            foreach (var pt in r.Skip(1)) b.LineTo(pt);
            using var path = b.Detach();
            canvas.DrawPath(path, line);
            canvas.DrawCircle(r[0], 1.8f * u, dot);
            using (var g = Stroke(Neighbour, 0.8f * u)) canvas.DrawCircle(r[^1], 5 * u, g);
            // Number the hole where it is on the sheet: at the tee if visible, else at the green.
            var at = map.Contains(r[0]) ? r[0] : r[^1];
            if (map.Contains(at))
                Text(canvas, $"{s.Holes[i].Hole.Number}", at.X + 4 * u, at.Y - 4 * u, 7 * u, _sansBold, Neighbour, halo: Paper);
        }
    }

    /// <summary>Ground height (ft) at every numbered tick, opposite the yardage number.</summary>
    void DrawRouteHeights(SKCanvas canvas, HoleSheet h, SKPoint[] r, List<SKRect> obstacles)
    {
        float u = _u, size = 5.4f * u;
        var cum = Cumulative(r);
        var world = h.Routing.Route;
        double metresPerTick = 2 * _o.TickYards / TerrainSurvey.YardsPerMetre;
        double total = world.Zip(world.Skip(1), Vec2.Distance).Sum();
        for (int k = 1; k * metresPerTick < total - 20; k++)
        {
            // Walk the world route to the same distance the ticks use.
            double left = k * metresPerTick;
            Vec2 w = world[^1];
            for (int i = 1; i < world.Count; i++)
            {
                double seg = Vec2.Distance(world[i - 1], world[i]);
                if (left <= seg) { w = world[i - 1] + (world[i] - world[i - 1]) * (left / seg); break; }
                left -= seg;
            }
            float at = (float)(k * metresPerTick / total) * cum[^1];
            var pt = PointAt(r, cum, at);
            var dir = Tangent(r, cum, at);
            var n = new SKPoint(dir.Y, -dir.X);   // opposite side to the tick numbers
            if (h.Survey.IsWater(h.Survey.Field.Sample(w))) continue;
            // Aim points carry their own label; keep their surroundings clear.
            if (r.Skip(1).SkipLast(1).Any(a => SKPoint.Distance(a, pt) < 14 * u)) continue;
            var lp = new SKPoint(pt.X + n.X * 9 * u, pt.Y + n.Y * 9 * u + size * 0.36f);
            Text(canvas, $"{Feet(h.Survey.Feet(w))}′", lp.X, lp.Y, size, _sansItalic, IndexContour, SKTextAlign.Center, halo: Paper);
        }
    }

    // ───────────────────────────── header & footer ─────────────────────────────

    void DrawHeader(SKCanvas canvas, string courseName, HoleSheet sheet)
    {
        float u = _u, left = 36 * u, right = (HoleSheet.PageWidth - 36) * u, top = 30 * u;
        var h = sheet.Routing;
        var s = sheet.Survey;
        Text(canvas, $"HOLE {h.Hole.Number}", left, top + 22 * u, 26 * u, _serifBold, Ink, tracking: 1.5f * u);
        float x = left + Measure($"HOLE {h.Hole.Number}", 26 * u, _serifBold) + 1.5f * u * 6 + 14 * u;
        Text(canvas, $"Par {h.Hole.Par}  ·  {h.Yards} yds", x, top + 21 * u, 13 * u, _serif, Ink);

        string change = Math.Abs(h.ChangeFeet) < 0.5 ? "level" : $"{(h.ChangeFeet > 0 ? "+" : "−")}{Math.Abs(h.ChangeFeet):0} ft";
        Text(canvas, $"Tee {h.TeeFeet:0} ft  →  green {h.GreenFeet:0} ft  ({change})", right, top + 10 * u, 8.5f * u, _sansBold, Ink, SKTextAlign.Right);
        Text(canvas, $"Green site slope {h.GreenSiteSlopePct:0.0}%  ·  ground on sheet {s.CorridorFeet.Low:0}–{s.CorridorFeet.High:0} ft",
            right, top + 22 * u, 7.5f * u, _sans, h.GreenSiteSlopePct > 5 ? Flag : Grey, SKTextAlign.Right);

        string datum = sheet.DatumIsSeaLevel ? "elevations in feet above sea level" : "elevations in feet, same datum as the course terrain map";
        Text(canvas, $"{courseName}  ·  terrain sketch sheet  ·  {s.IntervalFeet:0.##} ft contours, index every {s.IntervalFeet * s.IndexEvery:0.##} ft  ·  {datum}",
            left, top + 38 * u, 8 * u, _serifItalic, Grey);
        using var rule = Stroke(Ink, 0.7f * u);
        canvas.DrawLine(left, top + 44 * u, right, top + 44 * u, rule);
    }

    void DrawFooter(SKCanvas canvas, HoleSheet sheet, MapProjection p, SKRect map)
    {
        float u = _u;
        var s = sheet.Survey;
        float top = map.Bottom + 18 * u, left = 36 * u, right = (HoleSheet.PageWidth - 36) * u;
        using (var rule = Stroke(Rule, 0.6f * u)) canvas.DrawLine(left, top, right, top, rule);

        // Legend in three columns.
        float y0 = top + 13 * u, rowH = 11.5f * u, sw = 22 * u;
        float[] cols = [left, left + 168 * u, left + 336 * u];
        void Row(int col, int row, Action<float, float> sample, string label)
        {
            float x = cols[col], y = y0 + row * rowH;
            sample(x, y - 2.5f * u);
            Text(canvas, label, x + sw + 6 * u, y, 6.6f * u, _sans, Ink);
        }
        Row(0, 0, (x, y) => { using var pnt = Stroke(MinorContour, 0.38f * u); canvas.DrawLine(x, y, x + sw, y, pnt); },
            $"Contour every {s.IntervalFeet:0.##} ft");
        Row(0, 1, (x, y) => { using var pnt = Stroke(IndexContour, 0.95f * u); canvas.DrawLine(x, y, x + sw, y, pnt); },
            $"Index contour every {s.IntervalFeet * s.IndexEvery:0.##} ft");
        Row(0, 2, (x, y) =>
        {
            using var dot = Fill(GridLine);
            canvas.DrawCircle(x + sw / 2, y + 1.5f * u, 0.8f * u, dot);
            Text(canvas, "52", x + sw / 2 + 2 * u, y, 5 * u, _sansItalic, GridLine);
        }, $"Grid {_sheet.GridYards:0} yds, spot heights at crossings");
        if (s.SeaLevelMetres is not null)
            Row(0, 3, (x, y) =>
            {
                using var f = Fill(Sea); canvas.DrawRect(x, y - 3.5f * u, sw, 7 * u, f);
                using var e = Stroke(Shore, 0.9f * u); canvas.DrawLine(x, y - 3.5f * u, x + sw, y - 3.5f * u, e);
            }, "Sea and shoreline");

        Row(1, 0, (x, y) =>
        {
            using var c = Stroke(Ink, 1.3f * u); canvas.DrawLine(x + 2 * u, y, x + sw - 2 * u, y, c);
            using var f = Fill(Ink); canvas.DrawCircle(x + 2 * u, y, 2.9f * u, f);
            DrawAimPoint(canvas, new SKPoint(x + sw - 2 * u, y));
        }, "Back tee, route, aim point");
        Row(1, 1, (x, y) =>
        {
            using var c = Stroke(Ink, 1.3f * u); canvas.DrawLine(x, y, x + sw, y, c);
            using var t = Stroke(Ink, 0.8f * u);
            for (int k = 0; k < 3; k++) { float tx = x + 4 * u + k * 7 * u, len = (k == 1 ? 3.6f : 2.4f) * u; canvas.DrawLine(tx, y - len, tx, y + len, t); }
        }, $"Ticks every {_sheet.TickYards} yds, numbered every {2 * _sheet.TickYards}");
        Row(1, 2, (x, y) => Text(canvas, "48′", x + sw / 2, y + 2 * u, 5.4f * u, _sansItalic, IndexContour, SKTextAlign.Center),
            "Ground height at numbered ticks");
        Row(1, 3, (x, y) =>
        {
            using var e = Stroke(GreenEdge, 0.9f * u);
            canvas.DrawCircle(x + sw / 2, y, 4 * u, e);
        }, $"Green site ({_o.GreenSiteRadiusMetres * TerrainSurvey.YardsPerMetre:0} yd), arrow shows fall");
        Row(1, 4, (x, y) =>
        {
            using var l = Stroke(Neighbour, 0.9f * u);
            using var dash = SKPathEffect.CreateDash([4 * u, 2.5f * u], 0);
            l.PathEffect = dash;
            canvas.DrawLine(x, y, x + sw, y, l);
        }, "Neighbouring holes");

        // Scale bar and north arrow.
        float pxPerYard = (float)(p.PointsPerMetre / TerrainSurvey.YardsPerMetre);
        int total = new[] { 200, 150, 100, 50 }.First(t => t * pxPerYard <= 120 * u || t == 50);
        int div = total / 50 >= 2 ? total / 50 : 2;
        float seg = total * pxPerYard / div, bx = cols[2], by = y0 - 2 * u, bh = 3.5f * u;
        using (var ink = Fill(Ink))
        using (var edge = Stroke(Ink, 0.5f * u))
            for (int i = 0; i < div; i++)
            {
                var rect = SKRect.Create(bx + i * seg, by, seg, bh);
                if (i % 2 == 0) canvas.DrawRect(rect, ink);
                canvas.DrawRect(rect, edge);
            }
        for (int i = 0; i <= div; i++)
            Text(canvas, $"{total * i / div}", bx + i * seg, by + bh + 8 * u, 6 * u, _sans, Ink, SKTextAlign.Center);
        Text(canvas, "yards", bx + div * seg + 5 * u, by + bh, 6 * u, _sansItalic, Grey);
        double yardsPerInch = _u * 72 / p.PointsPerMetre * TerrainSurvey.YardsPerMetre;
        Text(canvas, $"1 in = {yardsPerInch:0} yds when printed at 100 %",
            bx, by + bh + 20 * u, 6 * u, _sansItalic, Grey);
        DrawNorthArrow(canvas, new SKPoint(right - 14 * u, y0 + 14 * u), p.MapDirection(MapProjection.North));

        Text(canvas, $"Terrain from {s.Course.HeightStamps.Count:N0} height stamps; check critical grades in the game.",
            cols[2], y0 + 3 * rowH, 5.8f * u, _sansItalic, Grey);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
            Text(canvas, _o.Credit.ToUpperInvariant(), cols[2], y0 + 4 * rowH, 6.2f * u, _serif, Grey, tracking: 1f * u);
    }

    void DrawNorthArrow(SKCanvas canvas, SKPoint c, SKPoint dir)
    {
        float u = _u, len = 11 * u;
        var n = new SKPoint(-dir.Y, dir.X);
        SKPoint P(float along, float across) => new(c.X + dir.X * along + n.X * across, c.Y + dir.Y * along + n.Y * across);
        using var b = new SKPathBuilder();
        b.MoveTo(P(len, 0)); b.LineTo(P(-len * 0.6f, 4 * u)); b.LineTo(P(-len * 0.25f, 0)); b.Close();
        using var right = b.Detach();
        using var b2 = new SKPathBuilder();
        b2.MoveTo(P(len, 0)); b2.LineTo(P(-len * 0.6f, -4 * u)); b2.LineTo(P(-len * 0.25f, 0)); b2.Close();
        using var left = b2.Detach();
        using var ink = Fill(Ink);
        using var edge = Stroke(Ink, 0.9f * u);
        canvas.DrawPath(right, ink);
        canvas.DrawPath(right, edge);
        canvas.DrawPath(left, edge);
        var label = P(len + 6 * u, 0);
        Text(canvas, "N", label.X, label.Y + 3 * u, 7.5f * u, _serifBold, Ink, SKTextAlign.Center);
    }

    /// <summary>Whole number without a "-0".</summary>
    static string Feet(double v) => Math.Round(v) is var r && r == 0 ? "0" : $"{r:0}";

    static float Measure(string text, float size, SKTypeface face)
    {
        using var font = new SKFont(face, size);
        return font.MeasureText(text);
    }
}
