using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// The Course Portrait scorecard map style: the whole course as a watercolour aerial cut-out on white paper, north
/// up, fitted to its property parcel (<see cref="PortraitParcel"/>), with a centred spaced-caps title, the designer
/// credit and "IN THE STYLE OF BRUSH &amp; BIRDIE" underneath. No scorecard, numbers, legend or scale: it is a wall
/// print. 12 × 16 in (or 16 × 12 when the parcel is wider than tall). The art is a raster (PNG, and embedded in the
/// PDF as a JPEG); the type is vector in both. Seeded from the course name, so a course renders the same twice.
/// Pixel sizes below are those of the 300 dpi print and scale with the resolution.
/// </summary>
public sealed class CoursePortraitRenderer : IDisposable
{
    public const float LongSide = 1600, ShortSide = 1200;   // 16 × 12 in at 100 units/in
    public const string StyleCredit = "IN THE STYLE OF BRUSH & BIRDIE";

    readonly ScorecardMapOptions _o;
    readonly CourseModel _course;
    readonly CourseLayers _layers;
    readonly SKTypeface _face;
    SKImage? _art;
    float _artScale;

    /// <summary>The parcel in world X/Z; empty for a course with no holes.</summary>
    public IReadOnlyList<Vec2> Parcel { get; }
    public SKSize Page { get; }
    public bool Landscape { get; }

    /// <summary>Land as the foam line closes it (world X/Z); null when there's no coast to draw.</summary>
    public IReadOnlyList<Vec2>? Land { get; }

    public CoursePortraitRenderer(CourseModel course, CourseLayers layers, ScorecardMapOptions options)
    {
        _course = course; _layers = layers; _o = options;
        Parcel = PortraitParcel.Build(layers);
        var (minX, minY, maxX, maxY) = Bounds(Parcel.Select(North));
        Landscape = maxX - minX >= maxY - minY;
        Page = Landscape ? new SKSize(LongSide, ShortSide) : new SKSize(ShortSide, LongSide);
        Land = options.ShowCoast ? CoastLand(course, Parcel) : null;
        _face = LoraFace();
    }

    public void Dispose() { _art?.Dispose(); _face.Dispose(); }

    /// <summary>North up: x' = −X (east to the right), y' = Z (south down).</summary>
    static SKPoint North(Vec2 p) => new((float)-p.X, (float)p.Z);

    static (float MinX, float MinY, float MaxX, float MaxY) Bounds(IEnumerable<SKPoint> pts)
    {
        var l = pts.ToList();
        return l.Count == 0 ? (0, 0, 1, 1) : (l.Min(p => p.X), l.Min(p => p.Y), l.Max(p => p.X), l.Max(p => p.Y));
    }

    /// <summary>The OceanFoam objects that trace the shore.</summary>
    public static List<Vec2> Foam(CourseModel course) =>
        course.PlacedObjects.Where(o => o.AssetPath.Contains("OceanFoam", StringComparison.OrdinalIgnoreCase)).Select(o => o.Position).ToList();

    /// <summary>
    /// The land the foam line closes: the foam points joined in order of bearing round the parcel's centre (a concave
    /// hull collapses to a sliver on a coast). No foam, no coast: water is never invented.
    /// </summary>
    public static List<Vec2>? CoastLand(CourseModel course, IReadOnlyList<Vec2> parcel)
    {
        var foam = Foam(course);
        if (foam.Count < 3 || parcel.Count < 3) return null;
        var c = new Vec2(parcel.Average(p => p.X), parcel.Average(p => p.Z));
        return foam.OrderBy(p => Math.Atan2(p.Z - c.Z, p.X - c.X)).ThenBy(p => Vec2.Distance(p, c)).ToList();
    }

    /// <summary>Lora (bundled in fonts/lora next to the app, else installed), else Georgia.</summary>
    static SKTypeface LoraFace()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "fonts", "lora", "Lora[wght].ttf");
        if (File.Exists(file) && SKTypeface.FromFile(file) is { } bundled) return bundled;
        foreach (var f in new[] { "Lora", "Georgia" })
            if (SKTypeface.FromFamilyName(f) is { } t && string.Equals(t.FamilyName, f, StringComparison.OrdinalIgnoreCase)) return t;
        return SKTypeface.Default;
    }

    /// <summary>A stable seed from the course name (string.GetHashCode changes from run to run).</summary>
    static int Seed(string name)
    {
        uint h = 2166136261;
        foreach (char ch in name) { h ^= ch; h *= 16777619; }
        return (int)(h % 100000) + 11;
    }

    // ───────────────────────── page ─────────────────────────

    /// <summary>Draws the page in page units (1/100 in): the art at <paramref name="pxPerUnit"/>, then the type as vectors.</summary>
    public void Draw(SKCanvas canvas, float pxPerUnit, bool jpegArt = false)
    {
        var art = Art(pxPerUnit);
        var page = new SKRect(0, 0, Page.Width, Page.Height);
        using var sampling = new SKPaint();
        if (jpegArt)
        {
            // The PDF embeds a JPEG of the art (a lossless 300 dpi raster would be tens of MB).
            using var data = art.Encode(SKEncodedImageFormat.Jpeg, 92);
            using var jpeg = SKImage.FromEncodedData(data);
            canvas.DrawImage(jpeg, page, new SKSamplingOptions(SKFilterMode.Linear), sampling);
        }
        else canvas.DrawImage(art, page, new SKSamplingOptions(SKFilterMode.Linear), sampling);
        DrawType(canvas);
    }

    void DrawType(SKCanvas canvas)
    {
        string title = string.IsNullOrWhiteSpace(_o.Title) ? (_course.Name ?? "").ToUpperInvariant() : _o.Title.Trim();
        int year = _o.Year ?? DateTime.Now.Year;
        string credit = string.IsNullOrWhiteSpace(_o.Credit) ? $"{year}" : $"{_o.Credit.Trim().ToUpperInvariant()} · {year}";
        float ty = 0.885f * Page.Height;
        Spaced(canvas, title, ty, 22, SKColor.Parse("#1f1d1a"), 0.22f);
        Spaced(canvas, credit, ty + 36, 7.5f, SKColor.Parse("#6b665e"), 0.28f);
        Spaced(canvas, StyleCredit, ty + 58, 6.5f, SKColor.Parse("#8c877e"), 0.28f);
        Spaced(canvas, Attribution.Text, ty + 76, 5f, SKColor.Parse("#aaa59c"), 0.12f);
    }

    /// <summary>Centred text with letter spacing <paramref name="track"/> × size; <paramref name="pt"/> in points.</summary>
    void Spaced(SKCanvas canvas, string text, float baseline, float pt, SKColor colour, float track)
    {
        if (text.Length == 0) return;
        using var font = new SKFont(_face, pt * 100f / 72f);
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        float maxWidth = Page.Width * 0.84f;
        // A very long title shrinks to fit the page.
        float Total(SKFont f) => text.Sum(ch => f.MeasureText(ch.ToString())) + track * f.Size * (text.Length - 1);
        while (Total(font) > maxWidth && font.Size > 4) font.Size *= 0.95f;
        float x = Page.Width / 2 - Total(font) / 2;
        foreach (char ch in text)
        {
            string s = ch.ToString();
            canvas.DrawText(s, x, baseline, SKTextAlign.Left, font, paint);
            x += font.MeasureText(s) + track * font.Size;
        }
    }

    // ───────────────────────── art ─────────────────────────

    static (float R, float G, float B) Rgb(string hex) { var c = SKColor.Parse(hex); return (c.Red / 255f, c.Green / 255f, c.Blue / 255f); }

    /// <summary>The watercolour art for the whole page at <paramref name="pxPerUnit"/> (3 = 300 dpi), built once per scale.</summary>
    public SKImage Art(float pxPerUnit)
    {
        if (_art is not null && _artScale == pxPerUnit) return _art;
        _art?.Dispose();
        _artScale = pxPerUnit;
        return _art = Paint(pxPerUnit);
    }

    SKImage Paint(float pxPerUnit)
    {
        int W = (int)Math.Round(Page.Width * pxPerUnit), H = (int)Math.Round(Page.Height * pxPerUnit);
        float k = pxPerUnit / 3f;                         // px sizes are the 300 dpi print's
        float K(float px300) => px300 * k;
        int N(int px300) => Math.Max(2, (int)Math.Round(px300 * k));
        int seed = Seed(_course.Name ?? "");
        // N1 = big washes (400 px, 4 octaves), N2 = mottle (90, 3), NF = fine (14, 2); grain (3, 1) on its own.
        var cv = new WatercolourCanvas(W, H, seed, (N(400), 4), (N(90), 3), (N(14), 2));
        var big = cv.N1; var mid = cv.N2; var fine = cv.NF;
        var grain = WatercolourCanvas.Noise(W, H, N(3), 1, seed * 7919 + 4);
        float shortSide = Math.Min(W, H);

        // Fit the parcel into the art box (x 10–90 %, y 8–80 % of the page).
        var (minX, minY, maxX, maxY) = Bounds(Parcel.Select(North));
        float ax0 = 0.10f * W, ay0 = 0.08f * H, ax1 = 0.90f * W, ay1 = 0.80f * H;
        float S = Math.Min((ax1 - ax0) / Math.Max(1, maxX - minX), (ay1 - ay0) / Math.Max(1, maxY - minY));
        float ox = (ax0 + ax1) / 2 - S * (minX + maxX) / 2, oy = (ay0 + ay1) / 2 - S * (minY + maxY) / 2;
        SKPoint Px(Vec2 v) { var n = North(v); return new(n.X * S + ox, n.Y * S + oy); }
        SKPath Poly(IReadOnlyList<Vec2> p) => PortraitParcel.Path(p, Px);

        float[] Polys(IEnumerable<IReadOnlyList<Vec2>> polys, float blur, bool evenOdd = false) => cv.Mask((c, p) =>
        {
            foreach (var poly in polys) if (poly.Count >= 3) using (var path = Poly(poly)) { if (evenOdd) path.FillType = SKPathFillType.EvenOdd; c.DrawPath(path, p); }
        }, blur);
        float[] Lines(IEnumerable<(IReadOnlyList<Vec2> Pts, float Px)> lines, float blur) => cv.Mask((c, p) =>
        {
            p.Style = SKPaintStyle.Stroke; p.StrokeCap = SKStrokeCap.Round; p.StrokeJoin = SKStrokeJoin.Round;
            foreach (var (pts, w) in lines)
            {
                if (pts.Count < 2) continue;
                p.StrokeWidth = Math.Max(1, w);
                using var b = new SKPathBuilder();
                b.MoveTo(Px(pts[0]));
                foreach (var q in pts.Skip(1)) b.LineTo(Px(q));
                using var path = b.Detach();
                c.DrawPath(path, p);
            }
        }, blur);
        float[] Discs(IEnumerable<(Vec2 At, float R)> discs, float blur) => cv.Mask((c, p) =>
        {
            foreach (var (at, r) in discs) c.DrawCircle(Px(at), r, p);
        }, blur);
        void Tint(float[] m, string hex, float strength) => cv.Wash(m, SKColor.Parse(hex), strength, edge: 0, grain: 0);
        void PaintOver(float[] m, string hex, float alpha) => cv.Cover(m, SKColor.Parse(hex), alpha);
        float[] Clip(float[] m, float[] by) => cv.Map(i => m[i] * by[i]);

        // 1. Watercolour bleed round the parcel.
        var pm = Parcel.Count >= 3 ? Polys([Parcel], 0) : new float[W * H];
        var distOut = cv.Distance(pm, v => v >= 0.5f);
        float reach = 0.045f * shortSide;
        var bleed = cv.Map(i => pm[i] >= 0.5f ? 0 : MathF.Pow(Math.Clamp(1 - distOut[i] / (reach * (0.5f + 0.9f * big[i])), 0, 1), 1.4f));
        Tint(cv.Map(i => bleed[i] * (0.55f + 0.45f * Math.Clamp((mid[i] - 0.38f) * 3, 0, 1))), "#d3dfc6", 0.75f);
        Tint(cv.Map(i => bleed[i] * Math.Clamp((fine[i] - 0.55f) * 3, 0, 1)), "#b9c9b2", 0.35f);
        Tint(cv.Map(i => bleed[i] * bleed[i] * bleed[i] * Math.Clamp((mid[i] - 0.5f) * 4, 0, 1)), "#c4c8c2", 0.45f);
        distOut = null;

        // 2. Sea: seaward of the foam line, a pale wash fading with distance from the foam.
        if (Land is { Count: >= 3 } land)
        {
            var lm = Polys([land], 0);
            // Smooth the land edge: close it by 45 m (grow then shrink).
            float r = 45 * S;
            var toLand = cv.Distance(lm, v => v >= 0.5f);
            var grown = cv.Map(i => toLand[i] <= r ? 1f : 0f);
            var toSea = cv.Distance(grown, v => v < 0.5f);
            var sea = cv.Map(i => toSea[i] > r ? 0f : 1f);
            toLand = null; grown = null; toSea = null;
            var foam = Discs(Foam(_course).Select(f => (f, K(4))), 0);
            var dFoam = cv.Distance(foam, v => v >= 0.5f);
            sea = cv.Blur(sea, K(9));
            var fade = cv.Map(i => MathF.Pow(Math.Clamp(1 - dFoam[i] / (0.075f * shortSide * (0.35f + 0.9f * big[i])), 0, 1), 1.3f));
            var wash = cv.Map(i => dFoam[i] < 0.12f * shortSide ? sea[i] * fade[i] : 0);
            Tint(cv.Map(i => wash[i] * (0.7f + 0.3f * mid[i])), "#b3cbd6", 0.7f);
            Tint(cv.Map(i => wash[i] * Math.Clamp((mid[i] - 0.55f) * 3, 0, 1) * fade[i]), "#94b3c2", 0.35f);
        }

        // 3. The parcel's rough ground, mottled like a satellite view.
        var inside = cv.Blur(pm, K(0.8f));
        var rough = Rgb("#55722f");
        cv.Cover(inside, i => { float s = (0.84f + 0.26f * mid[i]) * (0.90f + 0.20f * fine[i]); return (rough.R * s, rough.G * s, rough.B * s); });
        // Waste scrub: surface-15 brushes and areas, blurred and broken up by the mottle.
        var waste = Discs(_layers.WasteBrushes.Select(d => (d.Centre, (float)d.Radius * S)), 0);
        var wasteAreas = Polys(_layers.WasteAreas, 0);
        var wm = cv.Blur(cv.Map(i => Math.Max(waste[i], wasteAreas[i])), K(14));
        PaintOver(cv.Map(i => Math.Clamp((wm[i] - 0.25f + 0.35f * (mid[i] - 0.5f)) * 2.2f, 0, 1) * inside[i] * (0.55f + 0.45f * fine[i])), "#8f8b6c", 0.7f);
        waste = null; wasteAreas = null; wm = null;
        var heavy = _layers.Grass.Where(g => !_layers.Fairways.Contains(g)).ToList();
        PaintOver(Clip(Polys(heavy, K(2)), inside), "#465f27", 0.55f);
        // Weathering (surface 18) is a tint, not a surface: Cassimbo's carries the fairway green out over the rough.
        var weathering = _course.SurfaceSplines.Where(s => s.Closed && s.Surface == Surfaces.Weathering && s.Points.Count >= 2)
            .Select(s => (IReadOnlyList<Vec2>)Geometry.SampleSpline(s, 12)).ToList();
        PaintOver(Clip(Polys(weathering, K(3)), inside), "#7f9f40", 0.45f);
        // Fescue tufts.
        var fescue = _course.PlacedObjects.Where(o => o.AssetPath.Contains("/Grass/Fescue", StringComparison.OrdinalIgnoreCase))
            .Select(o => (o.Position, Math.Max(K(1.2f), 0.9f * (float)o.Scale * S)));
        var fm = Clip(Discs(fescue, K(1.2f)), inside);
        PaintOver(cv.Map(i => fm[i] * (0.5f + 0.5f * fine[i])), "#8f8a52", 0.55f);
        fm = null;

        // 4. Roads and cart paths, inside the parcel only.
        PaintOver(Clip(Lines(_layers.Roads.Select(r => ((IReadOnlyList<Vec2>)r.Points, (float)r.Width * S)), K(1)), inside), "#b9b29c", 0.9f);
        PaintOver(Clip(Lines(_layers.Paths.Select(r => ((IReadOnlyList<Vec2>)r.Points, (float)r.Width * S * 1.1f)), K(0.8f)), inside), "#d6d3c8", 0.9f);

        // 5. Fairways (lighter in the middle) and tees.
        var fair = Polys(_layers.Fairways, K(2.2f));
        var shade = cv.Blur(Polys(_layers.Fairways, 0), K(10));
        var fc = Rgb("#8fb24a");
        cv.Cover(fair, i => { float s = (0.92f + 0.10f * shade[i]) * (0.96f + 0.06f * mid[i]); return (fc.R * s, fc.G * s, fc.B * s); });
        fair = null; shade = null;
        var tees = new List<IReadOnlyList<Vec2>>();
        foreach (var h in _layers.Holes)
        {
            if (h.Route.Count < 2) continue;
            var u = (h.Route[1] - h.Route[0]).Normalized();
            var v = new Vec2(-u.Z, u.X);
            foreach (var t in h.Hole.Tees)
                tees.Add(new[] { (-4.5, -2.5), (4.5, -2.5), (4.5, 2.5), (-4.5, 2.5) }.Select(d => t + u * d.Item1 + v * d.Item2).ToList());
        }
        PaintOver(Polys(tees, K(1.2f)), "#9cc256", 1);

        // 6. Inland water: surface-10 areas and ribbons, and the traced ponds and creeks of the water entries.
        var wa = Polys(_layers.WaterAreas, K(1.2f));
        var wr = Lines(_layers.WaterRibbons.Select(r => ((IReadOnlyList<Vec2>)r.Points, (float)r.Width * S)), K(1.2f));
        var wb = cv.Mask((c, p) =>
        {
            foreach (var body in _layers.WaterBodies)
            {
                using var b = new SKPathBuilder();
                foreach (var shore in body.Where(s => s.Count >= 3))
                {
                    b.MoveTo(Px(shore[0]));
                    foreach (var q in shore.Skip(1)) b.LineTo(Px(q));
                    b.Close();
                }
                using var path = b.Detach();
                path.FillType = SKPathFillType.EvenOdd;
                c.DrawPath(path, p);
            }
        }, K(1.2f));
        PaintOver(cv.Map(i => Math.Max(wa[i], Math.Max(wr[i], wb[i]))), "#b3cad3", 1);
        wa = null; wr = null; wb = null;

        // 7. Bush and tree masses (soft shadow, dark body, sunlit rim) and rocks.
        var bushes = _course.PlacedObjects.Where(o => Landscape_IsTreeOrBush(o))
            .Select(o => (o.Position, Math.Max(K(1.6f), 2.2f * (float)o.Scale * S)));
        var bm = Clip(Discs(bushes, K(1)), inside);
        var shadowed = cv.Shift(cv.Blur(bm, K(3)), (int)Math.Round(K(3)), (int)Math.Round(K(4)));
        PaintOver(cv.Map(i => shadowed[i] * (1 - bm[i]) * inside[i]), "#2c3d1c", 0.45f);
        PaintOver(cv.Map(i => bm[i] * (0.75f + 0.25f * fine[i])), "#2f4320", 1);
        int lit = Math.Max(1, (int)Math.Round(K(2)));
        var bmShift = cv.Shift(bm, lit, lit);
        PaintOver(cv.Map(i => grain[i] > 0.45f ? Math.Clamp(bm[i] - bmShift[i], 0, 1) : 0), "#5f7d36", 0.7f);
        bm = null; shadowed = null; bmShift = null;
        var rocks = _course.PlacedObjects.Where(o => o.AssetPath.Contains("/Rocks", StringComparison.OrdinalIgnoreCase))
            .Select(o => (o.Position, Math.Max(K(1.2f), 1.4f * (float)o.Scale * S)));
        PaintOver(Clip(Discs(rocks, K(0.8f)), inside), "#8a8576", 0.9f);

        // 8. Bunkers: merged outlines, cream sand with a faint darker lip.
        var bun = Clip(Polys(_layers.MergedBunkers(_o.BunkerMergeMetres).Select(b => (IReadOnlyList<Vec2>)b), K(1)), inside);
        var bunBlur = cv.Blur(bun, K(2.2f));
        PaintOver(cv.Map(i => Math.Clamp(bunBlur[i] * 1.3f - bun[i], 0, 1)), "#5a6a33", 0.5f);
        PaintOver(bun, "#ecdfa4", 1);
        Tint(cv.Map(i => bun[i] * fine[i]), "#e2cf86", 0.35f);
        bun = null; bunBlur = null;

        // 9. Greens, brightest, with a soft 1.4 m collar.
        var gm = Polys(_layers.Greens, K(1.2f));
        var gGrown = cv.Mask((c, p) =>
        {
            using var stroke = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 2 * 1.4f * S, StrokeJoin = SKStrokeJoin.Round };
            foreach (var g in _layers.Greens) if (g.Count >= 3) using (var path = Poly(g)) c.DrawPath(path, stroke);
        }, 0);
        var collar = cv.Blur(gGrown, K(1.5f));
        PaintOver(cv.Map(i => Math.Clamp(collar[i] - gm[i], 0, 1)), "#9cc256", 0.9f);
        PaintOver(gm, "#b3d466", 1);
        gm = null; gGrown = null; collar = null;

        // 10. Parcel edge: a crisp dark property line, a slight inner shade, and satellite grain inside.
        var dIn = cv.Distance(pm, v => v < 0.5f);
        var e3 = cv.Blur(cv.Map(i => dIn[i] > K(3) ? 1f : 0f), K(1.2f));
        var pmBlur = cv.Blur(pm, K(1.2f));
        PaintOver(cv.Map(i => Math.Clamp(pmBlur[i] - e3[i], 0, 1)), "#22301a", 0.85f);
        var e14 = cv.Blur(cv.Map(i => dIn[i] > K(14) ? 1f : 0f), K(8));
        Tint(cv.Map(i => Math.Clamp(pm[i] - e14[i], 0, 1)), "#33441f", 0.25f);
        cv.Multiply(i => (1 + 0.035f * (grain[i] - 0.5f) * inside[i]) * (1 - 0.012f * (fine[i] - 0.5f)));
        return cv.ToImage();
    }

    static bool Landscape_IsTreeOrBush(PlacedObject o) => Maps.Landscape.IsTree(o) || Maps.Landscape.IsBush(o);
}
