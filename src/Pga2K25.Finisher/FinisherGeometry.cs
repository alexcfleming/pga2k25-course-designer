using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;

namespace Pga2K25.Finisher;

/// <summary>Arc-length parameterised polyline (<c>_prepare_arc</c>).</summary>
public sealed record Arc(Vec2[] Points, double[] Cumulative, double Length)
{
    /// <summary><c>_prepare_arc</c>: drops zero-length steps; null when fewer than two distinct points.</summary>
    public static Arc? From(IReadOnlyList<Vec2> points)
    {
        if (points.Count < 2) return null;
        var kept = new List<Vec2> { points[0] };
        for (int i = 1; i < points.Count; i++)
            if (Vec2.Distance(points[i], points[i - 1]) > 1e-8) kept.Add(points[i]);
        if (kept.Count < 2) return null;
        var cum = new double[kept.Count];
        for (int i = 1; i < kept.Count; i++) cum[i] = cum[i - 1] + Vec2.Distance(kept[i], kept[i - 1]);
        return new Arc(kept.ToArray(), cum, cum[^1]);
    }

    /// <summary><c>_point_tangent</c>: position and unit tangent at an arc distance (clamped).</summary>
    public (double X, double Z, double Tx, double Tz) At(double distance)
    {
        distance = Math.Min(Math.Max(distance, 0.0), Length);
        int i = NumPy.SearchSortedRight(Cumulative, distance) - 1;
        i = Math.Min(Math.Max(i, 0), Points.Length - 2);
        var a = Points[i];
        var b = Points[i + 1];
        double segment = Math.Max(Cumulative[i + 1] - Cumulative[i], 1e-9);
        double t = (distance - Cumulative[i]) / segment;
        var p = a * (1.0 - t) + b * t;
        var tangent = b - a;
        double n = Math.Max(Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z), 1e-9);
        return (p.X, p.Z, tangent.X / n, tangent.Z / n);
    }
}

/// <summary>Spline sampling and polygon helpers shared by every finishing pass.</summary>
public static class FinisherGeometry
{
    /// <summary>
    /// <c>spline_dense_polyline</c>: each segment is the cubic Bézier (a.waypoint, a.pointTwo,
    /// b.pointOne, b.waypoint) sampled at <paramref name="samplesPerSegment"/>+1 points, joints shared.
    /// A closed polyline ends back on its first point (callers drop the duplicate where needed).
    /// </summary>
    public static List<Vec2> DensePolyline(JsonObject spline, int samplesPerSegment = 24, bool? close = null)
    {
        var wps = Json.Arr(spline, "waypoints");
        var points = new List<Vec2>();
        if (wps.Count < 2) return points;
        bool closed = close ?? (Json.Truthy(spline["isClosed"]) || Json.Truthy(spline["isFilled"]));
        int count = closed ? wps.Count : wps.Count - 1;
        for (int i = 0; i < count; i++)
        {
            var a = wps[i]!;
            var b = wps[(i + 1) % wps.Count]!;
            var p0 = P(a["waypoint"]);
            var p1 = P(a["pointTwo"]);
            var p2 = P(b["pointOne"]);
            var p3 = P(b["waypoint"]);
            for (int j = points.Count > 0 ? 1 : 0; j <= samplesPerSegment; j++)
            {
                double t = j / (double)samplesPerSegment, u = 1.0 - t;
                points.Add(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t));
            }
        }
        return points;
    }

    /// <summary>Dense closed outline without the repeated closing point.</summary>
    public static List<Vec2> ClosedOutline(JsonObject spline, int samplesPerSegment)
    {
        var dense = DensePolyline(spline, samplesPerSegment, close: true);
        if (dense.Count > 1 && Vec2.Distance(dense[0], dense[^1]) < 1e-6) dense.RemoveAt(dense.Count - 1);
        return dense;
    }

    static Vec2 P(JsonNode? p) => new(Json.Num(p?["x"]), Json.Num(p?["z"]));

    /// <summary><c>_closed_spline</c>: <c>state != 0</c> (missing state counts as closed); legacy booleans as fallback.</summary>
    public static bool IsClosed(JsonObject spline)
    {
        var state = spline["state"];
        if (state is null) return true;
        double s = Json.Num(state);
        if (double.IsFinite(s)) return (int)s != 0;
        return Json.Truthy(spline["isClosed"]) || Json.Truthy(spline["isFilled"]);
    }

    /// <summary><c>_point_in_polygon</c> (even-odd ray cast).</summary>
    public static bool Contains(IReadOnlyList<Vec2> polygon, double x, double z)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = polygon[i].X, zi = polygon[i].Z, xj = polygon[j].X, zj = polygon[j].Z;
            if ((zi > z) != (zj > z))
            {
                double denom = Math.Abs(zj - zi) > 1e-12 ? zj - zi : 1e-12;
                if (x < (xj - xi) * (z - zi) / denom + xi) inside = !inside;
            }
        }
        return inside;
    }

    public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 p) => Contains(polygon, p.X, p.Z);

    /// <summary><c>_polygon_area</c> (the module's second definition, which is the one in effect).</summary>
    public static double Area(IReadOnlyList<Vec2>? poly)
    {
        if (poly is null || poly.Count < 3) return 0;
        double s = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            s += poly[i].X * poly[j].Z - poly[i].Z * poly[j].X;
        return 0.5 * Math.Abs(s);
    }

    /// <summary><c>_poly_bbox</c>: (xmin, zmin, xmax, zmax).</summary>
    public static (double X0, double Z0, double X1, double Z1) BBox(IReadOnlyList<Vec2> poly)
    {
        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
        foreach (var p in poly)
        {
            x0 = Math.Min(x0, p.X); z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X); z1 = Math.Max(z1, p.Z);
        }
        return (x0, z0, x1, z1);
    }

    public static Vec2 Mean(IReadOnlyList<Vec2> poly)
    {
        double x = 0, z = 0;
        foreach (var p in poly) { x += p.X; z += p.Z; }
        return new Vec2(x / poly.Count, z / poly.Count);
    }

    /// <summary>
    /// <c>_polygon_pca_dimensions</c>: (minor, major) extents along the principal axes;
    /// (0, 0) for fewer than three points.
    /// </summary>
    public static (double Minor, double Major) PcaDimensions(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count < 3) return (0, 0);
        var (major, minor, mean) = LinAlg.PrincipalAxes(poly.Select(p => (p.X, p.Z)).ToList());
        double a0 = double.MaxValue, a1 = double.MinValue, b0 = double.MaxValue, b1 = double.MinValue;
        foreach (var p in poly)
        {
            double dx = p.X - mean.X, dz = p.Z - mean.Z;
            double a = dx * major.X + dz * major.Z, b = dx * minor.X + dz * minor.Z;
            a0 = Math.Min(a0, a); a1 = Math.Max(a1, a);
            b0 = Math.Min(b0, b); b1 = Math.Max(b1, b);
        }
        double e1 = a1 - a0, e2 = b1 - b0;
        return (Math.Min(e1, e2), Math.Max(e1, e2));
    }

    /// <summary>
    /// <c>_polygon_centroid_point</c>: area centroid if it lies inside, else the vertex mean if inside,
    /// else the interior grid point nearest the mean; null for fewer than three points.
    /// </summary>
    public static Vec2? CentroidPoint(IReadOnlyList<Vec2>? poly)
    {
        if (poly is null || poly.Count < 3) return null;
        int n = poly.Count;
        double twice = 0, sx = 0, sz = 0, mx = 0, mz = 0;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            double cross = a.X * b.Z - b.X * a.Z;
            twice += cross;
            sx += (a.X + b.X) * cross;
            sz += (a.Z + b.Z) * cross;
            mx += a.X; mz += a.Z;
        }
        if (Math.Abs(twice) > 1e-10)
        {
            double cx = sx / (3.0 * twice), cz = sz / (3.0 * twice);
            if (Contains(poly, cx, cz)) return new Vec2(cx, cz);
        }
        var mean = new Vec2(mx / n, mz / n);
        if (Contains(poly, mean)) return mean;

        var (x0, z0, x1, z1) = BBox(poly);
        double span = Math.Max(x1 - x0, z1 - z0);
        double step = Math.Max(0.5, Math.Min(2.0, span / 20.0));
        Vec2? best = null;
        double bestDist = double.PositiveInfinity;
        foreach (double zz in NumPy.Arange(z0 + 0.5 * step, z1 + 1e-6, step))
            foreach (double xx in NumPy.Arange(x0 + 0.5 * step, x1 + 1e-6, step))
            {
                if (!Contains(poly, xx, zz)) continue;
                double d = Math.Sqrt((xx - mean.X) * (xx - mean.X) + (zz - mean.Z) * (zz - mean.Z));
                if (d < bestDist) { bestDist = d; best = new Vec2(xx, zz); }
            }
        return best ?? mean;
    }

    /// <summary><c>_point_segment_distance_2d</c>.</summary>
    public static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        double denom = dx * dx + dz * dz;
        if (denom <= 1e-12) return Vec2.Distance(p, a);
        double t = Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / denom));
        return Vec2.Distance(p, new Vec2(a.X + t * dx, a.Z + t * dz));
    }

    /// <summary><c>_polygon_edge_clearance</c>: distance to the nearest edge (closed polygon).</summary>
    public static double EdgeClearance(Vec2 p, IReadOnlyList<Vec2>? poly)
    {
        if (poly is null || poly.Count < 3) return 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < poly.Count; i++)
            best = Math.Min(best, SegmentDistance(p, poly[i], poly[(i + 1) % poly.Count]));
        return double.IsFinite(best) ? best : 0;
    }

    public static double Hypot(double x, double z) => double.Hypot(x, z);
}
