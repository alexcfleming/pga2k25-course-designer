using SkiaSharp;

namespace Pga2K25.HoleSim;

/// <summary>Points and aim rings drawn over the hole map (a shot pattern).</summary>
public sealed record MapOverlay(IReadOnlyList<(FramePoint Point, SKColor Colour)> Points, IReadOnlyList<(FramePoint Point, SKColor Colour)> Rings);

public sealed record MapView
{
    public double PixelsPerYard { get; init; } = 2;
    public double? AlongFrom { get; init; }
    public double? AlongTo { get; init; }
    public double? LatFrom { get; init; }
    public double? LatTo { get; init; }
    public int SelectedPin { get; init; } = 1;
    public bool ShowWidths { get; init; } = true;
    public MapOverlay? Overlay { get; init; }
}

/// <summary>The hole in its frame, tee at the bottom and green at the top, as a PNG: surfaces from the grid, yardage lines from the chosen tee, the line of play, aim marker, widths, pins and tees.</summary>
public static class HoleMapRenderer
{
    public static readonly IReadOnlyDictionary<Ground, SKColor> Colours = new Dictionary<Ground, SKColor>
    {
        [Ground.Native] = SKColor.Parse("#E9E3D2"),
        [Ground.HeavyRough] = SKColor.Parse("#D2D8B2"),
        [Ground.Rough] = SKColor.Parse("#DCE7C3"),
        [Ground.Scrub] = SKColor.Parse("#7D8C62"),
        [Ground.Water] = SKColor.Parse("#8FB6C9"),
        [Ground.Fairway] = SKColor.Parse("#A9D18E"),
        [Ground.Bunker] = SKColor.Parse("#F0DDB6"),
        [Ground.Green] = SKColor.Parse("#4D9A57"),
    };

    static readonly SKColor Ink = SKColor.Parse("#1F1D1A"), Muted = SKColor.Parse("#6B665E"), Gold = SKColor.Parse("#B09766");

    public static byte[] RenderPng(HoleModel m, MapView? view = null, SurfaceGrid? grid = null)
    {
        using var bmp = Render(m, view, grid);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static SKBitmap Render(HoleModel m, MapView? view = null, SurfaceGrid? grid = null)
    {
        view ??= new MapView();
        grid ??= m.Grid;
        double s = view.PixelsPerYard;
        double lat0 = view.LatFrom ?? Math.Max(grid.LatMin, Math.Min(-70, m.Route.Min(r => r.Lat) - 45));
        double lat1 = view.LatTo ?? Math.Min(grid.LatMax, Math.Max(70, m.Route.Max(r => r.Lat) + 45));
        double al0 = view.AlongFrom ?? -15, al1 = view.AlongTo ?? m.GreenFrame.Along + 35;
        int W = (int)Math.Ceiling((lat1 - lat0) * s), H = (int)Math.Ceiling((al1 - al0) * s);
        float X(double lat) => (float)((lat - lat0) * s);
        float Y(double along) => (float)((al1 - along) * s);
        SKPoint P(FramePoint p) => new(X(p.Lat), Y(p.Along));

        // Surfaces: one pixel per yard, scaled up.
        int gw = (int)Math.Ceiling(lat1 - lat0), gh = (int)Math.Ceiling(al1 - al0);
        using var cells = new SKBitmap(gw, gh, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                cells.SetPixel(x, y, Colours[grid.At(new FramePoint(lat0 + x + 0.5, al1 - y - 0.5))]);
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        using (var img = SKImage.FromBitmap(cells))
            c.DrawImage(img, new SKRect(0, 0, gw, gh), new SKRect(0, 0, (float)(gw * s), (float)(gh * s)), new SKSamplingOptions(SKFilterMode.Linear), null);

        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default, 11);
        using var small = new SKFont(font.Typeface, 10);
        using var ink = new SKPaint { Color = Ink, IsAntialias = true };
        using var muted = new SKPaint { Color = Muted, IsAntialias = true };
        using var thin = new SKPaint { Color = Muted.WithAlpha(90), IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash([3, 4], 0) };

        // Yardage lines from the chosen tee.
        for (int a = (int)Math.Ceiling(Math.Max(al0, 1) / 50) * 50; a <= al1; a += 50)
        {
            c.DrawLine(0, Y(a), W, Y(a), thin);
            c.DrawText(a.ToString(), 4, Y(a) - 4, SKTextAlign.Left, small, muted);
        }

        // Line of play.
        using (var route = new SKPaint { Color = Gold, IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash([6, 4], 0) })
            for (int i = 0; i + 1 < m.Route.Count; i++) c.DrawLine(P(m.Route[i]), P(m.Route[i + 1]), route);

        if (view.ShowWidths)
            using (var bar = new SKPaint { Color = Ink, IsAntialias = true, StrokeWidth = 1.3f, Style = SKPaintStyle.Stroke })
                foreach (var w in m.Widths)
                {
                    var l = P(m.Frame.ToFrame(w.Left)); var r = P(m.Frame.ToFrame(w.Right));
                    c.DrawLine(l, r, bar);
                    c.DrawText($"{w.Yards}", (l.X + r.X) / 2, Math.Min(l.Y, r.Y) - 4, SKTextAlign.Center, small, ink);
                }

        // Shot pattern.
        if (view.Overlay is { } o)
        {
            foreach (var (pt, col) in o.Points)
                using (var dot = new SKPaint { Color = col.WithAlpha(150), IsAntialias = true })
                    c.DrawCircle(P(pt), (float)Math.Max(1.6, s * 0.75), dot);
            foreach (var (pt, col) in o.Rings)
                using (var ring = new SKPaint { Color = col, IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke })
                    c.DrawCircle(P(pt), 6, ring);
        }

        // Aim marker.
        if (m.HasAimMarker)
        {
            var a = P(m.AimFrame);
            using var ring = new SKPaint { Color = Ink, IsAntialias = true, StrokeWidth = 1.6f, Style = SKPaintStyle.Stroke };
            c.DrawCircle(a, 6, ring); c.DrawCircle(a, 2, ink);
            c.DrawText($"{m.AimFromTeeYards}", a.X + 9, a.Y + 4, SKTextAlign.Left, font, ink);
        }

        // Pins: the chosen one solid.
        using var paper = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var edge = new SKPaint { Color = Ink, IsAntialias = true, StrokeWidth = 1.3f, Style = SKPaintStyle.Stroke };
        foreach (var pin in m.Pins)
        {
            var p = P(pin.Frame); bool on = pin.Number == view.SelectedPin;
            c.DrawCircle(p, on ? 7 : 5.5f, on ? ink : paper); c.DrawCircle(p, on ? 7 : 5.5f, edge);
            using var t = new SKPaint { Color = on ? SKColors.White : Ink, IsAntialias = true };
            c.DrawText(pin.Number.ToString(), p.X, p.Y + 3.5f, SKTextAlign.Center, small, t);
        }

        // Tees.
        foreach (var t in m.Tees)
        {
            var p = P(m.Frame.ToFrame(t.World)); bool on = t.Index == m.TeeIndex;
            var r = new SKRect(p.X - 3.5f, p.Y - 3.5f, p.X + 3.5f, p.Y + 3.5f);
            c.DrawRect(r, on ? ink : paper); c.DrawRect(r, edge);
            c.DrawText(t.Name, p.X + 7, p.Y + 4, SKTextAlign.Left, small, on ? ink : muted);
        }
        return bmp;
    }
}
