using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>
/// The Fried Egg look, inspired by Fried Egg Golf's hole sketches: a landscape page with play running left to right, the
/// hole drawn in ink over flat washes on yellow paper. Rough is a pale olive and fairway a deeper one, the ground's rise
/// and fall shaded into them; the slopes round the hole are combs of short ink strokes hanging downhill from the contour
/// lines wherever the ground drops; bunkers are ochre and stippled with a hachured face, big sand a pale waste; the green,
/// drawn over everything, has 6 in contour rings and fall arrows; fescue tufts, scalloped trees and rocks round it. Small
/// handwritten yardages run along the top; the course and its place top left, the hole top right, the designer bottom
/// left, the course seal and the credits bottom right. The art is a raster at 300 dpi; the type is vector.
/// </summary>
public sealed class FriedEggPlateRenderer : IPlateRenderer
{
    public const string StyleCredit = "Inspired by Fried Egg Golf";

    /// <summary>The page: 15 × 10 in, landscape, in units of 1/100 in.</summary>
    public const float PageWidth = 1500, PageHeight = 1000;

    /// <summary>Where the art goes; it feathers into the paper at these edges.</summary>
    public static readonly SKRect ArtZone = new(40, 250, 1460, 880);

    /// <summary>Horizontal room the hole is fitted into (units), and its height (the lateral spread plus a margin).</summary>
    const float FitWidth = 1320, FitHeight = 520;

    public SKSize PageSize => new(PageWidth, PageHeight);

    static readonly SKColor Paper = SKColor.Parse("#f8e9b3"), Ink = SKColor.Parse("#2a2820");
    static readonly SKColor RoughWash = SKColor.Parse("#c2c48c"), FairwayWash = SKColor.Parse("#a5aa66"), GreenWash = SKColor.Parse("#cdd497");
    static readonly SKColor Sand = SKColor.Parse("#e2a63c"), SandDot = SKColor.Parse("#9a6418"), Waste = SKColor.Parse("#ead8a2");
    static readonly SKColor Crown = SKColor.Parse("#b8b77c"), WaterWash = SKColor.Parse("#a9c6c9"), Shade = SKColor.Parse("#454a22");

    const float R = PlateLayout.RasterScale;   // raster pixels per page unit (300 dpi)
    const float Q = R / 2;                      // pen sizes below are tuned at 2 px a unit

    readonly IllustrationOptions _o;
    readonly SKTypeface _serif, _serifBold, _hand;

    public FriedEggPlateRenderer(IllustrationOptions options, IProgress<string>? log = null)
    {
        _o = options;
        string lora = Path.Combine(AppContext.BaseDirectory, "fonts", "lora", "Lora-Italic[wght].ttf");
        string caveat = Path.Combine(AppContext.BaseDirectory, "fonts", "sketches", "Caveat[wght].ttf");
        _serif = File.Exists(lora) ? Weight(SKTypeface.FromFile(lora), 400) : SKTypeface.FromFamilyName("Georgia", SKFontStyle.Italic);
        _serifBold = File.Exists(lora) ? Weight(SKTypeface.FromFile(lora), 700) : SKTypeface.FromFamilyName("Georgia", SKFontStyle.BoldItalic);
        _hand = File.Exists(caveat) ? Weight(SKTypeface.FromFile(caveat), 500) : SKTypeface.FromFamilyName("Segoe Print", SKFontStyle.Normal);
        if (!File.Exists(lora)) log?.Report("Hole illustrations: Lora isn't bundled; setting the Fried Egg sketches in Georgia.");
    }

    public void Dispose() { _serif.Dispose(); _serifBold.Dispose(); _hand.Dispose(); }

    static SKTypeface Weight(SKTypeface variable, float weight)
    {
        const uint Wght = ('w' << 24) | ('g' << 16) | ('h' << 8) | 't';
        if (!variable.VariationDesignParameters.Any(a => a.Tag == Wght)) return variable;
        var set = variable.Clone([new SKFontVariationPositionCoordinate { Axis = Wght, Value = weight }]);
        if (set is null) return variable;
        variable.Dispose();
        return set;
    }

    /// <summary>
    /// World → page for a landscape plate: the plate's own rotation points play up the page; this turns it a quarter turn
    /// clockwise, so play runs left to right and the player's right is down the page. The hole is centred across the page.
    /// </summary>
    public sealed class LandscapeProjection : IPlateProjection
    {
        readonly MapProjection _turn;
        readonly double _latMid;
        readonly float _x0;
        public float UnitsPerMetre { get; }
        public float PixelsPerMetre => UnitsPerMetre * R;

        public LandscapeProjection(HoleAnalysis a)
        {
            _turn = MapProjection.Place(a.RotationDegrees, a.BackTee, 1f, new SKPoint(0, 0));
            double length = Vec2.Distance(a.BackTee, a.Green);
            var lat = a.Route.Select(p => (double)_turn.Map(p).X).ToList();
            _latMid = (lat.Min() + lat.Max()) / 2;
            UnitsPerMetre = (float)Math.Min(FitWidth / (length + 75), FitHeight / (lat.Max() - lat.Min() + 110));
            _x0 = (float)(PageWidth / 2 - length / 2 * UnitsPerMetre);
        }

        public SKPoint Page(Vec2 w)
        {
            var m = _turn.Map(w);
            return new SKPoint(_x0 - m.Y * UnitsPerMetre, ArtZone.MidY + (float)(m.X - _latMid) * UnitsPerMetre);
        }

        public SKPoint Pixel(Vec2 w)
        {
            var p = Page(w);
            return new SKPoint((p.X - ArtZone.Left) * R, (p.Y - ArtZone.Top) * R);
        }

        /// <summary>The world point under an art-raster pixel.</summary>
        public Vec2 Unpixel(SKPoint px)
        {
            double along = (px.X / R + ArtZone.Left - _x0) / UnitsPerMetre, lat = (px.Y / R + ArtZone.Top - ArtZone.MidY) / UnitsPerMetre + _latMid;
            return _turn.Unmap(new SKPoint((float)lat, (float)-along));
        }
    }

    // ───────────────────────── art ─────────────────────────

    public Plate Render(HoleAnalysis a, string? holeName = null, WasteShapes? waste = null)
    {
        var proj = new LandscapeProjection(a);
        int w = (int)Math.Round(ArtZone.Width * R), h = (int)Math.Round(ArtZone.Height * R);
        var bounds = new SKRect(0, 0, w, h);
        float ppm = proj.PixelsPerMetre, ppy = (float)(ppm * HoleAnalysis.Yard);
        var rnd = new Random(a.Hole.Number * 97 + 3);

        SKPath Path(IReadOnlyList<Vec2> pts, bool close = true)
        {
            using var b = new SKPathBuilder();
            b.MoveTo(proj.Pixel(pts[0]));
            foreach (var q in pts.Skip(1)) b.LineTo(proj.Pixel(q));
            if (close) b.Close();
            return b.Detach();
        }
        bool Own(List<Vec2> s) => !a.Foreign.Contains(s) && s.Count >= 3;
        var route = a.Route.Select(proj.Pixel).ToList();
        float FromPlay(SKPoint p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < route.Count; i++)
            {
                var (s, e) = (route[i - 1], route[i]);
                float dx = e.X - s.X, dy = e.Y - s.Y, l2 = dx * dx + dy * dy;
                float t = l2 == 0 ? 0 : Math.Clamp(((p.X - s.X) * dx + (p.Y - s.Y) * dy) / l2, 0, 1);
                best = Math.Min(best, SKPoint.Distance(p, new SKPoint(s.X + t * dx, s.Y + t * dy)));
            }
            return best / ppy;
        }

        // The turf near the hole: rough within 45 yds of play, so the drawing floats on the paper.
        using var corridor = new SKPath();
        using (var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 90 * ppy, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
        using (var line = new SKPath())
        {
            line.MoveTo(route[0]);
            foreach (var q in route.Skip(1)) line.LineTo(q);
            stroke.GetFillPath(line, corridor);
        }
        using var roughAll = InkLines.Union(a.Rough.Where(Own).Select(r => Path(r)));
        using var rough = roughAll.Op(corridor, SKPathOp.Intersect) ?? new SKPath();
        using var fairway = InkLines.Union(a.Fairways.Where(Own).Select(f => Path(f)));
        using var green = InkLines.Union(a.Greens.Where(Own).Select(g => Path(g)));
        using var turf = rough.Op(fairway, SKPathOp.Union) ?? new SKPath();

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var c = surface.Canvas;
        c.Clear(Paper);

        Tufts(c, bounds, turf, green, FromPlay, rnd);

        // Turf: rough paler, fairway over it the deeper olive; the ground's rise and fall shaded into both.
        using (var f = Fill(RoughWash)) c.DrawPath(rough, f);
        using (var f = Fill(FairwayWash)) c.DrawPath(fairway, f);
        if (a.Field is { } field)
        {
            Hillshade(c, field, proj, turf, w, h);
            Hachures(c, field, proj, bounds, green, turf, FromPlay, rnd);
        }
        // Each outline stops where the surface drawn over it starts: rough under fairway, both under the green.
        c.Save(); c.ClipPath(fairway, SKClipOperation.Difference, antialias: true); c.ClipPath(green, SKClipOperation.Difference, antialias: true);
        using (var edge = Pen(2.2f, 230, 3)) c.DrawPath(rough, edge);
        c.Restore();
        c.Save(); c.ClipPath(green, SKClipOperation.Difference, antialias: true);
        using (var edge = Pen(2.6f, 245, 5)) c.DrawPath(fairway, edge);
        c.Restore();

        // Water, then walled water.
        foreach (var s in a.Water.Where(s => s.Count >= 3).Concat(a.WaterBodies.SelectMany(b => b).Where(s => s.Count >= 3)))
        {
            using var wp = Path(s);
            using (var f = Fill(WaterWash)) c.DrawPath(wp, f);
            using var e = Pen(2.2f, 230, 7); c.DrawPath(wp, e);
        }
        if (a.WaterWalls.Count > 0)
            Maps.WaterWall.Draw(c, a.WaterWalls, a.WaterWallOver, s => Path(s), WaterWash, Ink, Paper, 2.4f * Q);

        // Trees and bushes near play: scalloped crowns, never on the fairway.
        using (var crown = Fill(Crown))
        using (var scallop = Pen(1.6f, 220, 9, 0.5f))
            foreach (var t in a.Trees.Concat(a.Scrub))
            {
                var p = proj.Pixel(t.Position);
                if (!bounds.Contains(p) || FromPlay(p) > 50 || fairway.Contains(p.X, p.Y) || green.Contains(p.X, p.Y)) continue;
                bool bush = a.Scrub.Contains(t);
                float r = Math.Clamp((float)((bush ? 1.6 : 3.4) * Math.Max(0.6, t.Scale) * ppm), 6 * Q, 26 * Q);
                c.DrawCircle(p, r, crown);
                using var sc = new SKPath();
                for (int i = 0; i < 9; i++) { float a0 = i * MathF.PI * 2 / 9; sc.AddCircle(p.X + MathF.Cos(a0) * r * 0.78f, p.Y + MathF.Sin(a0) * r * 0.78f, r * 0.32f); }
                c.DrawPath(sc, scallop);
            }

        // Rock outcrops as they show above the ground: pale stone, an ink edge, a few cracks.
        using (var stone = Fill(SKColor.Parse("#e6dcc0")))
        using (var edge = Pen(1.8f, 235, 21, 0.6f))
        using (var crack = Pen(1.0f, 170, 0, 0))
            foreach (var o in a.Outcrops.Where(o => o.Count >= 3))
            {
                using var rp = Path(o);
                if (SKRect.Intersect(rp.Bounds, bounds).IsEmpty) continue;
                c.DrawPath(rp, stone);
                var rb = rp.Bounds;
                c.Save(); c.ClipPath(rp, antialias: true);
                for (int i = 0; i < 2 + (int)(rb.Width / (30 * Q)); i++)
                {
                    float x = rb.Left + (float)rnd.NextDouble() * rb.Width, y = rb.Top + (float)rnd.NextDouble() * rb.Height;
                    c.DrawLine(x, y, x + (float)(rnd.NextDouble() - 0.5) * 10 * Q, y + 6 * Q, crack);
                }
                c.Restore();
                c.DrawPath(rp, edge);
            }

        Bunkers(c, a, proj, bounds, Own, waste, rnd);

        // Tees: small olive pads.
        foreach (var tee in a.TeeSets.Select(t => t.Tee).Distinct())
        {
            var p = proj.Pixel(tee);
            float r = Math.Max(7 * Q, 4 * ppy);
            var rect = new SKRect(p.X - r * 0.7f, p.Y - r, p.X + r * 0.7f, p.Y + r);
            using (var f = Fill(FairwayWash)) c.DrawRoundRect(rect, 3 * Q, 3 * Q, f);
            using var e = Pen(1.6f, 240, 17, 0.3f); c.DrawRoundRect(rect, 3 * Q, 3 * Q, e);
        }

        // The green, over everything: its wash, 6 in contour rings, fall arrows, the firmest line, the flag.
        using (var f = Fill(GreenWash)) c.DrawPath(green, f);
        if (a.Field is { } gf && !green.IsEmpty) GreenDetail(c, gf, proj, green, rnd);
        using (var e = Pen(2.4f, 245, 15)) c.DrawPath(green, e);
        var pin = proj.Pixel(a.Hole.Pins.Count > 0 ? a.Hole.Pins[0] : a.Green);
        using (var pole = Pen(2f, 250, 0, 0))
        {
            c.DrawLine(pin.X, pin.Y, pin.X, pin.Y - 34 * Q, pole);
            using var flag = new SKPath();
            flag.MoveTo(pin.X, pin.Y - 34 * Q); flag.LineTo(pin.X + 16 * Q, pin.Y - 28 * Q); flag.LineTo(pin.X, pin.Y - 22 * Q); flag.Close();
            c.DrawPath(flag, pole);
        }

        Feather(c, w, h);

        using var snap = surface.Snapshot();
        var pixels = new SKBitmap(info);
        snap.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0);
        int yards = a.LengthYards;
        return new Plate
        {
            Analysis = a, Projection = proj, RulerTop = yards, UnitsPerYard = (float)(proj.UnitsPerMetre * HoleAnalysis.Yard), HoleName = holeName,
            Art = SKImage.FromBitmap(pixels),
            Darkness = (x, y) =>
            {
                // Darkness against the paper, so the yellow ground reads as 0.
                var col = pixels.GetPixel(x, y);
                return Math.Clamp(1 - (col.Red + col.Green + col.Blue) / (float)(Paper.Red + Paper.Green + Paper.Blue), 0, 1);
            },
        };
    }

    static SKPaint Fill(SKColor colour) => new() { IsAntialias = true, Color = colour };

    /// <summary>An ink pen; widths are tuned at 2 px a unit and scaled to the raster.</summary>
    static SKPaint Pen(float width, byte alpha = 235, uint seed = 1, float wobble = 0.9f)
    {
        var p = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width * Q, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            Color = Ink.WithAlpha(alpha),
        };
        if (wobble > 0) p.PathEffect = SKPathEffect.CreateCompose(SKPathEffect.CreateCorner(3 * Q), SKPathEffect.CreateDiscrete(8 * Q, wobble * Q, seed));
        return p;
    }

    /// <summary>Fescue tufts on the paper round the turf: fans of three to six blades, thinning out away from play.</summary>
    static void Tufts(SKCanvas c, SKRect bounds, SKPath turf, SKPath green, Func<SKPoint, float> fromPlay, Random rnd)
    {
        using var blade = Pen(1.2f, 160, 0, 0);
        int count = (int)(bounds.Width * bounds.Height / (2600 * Q * Q));
        for (int i = 0; i < count; i++)
        {
            var p = new SKPoint(bounds.Left + (float)rnd.NextDouble() * bounds.Width, bounds.Top + (float)rnd.NextDouble() * bounds.Height);
            if (turf.Contains(p.X, p.Y) || green.Contains(p.X, p.Y)) continue;
            float d = fromPlay(p);
            if (d > 60 || rnd.NextDouble() > (60 - d) / 45) continue;
            int blades = 3 + rnd.Next(4);
            float size = (4 + (float)rnd.NextDouble() * 5) * Q, lean = (float)(rnd.NextDouble() - 0.5) * 0.6f;
            for (int j = 0; j < blades; j++)
            {
                float t = blades == 1 ? 0 : j / (float)(blades - 1) - 0.5f;
                float angle = -MathF.PI / 2 + lean + t * 1.3f;
                c.DrawLine(p.X + t * 3 * Q, p.Y, p.X + t * 3 * Q + MathF.Cos(angle) * size, p.Y + MathF.Sin(angle) * size, blade);
            }
        }
    }

    /// <summary>Slopes facing away from a light at the top left go darker, inside the turf only.</summary>
    static void Hillshade(SKCanvas c, Heightfield field, LandscapeProjection proj, SKPath turf, int w, int h)
    {
        const int step = 6;
        int sw = w / step, sh = h / step;
        using var shade = new SKBitmap(sw, sh, SKColorType.Rgba8888, SKAlphaType.Premul);
        shade.Erase(SKColors.Transparent);
        float ppm = proj.PixelsPerMetre;
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                var px = new SKPoint(x * step, y * step);
                if (!turf.Contains(px.X, px.Y)) continue;
                var wpt = proj.Unpixel(px);
                var g = field.Gradient(wpt);
                var gx = proj.Pixel(wpt + new Vec2(1, 0)) - proj.Pixel(wpt);
                var gz = proj.Pixel(wpt + new Vec2(0, 1)) - proj.Pixel(wpt);
                float dx = (float)(g.X * gx.X + g.Z * gz.X) / ppm, dy = (float)(g.X * gx.Y + g.Z * gz.Y) / ppm;
                float away = Math.Clamp((dx + dy) * 0.7f * 9, 0, 1);
                shade.SetPixel(x, y, Shade.WithAlpha((byte)(away * 110)));
            }
        using var img = SKImage.FromBitmap(shade);
        c.Save(); c.ClipPath(turf, antialias: true);
        using (var p = new SKPaint { IsAntialias = true }) c.DrawImage(img, new SKRect(0, 0, sw * step, sh * step), new SKSamplingOptions(SKFilterMode.Linear), p);
        c.Restore();
    }

    /// <summary>
    /// Combs of short pencil strokes hanging downhill from the contour lines (every 0.5 m) wherever the ground drops more
    /// than 12 %, the way a sketch shows a bank: the contours themselves aren't drawn. Fainter on the turf, fading out
    /// 35–55 yds from play.
    /// </summary>
    static void Hachures(SKCanvas c, Heightfield field, LandscapeProjection proj, SKRect bounds, SKPath green, SKPath turf, Func<SKPoint, float> fromPlay, Random rnd)
    {
        // Pencil, not pen: grainy edges (a fine wobble) softened a touch, in graphite rather than black ink.
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round,
            PathEffect = SKPathEffect.CreateDiscrete(1.6f * Q, 0.35f * Q, 29),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 0.45f * Q),
        };
        float cell = 5 * Q, spacing = 5.5f * Q;
        int gw = (int)(bounds.Width / cell) + 1, gh = (int)(bounds.Height / cell) + 1;
        var hv = new double[gw, gh];
        var near = new bool[gw, gh];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < gw; i++)
            for (int j = 0; j < gh; j++)
            {
                var px = new SKPoint(i * cell, j * cell);
                near[i, j] = fromPlay(px) < 55;
                if (!near[i, j]) continue;
                hv[i, j] = field.Sample(proj.Unpixel(px));
                lo = Math.Min(lo, hv[i, j]); hi = Math.Max(hi, hv[i, j]);
            }
        if (lo > hi) return;
        const double interval = 0.5;
        Span<double> v = stackalloc double[4];
        for (double lev = Math.Ceiling(lo / interval) * interval; lev < hi; lev += interval)
        {
            float carry = 0;
            for (int i = 0; i < gw - 1; i++)
                for (int j = 0; j < gh - 1; j++)
                {
                    if (!(near[i, j] && near[i + 1, j + 1])) continue;
                    v[0] = hv[i, j]; v[1] = hv[i + 1, j]; v[2] = hv[i + 1, j + 1]; v[3] = hv[i, j + 1];
                    SKPoint? s0 = null, s1 = null;
                    for (int q = 0; q < 4; q++)
                    {
                        double va = v[q], vb = v[(q + 1) % 4];
                        if ((va - lev) * (vb - lev) >= 0) continue;
                        float t = (float)((lev - va) / (vb - va));
                        var (ax, ay) = q switch { 0 => (i, j), 1 => (i + 1, j), 2 => (i + 1, j + 1), _ => (i, j + 1) };
                        var (bx, by) = ((q + 1) % 4) switch { 0 => (i, j), 1 => (i + 1, j), 2 => (i + 1, j + 1), _ => (i, j + 1) };
                        var pt = new SKPoint((ax + (bx - ax) * t) * cell, (ay + (by - ay) * t) * cell);
                        if (s0 is null) s0 = pt; else if (s1 is null) s1 = pt;
                    }
                    if (s0 is not { } a0 || s1 is not { } a1) continue;
                    var mid = new SKPoint((a0.X + a1.X) / 2, (a0.Y + a1.Y) / 2);
                    if (green.Contains(mid.X, mid.Y)) continue;
                    var wpt = proj.Unpixel(mid);
                    var g = field.Gradient(wpt);
                    double slope = g.Length;
                    if (slope < 0.12) continue;
                    var down = proj.Pixel(wpt - g * (1 / slope)) - mid;
                    float dl = MathF.Max(0.001f, down.Length);
                    var dir = new SKPoint(down.X / dl, down.Y / dl);
                    float len = (float)Math.Clamp(5 + 48 * slope, 6, 18) * Q, seg = SKPoint.Distance(a0, a1);
                    float fade = Math.Clamp((55 - fromPlay(mid)) / 20, 0, 1);
                    bool onTurf = turf.Contains(mid.X, mid.Y);
                    float width = (float)Math.Clamp(0.9 + 1.2 * slope, 1.0, 1.6) * Q;
                    byte alpha = (byte)(Math.Clamp(100 + 500 * slope, 115, 200) * fade * (onTurf ? 0.5 : 1));
                    for (float u = carry; u < seg; u += spacing)
                    {
                        var at = new SKPoint(a0.X + (a1.X - a0.X) * u / seg, a0.Y + (a1.Y - a0.Y) * u / seg);
                        float l = len * (0.75f + 0.5f * (float)rnd.NextDouble());
                        PencilStroke(c, at, new SKPoint(at.X + dir.X * l, at.Y + dir.Y * l), width, alpha, stroke, rnd);
                        carry = spacing - (seg - u);
                    }
                    carry = Math.Clamp(carry, 0, spacing);
                }
        }
    }

static readonly SKColor Graphite = SKColor.Parse("#3b3a33");

    /// <summary>
    /// One pencil stroke from <paramref name="a"/> to <paramref name="b"/>: a main pass with a slight bow, then a fainter,
    /// thinner pass beside it that lifts off early, so the stroke is heavier where the pencil lands and grainy along its
    /// length. Same place and length as a pen line; only the mark changes.
    /// </summary>
    static void PencilStroke(SKCanvas c, SKPoint a, SKPoint b, float width, byte alpha, SKPaint paint, Random rnd)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f || alpha == 0) return;
        float nx = -dy / len, ny = dx / len;
        float bow = (float)(rnd.NextDouble() - 0.5) * 0.12f * len;
        using (var main = new SKPath())
        {
            main.MoveTo(a);
            main.QuadTo(a.X + dx / 2 + nx * bow, a.Y + dy / 2 + ny * bow, b.X, b.Y);
            paint.StrokeWidth = width;
            paint.Color = Graphite.WithAlpha((byte)(alpha * 0.8f));
            c.DrawPath(main, paint);
        }
        float side = (rnd.NextDouble() < 0.5 ? -1 : 1) * width * 0.45f, reach = 0.55f + 0.3f * (float)rnd.NextDouble();
        using (var second = new SKPath())
        {
            second.MoveTo(a.X + nx * side, a.Y + ny * side);
            second.QuadTo(a.X + dx * reach / 2 + nx * (side + bow * reach), a.Y + dy * reach / 2 + ny * (side + bow * reach),
                a.X + dx * reach + nx * side, a.Y + dy * reach + ny * side);
            paint.StrokeWidth = width * 0.6f;
            paint.Color = Graphite.WithAlpha((byte)(alpha * 0.5f));
            c.DrawPath(second, paint);
        }
    }

    /// <summary>
    /// Bunkers: ochre with a stipple, an ink edge, and a fringe of short strokes on the face away from the player. Sand over
    /// the waste size is a pale waste with a light stipple and a soft edge (and so are surface-15 waste shapes when asked).
    /// </summary>
    void Bunkers(SKCanvas c, HoleAnalysis a, LandscapeProjection proj, SKRect bounds, Func<List<Vec2>, bool> own, WasteShapes? waste, Random rnd)
    {
        SKPath Path(IReadOnlyList<Vec2> pts) { using var b = new SKPathBuilder(); b.MoveTo(proj.Pixel(pts[0])); foreach (var q in pts.Skip(1)) b.LineTo(proj.Pixel(q)); b.Close(); return b.Detach(); }
        double wasteM2 = _o.WasteThresholdSqYd * HoleAnalysis.Yard * HoleAnalysis.Yard;
        void Stipple(SKPath area, SKColor dot, float perPixels)
        {
            var box = SKRect.Intersect(area.Bounds, bounds);
            if (box.IsEmpty) return;
            c.Save(); c.ClipPath(area, antialias: true);
            using var paint = Fill(dot);
            for (int i = 0; i < box.Width * box.Height / perPixels; i++)
                c.DrawCircle(box.Left + (float)rnd.NextDouble() * box.Width, box.Top + (float)rnd.NextDouble() * box.Height, 0.9f * Q, paint);
            c.Restore();
        }
        var wasteShapes = (waste ?? WasteShapes.None).Areas.Where(s => s.Count >= 3).ToList();
        foreach (var s in a.Bunkers.Where(own).Where(b => Geometry.Area(b) > wasteM2).Concat(wasteShapes))
        {
            using var bp = Path(s);
            using (var f = Fill(Waste)) c.DrawPath(bp, f);
            Stipple(bp, SandDot.WithAlpha(90), 160 * Q * Q);
            using var soft = Pen(1.6f, 170, 19); c.DrawPath(bp, soft);
        }
        foreach (var b in a.Bunkers.Where(own).Where(b => Geometry.Area(b) <= wasteM2))
        {
            using var bp = Path(b);
            if (SKRect.Intersect(bp.Bounds, bounds).IsEmpty) continue;
            using (var f = Fill(Sand)) c.DrawPath(bp, f);
            Stipple(bp, SandDot.WithAlpha(150), 60 * Q * Q);
            // The face: a fringe of short strokes just outside the edge, on the side away from the player.
            var centre = new Vec2(b.Average(v => v.X), b.Average(v => v.Z));
            bool greenside = (centre - a.Green).Length / HoleAnalysis.Yard <= _o.GreensideBunkerYards;
            var toward = ((greenside ? a.Green : a.BackTee) - centre).Normalized();
            using (var face = Pen(1.0f, 150, 0, 0))
                for (int i = 0; i < b.Count; i++)
                {
                    var (p0w, p1w) = (b[i], b[(i + 1) % b.Count]);
                    int n = Math.Max(1, (int)(Vec2.Distance(p0w, p1w) / 0.9));
                    for (int k = 0; k < n; k++)
                    {
                        var vw = p0w + (p1w - p0w) * ((double)k / n);
                        var outward = (vw - centre).Normalized();
                        if (outward.X * toward.X + outward.Z * toward.Z > -0.15) continue;
                        c.DrawLine(proj.Pixel(vw), proj.Pixel(vw + outward * 1.4), face);
                    }
                }
            using var e = Pen(2.0f, 240, 11); c.DrawPath(bp, e);
        }
    }

    /// <summary>The green's 6 in contour rings (marching squares over a 0.5 m grid) and a few fall arrows, inside the green.</summary>
    static void GreenDetail(SKCanvas c, Heightfield field, LandscapeProjection proj, SKPath green, Random rnd)
    {
        var gb = green.Bounds;
        float cell = Math.Max(3 * Q, 0.5f * proj.PixelsPerMetre);
        int gw = (int)(gb.Width / cell) + 2, gh = (int)(gb.Height / cell) + 2;
        var hv = new double[gw, gh];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < gw; i++)
            for (int j = 0; j < gh; j++)
            {
                hv[i, j] = field.Sample(proj.Unpixel(new SKPoint(gb.Left + i * cell, gb.Top + j * cell)));
                lo = Math.Min(lo, hv[i, j]); hi = Math.Max(hi, hv[i, j]);
            }
        const double interval = 0.1524;   // 6 in
        c.Save(); c.ClipPath(green, antialias: true);
        using var ring = Pen(1.3f, 170, 13, 0.4f);
        for (double lev = Math.Ceiling(lo / interval) * interval; lev < hi; lev += interval)
            for (int i = 0; i < gw - 1; i++)
                for (int j = 0; j < gh - 1; j++)
                {
                    double[] v = [hv[i, j], hv[i + 1, j], hv[i + 1, j + 1], hv[i, j + 1]];
                    var pts = new List<SKPoint>(4);
                    for (int q = 0; q < 4; q++)
                    {
                        double va = v[q], vb = v[(q + 1) % 4];
                        if ((va - lev) * (vb - lev) >= 0) continue;
                        float t = (float)((lev - va) / (vb - va));
                        var (ax, ay) = q switch { 0 => (i, j), 1 => (i + 1, j), 2 => (i + 1, j + 1), _ => (i, j + 1) };
                        var (bx, by) = ((q + 1) % 4) switch { 0 => (i, j), 1 => (i + 1, j), 2 => (i + 1, j + 1), _ => (i, j + 1) };
                        pts.Add(new SKPoint(gb.Left + (ax + (bx - ax) * t) * cell, gb.Top + (ay + (by - ay) * t) * cell));
                    }
                    if (pts.Count >= 2) c.DrawLine(pts[0], pts[1], ring);
                    if (pts.Count == 4) c.DrawLine(pts[2], pts[3], ring);
                }
        using var arrow = Pen(1.6f, 230, 0, 0);
        for (int i = 0; i < 8; i++)
        {
            var p = new SKPoint(gb.Left + (0.2f + 0.6f * (float)rnd.NextDouble()) * gb.Width, gb.Top + (0.2f + 0.6f * (float)rnd.NextDouble()) * gb.Height);
            if (!green.Contains(p.X, p.Y)) continue;
            var wpt = proj.Unpixel(p);
            var g = field.Gradient(wpt);
            if (g.Length < 0.008) continue;
            var down = proj.Pixel(wpt - g.Normalized()) - p;
            float dl = MathF.Max(0.001f, down.Length);
            var d = new SKPoint(down.X / dl, down.Y / dl);
            var tip = new SKPoint(p.X + d.X * 16 * Q, p.Y + d.Y * 16 * Q);
            c.DrawLine(p, tip, arrow);
            c.DrawLine(tip, new SKPoint(tip.X - d.X * 6 * Q + d.Y * 4 * Q, tip.Y - d.Y * 6 * Q - d.X * 4 * Q), arrow);
            c.DrawLine(tip, new SKPoint(tip.X - d.X * 6 * Q - d.Y * 4 * Q, tip.Y - d.Y * 6 * Q + d.X * 4 * Q), arrow);
        }
        c.Restore();
    }

    /// <summary>Fades the art into the paper at the zone's edges (60 units).</summary>
    static void Feather(SKCanvas c, int w, int h)
    {
        float f = 60 * R;
        void Band(SKPoint from, SKPoint to, SKRect rect)
        {
            using var p = new SKPaint { Shader = SKShader.CreateLinearGradient(from, to, [Paper, Paper.WithAlpha(0)], SKShaderTileMode.Clamp) };
            c.DrawRect(rect, p);
        }
        Band(new SKPoint(0, 0), new SKPoint(f, 0), new SKRect(0, 0, f, h));
        Band(new SKPoint(w, 0), new SKPoint(w - f, 0), new SKRect(w - f, 0, w, h));
        Band(new SKPoint(0, 0), new SKPoint(0, f), new SKRect(0, 0, w, f));
        Band(new SKPoint(0, h), new SKPoint(0, h - f), new SKRect(0, h - f, w, h));
    }

    // ───────────────────────── page ─────────────────────────

    public void DrawPage(SKCanvas canvas, string courseName, Plate plate)
    {
        var a = plate.Analysis;
        canvas.Clear(Paper);
        using (var paint = new SKPaint { IsAntialias = true }) canvas.DrawImage(plate.Art, ArtZone, new SKSamplingOptions(SKFilterMode.Linear), paint);

        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        using var muted = new SKPaint { IsAntialias = true, Color = Ink.WithAlpha(170) };

        // Yardages along the top from the back tee: every 100 yds, or every 50 on a short hole.
        var proj = (LandscapeProjection)plate.Projection;
        using (var hand = new SKFont(_hand, 15))
        using (var tick = new SKPaint { IsAntialias = true, Color = Ink.WithAlpha(160), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f })
        {
            int step = a.LengthYards > 260 ? 100 : 50;
            for (int y = step; y < a.LengthYards; y += step)
            {
                float x = proj.Page(a.BackTee + a.Forward * (y * HoleAnalysis.Yard)).X;
                canvas.DrawText($"{y} yds", x, 222, SKTextAlign.Center, hand, ink);
                canvas.DrawLine(x, 228, x, 238, tick);
            }
        }

        void Text(string text, float x, float y, SKTypeface face, float size, SKTextAlign align, SKPaint paint, float maxWidth = 640)
        {
            using var font = new SKFont(face, size) { Subpixel = true };
            float width = font.MeasureText(text);
            if (width > maxWidth) font.Size = size * maxWidth / width;
            canvas.DrawText(text, x, y, align, font, paint);
        }
        // Top left: the course and its place. Top right: the hole, its name if it has one, and its yards.
        Text(courseName, 90, 105, _serifBold, 28, SKTextAlign.Left, ink);
        string place = _o.PlaceLines.Count > 0 ? string.Join(", ", _o.PlaceLines) : "PGA TOUR 2K25";
        Text(place, 90, 132, _serif, 20, SKTextAlign.Left, ink);
        Text($"Hole {a.Hole.Number}  ·  Par {a.Hole.Par}", PageWidth - 90, 105, _serif, 20, SKTextAlign.Right, ink);
        Text($"{a.LengthYards} yards", PageWidth - 90, 132, _serifBold, 28, SKTextAlign.Right, ink);
        if (!string.IsNullOrWhiteSpace(plate.HoleName)) Text(plate.HoleName.Trim(), PageWidth - 90, 156, _serif, 15, SKTextAlign.Right, muted);

        // Bottom left: the designer and year. Bottom right: the course seal, then the credits.
        if (!string.IsNullOrWhiteSpace(_o.Credit)) Text(_o.Credit, 90, 925, _serifBold, 26, SKTextAlign.Left, ink);
        Text(_o.Year.ToString(), 90, 952, _serif, 20, SKTextAlign.Left, ink);
        if (_o.Logos?.Get(LogoKind.Seal, LogoTone.Ink)?.Image is { } seal)
            CourseLogoArt.Draw(canvas, seal, new SKRect(PageWidth - 150, 868, PageWidth - 90, 928));
        Text(StyleCredit, PageWidth - 90, 948, _serif, 11, SKTextAlign.Right, muted);
        Text(Attribution.Text, PageWidth - 90, 964, _serif, 9, SKTextAlign.Right, muted);
    }
}
