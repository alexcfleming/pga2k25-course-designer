using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>Surface-15 waste near a hole (drawn only when <see cref="IllustrationOptions.Waste"/> is on).</summary>
public sealed record WasteShapes(IReadOnlyList<List<Vec2>> Areas, IReadOnlyList<Ribbon> Ribbons, IReadOnlyList<Disc> Brushes)
{
    public static readonly WasteShapes None = new([], [], []);
}

/// <summary>One hole's plate: the art raster (the art zone at 300 dpi) and what the vector pass prints round it.</summary>
public sealed class Plate : IDisposable
{
    public required HoleAnalysis Analysis { get; init; }
    public required PlateProjection Projection { get; init; }
    public required int RulerTop { get; init; }
    public required float UnitsPerYard { get; init; }
    public required string? HoleName { get; init; }
    public required SKImage Art { get; init; }

    /// <summary>Darkness of the art at a raster pixel, before encoding (for tests).</summary>
    public required Func<int, int, float> Darkness { get; init; }

    /// <summary>Back tee and green centre on the page (units).</summary>
    public SKPoint BackTee => Projection.Page(Analysis.BackTee);
    public SKPoint Green => Projection.Page(Analysis.Green);

    public void Dispose() => Art.Dispose();
}

/// <summary>
/// The Hole Illustrations product: one watercolour-and-ink plate per hole on US Letter portrait. The art is raster
/// (washes multiplied over white paper, ink composited in) and the frame, ruler and every word are vectors on top, so
/// type stays sharp and searchable in the PDF. The look is after the hole plates posted by Jim Ryan and illustrated by
/// Tyler J. Rae (both credited on every plate); the geometry is the yardage book's hole analysis with its numbers left out.
/// </summary>
public sealed class HoleIllustrationRenderer : IDisposable
{
    /// <summary>The inspiration credit printed on every plate, before the designer credit.</summary>
    public const string StyleCredit = "AFTER THE HOLE PLATES OF JIM RYAN · ILLUSTRATED BY TYLER J. RAE";

    public const string SubCredit = "PGA TOUR 2K25 · ILLUSTRATED FROM THE COURSE FILE";

    readonly IllustrationOptions _o;
    readonly IllustrationStyle _s;
    readonly SKTypeface _regular, _italic;

    /// <summary>The family the plates are set in ("Lora", else the fallback the renderer found).</summary>
    public string FontFamily { get; }

    public HoleIllustrationRenderer(IllustrationOptions options, IProgress<string>? log = null)
    {
        _o = options;
        _s = options.Style;
        // Bundled Lora (fonts/lora next to the app), else an installed Lora, else Georgia.
        string fonts = Path.Combine(AppContext.BaseDirectory, "fonts", "lora");
        var bundled = File.Exists(Path.Combine(fonts, "Lora[wght].ttf")) ? SKTypeface.FromFile(Path.Combine(fonts, "Lora[wght].ttf")) : null;
        var bundledItalic = File.Exists(Path.Combine(fonts, "Lora-Italic[wght].ttf")) ? SKTypeface.FromFile(Path.Combine(fonts, "Lora-Italic[wght].ttf")) : null;
        if (bundled is not null)
        {
            _regular = bundled;
            _italic = bundledItalic ?? SKTypeface.FromFamilyName("Lora", SKFontStyle.Italic);
            FontFamily = "Lora";
        }
        else
        {
            FontFamily = new[] { "Lora", "Georgia" }.FirstOrDefault(f =>
                string.Equals(SKTypeface.FromFamilyName(f)?.FamilyName, f, StringComparison.OrdinalIgnoreCase)) ?? SKTypeface.Default.FamilyName;
            _regular = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
            _italic = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Italic);
            if (FontFamily != "Lora") log?.Report($"Hole illustrations: Lora isn't bundled or installed; setting the plates in {FontFamily}.");
        }
    }

    public void Dispose() { _regular.Dispose(); _italic.Dispose(); }

    // ───────────────────────── art ─────────────────────────

    public Plate Render(HoleAnalysis a, string? holeName = null, WasteShapes? waste = null)
    {
        int rulerTop = PlateLayout.RulerTop(a.LengthYards, _o.RulerMarginYards, _o.RulerStepYards);
        float upy = PlateLayout.UnitsPerYard(rulerTop);
        var proj = new PlateProjection(a.RotationDegrees, a.Route, upy);
        int w = (int)Math.Round(PlateLayout.ArtZone.Width * PlateLayout.RasterScale), h = (int)Math.Round(PlateLayout.ArtZone.Height * PlateLayout.RasterScale);
        var c = new WatercolourCanvas(w, h, a.Hole.Number);
        var rng = new Random(a.Hole.Number * 7919 + 17);
        float ppy = proj.PixelsPerYard, ppm = proj.PixelsPerMetre;

        bool Own(List<Vec2> s) => !a.Foreign.Contains(s);
        var ownFairways = a.Fairways.Where(Own).Where(f => f.Count >= 3).ToList();
        var ownGreens = a.Greens.Where(Own).Where(g => g.Count >= 3).ToList();
        var ownBunkers = a.Bunkers.Where(Own).Where(b => b.Count >= 3).ToList();
        double wasteM2 = _o.WasteThresholdSqYd * HoleAnalysis.Yard * HoleAnalysis.Yard;
        var bigSand = ownBunkers.Where(b => Geometry.Area(b) > wasteM2).ToList();
        var bunkers = ownBunkers.Where(b => Geometry.Area(b) <= wasteM2).ToList();
        var teePads = a.TeeSets.Select(t => t.Tee).Distinct().Select(t => TeePad(t, a.Forward)).ToList();
        var route = proj.Pixels(a.Route);
        var greenPx = proj.Pixel(a.Green);
        var backPx = proj.Pixel(a.BackTee);

        // 1. Rough: a sage wash round this hole's corridor with a ragged edge, and the fade every background layer shares.
        var corr = c.Mask((cv, p) =>
        {
            foreach (var s in ownFairways.Concat(ownGreens).Concat(ownBunkers).Concat(teePads)) { using var path = proj.Path(s); cv.DrawPath(path, p); }
            using var line = PlateProjection.PixelPath(route, close: false);
            using var buf = InkLines.Stroke(SKColors.White, (float)(2 * _o.RouteBufferYards * ppy));
            cv.DrawPath(line, buf);
        }, blur: 0);
        var dist = c.Distance(corr, v => v >= 0.5f);
        float reach0 = (float)(_o.RoughReachYards * ppy);
        var rough = c.Blur(c.Map(i =>
        {
            float reach = reach0 * (0.75f + 0.5f * c.N1[i]);
            return MathF.Pow(Math.Clamp(1 - (dist[i] - reach * 0.45f) / (reach * 0.55f), 0, 1), 1.3f);
        }), 6);
        var fade = c.Map(i =>
        {
            float reach = reach0 * (0.75f + 0.5f * c.N1[i]);
            return Math.Clamp(1 - (dist[i] - reach) / (1.4f * reach), 0, 1);
        });
        var corrSoft = c.Blur(corr, 25);
        var keep = c.Map(i => Math.Max(fade[i], corrSoft[i]));   // background kept near this hole's play
        c.Wash(rough, _s.Rough, 1, edge: 0, grain: 0.26f);
        var roughBlur = c.Blur(rough, 14);
        c.Wash(c.Map(i => Math.Clamp(rough[i] - roughBlur[i], 0, 1) * 2.5f), _s.RoughRing, 0.5f, edge: 0, grain: 0.3f);
        c.Wash(c.Map(i => c.N2[i] > 0.55f ? rough[i] * 0.5f : 0), _s.RoughMottle, 0.35f, edge: 0, grain: 0.3f);

        // 2. Water (planes and traced bodies, islands kept dry), then other holes' surfaces, pale and unlined.
        using (var waterBuilder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd })
        {
            foreach (var s in a.Water.Where(s => s.Count >= 3)) PlateProjection.AddPixels(waterBuilder, proj.Pixels(s));
            foreach (var body in a.WaterBodies) foreach (var shore in body.Where(s => s.Count >= 3)) PlateProjection.AddPixels(waterBuilder, proj.Pixels(shore));
            using var water = waterBuilder.Detach();
            if (!water.IsEmpty)
            {
                var wm = c.Mask(water);
                c.Wash(c.Map(i => wm[i] * keep[i]), _s.Water, 1, edge: 0.35f);
                c.Ink(cv => { using var wob = InkLines.Wobble(water, 1.0f, rng); using var ink = InkLines.Stroke(_s.WaterInk, 1.6f); cv.DrawPath(wob, ink); });
            }
        }
        var foreignTurf = c.Mask((cv, p) =>
        {
            foreach (var s in a.Fairways.Concat(a.Greens).Where(s => !Own(s) && s.Count >= 3)) { using var path = proj.Path(s); cv.DrawPath(path, p); }
        });
        c.Wash(c.Map(i => foreignTurf[i] * fade[i]), _s.Foreign, 0.55f, edge: 0.05f);
        var foreignSand = c.Mask((cv, p) =>
        {
            foreach (var s in a.Bunkers.Where(s => !Own(s) && s.Count >= 3)) { using var path = proj.Path(s); cv.DrawPath(path, p); }
        });
        c.Cover(c.Map(i => foreignSand[i] * fade[i] * 0.55f), _s.Sand);

        // 4. Fairways: cover, straw wash, mow bands, faint contour hatching, one ink line round the union.
        using var fairwayUnion = InkLines.Union(ownFairways.Select(f => proj.Path(f)));
        var fm = c.Mask(fairwayUnion);
        c.Cover(fm, _s.Cover);
        c.Wash(fm, _s.Fairway, 1, edge: 0.20f, grain: 0.20f);
        float zeroPx = (PlateLayout.ZeroY - PlateLayout.ArtZone.Top) * PlateLayout.RasterScale;
        float band = _o.MowBandYards * ppy;
        c.Wash(c.Map(i => ((int)Math.Floor((zeroPx - i / w) / band) & 1) == 0 ? fm[i] * 0.5f : 0), _s.MowBand, 0.30f, edge: 0, grain: 0.1f);
        c.Ink(cv =>
        {
            cv.Save();
            cv.ClipPath(fairwayUnion, antialias: true);
            using var line = InkLines.Stroke(_s.FairwayContour, 1.2f);
            for (int yd = 0; yd <= PlateLayout.RulerTop(a.LengthYards) + 60; yd += 9)
            {
                float y = zeroPx - yd * ppy;
                var pts = new List<SKPoint>();
                for (int x = 0; x <= w; x += 12) pts.Add(new SKPoint(x, y + 6 * MathF.Sin(x / 90f + yd)));
                using var path = PlateProjection.PixelPath(pts, close: false);
                cv.DrawPath(path, line);
            }
            cv.Restore();
            using var wob = InkLines.Wobble(fairwayUnion, 1.0f, rng);
            using var ink = InkLines.Stroke(_s.FairwayInk, 2.4f);
            cv.DrawPath(wob, ink);
        });

        // 5. Tee pads.
        using (var tees = InkLines.Union(teePads.Select(t => proj.Path(t))))
        {
            var tm = c.Mask(tees);
            c.Cover(tm, _s.Cover);
            c.Wash(tm, _s.Fairway, 1, edge: 0.2f);
            c.Ink(cv => { using var wob = InkLines.Wobble(tees, 0.6f, rng); using var ink = InkLines.Stroke(_s.FairwayInk.WithAlpha(220), 2.0f); cv.DrawPath(wob, ink); });
        }

        // 6. Vegetation: scalloped canopies with a cast shadow and leaf speckle; then rock outcrops.
        var masses = a.Vegetation.Where(v => v.Count >= 3).Select(proj.Pixels).ToList();
        var canopy = CanopyShapes.Mask(c, masses, (float)(_o.CanopyBumpYards * ppy), rng);
        // Crowns never sit on this hole's tees, fairways or greens (a tee complex cut into scrub stays clear).
        var play = c.Mask((cv, p) =>
        {
            foreach (var s in ownFairways.Concat(ownGreens).Concat(teePads)) { using var path = proj.Path(s); cv.DrawPath(path, p); }
        }, blur: 0);
        var playGrown = c.Distance(play, v => v >= 0.5f);
        canopy = c.Blur(c.Map(i => canopy[i] * Math.Clamp(playGrown[i] - 4, 0, 1)), 0.8f);
        var shadow = c.Shift(c.Blur(canopy, 7), 11, 14);
        c.Wash(c.Map(i => shadow[i] * (1 - canopy[i]) * keep[i]), _s.CanopyShadow, 0.45f, edge: 0, grain: 0.2f);
        var canopyKept = c.Map(i => canopy[i] * keep[i]);
        // Crowns are lit from above: lift them most of the way back to paper so they read pale over the rough.
        c.Cover(canopyKept, _s.Cover, 0.6f);
        c.Wash(canopyKept, _s.Canopy, 1, edge: 0.35f, grain: 0.35f);
        c.Wash(c.Map(i => canopyKept[i] * Math.Clamp((c.NF[i] - 0.45f) * 2.2f, 0, 1) * (0.5f + c.N2[i])), _s.CanopySpeckle, 0.3f, edge: 0, grain: 0);
        var canopyEdge = CanopyShapes.EdgeInk(c, canopy, 1.4f);
        c.Cover(c.Map(i => canopyEdge[i] * Math.Clamp((keep[i] - 0.35f) * 4, 0, 1)), _s.CanopyInk.WithAlpha(255), _s.CanopyInk.Alpha / 255f);

        var rocks = a.Outcrops.Where(o => o.Count >= 3).ToList();
        if (rocks.Count > 0)
        {
            var rm = c.Mask((cv, p) => { foreach (var r in rocks) { using var path = proj.Path(r); cv.DrawPath(path, p); } });
            var rk = c.Map(i => rm[i] * keep[i]);
            c.Cover(rk, _s.Cover, 0.5f);
            c.Wash(rk, _s.Rock, 1, edge: 0.4f);
            c.Ink(cv =>
            {
                using var ink = InkLines.Stroke(_s.RockInk, 1.4f);
                foreach (var r in rocks)
                {
                    var px = proj.Pixels(r);
                    if (!a.InPlay.Contains(r) && Sample(keep, w, h, InkLines.Centroid(px)) <= 0.35f) continue;
                    using var path = PlateProjection.PixelPath(px);
                    using var wob = InkLines.Wobble(path, 0.8f, rng);
                    cv.DrawPath(wob, ink);
                }
            });
        }

        // Paths: white ribbons with grey edges over the planting, only where the plate hasn't faded out.
        c.Ink(cv =>
        {
            foreach (var r in a.Paths)
            {
                float width = Math.Max(6, (float)(r.Width * ppm * 1.4));
                using var edge = InkLines.Stroke(_s.PathEdge, width + 5);
                using var ribbon = InkLines.Stroke(_s.Path, width);
                foreach (var run in Runs(proj.Pixels(r.Points), p => Sample(fade, w, h, p) > 0.5f))
                {
                    using var path = PlateProjection.PixelPath(run, close: false);
                    cv.DrawPath(path, edge);
                    cv.DrawPath(path, ribbon);
                }
            }
        });

        // 7. Big sand reads as waste: a peach wash, sparse stipple, a soft line, no hachures.
        waste = _o.Waste ? waste ?? WasteShapes.None : WasteShapes.None;
        if (bigSand.Count > 0 || waste.Areas.Count + waste.Ribbons.Count + waste.Brushes.Count > 0)
        {
            var wm = c.Mask((cv, p) =>
            {
                foreach (var s in bigSand.Concat(waste.Areas).Where(s => s.Count >= 3)) { using var path = proj.Path(s); cv.DrawPath(path, p); }
                foreach (var r in waste.Ribbons)
                {
                    using var path = proj.Path(r.Points, close: false);
                    using var stroke = InkLines.Stroke(SKColors.White, (float)(r.Width * ppm));
                    cv.DrawPath(path, stroke);
                }
                foreach (var d in waste.Brushes) cv.DrawCircle(proj.Pixel(d.Centre), (float)(d.Radius * ppm), p);
            });
            if (waste.Areas.Count + waste.Ribbons.Count + waste.Brushes.Count > 0) wm = c.Map(i => wm[i] * (bigSand.Count > 0 ? 1 : keep[i]));
            c.Cover(c.Map(i => wm[i] * 0.85f), _s.Cover);
            c.Wash(wm, _s.Waste, 0.85f, edge: 0.25f, grain: 0.35f);
            var wasteEdge = CanopyShapes.EdgeInk(c, wm, 1.5f);
            c.Cover(wasteEdge, _s.WasteInk.WithAlpha(255), _s.WasteInk.Alpha / 255f);
            double area = 0; for (int i = 0; i < wm.Length; i++) area += wm[i];
            c.Ink(cv =>
            {
                using var dot = InkLines.Fill(_s.WasteStipple);
                // One dot per 900 px² of waste, scattered over its mask.
                int n = (int)(area / 900), placed = 0;
                for (int tries = 0; placed < n && tries < n * 200; tries++)
                {
                    int x = rng.Next(w), y = rng.Next(h);
                    float r = 0.8f + 0.6f * (float)rng.NextDouble();
                    if (wm[y * w + x] <= 0.5f) continue;
                    cv.DrawCircle(x, y, r, dot);
                    placed++;
                }
            });
        }

        // 8. Bunkers: grey lip, white sand, stipple, hachured face, ink edge.
        if (bunkers.Count > 0)
        {
            var b0 = c.Mask((cv, p) => { foreach (var b in bunkers) { using var path = proj.Path(b); cv.DrawPath(path, p); } }, blur: 0);
            var outside = c.Distance(b0, v => v >= 0.5f);
            var grown = c.Blur(c.Map(i => Math.Clamp(7.5f - outside[i], 0, 1)), 2);
            var b0Blur = c.Blur(b0, 2);
            c.Wash(c.Map(i => Math.Clamp(grown[i] - b0Blur[i], 0, 1)), _s.Lip, 0.55f, edge: 0, grain: 0.3f);
            c.Cover(c.Blur(b0, 0.6f), _s.Sand);
            float greenside = (float)(_o.GreensideBunkerYards * ppy);
            c.Ink(cv =>
            {
                foreach (var b in bunkers)
                {
                    var ring = proj.Pixels(b);
                    var target = SKPoint.Distance(InkLines.Centroid(ring), greenPx) < greenside ? greenPx : backPx;
                    BunkerInk.Draw(cv, ring, target, _s, rng);
                }
            });
        }

        // 9. Greens: collar, cover, blush wash, the firmest ink line on the plate.
        if (ownGreens.Count > 0)
        {
            using var greens = InkLines.Union(ownGreens.Select(g => proj.Path(g)));
            var g0 = c.Mask(greens, blur: 0);
            var outside = c.Distance(g0, v => v >= 0.5f);
            var collar = c.Blur(c.Map(i => Math.Clamp(9.5f - outside[i], 0, 1)), 2);
            var gm = c.Blur(g0, 0.8f);
            c.Wash(c.Map(i => Math.Clamp(collar[i] - gm[i], 0, 1)), _s.Collar, 0.7f, edge: 0);
            c.Cover(gm, _s.Cover);
            c.Wash(gm, _s.Green, 1, edge: 0.3f, grain: 0.12f);
            c.Ink(cv => { using var wob = InkLines.Wobble(greens, 0.8f, rng); using var ink = InkLines.Stroke(_s.Ink.WithAlpha(235), 2.8f); cv.DrawPath(wob, ink); });
        }

        // 10. Line of play: fine dashed ink from the back tee through the aim points to the green centre.
        if (_o.ShowLineOfPlay)
            c.Ink(cv =>
            {
                using var path = PlateProjection.PixelPath(route, close: false);
                using var ink = InkLines.Stroke(_s.LineOfPlay, 1.8f);
                ink.StrokeCap = SKStrokeCap.Butt;
                using var dash = SKPathEffect.CreateDash([14, 10], 0);
                ink.PathEffect = dash;
                cv.DrawPath(path, ink);
            });

        // 11. Feather into the paper at the zone's edges (a tighter feather under the tee), then the paper grain.
        c.FadeTo(ZoneFade(w, h));
        c.Grain();

        return new Plate
        {
            Analysis = a, Projection = proj, RulerTop = rulerTop, UnitsPerYard = upy, HoleName = holeName,
            Art = c.ToImage(), Darkness = c.Darkness,
        };
    }

    /// <summary>A 5 × 9 m tee pad (world), the long side along the line of play.</summary>
    static List<Vec2> TeePad(Vec2 centre, Vec2 forward)
    {
        var f = forward.Normalized();
        var s = new Vec2(f.Z, -f.X);
        return [centre - f * 4.5 - s * 2.5, centre - f * 4.5 + s * 2.5, centre + f * 4.5 + s * 2.5, centre + f * 4.5 - s * 2.5];
    }

    /// <summary>The art zone's feather: 0 at its edges, 1 inside, over 135 px (45 px under the tee end).</summary>
    public static float[] ZoneFade(int w, int h)
    {
        const float Side = 135, Bottom = 50;
        static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
        var z = new float[w * h];
        Parallel.For(0, h, y =>
        {
            float fy = Smooth(y / Side) * Smooth((h - 1 - y) / Bottom);
            for (int x = 0; x < w; x++) z[y * w + x] = fy * Smooth(x / Side) * Smooth((w - 1 - x) / Side);
        });
        return z;
    }

    static float Sample(float[] field, int w, int h, SKPoint p)
    {
        int x = (int)p.X, y = (int)p.Y;
        return x < 0 || y < 0 || x >= w || y >= h ? 0 : field[y * w + x];
    }

    /// <summary>Splits a polyline into the runs of consecutive points that pass <paramref name="keep"/>.</summary>
    static IEnumerable<List<SKPoint>> Runs(List<SKPoint> pts, Func<SKPoint, bool> keep)
    {
        var run = new List<SKPoint>();
        foreach (var p in pts)
        {
            if (keep(p)) { run.Add(p); continue; }
            if (run.Count >= 2) yield return run;
            run = [];
        }
        if (run.Count >= 2) yield return run;
    }

    // ───────────────────────── page (vectors) ─────────────────────────

    /// <summary>Draws the whole plate in page units: the art raster in the art zone, then frame, ruler and type as vectors.</summary>
    public void DrawPage(SKCanvas canvas, string courseName, Plate plate)
    {
        var a = plate.Analysis;
        canvas.Clear(SKColors.White);
        using (var paint = new SKPaint { IsAntialias = true })
            canvas.DrawImage(plate.Art, PlateLayout.ArtZone, new SKSamplingOptions(SKFilterMode.Linear), paint);
        using (var frame = InkLines.Stroke(_s.Ink, 100f / 72))
        {
            frame.StrokeJoin = SKStrokeJoin.Miter;
            canvas.DrawRect(PlateLayout.Frame, frame);
        }

        const float Left = 64, Right = 786;
        Spaced(canvas, courseName.ToUpperInvariant(), Left, 90, 30, _regular, _s.Ink, 0.10f);
        if (!string.IsNullOrWhiteSpace(plate.HoleName))
            Spaced(canvas, plate.HoleName.Trim(), Left + 4, 125, 15, _italic, _s.Muted, 0);
        Spaced(canvas, $"HOLE NO. {a.Hole.Number}", Right, 84, 19, _regular, _s.Ink, 0.12f, SKTextAlign.Right);
        Spaced(canvas, $"PAR {a.Hole.Par}", Right, 112, 13, _regular, _s.Ink, 0.14f, SKTextAlign.Right);
        Spaced(canvas, $"{a.LengthYards} YARDS", Right, 134, 13, _regular, _s.Ink, 0.14f, SKTextAlign.Right);

        // Ruler: a tick every 50 yds straight up the page from the back tee (the Wakonda convention).
        using (var tick = InkLines.Stroke(_s.Ink, 1f))
        {
            tick.StrokeCap = SKStrokeCap.Butt;
            using var font = Font(_regular, 14);
            float capHalf = font.Metrics.CapHeight / 2;
            for (int yd = 0; yd <= plate.RulerTop; yd += _o.RulerStepYards)
            {
                float y = PlateLayout.ZeroY - yd * plate.UnitsPerYard;
                canvas.DrawLine(58, y, 76, y, tick);
                float end = Spaced(canvas, yd.ToString(System.Globalization.CultureInfo.InvariantCulture), 92, y + capHalf, 14, _regular, _s.Ink, 0.04f);
                if (yd == 0) Spaced(canvas, "YDS.", end + 7, y + capHalf, 10, _regular, _s.Ink, 0.10f);
            }
        }

        // Footer: the inspiration credit, then the designer, then the product line and the studio's attribution.
        Spaced(canvas, StyleCredit, Left, 994, 8.5f, _regular, _s.Ink, 0.14f);
        if (!string.IsNullOrWhiteSpace(_o.Credit))
            Spaced(canvas, $"DESIGNED BY {_o.Credit.Trim().ToUpperInvariant()} · {_o.Year}", Left, 1014, 11, _regular, _s.Ink, 0.12f);
        Spaced(canvas, SubCredit, Left, 1032, 8, _regular, _s.Muted, 0.14f);
        Spaced(canvas, Attribution.Text, Left, 1047, 6.5f, _italic, _s.Muted, 0.02f);
        if (_o.ShowTeeStrip && a.TeeSets.Count > 0)
            Spaced(canvas, string.Join("   ", a.TeeSets.Select(t => $"{t.Name.ToUpperInvariant()} {t.Yards}")), Right, 1032, 8, _regular, _s.Muted, 0.14f, SKTextAlign.Right);
    }

    static SKFont Font(SKTypeface face, float points) => new(face, points * 100f / 72) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };

    /// <summary>Tracked text (tracking as a fraction of the size) as one positioned run; returns the right edge.</summary>
    static float Spaced(SKCanvas canvas, string text, float x, float y, float points, SKTypeface face, SKColor colour, float tracking, SKTextAlign align = SKTextAlign.Left)
    {
        if (string.IsNullOrEmpty(text)) return x;
        using var font = Font(face, points);
        var glyphs = font.GetGlyphs(text);
        var widths = font.GetGlyphWidths(glyphs);
        float track = tracking * font.Size;
        float total = widths.Sum() + track * (glyphs.Length - 1);
        float start = align == SKTextAlign.Right ? x - total : align == SKTextAlign.Center ? x - total / 2 : x;
        var pos = new SKPoint[glyphs.Length];
        float cx = start;
        for (int i = 0; i < glyphs.Length; i++) { pos[i] = new SKPoint(cx, y); cx += widths[i] + track; }
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(font, glyphs.Length);
        glyphs.CopyTo(run.Glyphs);
        pos.CopyTo(run.Positions);
        using var blob = builder.Build();
        using var paint = new SKPaint { IsAntialias = true, Color = colour };
        if (blob is not null) canvas.DrawText(blob, 0, 0, paint);
        return start + total;
    }
}
