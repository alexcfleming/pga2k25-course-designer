using System.Runtime.InteropServices;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>
/// Shared drawing for the terrain sheets: shading, contours, spot heights, drawn surfaces and the routing
/// as sticks and dots. Sheets supply the projection and layout.
/// </summary>
public abstract class TerrainInk : IDisposable
{
    protected static readonly SKColor Paper = SKColor.Parse("#fbf8f1");
    protected static readonly SKColor Ink = SKColor.Parse("#1f1d1a");
    protected static readonly SKColor Grey = SKColor.Parse("#6b665e");
    protected static readonly SKColor Rule = SKColor.Parse("#cfc8bb");
    protected static readonly SKColor MinorContour = SKColor.Parse("#b98a63");
    protected static readonly SKColor IndexContour = SKColor.Parse("#8a4f28");
    protected static readonly SKColor GridLine = SKColor.Parse("#56708c");
    protected static readonly SKColor GreenFill = SKColor.Parse("#8cc07a");
    protected static readonly SKColor GreenEdge = SKColor.Parse("#3c6e2f");
    protected static readonly SKColor Flag = SKColor.Parse("#c0392b");
    protected static readonly SKColor Sea = SKColor.Parse("#b7d5e8");
    protected static readonly SKColor Shore = SKColor.Parse("#3f7fae");

    /// <summary>Relief tint, low → high: pale meadow green to pale tan.</summary>
    protected static readonly (double T, SKColor C)[] Hypsometric =
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

    protected readonly TerrainMapOptions _o;
    protected readonly float _u;
    protected readonly SKTypeface _serif, _serifBold, _serifItalic, _sans, _sansBold, _sansItalic, _sansBoldItalic;

    /// <param name="pixelsPerPoint">Output pixels per typographic point; every size below is in points.</param>
    /// <summary>Green sites as a tinted disc (plot map) or an outline that leaves the ground clear to sketch on.</summary>
    protected virtual bool FillGreenSites => true;

    /// <summary>Numbered hole badges with par · yards · rise (off where a sheet header already says it).</summary>
    protected virtual bool ShowBadges => true;

    protected TerrainInk(TerrainMapOptions options, float pixelsPerPoint)
    {
        _o = options;
        _u = pixelsPerPoint;
        string serif = Resolve("Lora", "Georgia", "Cambria", "Times New Roman");
        string sans = Resolve("Segoe UI", "Arial", "Helvetica");
        _serif = SKTypeface.FromFamilyName(serif, SKFontStyle.Normal);
        _serifBold = SKTypeface.FromFamilyName(serif, SKFontStyle.Bold);
        _serifItalic = SKTypeface.FromFamilyName(serif, SKFontStyle.Italic);
        _sans = SKTypeface.FromFamilyName(sans, SKFontStyle.Normal);
        _sansBold = SKTypeface.FromFamilyName(sans, SKFontStyle.Bold);
        _sansItalic = SKTypeface.FromFamilyName(sans, SKFontStyle.Italic);
        _sansBoldItalic = SKTypeface.FromFamilyName(sans, SKFontStyle.BoldItalic);
    }

    static string Resolve(params string[] families) =>
        families.FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase))
        ?? SKTypeface.Default.FamilyName;

    public void Dispose()
    {
        _serif.Dispose(); _serifBold.Dispose(); _serifItalic.Dispose();
        _sans.Dispose(); _sansBold.Dispose(); _sansItalic.Dispose(); _sansBoldItalic.Dispose();
    }

    // ───────────────────────────── terrain ─────────────────────────────

    /// <summary>
    /// Relief or slope tint with hillshade, plus the sea, rasterised at terrain resolution and drawn
    /// through the projection (any rotation). The light always comes from the page's upper left.
    /// </summary>
    protected void DrawShading(SKCanvas canvas, TerrainSurvey s, MapProjection p)
    {
        var f = s.Field;
        int w = f.Width, h = f.Height;
        double cell = f.Cell, range = Math.Max(1e-6, s.MaxFeet / TerrainSurvey.FeetPerMetre);
        // Heights exaggerated a little because golf-scale relief is subtle.
        const double z = 1.5, alt = 45 * Math.PI / 180, strength = 0.45;
        double lx = -Math.Cos(alt) / Math.Sqrt(2), ly = -Math.Cos(alt) / Math.Sqrt(2), lz = Math.Sin(alt);
        // Page images of the world axes turn a world gradient into a page gradient.
        var ex = p.MapDirection(new Vec2(1, 0));
        var ez = p.MapDirection(new Vec2(0, 1));
        uint sea = 0xFF000000u | (uint)Sea.Red << 16 | (uint)Sea.Green << 8 | Sea.Blue;

        var pixels = new int[w * h];
        Parallel.For(0, h, iz =>
        {
            int zm = Math.Max(0, iz - 1), zp = Math.Min(h - 1, iz + 1);
            for (int ix = 0; ix < w; ix++)
            {
                if (s.IsWater(f[ix, iz]) || s.PaintedWater.Length > 0 && s.PaintedWater[iz * w + ix]) { pixels[iz * w + ix] = unchecked((int)sea); continue; }
                int xm = Math.Max(0, ix - 1), xp = Math.Min(w - 1, ix + 1);
                double gx = (f[xp, iz] - f[xm, iz]) / ((xp - xm) * cell);   // dh/dX
                double gz = (f[ix, zp] - f[ix, zm]) / ((zp - zm) * cell);   // dh/dZ
                double px = (ex.X * gx + ez.X * gz) * z, py = (ex.Y * gx + ez.Y * gz) * z;
                double shade = (-px * lx - py * ly + lz) / Math.Sqrt(px * px + py * py + 1);
                double rel = Math.Clamp(shade / lz, 0, 1.12);

                SKColor c = _o.Shading switch
                {
                    ReliefShading.Slope => SlopeClasses.First(k => Math.Sqrt(gx * gx + gz * gz) * 100 < k.MaxPct).C,
                    ReliefShading.None => Paper,
                    _ => Ramp((f[ix, iz] - s.DatumMetres) / range),
                };
                double m = _o.Shading == ReliefShading.None ? 1 : 1 - strength * (1 - rel);
                byte r = (byte)Math.Clamp(c.Red * m, 0, 255), g = (byte)Math.Clamp(c.Green * m, 0, 255), b = (byte)Math.Clamp(c.Blue * m, 0, 255);
                pixels[iz * w + ix] = unchecked((int)(0xFF000000u | (uint)r << 16 | (uint)g << 8 | b));
            }
        });

        DrawRaster(canvas, f, p, pixels, SKAlphaType.Opaque);
    }

    /// <summary>Draws one pixel per terrain cell, mapping the raster's axes through the projection.</summary>
    static void DrawRaster(SKCanvas canvas, GreensBook.Heightfield f, MapProjection p, int[] pixels, SKAlphaType alpha)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(f.Width, f.Height, SKColorType.Bgra8888, alpha));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        // Bitmap pixel (i, j) covers world cell (i, j).
        var o = p.Map(f.Origin);
        var ax = p.Map(f.Origin + new Vec2(f.Cell, 0));
        var az = p.Map(f.Origin + new Vec2(0, f.Cell));
        var matrix = new SKMatrix(ax.X - o.X, az.X - o.X, o.X, ax.Y - o.Y, az.Y - o.Y, o.Y, 0, 0, 1);
        canvas.Save();
        canvas.Concat(in matrix);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Restore();
    }

    /// <summary>
    /// Painted streams and ponds, laid over the contours (the bed under the water isn't contoured), with their edges.
    /// </summary>
    protected void DrawPaintedWater(SKCanvas canvas, TerrainSurvey s, MapProjection p)
    {
        if (s.PaintedWater.Length == 0) return;
        // Premultiplied sea colour; the cubic resampling softens the cell edges.
        uint wet = 0xFF000000u | (uint)Sea.Red << 16 | (uint)Sea.Green << 8 | Sea.Blue;
        var pixels = new int[s.PaintedWater.Length];
        for (int i = 0; i < pixels.Length; i++) if (s.PaintedWater[i]) pixels[i] = unchecked((int)wet);
        DrawRaster(canvas, s.Field, p, pixels, SKAlphaType.Premul);
        using var edge = Stroke(Shore, 0.6f * _u);
        using var b = new SKPathBuilder();
        foreach (var line in s.PaintedWaterEdges) p.AddPolyline(b, line, close: false, tolerance: 0.35f);
        using var path = b.Detach();
        canvas.DrawPath(path, edge);
    }

    protected void DrawShoreline(SKCanvas canvas, TerrainSurvey s, MapProjection p)
    {
        if (s.Shoreline.Count == 0) return;
        using var shore = Stroke(Shore, 0.9f * _u);
        using var b = new SKPathBuilder();
        foreach (var line in s.Shoreline) p.AddPolyline(b, line, close: false, tolerance: 0.35f);
        using var path = b.Detach();
        canvas.DrawPath(path, shore);
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

    protected void DrawFeatures(SKCanvas canvas, TerrainSurvey s, MapProjection p)
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

    /// <param name="labelSpacing">Distance along an index contour between its labels (points).</param>
    protected void DrawContours(SKCanvas canvas, TerrainSurvey s, MapProjection p, SKRect map, List<SKPoint[]> routes, List<SKRect> obstacles,
        float labelSpacing = 190)
    {
        float u = _u, size = 5.6f * u;
        using var font = new SKFont(_sans, size) { Subpixel = true };
        var safe = map;
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
            float spacing = labelSpacing * u;
            for (float at = Math.Min(70 * u, total / 2); at < total - half; at += spacing)
            {
                if (at < half) continue;
                var a = PointAt(pts, cum, at - half);
                var b = PointAt(pts, cum, at + half);
                var mid = PointAt(pts, cum, at);
                if (SKPoint.Distance(a, b) < 2 * half * 0.94f) continue;               // too curvy here
                if (!safe.Contains(mid)) continue;
                // No labels on cliff faces: the lines are too close to read them against.
                if (s.SlopePct(WorldAt(c.Points, cum, at)) > 60) continue;
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

    protected List<(SKPoint At, string Text, SKRect Box)> PlaceSummits(TerrainSurvey s, MapProjection p, SKRect map, List<SKRect> obstacles)
    {
        float u = _u, size = 5.8f * u;
        using var font = new SKFont(_sansItalic, size);
        var result = new List<(SKPoint, string, SKRect)>();
        foreach (var sp in s.Summits)
        {
            var at = p.Map(sp.Position);
            string text = $"{sp.Feet:0}";
            float w = font.MeasureText(text);
            var box = new SKRect(at.X - 3 * u, at.Y - 3.5f * u, at.X + 4.5f * u + w, at.Y + 3.5f * u);
            if (!map.Contains(box) || obstacles.Any(o => o.IntersectsWith(box))) continue;
            obstacles.Add(box);
            result.Add((at, text, box));
        }
        return result;
    }

    protected void DrawSummits(SKCanvas canvas, List<(SKPoint At, string Text, SKRect Box)> summits)
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

    // ───────────────────────────── routing ─────────────────────────────

    /// <summary>Sticks and dots for <paramref name="holes"/> (projected <paramref name="routes"/>), ticks, greens and labels.</summary>
    protected void DrawRouting(SKCanvas canvas, IReadOnlyList<HoleRouting> holes, MapProjection p, List<SKPoint[]> routes, List<SKRect> obstacles)
    {
        float u = _u;
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

        for (int i = 0; i < holes.Count; i++) DrawTicks(canvas, holes[i], routes[i], p);

        // Greens, tees and aim points over every line so crossings never hide a dot.
        for (int i = 0; i < holes.Count; i++)
        {
            var h = holes[i];
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
        for (int i = 0; i < holes.Count; i++) DrawAimLabels(canvas, holes[i], routes[i], routes, obstacles);
        if (ShowBadges)
            for (int i = 0; i < holes.Count; i++) DrawBadge(canvas, holes[i], routes[i], routes, obstacles);
    }

    protected void DrawTicks(SKCanvas canvas, HoleRouting h, SKPoint[] r, MapProjection p)
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

    protected void DrawGreen(SKCanvas canvas, HoleRouting h, SKPoint at, MapProjection p)
    {
        float u = _u;
        float radius = Math.Max(3.5f * u, (float)(_o.GreenSiteRadiusMetres * p.PointsPerMetre));
        // The site circle sits on the drawn green when there is one; the flag stays on the route's end.
        var site = p.Map(h.GreenSiteCentre);
        if (FillGreenSites)
            using (var fill = Fill(GreenFill.WithAlpha(150))) canvas.DrawCircle(site, radius, fill);
        using (var edge = Stroke(GreenEdge, 0.9f * u)) canvas.DrawCircle(site, radius, edge);

        // Fall arrow across the green site, when its best-fit plane falls by more than 1 %.
        if (h.GreenSiteGradePct > 1 && h.GreenSiteFallDirection.Length > 0)
        {
            var d = p.MapDirection(h.GreenSiteFallDirection);
            float len = radius * 0.72f;
            var a = new SKPoint(site.X - d.X * len, site.Y - d.Y * len);
            var b = new SKPoint(site.X + d.X * len, site.Y + d.Y * len);
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

    protected void DrawAimPoint(SKCanvas canvas, SKPoint at)
    {
        float u = _u;
        using var fill = Fill(SKColors.White);
        using var edge = Stroke(Ink, 1.0f * u);
        canvas.DrawCircle(at, 2.7f * u, fill);
        canvas.DrawCircle(at, 2.7f * u, edge);
    }

    /// <summary>Aim points: "from tee · to green" yards, on the outside of the turn.</summary>
    protected void DrawAimLabels(SKCanvas canvas, HoleRouting h, SKPoint[] r, List<SKPoint[]> routes, List<SKRect> obstacles)
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

    protected void DrawBadge(SKCanvas canvas, HoleRouting h, SKPoint[] r, List<SKPoint[]> routes, List<SKRect> obstacles)
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
    protected void PlaceText(SKCanvas canvas, string text, SKPoint anchor, float offset, float size, SKTypeface face, SKColor colour,
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

    // ───────────────────────────── helpers ─────────────────────────────

    protected static float[] Cumulative(SKPoint[] pts)
    {
        var cum = new float[pts.Length];
        for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + SKPoint.Distance(pts[i - 1], pts[i]);
        return cum;
    }

    protected static SKPoint PointAt(SKPoint[] pts, float[] cum, float s)
    {
        int i = Array.BinarySearch(cum, s);
        if (i >= 0) return pts[i];
        i = ~i;
        if (i <= 0) return pts[0];
        if (i >= pts.Length) return pts[^1];
        float t = (s - cum[i - 1]) / Math.Max(1e-6f, cum[i] - cum[i - 1]);
        return new SKPoint(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * t);
    }

    /// <summary>World point at page distance <paramref name="at"/> along a projected line (uniform scale, so proportional).</summary>
    static Vec2 WorldAt(IReadOnlyList<Vec2> world, float[] cum, float at)
    {
        int i = Array.BinarySearch(cum, at);
        if (i >= 0) return world[i];
        i = ~i;
        if (i <= 0) return world[0];
        if (i >= world.Count) return world[^1];
        double t = (at - cum[i - 1]) / Math.Max(1e-6, cum[i] - cum[i - 1]);
        return world[i - 1] + (world[i] - world[i - 1]) * t;
    }

    protected static SKPoint Tangent(SKPoint[] pts, float[] cum, float s)
    {
        int i = Array.BinarySearch(cum, s);
        i = Math.Clamp(i < 0 ? ~i : i, 1, pts.Length - 1);
        return Unit(pts[i] - pts[i - 1]);
    }

    protected static SKPoint Unit(SKPoint v) => v.Length < 1e-6f ? new SKPoint(0, 0) : new SKPoint(v.X / v.Length, v.Y / v.Length);

    protected static float DistanceToPolyline(SKPoint p, SKPoint[] line)
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

    protected static SKRect Inflate(SKRect r, float by) { r.Inflate(by, by); return r; }

    protected static SKPaint Stroke(SKColor c, float width) =>
        new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round };

    protected static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };

    protected static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor color,
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
