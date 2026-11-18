using System.Runtime.CompilerServices;

namespace Pga2K25.CourseFormat;

/// <summary>
/// Water held in by a bunker wall: a closed bunker outline with water on it, the bunker's cut digging the bed. Two
/// builds are known: water planes (<c>water[]</c> entries) laid along it, all at one level (Alex's Berwick Coast
/// stream, 17 Nov 2026: one 850 m × 5 m outline and 55 dabs at 8.51 m), or a water-tool body of water drawn round it,
/// with retaining-wall pieces placed along the bunker's edge (a tester's course, holes 7–9, 18 Nov 2026). Every product draws
/// the outline as water edged with a wall and plays it as water, not sand.
/// </summary>
/// <param name="SplineIndex">The bunker spline's index in <see cref="CourseModel.SurfaceSplines"/>.</param>
/// <param name="Outline">The outline, sampled as the maps sample it.</param>
/// <param name="Level">The water level (world m): the planes' level, or the body of water's.</param>
/// <param name="PlaneIndices">The <see cref="WaterPlane.Index"/> of every plane that belongs to it.</param>
/// <param name="Compactness">4π·area ÷ perimeter²: 1 for a circle, about 0.02 for Berwick Coast's stream.</param>
public sealed record WalledWater(int SplineIndex, List<Vec2> Outline, double Level, IReadOnlyList<int> PlaneIndices, double Compactness)
{
    /// <summary>The positions in <see cref="CourseModel.WaterPaths"/> of the bodies of water that belong to it.</summary>
    public IReadOnlyList<int> PathIndices { get; init; } = [];

    /// <summary>Long and thin (compactness under <see cref="WalledWaterRules.StreamCompactness"/>): a stream; otherwise a pond or lake.</summary>
    public bool IsStream => Compactness < WalledWaterRules.StreamCompactness;
}

/// <summary>The rule that finds <see cref="WalledWater"/>, from two courses so far (Berwick Coast and a tester's course).</summary>
public static class WalledWaterRules
{
    /// <summary>At least this many plane centres on the outline make it walled water.</summary>
    public const int MinPlanes = 3;

    /// <summary>A plane belongs to an outline when its centre is inside it or this close (m) to its edge; a body of water holds an outline point this close to its edge too.</summary>
    public const double PlaneReachMetres = 5;

    /// <summary>Planes this close in level (m) are one body of water.</summary>
    public const double LevelTolerance = 0.01;

    /// <summary>Below this compactness a walled outline is a stream; at or above it, a pond or lake.</summary>
    public const double StreamCompactness = 0.15;

    /// <summary>A body of water walls an outline when it holds at least this share of the outline's points...</summary>
    public const double BodyHoldsShare = 0.8;

    /// <summary>
    /// ...and the outline is at least this share of the body's area, so a sand island in a lake stays sand. The walled
    /// pond on the tester's hole 7 is 27 % of the body drawn round it; the other two are 60 % and 80 %.
    /// </summary>
    public const double MinShareOfBody = 0.2;

    static readonly ConditionalWeakTable<CourseModel, IReadOnlyList<WalledWater>> Cache = new();

    /// <summary>Every walled water body on the course (cached per course).</summary>
    public static IReadOnlyList<WalledWater> Find(CourseModel course) => Cache.GetValue(course, Compute);

    /// <summary>The water planes that belong to a walled body: the body is drawn and played from its outline instead.</summary>
    public static HashSet<int> ClaimedPlanes(CourseModel course) => Find(course).SelectMany(w => w.PlaneIndices).ToHashSet();

    /// <summary>The water-tool bodies (positions in <see cref="CourseModel.WaterPaths"/>) that belong to a walled body.</summary>
    public static HashSet<int> ClaimedPaths(CourseModel course) => Find(course).SelectMany(w => w.PathIndices).ToHashSet();

    /// <summary>The bunker splines that are walls of water, not bunkers.</summary>
    public static HashSet<int> WallSplines(CourseModel course) => Find(course).Select(w => w.SplineIndex).ToHashSet();

    static IReadOnlyList<WalledWater> Compute(CourseModel course)
    {
        var planes = course.Water.Where(p => p.HalfX > 0 && p.HalfZ > 0 && double.IsFinite(p.Level)).ToList();
        var bodies = course.WaterPaths.Select((p, i) => (Path: p, Index: i))
            .Where(x => x.Path.IsBody && x.Path.Level is double l && double.IsFinite(l))
            .Select(x => (x.Index, Level: x.Path.Level!.Value, Outline: Geometry.SampleSpline(x.Path.Path, 8)))
            .Where(x => x.Outline.Count >= 3)
            .Select(x => (x.Index, x.Level, x.Outline, Area: Math.Abs(Geometry.Area(x.Outline))))
            .ToList();
        if (planes.Count < MinPlanes && bodies.Count == 0) return [];

        var found = new List<WalledWater>();
        var takenPlanes = new HashSet<int>();
        var takenBodies = new HashSet<int>();
        foreach (var s in course.SurfaceSplines)
        {
            if (s.Surface != Surfaces.Bunker || !s.Closed || s.Points.Count < 3) continue;
            var outline = Geometry.SampleSpline(s, 12);
            if (outline.Count < 3) continue;
            double area = Math.Abs(Geometry.Area(outline));
            double perimeter = outline.Zip(outline.Skip(1).Append(outline[0]), Vec2.Distance).Sum();
            double compactness = perimeter > 0 ? 4 * Math.PI * area / (perimeter * perimeter) : 0;
            bool Near(List<Vec2> poly, Vec2 p) => Geometry.Contains(poly, p) || Geometry.DistanceToEdge(poly, p) <= PlaneReachMetres;

            // Water planes laid along the outline.
            double x0 = outline.Min(p => p.X) - PlaneReachMetres, x1 = outline.Max(p => p.X) + PlaneReachMetres;
            double z0 = outline.Min(p => p.Z) - PlaneReachMetres, z1 = outline.Max(p => p.Z) + PlaneReachMetres;
            var on = planes.Where(p => !takenPlanes.Contains(p.Index) && p.Position.X >= x0 && p.Position.X <= x1 && p.Position.Z >= z0 && p.Position.Z <= z1
                    && Near(outline, p.Position)).ToList();
            if (on.Count >= MinPlanes)
            {
                // One body of water: the level most of its planes share.
                var members = on.GroupBy(p => Math.Round(p.Level / LevelTolerance)).MaxBy(g => g.Count())!.ToList();
                if (members.Count >= MinPlanes)
                {
                    found.Add(new WalledWater(s.Index, outline, members[0].Level, members.Select(p => p.Index).ToList(), compactness));
                    takenPlanes.UnionWith(members.Select(p => p.Index));
                    continue;
                }
            }

            // A body of water drawn round the outline: it holds the outline, and the outline is a fair share of it.
            foreach (var b in bodies)
            {
                if (takenBodies.Contains(b.Index) || area < MinShareOfBody * b.Area) continue;
                if (outline.Count(p => Near(b.Outline, p)) < BodyHoldsShare * outline.Count) continue;
                found.Add(new WalledWater(s.Index, outline, b.Level, [], compactness) { PathIndices = [b.Index] });
                takenBodies.Add(b.Index);
                break;
            }
        }
        return found;
    }
}
