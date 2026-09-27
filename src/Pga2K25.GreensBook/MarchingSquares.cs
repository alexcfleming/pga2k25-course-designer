using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>Iso-line extraction on a heightfield raster, chained into polylines.</summary>
public static class MarchingSquares
{
    /// <param name="values">Per-cell values sampled at cell centres (row-major, field dimensions).</param>
    /// <param name="mask">Cells that may contribute; a square is traced only if all four corners are masked in.</param>
    public static List<List<Vec2>> Trace(Heightfield field, double[] values, bool[] mask, double level)
    {
        int w = field.Width, h = field.Height;
        // Segment endpoints are identified by the grid edge they lie on so segments can be chained.
        var segments = new List<(long A, long B, Vec2 PA, Vec2 PB)>();

        long HEdge(int ix, int iz) => ((long)iz * w + ix) * 2;      // (ix,iz)-(ix+1,iz)
        long VEdge(int ix, int iz) => ((long)iz * w + ix) * 2 + 1;  // (ix,iz)-(ix,iz+1)

        Vec2 Lerp(int ax, int az, int bx, int bz)
        {
            double va = values[az * w + ax], vb = values[bz * w + bx];
            double t = Math.Abs(vb - va) < 1e-12 ? 0.5 : (level - va) / (vb - va);
            var pa = field.CellCentre(ax, az);
            var pb = field.CellCentre(bx, bz);
            return pa + (pb - pa) * Math.Clamp(t, 0, 1);
        }

        for (int iz = 0; iz < h - 1; iz++)
            for (int ix = 0; ix < w - 1; ix++)
            {
                int k00 = iz * w + ix, k10 = k00 + 1, k01 = k00 + w, k11 = k01 + 1;
                if (!(mask[k00] && mask[k10] && mask[k01] && mask[k11])) continue;
                int c = (values[k00] >= level ? 1 : 0) | (values[k10] >= level ? 2 : 0)
                      | (values[k11] >= level ? 4 : 0) | (values[k01] >= level ? 8 : 0);
                if (c == 0 || c == 15) continue;

                // Edges: bottom (00-10), right (10-11), top (01-11), left (00-01).
                var bottom = (HEdge(ix, iz), (Func<Vec2>)(() => Lerp(ix, iz, ix + 1, iz)));
                var right = (VEdge(ix + 1, iz), (Func<Vec2>)(() => Lerp(ix + 1, iz, ix + 1, iz + 1)));
                var top = (HEdge(ix, iz + 1), (Func<Vec2>)(() => Lerp(ix, iz + 1, ix + 1, iz + 1)));
                var left = (VEdge(ix, iz), (Func<Vec2>)(() => Lerp(ix, iz, ix, iz + 1)));

                void Add((long, Func<Vec2>) a, (long, Func<Vec2>) b) => segments.Add((a.Item1, b.Item1, a.Item2(), b.Item2()));

                double centre = (values[k00] + values[k10] + values[k01] + values[k11]) / 4;
                switch (c)
                {
                    case 1: case 14: Add(left, bottom); break;
                    case 2: case 13: Add(bottom, right); break;
                    case 3: case 12: Add(left, right); break;
                    case 4: case 11: Add(right, top); break;
                    case 6: case 9: Add(bottom, top); break;
                    case 7: case 8: Add(left, top); break;
                    case 5:
                        if (centre >= level) { Add(left, top); Add(bottom, right); }
                        else { Add(left, bottom); Add(right, top); }
                        break;
                    case 10:
                        if (centre >= level) { Add(left, bottom); Add(right, top); }
                        else { Add(left, top); Add(bottom, right); }
                        break;
                }
            }

        return Chain(segments);
    }

    static List<List<Vec2>> Chain(List<(long A, long B, Vec2 PA, Vec2 PB)> segments)
    {
        var byEdge = new Dictionary<long, List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            (byEdge.TryGetValue(segments[i].A, out var la) ? la : byEdge[segments[i].A] = new()).Add(i);
            (byEdge.TryGetValue(segments[i].B, out var lb) ? lb : byEdge[segments[i].B] = new()).Add(i);
        }

        var used = new bool[segments.Count];
        var result = new List<List<Vec2>>();
        for (int start = 0; start < segments.Count; start++)
        {
            if (used[start]) continue;
            used[start] = true;
            var line = new LinkedList<Vec2>();
            line.AddLast(segments[start].PA);
            line.AddLast(segments[start].PB);
            Extend(segments[start].B, forward: true);
            Extend(segments[start].A, forward: false);
            result.Add(line.ToList());

            void Extend(long edge, bool forward)
            {
                while (byEdge.TryGetValue(edge, out var candidates))
                {
                    int next = candidates.FirstOrDefault(i => !used[i], -1);
                    if (next < 0) return;
                    used[next] = true;
                    var s = segments[next];
                    (long nextEdge, Vec2 point) = s.A == edge ? (s.B, s.PB) : (s.A, s.PA);
                    if (forward) line.AddLast(point); else line.AddFirst(point);
                    edge = nextEdge;
                }
            }
        }
        return result;
    }
}
