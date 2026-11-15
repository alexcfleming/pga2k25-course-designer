using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>How a hole plate is drawn.</summary>
public enum IllustrationLook
{
    /// <summary>Watercolour and ink, after Jim Ryan's hole plates illustrated by Tyler J. Rae.</summary>
    Watercolour,
    /// <summary>Graphite pencil on cream, after Coore &amp; Crenshaw's hand-drawn hole sketches.</summary>
    CooreCrenshaw,
}

/// <summary>What the builder needs from a plate style.</summary>
public interface IPlateRenderer : IDisposable
{
    Plate Render(HoleAnalysis a, string? holeName = null, WasteShapes? waste = null);
    void DrawPage(SKCanvas canvas, string courseName, Plate plate);
}

/// <summary>
/// The Coore &amp; Crenshaw look: a pencil sketch on cream paper. The fairway is one heavy wobbling outline round
/// graphite shading; the green darker, with a flag; bunkers heavy-lipped on the face the player looks into; tees as
/// shaded pads marked T; the ground's slopes as short hachure strokes running downhill round the hole (fading out with
/// distance); pines as
/// asterisks and other trees as scribbled rings. No frame or ruler: a hand-lettered caption with the course, hole, yards
/// and par. The art is a raster at 300 dpi; the caption and credits are vector.
/// </summary>
public sealed class SketchPlateRenderer : IPlateRenderer
{
    public const string StyleCredit = "IN THE STYLE OF COORE & CRENSHAW'S HOLE SKETCHES";

    static readonly SKColor Paper = SKColor.Parse("#f2ede1"), Graphite = SKColor.Parse("#2a2826"), Lead = SKColor.Parse("#4a4744");

    readonly IllustrationOptions _o;
    readonly SKTypeface _hand;

    public SketchPlateRenderer(IllustrationOptions options, IProgress<string>? log = null)
    {
        _o = options;
        string file = Path.Combine(AppContext.BaseDirectory, "fonts", "sketches", "ArchitectsDaughter-Regular.ttf");
        _hand = File.Exists(file) ? SKTypeface.FromFile(file) : SKTypeface.FromFamilyName("Segoe Print", SKFontStyle.Bold);
        if (!File.Exists(file)) log?.Report("Hole illustrations: Architects Daughter isn't bundled; lettering the sketches in an installed hand.");
    }

    public void Dispose() => _hand.Dispose();

    // ───────────────────────── pencil primitives ─────────────────────────

    static SKPaint Pencil(float width, byte alpha = 235, uint seed = 1, float wobble = 1.1f)
    {
        var p = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            Color = Graphite.WithAlpha(alpha),
        };
        if (wobble > 0) p.PathEffect = SKPathEffect.CreateCompose(SKPathEffect.CreateCorner(3), SKPathEffect.CreateDiscrete(9, wobble, seed));
        return p;
    }

    /// <summary>A seamless tile of short pencil strokes at a slant, as a repeating shader (pixels).</summary>
    static SKShader GraphiteTile(int strokes, byte alphaLo, byte alphaHi, int seed, float angleDegrees = -58)
    {
        const int size = 120;
        using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            var rnd = new Random(seed);
            using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            for (int i = 0; i < strokes; i++)
            {
                float x = (float)rnd.NextDouble() * size, y = (float)rnd.NextDouble() * size;
                float len = 7 + (float)rnd.NextDouble() * 12, a = (angleDegrees + (float)(rnd.NextDouble() - 0.5) * 14) * MathF.PI / 180;
                p.StrokeWidth = 0.7f + (float)rnd.NextDouble() * 0.6f;
                p.Color = Lead.WithAlpha((byte)rnd.Next(alphaLo, alphaHi));
                float dx = MathF.Cos(a) * len / 2, dy = MathF.Sin(a) * len / 2;
                for (int ox = -1; ox <= 1; ox++)
                    for (int oy = -1; oy <= 1; oy++)
                        c.DrawLine(x - dx + ox * size, y - dy + oy * size, x + dx + ox * size, y + dy + oy * size, p);
            }
        }
        using var img = SKImage.FromBitmap(bmp);
        return img.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Linear));
    }

    static void Shade(SKCanvas c, SKPath area, SKShader tile)
    {
        using var p = new SKPaint { IsAntialias = true, Shader = tile };
        c.DrawPath(area, p);
    }

    static SKPath Union(IEnumerable<SKPath> paths)
    {
        var u = new SKPath();
        foreach (var p in paths)
            if (u.Op(p, SKPathOp.Union) is { } next) { u.Dispose(); u = next; }
        return u;
    }

    // ───────────────────────── art ─────────────────────────

    public Plate Render(HoleAnalysis a, string? holeName = null, WasteShapes? waste = null)
    {
        int rulerTop = PlateLayout.RulerTop(a.LengthYards, _o.RulerMarginYards, _o.RulerStepYards);
        float upy = PlateLayout.UnitsPerYard(rulerTop);
        var proj = new PlateProjection(a.RotationDegrees, a.Route, upy);
        int w = (int)Math.Round(PlateLayout.ArtZone.Width * PlateLayout.RasterScale), h = (int)Math.Round(PlateLayout.ArtZone.Height * PlateLayout.RasterScale);
        float ppy = proj.PixelsPerYard;
        var rnd = new Random(a.Hole.Number * 6421 + 5);
        var bounds = new SKRect(0, 0, w, h);

        bool Own(List<Vec2> s) => !a.Foreign.Contains(s);
        var fairwayPaths = a.Fairways.Where(Own).Where(f => f.Count >= 3).Select(f => proj.Path(f)).ToList();
        var greenPaths = a.Greens.Where(Own).Where(g => g.Count >= 3).Select(g => proj.Path(g)).ToList();
        var bunkerShapes = a.Bunkers.Where(Own).Where(b => b.Count >= 3).ToList();
        using var fairway = Union(fairwayPaths);
        using var green = Union(greenPaths);
        var route = proj.Pixels(a.Route);

        // Distance (yds) from the line of play, for the fade of everything drawn round the hole.
        var dense = new List<SKPoint>();
        for (int i = 1; i < route.Count; i++)
        {
            int n = Math.Max(1, (int)(SKPoint.Distance(route[i - 1], route[i]) / (4 * ppy)));
            for (int k = 0; k < n; k++) dense.Add(new SKPoint(route[i - 1].X + (route[i].X - route[i - 1].X) * k / n, route[i - 1].Y + (route[i].Y - route[i - 1].Y) * k / n));
        }
        dense.Add(route[^1]);
        float FromPlay(SKPoint p)
        {
            float best = float.MaxValue;
            foreach (var q in dense) best = Math.Min(best, SKPoint.Distance(p, q));
            if (fairway.Contains(p.X, p.Y)) best = Math.Min(best, 0);
            return best / ppy;
        }
        float Fade(SKPoint p, float full = 34, float gone = 62) => Math.Clamp((gone - FromPlay(p)) / (gone - full), 0, 1);

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var c = surface.Canvas;
        c.Clear(Paper);

        // Water: an outline and level pencil strokes.
        using (var wb = new SKPathBuilder { FillType = SKPathFillType.EvenOdd })
        {
            foreach (var s in a.Water.Where(s => s.Count >= 3)) PlateProjection.AddPixels(wb, proj.Pixels(s));
            foreach (var body in a.WaterBodies) foreach (var shore in body.Where(s => s.Count >= 3)) PlateProjection.AddPixels(wb, proj.Pixels(shore));
            using var water = wb.Detach();
            if (!water.IsEmpty)
            {
                c.Save(); c.ClipPath(water, antialias: true);
                using var lines = Pencil(1.1f, 120, 3, 0.6f);
                var wbx = SKRect.Intersect(water.Bounds, bounds);
                for (float y = wbx.Top; y < wbx.Bottom; y += 9) c.DrawLine(wbx.Left, y + (float)rnd.NextDouble() * 3, wbx.Right, y + (float)rnd.NextDouble() * 3, lines);
                c.Restore();
                using var bank = Pencil(3.2f, 230, 4); c.DrawPath(water, bank);
            }
        }

        // Slopes: short strokes running downhill, heavier the steeper, fading out away from the hole.
        if (a.Field is { } field)
        {
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            var win = a.Window;
            double minX = win.Min(v => v.X), maxX = win.Max(v => v.X), minZ = win.Min(v => v.Z), maxZ = win.Max(v => v.Z);
            const double step = 1.9;
            for (double x = minX; x < maxX; x += step)
                for (double z = minZ; z < maxZ; z += step)
                {
                    var wpt = new Vec2(x + (rnd.NextDouble() - 0.5) * step, z + (rnd.NextDouble() - 0.5) * step);
                    var px = proj.Pixel(wpt);
                    if (!bounds.Contains(px)) continue;
                    if (green.Contains(px.X, px.Y)) continue;
                    var g = field.Gradient(wpt);
                    double s = g.Length;
                    if (s < 0.07) continue;
                    float fade = Fade(px);
                    if (fade <= 0) continue;
                    bool onFairway = fairway.Contains(px.X, px.Y);
                    var down = proj.Pixel(wpt - g * (1 / s)) - px;
                    float dl = MathF.Max(0.001f, down.Length);
                    var d = new SKPoint(down.X / dl, down.Y / dl);
                    float len = (float)Math.Clamp(5 + 60 * s, 6, 22);
                    float bend = (float)(rnd.NextDouble() - 0.5) * len * 0.5f;
                    stroke.StrokeWidth = (float)Math.Clamp(1.0 + 2.8 * s, 1.1, 2.6);
                    stroke.Color = Lead.WithAlpha((byte)(Math.Clamp(60 + 620 * s, 70, 225) * fade * (onFairway ? 0.55 : 1)));
                    using var path = new SKPath();
                    path.MoveTo(px.X - d.X * len / 2, px.Y - d.Y * len / 2);
                    path.QuadTo(px.X - d.Y * bend, px.Y + d.X * bend, px.X + d.X * len / 2, px.Y + d.Y * len / 2);
                    c.DrawPath(path, stroke);
                }
        }

        // Fairway: graphite shading and one heavy wobbling outline.
        using (var tile = GraphiteTile(260, 40, 95, a.Hole.Number)) Shade(c, fairway, tile);
        using (var edge = Pencil(3.6f, 245, (uint)a.Hole.Number)) c.DrawPath(fairway, edge);

        // Trees near the hole: pines as asterisks, the rest as scribbled rings.
        using (var pen = Pencil(1.6f, 220, 7, 0))
        {
            foreach (var cf in a.Conifers)
            {
                var p = proj.Pixel(cf.Centre);
                if (!bounds.Contains(p) || Fade(p, 20, 45) < 0.5f || fairway.Contains(p.X, p.Y)) continue;
                float r = Math.Clamp((float)(cf.Radius * proj.PixelsPerMetre) * 0.5f, 9, 18);
                for (int i = 0; i < 4; i++)
                {
                    float ang = i * MathF.PI / 4 + 0.2f;
                    c.DrawLine(p.X - MathF.Cos(ang) * r, p.Y - MathF.Sin(ang) * r, p.X + MathF.Cos(ang) * r, p.Y + MathF.Sin(ang) * r, pen);
                }
            }
            using var ring = Pencil(1.4f, 200, 9, 1.4f);
            foreach (var t in a.Trees)
            {
                var p = proj.Pixel(t.Position);
                if (!bounds.Contains(p) || Fade(p, 20, 45) < 0.5f || fairway.Contains(p.X, p.Y)) continue;
                if (a.Conifers.Any(cf => (cf.Centre - t.Position).Length < 1)) continue;
                float r = Math.Clamp((float)(4 * Math.Max(0.6, t.Scale) * proj.PixelsPerMetre) * 0.6f, 8, 20);
                c.DrawCircle(p, r, ring);
            }
        }

        // Bunkers: paper inside, a light stipple, and a heavy lip on the face the player looks into.
        double wasteM2 = _o.WasteThresholdSqYd * HoleAnalysis.Yard * HoleAnalysis.Yard;
        using (var sparse = GraphiteTile(40, 20, 50, 79, 20))
        using (var soft = Pencil(1.6f, 170, 25, 1.4f))
            foreach (var b in bunkerShapes.Where(b => Geometry.Area(b) > wasteM2))
            {
                // A sandy expanse: a light stipple and a soft line, no lip.
                using var path = proj.Path(b);
                using (var paper = new SKPaint { IsAntialias = true, Color = Paper }) c.DrawPath(path, paper);
                Shade(c, path, sparse);
                c.DrawPath(path, soft);
            }
        using (var stipple = GraphiteTile(90, 25, 60, 77, 20))
            foreach (var b in bunkerShapes.Where(b => Geometry.Area(b) <= wasteM2))
            {
                using var path = proj.Path(b);
                var centre = new Vec2(b.Average(v => v.X), b.Average(v => v.Z));
                bool greenside = (centre - a.Green).Length / HoleAnalysis.Yard <= _o.GreensideBunkerYards;
                var toward = greenside ? a.Green - centre : a.BackTee - centre;
                var dirPx = proj.Pixel(centre + toward.Normalized()) - proj.Pixel(centre);
                float dl = MathF.Max(0.001f, dirPx.Length);
                // The lip: the outline drawn heavy, nudged away from the player so it shows on the far side.
                c.Save(); c.Translate(-dirPx.X / dl * 3.5f, -dirPx.Y / dl * 3.5f);
                using (var lip = Pencil(7f, 240, 21, 1.6f)) c.DrawPath(path, lip);
                c.Restore();
                using (var paper = new SKPaint { IsAntialias = true, Color = Paper }) c.DrawPath(path, paper);
                Shade(c, path, stipple);
                using var edge = Pencil(2.4f, 240, 23); c.DrawPath(path, edge);
            }

        // Tees: shaded pads marked T.
        using (var tile = GraphiteTile(420, 70, 140, 31))
        using (var edge = Pencil(3f, 245, 33))
        using (var tFont = new SKFont(_hand, 26))
        using (var tPaint = new SKPaint { IsAntialias = true, Color = Graphite })
            foreach (var tee in a.TeeSets.Select(t => t.Tee).Distinct())
            {
                var p = proj.Pixel(tee);
                float r = Math.Max(14, 4.5f * ppy);
                using var pad = new SKPath();
                pad.AddRoundRect(new SKRect(p.X - r, p.Y - r * 0.85f, p.X + r, p.Y + r * 0.85f), r * 0.35f, r * 0.35f);
                using (var paper = new SKPaint { IsAntialias = true, Color = Paper }) c.DrawPath(pad, paper);
                Shade(c, pad, tile);
                c.DrawPath(pad, edge);
                c.DrawText("T", p.X, p.Y + 9, SKTextAlign.Center, tFont, tPaint);
            }

        // The green: dense graphite, a heavy outline, the flag on the first pin.
        using (var tile = GraphiteTile(700, 80, 170, 41, -40)) Shade(c, green, tile);
        using (var edge = Pencil(4.2f, 250, 43)) c.DrawPath(green, edge);
        var pin = proj.Pixel(a.Hole.Pins.Count > 0 ? a.Hole.Pins[0] : a.Green);
        using (var pole = Pencil(2f, 250, 0, 0))
        {
            c.DrawLine(pin.X, pin.Y, pin.X, pin.Y - 46, pole);
            using var flag = new SKPath();
            flag.MoveTo(pin.X, pin.Y - 46); flag.LineTo(pin.X + 22, pin.Y - 38); flag.LineTo(pin.X, pin.Y - 30); flag.Close();
            c.DrawPath(flag, pole);
        }

        foreach (var p in fairwayPaths.Concat(greenPaths)) p.Dispose();
        using var snap = surface.Snapshot();
        var pixels = new SKBitmap(info);
        snap.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0);
        return new Plate
        {
            Analysis = a, Projection = proj, RulerTop = rulerTop, UnitsPerYard = upy, HoleName = holeName,
            Art = SKImage.FromBitmap(pixels),
            Darkness = (x, y) => { var col = pixels.GetPixel(x, y); return 1 - (col.Red + col.Green + col.Blue) / (3f * 255); },
        };
    }

    // ───────────────────────── page ─────────────────────────

    public void DrawPage(SKCanvas canvas, string courseName, Plate plate)
    {
        var a = plate.Analysis;
        canvas.Clear(Paper);
        // No ruler to line up with, so the drawing is centred across the page by where its pencil actually is.
        int w = plate.Art.Width, h = plate.Art.Height, left = w, right = 0;
        for (int x = 0; x < w; x += 4)
            for (int yy = 0; yy < h; yy += 6)
                if (plate.Darkness(x, yy) > 0.18f) { left = Math.Min(left, x); right = Math.Max(right, x); break; }
        float dx = right <= left ? 0 : PlateLayout.PageWidth / 2 - (PlateLayout.ArtZone.Left + (left + right) / 2f / PlateLayout.RasterScale);
        dx = Math.Clamp(dx, 40 - PlateLayout.ArtZone.Left, PlateLayout.PageWidth - 40 - PlateLayout.ArtZone.Right);
        using (var paint = new SKPaint { IsAntialias = true })
        {
            var at = PlateLayout.ArtZone; at.Offset(dx, 0);
            canvas.DrawImage(plate.Art, at, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }

        // The caption, hand-lettered at the bottom right like the sketches' own.
        using var ink = new SKPaint { IsAntialias = true, Color = Graphite };
        void Line(string text, float y, float size)
        {
            using var font = new SKFont(_hand, size);
            float width = font.MeasureText(text);
            if (width > 300) font.Size = size * 300 / width;
            canvas.DrawText(text, 520, y, SKTextAlign.Left, font, ink);
        }
        float y = 1000;
        Line(courseName.ToUpperInvariant(), y, 20); y += 25;
        if (!string.IsNullOrWhiteSpace(plate.HoleName)) { Line(plate.HoleName.Trim().ToUpperInvariant(), y, 16); y += 22; }
        Line($"HOLE NO. {a.Hole.Number}", y, 20); y += 25;
        Line($"{a.LengthYards} YARDS PAR {a.Hole.Par}", y, 20);

        using var small = new SKFont(_hand, 7.5f);
        using var muted = new SKPaint { IsAntialias = true, Color = Lead };
        canvas.DrawText(StyleCredit, 40, 1062, SKTextAlign.Left, small, muted);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
            canvas.DrawText($"DESIGNED BY {_o.Credit.ToUpperInvariant()} · {_o.Year}", 40, 1074, SKTextAlign.Left, small, muted);
        canvas.DrawText(Attribution.Text, 40, 1086, SKTextAlign.Left, small, muted);
    }
}
