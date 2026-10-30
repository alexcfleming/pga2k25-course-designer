using Pga2K25.CourseFormat;

namespace Pga2K25.Maps;

public static class OceanFinder
{
    static readonly string[] Names = ["NORTH", "NORTH-EAST", "EAST", "SOUTH-EAST", "SOUTH", "SOUTH-WEST", "WEST", "NORTH-WEST"];

    /// <summary>
    /// Direction (whole-degree bearing) from the middle of the routing towards the surf: the ocean props
    /// (OceanChurn wave / impact prefabs) the designer places along the shoreline, each weighted by 1/d² of its
    /// distance to the nearest hole so the shore that play actually runs along wins on a headland. Null when there
    /// are none. Ground at sea level is not used: an unsculpted plot is a flat at exactly that height all round.
    /// A heuristic: the course card settings and the Place card can override it.
    /// </summary>
    public static int? SeaBearing(CourseModel course)
    {
        var surf = course.PlacedObjects.Where(p => p.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase)).ToList();
        if (surf.Count == 0 || course.Holes.Count == 0) return null;
        var pts = course.Holes.SelectMany(h => h.Route).ToList();
        var centre = new Vec2(pts.Average(p => p.X), pts.Average(p => p.Z));
        double sx = 0, sz = 0;
        foreach (var p in surf)
        {
            var d = (p.Position - centre).Normalized();
            double near = Math.Max(20, pts.Min(q => Vec2.Distance(q, p.Position)));
            sx += d.X / (near * near); sz += d.Z / (near * near);
        }
        if (Math.Sqrt(sx * sx + sz * sz) < 1e-6) return null;
        double deg = Math.Atan2(-sx, -sz) * 180 / Math.PI;   // game north is -Z, east -X
        return (int)Math.Round((deg % 360 + 360) % 360) % 360;
    }

    /// <summary>
    /// The course's sea side as a bearing: the Place card's choice ("WEST", "NORTH-EAST", …), "none" for an inland
    /// course, or the surf props when it says to detect.
    /// </summary>
    public static int? SeaBearing(CoursePlace place, CourseModel course)
    {
        if (place.SeaSide is null) return SeaBearing(course);
        int i = Array.FindIndex(Names, n => n.Equals(place.SeaSide.Trim(), StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : i * 45;
    }
}
