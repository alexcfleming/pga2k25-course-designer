using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>
/// The plate's art as float RGB over white paper, with the raster primitives the watercolour look is made of:
/// masks rasterised from paths, seeded value noise, Gaussian blur, a Euclidean distance transform, multiply
/// washes with pigment pooling at the edges, opaque covers, and ink overlays drawn with Skia and composited in.
/// Everything is deterministic for a given seed, so the same hole renders the same pixels twice.
/// </summary>
public sealed class WatercolourCanvas
{
    public int Width { get; }
    public int Height { get; }
    readonly float[] _r, _g, _b;

    /// <summary>Large washes (4 octaves, 220 px), mottle (3 octaves, 60 px) and paper grain / speckle (2 octaves, 6 px).</summary>
    public float[] N1 { get; }
    public float[] N2 { get; }
    public float[] NF { get; }

    public WatercolourCanvas(int width, int height, int seed)
    {
        Width = width; Height = height;
        int n = width * height;
        _r = new float[n]; _g = new float[n]; _b = new float[n];
        Array.Fill(_r, 1f); Array.Fill(_g, 1f); Array.Fill(_b, 1f);
        N1 = Noise(width, height, 220, 4, seed * 7919 + 1);
        N2 = Noise(width, height, 60, 3, seed * 7919 + 2);
        NF = Noise(width, height, 6, 2, seed * 7919 + 3);
    }

    // ───────────────────────── washes ─────────────────────────

    /// <summary>
    /// Watercolour wash, multiply blended: density follows the mask and the noise, and pigment pools in a band just
    /// inside the edge (mask minus its σ5 blur).
    /// </summary>
    public void Wash(float[] mask, SKColor colour, float strength = 1, float edge = 0.22f, float grain = 0.18f)
    {
        var e = edge > 0 ? Blur(mask, Width, Height, 5) : null;
        float cr = 1 - colour.Red / 255f, cg = 1 - colour.Green / 255f, cb = 1 - colour.Blue / 255f;
        Rows(y =>
        {
            for (int i = y * Width, end = i + Width; i < end; i++)
            {
                float m = mask[i];
                if (m <= 0) continue;
                float d = m * strength * (1 - grain + grain * (0.6f * N1[i] + 0.4f * N2[i]));
                if (e is not null) d += Math.Clamp(m - e[i], 0, 1) * 2.2f * edge;
                d = Math.Clamp(d, 0, 1);
                _r[i] *= 1 - d * cr; _g[i] *= 1 - d * cg; _b[i] *= 1 - d * cb;
            }
        });
    }

    /// <summary>Opaque reset toward <paramref name="colour"/> (sand, or the paper under a fairway wash).</summary>
    public void Cover(float[] mask, SKColor colour, float amount = 1)
    {
        float cr = colour.Red / 255f, cg = colour.Green / 255f, cb = colour.Blue / 255f;
        Rows(y =>
        {
            for (int i = y * Width, end = i + Width; i < end; i++)
            {
                float m = mask[i] * amount;
                if (m <= 0) continue;
                _r[i] += (cr - _r[i]) * m; _g[i] += (cg - _g[i]) * m; _b[i] += (cb - _b[i]) * m;
            }
        });
    }

    /// <summary>Draws ink with Skia on a transparent layer (antialiased) and composites it over the art.</summary>
    public void Ink(Action<SKCanvas> draw)
    {
        using var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        using (var c = new SKCanvas(bmp)) draw(c);
        var px = bmp.GetPixelSpan().ToArray();
        Rows(y =>
        {
            for (int i = y * Width, end = i + Width; i < end; i++)
            {
                int o = i * 4;
                float a = px[o + 3] / 255f;
                if (a <= 0) continue;
                // Premultiplied source over.
                _r[i] = px[o] / 255f + _r[i] * (1 - a);
                _g[i] = px[o + 1] / 255f + _g[i] * (1 - a);
                _b[i] = px[o + 2] / 255f + _b[i] * (1 - a);
            }
        });
    }

    /// <summary>Fades the art toward paper where <paramref name="zone"/> is below 1: 1 − (1 − pixel)·zone.</summary>
    public void FadeTo(float[] zone) => Rows(y =>
    {
        for (int i = y * Width, end = i + Width; i < end; i++)
        {
            float z = zone[i];
            _r[i] = 1 - (1 - _r[i]) * z; _g[i] = 1 - (1 - _g[i]) * z; _b[i] = 1 - (1 - _b[i]) * z;
        }
    });

    /// <summary>Fine paper grain, ±<paramref name="amount"/>/2.</summary>
    public void Grain(float amount = 0.025f) => Rows(y =>
    {
        for (int i = y * Width, end = i + Width; i < end; i++)
        {
            float k = 1 - amount * (NF[i] - 0.5f);
            _r[i] = Math.Min(1, _r[i] * k); _g[i] = Math.Min(1, _g[i] * k); _b[i] = Math.Min(1, _b[i] * k);
        }
    });

    /// <summary>Darkness (1 − luminance-ish minimum channel) at a pixel, for tests.</summary>
    public float Darkness(int x, int y) { int i = y * Width + x; return 1 - Math.Min(_r[i], Math.Min(_g[i], _b[i])); }

    public SKImage ToImage()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var bytes = new byte[Width * Height * 4];
        Rows(y =>
        {
            for (int i = y * Width, end = i + Width; i < end; i++)
            {
                int o = i * 4;
                bytes[o] = ToByte(_r[i]); bytes[o + 1] = ToByte(_g[i]); bytes[o + 2] = ToByte(_b[i]); bytes[o + 3] = 255;
            }
        });
        return SKImage.FromPixelCopy(info, bytes, Width * 4);
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);

    // ───────────────────────── masks and fields ─────────────────────────

    /// <summary>Rasterises whatever <paramref name="draw"/> fills (antialiased, any colour) into a 0–1 mask, blurred by σ.</summary>
    public float[] Mask(Action<SKCanvas, SKPaint> draw, float blur = 0.8f)
    {
        using var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Alpha8, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        using (var c = new SKCanvas(bmp))
        using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill })
            draw(c, p);
        var a = bmp.GetPixelSpan();
        var m = new float[Width * Height];
        for (int i = 0; i < m.Length; i++) m[i] = a[i] / 255f;
        return blur > 0 ? Blur(m, Width, Height, blur) : m;
    }

    public float[] Mask(SKPath? path, float blur = 0.8f) => Mask((c, p) => { if (path is not null) c.DrawPath(path, p); }, blur);

    public float[] Blur(float[] src, float sigma) => Blur(src, Width, Height, sigma);

    /// <summary>Distance (px) from every pixel to the nearest pixel where <paramref name="feature"/> holds.</summary>
    public float[] Distance(float[] mask, Func<float, bool> feature) => DistanceTransform(mask, Width, Height, feature);

    /// <summary>Shifts a field by whole pixels (dx right, dy down), filling with 0.</summary>
    public float[] Shift(float[] src, int dx, int dy)
    {
        var dst = new float[src.Length];
        int w = Width, h = Height;
        Rows(y =>
        {
            int sy = y - dy;
            if (sy < 0 || sy >= h) return;
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx;
                if (sx >= 0 && sx < w) dst[y * w + x] = src[sy * w + sx];
            }
        });
        return dst;
    }

    /// <summary>A new field from a per-pixel function of the index.</summary>
    public float[] Map(Func<int, float> f)
    {
        var dst = new float[Width * Height];
        Rows(y => { for (int i = y * Width, end = i + Width; i < end; i++) dst[i] = f(i); });
        return dst;
    }

    void Rows(Action<int> body) => Parallel.For(0, Height, body);

    // ───────────────────────── raster maths ─────────────────────────

    /// <summary>Value noise: octaves of seeded random grids, smoothly interpolated, normalised to 0–1.</summary>
    public static float[] Noise(int w, int h, int scale, int octaves, int seed)
    {
        var rng = new Random(seed);
        var outp = new float[w * h];
        float amp = 1, total = 0;
        for (int o = 0; o < octaves; o++)
        {
            int s = Math.Max(2, scale >> o);
            int gw = w / s + 3, gh = h / s + 3;
            var grid = new float[gw * gh];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
            float a = amp;
            Parallel.For(0, h, y =>
            {
                float fy = (float)y / s; int y0 = (int)fy; float ty = fy - y0; ty = ty * ty * (3 - 2 * ty);
                for (int x = 0; x < w; x++)
                {
                    float fx = (float)x / s; int x0 = (int)fx; float tx = fx - x0; tx = tx * tx * (3 - 2 * tx);
                    float v00 = grid[y0 * gw + x0], v10 = grid[y0 * gw + x0 + 1], v01 = grid[(y0 + 1) * gw + x0], v11 = grid[(y0 + 1) * gw + x0 + 1];
                    float top = v00 + (v10 - v00) * tx, bot = v01 + (v11 - v01) * tx;
                    outp[y * w + x] += (top + (bot - top) * ty) * a;
                }
            });
            total += amp; amp *= 0.5f;
        }
        for (int i = 0; i < outp.Length; i++) outp[i] /= total;
        return outp;
    }

    /// <summary>Gaussian blur approximated by three box blurs (edges clamped).</summary>
    public static float[] Blur(float[] src, int w, int h, float sigma)
    {
        if (sigma <= 0) return (float[])src.Clone();
        var sizes = BoxesForGauss(sigma, 3);
        var a = (float[])src.Clone();
        var b = new float[a.Length];
        foreach (int size in sizes)
        {
            int r = (size - 1) / 2;
            if (r <= 0) continue;
            BoxH(a, b, w, h, r);
            BoxV(b, a, w, h, r);
        }
        return a;
    }

    static int[] BoxesForGauss(double sigma, int n)
    {
        double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
        int wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;
        double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
        int m = (int)Math.Round(mIdeal);
        return Enumerable.Range(0, n).Select(i => i < m ? wl : wu).ToArray();
    }

    static void BoxH(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            float acc = 0;
            for (int x = -r - 1; x < r; x++) acc += src[row + Math.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                acc += src[row + Math.Min(x + r, w - 1)] - src[row + Math.Max(x - r - 1, 0)];
                dst[row + x] = acc * inv;
            }
        });
    }

    static void BoxV(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        Parallel.For(0, w, x =>
        {
            float acc = 0;
            for (int y = -r - 1; y < r; y++) acc += src[Math.Clamp(y, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                acc += src[Math.Min(y + r, h - 1) * w + x] - src[Math.Max(y - r - 1, 0) * w + x];
                dst[y * w + x] = acc * inv;
            }
        });
    }

    /// <summary>Exact Euclidean distance transform (Felzenszwalb–Huttenlocher), in pixels.</summary>
    public static float[] DistanceTransform(float[] mask, int w, int h, Func<float, bool> feature)
    {
        // "Infinity" stays small enough for exact double arithmetic in the parabola intersections.
        double inf = 2.0 * (w + h) * (w + h);
        var d = new double[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = feature(mask[i]) ? 0 : inf;
        Parallel.For(0, w, () => (new double[h], new double[h], new int[h], new double[h + 1]), (x, _, buf) =>
        {
            var (f, outp, v, z) = buf;
            for (int y = 0; y < h; y++) f[y] = d[y * w + x];
            Dt1(f, outp, v, z, h);
            for (int y = 0; y < h; y++) d[y * w + x] = outp[y];
            return buf;
        }, _ => { });
        var result = new float[w * h];
        Parallel.For(0, h, () => (new double[w], new double[w], new int[w], new double[w + 1]), (y, _, buf) =>
        {
            var (f, outp, v, z) = buf;
            Array.Copy(d, y * w, f, 0, w);
            Dt1(f, outp, v, z, w);
            for (int x = 0; x < w; x++) result[y * w + x] = (float)Math.Sqrt(outp[x]);
            return buf;
        }, _ => { });
        return result;
    }

    static void Dt1(double[] f, double[] d, int[] v, double[] z, int n)
    {
        int k = 0;
        v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            }
            k++;
            v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            int p = v[k];
            d[q] = (q - p) * (double)(q - p) + f[p];
        }
    }
}
