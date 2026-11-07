using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>
/// The plate's palette: watercolour washes multiplied over white paper, inked edges and the type colours.
/// Pixel figures in the renderer are at 300 dpi.
/// </summary>
public sealed record IllustrationStyle
{
    public static readonly IllustrationStyle Default = new();

    public SKColor Ink { get; init; } = SKColor.Parse("#1f1d1a");
    public SKColor Muted { get; init; } = SKColor.Parse("#6b665e");

    public SKColor Rough { get; init; } = SKColor.Parse("#a3ad9e");
    public SKColor RoughMottle { get; init; } = SKColor.Parse("#8e9a8a");
    public SKColor RoughRing { get; init; } = SKColor.Parse("#7f8b7c");

    public SKColor Cover { get; init; } = SKColor.Parse("#fdfcf8");
    public SKColor Fairway { get; init; } = SKColor.Parse("#efe3c0");
    public SKColor MowBand { get; init; } = SKColor.Parse("#e2d3a6");
    public SKColor FairwayContour { get; init; } = SKColor.Parse("#96825a").WithAlpha(40);
    public SKColor FairwayInk { get; init; } = SKColor.Parse("#3a342c").WithAlpha(210);

    public SKColor Green { get; init; } = SKColor.Parse("#f3d8cf");
    public SKColor Collar { get; init; } = SKColor.Parse("#c9cfae");

    public SKColor Sand { get; init; } = SKColor.Parse("#fbfaf5");
    public SKColor Stipple { get; init; } = SKColor.Parse("#46423c").WithAlpha(200);
    public SKColor Hachure { get; init; } = SKColor.Parse("#37342e").WithAlpha(200);
    public SKColor Lip { get; init; } = SKColor.Parse("#7d8579");

    public SKColor Waste { get; init; } = SKColor.Parse("#e8c9b2");
    public SKColor WasteInk { get; init; } = SKColor.Parse("#96786a").WithAlpha(170);
    public SKColor WasteStipple { get; init; } = SKColor.Parse("#786050").WithAlpha(150);

    public SKColor Canopy { get; init; } = SKColor.Parse("#c9c497");
    public SKColor CanopySpeckle { get; init; } = SKColor.Parse("#9c9870");
    public SKColor CanopyInk { get; init; } = SKColor.Parse("#7a7554").WithAlpha(170);
    public SKColor CanopyShadow { get; init; } = SKColor.Parse("#5d6658");

    public SKColor Rock { get; init; } = SKColor.Parse("#b9b3a6");
    public SKColor RockInk { get; init; } = SKColor.Parse("#645e54").WithAlpha(190);

    public SKColor Water { get; init; } = SKColor.Parse("#cfdfec");
    public SKColor WaterInk { get; init; } = SKColor.Parse("#5a6e82").WithAlpha(180);

    public SKColor Path { get; init; } = SKColor.Parse("#faf9f4");
    public SKColor PathEdge { get; init; } = SKColor.Parse("#969a94").WithAlpha(200);

    public SKColor Foreign { get; init; } = SKColor.Parse("#efe6cd");
    public SKColor LineOfPlay { get; init; } = SKColor.Parse("#1f1d1a").WithAlpha(190);
}
