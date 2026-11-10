using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Course Portrait's "property parcel": the crisp cut-out the course sits in. Every hole route (back tee through
/// its waypoints) widened by <see cref="RouteReach"/> m and every fairway and green widened by <see cref="SurfaceReach"/>
/// m, closed so the gaps between holes fill (grow <see cref="CloseGrow"/>, shrink <see cref="CloseShrink"/>), kept as the
/// largest piece without holes, then simplified (Douglas–Peucker, <see cref="FacetTolerance"/> m) into long straight
/// property-line facets. Built on a raster in world metres with exact distance transforms, so the closing has round
/// joins; the simplify straightens them.
/// </summary>
public static class PortraitParcel
{
    public const double RouteReach = 42, SurfaceReach = 18, CloseGrow = 170, CloseShrink = 150, FacetTolerance = 16;
    const double Cell = 2;

    /// <summary>The parcel outline in world X/Z, or empty for a course with no holes.</summary>
    public static List<Vec2> Build(CourseLayers layers)
    {
        var routes = layers.Holes.Select(h => h.Route).Where(r => r.Count > 0).ToList();
        var surfaces = layers.Fairways.Concat(layers.Greens).Where(p => p.Count >= 3).ToList();
        var pts = routes.SelectMany(r => r).Concat(surfaces.SelectMany(p => p)).ToList();
        if (pts.Count == 0) return [];
        double pad = RouteReach + SurfaceReach + CloseGrow + 4 * Cell;
        double minX = pts.Min(p => p.X) - pad, minZ = pts.Min(p => p.Z) - pad;
        int w = (int)Math.Ceiling((pts.Max(p => p.X) + pad - minX) / Cell), h = (int)Math.Ceiling((pts.Max(p => p.Z) + pad - minZ) / Cell);
        SKPoint G(Vec2 v) => new((float)((v.X - minX) / Cell), (float)((v.Z - minZ) / Cell));

        bool[] Raster(Action<SKCanvas> draw)
        {
            using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque));
            using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.Black); draw(c); }
            var span = bmp.GetPixelSpan();
            var m = new bool[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = span[y * bmp.RowBytes + x] >= 128;
            return m;
        }

        var routeMask = Raster(c =>
        {
            using var stroke = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)(2 * RouteReach / Cell), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            using var dot = new SKPaint { Color = SKColors.White, IsAntialias = true };
            foreach (var r in routes)
            {
                if (r.Count == 1) { c.DrawCircle(G(r[0]), (float)(RouteReach / Cell), dot); continue; }
                using var b = new SKPathBuilder();
                b.MoveTo(G(r[0]));
                foreach (var p in r.Skip(1)) b.LineTo(G(p));
                using var path = b.Detach();
                c.DrawPath(path, stroke);
            }
        });
        var surfaceMask = Raster(c =>
        {
            using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
            foreach (var poly in surfaces) using (var path = Path(poly, G)) c.DrawPath(path, fill);
        });

        // core = routes ∪ surfaces grown by 18 m; then grow 170 and shrink 150 (Euclidean, exact).
        var core = Grow(surfaceMask, w, h, SurfaceReach / Cell);
        for (int i = 0; i < core.Length; i++) core[i] |= routeMask[i];
        var grown = Grow(core, w, h, CloseGrow / Cell);
        var shrunk = Grow(grown.Select(b => !b).ToArray(), w, h, CloseShrink / Cell);
        var field = new float[w * h];
        for (int i = 0; i < field.Length; i++) field[i] = shrunk[i] ? 0f : 1f;

        // The largest loop is the outer edge of the largest piece; inner loops (holes) and smaller pieces are dropped.
        var loops = ShapeMerger.Contours(field, w, h, 0.5f)
            .Select(l => l.Select(p => new Vec2(minX + p.X * Cell, minZ + p.Y * Cell)).ToList())
            .Where(l => l.Count >= 3).ToList();
        if (loops.Count == 0) return [];
        var outer = loops.MaxBy(l => Math.Abs(Geometry.Area(l)))!;
        return SimplifyClosed(outer, FacetTolerance);
    }

    /// <summary>Cells within <paramref name="cells"/> of a set cell.</summary>
    static bool[] Grow(bool[] mask, int w, int h, double cells)
    {
        var d2 = ShapeMerger.DistanceTransform(mask, w, h);
        var r2 = cells * cells;
        var outp = new bool[mask.Length];
        for (int i = 0; i < outp.Length; i++) outp[i] = d2[i] <= r2;
        return outp;
    }

    internal static SKPath Path(IReadOnlyList<Vec2> poly, Func<Vec2, SKPoint> map)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(map(poly[0]));
        for (int i = 1; i < poly.Count; i++) b.LineTo(map(poly[i]));
        b.Close();
        return b.Detach();
    }

    /// <summary>Douglas–Peucker on a closed ring: split at the two points farthest apart, simplify each half.</summary>
    public static List<Vec2> SimplifyClosed(IReadOnlyList<Vec2> ring, double tolerance)
    {
        var pts = ring.ToList();
        if (pts.Count > 1 && Vec2.Distance(pts[0], pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 4) return pts;
        int a = 0, b = 0; double best = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            double d = Vec2.Distance(pts[0], pts[i]);
            if (d > best) { best = d; a = i; }
        }
        best = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            double d = Vec2.Distance(pts[a], pts[i]);
            if (d > best) { best = d; b = i; }
        }
        if (a > b) (a, b) = (b, a);
        var first = pts.Skip(a).Take(b - a + 1).ToList();
        var second = pts.Skip(b).Concat(pts.Take(a + 1)).ToList();
        var result = Dp(first, tolerance);
        result.RemoveAt(result.Count - 1);
        var rest = Dp(second, tolerance);
        rest.RemoveAt(rest.Count - 1);
        result.AddRange(rest);
        return result;
    }

    static List<Vec2> Dp(List<Vec2> p, double tol)
    {
        var keep = new bool[p.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, p.Count - 1));
        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            double best = 0; int idx = -1;
            for (int i = s + 1; i < e; i++)
            {
                double d = SegmentDistance(p[i], p[s], p[e]);
                if (d > best) { best = d; idx = i; }
            }
            if (idx >= 0 && best > tol) { keep[idx] = true; stack.Push((s, idx)); stack.Push((idx, e)); }
        }
        return p.Where((_, i) => keep[i]).ToList();
    }

    static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2, 0, 1);
        return Vec2.Distance(p, new Vec2(a.X + t * dx, a.Z + t * dz));
    }
}
