using Pga2K25.CourseFormat;

namespace Pga2K25.Maps;

/// <summary>
/// How one hole plays in a wind rose: the share of breezy hours (8 mph and up) with the wind against, across
/// (from the left or right) or behind the line of play, back tee to green.
/// </summary>
public sealed record HoleWind(int Hole, int Par, int Bearing, double Into, double FromLeft, double FromRight, double Down)
{
    public double Across => FromLeft + FromRight;

    public string Character => Into >= .45 ? "Into the wind" : Down >= .45 ? "Downwind" : Across >= .5 ? "Crosswind" : "Mixed";

    /// <summary>One line for a hole sheet, e.g. "Into the wind 38% of breezy hours · across 41% · down 21%".</summary>
    public string Line => $"{Character}  ·  into {Into:P0}, across {Across:P0}, down {Down:P0} of breezy hours";
}

public static class WindExposure
{
    /// <summary>Each hole's exposure, in hole order. Bearings run from the back tee to the green.</summary>
    public static IReadOnlyList<HoleWind> For(CourseModel course, WindRose rose) =>
        course.Holes.OrderBy(h => h.Number).Select(h =>
        {
            var tee = h.Tees.Count > 0 ? h.Tees[^1] : h.Route[0];
            return For(h.Number, h.Par, Bearing(tee, h.GreenAnchor), rose);
        }).ToList();

    public static HoleWind For(int hole, int par, int bearing, WindRose rose)
    {
        double into = 0, down = 0, left = 0, right = 0;
        for (int i = 0; i < 16; i++)
        {
            double v = rose.Sectors[i].Skip(1).Sum();
            double d = ((i * 22.5 - bearing) % 360 + 540) % 360 - 180;   // wind-from relative to the line of play
            if (Math.Abs(d) <= 45) into += v;
            else if (Math.Abs(d) >= 135) down += v;
            else if (d > 0) right += v;
            else left += v;
        }
        double t = into + down + left + right;
        return t <= 0 ? new(hole, par, bearing, 0, 0, 0, 0) : new(hole, par, bearing, into / t, left / t, right / t, down / t);
    }

    /// <summary>"Into the wind: holes 3, 7 · downwind: hole 12 · par 3s into the wind: 1 of 4 · par 5s downwind: 0 of 4".</summary>
    public static string Summary(IReadOnlyList<HoleWind> holes)
    {
        string List(IEnumerable<HoleWind> hs)
        {
            var l = hs.Select(h => h.Hole).ToList();
            return l.Count == 0 ? "none" : (l.Count == 1 ? "hole " : "holes ") + string.Join(", ", l);
        }
        var into = holes.Where(h => h.Character == "Into the wind").ToList();
        var down = holes.Where(h => h.Character == "Downwind").ToList();
        return $"Into the wind: {List(into)}  ·  downwind: {List(down)}  ·  par 3s into the wind: {into.Count(h => h.Par == 3)} of {holes.Count(h => h.Par == 3)}" +
               $"  ·  par 5s downwind: {down.Count(h => h.Par == 5)} of {holes.Count(h => h.Par == 5)}";
    }

    /// <summary>Compass bearing from a to b. Game north is −Z and east is −X.</summary>
    static int Bearing(Vec2 a, Vec2 b)
    {
        var d = b - a;
        double deg = Math.Atan2(-d.X, -d.Z) * 180 / Math.PI;
        return (int)Math.Round((deg % 360 + 360) % 360) % 360;
    }
}
