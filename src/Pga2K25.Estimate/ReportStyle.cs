using SkiaSharp;

namespace Pga2K25.Estimate;

/// <summary>Brand styling for estimate pages: Lora (bundled), gold / cream / ink, par colours.</summary>
public static class ReportStyle
{
    public static readonly SKColor Cream = SKColor.Parse("#F4F1EA"), Ink = SKColor.Parse("#1f1d1a"), Gold = SKColor.Parse("#B09766"),
        Gray = SKColor.Parse("#6b665e"), RuleLine = SKColor.Parse("#d8d2c4"), Pale = SKColor.Parse("#e9e4d8"),
        Fill = SKColor.Parse("#2E8C73"), Cut = SKColor.Parse("#E06A3A"),
        Par3 = SKColor.Parse("#5CC09E"), Par4 = SKColor.Parse("#E06A3A"), Par5 = SKColor.Parse("#A8A2E0");

    public static SKColor ParColour(int par) => par switch { 3 => Par3, 5 => Par5, _ => Par4 };

    static readonly Lazy<(SKTypeface Regular, SKTypeface Italic)> Faces = new(() =>
    {
        // Bundled Lora next to the app first, then an installed Lora or a serif fallback (as the course cards do).
        string fonts = Path.Combine(AppContext.BaseDirectory, "fonts", "lora");
        var regular = SKTypeface.FromFile(Path.Combine(fonts, "Lora[wght].ttf"));
        var italic = SKTypeface.FromFile(Path.Combine(fonts, "Lora-Italic[wght].ttf"));
        if (regular is not null && italic is not null) return (regular, italic);
        string family = new[] { "Lora", "Georgia", "Cambria", "Times New Roman" }
            .FirstOrDefault(f => string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase))
            ?? SKTypeface.Default.FamilyName;
        return (SKTypeface.FromFamilyName(family, SKFontStyle.Normal), SKTypeface.FromFamilyName(family, SKFontStyle.Italic));
    });

    public static SKTypeface Regular => Faces.Value.Regular;
    public static SKTypeface Italic => Faces.Value.Italic;

    public static SKFont Font(float size, bool italic = false) => new(italic ? Italic : Regular, size);

    public static SKPaint Paint(SKColor c, bool stroke = false, float width = 1) =>
        new() { Color = c, IsAntialias = true, IsStroke = stroke, StrokeWidth = width };

    public static SKColor Mix(SKColor a, SKColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));
    }

    /// <summary>Money for display: estimates rest on assumptions, so headline figures are rounded.</summary>
    public static string Money(double v) => Math.Abs(v) switch
    {
        >= 1_000_000 => FormattableString.Invariant($"${v / 1_000_000:0.0}M"),
        >= 10_000 => FormattableString.Invariant($"${v / 1000:0}k"),
        >= 1000 => FormattableString.Invariant($"${v / 1000:0.0}k"),
        _ => FormattableString.Invariant($"${v:0}"),
    };
}
