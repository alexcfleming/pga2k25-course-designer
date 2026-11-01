using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The page round the map, shared by every theme but the Plan Sheet: the map across the page and a scorecard strip
/// under it, a column per hole (1–9, Out, 10–18, In, Total) and a row per tee set (up to five) then Par, with the title,
/// credit and legend beside it. A portrait page puts the title on top and the strip under the map. The compass rose and
/// the course's wind rose sit in the map's emptiest corners.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    /// <summary>Tee rows the strip has room for.</summary>
    public const int StripTeeRows = 5;

    const float StripTop = 652, StripLeftColumn = 250;

    /// <summary>Page margin round the strip layout: wider inside the Vintage frame.</summary>
    float StripMargin => _t.Frame ? 40 : 30;

    SKRect StripMapRect(SKSize page) => new(StripMargin, StripMargin, page.Width - StripMargin, StripTop - 12);

    /// <summary>The scorecard strip under the map: the title column (name, credit, legend) and the table.</summary>
    void DrawStrip(SKCanvas canvas, string courseName, CourseModel course, SKSize page)
    {
        var strip = new SKRect(StripMargin, StripTop, page.Width - StripMargin, page.Height - StripMargin);
        DrawStripTitle(canvas, courseName, new SKRect(strip.Left, strip.Top, strip.Left + StripLeftColumn - 16, strip.Bottom));
        DrawStripTable(canvas, course, new SKRect(strip.Left + StripLeftColumn, strip.Top, strip.Right, strip.Bottom));
        Text(canvas, Attribution.Text, strip.Right, page.Height - (_t.Frame ? 26 : 14), 7.5f, _body, _t.MutedInk, SKTextAlign.Right);
    }

    /// <summary>
    /// The tee sets the strip shows, longest first: all of them up to <see cref="StripTeeRows"/>, else the longest
    /// five (sets are numbered shortest → longest, as in the course file).
    /// </summary>
    public static IReadOnlyList<int> StripTeeSets(int sets) =>
        Enumerable.Range(0, Math.Min(sets, StripTeeRows)).Select(i => sets - 1 - i).ToList();

    void DrawStripTitle(SKCanvas canvas, string name, SKRect r)
    {
        if (_t.Kind is MapThemeKind.Vintage or MapThemeKind.Illustrated) name = name.ToUpperInvariant();
        float size = 26;
        var lines = new List<string> { name };
        if (Measure(name, size, _title) > r.Width && name.Contains(' '))
        {
            int mid = name.Length / 2;
            int cut = Enumerable.Range(0, name.Length).Where(i => name[i] == ' ').MinBy(i => Math.Abs(i - mid));
            lines = [name[..cut], name[(cut + 1)..]];
            size = 21;
        }
        // The course seal at the left of the title column, the name beside it.
        float seal = _logo is null ? 0 : 58, nameLeft = r.Left + (seal > 0 ? seal + 10 : 0), nameWidth = r.Right - nameLeft;
        if (_logo is { } logo) GreensBook.CourseLogoArt.Draw(canvas, logo, new SKRect(r.Left, r.Top, r.Left + seal, r.Top + seal));
        if (seal > 0 && lines.Count == 1 && name.Contains(' ') && Measure(name, size, _title) > nameWidth)
        {
            int mid = name.Length / 2;
            int cut = Enumerable.Range(0, name.Length).Where(i => name[i] == ' ').MinBy(i => Math.Abs(i - mid));
            lines = [name[..cut], name[(cut + 1)..]];
            size = 21;
        }
        size = Math.Max(12, Math.Min(size, lines.Min(l => size * nameWidth / Math.Max(1, Measure(l, size, _title)))));
        float block = size * 1.1f * lines.Count;
        float y = r.Top + (seal > 0 ? Math.Max(0, (seal - block) / 2) : 0) + size * 0.92f;
        foreach (var line in lines) { Text(canvas, line, nameLeft, y, size, _title, _t.Ink); y += size * 1.1f; }
        if (seal > 0) y = Math.Max(y, r.Top + seal + size * 0.1f + 9);
        string sub = string.IsNullOrWhiteSpace(_o.Credit) ? _o.Subtitle : $"{_o.Subtitle}  ·  Designed by {_o.Credit}";
        float subSize = 10;
        while (subSize > 7 && Measure(sub, subSize, _body) > r.Width) subSize -= 0.25f;
        Text(canvas, sub, r.Left, y + 3 - size * 0.1f, subSize, _body, _t.MutedInk);
        var legend = new SKRect(r.Left, y + 18, r.Right, r.Bottom);
        if (IsModernIllustrated) DrawStripLegend(canvas, legend);
        else DrawLegendGrid(canvas, legend);
    }

    /// <summary>
    /// The scorecard strip: a column per hole (1–9, Out, 10–18, In, Total), a row per tee set in <paramref name="teeRows"/>
    /// (default: the longest four), then Par. Modern Illustrated draws it as a card with a green header; the other themes
    /// in their own ink, ruled and framed like their side tables. Returns the bottom of the table.
    /// </summary>
    float DrawStripTable(SKCanvas canvas, CourseModel course, SKRect r, bool championshipOnly = false, float maxRowH = 28)
    {
        var (names, yards) = TeeColumns(course);
        var sets = championshipOnly ? [names.Count - 1] : StripTeeSets(names.Count);
        // As in the side tables: the championship row is "Yards" unless the Holes card names it.
        if (championshipOnly && (_o.Holes?.Tees ?? []).ElementAtOrDefault(names.Count - 1) is not { Length: > 0 }) names[^1] = "Yards";
        bool card = IsModernIllustrated;
        SKColor headInk = card ? Scenery.SandLit : _t.Ink;
        var holes = course.Holes;
        var front = holes.Take(9).ToList();
        var back = holes.Skip(9).ToList();
        string? NameOf(Hole h) => _o.Holes?.NameOf(h.Number);
        bool named = holes.Any(h => NameOf(h) is not null);

        // Columns: the label, then 1–9, Out, 10–18, In, Total (a nine-hole course: 1–9 and Total).
        var cols = new List<(string Head, Func<Func<Hole, int>, int> Value, bool Total, Hole? Hole)>();
        foreach (var h in front) cols.Add((h.Number.ToString(), v => v(h), false, h));
        if (back.Count > 0)
        {
            cols.Add(("Out", v => front.Sum(v), true, null));
            foreach (var h in back) cols.Add((h.Number.ToString(), v => v(h), false, h));
            cols.Add(("In", v => back.Sum(v), true, null));
        }
        cols.Add(("Total", v => holes.Sum(v), true, null));

        float size = 10.5f;
        float labelW = Math.Clamp(sets.Max(i => Measure(names[i], size, _body)) + 26, 62, 120);
        float cw = (r.Width - labelW) / cols.Count;
        string widest = sets.Count == 0 ? "0" : holes.Sum(h => yards(h, sets[0])).ToString("N0");
        while (size > 7 && Measure(widest, size, _bodyBold) > cw - 6) size -= 0.25f;

        int rows = 1 + (named ? 1 : 0) + sets.Count + 1;
        float rowH = Math.Min(maxRowH, r.Height / (rows + (named ? 0.6f : 0)));
        float nameH = named ? rowH * 1.6f : 0;
        var table = new SKRect(r.Left, r.Top, r.Right, r.Top + rowH * (rows - (named ? 1 : 0)) + nameH);
        if (card)
        {
            using (var fill = Fill(Card)) canvas.DrawRoundRect(table, 6, 6, fill);
            canvas.Save();
            canvas.ClipRect(new SKRect(table.Left, table.Top, table.Right, table.Top + rowH));
            using (var head = Fill(_t.GreenFill)) canvas.DrawRoundRect(table, 6, 6, head);
            canvas.Restore();
            canvas.Save();
            canvas.ClipRect(new SKRect(table.Left, table.Top + rowH, table.Right, table.Bottom));
            using (var band = Fill(CardBand))
                for (int c = 0; c < cols.Count; c++)
                    if (cols[c].Total) canvas.DrawRect(new SKRect(table.Left + labelW + c * cw, table.Top + rowH, table.Left + labelW + (c + 1) * cw, table.Bottom), band);
            canvas.Restore();
        }
        else
        {
            if (_t.Kind == MapThemeKind.Modern) using (var box = Fill(_t.Background)) canvas.DrawRect(table, box);
            // Ruled like the side tables: a frame, a rule under the header, and lines either side of the totals.
            using var frame = Stroke(_t.Kind == MapThemeKind.Modern ? _t.MutedInk : _t.Ink, 0.9f);
            canvas.DrawRect(table, frame);
            using var line = Stroke(_t.Kind == MapThemeKind.Modern ? _t.MutedInk.WithAlpha(160) : _t.Ink, 0.7f);
            canvas.DrawLine(table.Left, table.Top + rowH, table.Right, table.Top + rowH, line);
            canvas.DrawLine(table.Left + labelW, table.Top, table.Left + labelW, table.Bottom, line);
            for (int c = 0; c < cols.Count; c++)
                if (cols[c].Total)
                {
                    canvas.DrawLine(table.Left + labelW + c * cw, table.Top, table.Left + labelW + c * cw, table.Bottom, line);
                    canvas.DrawLine(table.Left + labelW + (c + 1) * cw, table.Top, table.Left + labelW + (c + 1) * cw, table.Bottom, line);
                }
        }

        float Mid(int c) => table.Left + labelW + c * cw + cw / 2;
        float Base(float top, float h) => top + h / 2 + size * 0.36f;
        float y = table.Top;
        Text(canvas, "Hole", table.Left + 8, Base(y, rowH), size, _bodyBold, headInk);
        for (int c = 0; c < cols.Count; c++) Text(canvas, cols[c].Head, Mid(c), Base(y, rowH), size, _bodyBold, headInk, SKTextAlign.Center);
        y += rowH;

        using var rule = Stroke(card ? CardRule : _t.MutedInk.WithAlpha(110), 0.5f);
        if (named)
        {
            // Names wrap onto a second line, then shrink, then are cut, to fit their column.
            Text(canvas, "Name", table.Left + 8, Base(y, nameH), size, _bodyBold, _t.Ink);
            for (int c = 0; c < cols.Count; c++)
                if (cols[c].Hole is { } h && NameOf(h) is { } name)
                    DrawCellName(canvas, name, Mid(c), y, cw - 4, nameH, size * 0.82f);
            y += nameH;
            canvas.DrawLine(table.Left, y, table.Right, y, rule);
        }

        for (int t = 0; t < sets.Count; t++)
        {
            int set = sets[t];
            float x = table.Left + 8;
            if (TeeColour(names[set]) is { } dot)
            {
                var at = new SKPoint(x + 4, y + rowH / 2);
                using (var f = Fill(dot)) canvas.DrawCircle(at, 3.8f, f);
                using (var ring = Stroke(_t.Ink, 0.5f)) canvas.DrawCircle(at, 3.8f, ring);
                x += 12;
            }
            string label = names[set];
            while (label.Length > 1 && Measure(label, size, _body) > table.Left + labelW - 4 - x) label = label[..^2] + "…";
            Text(canvas, label, x, Base(y, rowH), size, t == 0 ? _bodyBold : _body, _t.Ink);
            for (int c = 0; c < cols.Count; c++)
                Text(canvas, cols[c].Value(h => yards(h, set)).ToString("N0"), Mid(c), Base(y, rowH), size, t == 0 || cols[c].Total ? _bodyBold : _body, _t.Ink, SKTextAlign.Center);
            y += rowH;
            canvas.DrawLine(table.Left, y, table.Right, y, rule);
        }
        Text(canvas, "Par", table.Left + 8, Base(y, rowH), size, _bodyBold, _t.Ink);
        for (int c = 0; c < cols.Count; c++) Text(canvas, cols[c].Value(h => h.Par).ToString(), Mid(c), Base(y, rowH), size, _bodyBold, _t.Ink, SKTextAlign.Center);
        return table.Bottom;
    }

    void DrawCellName(SKCanvas canvas, string name, float x, float top, float width, float height, float size)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> lines = [name];
        if (Measure(name, size, _italic) > width && words.Length > 1)
        {
            // Two lines, broken at the space that best balances them.
            int best = Enumerable.Range(1, words.Length - 1).MinBy(i =>
                Math.Max(Measure(string.Join(' ', words[..i]), size, _italic), Measure(string.Join(' ', words[i..]), size, _italic)));
            lines = [string.Join(' ', words[..best]), string.Join(' ', words[best..])];
        }
        float s = size;
        while (s > size * 0.75f && lines.Any(l => Measure(l, s, _italic) > width)) s -= 0.25f;
        for (int i = 0; i < lines.Count; i++)
            while (lines[i].Length > 1 && Measure(lines[i], s, _italic) > width) lines[i] = lines[i][..^2] + "…";
        float lineH = s * 1.15f, y = top + height / 2 - lineH * (lines.Count - 1) / 2 + s * 0.36f;
        foreach (var l in lines) { Text(canvas, l, x, y, s, _italic, _t.Ink, SKTextAlign.Center); y += lineH; }
    }

    /// <summary>Portrait BigMap: the scorecard strip across the page under the map, then the totals, credit and legend in a row.</summary>
    void DrawPortraitStrip(SKCanvas canvas, CourseModel course, SKRect band)
    {
        float y = DrawStripTable(canvas, course, new SKRect(band.Left, band.Top, band.Right, band.Bottom - 50), maxRowH: 21);
        var (names, yards) = TeeColumns(course);
        int longest = names.Count - 1;
        y += 18;
        string totals = $"Par {course.Holes.Sum(h => h.Par)}  ·  {course.Holes.Sum(h => yards(h, longest)):N0} yards";
        Text(canvas, totals, band.Left, y, 10.5f, _body, _t.Ink);
        if (!string.IsNullOrWhiteSpace(_o.Credit)) Text(canvas, $"Designed by {_o.Credit}", band.Left, y + 15, 10.5f, _italic, _t.MutedInk);
        Text(canvas, Attribution.Text, band.Right, band.Bottom, 7.5f, _body, _t.MutedInk, SKTextAlign.Right);
        DrawLegendRow(canvas, new SKPoint(band.Left + 250, y - 4), band.Right);
    }

    /// <summary>The legend in a row (wrapping to a second) from <paramref name="at"/> to <paramref name="right"/>.</summary>
    void DrawLegendRow(SKCanvas canvas, SKPoint at, float right)
    {
        float x = at.X, y = at.Y;
        foreach (var (draw, label) in LegendItems(canvas))
        {
            float w = 30 + Measure(label, 9.5f, _body) + 16;
            if (x + w > right) { x = at.X; y += 16; }
            draw(new SKRect(x, y - 9, x + 22, y + 1));
            Text(canvas, label, x + 30, y, 9.5f, _body, _t.Ink);
            x += w;
        }
    }

    /// <summary>The legend in two columns filling <paramref name="r"/>.</summary>
    void DrawLegendGrid(SKCanvas canvas, SKRect r)
    {
        var items = LegendItems(canvas);
        int perColumn = (items.Count + 1) / 2;
        float rowH = Math.Min(17, r.Height / Math.Max(1, perColumn));
        for (int i = 0; i < items.Count; i++)
        {
            float x = r.Left + (i / perColumn) * (r.Width / 2), y = r.Top + rowH * (i % perColumn) + rowH * 0.7f;
            items[i].Draw(new SKRect(x, y - 9, x + 22, y + 1));
            Text(canvas, items[i].Label, x + 30, y, 9.5f, _body, _t.Ink);
        }
    }

    /// <summary>The theme's legend swatches, as in the side panel: course features, then trees and hills on the Illustrated map.</summary>
    List<(Action<SKRect> Draw, string Label)> LegendItems(SKCanvas canvas)
    {
        var items = new List<(Action<SKRect> Draw, string Label)>
        {
            (b => { using var f = Fill(_t.GrassFill); canvas.DrawRoundRect(b, 3, 3, f); if (_t.GrassOutline is { } o) { using var s = Stroke(o, 0.9f); canvas.DrawRoundRect(b, 3, 3, s); } }, "Fairway / tee"),
            (b => { using var f = Fill(_t.GreenFill); canvas.DrawOval(b, f); if (_t.GreenOutline is { } o) { using var s = Stroke(o, 0.9f); canvas.DrawOval(b, s); } }, "Green"),
            (b => { using var f = Fill(_t.BunkerFill); canvas.DrawOval(new SKRect(b.Left + 4, b.Top + 1, b.Right - 4, b.Bottom - 1), f); }, "Bunker"),
            (b => { using var s = Stroke(_t.RouteColour, 0.9f); if (_t.RouteDash is { } d) s.PathEffect = SKPathEffect.CreateDash(d, 0); canvas.DrawLine(b.Left, b.MidY, b.Right, b.MidY, s); }, "Ball flight"),
        };
        if (_landscape is { Trees.Count: > 0 })
            items.Add((b => { using var c = Fill(_t.CrownFill); using var e = Stroke(_t.Ink, 0.5f); canvas.DrawCircle(b.Left + 6, b.MidY, 4.5f, c); canvas.DrawCircle(b.Left + 6, b.MidY, 4.5f, e); canvas.DrawCircle(b.Left + 14, b.MidY, 5, c); canvas.DrawCircle(b.Left + 14, b.MidY, 5, e); }, "Trees"));
        if (_o.ShowWaste)
            items.Insert(3, (b => { using var f = Fill(_t.WasteFill); canvas.DrawRoundRect(b, 3, 3, f); }, "Waste area"));
        if (_landscape is { HasHills: true })
            items.Add((b => { using var s = Stroke(_t.Ink, 0.45f); for (float hx = b.Left + 2; hx < b.Right - 1; hx += 2.6f) canvas.DrawLine(hx, b.Top + 1, hx + 0.6f, b.Bottom - 1, s); }, "Hills"));
        return items;
    }

    /// <summary>Portrait: the strip band under the map, a row per tee set up to <see cref="StripTeeRows"/>, then the totals line and legend.</summary>
    static float PortraitBand(int teeSets) => 80 + 21 * (2 + Math.Min(teeSets, StripTeeRows));

    /// <summary>The wind rose's panel on the map, and the compass rose's radius.</summary>
    const float WindBoxWidth = 170, WindBoxHeight = 150, CompassRadius = 34;

    /// <summary>
    /// The compass rose, turned to game north, and the course's wind rose (north-up, as on the Wind Card), each where
    /// the least of the course lies under it: the top corners and bottom right first, then spots along the map's edges
    /// (the scale bar has the bottom left). The wind rose chooses first, as it needs the most room.
    /// </summary>
    void DrawRoses(SKCanvas canvas, CourseLayers layers, MapProjection proj, SKRect map)
    {
        SKRect? taken = null;
        SKRect Place(float w, float h) => RoseSpots
            .Select((f, i) => (Box: SpotBox(map, f, w, h), Corner: i < 3))
            .Where(c => taken is not { } t || SKRect.Intersect(SKRect.Inflate(t, 6, 6), c.Box).IsEmpty)
            .MinBy(c => PlayUnder(layers, proj, c.Box) + (c.Corner ? 0 : 3)).Box;
        if (_o.Wind is { } wind)
        {
            var box = Place(WindBoxWidth, WindBoxHeight);
            taken = box;
            using (var panel = Fill(PanelColour.WithAlpha(235))) canvas.DrawRoundRect(box, 4, 4, panel);
            using (var edge = Stroke(_t.Ink.WithAlpha(140), 0.5f)) canvas.DrawRoundRect(box, 4, 4, edge);
            WindRoseDrawing.Draw(canvas, SKRect.Inflate(box, -4, -4), wind, _body, _t.Ink, PanelColour, 0.7f);
        }
        float side = 2 * CompassRadius + 24;
        var at = Place(side, side);
        DrawCompassRose(canvas, proj, new SKPoint(at.MidX, at.MidY + 4), CompassRadius);
    }

    /// <summary>Where a rose may go, as fractions across and down the map: three corners, then along the edges.</summary>
    static readonly (float X, float Y)[] RoseSpots =
        [(0, 0), (1, 0), (1, 1), (0.5f, 0), (0.25f, 0), (0.75f, 0), (1, 0.5f), (0, 0.5f), (0.5f, 1), (0.75f, 1)];

    /// <summary>The background of panels laid on the map: the page colour, or Modern Illustrated's lit sand.</summary>
    SKColor PanelColour => IsModernIllustrated ? Scenery.SandLit : _t.Background;

    /// <summary>A <paramref name="w"/> × <paramref name="h"/> box inset in the map at spot <paramref name="f"/>.</summary>
    static SKRect SpotBox(SKRect map, (float X, float Y) f, float w, float h)
    {
        const float inset = 10;
        float x = map.Left + inset + f.X * (map.Width - 2 * inset - w), y = map.Top + inset + f.Y * (map.Height - 2 * inset - h);
        return new SKRect(x, y, x + w, y + h);
    }

    /// <summary>
    /// How much of the course falls inside <paramref name="box"/>: grass and green outline points, and points every
    /// 4 page units along each hole's route (a long shot has no vertex of its own in a corner it crosses).
    /// </summary>
    static int PlayUnder(CourseLayers l, MapProjection p, SKRect box)
    {
        int n = l.Grass.Concat(l.Greens).SelectMany(s => s).Select(p.Map).Count(box.Contains);
        foreach (var h in l.Holes)
        {
            var route = h.Route.Select(p.Map).ToList();
            for (int i = 1; i < route.Count; i++)
            {
                var (a, b) = (route[i - 1], route[i]);
                int steps = Math.Max(1, (int)(SKPoint.Distance(a, b) / 4));
                for (int k = 0; k <= steps; k++)
                    if (box.Contains(a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps)) n += 4;
            }
        }
        return n;
    }
}
