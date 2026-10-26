using Pga2K25.CourseFormat;

namespace Pga2K25.TerrainMap;

/// <summary>
/// A rough estimate of the course designer's two budget counters, which the file doesn't store: the object counter
/// (how many things are in the plot, 0–100 %) and the memory counter (driven mostly by how many different things
/// there are). The game works both out when it loads the course; this fits them from what the file holds.
/// </summary>
/// <remarks>
/// Calibrated on four in-game readings (2026-10-26, object % / memory %): Sagan-Khushun 72.5 / 62.7, Wakinyan Dunes
/// 0.7 / 14.3, Blackwolf Lenz Upper 6.3 / 46.9, Murray Estate 11.7 / 28.1. Four readings fit the object formula's
/// four constants exactly and the memory formula's three to within 0.9 points, so neither has been checked against a
/// course it wasn't fitted to. Treat both as rough. Readings from more courses (Halvoy for the empty-plot baseline,
/// Berwick Coast for walls) would test them.
/// </remarks>
public sealed record GameBudget(int UniqueAssets, double Objects, double ObjectPercent, double MemoryPercent)
{
    /// <summary>Objects at 100 % on the object counter.</summary>
    public const double ObjectLimit = 119_350;
    /// <summary>Objects per metre of wall, fence or other object path (its pieces).</summary>
    public const double PerPathMetre = 2.445;
    /// <summary>Objects each hole brings with it (tee markers, pin and the like).</summary>
    public const double PerHole = 45.9;
    /// <summary>
    /// Planted areas hold this many times the area × <c>fillPct</c> reading: about 0.24 trees per m² for Murray's
    /// 0.0075. (The planting overlay still uses the plain reading; neither has been checked by counting in game.)
    /// </summary>
    public const double AreaFillScale = 31.9;
    /// <summary>Memory % = base + per different asset × count + per √object × √objects.</summary>
    public const double MemoryBase = 9.761, MemoryPerAsset = 0.315, MemoryPerRootObject = 0.102;

    public static GameBudget Estimate(CourseModel course)
    {
        int unique = course.PlacedObjects.Select(o => o.AssetPath)
            .Concat(course.PlacedClusters.Select(c => c.AssetPath))
            .Concat(course.PlacedAreas.Select(a => a.AssetPath))
            .Concat(course.ObjectPaths.Select(p => p.AssetPath))
            .Distinct().Count();
        double pathMetres = course.ObjectPaths.Sum(p => p.Points.Zip(p.Points.Skip(1), Vec2.Distance).Sum());
        double objects = course.PlacedObjects.Count
            + course.PlacedClusters.Sum(c => (double)c.Count)
            + course.PlacedAreas.Sum(a => a.AreaSquareMetres * a.FillPerSquareMetre * AreaFillScale)
            + pathMetres * PerPathMetre
            + course.Holes.Count * PerHole;
        double memory = MemoryBase + MemoryPerAsset * unique + MemoryPerRootObject * Math.Sqrt(objects);
        return new GameBudget(unique, objects, objects / ObjectLimit * 100, memory);
    }
}
