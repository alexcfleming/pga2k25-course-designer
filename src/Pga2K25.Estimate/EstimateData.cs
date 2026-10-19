using System.Text.Json;
using System.Text.Json.Serialization;
using Pga2K25.CourseFormat;

namespace Pga2K25.Estimate;

/// <summary>Unit conversions. The engine works in world metres; reports use feet for heights, yards for distances.</summary>
public static class Units
{
    public const double FeetPerMetre = 3.280839895;
    public const double YardsPerMetre = 1.0936132983;
    public const double CubicYardsPerCubicMetre = 1.3079506193;
    public const double SquareFeetPerSquareMetre = 10.7639104167;
    public const double SquareMetresPerAcre = 4046.8564224;
}

/// <summary>Where a quantity comes from: read off the course file, worked out from assumptions, or assumed outright.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Provenance>))]
public enum Provenance { Measured, Computed, Assumed }

[JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
public enum Confidence { Low, Medium, High }

/// <summary>One site-condition class (zone-presets.json). All values are placeholders until Alex sets them.</summary>
public sealed record ZonePreset
{
    public double NaturalFidelity { get; init; } = 1;
    /// <summary>Landforms wider than this count as natural; 0 = the baseline is a best-fit plane.</summary>
    public double BaselineWavelengthYd { get; init; }
    public double ClearingPct { get; init; }
    public double RockFactor { get; init; } = 1;
    public double DrainageNeed { get; init; } = 1;
    public double ImportExportBias { get; init; }
    public string Source { get; init; } = "PLACEHOLDER";
    public string Notes { get; init; } = "";
}

public sealed record ZoneDefaults
{
    /// <summary>Class for ground inside play splines (fairway, rough, greens, tees, bunkers, waste, paths).</summary>
    public string ManufacturedClass { get; init; } = "moderate_earthmoving";
    /// <summary>Class for everything else: natural ground with no earthwork.</summary>
    public string NaturalClass { get; init; } = "natural_keep";
    /// <summary>Width over which a worked zone blends into the natural ground at its edge.</summary>
    public double FeatherYd { get; init; } = 10;
    /// <summary>Tees are points in the course file, so each gets an assumed round pad of this radius.</summary>
    public double TeePadRadiusYd { get; init; } = 8;
    /// <summary>Volume lost when cut is compacted as fill (bank → compacted).</summary>
    public double Shrink { get; init; } = 0.10;
    /// <summary>Volume gained when cut is dug and loaded (bank → loose), for haul-off.</summary>
    public double Swell { get; init; } = 0.25;
    public string Source { get; init; } = "PLACEHOLDER";
}

public sealed record ZonePresetFile
{
    public string Note { get; init; } = "";
    public ZoneDefaults Defaults { get; init; } = new();
    public Dictionary<string, ZonePreset> Presets { get; init; } = new();

    public ZonePreset Get(string name) =>
        Presets.TryGetValue(name, out var p) ? p : throw new KeyNotFoundException($"Unknown zone class '{name}'.");
}

public sealed record UnitCost
{
    public string Label { get; init; } = "";
    public string Unit { get; init; } = "";
    public double? Price { get; init; }
    public double? Low { get; init; }
    public double? High { get; init; }
    public int? Year { get; init; }
    /// <summary>placeholder, researched or derived. Placeholders stay labelled PLACEHOLDER in every report.</summary>
    public string Status { get; init; } = "placeholder";
    public string? Grade { get; init; }
    public string Source { get; init; } = "";

    public bool IsPlaceholder => Status.Equals("placeholder", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One project-level setting (project-defaults.json): a fraction, factor or amount, with where it came from.</summary>
public sealed record Setting
{
    public double Value { get; init; }
    public string Status { get; init; } = "placeholder";
    public string Source { get; init; } = "";

    public bool IsPlaceholder => Status.Equals("placeholder", StringComparison.OrdinalIgnoreCase);
}

public sealed record UnitCostFile
{
    public string Note { get; init; } = "";
    public Dictionary<string, UnitCost> Items { get; init; } = new();

    public UnitCost Get(string key) =>
        Items.TryGetValue(key, out var c) ? c : throw new KeyNotFoundException($"Unknown cost item '{key}'.");
}

public sealed record Rule
{
    public double Value { get; init; }
    public string Source { get; init; } = "PLACEHOLDER";
}

/// <summary>Rates for quantities the course file can't measure (quantity-rules.json); all are assumptions.</summary>
public sealed record QuantityRules
{
    public string Note { get; init; } = "";
    public Rule GreenSurroundSodYd { get; init; } = new() { Value = 4 };
    public Rule DrainageLfPerAcre { get; init; } = new() { Value = 140 };
    public Rule HeadsPerAcre { get; init; } = new() { Value = 8 };
    public Rule PathFtPerRouteFt { get; init; } = new() { Value = 1 };
    public Rule PracticeGreenYd { get; init; } = new() { Value = 45 };
}

/// <summary>A drawn area with its own class; later zones win where they overlap, and all win over the spline default.</summary>
public sealed record ZonePolygon
{
    public string Class { get; init; } = "";
    /// <summary>World X/Z in metres, as the course file stores them.</summary>
    public List<double[]> Polygon { get; init; } = [];
    public string Note { get; init; } = "";

    public List<Vec2> Points() => Polygon.Where(p => p.Length >= 2).Select(p => new Vec2(p[0], p[1])).ToList();
}

public sealed record HoleOverride
{
    /// <summary>Class for this hole's play splines in place of the default manufactured class.</summary>
    public string? ZoneClass { get; init; }
}

/// <summary>
/// Per-course choices (<c>Estimate.json</c> beside the products). Never holds pars, yardages or names:
/// those come from the course file.
/// </summary>
public sealed record EstimateInput
{
    public Dictionary<string, HoleOverride> Holes { get; init; } = new();
    public List<ZonePolygon> Zones { get; init; } = [];
    /// <summary>Added to every zone's natural fidelity (clamped 0..1): the sensitivity dial.</summary>
    public double FidelityOffset { get; init; }
    /// <summary>Overrides for project-defaults.json values, by key (e.g. regional_factor).</summary>
    public Dictionary<string, double> Project { get; init; } = new();
    /// <summary>Quantity overrides for course-wide items, by unit-cost key (e.g. practice_area: 0 when there is none).</summary>
    public Dictionary<string, double> CourseWide { get; init; } = new();

    public HoleOverride ForHole(int n) => Holes.TryGetValue(n.ToString(), out var h) ? h : new();
}

public static class EstimateData
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The data files shipped beside the assembly (assets/estimate).</summary>
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "estimate");

    public static ZonePresetFile LoadPresets(string? path = null) =>
        Load<ZonePresetFile>(path ?? Path.Combine(DataDir, "zone-presets.json"));

    /// <summary>
    /// Where Alex's own values live, so they survive app updates: unit-price overrides shared by all courses
    /// (<c>unit-costs.json</c>). Settable so tests can point it elsewhere.
    /// </summary>
    public static string UserDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "estimate");

    public static string UserCostsPath => Path.Combine(UserDir, "unit-costs.json");

    /// <summary>
    /// Shipped unit costs, with the user's overrides from <paramref name="userDir"/> laid over them item by item when
    /// one is given (label and unit always come from the shipped file, so an override can't change what is priced).
    /// </summary>
    public static UnitCostFile LoadUnitCosts(string? path = null, string? userDir = null)
    {
        var shipped = Load<UnitCostFile>(path ?? Path.Combine(DataDir, "unit-costs.json"));
        if (userDir is null) return shipped;
        var overrides = LoadUserCosts(Path.Combine(userDir, "unit-costs.json"));
        var items = new Dictionary<string, UnitCost>(shipped.Items);
        foreach (var (key, mine) in overrides)
            if (items.TryGetValue(key, out var baseCost))
                items[key] = mine with { Label = baseCost.Label, Unit = baseCost.Unit };
        return shipped with { Items = items };
    }

    /// <summary>The user's unit-cost overrides by key (empty when there are none).</summary>
    public static Dictionary<string, UnitCost> LoadUserCosts(string? path = null)
    {
        path ??= UserCostsPath;
        return File.Exists(path) ? Load<Dictionary<string, UnitCost>>(path) : new();
    }

    /// <summary>Writes the user's overrides; an empty set deletes the file so the shipped prices apply again.</summary>
    public static void SaveUserCosts(IReadOnlyDictionary<string, UnitCost> overrides, string? path = null)
    {
        path ??= UserCostsPath;
        if (overrides.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(overrides, Json));
    }

    /// <summary>Writes a course's Estimate.json (drawn zones and anything else in it are kept as given).</summary>
    public static void SaveInput(string path, EstimateInput input)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(input, Json));
    }

    /// <summary>Project defaults with a course's overrides applied (an override's status becomes "Estimate.json").</summary>
    public static Dictionary<string, Setting> LoadProject(EstimateInput? input = null, string? path = null)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path ?? Path.Combine(DataDir, "project-defaults.json")),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        var result = new Dictionary<string, Setting>();
        foreach (var (key, node) in json)
            if (node is System.Text.Json.Nodes.JsonObject)
                result[key] = node.Deserialize<Setting>(Json)!;
        foreach (var (key, value) in input?.Project ?? [])
        {
            if (!result.ContainsKey(key)) throw new KeyNotFoundException($"Estimate.json sets unknown project value '{key}'.");
            result[key] = new Setting { Value = value, Status = "Estimate.json", Source = "set for this course" };
        }
        return result;
    }

    public static ScenarioFile LoadScenarios(string? path = null) =>
        Load<ScenarioFile>(path ?? Path.Combine(DataDir, "scenarios.json"));

    public static QuantityRules LoadRules(string? path = null) =>
        Load<QuantityRules>(path ?? Path.Combine(DataDir, "quantity-rules.json"));

    public static EstimateInput LoadInput(string? path) =>
        path is not null && File.Exists(path) ? Load<EstimateInput>(path) : new EstimateInput();

    static T Load<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"Empty file {path}");
}
