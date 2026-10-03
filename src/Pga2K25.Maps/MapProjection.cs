using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// World X/Z → page transform: rotate by <see cref="RotationDegrees"/>, scale, and centre in a
/// page rectangle. At 0° world +Z is up the page; Z is flipped to canvas Y exactly once, here.
/// </summary>
public sealed class MapProjection
{
    /// <summary>
    /// Compass north in world space. The game's north is −Z (and east −X): fitted from the Cassimbo
    /// designer's tee/approach bearings and confirmed by the designer. North-up is therefore 180°.
    /// </summary>
    public static readonly Vec2 North = new(0, -1);
    public const double NorthUpRotation = 180;

    readonly double _cos, _sin;
    readonly Vec2 _centre;
    readonly SKPoint _pageCentre;
    public double RotationDegrees { get; }
    public float PointsPerMetre { get; }

    MapProjection(double rotationDegrees, Vec2 centre, float pointsPerMetre, SKPoint pageCentre)
    {
        RotationDegrees = rotationDegrees;
        double r = rotationDegrees * Math.PI / 180;
        _cos = Math.Cos(r); _sin = Math.Sin(r);
        _centre = centre; PointsPerMetre = pointsPerMetre; _pageCentre = pageCentre;
    }

    public SKPoint Map(Vec2 w)
    {
        double dx = w.X - _centre.X, dz = w.Z - _centre.Z;
        double u = dx * _cos - dz * _sin, v = dx * _sin + dz * _cos;   // rotate counter-clockwise
        return new SKPoint(_pageCentre.X + (float)(u * PointsPerMetre), _pageCentre.Y - (float)(v * PointsPerMetre));
    }

    /// <summary>World point under a page point (the inverse of <see cref="Map"/>).</summary>
    public Vec2 Unmap(SKPoint p)
    {
        double u = (p.X - _pageCentre.X) / PointsPerMetre, v = -(p.Y - _pageCentre.Y) / PointsPerMetre;
        return new Vec2(_centre.X + u * _cos + v * _sin, _centre.Z - u * _sin + v * _cos);
    }

    /// <summary>Page direction (unit, canvas coordinates) of a world direction.</summary>
    public SKPoint MapDirection(Vec2 d)
    {
        var a = Map(_centre);
        var b = Map(_centre + d.Normalized());
        var v = new SKPoint(b.X - a.X, b.Y - a.Y);
        return new SKPoint(v.X / v.Length, v.Y / v.Length);
    }

    public SKPath Path(IReadOnlyList<Vec2> pts, bool close)
    {
        using var b = new SKPathBuilder();
        AddPolyline(b, pts, close);
        return b.Detach();
    }

    /// <summary>
    /// Appends a projected polyline, simplified (Douglas–Peucker) to <paramref name="tolerance"/> page units.
    /// Merged shapes are traced on a fine grid, so most of their vertices are closer together than print
    /// can resolve; dropping them keeps vector PDFs small without any visible change.
    /// </summary>
    public void AddPolyline(SKPathBuilder b, IReadOnlyList<Vec2> pts, bool close, float tolerance = 0.1f)
    {
        if (pts.Count == 0) return;
        var m = new SKPoint[pts.Count];
        for (int i = 0; i < m.Length; i++) m[i] = Map(pts[i]);
        var keep = Simplify(m, tolerance);
        b.MoveTo(m[0]);
        for (int i = 1; i < m.Length; i++) if (keep[i]) b.LineTo(m[i]);
        if (close) b.Close();
    }

    static bool[] Simplify(SKPoint[] p, float tol)
    {
        var keep = new bool[p.Length];
        keep[0] = keep[^1] = true;
        if (p.Length < 3) { Array.Fill(keep, true); return keep; }
        var stack = new Stack<(int, int)>();
        stack.Push((0, p.Length - 1));
        float tol2 = tol * tol;
        while (stack.Count > 0)
        {
            var (a, z) = stack.Pop();
            float dx = p[z].X - p[a].X, dy = p[z].Y - p[a].Y, len2 = dx * dx + dy * dy;
            int worst = -1; float worstD = tol2;
            for (int i = a + 1; i < z; i++)
            {
                float ex = p[i].X - p[a].X, ey = p[i].Y - p[a].Y, d2;
                if (len2 < 1e-12f) d2 = ex * ex + ey * ey;
                else { float c = ex * dy - ey * dx; d2 = c * c / len2; }
                if (d2 > worstD) { worstD = d2; worst = i; }
            }
            if (worst < 0) continue;
            keep[worst] = true;
            stack.Push((a, worst));
            stack.Push((worst, z));
        }
        return keep;
    }

    /// <summary>Fits points into <paramref name="rect"/> at a fixed rotation.</summary>
    public static MapProjection Fit(IReadOnlyList<Vec2> points, SKRect rect, double rotationDegrees, double padding = 1.04)
    {
        var (minU, maxU, minV, maxV) = Bounds(points, rotationDegrees);
        double r = rotationDegrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double mu = (minU + maxU) / 2, mv = (minV + maxV) / 2;
        // Inverse-rotate the rotated-space centre back to world.
        var centre = new Vec2(mu * c + mv * s, -mu * s + mv * c);
        float k = (float)Math.Min(rect.Width / ((maxU - minU) * padding), rect.Height / ((maxV - minV) * padding));
        return new MapProjection(rotationDegrees, centre, k, new SKPoint(rect.MidX, rect.MidY));
    }

    /// <summary>
    /// Rotation within ±90° of north-up (so north always points into the upper half of the page)
    /// that lets the course fill <paramref name="rect"/> at the largest scale.
    /// </summary>
    public static double BestRotation(IReadOnlyList<Vec2> points, SKRect rect)
    {
        double best = NorthUpRotation, bestScale = 0;
        for (int off = -90; off < 90; off++)
        {
            double deg = NorthUpRotation + off;
            var (minU, maxU, minV, maxV) = Bounds(points, deg);
            double scale = Math.Min(rect.Width / (maxU - minU), rect.Height / (maxV - minV));
            // Tiny preference for small turns so near-ties stay close to north-up.
            scale *= 1 - Math.Abs(off) * 1e-5;
            if (scale > bestScale) { bestScale = scale; best = deg; }
        }
        return best;
    }

    public static double FitScale(IReadOnlyList<Vec2> points, SKRect rect, double rotationDegrees)
    {
        var (minU, maxU, minV, maxV) = Bounds(points, rotationDegrees);
        return Math.Min(rect.Width / (maxU - minU), rect.Height / (maxV - minV));
    }

    static (double MinU, double MaxU, double MinV, double MaxV) Bounds(IReadOnlyList<Vec2> points, double deg)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var p in points)
        {
            double u = p.X * c - p.Z * s, v = p.X * s + p.Z * c;
            if (u < minU) minU = u; if (u > maxU) maxU = u;
            if (v < minV) minV = v; if (v > maxV) maxV = v;
        }
        return (minU, maxU, minV, maxV);
    }
}
