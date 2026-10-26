using System.Text.RegularExpressions;

namespace Pga2K25.CourseFormat;

public enum PlantKind { Tree, Bush, Grass, Flower }

/// <summary>Trees are drawn darker for conifers than for broadleaf trees, so the two read apart in black and white too.</summary>
public enum TreeForm { Broadleaf, Conifer }

/// <summary>An asset's kind and species, from the folders in its path ("…/SpeedTree8/Trees/Birch_River/Prefabs/…").</summary>
public sealed record PlantSpecies(string Name, PlantKind Kind, TreeForm Form)
{
    static readonly string[] Conifers = ["Pine", "Larch", "Cedar", "Spruce", "Fir", "Cypress", "Juniper", "Hemlock", "Redwood", "Sequoia", "Yew"];

    /// <summary>The plant an asset path names, or null for anything that isn't vegetation (props, rocks, walls).</summary>
    public static PlantSpecies? Of(string assetPath)
    {
        var parts = assetPath.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            PlantKind? kind = parts[i] switch
            {
                "Trees" => PlantKind.Tree,
                "Bushes" => PlantKind.Bush,
                "Grass" => PlantKind.Grass,
                "Plants" => PlantKind.Flower,
                _ => null,
            };
            // Only foliage and the course generator's detail plants: "Grass" elsewhere could be anything.
            if (kind is not { } k || !(assetPath.Contains("/Foliage/") || assetPath.Contains("/CourseGen/Detail/"))) continue;
            string folder = i + 1 < parts.Length - 1 ? parts[i + 1] : "";
            string name = folder is "" or "Prefabs" ? KindName(k) : Readable(folder);
            var form = k == PlantKind.Tree && Conifers.Any(c => name.Contains(c, StringComparison.OrdinalIgnoreCase)) ? TreeForm.Conifer : TreeForm.Broadleaf;
            return new PlantSpecies(name, k, form);
        }
        return null;
    }

    static string KindName(PlantKind k) => k switch { PlantKind.Tree => "Trees", PlantKind.Bush => "Bushes", PlantKind.Grass => "Grass", _ => "Flowers" };

    /// <summary>"Birch_River" → "Birch River", "AfghanBushes" → "Afghan Bushes".</summary>
    public static string Readable(string folder) =>
        Regex.Replace(folder.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim();
}
