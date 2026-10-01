using System.Runtime.InteropServices;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>
/// Draws the terrain planning map: a north-up topographic sheet (relief or slope shading, minor and
/// labelled index contours, hilltop spot heights, lettered reference grid) with the routing on top as
/// sticks and dots, and a side panel with the hole table, legend, scale and north arrow.
/// All sizes are in output pixels; <c>_u</c> is pixels per typographic point, tied to the map scale so
/// the sheet reads the same at any resolution.
/// </summary>
public sealed class TerrainMapRenderer : IDisposable
{
    static readonly SKColor Paper = SKColor.Parse("#fbf8f1");
    static readonly SKColor Ink = SKColor.Parse("#1f1d1a");
    static readonly SKColor Grey = SKColor.Parse("#6b665e");
    static readonly SKColor Rule = SKColor.Parse("#cfc8bb");
    static readonly SKColor MinorContour = SKColor.Parse("#b98a63");
    static readonly SKColor IndexContour = SKColor.Parse("#8a4f28");
    static readonly SKColor GridLine = SKColor.Parse("#56708c");
    static readonly SKColor GreenFill = SKColor.Parse("#8cc07a");
    static readonly SKColor GreenEdge = SKColor.Parse("#3c6e2f");
    static readonly SKColor Flag = SKColor.Parse("#c0392b");
    static readonly SKColor Sea = SKColor.Parse("#b7d5e8");
    static readonly SKColor Shore = SKColor.Parse("#3f7fae");

    /// <summary>Relief tint, low → high: pale meadow green to pale tan.</summary>
    static readonly (double T, SKColor C)[] Hypsometric =
    [
        (0.00, SKColor.Parse("#dde8cf")), (0.30, SKColor.Parse("#ecf0d6")), (0.55, SKColor.Parse("#f4ecd2")),
        (0.80, SKColor.Parse("#ecdcc2")), (1.00, SKColor.Parse("#e2d0bf")),
    ];

    /// <summary>Slope classes for planning: green sites, comfortable fairway, steep, needs grading, severe.</summary>
    public static readonly (double MaxPct, SKColor C, string Label)[] SlopeClasses =
    [
        (3, SKColor.Parse("#cfe6c4"), "0–3 %  green sites"),
        (8, SKColor.Parse("#edf1cf"), "3–8 %  fairway"),
        (15, SKColor.Parse("#f6e3c0"), "8–15 %  steep for play"),
        (25, SKColor.Parse("#f0c6ae"), "15–25 %  needs grading"),
        (double.MaxValue, SKColor.Parse("#d9a9a6"), "over 25 %  severe"),
    ];

    readonly TerrainMapOptions _o;
    readonly float _u;
    readonly SKTypeface _serif, _serifBold, _serifItalic, _sans, _sansBold, _sansItalic;

    public TerrainMapRenderer(TerrainMapOptions options)
    {
        _o = options;
        _u = (float)(options.PixelsPerMetre * 0.87);
        string serif = Resolve("Lora", "Georgia", "Cambria", "Times New Roman");
        string sans = Resolve("Segoe UI", "Arial", "Helvetica");
        _serif = SKTypeface.FromFamilyName(serif, SKFontStyle.Normal);
        _serifBold = SKTypeface.FromFamilyName(serif, SKFontStyle.Bold);
        _serifItalic = SKTypeface.FromFamilyName(serif, SKFontStyle.Italic);
        _sans = SKTypeface.FromFamilyName(sans, SKFontStyle.Normal);
        _sansBold = SKTypeface.FromFamilyName(sans, SKFontStyle.Bold);
        _sansItalic = SKTypeface.FromFamilyName(sans, SKFontStyle.Italic);
    }

    static string Resolve(params string[] families) =>
        families.FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase))
        ?? SKTypeface.Default.FamilyName;

    public void Dispose()
    {
        _serif.Dispose(); _serifBold.Dispose(); _serifItalic.Dispose();
        _sans.Dispose(); _sansBold.Dispose(); _sansItalic.Dispose();
    }

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

    public void Draw(SKCanvas canvas, TerrainSurvey s)
    {
        var l = Plan(s);
        canvas.Clear(Paper);

        canvas.Save();
        canvas.ClipRect(l.Map);
        if (_o.Shading != ReliefShading.None || s.SeaLevelMetres is not null) DrawShading(canvas, s, l);
        if (_o.ShowFeatures) DrawFeatures(canvas, s, l.Projection);
        if (s.Shoreline.Count > 0)
        {
            using var shore = Stroke(Shore, 0.9f * _u);
            using var b = new SKPathBuilder();
            foreach (var line in s.Shoreline) l.Projection.AddPolyline(b, line, close: false, tolerance: 0.35f);
            using var path = b.Detach();
            canvas.DrawPath(path, shore);
        }
        var routes = s.Holes.Select(h => h.Route.Select(l.Projection.Map).ToArray()).ToList();
        var obstacles = new List<SKRect>();
        var summitLabels = _o.ShowSummits ? PlaceSummits(s, l, obstacles) : [];
        DrawContours(canvas, s, l, routes, obstacles);
        DrawSummits(canvas, summitLabels);
        if (_o.GridYards > 0) DrawGridLines(canvas, l);
        DrawRouting(canvas, s, l, routes, obstacles);
        canvas.Restore();

        using (var frame = Stroke(Ink, 0.9f * _u)) canvas.DrawRect(l.Map, frame);
        if (_o.GridYards > 0) DrawGridLabels(canvas, l);
        DrawTitle(canvas, s, l);
        DrawPanel(canvas, s, l);
    }

    // ───────────────────────────── terrain ─────────────────────────────

    void DrawShading(SKCanvas canvas, TerrainSurvey s, Layout l)
    {
        var f = s.Field;
        int w = f.Width, h = f.Height;
        double cell = f.Cell, range = Math.Max(1e-6, s.MaxFeet / TerrainSurvey.FeetPerMetre);
        // Soft north-west light; heights exaggerated a little because golf-scale relief is subtle.
        const double z = 1.5, alt = 45 * Math.PI / 180, strength = 0.45;
        double lx = -Math.Cos(alt) / Math.Sqrt(2), ly = -Math.Cos(alt) / Math.Sqrt(2), lz = Math.Sin(alt);

        var pixels = new int[w * h];
        Parallel.For(0, h, iz =>
        {
            int zm = Math.Max(0, iz - 1), zp = Math.Min(h - 1, iz + 1);
            for (int ix = 0; ix < w; ix++)
            {
                int xm = Math.Max(0, ix - 1), xp = Math.Min(w - 1, ix + 1);
                double gx = (f[xp, iz] - f[xm, iz]) / ((xp - xm) * cell);   // dh/dX
                double gz = (f[ix, zp] - f[ix, zm]) / ((zp - zm) * cell);   // dh/dZ
                // Page axes: right = −X (east), down = +Z (south).
                double px = -gx * z, py = gz * z;
                double shade = (-px * lx - py * ly + lz) / Math.Sqrt(px * px + py * py + 1);
                double rel = Math.Clamp(shade / lz, 0, 1.12);

                if (s.IsWater(f[ix, iz]))
                {
                    pixels[iz * w + (w - 1 - ix)] = unchecked((int)(0xFF000000u | (uint)Sea.Red << 16 | (uint)Sea.Green << 8 | Sea.Blue));
                    continue;
                }
                SKColor c = _o.Shading switch
                {
                    ReliefShading.Slope => SlopeClasses.First(k => Math.Sqrt(gx * gx + gz * gz) * 100 < k.MaxPct).C,
                    ReliefShading.None => Paper,
                    _ => Ramp((f[ix, iz] - s.DatumMetres) / range),
                };
                double m = _o.Shading == ReliefShading.None ? 1 : 1 - strength * (1 - rel);
                byte r = (byte)Math.Clamp(c.Red * m, 0, 255), g = (byte)Math.Clamp(c.Green * m, 0, 255), b = (byte)Math.Clamp(c.Blue * m, 0, 255);
                pixels[iz * w + (w - 1 - ix)] = unchecked((int)(0xFF000000u | (uint)r << 16 | (uint)g << 8 | b));
            }
        });

        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        var a = l.Projection.Map(f.Origin);
        var b2 = l.Projection.Map(f.Origin + new Vec2(w * cell, h * cell));
        var dest = new SKRect(Math.Min(a.X, b2.X), Math.Min(a.Y, b2.Y), Math.Max(a.X, b2.X), Math.Max(a.Y, b2.Y));
        canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    static SKColor Ramp(double t)
    {
        t = Math.Clamp(t, 0, 1);
        for (int i = 1; i < Hypsometric.Length; i++)
        {
            if (t > Hypsometric[i].T) continue;
            var (t0, c0) = Hypsometric[i - 1];
            var (t1, c1) = Hypsometric[i];
            double k = (t - t0) / (t1 - t0);
            return new SKColor((byte)(c0.Red + (c1.Red - c0.Red) * k), (byte)(c0.Green + (c1.Green - c0.Green) * k), (byte)(c0.Blue + (c1.Blue - c0.Blue) * k));
        }
        return Hypsometric[^1].C;
    }

    void DrawFeatures(SKCanvas canvas, TerrainSurvey s, MapProjection p)
    {
        var L = s.Layers;
        float u = _u;
        using var water = Fill(SKColor.Parse("#9cc3dd").WithAlpha(170));
        using var waterEdge = Stroke(SKColor.Parse("#4f86ad"), 0.6f * u);
        using var sand = Fill(SKColor.Parse("#ecdcae").WithAlpha(200));
        using var sandEdge = Stroke(SKColor.Parse("#a98d4d"), 0.5f * u);
        using var green = Fill(GreenFill.WithAlpha(110));
        using var greenEdge = Stroke(GreenEdge, 0.6f * u);
        using var grassEdge = Stroke(SKColor.Parse("#4f7f3f").WithAlpha(190), 0.6f * u);
        using var dash = SKPathEffect.CreateDash([4 * u, 2.5f * u], 0);
        grassEdge.PathEffect = dash;

        foreach (var poly in L.WaterAreas) { using var path = p.Path(poly, true); canvas.DrawPath(path, water); canvas.DrawPath(path, waterEdge); }
        foreach (var r in L.WaterRibbons) DrawRibbon(canvas, p, r, SKColor.Parse("#9cc3dd"));
        foreach (var poly in L.Grass) { using var path = p.Path(poly, true); canvas.DrawPath(path, grassEdge); }
        foreach (var poly in L.Greens) { using var path = p.Path(poly, true); canvas.DrawPath(path, green); canvas.DrawPath(path, greenEdge); }
        // Waste areas (rock and scrub) are left off: this sheet is for planning the golf.
        foreach (var poly in L.BunkerAreas) { using var path = p.Path(poly, true); canvas.DrawPath(path, sand); canvas.DrawPath(path, sandEdge); }
        foreach (var r in L.BunkerRibbons) DrawRibbon(canvas, p, r, SKColor.Parse("#ecdcae"));
        foreach (var d in L.BunkerBrushes)
            canvas.DrawCircle(p.Map(d.Centre), (float)(d.Radius * p.PointsPerMetre), sand);
        foreach (var r in L.Roads.Concat(L.Paths)) DrawRibbon(canvas, p, r, SKColor.Parse("#9a958c").WithAlpha(170));
    }

    static void DrawRibbon(SKCanvas canvas, MapProjection p, Ribbon r, SKColor c)
    {
        using var path = p.Path(r.Points, false);
        using var paint = Stroke(c, (float)(r.Width * p.PointsPerMetre));
        paint.StrokeCap = SKStrokeCap.Round;
        canvas.DrawPath(path, paint);
    }

    sealed record ContourLabel(SKPoint At, float Degrees, string Text, SKRect Box);

    void DrawContours(SKCanvas canvas, TerrainSurvey s, Layout l, List<SKPoint[]> routes, List<SKRect> obstacles)
    {
        float u = _u, size = 5.6f * u;
        var p = l.Projection;
        using var font = new SKFont(_sans, size) { Subpixel = true };
        var safe = l.Map;
        safe.Inflate(-10 * u, -10 * u);

        // Labels first so the lines can break around them.
        var labels = new List<ContourLabel>();
        foreach (var c in s.Contours.Where(c => c.Index).OrderByDescending(c => c.Points.Count))
        {
            var pts = c.Points.Select(p.Map).ToArray();
            var cum = Cumulative(pts);
            float total = cum[^1];
            string text = $"{c.Feet:0}";
            float half = font.MeasureText(text) / 2 + 2.5f * u;
            if (total < 6 * half) continue;
            float spacing = 190 * u;
            for (float at = Math.Min(70 * u, total / 2); at < total - half; at += spacing)
            {
                if (at < half) continue;
                var a = PointAt(pts, cum, at - half);
                var b = PointAt(pts, cum, at + half);
                var mid = PointAt(pts, cum, at);
                if (SKPoint.Distance(a, b) < 2 * half * 0.94f) continue;               // too curvy here
                if (!safe.Contains(mid)) continue;
                var box = SKRect.Create(mid.X - half, mid.Y - size * 0.7f, 2 * half, size * 1.4f);
                if (obstacles.Any(o => o.IntersectsWith(Inflate(box, 6 * u)))) continue;
                if (routes.Any(r => DistanceToPolyline(mid, r) < half + 6 * u)) continue;
                float deg = (float)(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
                if (deg > 90) deg -= 180; else if (deg < -90) deg += 180;
                labels.Add(new ContourLabel(mid, deg, text, box));
                obstacles.Add(box);
            }
        }

        using var gaps = new SKPathBuilder();
        foreach (var lab in labels)
        {
            var m = SKMatrix.CreateRotationDegrees(lab.Degrees, lab.At.X, lab.At.Y);
            float half = lab.Box.Width / 2, hh = size * 0.62f;
            var corners = m.MapPoints([new SKPoint(lab.At.X - half, lab.At.Y - hh), new SKPoint(lab.At.X + half, lab.At.Y - hh),
                                       new SKPoint(lab.At.X + half, lab.At.Y + hh), new SKPoint(lab.At.X - half, lab.At.Y + hh)]);
            gaps.MoveTo(corners[0]);
            for (int i = 1; i < 4; i++) gaps.LineTo(corners[i]);
            gaps.Close();
        }
        using var gapPath = gaps.Detach();

        canvas.Save();
        canvas.ClipPath(gapPath, SKClipOperation.Difference, antialias: true);
        using (var minor = Stroke(MinorContour.WithAlpha(185), 0.38f * u))
        using (var minorPath = Build(s.Contours.Where(c => !c.Index), p))
            canvas.DrawPath(minorPath, minor);
        using (var index = Stroke(IndexContour.WithAlpha(235), 0.95f * u))
        using (var indexPath = Build(s.Contours.Where(c => c.Index), p))
            canvas.DrawPath(indexPath, index);
        canvas.Restore();

        using var ink = new SKPaint { Color = IndexContour, IsAntialias = true };
        foreach (var lab in labels)
        {
            canvas.Save();
            canvas.RotateDegrees(lab.Degrees, lab.At.X, lab.At.Y);
            canvas.DrawText(lab.Text, lab.At.X, lab.At.Y + size * 0.36f, SKTextAlign.Center, font, ink);
            canvas.Restore();
        }
    }

    static SKPath Build(IEnumerable<ContourLine> lines, MapProjection p)
    {
        using var b = new SKPathBuilder();
        foreach (var c in lines) p.AddPolyline(b, c.Points, close: false, tolerance: 0.35f);
        return b.Detach();
    }

    List<(SKPoint At, string Text, SKRect Box)> PlaceSummits(TerrainSurvey s, Layout l, List<SKRect> obstacles)
    {
        float u = _u, size = 5.8f * u;
        using var font = new SKFont(_sansItalic, size);
        var result = new List<(SKPoint, string, SKRect)>();
        foreach (var sp in s.Summits)
        {
            var at = l.Projection.Map(sp.Position);
            string text = $"{sp.Feet:0}";
            float w = font.MeasureText(text);
            var box = new SKRect(at.X - 3 * u, at.Y - 3.5f * u, at.X + 4.5f * u + w, at.Y + 3.5f * u);
            if (!l.Map.Contains(box) || obstacles.Any(o => o.IntersectsWith(box))) continue;
            obstacles.Add(box);
            result.Add((at, text, box));
        }
        return result;
    }

    void DrawSummits(SKCanvas canvas, List<(SKPoint At, string Text, SKRect Box)> summits)
    {
        float u = _u;
        using var fill = Fill(Ink);
        foreach (var (at, text, _) in summits)
        {
            using var b = new SKPathBuilder();
            b.MoveTo(at.X, at.Y - 2.2f * u); b.LineTo(at.X + 2 * u, at.Y + 1.4f * u); b.LineTo(at.X - 2 * u, at.Y + 1.4f * u); b.Close();
            using var tri = b.Detach();
            canvas.DrawPath(tri, fill);
            Text(canvas, text, at.X + 3.2f * u, at.Y + 2 * u, 5.8f * u, _sansItalic, Ink, halo: Paper);
        }
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

    public static string ColumnName(int i) => i < 26 ? ((char)('A' + i)).ToString() : ColumnName(i / 26 - 1) + (char)('A' + i % 26);

    // ───────────────────────────── routing ─────────────────────────────

    void DrawRouting(SKCanvas canvas, TerrainSurvey s, Layout l, List<SKPoint[]> routes, List<SKRect> obstacles)
    {
        float u = _u;
        var p = l.Projection;
        using var casing = Stroke(Paper.WithAlpha(225), 3.8f * u);
        using var line = Stroke(Ink, 1.3f * u);
        casing.StrokeCap = line.StrokeCap = SKStrokeCap.Round;
        casing.StrokeJoin = line.StrokeJoin = SKStrokeJoin.Round;

        foreach (var r in routes)
        {
            using var b = new SKPathBuilder();
            b.MoveTo(r[0]);
            foreach (var pt in r.Skip(1)) b.LineTo(pt);
            using var path = b.Detach();
            canvas.DrawPath(path, casing);
            canvas.DrawPath(path, line);
        }

        for (int i = 0; i < s.Holes.Count; i++) DrawTicks(canvas, s.Holes[i], routes[i], p);

        // Greens, tees and aim points over every line so crossings never hide a dot.
        for (int i = 0; i < s.Holes.Count; i++)
        {
            var h = s.Holes[i];
            var r = routes[i];
            DrawGreen(canvas, h, r[^1], p);
            for (int k = 1; k < r.Length - 1; k++) DrawAimPoint(canvas, r[k]);
            using var tee = Fill(Ink);
            using var ring = Stroke(Paper, 0.8f * u);
            canvas.DrawCircle(r[0], 2.9f * u, tee);
            canvas.DrawCircle(r[0], 2.9f * u, ring);
        }

        // Labels last, placed clear of every route and of each other.
        foreach (var r in routes)
            foreach (var pt in r) obstacles.Add(new SKRect(pt.X - 4 * u, pt.Y - 4 * u, pt.X + 4 * u, pt.Y + 4 * u));
        for (int i = 0; i < s.Holes.Count; i++) DrawAimLabels(canvas, s.Holes[i], routes[i], routes, obstacles);
        for (int i = 0; i < s.Holes.Count; i++) DrawBadge(canvas, s.Holes[i], routes[i], routes, obstacles);
    }

    void DrawTicks(SKCanvas canvas, HoleRouting h, SKPoint[] r, MapProjection p)
    {
        if (_o.TickYards <= 0) return;
        float u = _u;
        var cum = Cumulative(r);
        float pxPerYard = (float)(p.PointsPerMetre / TerrainSurvey.YardsPerMetre);
        float total = cum[^1];
        using var tick = Stroke(Ink, 0.8f * u);
        for (int k = 1; ; k++)
        {
            float at = k * _o.TickYards * pxPerYard;
            if (at > total - 25 * pxPerYard) break;
            var pt = PointAt(r, cum, at);
            var dir = Tangent(r, cum, at);
            var n = new SKPoint(-dir.Y, dir.X);
            bool major = k % 2 == 0;
            float len = (major ? 3.6f : 2.4f) * u;
            canvas.DrawLine(pt.X - n.X * len, pt.Y - n.Y * len, pt.X + n.X * len, pt.Y + n.Y * len, tick);
            if (!major) continue;
            var lp = new SKPoint(pt.X + n.X * 8 * u, pt.Y + n.Y * 8 * u + 1.9f * u);
            Text(canvas, $"{k * _o.TickYards}", lp.X, lp.Y, 5.2f * u, _sans, Ink, SKTextAlign.Center, halo: Paper);
        }
    }

    void DrawGreen(SKCanvas canvas, HoleRouting h, SKPoint at, MapProjection p)
    {
        float u = _u;
        float radius = Math.Max(3.5f * u, (float)(_o.GreenSiteRadiusMetres * p.PointsPerMetre));
        using (var fill = Fill(GreenFill.WithAlpha(150))) canvas.DrawCircle(at, radius, fill);
        using (var edge = Stroke(GreenEdge, 0.9f * u)) canvas.DrawCircle(at, radius, edge);

        // Fall arrow across the green site, when it falls by more than 1 %.
        if (h.GreenSiteSlopePct > 1 && h.GreenSiteFallDirection.Length > 0)
        {
            var d = p.MapDirection(h.GreenSiteFallDirection);
            float len = radius * 0.72f;
            var a = new SKPoint(at.X - d.X * len, at.Y - d.Y * len);
            var b = new SKPoint(at.X + d.X * len, at.Y + d.Y * len);
            using var arrow = Stroke(GreenEdge, 0.8f * u);
            canvas.DrawLine(a, b, arrow);
            var n = new SKPoint(-d.Y, d.X);
            float head = 2.6f * u;
            using var hb = new SKPathBuilder();
            hb.MoveTo(b);
            hb.LineTo(b.X - d.X * head + n.X * head * 0.6f, b.Y - d.Y * head + n.Y * head * 0.6f);
            hb.LineTo(b.X - d.X * head - n.X * head * 0.6f, b.Y - d.Y * head - n.Y * head * 0.6f);
            hb.Close();
            using var headPath = hb.Detach();
            using var headFill = Fill(GreenEdge);
            canvas.DrawPath(headPath, headFill);
        }

        // Flag: stick from the anchor, pennant to the right.
        using var stick = Stroke(Ink, 0.7f * u);
        canvas.DrawLine(at.X, at.Y, at.X, at.Y - 11 * u, stick);
        using var fb = new SKPathBuilder();
        fb.MoveTo(at.X, at.Y - 11 * u); fb.LineTo(at.X + 6 * u, at.Y - 9 * u); fb.LineTo(at.X, at.Y - 7 * u); fb.Close();
        using var flagPath = fb.Detach();
        using var flag = Fill(Flag);
        canvas.DrawPath(flagPath, flag);
        using var cup = Fill(Ink);
        canvas.DrawCircle(at, 1.2f * u, cup);
    }

    void DrawAimPoint(SKCanvas canvas, SKPoint at)
    {
        float u = _u;
        using var fill = Fill(SKColors.White);
        using var edge = Stroke(Ink, 1.0f * u);
        canvas.DrawCircle(at, 2.7f * u, fill);
        canvas.DrawCircle(at, 2.7f * u, edge);
    }

    /// <summary>Aim points: "from tee · to green" yards, on the outside of the turn.</summary>
    void DrawAimLabels(SKCanvas canvas, HoleRouting h, SKPoint[] r, List<SKPoint[]> routes, List<SKRect> obstacles)
    {
        float u = _u, size = 5.8f * u;
        int fromTee = 0;
        for (int k = 1; k < r.Length - 1; k++)
        {
            fromTee += h.LegYards[k - 1];
            int toGreen = h.LegYards.Skip(k).Sum();
            string text = $"{fromTee} · {toGreen}";
            var inDir = Unit(r[k] - r[k - 1]);
            var outDir = Unit(r[k + 1] - r[k]);
            var outside = Unit(new SKPoint(inDir.X - outDir.X, inDir.Y - outDir.Y));
            if (outside.Length < 0.1f) outside = new SKPoint(-inDir.Y, inDir.X);
            PlaceText(canvas, text, r[k], 7 * u, size, _sansBold, Ink, routes, obstacles, preferred: outside);
        }
    }

    void DrawBadge(SKCanvas canvas, HoleRouting h, SKPoint[] r, List<SKPoint[]> routes, List<SKRect> obstacles)
    {
        float u = _u, radius = 7.2f * u;
        var dir = Unit(r[1] - r[0]);
        var centre = new SKPoint(r[0].X - dir.X * (radius + 4.5f * u), r[0].Y - dir.Y * (radius + 4.5f * u));
        using (var fill = Fill(Ink)) canvas.DrawCircle(centre, radius, fill);
        using (var ring = Stroke(Paper, 0.9f * u)) canvas.DrawCircle(centre, radius, ring);
        Text(canvas, h.Hole.Number.ToString(), centre.X, centre.Y + 3.1f * u, 8.6f * u, _sansBold, SKColors.White, SKTextAlign.Center);
        obstacles.Add(new SKRect(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius));

        string change = Math.Abs(h.ChangeFeet) < 0.5 ? "level" : $"{(h.ChangeFeet > 0 ? "▲" : "▼")} {Math.Abs(h.ChangeFeet):0} ft";
        string text = $"Par {h.Hole.Par} · {h.Yards} y · {change}";
        PlaceText(canvas, text, centre, radius + 3 * u, 6.2f * u, _sansBold, Ink, routes, obstacles, preferred: new SKPoint(-dir.X, -dir.Y));
    }

    /// <summary>
    /// Writes a haloed label next to <paramref name="anchor"/>, trying 16 directions (best first: clear of
    /// obstacles, far from routes, close to <paramref name="preferred"/>) and records its box.
    /// </summary>
    void PlaceText(SKCanvas canvas, string text, SKPoint anchor, float offset, float size, SKTypeface face, SKColor colour,
        List<SKPoint[]> routes, List<SKRect> obstacles, SKPoint preferred)
    {
        using var font = new SKFont(face, size);
        float w = font.MeasureText(text), hgt = size * 1.1f;
        SKRect best = default;
        double bestScore = double.MinValue;
        for (int i = 0; i < 16; i++)
        {
            double a = i * Math.PI / 8;
            var d = new SKPoint((float)Math.Cos(a), (float)Math.Sin(a));
            // Box edge sits `offset` from the anchor along d.
            float cx = anchor.X + d.X * (offset + w / 2 * Math.Abs(d.X)), cy = anchor.Y + d.Y * (offset + hgt / 2 * Math.Abs(d.Y));
            var box = SKRect.Create(cx - w / 2, cy - hgt / 2, w, hgt);
            double clear = routes.Min(r => DistanceToPolyline(new SKPoint(box.MidX, box.MidY), r)) - Math.Max(w, hgt) / 2;
            double score = Math.Min(clear, 12 * _u) + 4 * _u * (d.X * preferred.X + d.Y * preferred.Y)
                           - (obstacles.Any(o => o.IntersectsWith(box)) ? 1000 * _u : 0);
            if (score > bestScore) { bestScore = score; best = box; }
        }
        obstacles.Add(best);
        Text(canvas, text, best.MidX, best.MidY + size * 0.36f, size, face, colour, SKTextAlign.Center, halo: Paper);
    }

    // ───────────────────────────── title & panel ─────────────────────────────

    void DrawTitle(SKCanvas canvas, TerrainSurvey s, Layout l)
    {
        float u = _u;
        var t = l.Title;
        string name = string.IsNullOrWhiteSpace(s.Course.Name) ? "Course" : s.Course.Name;
        Text(canvas, name.ToUpperInvariant(), t.Left, t.Top + 26 * u, 26 * u, _serifBold, Ink, tracking: 2.2f * u);
        Text(canvas, $"Terrain & routing plan  ·  {Interval(s)} contours, index every {s.IntervalFeet * s.IndexEvery:0.##} ft  ·  " +
                     (s.SeaLevelMetres is null ? "elevations in feet above the lowest point on the sheet" : "elevations in feet above sea level"), t.Left, t.Top + 44 * u, 10 * u, _serifItalic, Grey);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
            Text(canvas, _o.Credit.ToUpperInvariant(), t.Right, t.Top + 26 * u, 9 * u, _serif, Grey, SKTextAlign.Right, tracking: 1.6f * u);
        Text(canvas, "PGA TOUR 2K25 COURSE DESIGNER", t.Right, t.Top + 44 * u, 7.5f * u, _sans, Grey, SKTextAlign.Right, tracking: 1.2f * u);
        using var rule = Stroke(Ink, 0.8f * u);
        canvas.DrawLine(t.Left, t.Bottom - 4 * u, t.Right, t.Bottom - 4 * u, rule);
    }

    static string Interval(TerrainSurvey s) => $"{s.IntervalFeet:0.##} ft";

    const float RowH = 12.5f;

    float PanelHeight(TerrainSurvey s) => (300 + RowH * (s.Holes.Count + 2) + 420) * _u;

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
        float[] cols = [0, 22, 46, 86, 122, 160, 200];
        string[] head = ["No.", "Par", "Yds", "Tee ft", "Green ft", "Change", "Site"];
        for (int i = 0; i < head.Length; i++)
            Text(canvas, head[i], r.Left + (cols[i] + (i == 0 ? 0 : 30)) * u, y + 6 * u, 6.8f * u, _sansBold, Grey, i == 0 ? SKTextAlign.Left : SKTextAlign.Right);
        y += RowH * u;
        foreach (var h in s.Holes)
        {
            string[] cells =
            [
                h.Hole.Number.ToString(), h.Hole.Par.ToString(), h.Yards.ToString(), $"{h.TeeFeet:0}", $"{h.GreenFeet:0}",
                Math.Abs(h.ChangeFeet) < 0.5 ? "0" : $"{(h.ChangeFeet > 0 ? "+" : "−")}{Math.Abs(h.ChangeFeet):0}",
                $"{h.GreenSiteSlopePct:0.0}%",
            ];
            for (int i = 0; i < cells.Length; i++)
            {
                var colour = i == 6 && h.GreenSiteSlopePct > 5 ? Flag : Ink;
                Text(canvas, cells[i], r.Left + (cols[i] + (i == 0 ? 0 : 30)) * u, y + 6 * u, 7.6f * u, i == 0 ? _sansBold : _sans, colour,
                    i == 0 ? SKTextAlign.Left : SKTextAlign.Right);
            }
            y += RowH * u;
        }
        Text(canvas, $"Change: green vs back tee.  Site: mean slope within {_o.GreenSiteRadiusMetres * TerrainSurvey.YardsPerMetre:0} yds", r.Left, y + 6 * u, 6.6f * u, _sansItalic, Grey);
        y += 10 * u;
        Text(canvas, "of the green anchor (red over 5 %).", r.Left, y + 6 * u, 6.6f * u, _sansItalic, Grey);
        y += 22 * u;

        // Legend.
        Heading("LEGEND");
        float sx = r.Left, sw = 34 * u;
        void Row(Action<float> sample, string label)
        {
            sample(y + 4 * u);
            Text(canvas, label, sx + sw + 10 * u, y + 7 * u, 7.6f * u, _sans, Ink);
            y += 15 * u;
        }
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
            }, "Sea, with shoreline at 0 ft (from the ocean props)");
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
            "(brush shapes treated as round). Check critical",
            "grades in the game before committing a design.",
            $"Sheet covers {(s.Max.X - s.Min.X) * TerrainSurvey.YardsPerMetre:N0} × {(s.Max.Z - s.Min.Z) * TerrainSurvey.YardsPerMetre:N0} yds.",
        ];
        foreach (var n in notes) { Text(canvas, n, r.Left, y + 4 * u, 6.8f * u, _sansItalic, Grey); y += 10 * u; }
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

    // ───────────────────────────── helpers ─────────────────────────────

    static float[] Cumulative(SKPoint[] pts)
    {
        var cum = new float[pts.Length];
        for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + SKPoint.Distance(pts[i - 1], pts[i]);
        return cum;
    }

    static SKPoint PointAt(SKPoint[] pts, float[] cum, float s)
    {
        int i = Array.BinarySearch(cum, s);
        if (i >= 0) return pts[i];
        i = ~i;
        if (i <= 0) return pts[0];
        if (i >= pts.Length) return pts[^1];
        float t = (s - cum[i - 1]) / Math.Max(1e-6f, cum[i] - cum[i - 1]);
        return new SKPoint(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * t);
    }

    static SKPoint Tangent(SKPoint[] pts, float[] cum, float s)
    {
        int i = Array.BinarySearch(cum, s);
        i = Math.Clamp(i < 0 ? ~i : i, 1, pts.Length - 1);
        return Unit(pts[i] - pts[i - 1]);
    }

    static SKPoint Unit(SKPoint v) => v.Length < 1e-6f ? new SKPoint(0, 0) : new SKPoint(v.X / v.Length, v.Y / v.Length);

    static float DistanceToPolyline(SKPoint p, SKPoint[] line)
    {
        float best = float.MaxValue;
        for (int i = 1; i < line.Length; i++)
        {
            var a = line[i - 1]; var ab = line[i] - a;
            float len2 = ab.X * ab.X + ab.Y * ab.Y;
            float t = len2 < 1e-6f ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
            best = Math.Min(best, SKPoint.Distance(p, new SKPoint(a.X + ab.X * t, a.Y + ab.Y * t)));
        }
        return best;
    }

    static SKRect Inflate(SKRect r, float by) { r.Inflate(by, by); return r; }

    static SKPaint Stroke(SKColor c, float width) =>
        new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round };

    static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };

    static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor color,
        SKTextAlign align = SKTextAlign.Left, SKColor? halo = null, float tracking = 0)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        if (tracking > 0)
        {
            var widths = text.Select(ch => font.MeasureText(ch.ToString())).ToArray();
            float total = widths.Sum() + tracking * (text.Length - 1);
            float cx = align switch { SKTextAlign.Right => x - total, SKTextAlign.Center => x - total / 2, _ => x };
            for (int i = 0; i < text.Length; i++)
            {
                canvas.DrawText(text[i].ToString(), cx, y, SKTextAlign.Left, font, paint);
                cx += widths[i] + tracking;
            }
            return;
        }
        if (halo is { } hc)
        {
            using var h = new SKPaint { Color = hc.WithAlpha(235), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.36f, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawText(text, x, y, align, font, h);
        }
        canvas.DrawText(text, x, y, align, font, paint);
    }
}
