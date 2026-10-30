using System.Text.Json;
using System.Text.Json.Serialization;
using Pga2K25.CourseFormat;

namespace Pga2K25.CourseCard;

/// <summary>Per-course palette. The default is Cassimbo's "Option A — high separation".</summary>
public sealed record CardPalette
{
    public string Par3 { get; init; } = "#5F7D8C";   // Fog Slate
    public string Par4 { get; init; } = "#C08A2E";   // Sand Ochre
    public string Par5 { get; init; } = "#9E3B28";   // Oxide Red
    public string Ink { get; init; } = "#262A2C";
    public string Salt { get; init; } = "#F2F0EC";   // page
    public string Fog { get; init; } = "#B9C3C7";    // rules, grid
}

/// <summary>
/// Per-course presentation settings (JSON). Nothing here is hole data: pars, yardages and bearings always come
/// from the course file. Unset values are derived (title from the course name, ocean side from the terrain) or left
/// off the card (no wind arrow without a wind bearing, no logo without a file).
/// </summary>
public sealed record CourseCardConfig
{
    public string? Title { get; init; }

    /// <summary>Short description for the hole data card subtitle, e.g. "Arid plateau headland, Angola coast (fictional)".</summary>
    public string? Description { get; init; }

    public CardPalette Palette { get; init; } = new();

    /// <summary>Tee set names, shortest → longest; otherwise from the workbook, else "Back" / "Tee n".</summary>
    public IReadOnlyList<string>? TeeNames { get; init; }

    /// <summary>"WEST", "NORTH-EAST", … or "none". Null detects it from the sea on the plot.</summary>
    public string? OceanSide { get; init; }

    /// <summary>Text set along the ocean margin of the rose, e.g. "OCEAN & CACIMBO FOG".</summary>
    public string OceanLabel { get; init; } = "OCEAN";

    /// <summary>Prevailing wind blows FROM this bearing. The course file stores no wind, so null draws no arrow.</summary>
    public int? WindFromDegrees { get; init; }

    /// <summary>Optional note after the wind direction, e.g. "onshore".</summary>
    public string? WindNote { get; init; }

    /// <summary>
    /// Write the hole data card's Hazards and Green Contours &amp; Hole Positions from the course file
    /// (<see cref="HoleNotes"/>) wherever the workbook leaves them blank.
    /// </summary>
    public bool AutoHoleNotes { get; init; } = true;

    /// <summary>Optional square logo (PNG/JPG) for the card headers.</summary>
    public string? LogoPath { get; init; }

    /// <summary>Outer ring of the rose (yards); null rounds the longest hole up to the ring step.</summary>
    public int? RoseMaxYards { get; init; }
    public int RingStepYards { get; init; } = 100;
    public int SpokeStepDegrees { get; init; } = 30;

    public double Dpi { get; init; } = 300;

    /// <summary>Any of "png", "pdf", "svg".</summary>
    public IReadOnlyList<string> Formats { get; init; } = ["png", "pdf"];

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static CourseCardConfig Load(string path) =>
        JsonSerializer.Deserialize<CourseCardConfig>(File.ReadAllText(path), Json) ?? new();

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static string CompassName(double bearing)
    {
        string[] names = ["NORTH", "NORTH-EAST", "EAST", "SOUTH-EAST", "SOUTH", "SOUTH-WEST", "WEST", "NORTH-WEST"];
        return names[(int)Math.Round(((bearing % 360) + 360) % 360 / 45) % 8];
    }

    public static string CompassAbbreviation(double bearing)
    {
        string[] names = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];
        return names[(int)Math.Round(((bearing % 360) + 360) % 360 / 22.5) % 16];
    }
}
