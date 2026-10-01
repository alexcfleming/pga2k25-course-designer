namespace Pga2K25.Finisher.Support;

/// <summary>Plane fitting and 2-D principal axes (the <c>lstsq</c> / <c>pinv</c> / <c>eigh</c> / <c>svd</c> uses).</summary>
public static class LinAlg
{
    /// <summary>
    /// Weighted least-squares plane <c>v ≈ a·x + b·z + c</c> (NumPy <c>lstsq</c> on rows √w·(x, z, 1)).
    /// Solved in coordinates centred on the first point for conditioning; near-singular directions are
    /// dropped like <c>lstsq</c>'s rcond cut-off. Returns false when nothing could be fitted.
    /// </summary>
    public static bool FitPlane(ReadOnlySpan<double> xs, ReadOnlySpan<double> zs, ReadOnlySpan<double> vs,
        ReadOnlySpan<double> weights, out double a, out double b, out double c)
    {
        a = b = c = double.NaN;
        int n = xs.Length;
        if (n == 0) return false;
        double x0 = xs[0], z0 = zs[0];
        // Normal equations M·p = r for p = (a, b, c') with c' the value at (x0, z0).
        Span<double> m = stackalloc double[9];
        Span<double> r = stackalloc double[3];
        for (int i = 0; i < n; i++)
        {
            double w = weights.IsEmpty ? 1 : weights[i];
            double dx = xs[i] - x0, dz = zs[i] - z0, v = vs[i];
            m[0] += w * dx * dx; m[1] += w * dx * dz; m[2] += w * dx;
            m[4] += w * dz * dz; m[5] += w * dz;
            m[8] += w;
            r[0] += w * dx * v; r[1] += w * dz * v; r[2] += w * v;
        }
        m[3] = m[1]; m[6] = m[2]; m[7] = m[5];
        if (!SolveSymmetric3(m, r, Math.Max(n, 3), out var p)) return false;
        a = p[0];
        b = p[1];
        c = p[2] - a * x0 - b * z0;
        return double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c);
    }

    /// <summary>Minimum-norm solution of a symmetric positive semi-definite 3×3 system via Jacobi eigen-decomposition.</summary>
    static bool SolveSymmetric3(ReadOnlySpan<double> matrix, ReadOnlySpan<double> rhs, int rows, out double[] solution)
    {
        var a = matrix.ToArray();
        var v = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = a[1] * a[1] + a[2] * a[2] + a[5] * a[5];
            if (off < 1e-300) break;
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    double apq = a[p * 3 + q];
                    if (Math.Abs(apq) < 1e-300) continue;
                    double app = a[p * 3 + p], aqq = a[q * 3 + q];
                    double theta = (aqq - app) / (2 * apq);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k * 3 + p], akq = a[k * 3 + q];
                        a[k * 3 + p] = cs * akp - sn * akq;
                        a[k * 3 + q] = sn * akp + cs * akq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p * 3 + k], aqk = a[q * 3 + k];
                        a[p * 3 + k] = cs * apk - sn * aqk;
                        a[q * 3 + k] = sn * apk + cs * aqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k * 3 + p], vkq = v[k * 3 + q];
                        v[k * 3 + p] = cs * vkp - sn * vkq;
                        v[k * 3 + q] = sn * vkp + cs * vkq;
                    }
                }
        }
        double maxEig = Math.Max(a[0], Math.Max(a[4], a[8]));
        solution = new double[3];
        if (!(maxEig > 0)) return false;
        // lstsq keeps singular values s > eps·max(M, N)·s_max; eigenvalues here are s².
        double sMax = Math.Sqrt(maxEig), cutoff = 2.220446049250313e-16 * Math.Max(rows, 3) * sMax;
        for (int e = 0; e < 3; e++)
        {
            double lambda = a[e * 4];
            if (!(lambda > 0) || Math.Sqrt(lambda) <= cutoff) continue;
            double proj = v[e] * rhs[0] + v[3 + e] * rhs[1] + v[6 + e] * rhs[2];
            for (int k = 0; k < 3; k++) solution[k] += v[k * 3 + e] * proj / lambda;
        }
        return true;
    }

    /// <summary>
    /// Principal axes of a 2-D point set: unit major and minor directions (eigenvectors of the
    /// covariance, largest eigenvalue first). Falls back to the X/Z axes for degenerate sets.
    /// </summary>
    public static ((double X, double Z) Major, (double X, double Z) Minor, (double X, double Z) Mean) PrincipalAxes(IReadOnlyList<(double X, double Z)> pts)
    {
        double mx = 0, mz = 0;
        foreach (var (x, z) in pts) { mx += x; mz += z; }
        mx /= pts.Count; mz /= pts.Count;
        double sxx = 0, sxz = 0, szz = 0;
        foreach (var (x, z) in pts)
        {
            double dx = x - mx, dz = z - mz;
            sxx += dx * dx; sxz += dx * dz; szz += dz * dz;
        }
        double tr = sxx + szz, det = sxx * szz - sxz * sxz;
        double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc;
        (double X, double Z) major;
        if (Math.Abs(sxz) > 1e-12 * Math.Max(1, tr)) major = Unit(l1 - szz, sxz);
        else major = sxx >= szz ? (1, 0) : (0, 1);
        return (major, (-major.Z, major.X), (mx, mz));
    }

    static (double X, double Z) Unit(double x, double z)
    {
        double n = Math.Sqrt(x * x + z * z);
        return n > 0 ? (x / n, z / n) : (1, 0);
    }
}
