using Pga2K25.CourseFormat;

namespace Pga2K25.TerrainMap;

/// <summary>
/// A rough estimate of the course designer's two budget counters, which the file doesn't store: the object counter
/// (how many things are in the plot, 0–100 %) and the memory counter (driven mostly by how many different things
/// there are). The game works both out when it loads the course; this fits them from what the file holds.
/// </summary>
/// <remarks>
/// Objects are fitted to six in-game readings (object % / memory %): Sagan-Khushun 72.5 / 62.7, Wakinyan Dunes
/// 0.7 / 14.3, Blackwolf Lenz Upper 6.3 / 46.9, Murray Estate 11.7 / 28.1 (2026-10-26), and two of Plot Starter's
/// generated plots with nothing placed (2026-11-08): PS2 Dunes Layered, 2,455 stamps, 0.2 / 16.8, and PS4 Chop Hills,
/// 19,086 stamps, 1.9 / 12.6. Those two showed terrain stamps count as objects; all six fit within 0.11 points. Memory
/// is still the 2026-10-26 fit: it matches those four within a point, but the two bare plots read 16.8 % and 12.6 %
/// against about 11.5 and 14.6, the other way round from anything in the file (the second had its theme and backdrop
/// changed), so something the file doesn't show adds to memory. Treat both as rough.
/// </remarks>
public sealed record GameBudget(int UniqueAssets, double Objects, double ObjectPercent, double MemoryPercent, double? FilePercent = null)
{
    /// <summary>Objects at 100 % on the object counter.</summary>
    public const double ObjectLimit = 119_350;
    /// <summary>Objects per metre of wall, fence or other object path (its pieces).</summary>
    public const double PerPathMetre = 2.49;
    /// <summary>Objects each hole brings with it (tee markers, pin and the like).</summary>
    public const double PerHole = 8.14;
    /// <summary>Objects per terrain stamp (height[] entry): Plot Starter's bare plots read 0.2 % at 2,455 and 1.9 % at 19,086.</summary>
    public const double PerStamp = 0.118;
    /// <summary>
    /// Course description size (unpacked JSON bytes) at 100 % on the file size counter: 7.29 MB read 1.9 % and 0.94 MB
    /// 0.2 % on Plot Starter's two bare plots. Two readings, both terrain only, so a rough guide.
    /// </summary>
    public const double FileLimitBytes = 384e6;
    /// <summary>
    /// Planted areas hold this many times the area × <c>fillPct</c> reading: about 0.25 trees per m² for Murray's
    /// 0.0075. (The planting overlay still uses the plain reading; neither has been checked by counting in game.)
    /// </summary>
    public const double AreaFillScale = 33.5;
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
            + course.Holes.Count * PerHole
            + course.HeightStamps.Count * PerStamp;
        double memory = MemoryBase + MemoryPerAsset * unique + MemoryPerRootObject * Math.Sqrt(objects);
        double? file = course.DescriptionBytes > 0 ? course.DescriptionBytes / FileLimitBytes * 100 : null;
        return new GameBudget(unique, objects, objects / ObjectLimit * 100, memory, file);
    }
}
