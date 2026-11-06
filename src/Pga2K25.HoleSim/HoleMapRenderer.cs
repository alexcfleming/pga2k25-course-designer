using SkiaSharp;

namespace Pga2K25.HoleSim;

/// <summary>Points and aim rings drawn over the hole map (a shot pattern).</summary>
public sealed record MapOverlay(IReadOnlyList<(FramePoint Point, SKColor Colour)> Points, IReadOnlyList<(FramePoint Point, SKColor Colour)> Rings,
    IReadOnlyList<(FramePoint Point, string Text, SKColor Colour)>? Labels = null,
    IReadOnlyList<(FramePoint Centre, double Size, SKColor Colour)>? Squares = null,
    IReadOnlyList<(FramePoint From, FramePoint To, SKColor Colour, float Width)>? Lines = null);

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

        // Heat squares sit on the surfaces, under everything else.
        foreach (var (centre, size, col) in view.Overlay?.Squares ?? [])
            using (var sq = new SKPaint { Color = col, IsAntialias = false })
            {
                var pc = P(centre); float h = (float)(size * s / 2);
                c.DrawRect(new SKRect(pc.X - h, pc.Y - h, pc.X + h, pc.Y + h), sq);
            }

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
            foreach (var (a, b, col, width) in o.Lines ?? [])
                using (var line = new SKPaint { Color = col, IsAntialias = true, StrokeWidth = width, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round })
                    c.DrawLine(P(a), P(b), line);
            foreach (var (pt, col) in o.Points)
                using (var dot = new SKPaint { Color = col.WithAlpha(150), IsAntialias = true })
                    c.DrawCircle(P(pt), (float)Math.Max(1.6, s * 0.75), dot);
            foreach (var (pt, col) in o.Rings)
                using (var ring = new SKPaint { Color = col, IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke })
                    c.DrawCircle(P(pt), 6, ring);
            foreach (var (pt, text, col) in o.Labels ?? [])
            {
                var p = P(pt);
                using var fill = new SKPaint { Color = col, IsAntialias = true };
                using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
                float w = Math.Max(18, small.MeasureText(text) + 8);
                c.DrawRoundRect(new SKRect(p.X - w / 2, p.Y - 8, p.X + w / 2, p.Y + 8), 8, 8, fill);
                c.DrawText(text, p.X, p.Y + 3.5f, SKTextAlign.Center, small, white);
            }
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
    /// <summary>Diverging colour for strokes better (+) or worse (−) than a reference: green better, pale at par, red worse, full at ±<paramref name="full"/>.</summary>
    public static SKColor GainColour(double gain, double full = 0.15, byte alpha = 190)
    {
        double t = Math.Clamp(gain / full, -1, 1);
        SKColor mid = SKColor.Parse("#F3EBD3"), end = t >= 0 ? SKColor.Parse("#2F7D5B") : SKColor.Parse("#B5473A");
        double u = Math.Abs(t);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * u);
        return new SKColor(L(mid.Red, end.Red), L(mid.Green, end.Green), L(mid.Blue, end.Blue), alpha);
    }

    /// <summary>
    /// The approach heat map for one pin: each spot coloured by how much better or worse it plays than the median spot the
    /// same distance from the pin (<see cref="ApproachHeatMap.Relative"/>), the best angle ringed.
    /// </summary>
    public static byte[] RenderHeatPng(HoleModel m, ApproachHeatMap map, GreenSurface green, double full = 0.12)
    {
        var squares = map.Spots.Where(map.Relative.ContainsKey).Select(s => (s.Where, map.Spacing, GainColour(map.Relative[s], full))).ToList();
        var rings = new List<(FramePoint, SKColor)>(); var labels = new List<(FramePoint, string, SKColor)>();
        if (map.BestAngle is { } best) { rings.Add((best.Where, Ink)); labels.Add((best.Where + new FramePoint(0, -7), "best angle", Ink)); }
        var pts = map.Spots.Select(s => s.Where).Concat(green.Cells).ToList();
        double lat0 = pts.Min(p => p.Lat) - 12, lat1 = pts.Max(p => p.Lat) + 12, al0 = pts.Min(p => p.Along) - 12, al1 = pts.Max(p => p.Along) + 15;
        double ppy = Math.Min(620 / (al1 - al0), 500 / (lat1 - lat0));
        return RenderPng(m, new MapView
        {
            PixelsPerYard = ppy, SelectedPin = map.Pin, ShowWidths = false, AlongFrom = al0, AlongTo = al1, LatFrom = lat0, LatTo = lat1,
            Overlay = new MapOverlay([], rings, labels, squares),
        });
    }

    /// <summary>
    /// The leave map round the green: expected strokes still to play from where a shot finishes (green 1, gold 2, red 3+),
    /// with the ideal-angle spokes round the pin when given (the best one in ink, the rest coloured against the best).
    /// </summary>
    public static byte[] RenderLeavePng(HoleModel m, LeaveMap leave, GreenSurface green, IReadOnlyList<AngleOption>? angles = null,
        IReadOnlyList<(FramePoint Point, SKColor Colour)>? points = null, IReadOnlyList<(FramePoint Point, SKColor Colour)>? rings = null)
    {
        var (lo, hi) = leave.Range;
        var squares = leave.Cells.Select(x => (x.Where, 1.0, LeaveColour(x.Expected, lo, hi).WithAlpha(185))).ToList();
        var pinAt = m.Pins[leave.Pin - 1].Frame;
        var lines = new List<(FramePoint, FramePoint, SKColor, float)>(); var labels = new List<(FramePoint, string, SKColor)>();
        double r0 = Math.Max(8, green.Cells.Max(p => p.DistanceTo(pinAt)) + 4);
        if (angles is { Count: > 0 })
        {
            var best = angles.MinBy(a => a.Expected)!;
            foreach (var a in angles)
            {
                var u = (a.From - pinAt) * (1 / Math.Max((a.From - pinAt).Length, 1e-9));
                bool isBest = ReferenceEquals(a, best);
                var col = isBest ? Ink : GainColour(best.Expected - a.Expected, 0.15, 255);
                lines.Add((pinAt + u * r0, pinAt + u * (r0 + (isBest ? 14 : 10)), col, isBest ? 4 : 3));
                if (isBest) labels.Add((pinAt + u * (r0 + 19), "ideal", Ink));
            }
        }
        double lat0 = leave.Cells.Min(x => x.Where.Lat), lat1 = leave.Cells.Max(x => x.Where.Lat), al0 = leave.Cells.Min(x => x.Where.Along), al1 = leave.Cells.Max(x => x.Where.Along);
        double pad = angles is { Count: > 0 } ? Math.Max(0, r0 + 24 - ShortGameMap.Reach) : 0;
        lat0 -= pad; lat1 += pad; al0 -= pad; al1 += pad;
        double ppy = Math.Min(560 / (al1 - al0), 520 / (lat1 - lat0));
        return RenderPng(m, new MapView
        {
            PixelsPerYard = ppy, SelectedPin = leave.Pin, ShowWidths = false, AlongFrom = al0, AlongTo = al1, LatFrom = lat0, LatTo = lat1,
            Overlay = new MapOverlay(points ?? [], rings ?? [], labels, squares, lines),
        });
    }

    /// <summary>
    /// The par 5 second shot: every lay-up target coloured against going for the green (green: laying up there beats
    /// going; red: going is better), the best lay-up and the go aim ringed, and where the go shots finish.
    /// </summary>
    public static byte[] RenderSecondShotPng(HoleModel m, SecondShotPlan plan, GreenSurface green, double spacing = 5)
    {
        var squares = plan.Layups.Select(l => (l.Target, spacing, GainColour(plan.Go.Expected - l.Expected, 0.25))).ToList();
        var rings = new List<(FramePoint, SKColor)> { (plan.From, SKColor.Parse("#E06A3A")), (plan.Go.Aim, SKColor.Parse("#3A6EA5")) };
        var labels = new List<(FramePoint, string, SKColor)> { (plan.From + new FramePoint(0, -7), "from", SKColor.Parse("#E06A3A")), (plan.Go.Aim + new FramePoint(0, 8), "go", SKColor.Parse("#3A6EA5")) };
        if (plan.BestLayup is { } b) { rings.Add((b.Target, Ink)); labels.Add((b.Target + new FramePoint(0, -7), "lay up", Ink)); }
        var pts = plan.Layups.Select(l => l.Target).Concat(green.Cells).Append(plan.From).ToList();
        double lat0 = pts.Min(p => p.Lat) - 12, lat1 = pts.Max(p => p.Lat) + 12, al0 = pts.Min(p => p.Along) - 14, al1 = pts.Max(p => p.Along) + 15;
        double ppy = Math.Min(620 / (al1 - al0), 500 / (lat1 - lat0));
        return RenderPng(m, new MapView
        {
            PixelsPerYard = ppy, SelectedPin = m.Pins.Count >= 1 ? m.Pins.MinBy(p => p.Frame.DistanceTo(plan.Go.Aim))!.Number : 1, ShowWidths = false,
            AlongFrom = al0, AlongTo = al1, LatFrom = lat0, LatTo = lat1, Overlay = new MapOverlay([], rings, labels, squares),
        });
    }

    /// <summary>Colour for strokes still to play after a shot, on the map's own range: green at <paramref name="lo"/>, gold halfway, red at <paramref name="hi"/>.</summary>
    public static SKColor LeaveColour(double e, double lo, double hi) => PuttColour(1 + Math.Clamp((e - lo) / Math.Max(hi - lo, 1e-6), 0, 1) * 2);

    /// <summary>Colour for expected putts: green at one putt, gold at two, red at three or more.</summary>
    public static SKColor PuttColour(double e)
    {
        double t = Math.Clamp((e - 1) / 2, 0, 1);
        SKColor a = SKColor.Parse("#2F7D5B"), b = SKColor.Parse("#B09766"), c = SKColor.Parse("#B5473A");
        var (p, q, u) = t < 0.5 ? (a, b, t * 2) : (b, c, (t - 0.5) * 2);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * u);
        return new SKColor(L(p.Red, q.Red), L(p.Green, q.Green), L(p.Blue, q.Blue));
    }

    /// <summary>
    /// A close-up of the green: expected putts to <paramref name="pin"/> from every spot (when a map is given), arrows
    /// pointing downhill (longer where steeper), shaded where the slope is too steep to hold a stopped ball, and the pins.
    /// </summary>
    public static byte[] RenderGreenPng(HoleModel m, GreenSurface green, PuttingMap? map, int pin, double holdLimitPct, double pixelsPerYard = 12)
    {
        double lat0 = green.Cells.Min(c => c.Lat) - 6, lat1 = green.Cells.Max(c => c.Lat) + 6;
        double al0 = green.Cells.Min(c => c.Along) - 6, al1 = green.Cells.Max(c => c.Along) + 6;
        double s = pixelsPerYard;
        using var bmp = Render(m, new MapView { PixelsPerYard = s, LatFrom = lat0, LatTo = lat1, AlongFrom = al0, AlongTo = al1, ShowWidths = false, SelectedPin = pin });
        using var c = new SKCanvas(bmp);
        float X(double lat) => (float)((lat - lat0) * s);
        float Y(double along) => (float)((al1 - along) * s);
        using var fill = new SKPaint { IsAntialias = false };
        foreach (var cell in green.Cells)
        {
            var key = GreenSurface.Key(cell);
            if (map is not null && map.Cells.TryGetValue(key, out var v)) fill.Color = PuttColour(v.Expected).WithAlpha(225);
            else fill.Color = Colours[Ground.Green];
            c.DrawRect(new SKRect(X(cell.Lat - 0.5), Y(cell.Along + 0.5), X(cell.Lat + 0.5) + 0.5f, Y(cell.Along - 0.5) + 0.5f), fill);
            if (green.Slope(cell).Length * 100 > holdLimitPct)
                using (var hatch = new SKPaint { Color = Ink.WithAlpha(70), IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke })
                    c.DrawLine(X(cell.Lat - 0.5), Y(cell.Along - 0.5), X(cell.Lat + 0.5), Y(cell.Along + 0.5), hatch);
        }
        using var arrow = new SKPaint { Color = Ink.WithAlpha(150), IsAntialias = true, StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke };
        using var head = new SKPaint { Color = Ink.WithAlpha(170), IsAntialias = true };
        foreach (var cell in green.Cells)
        {
            if (((int)Math.Floor(cell.Lat)) % 2 != 0 || ((int)Math.Floor(cell.Along)) % 2 != 0) continue;
            var sl = green.Slope(cell); double pct = sl.Length * 100; if (pct < 0.3) continue;
            double len = Math.Min(1.7, 0.5 + pct / 3) * s * 0.6;
            double dx = -sl.Lat / sl.Length, dy = -sl.Along / sl.Length;
            float x0 = X(cell.Lat - dx * len / s / 2), y0 = Y(cell.Along - dy * len / s / 2), x1 = X(cell.Lat + dx * len / s / 2), y1 = Y(cell.Along + dy * len / s / 2);
            c.DrawLine(x0, y0, x1, y1, arrow);
            c.DrawCircle(x1, y1, 1.6f, head);
        }
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default, 11);
        using var ink = new SKPaint { Color = Ink, IsAntialias = true };
        using var paper = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var edge = new SKPaint { Color = Ink, IsAntialias = true, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke };
        foreach (var p in m.Pins)
        {
            bool on = p.Number == pin; var pt = new SKPoint(X(p.Frame.Lat), Y(p.Frame.Along));
            c.DrawCircle(pt, on ? 9 : 7, on ? ink : paper); c.DrawCircle(pt, on ? 9 : 7, edge);
            using var t = new SKPaint { Color = on ? SKColors.White : Ink, IsAntialias = true };
            c.DrawText(p.Number.ToString(), pt.X, pt.Y + 4, SKTextAlign.Center, font, t);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
