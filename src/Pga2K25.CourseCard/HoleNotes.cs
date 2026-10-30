using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.YardageBook;

namespace Pga2K25.CourseCard;

/// <summary>A hole's card text written from the course file: its hazards, and its green's shape, contour and hole positions.</summary>
public sealed record HoleNote(int Hole, IReadOnlyList<string> Hazards, string GreenNotes);

/// <summary>
/// Reads the hole data card's Hazards and Green Contours &amp; Hole Positions columns from the same analysis the
/// yardage book (bunkers, water along the line of play) and greens book (outline, surface, pins) draw from.
/// Yards are from the back tee. Sides are as the golfer faces the green: L and R.
/// </summary>
public static class HoleNotes
{
    const double Yard = 0.9144, MetresToInches = 39.3700787;

    /// <summary>Bunkers this close to the green's edge (m) are greenside.</summary>
    const double GreensideMetres = 10;

    /// <summary>Hazards further than this from the line of play (m) are left off.</summary>
    const double InPlayMetres = 35;

    public static IReadOnlyList<HoleNote> For(CourseModel course, IProgress<string>? log = null)
    {
        var layers = CourseLayers.From(course);
        var yardage = new YardageOptions();
        var greens = new GreensBookOptions();
        var rocks = new RockFootprints(course);
        var water = course.Water.Count > 0 ? WaterTracer.Trace(course, yardage.Terrain) : null;
        var notes = new List<HoleNote>();
        foreach (var hole in course.Holes.OrderBy(h => h.Number))
        {
            try
            {
                var a = HoleAnalysis.Analyze(course, layers, hole, yardage, 0.7, rocks, water);
                var g = GreenAnalyzer.Analyze(course, hole, greens);
                notes.Add(new HoleNote(hole.Number, Hazards(a, g), Green(g)));
            }
            catch (Exception ex)
            {
                log?.Report($"Hole {hole.Number}: notes not written ({ex.Message}).");
                notes.Add(new HoleNote(hole.Number, [], ""));
            }
        }
        return notes;
    }

    // ───────────────────────────── hazards ─────────────────────────────

    /// <summary>Station (m from the back tee along the line of play) and signed offset (m, + right) of a point.</summary>
    static (double Station, double Offset) OnRoute(IReadOnlyList<Vec2> route, Vec2 p)
    {
        double best = double.MaxValue, station = 0, offset = 0, run = 0;
        for (int i = 1; i < route.Count; i++)
        {
            var a = route[i - 1]; var b = route[i];
            var ab = b - a;
            double len = ab.Length;
            if (len < 1e-6) continue;
            var u = ab * (1 / len);
            double t = Math.Clamp(Dot(p - a, u), 0, len);
            var foot = a + u * t;
            double d = Vec2.Distance(p, foot);
            if (d < best)
            {
                best = d;
                station = run + t;
                var right = new Vec2(u.Z, -u.X);
                offset = Dot(p - foot, right);
            }
            run += len;
        }
        return (station, offset);
    }

    static List<string> Hazards(HoleAnalysis a, GreenAnalysis g)
    {
        var route = a.Route;
        double length = route.Zip(route.Skip(1), Vec2.Distance).Sum();
        var outline = g.Outline;
        int Yd(double m) => (int)Math.Round(m / Yard);

        var left = new List<int>(); var right = new List<int>(); var cross = new List<string>();
        var greenside = new HashSet<string>();
        foreach (var bunker in a.Bunkers)
        {
            if (a.Foreign.Contains(bunker) || bunker.Count < 3) continue;
            double edge = bunker.Min(p => Geometry.Contains(outline, p) ? 0 : Geometry.DistanceToEdge(outline, p));
            if (edge < GreensideMetres)
            {
                greenside.Add(GreenSide(g, Centroid(bunker)));
                continue;
            }
            var spots = bunker.Select(p => OnRoute(route, p)).ToList();
            if (spots.Min(s => Math.Abs(s.Offset)) > InPlayMetres) continue;
            double near = spots.Min(s => s.Station), far = spots.Max(s => s.Station);
            if (far < 60) continue;   // in front of the tees
            if (spots.Min(s => s.Offset) < 0 && spots.Max(s => s.Offset) > 0)
                cross.Add(Range(Yd(near), Yd(far)));
            else if (spots.Average(s => s.Offset) < 0) left.Add(Yd(far));   // the carry
            else right.Add(Yd(far));
        }

        var hazards = new List<string>();
        foreach (var body in a.WaterBodies)
        {
            if (body.Count == 0 || body[0].Count < 3) continue;
            bool In(Vec2 p) => body.Count(ring => Geometry.Contains(ring, p)) % 2 == 1;
            // Walk the line of play: where it is over the water, and where the water is beside it.
            double? enter = null, exit = null, besideFrom = null, besideTo = null;
            double besideOffset = 0, run = 0;
            for (int i = 1; i < route.Count; i++)
            {
                var p0 = route[i - 1]; var p1 = route[i];
                double len = Vec2.Distance(p0, p1);
                for (double t = 0; t < len; t += 2)
                {
                    var p = p0 + (p1 - p0) * (t / len);
                    double s = run + t;
                    if (In(p)) { enter ??= s; exit = s; continue; }
                    double d = body.Min(ring => Geometry.DistanceToEdge(ring, p));
                    if (d < 25)
                    {
                        besideFrom ??= s; besideTo = s;
                        var side = new Vec2((p1 - p0).Z, -(p1 - p0).X);
                        var nearest = body.SelectMany(r => r).MinBy(q => Vec2.Distance(q, p));
                        besideOffset += Dot(nearest - p, side);
                    }
                }
                run += len;
            }
            bool byGreen = body.Any(ring => ring.Any(q => Geometry.DistanceToEdge(outline, q) < GreensideMetres));
            if (enter is double e && exit is double x)
            {
                hazards.Add(length - x < 20 ? $"Water short of green {Yd(e)}" : $"Water carry {Range(Yd(e), Yd(x))}");
            }
            else if (besideFrom is double f && besideTo is double to)
            {
                string side = besideOffset < 0 ? "L" : "R";
                hazards.Add(length - to < 25 ? $"Water {side} of green" : $"Water {side} {Range(Yd(f), Yd(to))}");
            }
            else if (byGreen) hazards.Add($"Water {GreenSide(g, Centroid(body[0]))} of green");
        }

        if (cross.Count > 0) hazards.Add($"Cross bunker{(cross.Count > 1 ? "s" : "")} {string.Join(", ", cross)}");
        if (left.Count + right.Count > 0)
        {
            var parts = new List<string>();
            if (left.Count > 0) parts.Add($"L {Carries(left)}");
            if (right.Count > 0) parts.Add($"R {Carries(right)}");
            hazards.Add($"FW bunker{(left.Count + right.Count > 1 ? "s" : "")} {string.Join(", ", parts)}");
        }
        if (greenside.Count > 0)
        {
            string[] order = ["front", "front-L", "front-R", "L", "R", "back-L", "back-R", "back"];
            hazards.Add($"Greenside {string.Join(", ", order.Where(greenside.Contains))}");
        }
        return hazards;
    }

    static string Range(int from, int to) => to - from < 8 ? $"{from}" : $"{from}–{to}";

    /// <summary>Carry numbers, with a run of bunkers within 15 yd of each other given as one range: "340–351, 382".</summary>
    static string Carries(IEnumerable<int> yards)
    {
        var runs = new List<(int From, int To)>();
        foreach (int y in yards.Order())
            if (runs.Count > 0 && y - runs[^1].To <= 15) runs[^1] = (runs[^1].From, y);
            else runs.Add((y, y));
        return string.Join(", ", runs.Select(r => r.From == r.To ? $"{r.From}" : $"{r.From}–{r.To}"));
    }

    /// <summary>Where a point sits round the green as the golfer approaches: front, front-L, L, back-R, ….</summary>
    static string GreenSide(GreenAnalysis g, Vec2 p)
    {
        var c = Centroid(g.Outline);
        var f = g.Forward;
        var r = new Vec2(f.Z, -f.X);
        double along = Dot(p - c, f), across = Dot(p - c, r);
        double ang = Math.Atan2(across, -along) * 180 / Math.PI;   // 0 = front (towards the golfer), + = right
        return Math.Abs(ang) switch
        {
            < 30 => "front",
            < 70 => ang < 0 ? "front-L" : "front-R",
            < 120 => ang < 0 ? "L" : "R",
            < 155 => ang < 0 ? "back-L" : "back-R",
            _ => "back",
        };
    }

    // ───────────────────────────── green ─────────────────────────────

    static string Green(GreenAnalysis g)
    {
        var parts = new List<string> { Shape(g) };
        if (Tilt(g) is { } tilt) parts.Add(tilt);
        if (Feature(g) is { } feature) parts.Add(feature);
        if (Pins(g) is { } pins) parts.Add(pins);
        return string.Join(" · ", parts);
    }

    /// <summary>Depth and width in the approach frame, and how the green's long axis sits to the line of play ("angled L to R": front-left to back-right).</summary>
    static string Shape(GreenAnalysis g)
    {
        var o = g.Outline;
        var c = Centroid(o);
        var f = g.Forward;
        var r = new Vec2(f.Z, -f.X);
        double depth = (o.Max(p => Dot(p - c, f)) - o.Min(p => Dot(p - c, f))) / Yard;
        double width = (o.Max(p => Dot(p - c, r)) - o.Min(p => Dot(p - c, r))) / Yard;

        // Long axis from the outline's second moments, as an angle off the line of play (+ = runs front-L to back-R).
        double sxx = 0, szz = 0, sxz = 0;
        foreach (var p in o)
        {
            double u = Dot(p - c, r), v = Dot(p - c, f);
            sxx += u * u; szz += v * v; sxz += u * v;
        }
        double axis = 0.5 * Math.Atan2(2 * sxz, szz - sxx) * 180 / Math.PI;   // 0 = along the line of play
        double ratio = Math.Max(depth, width) / Math.Max(1, Math.Min(depth, width));

        double hull = Geometry.Area(ConvexHull(o)), solidity = hull > 0 ? g.AreaSquareMetres / hull : 1;
        string form = solidity < 0.86 ? "kidney" : ratio > 1.45 ? (depth > width ? "long, narrow" : "wide, shallow") : ratio > 1.15 ? "oval" : "round";
        string angle = ratio > 1.15 && Math.Abs(axis) is > 20 and < 70 ? (axis > 0 ? ", angled L to R" : ", angled R to L") : "";
        return $"{Cap(form)} {Math.Round(depth):0}×{Math.Round(width):0} yd{angle}";
    }

    /// <summary>The best-fit plane's fall across the surface, named by the way it runs to the golfer.</summary>
    static string? Tilt(GreenAnalysis g)
    {
        var pts = Surface(g);
        if (pts.Count < 10) return null;
        var c = Centroid(g.Outline);
        var f = g.Forward;
        var r = new Vec2(f.Z, -f.X);
        // Least squares h = a·u + b·v + k, with u across (right) and v along (away from the golfer).
        double su = 0, sv = 0, sh = 0, suu = 0, svv = 0, suv = 0, suh = 0, svh = 0;
        foreach (var (p, h) in pts)
        {
            double u = Dot(p - c, r), v = Dot(p - c, f);
            su += u; sv += v; sh += h; suu += u * u; svv += v * v; suv += u * v; suh += u * h; svh += v * h;
        }
        int n = pts.Count;
        double cuu = suu - su * su / n, cvv = svv - sv * sv / n, cuv = suv - su * sv / n, cuh = suh - su * sh / n, cvh = svh - sv * sh / n;
        double det = cuu * cvv - cuv * cuv;
        if (Math.Abs(det) < 1e-9) return null;
        double gu = (cuh * cvv - cvh * cuv) / det, gv = (cvh * cuu - cuh * cuv) / det;   // m per m
        double pct = Math.Sqrt(gu * gu + gv * gv) * 100;
        string fall = GreenAnalyzer.FormatFeetInches(g.FallInches).TrimStart('+');
        if (pct < 0.8) return $"nearly flat, {fall} fall";
        // Downhill runs along (−gu, −gv). Name where the high side is and where it drains to.
        double down = Math.Atan2(-gu, -gv) * 180 / Math.PI;   // 0 = away from the golfer (front to back), + = to the right
        string run = Math.Abs(down) switch
        {
            < 25 => "front to back",
            < 65 => down > 0 ? "front-L to back-R" : "front-R to back-L",
            < 115 => down > 0 ? "left to right" : "right to left",
            < 155 => down > 0 ? "back-L to front-R" : "back-R to front-L",
            _ => "back to front",
        };
        return $"falls {run} {pct:0.#}%, {fall}";
    }

    /// <summary>A tier or ridge (a band much steeper than the rest), a crown or a bowl, if the surface has one.</summary>
    static string? Feature(GreenAnalysis g)
    {
        var o = g.Outline;
        var c = Centroid(o);
        var f = g.Forward;
        var r = new Vec2(f.Z, -f.X);
        var steep = new List<Vec2>();
        int all = 0;
        double threshold = Math.Max(4.5, g.MedianSlopePct * 2.2);
        double minX = o.Min(p => p.X), maxX = o.Max(p => p.X), minZ = o.Min(p => p.Z), maxZ = o.Max(p => p.Z);
        for (double x = minX; x <= maxX; x += 0.5)
            for (double z = minZ; z <= maxZ; z += 0.5)
            {
                var p = new Vec2(x, z);
                if (!Geometry.Contains(o, p) || Geometry.DistanceToEdge(o, p) < 1.5) continue;
                all++;
                if (g.Field.Gradient(p).Length * 100 > threshold) steep.Add(p);
            }
        if (all > 0 && steep.Count > all * 0.07)
        {
            double depth = o.Max(p => Dot(p - c, f)) - o.Min(p => Dot(p - c, f));
            double width = o.Max(p => Dot(p - c, r)) - o.Min(p => Dot(p - c, r));
            var m = Centroid(steep);
            double along = Dot(m - c, f) / (depth / 2), across = Dot(m - c, r) / (width / 2);
            // A band running across the green (steep points spread side to side) is a tier; along it, a spine.
            double spreadAcross = steep.Max(p => Dot(p - c, r)) - steep.Min(p => Dot(p - c, r));
            double spreadAlong = steep.Max(p => Dot(p - c, f)) - steep.Min(p => Dot(p - c, f));
            if (spreadAcross / width > spreadAlong / depth)
                return $"tier {(along < -0.3 ? "near the front" : along > 0.3 ? "towards the back" : "through the middle")}";
            return across < -0.3 ? "spine down the left" : across > 0.3 ? "spine down the right" : "central spine";
        }

        // Crown or bowl: the middle against the edges, once the overall tilt is taken out.
        var pts = Surface(g);
        if (pts.Count < 20) return null;
        double radius = Math.Sqrt(g.AreaSquareMetres / Math.PI);
        var inner = pts.Where(t => Vec2.Distance(t.P, c) < radius * 0.4).Select(t => t.H).ToList();
        var outer = pts.Where(t => Vec2.Distance(t.P, c) > radius * 0.75).ToList();
        if (inner.Count < 4 || outer.Count < 8) return null;
        // Pair opposite edges: their mean cancels a plane, leaving curvature.
        double edgeMean = outer.Average(t => t.H), middle = inner.Average();
        double bump = (middle - edgeMean) * MetresToInches;
        return bump > 3 ? "crowned" : bump < -3 ? "bowl" : null;
    }

    /// <summary>Where the hole positions sit, e.g. "pins front, mid-R, back-L ×2".</summary>
    static string? Pins(GreenAnalysis g)
    {
        if (g.Pins.Count == 0) return null;
        var o = g.Outline;
        var c = Centroid(o);
        var f = g.Forward;
        var r = new Vec2(f.Z, -f.X);
        double depth = o.Max(p => Dot(p - c, f)) - o.Min(p => Dot(p - c, f));
        double width = o.Max(p => Dot(p - c, r)) - o.Min(p => Dot(p - c, r));
        var names = g.Pins.Select(pin =>
        {
            double along = Dot(pin.Position - c, f) / (depth / 2), across = Dot(pin.Position - c, r) / (width / 2);
            string row = along < -0.33 ? "front" : along > 0.33 ? "back" : "mid";
            string col = across < -0.33 ? "-L" : across > 0.33 ? "-R" : "";
            return row + col;
        }).ToList();
        string[] order = ["front-L", "front", "front-R", "mid-L", "mid", "mid-R", "back-L", "back", "back-R"];
        var grouped = order.Where(names.Contains).Select(n => names.Count(x => x == n) is var k and > 1 ? $"{n} ×{k}" : n);
        return $"pin{(g.Pins.Count == 1 ? "" : "s")} {string.Join(", ", grouped)}";
    }

    /// <summary>Surface heights (m, relative) on a 0.75 m lattice inside the outline, clear of the collar.</summary>
    static List<(Vec2 P, double H)> Surface(GreenAnalysis g)
    {
        var o = g.Outline;
        var list = new List<(Vec2, double)>();
        double minX = o.Min(p => p.X), maxX = o.Max(p => p.X), minZ = o.Min(p => p.Z), maxZ = o.Max(p => p.Z);
        for (double x = minX; x <= maxX; x += 0.75)
            for (double z = minZ; z <= maxZ; z += 0.75)
            {
                var p = new Vec2(x, z);
                if (Geometry.Contains(o, p) && Geometry.DistanceToEdge(o, p) >= 1) list.Add((p, g.Field.Sample(p) - g.ReferenceMetres));
            }
        return list;
    }

    static List<Vec2> ConvexHull(IReadOnlyList<Vec2> pts)
    {
        var p = pts.OrderBy(q => q.X).ThenBy(q => q.Z).ToList();
        if (p.Count < 3) return p;
        double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);
        var hull = new List<Vec2>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            foreach (var q in pass == 0 ? p : Enumerable.Reverse(p))
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], q) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    static Vec2 Centroid(IReadOnlyList<Vec2> pts) => new(pts.Average(p => p.X), pts.Average(p => p.Z));
    static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
