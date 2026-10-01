namespace Pga2K25.Finisher.Support;

/// <summary>The handful of NumPy / SciPy routines the reference tool uses, with the same edge behaviour.</summary>
public static class NumPy
{
    /// <summary><c>np.arange(start, stop, step)</c>: ceil((stop - start) / step) values start + i·step.</summary>
    public static double[] Arange(double start, double stop, double step)
    {
        double span = Math.Ceiling((stop - start) / step);
        int n = span > 0 && double.IsFinite(span) ? (int)span : 0;
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = start + i * step;
        return result;
    }

    /// <summary><c>np.linspace(start, stop, count)</c>.</summary>
    public static double[] Linspace(double start, double stop, int count)
    {
        var result = new double[count];
        if (count == 1) { result[0] = start; return result; }
        double step = (stop - start) / (count - 1);
        for (int i = 0; i < count; i++) result[i] = start + i * step;
        if (count > 1) result[^1] = stop;
        return result;
    }

    /// <summary><c>np.percentile(values, q)</c> with the default linear interpolation.</summary>
    public static double Percentile(IEnumerable<double> values, double q)
    {
        var sorted = values.ToArray();
        if (sorted.Length == 0) return double.NaN;
        Array.Sort(sorted);
        return PercentileSorted(sorted, q);
    }

    public static double PercentileSorted(double[] sorted, double q)
    {
        double pos = q / 100.0 * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        double t = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
    }

    /// <summary><c>np.median</c>.</summary>
    public static double Median(IEnumerable<double> values) => Percentile(values, 50);

    /// <summary><c>np.interp(x, xp, fp)</c> for increasing <paramref name="xp"/> (clamped outside).</summary>
    public static double Interp(double x, IReadOnlyList<double> xp, IReadOnlyList<double> fp)
    {
        int n = xp.Count;
        if (x <= xp[0]) return fp[0];
        if (x >= xp[n - 1]) return fp[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (xp[mid] <= x) lo = mid; else hi = mid;
        }
        double span = xp[hi] - xp[lo];
        return span == 0 ? fp[hi] : fp[lo] + (fp[hi] - fp[lo]) * (x - xp[lo]) / span;
    }

    /// <summary><c>np.searchsorted(a, v, side='right')</c>.</summary>
    public static int SearchSortedRight(IReadOnlyList<double> a, double v)
    {
        int lo = 0, hi = a.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (a[mid] <= v) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Normalised Gaussian kernel with SciPy's default <c>truncate=4.0</c>.</summary>
    static double[] GaussianKernel(double sigma)
    {
        int radius = (int)(4.0 * sigma + 0.5);
        var k = new double[2 * radius + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++) sum += k[i + radius] = Math.Exp(-0.5 * i * i / (sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        return k;
    }

    /// <summary><c>scipy.ndimage.gaussian_filter1d(data, sigma, mode='nearest')</c>.</summary>
    public static double[] GaussianFilter1D(IReadOnlyList<double> data, double sigma)
    {
        var k = GaussianKernel(sigma);
        int r = k.Length / 2, n = data.Count;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = 0;
            for (int j = -r; j <= r; j++) acc += data[Math.Clamp(i + j, 0, n - 1)] * k[j + r];
            result[i] = acc;
        }
        return result;
    }

    /// <summary><c>scipy.ndimage.gaussian_filter(grid, sigma, mode='nearest')</c> on a row-major [rows, cols] grid.</summary>
    public static double[,] GaussianFilter2D(double[,] grid, double sigma)
    {
        var k = GaussianKernel(sigma);
        int r = k.Length / 2, rows = grid.GetLength(0), cols = grid.GetLength(1);
        var tmp = new double[rows, cols];
        var result = new double[rows, cols];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                double acc = 0;
                for (int j = -r; j <= r; j++) acc += grid[Math.Clamp(y + j, 0, rows - 1), x] * k[j + r];
                tmp[y, x] = acc;
            }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                double acc = 0;
                for (int j = -r; j <= r; j++) acc += tmp[y, Math.Clamp(x + j, 0, cols - 1)] * k[j + r];
                result[y, x] = acc;
            }
        return result;
    }

    /// <summary><c>np.gradient(grid, h, h)</c>: (d/drow, d/dcol), central inside, one-sided at the edges.</summary>
    public static (double[,] DRow, double[,] DCol) Gradient(double[,] grid, double h)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var dr = new double[rows, cols];
        var dc = new double[rows, cols];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                dr[y, x] = rows < 2 ? 0 : y == 0 ? (grid[1, x] - grid[0, x]) / h
                    : y == rows - 1 ? (grid[y, x] - grid[y - 1, x]) / h
                    : (grid[y + 1, x] - grid[y - 1, x]) / (2 * h);
                dc[y, x] = cols < 2 ? 0 : x == 0 ? (grid[y, 1] - grid[y, 0]) / h
                    : x == cols - 1 ? (grid[y, x] - grid[y, x - 1]) / h
                    : (grid[y, x + 1] - grid[y, x - 1]) / (2 * h);
            }
        return (dr, dc);
    }

    /// <summary>
    /// <c>values[tuple(distance_transform_edt(~valid, return_indices=True)[1])]</c>: every invalid cell
    /// takes the value of its nearest valid cell (Euclidean, in cell units).
    /// </summary>
    public static double[,] FillNearest(double[,] values, bool[,] valid)
    {
        int rows = values.GetLength(0), cols = values.GetLength(1);
        var pts = new List<(double X, double Z)>();
        var cells = new List<(int R, int C)>();
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                if (valid[y, x]) { pts.Add((y, x)); cells.Add((y, x)); }
        var result = new double[rows, cols];
        if (pts.Count == 0) return result;
        var tree = new KdTree(pts);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                if (valid[y, x]) { result[y, x] = values[y, x]; continue; }
                var (_, i) = tree.Nearest(y, x);
                var (ry, rx) = cells[i];
                result[y, x] = values[ry, rx];
            }
        return result;
    }
}
