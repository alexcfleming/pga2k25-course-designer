using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// Turns a cloud of overlapping/nearby pieces (brush dabs, ribbons, small polygons) into smooth
/// merged outlines: rasterise to a mask, morphologically close it (grow by R, fuse, shrink by R) so
/// gaps narrower than ~2R and pinholes fill, round the result with a light blur, then trace the
/// iso-line. The result reads like a designer's hand-drawn bunker.
/// </summary>
public static class ShapeMerger
{
    /// <param name="closeRadius">Morphological closing radius R in metres; gaps up to about 2R fuse.</param>
    /// <param name="smooth">Final rounding blur (m).</param>
    /// <param name="cell">Raster resolution in metres.</param>
    public static List<List<Vec2>> Merge(IReadOnlyList<IReadOnlyList<Vec2>> polygons, IReadOnlyList<Ribbon> ribbons,
        IReadOnlyList<Disc> discs, double closeRadius = 1.6, double smooth = 0.6, double cell = 0.35, double discInflate = 1.0,
        IReadOnlyList<IReadOnlyList<Vec2>>? keepOut = null, double keepOutReach = 1.0)
    {
        var pts = polygons.SelectMany(p => p).Concat(ribbons.SelectMany(r => r.Points)).Concat(discs.Select(d => d.Centre)).ToList();
        if (pts.Count == 0) return [];
        double pad = 3 * closeRadius + 4 * smooth + 4;
        double minX = pts.Min(p => p.X) - pad, minZ = pts.Min(p => p.Z) - pad;
        double maxX = pts.Max(p => p.X) + pad, maxZ = pts.Max(p => p.Z) + pad;
        int w = (int)Math.Ceiling((maxX - minX) / cell), h = (int)Math.Ceiling((maxZ - minZ) / cell);

        // 1. Rasterise every piece into an 8-bit mask with Skia (grid units = cells).
        var info = new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            SKPoint G(Vec2 v) => new((float)((v.X - minX) / cell), (float)((v.Z - minZ) / cell));
            using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            foreach (var poly in polygons)
            {
                using var b = new SKPathBuilder();
                b.MoveTo(G(poly[0]));
                for (int i = 1; i < poly.Count; i++) b.LineTo(G(poly[i]));
                b.Close();
                using var path = b.Detach();
                canvas.DrawPath(path, fill);
            }
            foreach (var r in ribbons)
            {
                stroke.StrokeWidth = (float)(r.Width / cell);
                using var b = new SKPathBuilder();
                b.MoveTo(G(r.Points[0]));
                for (int i = 1; i < r.Points.Count; i++) b.LineTo(G(r.Points[i]));
                using var path = b.Detach();
                canvas.DrawPath(path, stroke);
            }
            foreach (var d in discs)
                canvas.DrawCircle(G(d.Centre), (float)(d.Radius * discInflate / cell), fill);
        }

        // Keep-out mask (e.g. fairways and greens): closing may not bridge across these.
        bool[]? blocked = null;
        if (keepOut is { Count: > 0 })
        {
            using var kb = new SKBitmap(info);
            using (var kc = new SKCanvas(kb))
            {
                kc.Clear(SKColors.Black);
                SKPoint G2(Vec2 v) => new((float)((v.X - minX) / cell), (float)((v.Z - minZ) / cell));
                using var kf = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Fill };
                foreach (var poly in keepOut)
                {
                    using var b = new SKPathBuilder();
                    b.MoveTo(G2(poly[0]));
                    for (int i = 1; i < poly.Count; i++) b.LineTo(G2(poly[i]));
                    b.Close();
                    using var path = b.Detach();
                    kc.DrawPath(path, kf);
                }
            }
            var ks = kb.GetPixelSpan();
            blocked = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    blocked[y * w + x] = ks[y * kb.RowBytes + x] >= 128;
        }

        // 2. Closing = dilate then erode, both by R, using a Euclidean distance transform.
        var mask = new bool[w * h];
        var span = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[y * w + x] = span[y * stride + x] >= 128;
        double rCells = closeRadius / cell;
        var toInside = DistanceTransform(mask, w, h);                 // distance to nearest filled cell
        var dilated = new bool[w * h];
        for (int i = 0; i < mask.Length; i++) dilated[i] = toInside[i] <= rCells * rCells;
        var outside = new bool[w * h];
        for (int i = 0; i < mask.Length; i++) outside[i] = !dilated[i];
        var toOutside = DistanceTransform(outside, w, h);             // distance to nearest empty cell
        var field = new float[w * h];
        double reach2 = (keepOutReach / cell) * (keepOutReach / cell);
        for (int i = 0; i < mask.Length; i++)
        {
            bool closed = toOutside[i] > rCells * rCells;
            // On keep-out ground, only keep fill that is genuinely next to an original piece.
            if (closed && blocked is not null && blocked[i] && toInside[i] > reach2) closed = false;
            field[i] = closed ? 1f : 0f;
        }

        // 3. Round off the closed shape, then trace the half-level iso-line.
        Blur(field, w, h, smooth / cell);
        var loops = new List<List<Vec2>>();
        foreach (var loop in Contours(field, w, h, 0.5f))
        {
            if (loop.Count < 6) continue;
            var world = loop.Select(p => new Vec2(minX + p.X * cell, minZ + p.Y * cell)).ToList();
            if (Geometry.Area(world) < 0.6) continue;
            loops.Add(Chaikin(Chaikin(world)));
        }
        return loops;
    }

    /// <summary>
    /// Squared Euclidean distance (in cells) from every cell to the nearest <c>true</c> cell
    /// (Felzenszwalb–Huttenlocher separable transform).
    /// </summary>
    static double[] DistanceTransform(bool[] target, int w, int h)
    {
        const double Inf = 1e20;
        var d = new double[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = target[i] ? 0 : Inf;
        var line = new double[Math.Max(w, h)];
        var outLine = new double[Math.Max(w, h)];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) line[y] = d[y * w + x];
            Dt1(line, h, outLine);
            for (int y = 0; y < h; y++) d[y * w + x] = outLine[y];
        }
        for (int y = 0; y < h; y++)
        {
            Array.Copy(d, y * w, line, 0, w);
            Dt1(line, w, outLine);
            Array.Copy(outLine, 0, d, y * w, w);
        }
        return d;
    }

    static void Dt1(double[] f, int n, double[] result)
    {
        var v = new int[n];
        var z = new double[n + 1];
        int k = 0;
        v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                if (s <= z[k] && k > 0) k--; else break;
            }
            if (s <= z[k]) { v[k] = q; z[k] = double.NegativeInfinity; z[k + 1] = double.PositiveInfinity; continue; }
            k++; v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            double dq = q - v[k];
            result[q] = dq * dq + f[v[k]];
        }
    }

    static void Blur(float[] f, int w, int h, double sigmaCells)
    {
        int r = Math.Max(1, (int)Math.Ceiling(sigmaCells * 3));
        var k = new float[2 * r + 1];
        float sum = 0;
        for (int i = -r; i <= r; i++) sum += k[i + r] = (float)Math.Exp(-i * i / (2 * sigmaCells * sigmaCells));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        var tmp = new float[f.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = 0;
                for (int j = -r; j <= r; j++) { int xx = x + j; if (xx >= 0 && xx < w) a += f[y * w + xx] * k[j + r]; }
                tmp[y * w + x] = a;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = 0;
                for (int j = -r; j <= r; j++) { int yy = y + j; if (yy >= 0 && yy < h) a += tmp[yy * w + x] * k[j + r]; }
                f[y * w + x] = a;
            }
    }

    /// <summary>Marching squares on a zero-bordered grid; returns closed loops in grid coordinates.</summary>
    static List<List<SKPoint>> Contours(float[] f, int w, int h, float level)
    {
        var segs = new List<(long A, long B, SKPoint PA, SKPoint PB)>();
        long HE(int x, int y) => ((long)y * w + x) * 2;
        long VE(int x, int y) => ((long)y * w + x) * 2 + 1;
        SKPoint L(int ax, int ay, int bx, int by)
        {
            float va = f[ay * w + ax], vb = f[by * w + bx];
            float t = Math.Abs(vb - va) < 1e-6f ? 0.5f : (level - va) / (vb - va);
            return new SKPoint(ax + (bx - ax) * t, ay + (by - ay) * t);
        }
        for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                float v00 = f[y * w + x], v10 = f[y * w + x + 1], v01 = f[(y + 1) * w + x], v11 = f[(y + 1) * w + x + 1];
                int c = (v00 >= level ? 1 : 0) | (v10 >= level ? 2 : 0) | (v11 >= level ? 4 : 0) | (v01 >= level ? 8 : 0);
                if (c == 0 || c == 15) continue;
                (long, SKPoint) B() => (HE(x, y), L(x, y, x + 1, y));
                (long, SKPoint) R() => (VE(x + 1, y), L(x + 1, y, x + 1, y + 1));
                (long, SKPoint) T() => (HE(x, y + 1), L(x, y + 1, x + 1, y + 1));
                (long, SKPoint) Lf() => (VE(x, y), L(x, y, x, y + 1));
                void Add((long, SKPoint) a, (long, SKPoint) b) => segs.Add((a.Item1, b.Item1, a.Item2, b.Item2));
                bool centre = (v00 + v10 + v01 + v11) / 4 >= level;
                switch (c)
                {
                    case 1: case 14: Add(Lf(), B()); break;
                    case 2: case 13: Add(B(), R()); break;
                    case 3: case 12: Add(Lf(), R()); break;
                    case 4: case 11: Add(R(), T()); break;
                    case 6: case 9: Add(B(), T()); break;
                    case 7: case 8: Add(Lf(), T()); break;
                    case 5: if (centre) { Add(Lf(), T()); Add(B(), R()); } else { Add(Lf(), B()); Add(R(), T()); } break;
                    case 10: if (centre) { Add(Lf(), B()); Add(R(), T()); } else { Add(Lf(), T()); Add(B(), R()); } break;
                }
            }

        var byEdge = new Dictionary<long, List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            (byEdge.TryGetValue(segs[i].A, out var la) ? la : byEdge[segs[i].A] = []).Add(i);
            (byEdge.TryGetValue(segs[i].B, out var lb) ? lb : byEdge[segs[i].B] = []).Add(i);
        }
        var used = new bool[segs.Count];
        var loops = new List<List<SKPoint>>();
        for (int s = 0; s < segs.Count; s++)
        {
            if (used[s]) continue;
            used[s] = true;
            var loop = new List<SKPoint> { segs[s].PA, segs[s].PB };
            long edge = segs[s].B;
            while (byEdge.TryGetValue(edge, out var cand))
            {
                int next = cand.FirstOrDefault(i => !used[i], -1);
                if (next < 0) break;
                used[next] = true;
                var g = segs[next];
                (edge, var p) = g.A == edge ? (g.B, g.PB) : (g.A, g.PA);
                loop.Add(p);
            }
            loops.Add(loop);
        }
        return loops;
    }

    /// <summary>One round of Chaikin corner cutting on a closed polygon.</summary>
    static List<Vec2> Chaikin(List<Vec2> p)
    {
        var r = new List<Vec2>(p.Count * 2);
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            r.Add(a * 0.75 + b * 0.25);
            r.Add(a * 0.25 + b * 0.75);
        }
        return r;
    }
}
