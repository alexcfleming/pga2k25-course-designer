using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// Room for the boxes a style lays over its map (title, card, legend, rose, signature). Instead of fitting the course
/// to the whole sheet and dropping each box in the least-crowded corner, the map is fitted around the boxes: the
/// largest scale (and the most central position at that scale) where no hole, green, bunker or line of play falls
/// under a box, trying every corner each box may take. A course that can't clear them above
/// <see cref="MinBoxScale"/> of its full size falls back to the old way (full size, least-crowded corners).
/// </summary>
public sealed partial class ScorecardMapRenderer
{
    /// <summary>
    /// A box over the map: its size and the spots it may take (fractions of the map rect, as <see cref="SpotBox"/>), in
    /// order of preference; or one fixed rect on the page (a scale bar, a credit line).
    /// </summary>
    internal readonly record struct BoxAsk(float W, float H, (float X, float Y)[] Spots, SKRect? Fixed = null)
    {
        public static BoxAsk At(SKRect fixedRect) => new(fixedRect.Width, fixedRect.Height, [(0, 0)], fixedRect);
        public SKRect Place(SKRect map, (float X, float Y) spot) => Fixed ?? SpotBox(map, spot, W, H);
    }

    /// <summary>The place lines under the title: the typed location (comma separated), else the place card's lines.</summary>
    List<string> PlaceList() =>
        (string.IsNullOrWhiteSpace(_o.Location) ? _o.PlaceLines : _o.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();

    List<Vec2>? _parcel;
    /// <summary>The property parcel (<see cref="PortraitParcel"/>), built once a sheet.</summary>
    List<Vec2> ParcelOf(CourseLayers l) => _parcel ??= PortraitParcel.Build(l);

    /// <summary>The architect-plan styles: each lays boxes over its map and fits the map round them.</summary>
    bool FitsAroundBoxes => IsSketchStyle || IsCutten || IsThompson || IsGrassing;

    /// <summary>
    /// What the map is fitted to. The architect-plan styles add the property parcel they draw and every bunker and pond
    /// near play kept whole, so neither runs off the sheet.
    /// </summary>
    List<Vec2> FitPoints(CourseLayers l)
    {
        var pts = l.ExtentPoints().ToList();
        if (!FitsAroundBoxes) return pts;
        foreach (var poly in l.BunkerAreas.Concat(l.WaterAreas).Where(s => s.Any(p => l.DistanceToPlay(p) <= 60)))
            pts.AddRange(poly);
        pts.AddRange(ParcelOf(l));
        return pts;
    }

    /// <summary>The boxes this style lays over its map, in the order it draws them.</summary>
    List<BoxAsk> StyleBoxes(string courseName, SKSize page, SKRect map) =>
        IsThompson ? ThompsonBoxes(courseName) : IsCutten ? CuttenBoxes(page, map) : IsGrassing ? GrassingBoxes(courseName)
        : IsSketchStyle ? SketchBoxes(courseName) : [];

    /// <summary>The boxes chosen for this sheet, in the order the style asked for them (empty when it asks for none).</summary>
    SKRect[] _boxes = [];

    /// <summary>The box chosen for ask <paramref name="i"/>, else <paramref name="otherwise"/> (the style's old corner pick).</summary>
    SKRect Reserved(int i, Func<SKRect> otherwise) => i < _boxes.Length ? _boxes[i] : otherwise();

    /// <summary>Smallest map scale, as a share of the full fit, accepted to keep the boxes clear.</summary>
    public const double MinBoxScale = 0.6;

    /// <summary>Space kept between a box and the play beside it, in page units.</summary>
    const float BoxGap = 6;

    /// <summary>
    /// Fits <paramref name="extent"/> into <paramref name="map"/> at <paramref name="rotation"/>, as large as it goes with
    /// no point of <paramref name="play"/> under any box. Returns the projection and the box chosen for each ask.
    /// </summary>
    internal static (MapProjection Proj, SKRect[] Boxes) FitAroundBoxes(IReadOnlyList<Vec2> extent, IReadOnlyList<Vec2> play, SKRect map,
        double rotation, IReadOnlyList<BoxAsk> asks)
    {
        var full = MapProjection.Fit(extent, map, rotation);
        if (asks.Count == 0 || extent.Count == 0) return (full, []);

        // Every way of placing the boxes without two of them touching, in the styles' order of preference.
        var combos = new List<SKRect[]> { Array.Empty<SKRect>() };
        foreach (var ask in asks)
            combos = combos.SelectMany(c => ask.Spots.Select(s => ask.Place(map, s))
                    .Where(r => c.All(o => !SKRect.Inflate(o, BoxGap, BoxGap).IntersectsWith(r)))
                    .Select(r => c.Append(r).ToArray()))
                .ToList();
        if (combos.Count == 0) combos.Add(asks.Select(a => a.Place(map, a.Spots[0])).ToArray());

        // Rotated metres, y down the page: the course's shape at any scale is these times k.
        double rad = rotation * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        (double U, double V) Rot(Vec2 w) => (w.X * cos - w.Z * sin, -(w.X * sin + w.Z * cos));
        var ext = extent.Select(Rot).ToList();
        double minU = ext.Min(e => e.U), maxU = ext.Max(e => e.U), minV = ext.Min(e => e.V), maxV = ext.Max(e => e.V);
        var pts = play.Select(Rot).ToList();
        double k0 = full.PointsPerMetre;
        const float cell = 3;

        for (double s = 1; s >= MinBoxScale - 1e-9; s -= 0.02)
        {
            double k = k0 * s;
            float w = (float)((maxU - minU) * k), h = (float)((maxV - minV) * k);
            // Fit's own margin (4 %) is kept at full size; smaller maps may slide anywhere inside it.
            float padX = (float)(map.Width * 0.02 * s), padY = (float)(map.Height * 0.02 * s);
            float x0 = map.Left + padX, x1 = map.Right - padX - w, y0 = map.Top + padY, y1 = map.Bottom - padY - h;
            if (x1 < x0) { float m = (map.Left + map.Right - w) / 2; x0 = x1 = m; }
            if (y1 < y0) { float m = (map.Top + map.Bottom - h) / 2; y0 = y1 = m; }

            // Play on a grid over the course's box at this scale, summed so any rectangle's count is four lookups.
            int gw = (int)Math.Ceiling(w / cell) + 2, gh = (int)Math.Ceiling(h / cell) + 2;
            var sum = new int[(gw + 1) * (gh + 1)];
            foreach (var (u, v) in pts)
            {
                int gx = (int)((u - minU) * k / cell), gy = (int)((v - minV) * k / cell);
                if (gx >= 0 && gy >= 0 && gx < gw && gy < gh) sum[(gy + 1) * (gw + 1) + gx + 1]++;
            }
            for (int y = 1; y <= gh; y++)
                for (int x = 1; x <= gw; x++)
                    sum[y * (gw + 1) + x] += sum[(y - 1) * (gw + 1) + x] + sum[y * (gw + 1) + x - 1] - sum[(y - 1) * (gw + 1) + x - 1];
            int Count(float l, float t, float r, float b)
            {
                int ax = Math.Clamp((int)Math.Floor(l / cell), 0, gw), ay = Math.Clamp((int)Math.Floor(t / cell), 0, gh);
                int bx = Math.Clamp((int)Math.Ceiling(r / cell), 0, gw), by = Math.Clamp((int)Math.Ceiling(b / cell), 0, gh);
                if (bx <= ax || by <= ay) return 0;
                return sum[by * (gw + 1) + bx] - sum[ay * (gw + 1) + bx] - sum[by * (gw + 1) + ax] + sum[ay * (gw + 1) + ax];
            }

            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            (float Ox, float Oy, SKRect[] Boxes)? best = null;
            float bestD = float.MaxValue;
            foreach (var combo in combos)
                for (float ox = x0; ox <= x1 + 0.01f; ox += cell)
                    for (float oy = y0; oy <= y1 + 0.01f; oy += cell)
                    {
                        float d = (ox - cx) * (ox - cx) + (oy - cy) * (oy - cy);
                        if (d >= bestD) continue;
                        bool clear = true;
                        foreach (var b in combo)
                            if (Count(b.Left - BoxGap - ox, b.Top - BoxGap - oy, b.Right + BoxGap - ox, b.Bottom + BoxGap - oy) > 0) { clear = false; break; }
                        if (clear) { bestD = d; best = (ox, oy, combo); }
                    }
            if (best is { } pick)
            {
                // The page point the rotated-space centre lands on, and the world point under it.
                var centrePage = new SKPoint(pick.Ox + w / 2, pick.Oy + h / 2);
                double mu = (minU + maxU) / 2, mv = -(minV + maxV) / 2;
                var centre = new Vec2(mu * cos + mv * sin, -mu * sin + mv * cos);
                return (MapProjection.Place(rotation, centre, (float)k, centrePage), pick.Boxes);
            }
        }

        // Nothing clears them: full size, each box where the least play falls under it.
        return (full, combos.MinBy(c => c.Sum(b => pts.Select(q => full.Map(Unrot(q))).Count(SKRect.Inflate(b, BoxGap, BoxGap).Contains)))!);

        Vec2 Unrot((double U, double V) q) { double v = -q.V; return new Vec2(q.U * cos + v * sin, -q.U * sin + v * cos); }
    }

    /// <summary>A closed outline's points every <paramref name="step"/> m or closer.</summary>
    static IEnumerable<Vec2> Densify(IReadOnlyList<Vec2> poly, double step)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % poly.Count]);
            int steps = Math.Max(1, (int)(Vec2.Distance(a, b) / step));
            for (int s = 0; s < steps; s++) yield return a + (b - a) * ((double)s / steps);
        }
    }

    /// <summary>Other turns tried round the boxes: every 10° within ±90° of north-up, keeping north in the upper half.</summary>
    static IEnumerable<double> AutoTurns(double planned) =>
        Enumerable.Range(-9, 18).Select(i => MapProjection.NorthUpRotation + i * 10.0).Where(r => Math.Abs(r - planned) > 2);

    /// <summary>
    /// What a box must not cover: the outlines of the grass, greens, bunkers and water near play, every tee, and each
    /// hole's line of play every 3 m.
    /// </summary>
    public static List<Vec2> PlayPoints(CourseLayers l, double reach = 40)
    {
        var pts = new List<Vec2>();
        void Outline(IReadOnlyList<Vec2> poly) => pts.AddRange(Densify(poly, 4));
        foreach (var poly in l.Grass.Concat(l.Greens).Concat(l.BunkerAreas).Concat(l.WaterAreas).Concat(l.WaterWalls))
            if (poly.Count > 0 && poly.Any(p => l.DistanceToPlay(p) <= reach)) Outline(poly);
        foreach (var h in l.Holes)
        {
            pts.AddRange(h.Hole.Tees);
            for (int i = 1; i < h.Route.Count; i++)
            {
                var (a, b) = (h.Route[i - 1], h.Route[i]);
                int steps = Math.Max(1, (int)(Vec2.Distance(a, b) / 3));
                for (int s = 0; s <= steps; s++) pts.Add(a + (b - a) * ((double)s / steps));
            }
        }
        return pts;
    }
}
