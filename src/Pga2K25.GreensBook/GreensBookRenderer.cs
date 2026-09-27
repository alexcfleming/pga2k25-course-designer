using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.GreensBook;

/// <summary>
/// Draws greens-book pages. Plate style from the green surface study (ink #1f1d1a,
/// grey #6b665e, Lora with serif fallback, solid arrows weighted by class, line-only
/// contours) with an optional slope heat map under the linework.
/// Every drawing path goes through <see cref="GreenView"/> so there is exactly one
/// world→page convention (approach from the bottom of the page).
/// </summary>
public sealed class GreensBookRenderer : IDisposable
{
    /// <summary>Single-green study page (US Letter landscape, points).</summary>
    public const float PageWidth = 792, PageHeight = 612;

    /// <summary>Default logical size of the one-page 18-green sheet.</summary>
    public const float SheetWidth = 1600, SheetHeight = 1000;

    static readonly SKColor Ink = SKColor.Parse("#1f1d1a");
    static readonly SKColor Grey = SKColor.Parse("#6b665e");
    static readonly SKColor Faint = SKColor.Parse("#b9b4ab");

    readonly GreensBookOptions _options;
    readonly SKTypeface _regular, _bold, _italic;
    readonly Dictionary<GreenAnalysis, SKImage> _heatMaps = new();
    public string FontFamily { get; }

    public GreensBookRenderer(GreensBookOptions options)
    {
        _options = options;
        FontFamily = new[] { "Lora", "Georgia", "Cambria", "Times New Roman" }
            .FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase))
            ?? SKTypeface.Default.FamilyName;
        _regular = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        _bold = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Bold);
        _italic = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Italic);
    }

    public void Dispose()
    {
        foreach (var image in _heatMaps.Values) image.Dispose();
        _regular.Dispose(); _bold.Dispose(); _italic.Dispose();
    }

    // ───────────────────────────── pages ─────────────────────────────

    /// <summary>One green, full detail: 2"/6" contours with labels, pins, stats panel, legend.</summary>
    public void DrawHolePage(SKCanvas canvas, string courseName, GreenAnalysis a)
    {
        canvas.Clear(SKColors.White);
        const float margin = 36;
        var greenRect = new SKRect(margin, 92, PageWidth - 236, PageHeight - 78);
        var panel = new SKRect(PageWidth - 216, 92, PageWidth - margin, PageHeight - 78);

        Text(canvas, $"HOLE {a.Hole.Number}", margin, 52, 30, _bold, Ink);
        Text(canvas, $"{courseName}  ·  Par {a.Hole.Par}", margin, 72, 12, _italic, Grey);
        Header(canvas, PageWidth - margin, 40, 1);

        var view = new GreenView(a, greenRect);
        DrawGreen(canvas, a, view, detail: true, scale: 1);
        DrawScaleBar(canvas, view, new SKPoint(greenRect.Left, greenRect.Bottom + 18));
        DrawApproachMark(canvas, new SKPoint(greenRect.MidX, greenRect.Bottom + 18), 1);

        DrawStatsPanel(canvas, a, panel);
        DrawLegend(canvas, new SKRect(margin, PageHeight - 40, PageWidth - margin, PageHeight - 20), overview: false, u: 1);
    }

    /// <summary>
    /// All greens on one page (6 × 3 for 18 holes): slope colour, half-foot contours,
    /// arrows, pin dots, per-hole caption. <paramref name="width"/>/<paramref name="height"/>
    /// are logical units; type scales with width so the same layout works as a PDF page.
    /// </summary>
    public void DrawSheet(SKCanvas canvas, string courseName, IReadOnlyList<GreenAnalysis> greens,
        float width = SheetWidth, float height = SheetHeight)
    {
        canvas.Clear(SKColors.White);
        float u = width / SheetWidth;          // typographic unit
        float margin = 44 * u;
        Text(canvas, courseName.ToUpperInvariant(), margin, 62 * u, 38 * u, _bold, Ink);
        Text(canvas, "Greens book  ·  slope colour, half-foot contours  ·  approach from bottom of each green",
            margin, 90 * u, 15 * u, _italic, Grey);
        Header(canvas, width - margin, 52 * u, u);

        int cols = greens.Count <= 9 ? 3 : 6;
        int rows = (int)Math.Ceiling(greens.Count / (double)cols);
        float top = 112 * u, bottom = height - 74 * u;
        float cw = (width - 2 * margin) / cols, ch = (bottom - top) / rows;
        for (int i = 0; i < greens.Count; i++)
        {
            var g = greens[i];
            float x = margin + (i % cols) * cw, y = top + (i / cols) * ch;
            Text(canvas, g.Hole.Number.ToString(), x + 8 * u, y + 24 * u, 22 * u, _bold, Ink);
            Text(canvas, $"PAR {g.Hole.Par}", x + 8 * u, y + 40 * u, 10 * u, _regular, Grey, tracking: 1.2f * u);
            var cell = new SKRect(x + 10 * u, y + 30 * u, x + cw - 10 * u, y + ch - 26 * u);
            DrawGreen(canvas, g, new GreenView(g, cell), detail: false, scale: u);
            Text(canvas, $"falls {g.FallInches:0} in.  ·  median {g.MedianSlopePct:0.0}%",
                x + cw / 2, y + ch - 8 * u, 11 * u, _italic, Grey, SKTextAlign.Center);
        }
        DrawLegend(canvas, new SKRect(margin, height - 44 * u, width - margin, height - 20 * u), overview: true, u: u);
    }

    void Header(SKCanvas canvas, float right, float y, float u)
    {
        Text(canvas, _options.Credit, right, y, 9 * u * (u < 1 ? 1 : 1.3f), _regular, Grey, SKTextAlign.Right, tracking: 1.5f * u);
        Text(canvas, "EXPERIMENTAL — terrain reconstructed from brush stamps, not yet calibrated",
            right, y + 16 * u * (u < 1 ? 1 : 1.2f), 8 * u * (u < 1 ? 1 : 1.4f), _italic, Grey, SKTextAlign.Right);
    }

    // ───────────────────────────── green ─────────────────────────────

    void DrawGreen(SKCanvas canvas, GreenAnalysis a, GreenView view, bool detail, float scale)
    {
        using var outlinePath = view.Path(a.Outline, close: true);
        float k = view.PointsPerMetre;
        bool colour = _options.ColourSlopes;

        canvas.Save();
        canvas.ClipPath(outlinePath, antialias: true);
        if (colour) DrawHeatMap(canvas, a, view);
        using (var hair = Stroke(Ink.WithAlpha((byte)(colour ? 110 : 150)), Math.Max(0.3f, 0.012f * k)))
        using (var heavy = Stroke(Ink.WithAlpha((byte)(colour ? 215 : 255)), Math.Max(0.55f, 0.05f * k)))
        {
            foreach (var c in a.Contours)
            {
                if (!detail && !c.Heavy) continue;   // sheet: half-foot interval only
                using var path = view.Path(c.Points, close: false);
                canvas.DrawPath(path, c.Heavy ? heavy : hair);
            }
        }
        canvas.Restore();

        using (var edge = Stroke(Ink, Math.Max(0.8f, 0.07f * k)))
        {
            if (a.OutlineEstimated) edge.PathEffect = SKPathEffect.CreateDash([4f * scale, 3f * scale], 0);
            canvas.DrawPath(outlinePath, edge);
        }

        foreach (var arrow in a.Arrows) DrawArrow(canvas, view, arrow, (detail ? 1f : 0.62f) * scale, halo: colour);

        using var dot = new SKPaint { Color = Ink, IsAntialias = true };
        using var ring = new SKPaint { Color = SKColors.White, IsAntialias = true };
        if (detail)
        {
            LabelHeavyContours(canvas, a, view);
            foreach (var pin in a.Pins)
            {
                var p = view.Map(pin.Position);
                canvas.DrawCircle(p, 3.6f, ring);
                canvas.DrawCircle(p, 2.4f, dot);
                Text(canvas, $"P{pin.Number} {GreenAnalyzer.FormatFeetInches(pin.Inches)}", p.X + 5, p.Y - 4, 8, _bold, Ink, halo: true);
            }
        }
        else
        {
            foreach (var pin in a.Pins)
            {
                var p = view.Map(pin.Position);
                canvas.DrawCircle(p, 3.2f * scale, ring);
                canvas.DrawCircle(p, 2.1f * scale, dot);
            }
        }
    }

    /// <summary>Slope heat map: one pixel per terrain cell, affine-mapped through the green view.</summary>
    void DrawHeatMap(SKCanvas canvas, GreenAnalysis a, GreenView view)
    {
        if (!_heatMaps.TryGetValue(a, out var image))
            _heatMaps[a] = image = BuildHeatMap(a.Field);

        var f = a.Field;
        var p00 = view.Map(f.Origin);
        var p10 = view.Map(f.Origin + new Vec2(f.Cell, 0));
        var p01 = view.Map(f.Origin + new Vec2(0, f.Cell));
        var matrix = new SKMatrix(
            p10.X - p00.X, p01.X - p00.X, p00.X,
            p10.Y - p00.Y, p01.Y - p00.Y, p00.Y,
            0, 0, 1);

        canvas.Save();
        canvas.Concat(in matrix);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        canvas.Restore();
    }

    static SKImage BuildHeatMap(Heightfield f)
    {
        var info = new SKImageInfo(f.Width, f.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        for (int iz = 0; iz < f.Height; iz++)
            for (int ix = 0; ix < f.Width; ix++)
            {
                int x0 = Math.Max(ix - 1, 0), x1 = Math.Min(ix + 1, f.Width - 1);
                int z0 = Math.Max(iz - 1, 0), z1 = Math.Min(iz + 1, f.Height - 1);
                double gx = (f[x1, iz] - f[x0, iz]) / ((x1 - x0) * f.Cell);
                double gz = (f[ix, z1] - f[ix, z0]) / ((z1 - z0) * f.Cell);
                bitmap.SetPixel(ix, iz, SlopePalette.Colour(Math.Sqrt(gx * gx + gz * gz) * 100));
            }
        return SKImage.FromBitmap(bitmap);
    }

    void DrawArrow(SKCanvas canvas, GreenView view, SlopeArrow arrow, float scale, bool halo)
    {
        double spacing = _options.ArrowSpacingMetres;
        // Length grows with slope (capped) so direction reads at a glance; weight encodes class.
        double lengthM = spacing * Math.Clamp(0.4 + arrow.SlopePct / 12.0, 0.4, 0.85);
        var tail = view.Map(arrow.Position - arrow.Downhill * (lengthM / 2));
        var tip = view.Map(arrow.Position + arrow.Downhill * (lengthM / 2));
        float shaft = (arrow.Steep ? 2.3f : 1.3f) * scale;
        float head = (arrow.Steep ? 6.5f : 5f) * scale;

        var dir = new SKPoint(tip.X - tail.X, tip.Y - tail.Y);
        float len = dir.Length;
        if (len < 1e-3f) return;
        dir = new SKPoint(dir.X / len, dir.Y / len);
        var normal = new SKPoint(-dir.Y, dir.X);
        var baseOfHead = new SKPoint(tip.X - dir.X * head, tip.Y - dir.Y * head);

        using var builder = new SKPathBuilder();
        builder.MoveTo(tip);
        builder.LineTo(baseOfHead.X + normal.X * head * 0.5f, baseOfHead.Y + normal.Y * head * 0.5f);
        builder.LineTo(baseOfHead.X - normal.X * head * 0.5f, baseOfHead.Y - normal.Y * head * 0.5f);
        builder.Close();
        using var headPath = builder.Detach();

        if (halo)
        {
            float h = Math.Max(1f, 1.1f * scale);
            using var white = new SKPaint { Color = SKColors.White.WithAlpha(230), IsAntialias = true, StrokeWidth = shaft + 2 * h, StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawLine(tail, baseOfHead, white);
            white.StrokeWidth = 2 * h;
            canvas.DrawPath(headPath, white);
        }
        using var paint = new SKPaint { Color = Ink, IsAntialias = true, StrokeWidth = shaft, StrokeCap = SKStrokeCap.Butt, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(tail, baseOfHead, paint);
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(headPath, paint);
    }

    void LabelHeavyContours(SKCanvas canvas, GreenAnalysis a, GreenView view)
    {
        foreach (var group in a.Contours.Where(c => c.Heavy).GroupBy(c => c.LevelInches))
        {
            var line = group.OrderByDescending(c => c.Points.Count).First();
            var inside = line.Points.Where(p => Geometry.Contains(a.Outline, p) && Geometry.DistanceToEdge(a.Outline, p) > 1.2).ToList();
            if (inside.Count == 0) continue;
            var at = view.Map(inside[inside.Count / 2]);
            Text(canvas, GreenAnalyzer.FormatFeetInches(group.Key), at.X, at.Y + 2.5f, 7, _regular, Ink, SKTextAlign.Center, halo: true);
        }
    }

    // ───────────────────────────── furniture ─────────────────────────────

    void DrawStatsPanel(SKCanvas canvas, GreenAnalysis a, SKRect r)
    {
        float y = r.Top + 6;
        void Row(string label, string value)
        {
            Text(canvas, label, r.Left, y, 9, _regular, Grey);
            Text(canvas, value, r.Right, y, 9, _bold, Ink, SKTextAlign.Right);
            y += 16;
        }
        const double sqFt = 10.7639;
        Row("Green area", $"{a.AreaSquareMetres * sqFt:N0} sq ft");
        Row("Total fall", $"{a.FallInches:0.#} in.");
        Row("High / low vs centre", $"{GreenAnalyzer.FormatFeetInches(a.HighInches)} / {GreenAnalyzer.FormatFeetInches(a.LowInches)}");
        Row("Median slope", $"{a.MedianSlopePct:0.0}%");
        Row("95th pct slope", $"{a.P95SlopePct:0.0}%");
        Row("Arrows steep / gentle", $"{a.Arrows.Count(x => x.Steep)} / {a.Arrows.Count(x => !x.Steep)}");
        y += 8;
        if (a.Pins.Count > 0)
        {
            Text(canvas, "PIN POSITIONS", r.Left, y, 8, _bold, Grey, tracking: 1.2f); y += 14;
            foreach (var pin in a.Pins) Row($"Pin {pin.Number}", GreenAnalyzer.FormatFeetInches(pin.Inches));
            y += 8;
        }
        Text(canvas, "Elevations relative to green centre (0).", r.Left, y, 7.5f, _italic, Grey); y += 11;
        Text(canvas, $"Stats exclude the outer {_options.EdgeExclusionMetres:0.#} m of the green.", r.Left, y, 7.5f, _italic, Grey); y += 14;
        foreach (var w in a.Warnings)
        {
            foreach (var line in Wrap(w, 44)) { Text(canvas, line, r.Left, y, 7.5f, _italic, Grey); y += 10; }
            y += 4;
        }
    }

    void DrawLegend(SKCanvas canvas, SKRect r, bool overview, float u)
    {
        using var rule = Stroke(Faint, 0.5f * u);
        canvas.DrawLine(r.Left, r.Top - 8 * u, r.Right, r.Top - 8 * u, rule);
        float size = overview ? 12 * u : 8;
        float x = r.Left, y = r.MidY + 3 * u;
        float steepDeg = (float)_options.SteepThresholdDegrees;
        float gap = 22 * (overview ? u * 1.4f : 1);

        if (_options.ColourSlopes)
        {
            float barW = (overview ? 260 : 150) * (overview ? u : 1), barH = (overview ? 10 : 6) * (overview ? u : 1);
            var bar = new SKRect(x, y - barH - 2 * u, x + barW, y - 2 * u);
            using (var shader = SKShader.CreateLinearGradient(new SKPoint(bar.Left, 0), new SKPoint(bar.Right, 0),
                       SlopePalette.Stops.Select(s => s.Colour).ToArray(),
                       SlopePalette.Stops.Select(s => (float)(s.Pct / SlopePalette.MaxPct)).ToArray(), SKShaderTileMode.Clamp))
            using (var fill = new SKPaint { Shader = shader, IsAntialias = true })
                canvas.DrawRect(bar, fill);
            float tick = overview ? 9.5f * u : 6.5f;
            for (int pct = 0; pct <= SlopePalette.MaxPct; pct += 2)
                Text(canvas, pct == (int)SlopePalette.MaxPct ? $"{pct}%+" : $"{pct}", bar.Left + barW * pct / (float)SlopePalette.MaxPct,
                    bar.Top - 3 * u, tick, _regular, Grey, SKTextAlign.Center);
            Text(canvas, "slope %", bar.Right + 8 * u, y - 2 * u, size, _regular, Ink);
            x += barW + 8 * u + Measure("slope %", size) + gap;
        }

        void ArrowSample(bool steep, string label)
        {
            var a = new SlopeArrow(new Vec2(0, 0), new Vec2(1, 0), steep ? 8 : 2, steep);
            float s = overview ? 0.62f * u * 1.5f : 1f;
            canvas.Save();
            canvas.Translate(x + 14 * s, y - 4 * (overview ? u : 1));
            DrawArrow(canvas, new GreenView.Fixed(28f * s / (float)(_options.ArrowSpacingMetres * 0.85)), a, s, halo: false);
            canvas.Restore();
            Text(canvas, label, x + 32 * s, y, size, _regular, Ink);
            x += 32 * s + Measure(label, size) + gap;
        }
        void LineSample(float width, SKColor color, string label, bool dashed = false)
        {
            float len = overview ? 30 * u : 24;
            using var p = Stroke(color, width);
            if (dashed) p.PathEffect = SKPathEffect.CreateDash([4f, 3f], 0);
            canvas.DrawLine(x, y - 4 * (overview ? u : 1), x + len, y - 4 * (overview ? u : 1), p);
            Text(canvas, label, x + len + 6, y, size, _regular, Ink);
            x += len + 6 + Measure(label, size) + gap;
        }

        ArrowSample(true, $"steep > {steepDeg:0.0}°");
        ArrowSample(false, $"gentle < {steepDeg:0.0}°");
        if (!overview) LineSample(0.4f, Ink.WithAlpha(150), "2 in. contour");
        LineSample(overview ? 1.1f * u * 1.4f : 1.1f, Ink, overview ? "6 in. contour" : "6 in. contour (labelled)");
        if (overview)
        {
            using var dot = new SKPaint { Color = Ink, IsAntialias = true };
            canvas.DrawCircle(x + 5 * u, y - 4 * u, 3 * u, dot);
            Text(canvas, "pin", x + 14 * u, y, size, _regular, Ink);
            x += 14 * u + Measure("pin", size) + gap;
        }
        LineSample(overview ? 1.2f * u * 1.4f : 1.2f, Ink, "estimated outline", dashed: true);
    }

    void DrawApproachMark(SKCanvas canvas, SKPoint at, float u)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(at.X - 30 * u, at.Y + 1 * u);
        builder.LineTo(at.X - 25 * u, at.Y - 7 * u);
        builder.LineTo(at.X - 20 * u, at.Y + 1 * u);
        builder.Close();
        using var triangle = builder.Detach();
        using var fill = new SKPaint { Color = Grey, IsAntialias = true };
        canvas.DrawPath(triangle, fill);
        Text(canvas, "approach from bottom", at.X - 14 * u, at.Y + 1 * u, 8 * u, _italic, Grey);
    }

    void DrawScaleBar(SKCanvas canvas, GreenView view, SKPoint at)
    {
        const double yard = 0.9144;
        float tenYards = (float)(10 * yard * view.PointsPerMetre);
        using var p = Stroke(Ink, 0.8f);
        canvas.DrawLine(at.X, at.Y, at.X + tenYards, at.Y, p);
        for (int i = 0; i <= 2; i++) canvas.DrawLine(at.X + tenYards * i / 2, at.Y - 3, at.X + tenYards * i / 2, at.Y + 3, p);
        Text(canvas, "10 yd", at.X + tenYards + 5, at.Y + 3, 7.5f, _regular, Grey);
    }

    // ───────────────────────────── primitives ─────────────────────────────

    static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color, StrokeWidth = width, IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
    };

    float Measure(string text, float size)
    {
        using var font = new SKFont(_regular, size);
        return font.MeasureText(text);
    }

    static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor color,
        SKTextAlign align = SKTextAlign.Left, bool halo = false, float tracking = 0)
    {
        using var font = new SKFont(face, size) { Subpixel = true };
        if (tracking > 0)
        {
            // Letter-spacing: lay glyphs out one by one (no reliance on special space glyphs).
            var widths = text.Select(ch => font.MeasureText(ch.ToString())).ToArray();
            float total = widths.Sum() + tracking * (text.Length - 1);
            float cx = align switch { SKTextAlign.Right => x - total, SKTextAlign.Center => x - total / 2, _ => x };
            using var p = new SKPaint { Color = color, IsAntialias = true };
            for (int i = 0; i < text.Length; i++)
            {
                canvas.DrawText(text[i].ToString(), cx, y, SKTextAlign.Left, font, p);
                cx += widths[i] + tracking;
            }
            return;
        }
        if (halo)
        {
            using var h = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.33f, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawText(text, x, y, align, font, h);
        }
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }

    static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0) { yield return line; line = word; }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) yield return line;
    }
}

/// <summary>
/// Slope colour ramp (0–8 %, dark green → red), matching the reference Course
/// Finisher's greens-book scale. Values above the top stop clamp to it.
/// </summary>
public static class SlopePalette
{
    public const double MaxPct = 8;

    public static readonly (double Pct, SKColor Colour)[] Stops =
    [
        (0.0, new SKColor(35, 112, 64)), (1.25, new SKColor(48, 139, 67)), (2.25, new SKColor(77, 160, 68)),
        (3.0, new SKColor(116, 177, 66)), (3.3, new SKColor(160, 187, 63)), (3.5, new SKColor(207, 194, 58)),
        (4.25, new SKColor(235, 171, 50)), (5.25, new SKColor(238, 127, 45)), (6.25, new SKColor(224, 81, 42)),
        (7.25, new SKColor(199, 51, 43)), (8.0, new SKColor(161, 35, 43)),
    ];

    public static SKColor Colour(double slopePct)
    {
        if (!double.IsFinite(slopePct) || slopePct <= Stops[0].Pct) return Stops[0].Colour;
        for (int i = 1; i < Stops.Length; i++)
        {
            if (slopePct > Stops[i].Pct) continue;
            var (p0, c0) = Stops[i - 1];
            var (p1, c1) = Stops[i];
            double t = (slopePct - p0) / (p1 - p0);
            return new SKColor(Lerp(c0.Red, c1.Red, t), Lerp(c0.Green, c1.Green, t), Lerp(c0.Blue, c1.Blue, t));
        }
        return Stops[^1].Colour;
    }

    static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
}

/// <summary>
/// The single world→page transform: rotates so the approach points up the page,
/// fits the green outline into a rectangle, and flips Z (world north-up) to
/// canvas Y (down) exactly once.
/// </summary>
public class GreenView
{
    readonly Vec2 _centre, _forward, _right;
    readonly SKPoint _pageCentre;
    public float PointsPerMetre { get; protected init; }

    protected GreenView() { _forward = new Vec2(0, 1); _right = new Vec2(1, 0); }

    public GreenView(GreenAnalysis a, SKRect rect)
    {
        _forward = a.Forward;
        _right = new Vec2(a.Forward.Z, -a.Forward.X);
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var p in a.Outline)
        {
            double u = Dot(p, _right), v = Dot(p, _forward);
            minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
        }
        double midU = (minU + maxU) / 2, midV = (minV + maxV) / 2;
        _centre = _right * midU + _forward * midV;
        const double pad = 1.08;
        PointsPerMetre = (float)Math.Min(rect.Width / ((maxU - minU) * pad), rect.Height / ((maxV - minV) * pad));
        _pageCentre = new SKPoint(rect.MidX, rect.MidY);
    }

    static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;

    public virtual SKPoint Map(Vec2 world)
    {
        var d = world - _centre;
        return new SKPoint(
            _pageCentre.X + (float)(Dot(d, _right) * PointsPerMetre),
            _pageCentre.Y - (float)(Dot(d, _forward) * PointsPerMetre));
    }

    public SKPath Path(IReadOnlyList<Vec2> points, bool close)
    {
        using var builder = new SKPathBuilder();
        if (points.Count > 0)
        {
            builder.MoveTo(Map(points[0]));
            for (int i = 1; i < points.Count; i++) builder.LineTo(Map(points[i]));
            if (close) builder.Close();
        }
        return builder.Detach();
    }

    /// <summary>Unrotated view centred on the canvas origin (legend samples).</summary>
    public sealed class Fixed : GreenView
    {
        public Fixed(float pointsPerMetre) { PointsPerMetre = pointsPerMetre; }
        public override SKPoint Map(Vec2 world) => new((float)(world.X * PointsPerMetre), (float)(-world.Z * PointsPerMetre));
    }
}
