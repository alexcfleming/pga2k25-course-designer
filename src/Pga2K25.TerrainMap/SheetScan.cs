using SkiaSharp;
using UglyToad.PdfPig;

namespace Pga2K25.TerrainMap;

/// <summary>A scanned page: the file it came from, its page number (1 for an image file) and the picture.</summary>
public sealed record ScanPage(string File, int Page, SKBitmap Bitmap);

public static class ScanPages
{
    public static readonly string[] Patterns = ["*.pdf", "*.jpg", "*.jpeg", "*.png"];

    /// <summary>
    /// The pages of a scan: the largest picture on each PDF page (phone scanners such as TurboScan save one JPEG per
    /// page), or the image file itself.
    /// </summary>
    public static IEnumerable<ScanPage> Load(string path)
    {
        if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (SKBitmap.Decode(path) is { } single) yield return new ScanPage(path, 1, single);
            yield break;
        }
        using var doc = PdfDocument.Open(path);
        foreach (var page in doc.GetPages())
        {
            var picture = page.GetImages().OrderByDescending(i => (long)i.WidthInSamples * i.HeightInSamples).FirstOrDefault();
            if (picture is null) continue;
            // JPEG (DCT) pictures are stored as the file itself; anything else PdfPig converts to PNG.
            byte[] bytes = picture.TryGetPng(out var png) ? png : picture.RawBytes.ToArray();
            if (SKBitmap.Decode(bytes) is { } bmp) yield return new ScanPage(path, page.Number, bmp);
        }
    }
}

/// <summary>Where a printed map frame lies in a scan: its corners (top-left, top-right, bottom-right, bottom-left) in scan pixels.</summary>
public sealed record FrameFit(SKPoint[] Corners, double Quality);

/// <summary>
/// Image work on scanned sheets: find the printed map frame, straighten the frame into a rectangle, and separate
/// hand sketching from the printed sheet. Sizes are tuned for a frame straightened at 3 px per point.
/// </summary>
public static class SheetScan
{
    /// <summary>Straightened frame resolution, px per point (216 dpi).</summary>
    public const int PixelsPerPoint = 3;

    // ───────────────────────────── frame ─────────────────────────────

    /// <summary>
    /// Finds the map frame of a page laid out as <paramref name="page"/> with the frame at <paramref name="frame"/>
    /// (points). The scan is assumed to be roughly the page, upright, as a phone scanner crops it. Each side's
    /// line is fitted (RANSAC) to the darkest pixel across a band around where it should be, so grid labels
    /// beside the frame don't pull it. Null when a side can't be found or the frame isn't the right shape.
    /// </summary>
    public static FrameFit? FindFrame(SKBitmap scan, SKSize page, SKRect frame)
    {
        using var rgba = ToRgba(scan);
        int w = rgba.Width, h = rgba.Height;
        var lum = Luma(rgba);
        float sx = w / page.Width, sy = h / page.Height;
        float x0 = frame.Left * sx, y0 = frame.Top * sy, x1 = frame.Right * sx, y1 = frame.Bottom * sy;
        float bx = 0.035f * w, by = 0.03f * h, tol = Math.Max(2f, 0.0012f * w);
        float vy0 = y0 + 0.1f * (y1 - y0), vy1 = y1 - 0.1f * (y1 - y0), hx0 = x0 + 0.1f * (x1 - x0), hx1 = x1 - 0.1f * (x1 - x0);

        var left = Side(lum, w, h, true, x0, vy0, vy1, bx, tol);
        var right = Side(lum, w, h, true, x1, vy0, vy1, bx, tol);
        if (left is null || right is null) return null;

        // The header's rule runs just above the frame's top, as long as it and nearly as dark, so the top and bottom
        // are looked for only near where the side lines end (they run unbroken corner to corner).
        Line? top = null, bottom = null;
        if (LineEnds(lum, w, h, left.Value) is { } le && LineEnds(lum, w, h, right.Value) is { } re)
        {
            float narrow = 0.008f * h;
            float topY = (le.Top.Y + re.Top.Y) / 2, botY = (le.Bottom.Y + re.Bottom.Y) / 2;
            top = Side(lum, w, h, false, topY, hx0, hx1, Math.Abs(le.Top.Y - re.Top.Y) / 2 + narrow, tol) ?? Through(le.Top, re.Top);
            bottom = Side(lum, w, h, false, botY, hx0, hx1, Math.Abs(le.Bottom.Y - re.Bottom.Y) / 2 + narrow, tol) ?? Through(le.Bottom, re.Bottom);
        }
        top ??= Side(lum, w, h, false, y0, hx0, hx1, by, tol);
        bottom ??= Side(lum, w, h, false, y1, hx0, hx1, by, tol);
        if (top is null || bottom is null) return null;

        SKPoint[] c = [Meet(top.Value, left.Value), Meet(top.Value, right.Value), Meet(bottom.Value, right.Value), Meet(bottom.Value, left.Value)];
        double quality = new[] { left.Value.Fit, right.Value.Fit, top.Value.Fit, bottom.Value.Fit }.Min();

        // The frame's shape: aspect within 5 % and each corner near where it should be.
        double width = (Dist(c[0], c[1]) + Dist(c[3], c[2])) / 2, height = (Dist(c[0], c[3]) + Dist(c[1], c[2])) / 2;
        double expected = (frame.Width * sx) / (frame.Height * sy);
        if (Math.Abs(width / height / expected - 1) > 0.05) return null;
        SKPoint[] want = [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
        for (int i = 0; i < 4; i++)
            if (Math.Abs(c[i].X - want[i].X) > 2 * bx || Math.Abs(c[i].Y - want[i].Y) > 2 * by) return null;
        return quality < 0.6 ? null : new FrameFit(c, quality);
    }

    readonly record struct Line(double Px, double Py, double Dx, double Dy, double Fit);

    static double Dist(SKPoint a, SKPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// One side of the frame. <paramref name="vertical"/>: the line x ≈ <paramref name="expect"/>, sampled on rows
    /// <paramref name="lo"/>…<paramref name="hi"/>; otherwise y ≈ expect sampled on columns.
    /// </summary>
    static Line? Side(float[] lum, int w, int h, bool vertical, float expect, float lo, float hi, float band, float tol)
    {
        var pts = new List<(double X, double Y)>();
        int a = Math.Max(1, (int)(expect - band)), b = Math.Min((vertical ? w : h) - 1, (int)(expect + band));
        if (b - a < 8) return null;
        var profile = new float[b - a];
        var sorted = new float[b - a];
        for (int s = 0; s < 160; s++)
        {
            int t = (int)(lo + (hi - lo) * s / 159.0);
            if (t < 1 || t >= (vertical ? h : w) - 1) continue;
            for (int k = a; k < b; k++)
            {
                float sum = 0;
                for (int d = -1; d <= 1; d++) sum += vertical ? lum[(t + d) * w + k] : lum[k * w + t + d];
                profile[k - a] = sum / 3;
            }
            int min = 0;
            for (int k = 1; k < profile.Length; k++) if (profile[k] < profile[min]) min = k;
            profile.CopyTo(sorted, 0);
            Array.Sort(sorted);
            if (profile[min] < sorted[sorted.Length / 2] - 40)
                pts.Add(vertical ? (a + min, t) : (t, a + min));
        }
        if (pts.Count < 20) return null;

        // RANSAC with a fixed seed, so a scan always gives the same frame.
        var rng = new Random(1);
        bool[]? best = null;
        int bestCount = 0;
        for (int it = 0; it < 400; it++)
        {
            int i = rng.Next(pts.Count), j = rng.Next(pts.Count);
            if (i == j) continue;
            double dx = pts[j].X - pts[i].X, dy = pts[j].Y - pts[i].Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) continue;
            double nx = -dy / len, ny = dx / len;
            var inl = new bool[pts.Count];
            int count = 0;
            for (int k = 0; k < pts.Count; k++)
                if (Math.Abs((pts[k].X - pts[i].X) * nx + (pts[k].Y - pts[i].Y) * ny) < tol) { inl[k] = true; count++; }
            if (count > bestCount) { bestCount = count; best = inl; }
        }
        if (best is null) return null;

        // Least squares through the inliers: their mean and principal direction.
        double mx = 0, my = 0;
        for (int k = 0; k < pts.Count; k++) if (best[k]) { mx += pts[k].X; my += pts[k].Y; }
        mx /= bestCount; my /= bestCount;
        double sxx = 0, sxy = 0, syy = 0;
        for (int k = 0; k < pts.Count; k++)
        {
            if (!best[k]) continue;
            double ex = pts[k].X - mx, ey = pts[k].Y - my;
            sxx += ex * ex; sxy += ex * ey; syy += ey * ey;
        }
        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return new Line(mx, my, Math.Cos(angle), Math.Sin(angle), (double)bestCount / pts.Count);
    }

    static Line Through(SKPoint a, SKPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        return new Line(a.X, a.Y, dx / len, dy / len, 0.8);
    }

    /// <summary>
    /// Where a near-vertical frame side starts and stops: followed up and down from its middle while it stays dark
    /// (halfway between its own darkness and the paper beside it), allowing gaps of a few pixels.
    /// </summary>
    static (SKPoint Top, SKPoint Bottom)? LineEnds(float[] lum, int w, int h, Line side)
    {
        if (Math.Abs(side.Dy) < 0.5) return null;
        double XAt(double y) => side.Px + (y - side.Py) * side.Dx / side.Dy;
        float Dark(int y)
        {
            int xc = (int)Math.Round(XAt(y));
            float m = float.MaxValue;
            for (int x = Math.Max(0, xc - 2); x <= Math.Min(w - 1, xc + 2); x++) m = Math.Min(m, lum[y * w + x]);
            return m;
        }
        float Paper(int y)
        {
            int xc = (int)Math.Round(XAt(y));
            return Math.Max(lum[y * w + Math.Clamp(xc - 12, 0, w - 1)], lum[y * w + Math.Clamp(xc + 12, 0, w - 1)]);
        }
        int mid = (int)Math.Clamp(side.Py, 1, h - 2);
        var line = new List<float>();
        var paper = new List<float>();
        for (int y = mid - h / 8; y <= mid + h / 8; y += 4)
            if (y > 0 && y < h) { line.Add(Dark(y)); paper.Add(Paper(y)); }
        line.Sort(); paper.Sort();
        float threshold = (line[line.Count / 2] + paper[paper.Count / 2]) / 2;

        int Walk(int step)
        {
            int last = mid, gap = 0;
            for (int y = mid; y > 0 && y < h - 1; y += step)
            {
                if (Dark(y) < threshold) { last = y; gap = 0; }
                else if (++gap > 6) break;
            }
            return last;
        }
        int top = Walk(-1), bottom = Walk(1);
        if (bottom - top < h / 3) return null;
        return (new SKPoint((float)XAt(top), top), new SKPoint((float)XAt(bottom), bottom));
    }

    static SKPoint Meet(Line p, Line q)
    {
        double det = p.Dx * -q.Dy - -q.Dx * p.Dy;
        double ex = q.Px - p.Px, ey = q.Py - p.Py;
        double s = (ex * -q.Dy - -q.Dx * ey) / det;
        return new SKPoint((float)(p.Px + s * p.Dx), (float)(p.Py + s * p.Dy));
    }

    // ───────────────────────────── straightening ─────────────────────────────

    /// <summary>The frame cut out of the scan and straightened (a perspective warp) to <paramref name="size"/>.</summary>
    public static SKBitmap Rectify(SKBitmap scan, SKPoint[] corners, SKSizeI size)
    {
        var h = Homography(corners, [new(0, 0), new(size.Width, 0), new(size.Width, size.Height), new(0, size.Height)]);
        var m = new SKMatrix
        {
            ScaleX = (float)h[0], SkewX = (float)h[1], TransX = (float)h[2],
            SkewY = (float)h[3], ScaleY = (float)h[4], TransY = (float)h[5],
            Persp0 = (float)h[6], Persp1 = (float)h[7], Persp2 = (float)h[8],
        };
        var bmp = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(m);
        using var image = SKImage.FromBitmap(scan);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return bmp;
    }

    /// <summary>The projective map taking the four <paramref name="src"/> points to <paramref name="dst"/>, row-major 3 × 3.</summary>
    public static double[] Homography(SKPoint[] src, SKPoint[] dst)
    {
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = src[i].X, y = src[i].Y, u = dst[i].X, v = dst[i].Y;
            double[] r1 = [x, y, 1, 0, 0, 0, -u * x, -u * y, u], r2 = [0, 0, 0, x, y, 1, -v * x, -v * y, v];
            for (int k = 0; k < 9; k++) { a[2 * i, k] = r1[k]; a[2 * i + 1, k] = r2[k]; }
        }
        // Gauss–Jordan with partial pivoting on the 8 × 8 system (last column is the right-hand side).
        for (int col = 0; col < 8; col++)
        {
            int piv = col;
            for (int r = col + 1; r < 8; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
            for (int k = 0; k < 9; k++) (a[col, k], a[piv, k]) = (a[piv, k], a[col, k]);
            for (int r = 0; r < 8; r++)
            {
                if (r == col) continue;
                double f = a[r, col] / a[col, col];
                for (int k = col; k < 9; k++) a[r, k] -= f * a[col, k];
            }
        }
        var hm = new double[9];
        for (int i = 0; i < 8; i++) hm[i] = a[i, 8] / a[i, i];
        hm[8] = 1;
        return hm;
    }

    // ───────────────────────────── pencil ─────────────────────────────

    /// <summary>
    /// Luminance divided by the local paper brightness (a wide max filter, blurred), 0–255: evens out the
    /// scan's lighting and the sheet's tint so the scan and a fresh render of the sheet can be compared.
    /// </summary>
    public static byte[] PaperNormalised(SKBitmap rgba)
    {
        int w = rgba.Width, h = rgba.Height;
        var lum = Luma(rgba);
        var paper = MaxFilter(lum, w, h, 10);
        for (int pass = 0; pass < 3; pass++) paper = BoxBlur(paper, w, h, 30);
        var result = new byte[lum.Length];
        for (int i = 0; i < lum.Length; i++) result[i] = (byte)Math.Clamp(lum[i] / Math.Max(paper[i], 1) * 255, 0, 255);
        return result;
    }

    /// <summary>
    /// How strongly each pixel of the straightened scan is pencil, 0–255. Pencil is grey (low chroma) and darker
    /// than anything printed within 3 px on <paramref name="referenceNormalised"/> (the same sheet rendered and
    /// <see cref="PaperNormalised"/>), so printed lines, labels and the route drop out even where the frame fit
    /// is a pixel or two off. Specks smaller than <paramref name="minSpeck"/> pixels are dropped.
    /// </summary>
    public static byte[] PencilAlpha(SKBitmap scanRgba, byte[] scanNormalised, byte[] referenceNormalised, int minSpeck = 25)
    {
        int w = scanRgba.Width, h = scanRgba.Height;
        var refF = new float[referenceNormalised.Length];
        for (int i = 0; i < refF.Length; i++) refF[i] = referenceNormalised[i];
        var darkestPrinted = MinFilter(refF, w, h, 3);
        var px = scanRgba.GetPixelSpan();

        var alpha = new byte[w * h];
        for (int i = 0; i < alpha.Length; i++)
        {
            byte r = px[4 * i], g = px[4 * i + 1], b = px[4 * i + 2];
            int chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            double excess = darkestPrinted[i] - scanNormalised[i];
            // Graphite is neutral; the sea and painted water scan as a faded blue that is otherwise nearly grey.
            int blue = b - r;
            double s = Math.Clamp((excess - 16) / 24, 0, 1) * Math.Clamp((48 - chroma) / 20.0, 0, 1) * Math.Clamp((14 - blue) / 8.0, 0, 1);
            int v = (int)(s * 255);
            alpha[i] = v < 60 ? (byte)0 : (byte)v;
        }
        RemoveSpecks(alpha, w, h, minSpeck);
        return alpha;
    }

    /// <summary>Zeroes 8-connected groups of non-zero pixels smaller than <paramref name="minPixels"/>.</summary>
    static void RemoveSpecks(byte[] a, int w, int h, int minPixels)
    {
        var seen = new bool[a.Length];
        var stack = new Stack<int>();
        var group = new List<int>();
        for (int start = 0; start < a.Length; start++)
        {
            if (a[start] == 0 || seen[start]) continue;
            group.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                group.Add(i);
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (a[n] != 0 && !seen[n]) { seen[n] = true; stack.Push(n); }
                }
            }
            if (group.Count < minPixels) foreach (int i in group) a[i] = 0;
        }
    }

    // ───────────────────────────── matching ─────────────────────────────

    /// <summary>A small zero-mean, unit-length copy of a normalised image (box average over <paramref name="f"/> px).</summary>
    public static float[] Thumbnail(byte[] normalised, int w, int h, int f = 6)
    {
        int tw = w / f, th = h / f;
        var t = new float[tw * th];
        for (int y = 0; y < th * f; y++)
        for (int x = 0; x < tw * f; x++)
            t[(y / f) * tw + x / f] += normalised[y * w + x];
        double mean = t.Average(), norm = 0;
        for (int i = 0; i < t.Length; i++) { t[i] = (float)(t[i] - mean); norm += t[i] * t[i]; }
        norm = Math.Sqrt(norm);
        if (norm > 0) for (int i = 0; i < t.Length; i++) t[i] = (float)(t[i] / norm);
        return t;
    }

    /// <summary>Normalised cross-correlation of two thumbnails (−1…1); <paramref name="flip"/> turns the first 180°.</summary>
    public static double Correlation(float[] a, float[] b, bool flip = false)
    {
        double sum = 0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) sum += (flip ? a[a.Length - 1 - i] : a[i]) * b[i];
        return sum;
    }

    // ───────────────────────────── pixels ─────────────────────────────

    public static SKBitmap ToRgba(SKBitmap b) =>
        b.ColorType == SKColorType.Rgba8888 ? b.Copy() : b.Copy(SKColorType.Rgba8888) ?? throw new InvalidOperationException("Could not read the scan's pixels.");

    static float[] Luma(SKBitmap rgba)
    {
        var px = rgba.GetPixelSpan();
        var lum = new float[rgba.Width * rgba.Height];
        for (int i = 0; i < lum.Length; i++) lum[i] = px[4 * i] * 0.299f + px[4 * i + 1] * 0.587f + px[4 * i + 2] * 0.114f;
        return lum;
    }

    static float[] MaxFilter(float[] v, int w, int h, int r) => Separable(v, w, h, r, max: true);
    static float[] MinFilter(float[] v, int w, int h, int r) => Separable(v, w, h, r, max: false);

    /// <summary>Square max / min filter of radius <paramref name="r"/>, as two 1-D passes with a monotonic queue.</summary>
    static float[] Separable(float[] v, int w, int h, int r, bool max)
    {
        var tmp = new float[v.Length];
        var outp = new float[v.Length];
        var line = new float[Math.Max(w, h)];
        var res = new float[Math.Max(w, h)];
        var q = new int[Math.Max(w, h)];
        for (int y = 0; y < h; y++)
        {
            Array.Copy(v, y * w, line, 0, w);
            Run(line, res, w, r, max, q);
            Array.Copy(res, 0, tmp, y * w, w);
        }
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) line[y] = tmp[y * w + x];
            Run(line, res, h, r, max, q);
            for (int y = 0; y < h; y++) outp[y * w + x] = res[y];
        }
        return outp;
    }

    static void Run(float[] line, float[] res, int n, int r, bool max, int[] q)
    {
        int head = 0, tail = 0, next = 0;
        for (int i = 0; i < n; i++)
        {
            int hi = Math.Min(n - 1, i + r);
            for (; next <= hi; next++)
            {
                while (tail > head && (max ? line[q[tail - 1]] <= line[next] : line[q[tail - 1]] >= line[next])) tail--;
                q[tail++] = next;
            }
            while (q[head] < i - r) head++;
            res[i] = line[q[head]];
        }
    }

    /// <summary>Box blur of radius <paramref name="r"/> (two 1-D running-sum passes, edges clamped).</summary>
    static float[] BoxBlur(float[] v, int w, int h, int r)
    {
        var tmp = new float[v.Length];
        var outp = new float[v.Length];
        for (int y = 0; y < h; y++) Blur1(v, tmp, y * w, 1, w, r);
        for (int x = 0; x < w; x++) Blur1(tmp, outp, x, w, h, r);
        return outp;
    }

    static void Blur1(float[] src, float[] dst, int start, int stride, int n, int r)
    {
        double sum = 0;
        for (int k = -r; k <= r; k++) sum += src[start + Math.Clamp(k, 0, n - 1) * stride];
        for (int i = 0; i < n; i++)
        {
            dst[start + i * stride] = (float)(sum / (2 * r + 1));
            sum += src[start + Math.Min(n - 1, i + r + 1) * stride] - src[start + Math.Max(0, i - r) * stride];
        }
    }
}
