using System.Text;
using System.Text.Json.Nodes;

namespace Pga2K25.CourseFormat;

/// <summary>
/// One slot of the designer's surface material table: the material chosen for it, or null for the theme's default
/// (the file names only what the designer changed), and its blend.
/// </summary>
public sealed record SurfaceMaterial(string? Name, double Blend)
{
    public bool IsThemeDefault => string.IsNullOrEmpty(Name);

    /// <summary>"Fairway43_RenaissanceClub" → "Fairway 43"; null for the theme default.</summary>
    public string? Material => Name is null ? null : CourseSurfaces.Split(Name).Material;

    /// <summary>"Fairway43_RenaissanceClub" → "Renaissance Club" (the course the material comes from); null when it has none.</summary>
    public string? Source => Name is null ? null : CourseSurfaces.Split(Name).Source;
}

/// <summary>
/// The look of the ground as the designer set it, read from the course file: theme and backdrop, the material of each
/// Create Surfaces slot (bunker, the five path slots, green, fringe, fairway and the roughs), the four Terrain
/// Painting layers, and Surface Weathering (the material each grass turns into where weathering is painted, its edge
/// blending, and how much is painted). Matched to the designer's own lists on 12 Nov 2026 (Halvoy's Mulch in slot 3,
/// Murray Estate's edge blending 85).
/// </summary>
public sealed class CourseSurfaces
{
    /// <summary>Slots of <c>surfaces2</c> and <c>secondarySurfaces</c>, in the designer's Create Surfaces order.</summary>
    public const int Bunker = 0, Path1 = 1, Path2 = 2, Path3 = 3, Path4 = 4, Path5 = 5, Green = 6, Fringe = 7, Fairway = 8,
        LightRough = 9, HeavyRough = 10, Topsoil = 11, SubsoilA = 12, SubsoilB = 13, Bedrock = 14, SlotCount = 15;

    /// <summary>The Terrain Painting layers' slots and the paint surface each layer's strokes are saved under.</summary>
    public static readonly IReadOnlyList<(int Slot, int PaintSurface, string Name)> PaintingLayers =
    [
        (Topsoil, 14, "Grass / topsoil"), (SubsoilA, 15, "Subsoil A"), (SubsoilB, 16, "Subsoil B"), (Bedrock, 17, "Bedrock"),
    ];

    /// <summary>The paint surface Surface Weathering strokes are saved under.</summary>
    public const int WeatheringPaintSurface = 18;

    /// <summary>The <c>theme</c> number (Desert, Boreal, Tropical… in the designer; the file stores only the number).</summary>
    public int Theme { get; init; } = -1;

    /// <summary>The <c>backdrop</c> number, 0–5: one of the theme's six backdrop pictures (they differ from theme to theme).</summary>
    public int Backdrop { get; init; } = -1;

    /// <summary>The Create Surfaces and Terrain Painting materials (<c>surfaces2</c>), <see cref="SlotCount"/> slots.</summary>
    public IReadOnlyList<SurfaceMaterial> Materials { get; init; } = Empty();

    /// <summary>What each grass becomes where Surface Weathering is painted (<c>secondarySurfaces</c>; green, fringe, fairway and roughs).</summary>
    public IReadOnlyList<SurfaceMaterial> Weathered { get; init; } = Empty();

    /// <summary>Surface Weathering's Edge Blending, 0–1 (the designer shows it ×100); null when the file doesn't say.</summary>
    public double? WeatheringEdgeBlend { get; init; }

    static List<SurfaceMaterial> Empty() => Enumerable.Range(0, SlotCount).Select(_ => new SurfaceMaterial(null, 0)).ToList();

    public static CourseSurfaces From(JsonObject desc) => new()
    {
        Theme = desc["theme"] is JsonValue t && t.TryGetValue(out double th) ? (int)th : -1,
        Backdrop = desc["backdrop"] is JsonValue b && b.TryGetValue(out double bd) ? (int)bd : -1,
        Materials = Slots(desc["surfaces2"] as JsonArray),
        Weathered = Slots(desc["secondarySurfaces"] as JsonArray),
        WeatheringEdgeBlend = desc["grassWeatheringEdgeBlend"] is JsonValue e && e.TryGetValue(out double eb) && eb >= 0 ? eb : null,
    };

    static List<SurfaceMaterial> Slots(JsonArray? array)
    {
        var list = Empty();
        if (array is null) return list;
        for (int i = 0; i < Math.Min(SlotCount, array.Count); i++)
        {
            if (array[i] is not JsonObject o) continue;
            string? name = o["name"] is JsonValue n && n.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;
            double blend = o["blend"] is JsonValue bv && bv.TryGetValue(out double d) ? d : 0;
            list[i] = new SurfaceMaterial(name, blend);
        }
        return list;
    }

    /// <summary>
    /// The designer's theme names by number, as far as they've been matched (the designer lists Desert, Boreal,
    /// Tropical, Countryside, Harvest, Winter, Delta, Rustic, Swiss, Steppe, Autumn, Highlands, Temperate and
    /// Fjordlands). Unmatched numbers show as "Theme n". Matched by Alex, 12 Nov 2026.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> ThemeNames = new Dictionary<int, string>
    {
        [10] = "Delta", [15] = "Highlands", [50] = "Tropical", [54] = "Fjordlands",
    };

    public string ThemeLabel => Theme < 0 ? "No theme" : ThemeNames.TryGetValue(Theme, out var n) ? n : $"Theme {Theme}";

    /// <summary>"Backdrop 6": the backdrop's place among the designer's six pictures.</summary>
    public string BackdropLabel => Backdrop < 0 ? "" : $"Backdrop {Backdrop + 1}";

    /// <summary>
    /// "Fairway43_RenaissanceClub" → ("Fairway 43", "Renaissance Club"); "Splat1_TPC_Sawgrass" → ("Splat 1", "TPC
    /// Sawgrass"); "HeavyRough38_PaynesValley" → ("Heavy Rough 38", "Paynes Valley"); "Gravel05" → ("Gravel 05", null).
    /// </summary>
    public static (string Material, string? Source) Split(string name)
    {
        int cut = name.IndexOf('_');
        string head = cut < 0 ? name : name[..cut];
        string? tail = cut < 0 ? null : name[(cut + 1)..];
        return (Words(head), string.IsNullOrEmpty(tail) ? null : string.Join(" ", tail.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(Words)));
    }

    /// <summary>"LightRough27" → "Light Rough 27"; "FriscoGC" → "Frisco GC"; "TPC" stays "TPC".</summary>
    static string Words(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i], prev = i > 0 ? s[i - 1] : ' ';
            bool nextLower = i + 1 < s.Length && char.IsLower(s[i + 1]);
            bool boundary = i > 0 && (
                (char.IsUpper(c) && (char.IsLower(prev) || (char.IsUpper(prev) && nextLower))) ||
                (char.IsDigit(c) && !char.IsDigit(prev)) ||
                (char.IsLetter(c) && char.IsDigit(prev)));
            if (boundary) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
