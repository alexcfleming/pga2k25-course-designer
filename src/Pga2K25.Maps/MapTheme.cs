using SkiaSharp;

namespace Pga2K25.Maps;

public enum MapThemeKind { Vintage, Modern, Illustrated }

/// <summary>Colours, strokes and type for a scorecard map style.</summary>
public sealed record MapTheme
{
    public required MapThemeKind Kind { get; init; }
    public required string Name { get; init; }
    public required SKColor Background { get; init; }
    public required SKColor Ink { get; init; }
    public required SKColor MutedInk { get; init; }
    public required SKColor GrassFill { get; init; }
    /// <summary>Outline drawn around the union of grass areas (vintage); null for none.</summary>
    public SKColor? GrassOutline { get; init; }
    public required SKColor GreenFill { get; init; }
    public SKColor? GreenOutline { get; init; }
    public required SKColor TeeFill { get; init; }
    public SKColor? TeeOutline { get; init; }
    public required SKColor BunkerFill { get; init; }
    public required SKColor WasteFill { get; init; }
    public required SKColor WasteStipple { get; init; }
    public required SKColor WaterFill { get; init; }
    public SKColor? WaterHatch { get; init; }
    public required SKColor PathColour { get; init; }
    public required SKColor RoadColour { get; init; }
    public required SKColor RouteColour { get; init; }
    public float[]? RouteDash { get; init; }
    public required bool NumberCircles { get; init; }
    public required bool Frame { get; init; }
    public required string[] TitleFonts { get; init; }
    public required string[] BodyFonts { get; init; }
    /// <summary>Draw the engraved landscape (<see cref="Maps.Landscape"/>) under the course, with a compass rose.</summary>
    public bool Landscape { get; init; }
    /// <summary>Tree canopy fill when <see cref="Landscape"/> is on.</summary>
    public SKColor CrownFill { get; init; } = SKColor.Parse("#efe6cb");

    /// <summary>Ink on parchment, after classic course-guide engravings (e.g. the Lido card).</summary>
    public static MapTheme Vintage { get; } = new()
    {
        Kind = MapThemeKind.Vintage,
        Name = "Vintage",
        Background = SKColor.Parse("#ebe0c2"),
        Ink = SKColor.Parse("#2a2520"),
        MutedInk = SKColor.Parse("#5d554a"),
        GrassFill = SKColor.Parse("#e3d6b1"),
        GrassOutline = SKColor.Parse("#2a2520"),
        GreenFill = SKColor.Parse("#d2c49a"),
        GreenOutline = SKColor.Parse("#2a2520"),
        TeeFill = SKColor.Parse("#e3d6b1"),
        TeeOutline = SKColor.Parse("#2a2520"),
        BunkerFill = SKColor.Parse("#2a2520"),
        WasteFill = SKColor.Parse("#e6dbbd"),
        WasteStipple = SKColor.Parse("#5d554a"),
        WaterFill = SKColor.Parse("#d9cfae"),
        WaterHatch = SKColor.Parse("#2a2520"),
        PathColour = SKColor.Parse("#8a8070"),
        RoadColour = SKColor.Parse("#6f665a"),
        RouteColour = SKColor.Parse("#2a2520"),
        RouteDash = [3f, 3f],
        NumberCircles = true,
        Frame = true,
        TitleFonts = ["Bodoni MT", "Didot", "Georgia", "Times New Roman"],
        BodyFonts = ["Georgia", "Times New Roman"],
    };

    /// <summary>Flat colour, after contemporary course illustrations (e.g. the ClubTFE card).</summary>
    public static MapTheme Modern { get; } = new()
    {
        Kind = MapThemeKind.Modern,
        Name = "Modern",
        Background = SKColor.Parse("#f6e7ba"),
        Ink = SKColor.Parse("#2f3a1c"),
        MutedInk = SKColor.Parse("#6d6a4c"),
        GrassFill = SKColor.Parse("#adbb69"),
        GreenFill = SKColor.Parse("#3b481c"),
        TeeFill = SKColor.Parse("#8fa04f"),
        BunkerFill = SKColor.Parse("#d98b3a"),
        WasteFill = SKColor.Parse("#eed49d"),
        WasteStipple = SKColor.Parse("#d98b3a"),
        WaterFill = SKColor.Parse("#6ca796"),
        PathColour = SKColor.Parse("#d2bb84"),
        RoadColour = SKColor.Parse("#c3aa74"),
        RouteColour = SKColor.Parse("#4f5234"),
        NumberCircles = false,
        Frame = false,
        TitleFonts = ["Segoe UI Semibold", "Segoe UI", "Arial"],
        BodyFonts = ["Segoe UI", "Arial"],
    };

    /// <summary>Vintage with the land drawn like an old engraved map: hachured hills, trees, bushes and the sea.</summary>
    public static MapTheme Illustrated { get; } = Vintage with { Kind = MapThemeKind.Illustrated, Name = "Illustrated", Landscape = true };

    public static MapTheme For(MapThemeKind kind) => kind switch
    {
        MapThemeKind.Modern => Modern,
        MapThemeKind.Illustrated => Illustrated,
        _ => Vintage,
    };
}
