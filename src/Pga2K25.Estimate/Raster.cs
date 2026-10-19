using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.Estimate;

/// <summary>Burns course shapes into a boolean grid aligned with a <see cref="Heightfield"/> (cell centres).</summary>
public sealed class CellMask
{
    readonly Heightfield _f;
    public bool[] Cells { get; }

    public CellMask(Heightfield field)
    {
        _f = field;
        Cells = new bool[field.Values.Length];
    }

    public int Count => Cells.Count(c => c);

    public static CellMask Union(Heightfield field, params CellMask[] masks)
    {
        var u = new CellMask(field);
        foreach (var m in masks)
            for (int k = 0; k < u.Cells.Length; k++) u.Cells[k] |= m.Cells[k];
        return u;
    }

    /// <summary>Scanline fill of a closed polygon.</summary>
    public void Polygon(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count < 3) return;
        double minZ = poly.Min(p => p.Z), maxZ = poly.Max(p => p.Z);
        int z0 = Math.Max(0, (int)Math.Floor((minZ - _f.Origin.Z) / _f.Cell - 0.5));
        int z1 = Math.Min(_f.Height - 1, (int)Math.Ceiling((maxZ - _f.Origin.Z) / _f.Cell - 0.5));
        var xs = new List<double>();
        for (int iz = z0; iz <= z1; iz++)
        {
            double zc = _f.Origin.Z + (iz + 0.5) * _f.Cell;
            xs.Clear();
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Z > zc) != (b.Z > zc)) xs.Add(a.X + (zc - a.Z) * (b.X - a.X) / (b.Z - a.Z));
            }
            xs.Sort();
            for (int p = 0; p + 1 < xs.Count; p += 2)
            {
                int x0 = Math.Max(0, (int)Math.Ceiling((xs[p] - _f.Origin.X) / _f.Cell - 0.5));
                int x1 = Math.Min(_f.Width - 1, (int)Math.Floor((xs[p + 1] - _f.Origin.X) / _f.Cell - 0.5));
                for (int ix = x0; ix <= x1; ix++) Cells[iz * _f.Width + ix] = true;
            }
        }
    }

    /// <summary>Cells within half the ribbon's width of its centreline.</summary>
    public void Ribbon(Ribbon r)
    {
        double half = r.Width / 2;
        for (int i = 1; i < r.Points.Count; i++) Capsule(r.Points[i - 1], r.Points[i], half);
        if (r.Points.Count == 1) Capsule(r.Points[0], r.Points[0], half);
    }

    public void Disc(Vec2 centre, double radius) => Capsule(centre, centre, radius);

    void Capsule(Vec2 a, Vec2 b, double radius)
    {
        int x0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - radius - _f.Origin.X) / _f.Cell));
        int x1 = Math.Min(_f.Width - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + radius - _f.Origin.X) / _f.Cell));
        int z0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Z, b.Z) - radius - _f.Origin.Z) / _f.Cell));
        int z1 = Math.Min(_f.Height - 1, (int)Math.Ceiling((Math.Max(a.Z, b.Z) + radius - _f.Origin.Z) / _f.Cell));
        for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
                if (SegmentDistance(_f.CellCentre(ix, iz), a, b) <= radius) Cells[iz * _f.Width + ix] = true;
    }

    public static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Z * ab.Z;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / len2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }
}
