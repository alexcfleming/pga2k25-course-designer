using Pga2K25.CourseFormat;

namespace Pga2K25.TerrainMap;

/// <summary>The planting layers a map can show; any mix.</summary>
[Flags]
public enum PlantingLayers
{
    None = 0,
    Trees = 1,
    Bushes = 2,
    Grass = 4,
    Flowers = 8,
    Walls = 16,
    All = Trees | Bushes | Grass | Flowers | Walls,
}

/// <summary>
/// Everything the designer planted, from the course file: single trees and plants, scatter groups and planted
/// areas, plus walls and fences. Vegetation the game generates by itself isn't in the file and isn't shown, and the
/// tree-clearing layers (which only remove that) are ignored.
/// </summary>
public sealed class Planting
{
    /// <summary>Canopy radius (m) of a tree at scale 1, as on the illustrated scorecard map.</summary>
    public const double TreeRadiusMetres = 4.0;

    public sealed record Single(Vec2 At, double Scale, PlantSpecies Species)
    {
        public double CanopyMetres => TreeRadiusMetres * Math.Max(0.5, Scale);
    }

    public sealed record Group(PlacedCluster Cluster, PlantSpecies Species);
    public sealed record Area(PlacedArea Placed, PlantSpecies Species);
    public sealed record Wall(IReadOnlyList<Vec2> Points, bool Fence);

    public required IReadOnlyList<Single> Singles { get; init; }
    public required IReadOnlyList<Group> Groups { get; init; }
    public required IReadOnlyList<Area> Areas { get; init; }
    public required IReadOnlyList<Wall> Walls { get; init; }

    public static Planting From(CourseModel course) => new()
    {
        Singles = course.PlacedObjects.Select(o => PlantSpecies.Of(o.AssetPath) is { } s ? new Single(o.Position, o.Scale, s) : null)
            .OfType<Single>().ToList(),
        Groups = course.PlacedClusters.Select(c => PlantSpecies.Of(c.AssetPath) is { } s ? new Group(c, s) : null).OfType<Group>().ToList(),
        Areas = course.PlacedAreas.Select(a => PlantSpecies.Of(a.AssetPath) is { } s ? new Area(a, s) : null).OfType<Area>().ToList(),
        // Native water is an object path too (GeneratedRiverSetupPrefab), and is drawn as water.
        Walls = course.ObjectPaths.Where(p => p.AssetPath.Contains("/Walls/") && !p.AssetPath.Contains("River", StringComparison.OrdinalIgnoreCase))
            .Select(p => new Wall(p.Points, p.AssetPath.Contains("Fence", StringComparison.OrdinalIgnoreCase))).ToList(),
    };

    public bool IsEmpty => Singles.Count == 0 && Groups.Count == 0 && Areas.Count == 0 && Walls.Count == 0;

    /// <summary>Whether any of <paramref name="layers"/> has something in it.</summary>
    public bool Has(PlantingLayers layers)
    {
        bool On(PlantSpecies s) => (layers & LayerOf(s.Kind)) != 0;
        return Singles.Any(p => On(p.Species)) || Groups.Any(g => On(g.Species)) || Areas.Any(a => On(a.Species))
            || ((layers & PlantingLayers.Walls) != 0 && Walls.Count > 0);
    }

    /// <summary>A kind's count: placed one by one, and about how many more the game scatters in groups and areas.</summary>
    public (int Placed, int Scattered) Count(PlantKind kind) =>
        (Singles.Count(p => p.Species.Kind == kind),
         Groups.Where(g => g.Species.Kind == kind).Sum(g => g.Cluster.Count) + Areas.Where(a => a.Species.Kind == kind).Sum(a => a.Placed.EstimatedCount));

    /// <summary>Tree species, most first: how many, and whether any of them are estimates (groups or areas).</summary>
    public IReadOnlyList<(PlantSpecies Species, int Count, bool Estimated)> TreeSpecies()
    {
        var all = new List<(PlantSpecies Species, int Count, bool Estimated)>();
        all.AddRange(Singles.Where(p => p.Species.Kind == PlantKind.Tree).Select(p => (p.Species, 1, false)));
        all.AddRange(Groups.Where(g => g.Species.Kind == PlantKind.Tree).Select(g => (g.Species, g.Cluster.Count, true)));
        all.AddRange(Areas.Where(a => a.Species.Kind == PlantKind.Tree).Select(a => (a.Species, a.Placed.EstimatedCount, true)));
        return all.GroupBy(t => t.Species.Name)
            .Select(g => (Species: g.First().Species, Count: g.Sum(t => t.Count), Estimated: g.Any(t => t.Estimated)))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Species.Name).ToList();
    }

    /// <summary>The layer a kind belongs to.</summary>
    public static PlantingLayers LayerOf(PlantKind kind) => kind switch
    {
        PlantKind.Tree => PlantingLayers.Trees,
        PlantKind.Bush => PlantingLayers.Bushes,
        PlantKind.Grass => PlantingLayers.Grass,
        _ => PlantingLayers.Flowers,
    };
}
