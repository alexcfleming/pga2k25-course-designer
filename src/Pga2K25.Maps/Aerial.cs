using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The ground the Routing Plan scorecard theme draws: the replayed terrain as a grey aerial (hillshade with a fine
/// scrub texture, the sea grey-blue), the same land in soft greens for the property, and white contour lines. Built
/// once per map in page units from the course file, then drawn for the PNG and the PDF alike.
/// </summary>
public sealed class Aerial : IDisposable
{
    /// <summary>Height samples per page unit (1/100 in); drawn smoothed, so plenty for print.</summary>
    const float Density = 1.5f;
    /// <summary>Slope is measured across this (m), as on Modern Illustrated's hillshade.</summary>
    const double SlopeSpanMetres = 12;
    /// <summary>Contour intervals (m) to choose from; the first that gives no more than <see cref="MaxContours"/> lines over the holes' relief.</summary>
    static readonly double[] Intervals = [0.5, 1, 2, 2.5, 5, 10];
    const int MaxContours = 26;

    public static readonly SKColor AerialBase = SKColor.Parse("#c2c2bc"), AerialLit = SKColor.Parse("#d9d9d4"), AerialShade = SKColor.Parse("#9a9a94");
    public static readonly SKColor SeaLight = SKColor.Parse("#b9c3c4"), SeaDeep = SKColor.Parse("#97a7aa");
    public static readonly SKColor LandBase = SKColor.Parse("#a8b98a"), LandLit = SKColor.Parse("#c0cda5"), LandShade = SKColor.Parse("#879872");

    /// <summary>The grey aerial over the whole rectangle.</summary>
    public required SKImage Grey { get; init; }
    /// <summary>The same land in soft greens, shown through the property.</summary>
    public required SKImage Land { get; init; }
    public required SKRect Rect { get; init; }
    /// <summary>Contour segments (page units) from marching squares; every fifth level is Major.</summary>
    public required IReadOnlyList<(SKPoint A, SKPoint B, bool Major)> Segments { get; init; }
    /// <summary>The contour interval in metres.</summary>
    public required double Interval { get; init; }

    public static Aerial Build(CourseModel course, CourseLayers layers, MapProjection p, SKRect rect)
    {
        var corners = new[] { new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Top),
                              new SKPoint(rect.Left, rect.Bottom), new SKPoint(rect.Right, rect.Bottom) }.Select(p.Unmap).ToList();
        double pad = SlopeSpanMetres;
        var min = new Vec2(corners.Min(c => c.X) - pad, corners.Min(c => c.Z) - pad);
        var max = new Vec2(corners.Max(c => c.X) + pad, corners.Max(c => c.Z) + pad);
        var field = TerrainReplay.Build(course, min, max, new TerrainModelOptions { CellMetres = 2, LargeBrushHardness = 0.3 });
        double sea = TerrainReplay.DetectSeaLevel(course) + 0.05;

        int w = (int)Math.Ceiling(rect.Width * Density), h = (int)Math.Ceiling(rect.Height * Density);
        SKPoint Page(int x, int y) => new(rect.Left + (x + 0.5f) / Density, rect.Top + (y + 0.5f) / Density);
        var z = new double[w * h];
        var lit = new double[w * h];
        // Lit from the upper left of the page, whatever the map's rotation.
        var light = (p.Unmap(new SKPoint(rect.Left, rect.Top)) - p.Unmap(new SKPoint(rect.Left + 1, rect.Top + 1))).Normalized();
        double span = SlopeSpanMetres / 2;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                var at = p.Unmap(Page(x, y));
                z[y * w + x] = field.Sample(at);
                double gx = (field.Sample(at + new Vec2(span, 0)) - field.Sample(at - new Vec2(span, 0))) / (2 * span);
                double gz = (field.Sample(at + new Vec2(0, span)) - field.Sample(at - new Vec2(0, span))) / (2 * span);
                lit[y * w + x] = Math.Clamp(-(gx * light.X + gz * light.Z) * 2.4, -1, 1);
            }
        });

        SKColor Sea(int i) => Lerp(SeaLight, SeaDeep, Math.Clamp((sea - z[i]) / 8, 0, 1));
        var grey = Raster(w, h, (x, y, i) =>
        {
            if (z[i] <= sea) return Sea(i);
            var c = lit[i] >= 0 ? Lerp(AerialBase, AerialLit, lit[i]) : Lerp(AerialBase, AerialShade, -lit[i]);
            // Broad mottling, and a fine speckle of scrub where the fine noise peaks.
            var q = Page(x, y);
            double broad = Noise.Fbm(q.X / 38, q.Y / 38, 4) - 0.5, fine = Noise.Fbm(q.X / 2.2, q.Y / 2.2, 2);
            return Scale(c, 1 + broad * 0.16 - (fine > 0.68 ? (fine - 0.68) * 1.1 : 0));
        });
        var land = Raster(w, h, (x, y, i) =>
        {
            if (z[i] <= sea) return Sea(i);
            var c = lit[i] >= 0 ? Lerp(LandBase, LandLit, lit[i]) : Lerp(LandBase, LandShade, -lit[i]);
            var q = Page(x, y);
            double broad = Noise.Fbm(q.X / 30, q.Y / 30, 4) - 0.5, fine = Noise.Fbm(q.X / 2.5, q.Y / 2.5, 2);
            return Scale(c, 1 + broad * 0.12 - (fine > 0.7 ? (fine - 0.7) * 0.8 : 0));
        });

        // The interval follows the relief along the holes, so a flat links and a mountain course both read.
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var at in layers.Holes.SelectMany(hl => hl.Route))
        {
            var q = p.Map(at);
            int x = Math.Clamp((int)((q.X - rect.Left) * Density), 0, w - 1), y = Math.Clamp((int)((q.Y - rect.Top) * Density), 0, h - 1);
            lo = Math.Min(lo, z[y * w + x]); hi = Math.Max(hi, z[y * w + x]);
        }
        double relief = lo > hi ? 1 : Math.Max(1, hi - lo);
        double step = Intervals.FirstOrDefault(s => relief / s <= MaxContours, Intervals[^1]);
        return new Aerial { Grey = grey, Land = land, Rect = rect, Segments = Contours(z, w, h, sea, step, rect), Interval = step };
    }

    /// <summary>Marching squares on every second sample: one segment per level crossing a cell.</summary>
    static List<(SKPoint, SKPoint, bool)> Contours(double[] z, int w, int h, double sea, double step, SKRect rect)
    {
        const int sub = 2;
        float cell = sub / Density;
        var segments = new List<(SKPoint, SKPoint, bool)>();
        Span<SKPoint> cut = stackalloc SKPoint[4];
        for (int y = 0; y + sub < h; y += sub)
            for (int x = 0; x + sub < w; x += sub)
            {
                double a = z[y * w + x], b = z[y * w + x + sub], c = z[(y + sub) * w + x + sub], d = z[(y + sub) * w + x];
                double lo = Math.Min(Math.Min(a, b), Math.Min(c, d)), hi = Math.Max(Math.Max(a, b), Math.Max(c, d));
                if (hi <= sea) continue;
                float x0 = rect.Left + (x + 0.5f) / Density, y0 = rect.Top + (y + 0.5f) / Density;
                var A = new SKPoint(x0, y0); var B = new SKPoint(x0 + cell, y0);
                var C = new SKPoint(x0 + cell, y0 + cell); var D = new SKPoint(x0, y0 + cell);
                for (double level = Math.Ceiling(lo / step) * step; level <= hi; level += step)
                {
                    if (level <= sea) continue;
                    int n = 0;
                    void Edge(double p, double q, SKPoint P, SKPoint Q, Span<SKPoint> into)
                    {
                        if ((p < level) == (q < level)) return;
                        float t = (float)((level - p) / (q - p));
                        into[n++] = new SKPoint(P.X + (Q.X - P.X) * t, P.Y + (Q.Y - P.Y) * t);
                    }
                    Edge(a, b, A, B, cut); Edge(b, c, B, C, cut); Edge(c, d, C, D, cut); Edge(d, a, D, A, cut);
                    double fifth = level / (step * 5);
                    bool heavy = Math.Abs(fifth - Math.Round(fifth)) < 1e-6;
                    for (int s = 0; s + 1 < n; s += 2) segments.Add((cut[s], cut[s + 1], heavy));
                }
            }
        return segments;
    }

    /// <summary>
    /// The contours sorted into <paramref name="zones"/> by <paramref name="zoneOf"/> (at each segment's middle), each
    /// zone's segments chained into polylines, the minor and major lines apart.
    /// </summary>
    public (SKPath Minor, SKPath Major)[] Zoned(int zones, Func<SKPoint, int> zoneOf)
    {
        var lists = Enumerable.Range(0, zones * 2).Select(_ => new List<(SKPoint, SKPoint)>()).ToArray();
        foreach (var (a, b, major) in Segments)
        {
            int zone = Math.Clamp(zoneOf(new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2)), 0, zones - 1);
            lists[zone * 2 + (major ? 1 : 0)].Add((a, b));
        }
        return Enumerable.Range(0, zones).Select(i => (Chain(lists[i * 2]), Chain(lists[i * 2 + 1]))).ToArray();
    }

    /// <summary>Joins segments that meet end to end into polylines: a fraction of the PDF size of loose segments.</summary>
    static SKPath Chain(List<(SKPoint A, SKPoint B)> segments)
    {
        static (int, int) Key(SKPoint p) => ((int)MathF.Round(p.X * 20), (int)MathF.Round(p.Y * 20));
        var ends = new Dictionary<(int, int), List<int>>();
        void Index(SKPoint p, int i)
        {
            if (!ends.TryGetValue(Key(p), out var list)) ends[Key(p)] = list = new List<int>(2);
            list.Add(i);
        }
        for (int i = 0; i < segments.Count; i++) { Index(segments[i].A, i); Index(segments[i].B, i); }
        var used = new bool[segments.Count];
        int Next(SKPoint end)
        {
            foreach (int j in ends[Key(end)]) if (!used[j]) return j;
            return -1;
        }
        SKPoint Other(int j, SKPoint end) => Key(segments[j].A) == Key(end) ? segments[j].B : segments[j].A;

        using var b = new SKPathBuilder();
        var line = new LinkedList<SKPoint>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            line.Clear();
            line.AddLast(segments[i].A);
            line.AddLast(segments[i].B);
            for (int j = Next(line.Last!.Value); j >= 0; j = Next(line.Last!.Value)) { used[j] = true; line.AddLast(Other(j, line.Last!.Value)); }
            for (int j = Next(line.First!.Value); j >= 0; j = Next(line.First!.Value)) { used[j] = true; line.AddFirst(Other(j, line.First!.Value)); }
            b.MoveTo(line.First!.Value);
            foreach (var q in line.Skip(1)) b.LineTo(q);
        }
        return b.Detach();
    }

    public void DrawGrey(SKCanvas canvas) => canvas.DrawImage(Grey, Rect, new SKSamplingOptions(SKFilterMode.Linear));

    /// <summary>The soft-green land, shown only inside <paramref name="property"/> with a feathered edge.</summary>
    public void DrawLand(SKCanvas canvas, SKPath property)
    {
        canvas.SaveLayer();
        canvas.DrawImage(Land, Rect, new SKSamplingOptions(SKFilterMode.Linear));
        // The mask is its own layer, so DstIn also clears everything outside the property.
        using (var dstIn = new SKPaint { BlendMode = SKBlendMode.DstIn })
        {
            canvas.SaveLayer(dstIn);
            using var mask = new SKPaint { IsAntialias = true, Color = SKColors.White, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5) };
            canvas.DrawPath(property, mask);
            canvas.Restore();
        }
        canvas.Restore();
    }

    /// <summary>White contour lines at <paramref name="alpha"/>; every fifth a little heavier.</summary>
    public static void DrawContours(SKCanvas canvas, (SKPath Minor, SKPath Major) lines, byte alpha)
    {
        using var thin = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.42f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = SKColors.White.WithAlpha(alpha) };
        using var heavy = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.7f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = SKColors.White.WithAlpha((byte)Math.Min(255, alpha * 1.35)) };
        canvas.DrawPath(lines.Minor, thin);
        canvas.DrawPath(lines.Major, heavy);
    }

    static SKImage Raster(int w, int h, Func<int, int, int, SKColor> colour)
    {
        var pixels = new SKColor[w * h];
        Parallel.For(0, h, y => { for (int x = 0; x < w; x++) pixels[y * w + x] = colour(x, y, y * w + x); });
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque) { Pixels = pixels };
        return SKImage.FromBitmap(bitmap);
    }

    static SKColor Lerp(SKColor a, SKColor b, double t) =>
        new((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));

    static SKColor Scale(SKColor c, double f) =>
        new((byte)Math.Clamp(c.Red * f, 0, 255), (byte)Math.Clamp(c.Green * f, 0, 255), (byte)Math.Clamp(c.Blue * f, 0, 255));

    public void Dispose() { Grey.Dispose(); Land.Dispose(); }

    /// <summary>Smooth value noise, deterministic, for the aerial's texture.</summary>
    static class Noise
    {
        static double Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
        }

        static double Value(double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            double a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        public static double Fbm(double x, double y, int octaves)
        {
            double sum = 0, amp = 0.5, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += Value(x, y) * amp; norm += amp; x *= 2.03; y *= 2.03; amp *= 0.5; }
            return sum / norm;
        }
    }
}
