namespace Pga2K25.Finisher.Support;

/// <summary>
/// Static 2-D k-d tree over X/Z points (stand-in for <c>scipy.spatial.cKDTree</c>).
/// <see cref="Query"/> returns the k nearest points in ascending distance, ties broken by index.
/// </summary>
public sealed class KdTree
{
    readonly double[] _x, _z;
    readonly int[] _order;       // point indices, arranged so each subtree is a contiguous range
    readonly byte[] _axis;       // split axis of the node whose median sits at that slot

    public int Count => _x.Length;

    public KdTree(IReadOnlyList<(double X, double Z)> points)
    {
        int n = points.Count;
        _x = new double[n];
        _z = new double[n];
        for (int i = 0; i < n; i++) (_x[i], _z[i]) = points[i];
        _order = Enumerable.Range(0, n).ToArray();
        _axis = new byte[n];
        Build(0, n);
    }

    public KdTree(double[] x, double[] z)
    {
        _x = x;
        _z = z;
        _order = Enumerable.Range(0, x.Length).ToArray();
        _axis = new byte[x.Length];
        Build(0, x.Length);
    }

    public (double X, double Z) Point(int index) => (_x[index], _z[index]);

    void Build(int lo, int hi)
    {
        while (hi - lo > 1)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            for (int i = lo; i < hi; i++)
            {
                int p = _order[i];
                minX = Math.Min(minX, _x[p]); maxX = Math.Max(maxX, _x[p]);
                minZ = Math.Min(minZ, _z[p]); maxZ = Math.Max(maxZ, _z[p]);
            }
            byte axis = (byte)(maxX - minX >= maxZ - minZ ? 0 : 1);
            int mid = (lo + hi) / 2;
            Select(lo, hi - 1, mid, axis);
            _axis[mid] = axis;
            Build(lo, mid);
            lo = mid + 1;
        }
    }

    double Coord(int p, int axis) => axis == 0 ? _x[p] : _z[p];

    /// <summary>Quickselect so that _order[k] holds the median along <paramref name="axis"/>.</summary>
    void Select(int lo, int hi, int k, int axis)
    {
        while (hi > lo)
        {
            double pivot = Coord(_order[(lo + hi) / 2], axis);
            int i = lo, j = hi;
            while (i <= j)
            {
                while (Coord(_order[i], axis) < pivot) i++;
                while (Coord(_order[j], axis) > pivot) j--;
                if (i <= j) { (_order[i], _order[j]) = (_order[j], _order[i]); i++; j--; }
            }
            if (k <= j) hi = j;
            else if (k >= i) lo = i;
            else return;
        }
    }

    /// <summary>
    /// k nearest neighbours of (x, z). Writes up to k results into the spans and returns how many.
    /// </summary>
    public int Query(double x, double z, int k, Span<double> distances, Span<int> indices)
    {
        k = Math.Min(k, Count);
        if (k <= 0) return 0;
        int found = 0;
        Span<double> d2 = stackalloc double[k];
        Search(0, Count, x, z, k, d2, indices, ref found);
        for (int i = 0; i < found; i++) distances[i] = Math.Sqrt(d2[i]);
        return found;
    }

    /// <summary>Nearest point: (distance, index); (∞, -1) for an empty tree.</summary>
    public (double Distance, int Index) Nearest(double x, double z)
    {
        Span<double> d = stackalloc double[1];
        Span<int> i = stackalloc int[1];
        return Query(x, z, 1, d, i) == 1 ? (d[0], i[0]) : (double.PositiveInfinity, -1);
    }

    void Search(int lo, int hi, double x, double z, int k, Span<double> d2, Span<int> idx, ref int found)
    {
        while (hi > lo)
        {
            int mid = (lo + hi) / 2;
            int p = _order[mid];
            double dx = _x[p] - x, dz = _z[p] - z;
            Insert(dx * dx + dz * dz, p, k, d2, idx, ref found);
            if (hi - lo == 1) return;
            int axis = _axis[mid];
            double diff = axis == 0 ? x - _x[p] : z - _z[p];
            // Visit the near side first, then the far side only if it can still hold a closer point.
            if (diff < 0)
            {
                Search(lo, mid, x, z, k, d2, idx, ref found);
                if (found == k && diff * diff > d2[found - 1]) return;
                lo = mid + 1;
            }
            else
            {
                Search(mid + 1, hi, x, z, k, d2, idx, ref found);
                if (found == k && diff * diff > d2[found - 1]) return;
                hi = mid;
            }
        }
    }

    static void Insert(double dist2, int index, int k, Span<double> d2, Span<int> idx, ref int found)
    {
        if (found == k && (dist2 > d2[k - 1] || (dist2 == d2[k - 1] && index > idx[k - 1]))) return;
        int pos = found < k ? found++ : k - 1;
        while (pos > 0 && (d2[pos - 1] > dist2 || (d2[pos - 1] == dist2 && idx[pos - 1] > index)))
        {
            d2[pos] = d2[pos - 1];
            idx[pos] = idx[pos - 1];
            pos--;
        }
        d2[pos] = dist2;
        idx[pos] = index;
    }
}
