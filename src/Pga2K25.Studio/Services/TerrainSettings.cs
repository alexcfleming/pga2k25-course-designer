using System.Text.Json.Serialization;
using Pga2K25.TerrainMap;

namespace Pga2K25.Studio.Services;

/// <summary>
/// A course's terrain map options, saved with its other settings. The live view, the terrain map build and the
/// hole sheets all use them; only the printed resolution is for the build alone.
/// </summary>
public sealed class TerrainSettings
{
    public const string Routing = "Routing", WholePlot = "Whole plot", Buildable = "Buildable ground";

    public string Area { get; set; } = Routing;
    public double MarginMetres { get; set; } = 150;
    /// <summary>Contour interval in feet; null picks the smallest of 1 / 2 / 5 / 10 … that fits.</summary>
    public double? IntervalFeet { get; set; }
    public int IndexEvery { get; set; } = 5;
    public double GridYards { get; set; } = 100;
    public string Shading { get; set; } = "Relief";
    public bool ShowSurfaces { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool ShowHilltops { get; set; } = true;
    public bool ShowWater { get; set; } = true;
    /// <summary>Pixels per metre of the built PNG.</summary>
    public double PrintPixelsPerMetre { get; set; } = 4;

    public const string SketchesOff = "Off", SketchesThisHole = "This hole", SketchesAll = "All holes";
    /// <summary>Scanned hole sketches over the live map: off, the selected hole's, or every hole's.</summary>
    public string Sketches { get; set; } = SketchesAll;
    public bool SketchColours { get; set; } = true;
    public double SketchOpacity { get; set; } = 0.9;
    /// <summary>The selected hole's whole scanned sheet instead of just its pencil.</summary>
    public bool SketchScan { get; set; }
    /// <summary>Also the traced construction lines: shot lines through the lettered aim points, aim dots, hatching.</summary>
    public bool SketchConstruction { get; set; }
    /// <summary>The scanned pencil as it is instead of the traced lines.</summary>
    public bool SketchRawPencil { get; set; }
    public bool ShowNotes { get; set; } = true;

    public TerrainMapOptions ToOptions(string credit, double? pixelsPerMetre = null) => new()
    {
        Extent = Area switch { WholePlot => MapExtent.FullPlot, Buildable => MapExtent.Buildable, _ => MapExtent.Routing },
        MarginMetres = MarginMetres,
        ContourIntervalFeet = IntervalFeet,
        IndexEvery = IndexEvery,
        GridYards = ShowGrid ? GridYards : 0,
        Shading = Shading switch { "Slope" => ReliefShading.Slope, "Plain" => ReliefShading.None, _ => ReliefShading.Relief },
        ShowFeatures = ShowSurfaces,
        ShowSummits = ShowHilltops,
        ShowWater = ShowWater,
        PixelsPerMetre = pixelsPerMetre ?? PrintPixelsPerMetre,
        Credit = credit,
    };

    /// <summary>The options that change what is surveyed (and so need a fresh survey, not just a redraw).</summary>
    [JsonIgnore]
    public (string, double, double?, int, bool) SurveyKey => (Area, MarginMetres, IntervalFeet, IndexEvery, ShowWater);
}
