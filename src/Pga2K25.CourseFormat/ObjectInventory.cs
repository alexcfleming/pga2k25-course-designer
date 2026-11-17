namespace Pga2K25.CourseFormat;

/// <summary>
/// Everything placed on the course, sorted into kinds by where its asset lives (<c>Assets/CourseProps/Buildings/…</c>,
/// <c>Assets/Foliage/…/Trees/…</c> and so on). Placed items count one each; scatter groups count their copies; planted
/// areas count once (how many plants the game puts in them is an estimate, see <see cref="PlacedArea"/>); walls,
/// fences and other object paths count once each.
/// </summary>
public static class ObjectInventory
{
    /// <summary>The kinds, in the order they're listed.</summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "Trees", "Bushes", "Grass", "Plants and flowers", "Rocks", "Walls and fences", "Buildings", "Bridges", "Signs",
        "Furniture", "Equipment", "Vehicles", "Wildlife", "Seasonal props", "Crowd and marshals", "Water effects",
        "Pot bunkers", "Cameras", "Mini putt", "Other",
    ];

    /// <summary>The kind an asset path belongs to.</summary>
    public static string KindOf(string assetPath)
    {
        string p = assetPath.Replace('\\', '/');
        bool Has(string part) => p.Contains(part, StringComparison.OrdinalIgnoreCase);
        if (Has("PotBunker")) return "Pot bunkers";
        if (Has("CameraPlaceholder")) return "Cameras";
        if (Has("/Trees/")) return "Trees";
        if (Has("/Bushes/")) return "Bushes";
        if (Has("/Grass/")) return "Grass";
        if (Has("/Plants/") || Has("/Flowers/")) return "Plants and flowers";
        if (Has("/Rocks/") || Has("/Stones/")) return "Rocks";
        if (Has("/Walls/") || Has("Fence")) return "Walls and fences";
        if (Has("/Buildings/") || Has("/ModularPacks/")) return "Buildings";
        if (Has("/Bridges/")) return "Bridges";
        if (Has("/Signs/")) return "Signs";
        if (Has("/Furniture/")) return "Furniture";
        if (Has("/Equipment/")) return "Equipment";
        if (Has("/Vehicles/")) return "Vehicles";
        if (Has("/Wildlife/")) return "Wildlife";
        if (Has("/Seasonal/")) return "Seasonal props";
        if (Has("/Crowd/") || Has("Spectator")) return "Crowd and marshals";
        if (Has("Ocean") || Has("Waterfall") || Has("River") || Has("/Effects/")) return "Water effects";
        if (Has("/MiniPutt/")) return "Mini putt";
        return "Other";
    }

    /// <summary>How many of each kind, in <see cref="Kinds"/> order, leaving out kinds with none.</summary>
    public static IReadOnlyList<(string Kind, int Count)> Count(CourseModel course)
    {
        var counts = new Dictionary<string, int>();
        void Add(string path, int n) { string k = KindOf(path); counts[k] = counts.GetValueOrDefault(k) + n; }
        foreach (var o in course.PlacedObjects) Add(o.AssetPath, 1);
        foreach (var c in course.PlacedClusters) Add(c.AssetPath, c.Count);
        foreach (var a in course.PlacedAreas) Add(a.AssetPath, 1);
        foreach (var p in course.ObjectPaths) Add(p.AssetPath, 1);
        return Kinds.Where(counts.ContainsKey).Select(k => (k, counts[k])).ToList();
    }

    /// <summary>
    /// Pot bunkers placed as objects, as discs: their footprint at scale 1 is taken as 2.5 m across the radius, which is
    /// an estimate (no course seen yet places one).
    /// </summary>
    public static IEnumerable<(Vec2 Centre, double Radius)> PotBunkers(CourseModel course) =>
        course.PlacedObjects.Where(o => KindOf(o.AssetPath) == "Pot bunkers").Select(o => (o.Position, PotBunkerRadiusMetres * Math.Max(0.2, o.Scale)));

    public const double PotBunkerRadiusMetres = 2.5;
}
