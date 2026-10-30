using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>
/// Draws the terrain planning map: a north-up topographic sheet (relief or slope shading, minor and
/// labelled index contours, hilltop spot heights, lettered reference grid) with the routing on top as
/// sticks and dots, and a side panel with the hole table, legend, scale and north arrow.
/// Points scale with the map (<see cref="TerrainMapOptions.PixelsPerMetre"/>) so the sheet reads the same at any resolution.
/// </summary>
public sealed class TerrainMapRenderer : TerrainInk
{
    public TerrainMapRenderer(TerrainMapOptions options) : base(options, (float)(options.PixelsPerMetre * 0.87)) { }

    // ───────────────────────────── layout ─────────────────────────────

    sealed record Layout(SKSizeI Size, SKRect Map, SKRect Panel, SKRect Title, MapProjection Projection);

    Layout Plan(TerrainSurvey s)
    {
        float u = _u, ppm = (float)_o.PixelsPerMetre;
        float margin = 28 * u, gutter = 16 * u, title = 62 * u, panelW = 236 * u;
        float mapW = (float)(s.Max.X - s.Min.X) * ppm, mapH = (float)(s.Max.Z - s.Min.Z) * ppm;
        var map = SKRect.Create(margin + gutter, margin + title + gutter, mapW, mapH);
        var panel = new SKRect(map.Right + gutter + 26 * u, map.Top, map.Right + gutter + 26 * u + panelW, map.Bottom);
        float height = Math.Max(map.Bottom + gutter + margin, map.Top + PanelHeight(s) + margin);
        var size = new SKSizeI((int)Math.Ceiling(panel.Right + margin), (int)Math.Ceiling(height));
        var projection = MapProjection.Fit([s.Min, s.Max, new Vec2(s.Min.X, s.Max.Z), new Vec2(s.Max.X, s.Min.Z)],
            map, MapProjection.NorthUpRotation, padding: 1.0);
        return new Layout(size, map, panel with { Bottom = height - margin }, new SKRect(margin, margin, panel.Right, margin + title), projection);
    }

    public SKSizeI PageSize(TerrainSurvey s) => Plan(s).Size;

    /// <summary>Where the map sits on the page and its projection, for viewers that read positions back.</summary>
    public (SKRect Map, MapProjection Projection) MapFrame(TerrainSurvey s) => (Plan(s).Map, Plan(s).Projection);

    /// <summary>Reference-grid square ("F7") of a page point on the map, or null off the map or with no grid.</summary>
    public string? GridReference(TerrainSurvey s, SKPoint page)
    {
        var map = Plan(s).Map;
        if (_o.GridYards <= 0 || !map.Contains(page)) return null;
        return $"{ColumnName((int)((page.X - map.Left) / GridStep))}{(int)((page.Y - map.Top) / GridStep) + 1}";
    }

    public void Draw(SKCanvas canvas, TerrainSurvey s)
    {
        var l = Plan(s);
        canvas.Clear(Paper);

        canvas.Save();
        canvas.ClipRect(l.Map);
        if (_o.Shading != ReliefShading.None || s.SeaLevelMetres is not null) DrawShading(canvas, s, l.Projection);
        if (_o.ShowFeatures) DrawFeatures(canvas, s, l.Projection);
        DrawShoreline(canvas, s, l.Projection);
        var routes = s.Holes.Select(h => h.Route.Select(l.Projection.Map).ToArray()).ToList();
        var obstacles = new List<SKRect>();
        var summitLabels = _o.ShowSummits ? PlaceSummits(s, l.Projection, l.Map, obstacles) : [];
        DrawContours(canvas, s, l.Projection, l.Map, routes, obstacles);
        DrawPaintedWater(canvas, s, l.Projection);
        DrawPlanting(canvas, s, l.Projection, l.Map, _o.Planting);
        DrawSummits(canvas, summitLabels);
        if (_o.GridYards > 0) DrawGridLines(canvas, l);
        DrawRouting(canvas, s.Holes, l.Projection, routes, obstacles);
        DrawLandmarks(canvas, s, l.Projection, routes, obstacles);
        canvas.Restore();

        using (var frame = Stroke(Ink, 0.9f * _u)) canvas.DrawRect(l.Map, frame);
        if (_o.GridYards > 0) DrawGridLabels(canvas, l);
        DrawTitle(canvas, s, l);
        DrawPanel(canvas, s, l);
    }
    // ───────────────────────────── grid ─────────────────────────────

    float GridStep => (float)(_o.GridYards / TerrainSurvey.YardsPerMetre * _o.PixelsPerMetre);

    void DrawGridLines(SKCanvas canvas, Layout l)
    {
        using var paint = Stroke(GridLine.WithAlpha(80), 0.4f * _u);
        for (float x = l.Map.Left + GridStep; x < l.Map.Right - 1; x += GridStep) canvas.DrawLine(x, l.Map.Top, x, l.Map.Bottom, paint);
        for (float y = l.Map.Top + GridStep; y < l.Map.Bottom - 1; y += GridStep) canvas.DrawLine(l.Map.Left, y, l.Map.Right, y, paint);
    }

    void DrawGridLabels(SKCanvas canvas, Layout l)
    {
        float u = _u, size = 6.5f * u;
        var c = GridLine;
        int col = 0;
        for (float x = l.Map.Left; x < l.Map.Right - GridStep * 0.3f; x += GridStep, col++)
        {
            float mid = Math.Min(x + GridStep / 2, (x + l.Map.Right) / 2);
            Text(canvas, ColumnName(col), mid, l.Map.Top - 5 * u, size, _sansBold, c, SKTextAlign.Center);
            Text(canvas, ColumnName(col), mid, l.Map.Bottom + 5 * u + size * 0.75f, size, _sansBold, c, SKTextAlign.Center);
        }
        int row = 1;
        for (float y = l.Map.Top; y < l.Map.Bottom - GridStep * 0.3f; y += GridStep, row++)
        {
            float mid = Math.Min(y + GridStep / 2, (y + l.Map.Bottom) / 2) + size * 0.36f;
            Text(canvas, row.ToString(), l.Map.Left - 5 * u, mid, size, _sansBold, c, SKTextAlign.Right);
            Text(canvas, row.ToString(), l.Map.Right + 5 * u, mid, size, _sansBold, c);
        }
    }

    void DrawLandmarks(SKCanvas canvas, TerrainSurvey s, MapProjection p, List<SKPoint[]> routes, List<SKRect> obstacles)
    {
        float u = _u;
        using var ink = Fill(Ink);
        using var ring = Stroke(Paper, 0.8f * u);
        foreach (var lm in s.Landmarks)
        {
            var at = p.Map(lm.Position);
            if (lm.Minor)
            {
                canvas.DrawCircle(at, 1.6f * u, ink);
                if (lm.Label.Length > 0)
                    Text(canvas, lm.Label, at.X + 3 * u, at.Y + 2 * u, 5.4f * u, _sansItalic, Ink, halo: Paper);
                continue;
            }
            var box = new SKRect(at.X - 2.6f * u, at.Y - 2.6f * u, at.X + 2.6f * u, at.Y + 2.6f * u);
            canvas.DrawRect(box, ink);
            canvas.DrawRect(box, ring);
            obstacles.Add(box);
            PlaceText(canvas, lm.Label, at, 5 * u, 6.6f * u, _sansBoldItalic, Ink, routes.Count > 0 ? routes : [[at, at]], obstacles,
                preferred: new SKPoint(1, 0));
        }
    }

    public static string ColumnName(int i) => i < 26 ? ((char)('A' + i)).ToString() : ColumnName(i / 26 - 1) + (char)('A' + i % 26);

    // ───────────────────────────── title & panel ─────────────────────────────

    void DrawTitle(SKCanvas canvas, TerrainSurvey s, Layout l)
    {
        float u = _u;
        var t = l.Title;
        string name = string.IsNullOrWhiteSpace(s.Course.Name) ? "Course" : s.Course.Name;
        Text(canvas, name.ToUpperInvariant(), t.Left, t.Top + 26 * u, 26 * u, _serifBold, Ink, tracking: 2.2f * u);
        string place = string.IsNullOrWhiteSpace(_o.Place) ? "" : _o.Place.Trim() + "  ·  ";
        Text(canvas, $"{place}{(s.Holes.Count == 0 ? "Terrain plan for routing" : "Terrain & routing plan")}  ·  {Interval(s)} contours, index every {s.IntervalFeet * s.IndexEvery:0.##} ft  ·  " +
                     (s.SeaLevelMetres is null ? "elevations in feet above the lowest point on the sheet" : "elevations in feet above sea level"), t.Left, t.Top + 44 * u, 10 * u, _serifItalic, Grey);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
            Text(canvas, _o.Credit.ToUpperInvariant(), t.Right, t.Top + 26 * u, 9 * u, _serif, Grey, SKTextAlign.Right, tracking: 1.6f * u);
        Text(canvas, "PGA TOUR 2K25 COURSE DESIGNER", t.Right, t.Top + 44 * u, 7.5f * u, _sans, Grey, SKTextAlign.Right, tracking: 1.2f * u);
        using var rule = Stroke(Ink, 0.8f * u);
        canvas.DrawLine(t.Left, t.Bottom - 4 * u, t.Right, t.Bottom - 4 * u, rule);
    }

    static string Interval(TerrainSurvey s) => $"{s.IntervalFeet:0.##} ft";

    const float RowH = 12.5f;

    float PanelHeight(TerrainSurvey s) => ((s.Holes.Count == 0 ? 520 : 300 + RowH * (s.Holes.Count + 2) + 420) + 80 + (ShowsPlanting(s) ? 280 : 0)
        + (_o.Region is not null ? RegionBlock : _o.SiteInset ? SiteBlock : 0)) * _u;

    // Heading, the inset square and its caption, in panel units.
    const float SiteSide = 170, SiteBlock = 22 + SiteSide + 28, RegionHeight = 176, RegionBlock = 22 + RegionHeight + 28;

    void DrawRegion(SKCanvas canvas, TerrainSurvey s, SKRect r, ref float y)
    {
        float u = _u;
        _site ??= Maps.SiteInset.Build(s.Course, s.Layers);
        var box = new SKRect(r.Left, y, r.Right, y + RegionHeight * u);
        Maps.RegionLocator.DrawLocator(canvas, box, _o.Region!, _o.RegionName, _serif, Ink, Paper, Flag, u, _site, Shore.WithAlpha(170));
        y += RegionHeight * u + 12 * u;
        string kind = _o.Region!.Shape switch
        {
            Maps.RegionShape.Island => "An invented island",
            Maps.RegionShape.Peninsula => "An invented peninsula",
            Maps.RegionShape.Bay => "An invented bay",
            Maps.RegionShape.Lake => "An invented lake shore",
            _ => "Invented inland country",
        };
        Text(canvas, $"{kind}, north up  ·  the course in red, its plot in the corner", r.Left, y, 7.6f * u, _serifItalic, Grey);
        y += 16 * u;
    }
    Maps.SiteInset? _site;

    void DrawSite(SKCanvas canvas, TerrainSurvey s, SKRect r, ref float y)
    {
        float u = _u;
        _site ??= Maps.SiteInset.Build(s.Course, s.Layers);
        var box = SKRect.Create(r.Left, y, SiteSide * u, SiteSide * u);
        _site.Draw(canvas, box, Ink, Paper, Flag, u, Shore.WithAlpha(170));
        Text(canvas, "N", box.Right + 8 * u, box.Top + 9 * u, 8 * u, _sansBold, Ink);
        using (var arrow = Stroke(Ink, 0.8f * u)) canvas.DrawLine(box.Right + 10.5f * u, box.Top + 13 * u, box.Right + 10.5f * u, box.Top + 26 * u, arrow);
        y += SiteSide * u + 12 * u;
        string sea = _site.HasSea ? "sea hatched  ·  " : "";
        Text(canvas, $"The plot, {_site.SideMetres * TerrainSurvey.YardsPerMetre:N0} yds across  ·  {sea}the course in red", r.Left, y, 7.6f * u, _serifItalic, Grey);
        y += 16 * u;
    }

    bool ShowsPlanting(TerrainSurvey s) => s.Planting.Has(_o.Planting);

    void DrawPanel(SKCanvas canvas, TerrainSurvey s, Layout l)
    {
        float u = _u;
        var r = l.Panel;
        float y = r.Top;

        void Heading(string text)
        {
            Text(canvas, text, r.Left, y + 7 * u, 7.5f * u, _sansBold, Grey, tracking: 1.4f * u);
            using var rule = Stroke(Rule, 0.6f * u);
            canvas.DrawLine(r.Left, y + 11 * u, r.Right, y + 11 * u, rule);
            y += 22 * u;
        }

        if (_o.Region is not null)
        {
            Heading("THE PLACE");
            DrawRegion(canvas, s, r, ref y);
        }
        else if (_o.SiteInset)
        {
            Heading("THE SITE");
            DrawSite(canvas, s, r, ref y);
        }

        if (s.Holes.Count == 0)
        {
            // Blank plot: what there is to route around.
            Heading("THE PLOT");
            Text(canvas, "No holes routed yet", r.Left, y + 6 * u, 10 * u, _serifBold, Ink);
            y += 16 * u;
            Text(canvas, $"{(s.Max.X - s.Min.X) * TerrainSurvey.YardsPerMetre:N0} × {(s.Max.Z - s.Min.Z) * TerrainSurvey.YardsPerMetre:N0} yds  ·  " +
                         $"high point {s.MaxFeet:0} ft", r.Left, y + 6 * u, 8 * u, _serifItalic, Grey);
            y += 14 * u;
            foreach (var lm in s.Landmarks.Where(l => !l.Minor))
            {
                Text(canvas, $"{lm.Name}  ·  {s.Feet(lm.Position):0} ft", r.Left, y + 6 * u, 7.6f * u, _sans, Ink);
                y += 11 * u;
            }
            y += 14 * u;
        }
        else
        {
        // Summary.
        Heading("THE ROUTING");
        int totalYards = s.Holes.Sum(h => h.Yards), par = s.Holes.Sum(h => h.Hole.Par);
        Text(canvas, $"{s.Holes.Count} holes  ·  par {par}  ·  {totalYards:N0} yds", r.Left, y + 6 * u, 10 * u, _serifBold, Ink);
        y += 16 * u;
        Text(canvas, $"Relief on sheet {s.MaxFeet:0} ft  ·  within {_o.CorridorMetres * TerrainSurvey.YardsPerMetre:0} yds of play " +
                     $"{s.CorridorFeet.Low:0}–{s.CorridorFeet.High:0} ft", r.Left, y + 6 * u, 8 * u, _serifItalic, Grey);
        y += 22 * u;

        // Hole table.
        Heading("HOLES  (back tees)");
        float[] cols = [0, 20, 42, 78, 112, 146, 180, 206];
        string[] head = ["No.", "Par", "Yds", "Tee ft", "Green ft", "Change", "Tilt", "±ft"];
        for (int i = 0; i < head.Length; i++)
            Text(canvas, head[i], r.Left + (cols[i] + (i == 0 ? 0 : 30)) * u, y + 6 * u, 6.8f * u, _sansBold, Grey, i == 0 ? SKTextAlign.Left : SKTextAlign.Right);
        y += RowH * u;
        foreach (var h in s.Holes)
        {
            string[] cells =
            [
                h.Hole.Number.ToString(), h.Hole.Par.ToString(), h.Yards.ToString(), $"{h.TeeFeet:0}", $"{h.GreenFeet:0}",
                Math.Abs(h.ChangeFeet) < 0.5 ? "0" : $"{(h.ChangeFeet > 0 ? "+" : "−")}{Math.Abs(h.ChangeFeet):0}",
                $"{h.GreenSiteGradePct:0.0}%", $"{h.GreenSiteReliefFeet:0}",
            ];
            for (int i = 0; i < cells.Length; i++)
            {
                Text(canvas, cells[i], r.Left + (cols[i] + (i == 0 ? 0 : 30)) * u, y + 6 * u, 7.6f * u, i == 0 ? _sansBold : _sans, Ink,
                    i == 0 ? SKTextAlign.Left : SKTextAlign.Right);
            }
            y += RowH * u;
        }
        Text(canvas, $"Change: green vs back tee.  Green site (drawn green, or {_o.GreenSiteRadiusMetres * TerrainSurvey.YardsPerMetre:0} yds round) as the", r.Left, y + 6 * u, 6.6f * u, _sansItalic, Grey);
        y += 10 * u;
        Text(canvas, "ground lies, before shaping: tilt of its best-fit plane, ± most the ground departs from it.", r.Left, y + 6 * u, 6.6f * u, _sansItalic, Grey);
        y += 22 * u;
        }

        // The designer's budget counters, estimated (the file doesn't store them).
        Heading("GAME BUDGET  (rough estimate)");
        var budget = GameBudget.Estimate(s.Course);
        void Meter(string label, double pct)
        {
            Text(canvas, label, r.Left, y + 6.5f * u, 7.6f * u, _sans, Ink);
            var bar = new SKRect(r.Left + 46 * u, y + 1 * u, r.Right - 46 * u, y + 7 * u);
            using (var back = Fill(Rule.WithAlpha(120))) canvas.DrawRect(bar, back);
            using (var fill = Fill(pct >= 90 ? Flag : Ink)) canvas.DrawRect(bar.Left, bar.Top, bar.Width * (float)Math.Clamp(pct / 100, 0, 1), bar.Height, fill);
            Text(canvas, $"≈ {pct:0} %", r.Right, y + 6.5f * u, 7.6f * u, _sansBold, pct >= 90 ? Flag : Ink, SKTextAlign.Right);
            y += 13 * u;
        }
        Meter("Objects", budget.ObjectPercent);
        Meter("Memory", budget.MemoryPercent);
        Text(canvas, $"≈ {budget.Objects:N0} objects, {budget.UniqueAssets} different assets. Fitted to four courses'", r.Left, y + 5 * u, 6.4f * u, _sansItalic, Grey);
        y += 9 * u;
        Text(canvas, "in-game readings; the designer's own counters are the real figures.", r.Left, y + 5 * u, 6.4f * u, _sansItalic, Grey);
        y += 20 * u;

        // Planting summary: trees, and their species.
        var plants = s.Planting;
        bool planting = ShowsPlanting(s), trees = planting && (_o.Planting & PlantingLayers.Trees) != 0;
        if (planting)
        {
            Heading("PLANTING");
            var (placed, scattered) = plants.Count(PlantKind.Tree);
            if (trees && placed + scattered > 0)
            {
                Text(canvas, scattered == 0 ? $"{placed:N0} trees" : $"≈ {placed + scattered:N0} trees", r.Left, y + 6 * u, 10 * u, _serifBold, Ink);
                y += 16 * u;
                if (scattered > 0)
                {
                    Text(canvas, $"{placed:N0} placed, ≈ {scattered:N0} in groups and areas", r.Left, y + 6 * u, 8 * u, _serifItalic, Grey);
                    y += 14 * u;
                }
                var species = plants.TreeSpecies();
                foreach (var (sp, count, estimated) in species.Take(6))
                {
                    using var dot = Fill(TreeFill(sp));
                    canvas.DrawCircle(r.Left + 3 * u, y + 4 * u, 2.6f * u, dot);
                    Text(canvas, sp.Name, r.Left + 10 * u, y + 6.5f * u, 7.4f * u, _sans, Ink);
                    Text(canvas, (estimated ? "≈ " : "") + count.ToString("N0"), r.Right - 10 * u, y + 6.5f * u, 7.4f * u, _sans, Ink, SKTextAlign.Right);
                    y += 11 * u;
                }
                if (species.Count > 6)
                {
                    Text(canvas, $"+ {species.Count - 6} more species", r.Left + 10 * u, y + 6.5f * u, 6.8f * u, _sansItalic, Grey);
                    y += 11 * u;
                }
            }
            Text(canvas, "Only what was planted in the designer: trees and", r.Left, y + 6 * u, 6.6f * u, _sansItalic, Grey);
            y += 10 * u;
            Text(canvas, "plants the game adds by itself aren't shown.", r.Left, y + 6 * u, 6.6f * u, _sansItalic, Grey);
            y += 20 * u;
        }

        // Legend.
        Heading("LEGEND");
        float sx = r.Left, sw = 34 * u;
        void Row(Action<float> sample, string label)
        {
            sample(y + 4 * u);
            Text(canvas, label, sx + sw + 10 * u, y + 7 * u, 7.6f * u, _sans, Ink);
            y += 15 * u;
        }
        if (planting) PlantingLegend(canvas, plants, Row, sx, sw);
        Row(cy => { using var p = Stroke(MinorContour, 0.38f * u); canvas.DrawLine(sx, cy, sx + sw, cy, p); },
            $"Contour, every {Interval(s)}" + (_o.MinContourSpacingPixels > 0 ? " (thinned on steep ground)" : ""));
        Row(cy =>
        {
            using var p = Stroke(IndexContour, 0.95f * u);
            canvas.DrawLine(sx, cy, sx + 8 * u, cy, p); canvas.DrawLine(sx + sw - 8 * u, cy, sx + sw, cy, p);
            Text(canvas, "250", sx + sw / 2, cy + 2 * u, 5.6f * u, _sans, IndexContour, SKTextAlign.Center);
        }, $"Index contour, every {s.IntervalFeet * s.IndexEvery:0.##} ft (labelled)");
        if (_o.ShowSummits)
            Row(cy =>
            {
                using var b = new SKPathBuilder();
                float cx = sx + sw / 2;
                b.MoveTo(cx, cy - 2.2f * u); b.LineTo(cx + 2 * u, cy + 1.4f * u); b.LineTo(cx - 2 * u, cy + 1.4f * u); b.Close();
                using var tri = b.Detach();
                using var f = Fill(Ink);
                canvas.DrawPath(tri, f);
            }, "Hilltop spot height (ft)");
        if (s.Holes.Count > 0)
        {
        Row(cy =>
        {
            using var c = Stroke(Ink, 1.3f * u);
            canvas.DrawLine(sx + 3 * u, cy, sx + sw - 3 * u, cy, c);
            using var f = Fill(Ink);
            canvas.DrawCircle(sx + 3 * u, cy, 2.9f * u, f);
            DrawAimPoint(canvas, new SKPoint(sx + sw - 3 * u, cy));
        }, "Back tee, route and aim point (yds from tee · to green)");
        Row(cy =>
        {
            using var c = Stroke(Ink, 1.3f * u);
            canvas.DrawLine(sx, cy, sx + sw, cy, c);
            using var t = Stroke(Ink, 0.8f * u);
            for (int k = 0; k < 3; k++) { float x = sx + 6 * u + k * 11 * u; float len = (k == 1 ? 3.6f : 2.4f) * u; canvas.DrawLine(x, cy - len, x, cy + len, t); }
        }, $"Ticks every {_o.TickYards} yds from the back tee, numbered every {2 * _o.TickYards}");
        if (s.Holes.Any(_o.ShowsGreenSite))
            Row(cy =>
            {
                float rad = 4.5f * u;
                var at = new SKPoint(sx + sw / 2, cy + 1 * u);
                using var f = Fill(GreenFill.WithAlpha(150));
                using var e = Stroke(GreenEdge, 0.9f * u);
                canvas.DrawCircle(at, rad, f); canvas.DrawCircle(at, rad, e);
                using var a = Stroke(GreenEdge, 0.8f * u);
                canvas.DrawLine(at.X - rad * 0.7f, at.Y, at.X + rad * 0.7f, at.Y, a);
            }, $"Green site ({_o.GreenSiteRadiusMetres * TerrainSurvey.YardsPerMetre:0} yd radius), arrow shows fall");
        Row(cy =>
        {
            var c = new SKPoint(sx + sw / 2, cy + 1 * u);
            using var f = Fill(Ink);
            canvas.DrawCircle(c, 5.5f * u, f);
            Text(canvas, "7", c.X, c.Y + 2.4f * u, 6.6f * u, _sansBold, SKColors.White, SKTextAlign.Center);
        }, "Hole number · par · yards · rise or fall to green");
        }
        if (s.Landmarks.Count > 0)
            Row(cy =>
            {
                var c = new SKPoint(sx + sw / 2, cy + 1 * u);
                using var f = Fill(Ink);
                canvas.DrawRect(c.X - 2.6f * u, c.Y - 2.6f * u, 5.2f * u, 5.2f * u, f);
            }, "Clubhouse, range tee, practice green (· range markers, yds)");
        if (_o.GridYards > 0)
            Row(cy =>
            {
                using var g = Stroke(GridLine.WithAlpha(120), 0.4f * u);
                canvas.DrawRect(sx + 4 * u, cy - 5 * u, sw - 8 * u, 10 * u, g);
                Text(canvas, "C7", sx + sw / 2, cy + 2.2f * u, 6 * u, _sansBold, GridLine, SKTextAlign.Center);
            }, $"Reference grid, {_o.GridYards:0} yd squares");
        if (s.SeaLevelMetres is not null)
            Row(cy =>
            {
                using var f = Fill(Sea);
                canvas.DrawRect(sx + 2 * u, cy - 5 * u, sw - 4 * u, 10 * u, f);
                using var e = Stroke(Shore, 0.9f * u);
                canvas.DrawLine(sx + 2 * u, cy - 5 * u, sx + sw - 2 * u, cy - 5 * u, e);
            }, "Sea, with shoreline at 0 ft");
        if (s.InlandWater is { } water)
        {
            Row(cy =>
            {
                var box = new SKRect(sx + 2 * u, cy - 4 * u, sx + sw - 2 * u, cy + 4 * u);
                using var shader = SKShader.CreateLinearGradient(new SKPoint(box.Left, 0), new SKPoint(box.Right, 0),
                    [Sea, DeepWater], SKShaderTileMode.Clamp);
                using var f = new SKPaint { Shader = shader };
                canvas.DrawRect(box, f);
                using var e = Stroke(Shore, 0.6f * u);
                canvas.DrawRect(box, e);
            }, "Ponds and streams, level in ft (darker is deeper)");
            if (_o.ShowWaterFootprints && water.Strokes.Any(k => k.Kind == GreensBook.WaterKind.Plane))
                Row(cy =>
                {
                    using var e = Stroke(Shore.WithAlpha(170), 0.5f * u);
                    using var dash = SKPathEffect.CreateDash([3 * u, 2.5f * u], 0);
                    e.PathEffect = dash;
                    canvas.DrawRect(sx + 2 * u, cy - 5 * u, sw - 4 * u, 10 * u, e);
                }, "Water plane: lower ground inside floods");
        }
        if (_o.ShowFeatures && (s.Layers.Grass.Count + s.Layers.Greens.Count + s.Layers.BunkerAreas.Count + s.Layers.WaterAreas.Count) > 0)
            Row(cy =>
            {
                using var e = Stroke(SKColor.Parse("#4f7f3f"), 0.6f * u);
                using var dash = SKPathEffect.CreateDash([4 * u, 2.5f * u], 0);
                e.PathEffect = dash;
                canvas.DrawRoundRect(sx + 2 * u, cy - 5 * u, sw - 4 * u, 10 * u, 4 * u, 4 * u, e);
            }, "Surfaces already drawn in the designer");

        // Shading key.
        y += 6 * u;
        if (_o.Shading == ReliefShading.Relief)
        {
            var bar = new SKRect(r.Left, y, r.Right - 10 * u, y + 9 * u);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(bar.Left, 0), new SKPoint(bar.Right, 0),
                Hypsometric.Select(h => h.C).ToArray(), Hypsometric.Select(h => (float)h.T).ToArray(), SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(bar, paint);
            using (var e = Stroke(Rule, 0.5f * u)) canvas.DrawRect(bar, e);
            Text(canvas, s.SeaLevelMetres is null ? "0 ft" : "0 ft (sea)", bar.Left, bar.Bottom + 9 * u, 6.6f * u, _sans, Grey);
            Text(canvas, $"{s.MaxFeet / 2:0}", bar.MidX, bar.Bottom + 9 * u, 6.6f * u, _sans, Grey, SKTextAlign.Center);
            Text(canvas, $"{s.MaxFeet:0} ft", bar.Right, bar.Bottom + 9 * u, 6.6f * u, _sans, Grey, SKTextAlign.Right);
            y += 26 * u;
            Text(canvas, "Elevation tint with north-west hillshade.", r.Left, y, 6.6f * u, _sansItalic, Grey);
            y += 16 * u;
        }
        else if (_o.Shading == ReliefShading.Slope)
        {
            foreach (var k in SlopeClasses)
            {
                using var f = Fill(k.C);
                canvas.DrawRect(r.Left, y, 18 * u, 9 * u, f);
                using (var e = Stroke(Rule, 0.5f * u)) canvas.DrawRect(r.Left, y, 18 * u, 9 * u, e);
                Text(canvas, k.Label, r.Left + 26 * u, y + 7 * u, 7.2f * u, _sans, Ink);
                y += 12 * u;
            }
            y += 6 * u;
        }

        // North arrow and scale bar.
        y += 8 * u;
        Heading("SCALE");
        DrawNorthArrow(canvas, new SKPoint(r.Right - 16 * u, y + 14 * u));
        DrawScaleBar(canvas, new SKPoint(r.Left, y + 10 * u), r.Width - 50 * u);
        y += 52 * u;

        // Notes.
        Heading("NOTES");
        string[] notes =
        [
            $"Terrain rebuilt from the course's {s.Course.HeightStamps.Count:N0} height stamps",
            "with the designer's brush shapes. Check critical",
            "grades in the game before committing a design.",
            $"Sheet covers {(s.Max.X - s.Min.X) * TerrainSurvey.YardsPerMetre:N0} × {(s.Max.Z - s.Min.Z) * TerrainSurvey.YardsPerMetre:N0} yds.",
        ];
        foreach (var n in notes) { Text(canvas, n, r.Left, y + 4 * u, 6.8f * u, _sansItalic, Grey); y += 10 * u; }
        Text(canvas, Attribution.Text, r.Left, y + 10 * u, 6 * u, _sans, Grey);
    }

    /// <summary>Legend rows for the planting layers that are on and have something in them.</summary>
    void PlantingLegend(SKCanvas canvas, Planting plants, Action<Action<float>, string> row, float sx, float sw)
    {
        float u = _u;
        bool On(PlantingLayers l) => (_o.Planting & l) != 0;
        bool Has(PlantKind k) => On(Planting.LayerOf(k)) && (plants.Singles.Any(p => p.Species.Kind == k)
            || plants.Groups.Any(g => g.Species.Kind == k) || plants.Areas.Any(a => a.Species.Kind == k));
        void Crowns(float cy, SKColor fill, SKColor edge)
        {
            using var f = Fill(fill);
            using var e = Stroke(edge, 0.5f * u);
            foreach (float dx in new[] { 9f, 17f, 25f }) { canvas.DrawCircle(sx + dx * u, cy, 3.4f * u, f); canvas.DrawCircle(sx + dx * u, cy, 3.4f * u, e); }
        }
        if (Has(PlantKind.Tree))
        {
            if (_o.TreesByType)
            {
                row(cy => Crowns(cy, Broadleaf, BroadleafEdge), "Broadleaf tree, drawn to its canopy");
                row(cy => Crowns(cy, Conifer, Paper), "Conifer");
            }
            else row(cy => Crowns(cy, OneTree, Paper), "Tree, drawn to its canopy");
        }
        if (plants.Groups.Any(g => On(Planting.LayerOf(g.Species.Kind))))
            row(cy =>
            {
                using var e = Stroke(OneTree, 0.6f * u);
                using var dash = SKPathEffect.CreateDash([3 * u, 2.2f * u], 0);
                e.PathEffect = dash;
                canvas.DrawCircle(sx + sw / 2, cy, 5.5f * u, e);
            }, "Scatter group, with its count");
        if (plants.Areas.Any(a => On(Planting.LayerOf(a.Species.Kind))))
            row(cy =>
            {
                var box = new SKRect(sx + 4 * u, cy - 5 * u, sx + sw - 4 * u, cy + 5 * u);
                canvas.Save();
                canvas.ClipRect(box);
                using (var h = Stroke(Conifer, 0.5f * u))
                    for (float x = box.Left - box.Height; x < box.Right; x += 3.2f * u) canvas.DrawLine(x, box.Bottom, x + box.Height, box.Top, h);
                canvas.Restore();
                using var e = Stroke(Conifer, 0.7f * u);
                canvas.DrawRect(box, e);
            }, "Planted area, with an estimate (area × fill)");
        if (Has(PlantKind.Grass))
            row(cy =>
            {
                using var p = new SKPaint { Color = GrassInk, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.4f * u };
                canvas.DrawPoints(SKPointMode.Points, [new(sx + 8 * u, cy - 2 * u), new(sx + 14 * u, cy + 2 * u), new(sx + 20 * u, cy - 3 * u),
                    new(sx + 25 * u, cy + 1 * u), new(sx + 11 * u, cy + 3.5f * u), new(sx + 28 * u, cy - 1 * u)], p);
            }, "Grass and fescue");
        if (Has(PlantKind.Bush))
            row(cy => { using var f = Fill(BushInk); canvas.DrawCircle(sx + 12 * u, cy, 1.8f * u, f); canvas.DrawCircle(sx + 22 * u, cy, 1.8f * u, f); }, "Bush");
        if (Has(PlantKind.Flower))
            row(cy => { using var f = Fill(FlowerInk); canvas.DrawCircle(sx + 12 * u, cy, 1.3f * u, f); canvas.DrawCircle(sx + 20 * u, cy, 1.3f * u, f); }, "Flowers");
        if (On(PlantingLayers.Walls) && plants.Walls.Count > 0)
            row(cy =>
            {
                using var w = Stroke(WallInk, 0.9f * u);
                canvas.DrawLine(sx + 2 * u, cy - 2 * u, sx + sw - 2 * u, cy - 2 * u, w);
                using var f = Stroke(WallInk, 0.7f * u);
                using var dash = SKPathEffect.CreateDash([2.4f * u, 1.4f * u], 0);
                f.PathEffect = dash;
                canvas.DrawLine(sx + 2 * u, cy + 3 * u, sx + sw - 2 * u, cy + 3 * u, f);
            }, "Wall (solid) and fence (dashed)");
    }

    void DrawNorthArrow(SKCanvas canvas, SKPoint c)
    {
        float u = _u, len = 14 * u;
        using var b = new SKPathBuilder();
        b.MoveTo(c.X, c.Y - len); b.LineTo(c.X + 5 * u, c.Y + len * 0.6f); b.LineTo(c.X, c.Y + len * 0.25f); b.Close();
        using var right = b.Detach();
        using var b2 = new SKPathBuilder();
        b2.MoveTo(c.X, c.Y - len); b2.LineTo(c.X - 5 * u, c.Y + len * 0.6f); b2.LineTo(c.X, c.Y + len * 0.25f); b2.Close();
        using var left = b2.Detach();
        using var ink = Fill(Ink);
        using var edge = Stroke(Ink, 0.7f * u);
        canvas.DrawPath(right, ink);
        canvas.DrawPath(left, edge);
        Text(canvas, "N", c.X, c.Y - len - 3 * u, 8 * u, _serifBold, Ink, SKTextAlign.Center);
    }

    void DrawScaleBar(SKCanvas canvas, SKPoint at, float maxWidth)
    {
        float u = _u;
        float pxPerYard = (float)(_o.PixelsPerMetre / TerrainSurvey.YardsPerMetre);
        int[] steps = [500, 400, 300, 200, 100];
        int total = steps.FirstOrDefault(t => t * pxPerYard <= maxWidth, 100);
        int div = total % 100 == 0 && total >= 200 ? total / 100 : 4;
        float seg = total * pxPerYard / div, h = 4 * u;
        using var ink = Fill(Ink);
        using var edge = Stroke(Ink, 0.6f * u);
        for (int i = 0; i < div; i++)
        {
            var rect = SKRect.Create(at.X + i * seg, at.Y, seg, h);
            if (i % 2 == 0) canvas.DrawRect(rect, ink);
            canvas.DrawRect(rect, edge);
        }
        for (int i = 0; i <= div; i++)
            Text(canvas, $"{total * i / div}", at.X + i * seg, at.Y + h + 9 * u, 6.4f * u, _sans, Ink, SKTextAlign.Center);
        Text(canvas, "yards", at.X + div * seg + 6 * u, at.Y + h, 6.4f * u, _sansItalic, Grey);
        Text(canvas, $"1 grid square = {_o.GridYards:0} yds  ·  {_o.PixelsPerMetre:0.#} px per metre", at.X, at.Y + h + 22 * u, 6.4f * u, _sansItalic, Grey);
    }
}
