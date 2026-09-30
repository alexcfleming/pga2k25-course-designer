
namespace Pga2K25.CourseFormat;

public static class Geometry
{
    /// <summary>
    /// Samples a 2K25 spline: each segment a→b is the cubic Bézier
    /// (a.Anchor, a.Out, b.In, b.Anchor); closed splines wrap last→first.
    /// </summary>
    public static List<Vec2> SampleSpline(SurfaceSpline spline, int samplesPerSegment = 16)
    {
        var pts = spline.Points;
        var result = new List<Vec2>();
        if (pts.Count < 2) return result;
        int segments = spline.Closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            for (int j = i == 0 ? 0 : 1; j <= samplesPerSegment; j++)
            {
                double t = j / (double)samplesPerSegment, u = 1 - t;
                result.Add(a.Anchor * (u * u * u) + a.Out * (3 * u * u * t) + b.In * (3 * u * t * t) + b.Anchor * (t * t * t));
            }
        }
        if (spline.Closed && result.Count > 1 && Vec2.Distance(result[0], result[^1]) < 1e-9)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    public static bool Contains(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < a.X + (p.Z - a.Z) * (b.X - a.X) / (b.Z - a.Z))
                inside = !inside;
        }
        return inside;
    }

    public static double Area(IReadOnlyList<Vec2> poly)
    {
        double s = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            s += poly[j].X * poly[i].Z - poly[i].X * poly[j].Z;
        return Math.Abs(s) / 2;
    }

    public static double DistanceToEdge(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        double best = double.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            best = Math.Min(best, SegmentDistance(p, poly[j], poly[i]));
        return best;
    }

    static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Z * ab.Z;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / len2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }

    /// <summary>Resamples a closed polygon to <paramref name="count"/> points at uniform arc length.</summary>
    public static List<Vec2> ResampleClosed(IReadOnlyList<Vec2> poly, int count)
    {
        var cum = new double[poly.Count + 1];
        for (int i = 0; i < poly.Count; i++)
            cum[i + 1] = cum[i] + Vec2.Distance(poly[i], poly[(i + 1) % poly.Count]);
        double total = cum[^1];
        var result = new List<Vec2>(count);
        int seg = 0;
        for (int k = 0; k < count; k++)
        {
            double d = total * k / count;
            while (seg < poly.Count - 1 && cum[seg + 1] < d) seg++;
            double segLen = cum[seg + 1] - cum[seg];
            double t = segLen > 1e-12 ? (d - cum[seg]) / segLen : 0;
            var a = poly[seg];
            var b = poly[(seg + 1) % poly.Count];
            result.Add(a + (b - a) * t);
        }
        return result;
    }

    /// <summary>
    /// Fourier low-pass of a closed outline, keeping harmonics 0..<paramref name="harmonics"/>.
    /// Lobes and waists survive; high-frequency ripple goes (same idea as the
    /// screenshot study's shape_harmonics).
    /// </summary>
    public static List<Vec2> FourierSmooth(IReadOnlyList<Vec2> poly, int harmonics, int samples = 400)
    {
        var p = ResampleClosed(poly, samples);
        int n = p.Count;
        var result = new List<Vec2>(n);
        var coef = new (double ReX, double ImX, double ReZ, double ImZ)[harmonics + 1];
        for (int k = 0; k <= harmonics; k++)
        {
            double rx = 0, ix = 0, rz = 0, iz = 0;
            for (int t = 0; t < n; t++)
            {
                double ang = -2 * Math.PI * k * t / n;
                double c = Math.Cos(ang), s = Math.Sin(ang);
                rx += p[t].X * c; ix += p[t].X * s;
                rz += p[t].Z * c; iz += p[t].Z * s;
            }
            coef[k] = (rx / n, ix / n, rz / n, iz / n);
        }
        for (int t = 0; t < n; t++)
        {
            double x = coef[0].ReX, z = coef[0].ReZ;
            for (int k = 1; k <= harmonics; k++)
            {
                double ang = 2 * Math.PI * k * t / n;
                double c = Math.Cos(ang), s = Math.Sin(ang);
                // Real signal: X(t) = a0 + 2*Σ Re(Ck e^{iωt})
                x += 2 * (coef[k].ReX * c - coef[k].ImX * s);
                z += 2 * (coef[k].ReZ * c - coef[k].ImZ * s);
            }
            result.Add(new Vec2(x, z));
        }
        return result;
    }

    public static List<Vec2> Ellipse(Vec2 centre, Vec2 majorAxis, double semiMajor, double semiMinor, int count = 120)
    {
        var u = majorAxis.Normalized();
        var v = new Vec2(-u.Z, u.X);
        var result = new List<Vec2>(count);
        for (int i = 0; i < count; i++)
        {
            double a = 2 * Math.PI * i / count;
            result.Add(centre + u * (semiMajor * Math.Cos(a)) + v * (semiMinor * Math.Sin(a)));
        }
        return result;
    }
}
