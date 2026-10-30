using System.Text.Json;

namespace Pga2K25.CourseFormat;

/// <summary>
/// Where a course is, real or invented: what the course file can't say. Saved as Place.json in the course's output
/// folder beside Notes.json, written by the Studio Place card and read by every product. Blank fields print nothing.
/// </summary>
public sealed class CoursePlace
{
    /// <summary>Up to three place lines, broadest last, e.g. "Olkhon Island", "Lake Baikal".</summary>
    public List<string> Lines { get; set; } = [];

    /// <summary>One-line description for the course cards, e.g. "Cliff-top links on a great lake (fictional)".</summary>
    public string Description { get; set; } = "";

    /// <summary>"WEST", "NORTH-EAST", … or "none". Null detects it from the sea on the plot.</summary>
    public string? SeaSide { get; set; }

    /// <summary>Prevailing wind blows FROM this bearing (degrees from north). The course file stores no wind.</summary>
    public int? WindFromDegrees { get; set; }

    /// <summary>
    /// Locator kind: "none"; "site" for a small map of the plot's own shore and the course on it; "region" for an
    /// invented landscape round the course (with the site inset) grown from <see cref="Shape"/> and <see cref="Seed"/>.
    /// </summary>
    public string Coastline { get; set; } = "none";

    /// <summary>Region shape: island, peninsula, bay, lake or inland.</summary>
    public string Shape { get; set; } = "island";

    /// <summary>Region seed: the same seed always draws the same coast.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Roughly how far across the region is, km.</summary>
    public double SizeKm { get; set; } = 25;

    /// <summary>Draw the region faint behind the Plan Sheet's map.</summary>
    public bool Watermark { get; set; } = true;

    /// <summary>Wind rose source: "none", "model" (<see cref="WindClimate"/>) or "data" (the fetched <see cref="WindData"/>).</summary>
    public string WindSource { get; set; } = "none";

    /// <summary>Climate model preset (see <see cref="WindClimate.Presets"/>); its main wind blows from <see cref="WindFromDegrees"/>.</summary>
    public string Climate { get; set; } = "sea";

    /// <summary>Climate model speed scale: 0.7 light, 1 moderate, 1.35 windy.</summary>
    public double WindStrength { get; set; } = 1;

    /// <summary>Climate model lobe tightness, 0.4 (wandering) to 2.2 (steady).</summary>
    public double WindSteadiness { get; set; } = 1;

    public int WindSeed { get; set; } = 1;

    /// <summary>Latitude and longitude of a real place, for fetching its wind record.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>
    /// A real place's rose, binned from a fetched hourly record and kept here so it never needs fetching again
    /// (and keeps working when online data is switched off in Studio).
    /// </summary>
    public WindRose? WindData { get; set; }

    /// <summary>
    /// The rose the products draw, or null for none. "data" without a saved record falls back to the climate model.
    /// </summary>
    /// <param name="seaBearing">The plot's sea bearing, for the sea-breeze climate.</param>
    public WindRose? Rose(int? seaBearing)
    {
        if (WindSource.Equals("data", StringComparison.OrdinalIgnoreCase) && WindData is { Sectors.Count: 16 } data) return data;
        if (WindSource.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        return WindClimate.Generate(Climate, WindFromDegrees ?? 270, seaBearing, WindStrength, WindSteadiness, WindSeed);
    }

    /// <summary>The products draw the site locator on its own.</summary>
    public bool ShowsSite => Coastline.Equals("site", StringComparison.OrdinalIgnoreCase);

    /// <summary>The products draw the generated region (which carries the site inset).</summary>
    public bool ShowsRegion => Coastline.Equals("region", StringComparison.OrdinalIgnoreCase);

    /// <summary>The place lines that have text, trimmed.</summary>
    public IReadOnlyList<string> PlaceLines => Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>The place lines on one line, e.g. "Olkhon Island, Lake Baikal".</summary>
    public string Line => string.Join(", ", PlaceLines);
}

public static class CoursePlaces
{
    public const string FileName = "Place.json";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathFor(string outputDir) => Path.Combine(outputDir, FileName);

    /// <summary>The course's place, or an empty one when there's no file (or it can't be read).</summary>
    public static CoursePlace Load(string outputDir)
    {
        string path = PathFor(outputDir);
        if (!File.Exists(path)) return new CoursePlace();
        try { return JsonSerializer.Deserialize<CoursePlace>(File.ReadAllText(path), Json) ?? new CoursePlace(); }
        catch (JsonException) { return new CoursePlace(); }
    }

    public static void Save(string outputDir, CoursePlace place)
    {
        Directory.CreateDirectory(outputDir);
        // Write beside and swap, so a crash mid-write can't lose the place.
        string path = PathFor(outputDir), tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(place, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
