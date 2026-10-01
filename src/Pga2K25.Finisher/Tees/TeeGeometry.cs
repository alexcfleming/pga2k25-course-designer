using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Tees;

/// <summary>A closed surface spline that could be a physical tee pad (<c>_tee_surface_geometries</c> item).</summary>
public sealed class TeeSurface
{
    public required int Index { get; init; }
    public required int Surface { get; init; }
    public required List<Vec2> Poly { get; init; }
    public required double Area { get; init; }
    public Vec2? Center { get; set; }

    /// <summary>1-based hole that owns the pad (<c>ownerHole</c>), assigned before tee creation.</summary>
    public int? OwnerHole { get; set; }
    public double? OwnerScore { get; set; }

    /// <summary>Always "spline": the REV3 rule uses closed surface splines only, never paint brushes.</summary>
    public string Kind => "spline";
}

/// <summary>A tee pad scored against one hole's tee axis (<c>_tee_pad_candidates</c> item).</summary>
public sealed record TeePadCandidate(TeeSurface Surface, Vec2 Center, double Along, double Lateral, double Distance,
    double Minor, double Major, double Area, bool Compact, IReadOnlyList<int> Occupied)
{
    public bool IsOccupied => Occupied.Count > 0;
}

/// <summary>A tee marker (or the route start as fallback) matched to the pad it stands on.</summary>
public sealed record TeeRegion(TeeSurface Surface, int Hole, Vec2 Anchor, string AnchorSource);

public static class TeeGeometry
{
    public const double MaxForward = 180.0, MaxLateral = 80.0, MaxDistance = 190.0, MaxMinor = 30.0, MaxMajor = 110.0,
        MaxArea = 1400.0, CompactMaxMajor = 32.0, CompactMaxArea = 300.0, MinForward = 8.0, MinSeparation = 8.0, InternalGrid = 1.0;

    /// <summary>Surfaces tee pads are drawn with (TGC imports use 2 or 3, occasionally 4; 1 is excluded by the green test).</summary>
    public static readonly int[] AllowedSurfaces = [1, 2, 3, 4];

    public static JsonArray Holes(JsonObject course) => Json.Arr(course, "holes3");

    /// <summary><c>_tee_xy</c>.</summary>
    public static Vec2? TeePoint(JsonNode? tee)
    {
        var (x, z) = Json.XZ(tee?["position"]);
        return Json.Finite(x, z) ? new Vec2(x, z) : null;
    }

    public static List<Vec2> TeePoints(JsonObject hole) =>
        Json.Arr(hole, "tees").Select(TeePoint).Where(p => p is not null).Select(p => p!.Value).ToList();

    static Vec2? WaypointPoint(JsonNode? wp)
    {
        var (x, z) = Json.XZ(wp);
        return Json.Finite(x, z) ? new Vec2(x, z) : null;
    }

    /// <summary><c>_hole_terminal_point</c>: last route waypoint (green anchor).</summary>
    public static Vec2? TerminalPoint(JsonObject hole)
    {
        var wps = Json.Arr(hole, "waypoints");
        return wps.Count == 0 ? null : WaypointPoint(wps[^1]);
    }

    /// <summary>
    /// <c>_hole_tee_axis</c>: back tee = the marker nearest waypoint[0] (or waypoint[0] itself), and the unit
    /// vector toward the first later waypoint more than 5 m away.
    /// </summary>
    public static (Vec2? Back, Vec2? Forward) HoleTeeAxis(JsonObject hole)
    {
        var wps = Json.Arr(hole, "waypoints");
        if (wps.Count == 0) return (null, null);
        var wp0 = WaypointPoint(wps[0]);
        if (wp0 is null) return (null, null);
        var tees = TeePoints(hole);
        var back = tees.Count > 0 ? MinBy(tees, p => Vec2.Distance(p, wp0.Value)) : wp0.Value;
        Vec2? target = null;
        foreach (var wp in wps.Skip(1))
        {
            var candidate = WaypointPoint(wp);
            if (candidate is null) continue;
            if (Vec2.Distance(candidate.Value, back) > 5.0) { target = candidate; break; }
        }
        if (target is null) return (back, null);
        var d = target.Value - back;
        double length = Hypot(d.X, d.Z);
        if (length <= 1e-6) return (back, null);
        return (back, new Vec2(d.X / length, d.Z / length));
    }

    /// <summary><c>_tee_progress</c>: (along, |lateral|) relative to the back tee and hole direction.</summary>
    public static (double Along, double Lateral) Progress(Vec2 point, Vec2 back, Vec2 forward)
    {
        double dx = point.X - back.X, dz = point.Z - back.Z;
        return (dx * forward.X + dz * forward.Z, Math.Abs(dx * forward.Z - dz * forward.X));
    }

    /// <summary><c>_tee_surface_geometries</c>: closed surface 1–4 splines that do not contain a green anchor.</summary>
    public static List<TeeSurface> SurfaceGeometries(JsonObject course)
    {
        var terminals = Holes(course).OfType<JsonObject>().Select(TerminalPoint).Where(p => p is not null).Select(p => p!.Value).ToList();
        var result = new List<TeeSurface>();
        var splines = Json.Arr(course, "surfaceSplines2");
        for (int index = 0; index < splines.Count; index++)
        {
            if (splines[index] is not JsonObject spline || !IsClosed(spline)) continue;
            int surface = Json.Int(spline["surface"], -1);
            if (!AllowedSurfaces.Contains(surface)) continue;
            var poly = DensePolyline(spline, 12, close: true);
            double area = Area(poly);
            if (poly.Count < 3 || area <= 0.1) continue;
            var center = CentroidPoint(poly);
            if (center is null) continue;
            if (terminals.Any(t => Contains(poly, t))) continue;
            result.Add(new TeeSurface { Index = index, Surface = surface, Poly = poly, Area = area, Center = center });
        }
        return result;
    }

    /// <summary><c>_assign_tee_geometry_owners</c>: a pad with markers belongs to the nearest marker's hole, else the best-aligned hole.</summary>
    public static List<TeeSurface> AssignOwners(JsonObject course, List<TeeSurface> geometries)
    {
        var contexts = new List<(int Hole, Vec2 Back, Vec2 Forward)>();
        var teesByHole = new List<(int Hole, List<Vec2> Points)>();
        var holes = Holes(course);
        for (int i = 0; i < holes.Count; i++)
        {
            if (holes[i] is not JsonObject hole) continue;
            var (back, forward) = HoleTeeAxis(hole);
            if (back is not null && forward is not null) contexts.Add((i + 1, back.Value, forward.Value));
            teesByHole.Add((i + 1, TeePoints(hole)));
        }
        foreach (var item in geometries)
        {
            item.Center ??= CentroidPoint(item.Poly);
            item.OwnerHole = null;
            item.OwnerScore = null;
            if (item.Center is not Vec2 center) continue;

            var occupied = new List<(double Distance, int Hole)>();
            foreach (var (hole, points) in teesByHole)
                foreach (var p in points)
                    if (Contains(item.Poly, p)) occupied.Add((Vec2.Distance(p, center), hole));
            if (occupied.Count > 0)
            {
                var best = occupied.Min();
                item.OwnerHole = best.Hole;
                item.OwnerScore = -1e6 + best.Distance;
                continue;
            }
            var scored = new List<(double Score, int Hole)>();
            foreach (var (hole, back, forward) in contexts)
            {
                var (along, lateral) = Progress(center, back, forward);
                double distance = Vec2.Distance(center, back);
                if (along < -15.0 || along > MaxForward || lateral > MaxLateral || distance > MaxDistance) continue;
                double score = 4.0 * lateral + 0.035 * distance + 0.02 * Math.Max(0.0, along) + 6.0 * Math.Max(0.0, -along);
                scored.Add((score, hole));
            }
            if (scored.Count > 0)
            {
                var best = scored.Min();
                item.OwnerScore = best.Score;
                item.OwnerHole = best.Hole;
            }
        }
        return geometries;
    }

    /// <summary><c>_tee_pad_candidates</c>: plausible pads for one hole, clustered, ordered back → green.</summary>
    public static (Vec2? Back, Vec2? Forward, List<TeePadCandidate> Candidates) PadCandidates(JsonObject hole, List<TeeSurface> geometries, int? holeIndex)
    {
        var (back, forward) = HoleTeeAxis(hole);
        if (back is not Vec2 b || forward is not Vec2 f) return (back, forward, []);
        var existing = TeePoints(hole);
        var terminal = TerminalPoint(hole);
        double maxForwardForHole = terminal is Vec2 t
            ? Math.Min(MaxForward, Math.Max(45.0, 0.68 * Vec2.Distance(t, b)))
            : MaxForward;

        var raw = new List<TeePadCandidate>();
        foreach (var item in geometries)
        {
            if (holeIndex is not null && item.OwnerHole != holeIndex) continue;
            var poly = item.Poly;
            if ((item.Center ?? CentroidPoint(poly)) is not Vec2 center) continue;
            var (along, lateral) = Progress(center, b, f);
            double distance = Vec2.Distance(center, b);
            var (minor, major) = PcaDimensions(poly);
            double area = item.Area;
            if (terminal is Vec2 term)
            {
                if (Contains(poly, term)) continue;
                if (Vec2.Distance(center, term) < 18.0) continue;
            }
            var occupied = existing.Select((p, i) => (p, i)).Where(e => Contains(poly, e.p)).Select(e => e.i).ToList();
            if (along < -20.0 || along > maxForwardForHole) continue;
            if (lateral > MaxLateral || distance > MaxDistance) continue;
            if (minor < 2.0 || minor > MaxMinor) continue;
            if (major > MaxMajor || area > MaxArea) continue;
            bool compact = major <= CompactMaxMajor && area <= CompactMaxArea;
            if (occupied.Count == 0 && !compact) continue;
            raw.Add(new TeePadCandidate(item, center, along, lateral, distance, minor, major, area, compact, occupied));
        }
        raw = raw.OrderBy(c => c.IsOccupied ? 0 : 1).ThenBy(c => c.Area).ThenBy(c => Math.Abs(c.Lateral)).ToList();

        var clusters = new List<TeePadCandidate>();
        foreach (var item in raw)
        {
            int matched = -1;
            for (int k = 0; k < clusters.Count; k++)
            {
                var existingItem = clusters[k];
                if (Vec2.Distance(item.Center, existingItem.Center) > 7.0) continue;
                double a = Math.Max(item.Area, 1e-6), c = Math.Max(existingItem.Area, 1e-6);
                if (Math.Max(a, c) / Math.Min(a, c) > 3.0) continue;
                matched = k;
                break;
            }
            if (matched < 0) { clusters.Add(item); continue; }
            var m = clusters[matched];
            if (item.IsOccupied && !m.IsOccupied) clusters[matched] = item;
            else if (!m.IsOccupied && item.Area < m.Area) clusters[matched] = item;
        }
        return (b, f, clusters.OrderBy(c => c.Along).ToList());
    }

    /// <summary><c>_tee_crosswise_center</c>: centre the marker left-right on its pad without moving it along the hole.</summary>
    public static (Vec2 Point, double Width)? CrosswiseCenter(IReadOnlyList<Vec2>? poly, Vec2 anchor, Vec2? forward)
    {
        if (poly is null || poly.Count < 3 || forward is not Vec2 fw) return null;
        double fx = fw.X, fz = fw.Z, n = Hypot(fx, fz);
        if (n <= 1e-9) return null;
        fx /= n; fz /= n;
        double lx = -fz, lz = fx, ax = anchor.X, az = anchor.Z;
        static double Cross(double ax_, double az_, double bx_, double bz_) => ax_ * bz_ - az_ * bx_;

        var crossings = new List<double>();
        int count = poly.Count;
        for (int i = 0; i < count; i++)
        {
            double x0 = poly[i].X, z0 = poly[i].Z, x1 = poly[(i + 1) % count].X, z1 = poly[(i + 1) % count].Z;
            double sx = x1 - x0, sz = z1 - z0, qx = x0 - ax, qz = z0 - az;
            double denom = Cross(lx, lz, sx, sz);
            if (Math.Abs(denom) <= 1e-9)
            {
                if (Math.Abs(Cross(qx, qz, lx, lz)) <= 1e-7)
                {
                    crossings.Add(qx * lx + qz * lz);
                    crossings.Add((x1 - ax) * lx + (z1 - az) * lz);
                }
                continue;
            }
            double t = Cross(qx, qz, sx, sz) / denom;
            double u = Cross(qx, qz, lx, lz) / denom;
            if (-1e-7 <= u && u <= 1.0000001) crossings.Add(t);
        }
        if (crossings.Count < 2) return null;
        crossings.Sort();
        var unique = new List<double>();
        foreach (double v in crossings)
            if (unique.Count == 0 || Math.Abs(v - unique[^1]) > 1e-5) unique.Add(v);
        var left = unique.Where(v => v <= 1e-6).ToList();
        var right = unique.Where(v => v >= -1e-6).ToList();
        if (left.Count == 0 || right.Count == 0) return null;
        double t0 = left.Max(), t1 = right.Min();
        if (t1 - t0 <= 1e-6) return null;
        double tm = 0.5 * (t0 + t1);
        return (new Vec2(ax + tm * lx, az + tm * lz), t1 - t0);
    }

    /// <summary><c>_tee_anchor_points</c>: tee markers, plus waypoint[0] when no marker is within 0.5 m of it.</summary>
    public static List<(Vec2 Point, string Source)> AnchorPoints(JsonObject hole)
    {
        var anchors = TeePoints(hole).Select(p => (p, "tee")).ToList();
        var wps = Json.Arr(hole, "waypoints");
        if (wps.Count > 0 && WaypointPoint(wps[0]) is Vec2 w0 && anchors.All(a => Vec2.Distance(a.p, w0) > 0.5))
            anchors.Add((w0, "waypoint1"));
        var result = new List<(Vec2, string)>();
        foreach (var (p, src) in anchors)
            if (result.All(r => Vec2.Distance(r.Item1, p) > 0.35)) result.Add((p, src));
        return result;
    }

    /// <summary><c>find_active_tee_regions</c>: the smallest pad under each tee anchor, de-duplicated.</summary>
    public static List<TeeRegion> ActiveRegions(JsonObject course, List<TeeSurface>? candidates = null)
    {
        candidates ??= SurfaceGeometries(course);
        var regions = new List<TeeRegion>();
        var holes = Holes(course);
        for (int h = 0; h < holes.Count; h++)
        {
            if (holes[h] is not JsonObject hole) continue;
            foreach (var (point, source) in AnchorPoints(hole))
            {
                var matches = candidates.Where(c => Contains(c.Poly, point)).ToList();
                if (matches.Count == 0) continue;
                var best = matches.OrderBy(m => m.Area).First();
                regions.Add(new TeeRegion(best, h + 1, point, source));
            }
        }
        var seen = new HashSet<(int, int, double, double)>();
        var unique = new List<TeeRegion>();
        foreach (var r in regions)
            if (seen.Add((r.Hole, r.Surface.Index, Py.Round(r.Anchor.X, 3), Py.Round(r.Anchor.Z, 3)))) unique.Add(r);
        return unique;
    }

    /// <summary><c>_active_tee_spline_indices</c>.</summary>
    public static HashSet<int> ActiveSplineIndices(JsonObject course) =>
        ActiveRegions(course).Select(r => r.Surface.Index).ToHashSet();

    internal static T MinBy<T>(IEnumerable<T> items, Func<T, double> key)
    {
        T best = default!;
        double bestKey = double.PositiveInfinity;
        bool any = false;
        foreach (var item in items)
        {
            double k = key(item);
            if (!any || k < bestKey) { best = item; bestKey = k; any = true; }
        }
        return best;
    }
}
