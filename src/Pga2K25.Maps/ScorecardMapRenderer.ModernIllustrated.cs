using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Modern Illustrated theme: Modern's colours with the land painted in (<see cref="Scenery"/>), the map across the
/// whole page and a scorecard strip beneath it, one row per tee set (up to four, longest first) and a Par row, with
/// the holes running 1–9, Out, 10–18, In, Total like a printed scorecard. Always landscape.
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    bool IsModernIllustrated => _t.Kind == MapThemeKind.ModernIllustrated;

    static readonly SKColor Fairway = SKColor.Parse("#b9c873"), FairwayStripe = SKColor.Parse("#c6d382");
    static readonly SKColor GreenCollar = SKColor.Parse("#86a046"), TeeEdge = SKColor.Parse("#7f9640");
    static readonly SKColor BunkerLip = SKColor.Parse("#8c6a2f"), BunkerLit = SKColor.Parse("#f3c27a");
    static readonly SKColor Shore = SKColor.Parse("#d6eae0"), Ripple = SKColor.Parse("#8fc4b6");
    static readonly SKColor Card = SKColor.Parse("#fbf4dc"), CardBand = SKColor.Parse("#efe3bf"), CardRule = SKColor.Parse("#d9c99a");

    void DrawModernIllustrated(SKCanvas canvas, string courseName, CourseModel course, CourseLayers layers, MapProjection proj, SKSize page, SKRect mapRect)
    {
        _scenery ??= Scenery.Build(course, proj, mapRect);
        _hasInlandWater = layers.WaterAreas.Count + layers.WaterBodies.Count + layers.WaterRibbons.Count > 0;
        canvas.Save();
        canvas.ClipRect(mapRect);
        _scenery.DrawGround(canvas);
        DrawIllustratedCourse(canvas, layers, proj, mapRect);
        canvas.Restore();
        DrawHoleNumbers(canvas, layers, proj, mapRect);

        DrawRoses(canvas, layers, proj, mapRect);
        var bar = new SKPoint(mapRect.Left + 18, mapRect.Bottom - 12);
        float hundred = (float)(100 * Yard * proj.PointsPerMetre);
        using (var panel = Fill(Scenery.SandLit)) canvas.DrawRoundRect(new SKRect(bar.X - 8, bar.Y - 16, bar.X + 2 * hundred + 40, bar.Y + 7), 4, 4, panel);
        DrawScaleBar(canvas, proj, bar);

        DrawStrip(canvas, courseName, course, page);
    }

    void DrawIllustratedCourse(SKCanvas canvas, CourseLayers l, MapProjection p, SKRect mapRect)
    {
        float k = p.PointsPerMetre;
        if (_o.ShowWaste) DrawWaste(canvas, l, p);

        // Rough, then the fairways mown lighter with diagonal stripes.
        FillUnion(canvas, l.Grass, p, _t.GrassFill, null, 0);
        {
            using var fairways = UnionPath(l.Fairways, p);
            using (var fill = Fill(Fairway)) canvas.DrawPath(fairways, fill);
            canvas.Save();
            canvas.ClipPath(fairways, antialias: true);
            using var stripe = Stroke(FairwayStripe, 2.6f);
            stripe.StrokeCap = SKStrokeCap.Butt;
            for (float d = -mapRect.Height; d < mapRect.Width; d += 6.5f)
                canvas.DrawLine(mapRect.Left + d, mapRect.Top, mapRect.Left + d + mapRect.Height * 0.8f, mapRect.Bottom, stripe);
            canvas.Restore();
        }

        // Water: a pale shore band, the water, and short ripple strokes on it.
        using (var b = new SKPathBuilder { FillType = SKPathFillType.EvenOdd })
        {
            foreach (var a in l.WaterAreas) p.AddPolyline(b, a, true);
            foreach (var shore in l.WaterBodies.SelectMany(body => body)) p.AddPolyline(b, shore, true);
            using var water = b.Detach();
            using (var band = Stroke(Shore, 2.4f)) canvas.DrawPath(water, band);
            using (var fill = Fill(_t.WaterFill)) canvas.DrawPath(water, fill);
            using var shoreStroke = Stroke(Shore, 1);
            using var fillStroke = Stroke(_t.WaterFill, 1);
            foreach (var r in l.WaterRibbons)
            {
                using var path = p.Path(r.Points, false);
                shoreStroke.StrokeWidth = (float)(r.Width * k) + 2.4f;
                canvas.DrawPath(path, shoreStroke);
                fillStroke.StrokeWidth = (float)(r.Width * k);
                canvas.DrawPath(path, fillStroke);
            }
            if (!water.IsEmpty)
            {
                canvas.Save();
                canvas.ClipPath(water, antialias: true);
                var box = SKRect.Intersect(water.Bounds, mapRect);
                using var ripple = Stroke(Ripple, 0.5f);
                var rnd = new Random(3);
                for (float y = box.Top; y < box.Bottom; y += 3.2f)
                    for (float x = box.Left; x < box.Right; x += 7f)
                    {
                        float jx = x + (float)rnd.NextDouble() * 6, jy = y + (float)(rnd.NextDouble() - 0.5) * 2;
                        canvas.DrawLine(jx, jy, jx + 2.6f, jy, ripple);
                    }
                canvas.Restore();
            }
        }

        foreach (var h in l.Holes)
            foreach (var tee in h.Hole.Tees)
                DrawTeePad(canvas, p, tee, h.Direction);

        // Greens with a lighter collar.
        var greens = l.Greens.Select(g => p.Path(g, true)).ToList();
        using (var collar = Stroke(GreenCollar, 2.2f)) foreach (var g in greens) canvas.DrawPath(g, collar);
        using (var fill = Fill(_t.GreenFill)) foreach (var g in greens) canvas.DrawPath(g, fill);
        foreach (var g in greens) g.Dispose();

        // Bunkers: a dark lip, the sand, and the far side lit.
        using (var bunkers = BunkerPath(l, p))
        {
            using (var lip = Stroke(BunkerLip, 0.9f)) canvas.DrawPath(bunkers, lip);
            using (var sand = Fill(_t.BunkerFill)) canvas.DrawPath(bunkers, sand);
            canvas.Save();
            canvas.ClipPath(bunkers, antialias: true);
            canvas.Translate(0.7f, 0.7f);
            using (var lit = Fill(BunkerLit)) canvas.DrawPath(bunkers, lit);
            canvas.Restore();
        }

        if (_o.ShowPaths)
        {
            using var road = Stroke(_t.RoadColour, 1);
            foreach (var r in l.TrimToPlay(l.Roads, _o.PathReachMetres)) { road.StrokeWidth = Math.Max(1f, (float)(r.Width * k)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, road); }
            using var cart = Stroke(_t.PathColour, 1);
            foreach (var r in l.TrimToPlay(l.Paths, _o.PathReachMetres)) { cart.StrokeWidth = Math.Max(0.8f, (float)(r.Width * k)); using var path = p.Path(r.Points, false); canvas.DrawPath(path, cart); }
        }

        _scenery!.DrawPlanting(canvas);

        using var route = Stroke(_t.RouteColour, 0.7f);
        foreach (var h in l.Holes) { using var path = p.Path(h.Route, false); canvas.DrawPath(path, route); }
    }

    /// <summary>Bunkers as one fillable path: the merged outlines, or (with merging off) the raw areas, dabs and ribbons.</summary>
    SKPath BunkerPath(CourseLayers l, MapProjection p)
    {
        using var b = new SKPathBuilder { FillType = _o.MergeBunkers ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
        if (_o.MergeBunkers)
        {
            foreach (var loop in l.MergedBunkers(_o.BunkerMergeMetres))
            {
                b.MoveTo(p.Map(loop[0]));
                for (int i = 1; i < loop.Count; i++) b.LineTo(p.Map(loop[i]));
                b.Close();
            }
            return b.Detach();
        }
        float k = p.PointsPerMetre;
        foreach (var a in l.BunkerAreas) p.AddPolyline(b, a, true);
        foreach (var d in l.BunkerBrushes) { var c = p.Map(d.Centre); b.AddCircle(c.X, c.Y, Math.Max(0.6f, (float)(d.Radius * k * 1.25))); }
        using var stroke = Stroke(_t.BunkerFill, 1);
        foreach (var r in l.BunkerRibbons)
        {
            stroke.StrokeWidth = Math.Max(0.8f, (float)(r.Width * k));
            using var line = p.Path(r.Points, false);
            using var outline = stroke.GetFillPath(line);
            if (outline is not null) b.AddPath(outline);
        }
        return b.Detach();
    }

    /// <summary>
    /// Closed outlines filled as one shape. Each is unioned in on its own: in a single path, the overlap of two
    /// splines drawn round opposite ways would cancel and leave a hole (Sangoma's overlapping fairways).
    /// </summary>
    public static SKPath UnionPath(IEnumerable<IReadOnlyList<Vec2>> shapes, MapProjection p)
    {
        var union = new SKPath();
        foreach (var s in shapes)
        {
            using var one = p.Path(s, true);
            if (union.Op(one, SKPathOp.Union) is { } next) { union.Dispose(); union = next; }
        }
        return union;
    }

    void DrawStripLegend(SKCanvas canvas, SKRect r)
    {
        var s = _scenery!;
        var items = new List<(string Label, Action<SKPoint> Draw)>
        {
            ("Fairway", q => { using var f = Fill(Fairway); canvas.DrawRoundRect(q.X - 6, q.Y - 4, 12, 8, 2, 2, f); }),
            ("Rough", q => { using var f = Fill(_t.GrassFill); canvas.DrawRoundRect(q.X - 6, q.Y - 4, 12, 8, 2, 2, f); }),
            ("Green", q => { using var f = Fill(_t.GreenFill); canvas.DrawOval(q, new SKSize(6, 4), f); }),
            ("Bunker", q => { using var f = Fill(_t.BunkerFill); canvas.DrawOval(q, new SKSize(6, 3.5f), f); }),
        };
        if (_o.ShowWaste)
            items.Add(("Waste area", q => { using var f = Fill(_t.WasteFill); canvas.DrawRoundRect(q.X - 6, q.Y - 4, 12, 8, 2, 2, f); }));
        if (s.HasSea || _hasInlandWater)
            items.Add(("Water", q => { using var f = Fill(_t.WaterFill); canvas.DrawOval(q, new SKSize(6, 4), f); }));
        using var body = new SKPaint { IsAntialias = true };
        using var lit = new SKPaint { IsAntialias = true };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.3f };
        if (s.HasBroadleaf) items.Add(("Trees", q => Scenery.DrawTree(canvas, q, 4.5f, false, body, lit, edge)));
        if (s.HasConifers) items.Add(("Conifers", q => Scenery.DrawTree(canvas, q, 4.8f, true, body, lit, edge)));
        if (s.Bushes.Count > 0)
            items.Add(("Bushes", q =>
            {
                using var f = Fill(Scenery.Bush);
                canvas.DrawCircle(q.X - 3, q.Y, 2, f); canvas.DrawCircle(q.X + 2, q.Y - 1, 2.2f, f); canvas.DrawCircle(q.X + 1, q.Y + 2, 1.8f, f);
            }));
        if (s.Tufts.Count + s.Flowers.Count > 0)
            items.Add((s.Flowers.Count == 0 ? "Grasses" : s.Tufts.Count == 0 ? "Flowers" : "Grasses & flowers", q =>
            {
                using var stroke = Stroke(Scenery.Tuft, 0.6f);
                for (int i = -1; i <= 1; i++) canvas.DrawLine(q.X + i * 3, q.Y + 3, q.X + i * 3.6f, q.Y - 3, stroke);
                if (s.Flowers.Count > 0) { using var f = Fill(SKColor.Parse("#d9677e")); canvas.DrawCircle(q.X + 5, q.Y - 2, 1.2f, f); }
            }));

        int rowsPerColumn = (items.Count + 1) / 2;
        float rowH = Math.Min(17, r.Height / Math.Max(1, rowsPerColumn));
        for (int i = 0; i < items.Count; i++)
        {
            var q = new SKPoint(r.Left + 7 + (i / rowsPerColumn) * (r.Width / 2), r.Top + rowH * (i % rowsPerColumn) + rowH / 2);
            items[i].Draw(q);
            Text(canvas, items[i].Label, q.X + 12, q.Y + 3.4f, 9.5f, _body, _t.Ink);
        }
    }

    bool _hasInlandWater;

}
