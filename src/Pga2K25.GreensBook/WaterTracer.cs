using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>How a run of <c>water[]</c> entries shows in game.</summary>
public enum WaterKind
{
    /// <summary>A flat plane: water only where the ground inside its rectangle lies below the level.</summary>
    Plane,
    /// <summary>A painted stroke: the game cuts its channel, so the whole stroke is water (Berwick Coast's stream).</summary>
    CutStroke,
}

/// <summary>Consecutive <c>water[]</c> entries at one level, close enough to be one stroke or one joined body.</summary>
public sealed record WaterStroke(WaterKind Kind, double Level, IReadOnlyList<WaterPlane> Members);

/// <summary>One connected body of water on the raster.</summary>
/// <param name="ChannelMetres">Length of shallow channel added to join it up (0 when none).</param>
/// <param name="Shores">Closed shorelines: the outer one first, then any islands. Fill even-odd.</param>
public sealed record WaterBody(double Level, double AreaM2, double MaxDepthMetres, Vec2 LabelPoint, double ChannelMetres,
    IReadOnlyList<List<Vec2>> Shores)
{
    /// <summary>The outer shoreline (empty if none could be traced).</summary>
    public List<Vec2> Outline => Shores.Count > 0 ? Shores[0] : [];
}

public sealed record WaterOptions
{
    /// <summary>
    /// A shallow channel joins two bodies when the lowest route between them, over ground cut by lower brushes
    /// inside the planes, rises no more than this above the water. The replay misses a metre or so in narrow
    /// dug channels (Luke's hole 1 creek: +1.65 m in the model, connected water in game).
    /// </summary>
    public double ChannelAllowanceMetres { get; init; } = 2.0;

    /// <summary>Width of a joining channel (m).</summary>
    public double ChannelWidthMetres { get; init; } = 4.0;

    /// <summary>Lower brushes up to this size (m) mark dug ground; bigger ones are sculpting, not channels.</summary>
    public double DugBrushMaxScale { get; init; } = 30;

    /// <summary>Wet pieces smaller than this (m²) are dropped as noise.</summary>
    public double MinBodyM2 { get; init; } = 20;

    /// <summary>
    /// Pieces cut by a plane's rectangle edge (with no other plane continuing them) smaller than this (m²) are
    /// dropped: in game the shorelines are rounded, so an edge-cut sliver is replay error, not a straight bank.
    /// </summary>
    public double MaxEdgeSliverM2 { get; init; } = 300;

    /// <summary>
    /// A stroke whose entries are this big or bigger (median of the longer half-size, m) is a water plane; smaller
    /// ones are painted dabs. Berwick Coast's stream dabs are 6–26 m; Luke Muscat's planes are 35–99 m.
    /// </summary>
    public double PlaneMinHalfSize { get; init; } = 30;

    /// <summary>Overrides the plane / cut-stroke rule for every stroke (null = decide per stroke).</summary>
    public WaterKind? ForceKind { get; init; }
}

/// <summary>Water from a course's <c>water[]</c> entries over one heightfield window.</summary>
public sealed class WaterMap
{
    public required Heightfield Field { get; init; }

    /// <summary>One flag per <see cref="Field"/> cell.</summary>
    public required bool[] Wet { get; init; }

    /// <summary>Water depth (m) per cell: level − ground for planes, 0 for cut strokes and shallow channels.</summary>
    public required double[] Depth { get; init; }

    public required IReadOnlyList<WaterStroke> Strokes { get; init; }
    public required IReadOnlyList<WaterBody> Bodies { get; init; }

    /// <summary>Index into <see cref="Bodies"/> per cell, -1 when dry.</summary>
    public required int[] BodyIndex { get; init; }

    /// <summary>Shorelines (world coordinates); fill with even-odd so islands stay dry.</summary>
    public required List<List<Vec2>> Outlines { get; init; }

    public bool Any => Bodies.Count > 0;

    public bool IsWet(Vec2 p) => BodyAt(p) >= 0;

    /// <summary>Index into <see cref="Bodies"/> of the water at a world point, -1 when dry or off the raster.</summary>
    public int BodyAt(Vec2 p)
    {
        int ix = (int)Math.Floor((p.X - Field.Origin.X) / Field.Cell), iz = (int)Math.Floor((p.Z - Field.Origin.Z) / Field.Cell);
        return ix >= 0 && iz >= 0 && ix < Field.Width && iz < Field.Height ? BodyIndex[iz * Field.Width + ix] : -1;
    }
}

/// <summary>
/// Traces the water a course's <c>water[]</c> entries show. The file stores two different things the same way:
/// painted streams (brush-sized dabs; the game cuts their channel) and water planes (large rectangles; the game
/// leaves the ground alone and the water shows where the land dips below the plane). The record fields are
/// identical and the ground doesn't separate them either (about a third of Berwick Coast's stream footprint lies
/// below its level, against a quarter of Luke Muscat's planes), so the kind goes by size: see
/// <see cref="WaterOptions.PlaneMinHalfSize"/>.
/// </summary>
public static class WaterTracer
{
    /// <summary>Replays the ground over every water entry (plus a margin) and traces it.</summary>
    public static WaterMap Trace(CourseModel course, TerrainModelOptions terrain, WaterOptions? options = null)
    {
        var water = Usable(course);
        if (water.Count == 0)
            return Trace(course, new Heightfield(new Vec2(0, 0), terrain.CellMetres, 1, 1), options);
        double pad = 10;
        var min = new Vec2(water.Min(w => w.Position.X - w.Reach) - pad, water.Min(w => w.Position.Z - w.Reach) - pad);
        var max = new Vec2(water.Max(w => w.Position.X + w.Reach) + pad, water.Max(w => w.Position.Z + w.Reach) + pad);
        return Trace(course, TerrainReplay.Build(course, min, max, terrain), options);
    }

    /// <summary>Traces water over an existing ground raster (a sheet's or page's own window).</summary>
    public static WaterMap Trace(CourseModel course, Heightfield ground, WaterOptions? options = null)
    {
        var o = options ?? new WaterOptions();
        int w = ground.Width, h = ground.Height, n = w * h;
        var wet = new bool[n];
        var depth = new double[n];
        var strokes = Strokes(course, o);

        // Plane footprints: the highest covering level per cell, plus which cells lie on an exposed edge.
        var level = new double[n];
        Array.Fill(level, double.NaN);
        var planes = strokes.Where(s => s.Kind == WaterKind.Plane).SelectMany(s => s.Members).ToList();
        foreach (var p in planes)
            ForEachCell(ground, p.Position, p.Reach, (k, c) =>
            {
                if (!p.Contains(c)) return;
                if (double.IsNaN(level[k]) || p.Level > level[k]) level[k] = p.Level;
                double d = p.Level - ground.Values[k];
                if (d > 0) { wet[k] = true; depth[k] = Math.Max(depth[k], d); }
            });
        foreach (var s in strokes.Where(s => s.Kind == WaterKind.CutStroke)) PaintStroke(ground, s, wet);

        var comp = Label(ground, wet, out var sizes);
        DropSlivers(ground, planes, level, wet, depth, comp, sizes, o);
        comp = Label(ground, wet, out sizes);
        // Pieces the designer marked dry in game go before any joining, so a marked piece can't pull a channel in either.
        if (DropMarked(ground, course.NotWater, wet, depth, comp)) comp = Label(ground, wet, out sizes);
        var channel = JoinChannels(course, ground, level, wet, comp, sizes, o);
        comp = Label(ground, wet, out sizes);

        var bodies = new List<WaterBody>();
        var bodyOf = new int[sizes.Count];
        Array.Fill(bodyOf, -1);
        double cellArea = ground.Cell * ground.Cell;
        for (int id = 0; id < sizes.Count; id++)
        {
            double area = sizes[id] * cellArea;
            if (area < o.MinBodyM2) continue;
            bodyOf[id] = bodies.Count;
            double deepest = 0, lvl = double.NaN;
            int best = -1, channelCells = 0, x0 = w, x1 = -1, z0 = h, z1 = -1;
            for (int k = 0; k < n; k++)
            {
                if (comp[k] != id) continue;
                int ix = k % w, iz = k / w;
                x0 = Math.Min(x0, ix); x1 = Math.Max(x1, ix); z0 = Math.Min(z0, iz); z1 = Math.Max(z1, iz);
                if (channel[k]) channelCells++;
                if (!double.IsNaN(level[k]) && (double.IsNaN(lvl) || level[k] > lvl)) lvl = level[k];
                if (best < 0 || depth[k] > deepest) { deepest = depth[k]; best = k; }
            }
            if (double.IsNaN(lvl)) lvl = StrokeLevelAt(strokes, ground.CellCentre(best % w, best / w));
            // Label at the deepest water, which is well inside the shoreline even for rings and bends.
            bodies.Add(new WaterBody(lvl, area, deepest, ground.CellCentre(best % w, best / w), channelCells * cellArea / o.ChannelWidthMetres,
                Shores(ground, comp, id, x0, x1, z0, z1)));
        }
        var bodyIndex = new int[n];
        for (int k = 0; k < n; k++)
        {
            bodyIndex[k] = comp[k] >= 0 ? bodyOf[comp[k]] : -1;
            if (comp[k] >= 0 && bodyIndex[k] < 0) { wet[k] = false; depth[k] = 0; }
        }

        return new WaterMap
        {
            Field = ground, Wet = wet, Depth = depth, Strokes = strokes, Bodies = bodies, BodyIndex = bodyIndex,
            Outlines = bodies.Count == 0 ? [] : TraceMask(ground, wet),
        };
    }

    /// <summary>
    /// One body's shorelines, traced on its own box with a dry border so every loop closes (even where the body
    /// runs off the raster). Largest loop first: that is the outer shore, the rest are islands.
    /// </summary>
    static List<List<Vec2>> Shores(Heightfield ground, int[] comp, int id, int x0, int x1, int z0, int z1)
    {
        int bw = x1 - x0 + 3, bh = z1 - z0 + 3;
        var box = new Heightfield(ground.CellCentre(x0 - 1, z0 - 1) - new Vec2(ground.Cell / 2, ground.Cell / 2), ground.Cell, bw, bh);
        for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
                if (comp[iz * ground.Width + ix] == id) box[ix - x0 + 1, iz - z0 + 1] = 1;
        var all = new bool[bw * bh];
        Array.Fill(all, true);
        return MarchingSquares.Trace(box, box.Values, all, 0.5).Where(l => l.Count >= 3)
            .OrderByDescending(l => Math.Abs(Geometry.Area(l))).ToList();
    }

    static List<WaterPlane> Usable(CourseModel course) =>
        course.Water.Where(p => p.HalfX > 0 && p.HalfZ > 0 && double.IsFinite(p.Level)).ToList();

    /// <summary>
    /// Joins consecutive entries at the same level into strokes (dabs of one drag are stored separately,
    /// about a brush width apart, sometimes more) and decides each stroke's kind.
    /// </summary>
    static List<WaterStroke> Strokes(CourseModel course, WaterOptions o)
    {
        var water = Usable(course);
        var runs = new List<List<WaterPlane>>();
        foreach (var p in water)
        {
            var last = runs.Count > 0 ? runs[^1][^1] : null;
            if (last is not null && Math.Abs(last.Level - p.Level) < 0.01 &&
                Vec2.Distance(last.Position, p.Position) <= 2 * Math.Max(Math.Max(last.HalfX, last.HalfZ), Math.Max(p.HalfX, p.HalfZ)))
                runs[^1].Add(p);
            else runs.Add([p]);
        }
        return runs.Select(r => new WaterStroke(o.ForceKind ?? KindBySize(r, o), r[0].Level, r)).ToList();
    }

    static WaterKind KindBySize(List<WaterPlane> run, WaterOptions o)
    {
        var sizes = run.Select(p => Math.Max(p.HalfX, p.HalfZ)).Order().ToList();
        return sizes[sizes.Count / 2] >= o.PlaneMinHalfSize ? WaterKind.Plane : WaterKind.CutStroke;
    }

    /// <summary>A cut stroke: discs of diameter scale.x at each dab, joined along the stroke (the original painted-water rule).</summary>
    static void PaintStroke(Heightfield ground, WaterStroke s, bool[] wet)
    {
        for (int i = 0; i < s.Members.Count; i++)
        {
            var a = s.Members[i];
            var b = i + 1 < s.Members.Count ? s.Members[i + 1] : a;
            double r = Math.Min(a.HalfX, b.HalfX) / 2;
            Capsule(ground, a.Position, a.Position, a.HalfX / 2, wet);
            Capsule(ground, a.Position, b.Position, r, wet);
        }
    }

    static void Capsule(Heightfield ground, Vec2 a, Vec2 b, double r, bool[] wet)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Z * ab.Z;
        ForEachCell(ground, (a + b) * 0.5, r + Math.Sqrt(len2) / 2, (k, c) =>
        {
            double t = len2 > 0 ? Math.Clamp(((c.X - a.X) * ab.X + (c.Z - a.Z) * ab.Z) / len2, 0, 1) : 0;
            if (Vec2.Distance(c, a + ab * t) <= r) wet[k] = true;
        });
    }

    static double StrokeLevelAt(IReadOnlyList<WaterStroke> strokes, Vec2 p) =>
        strokes.SelectMany(s => s.Members).MinBy(m => Vec2.Distance(m.Position, p))?.Level ?? double.NaN;

    /// <summary>Drops small pieces that touch a plane edge no other plane continues past.</summary>
    static void DropSlivers(Heightfield ground, List<WaterPlane> planes, double[] level, bool[] wet, double[] depth,
        int[] comp, List<int> sizes, WaterOptions o)
    {
        int w = ground.Width, h = ground.Height;
        double cellArea = ground.Cell * ground.Cell;
        var cut = new HashSet<int>();
        for (int k = 0; k < wet.Length; k++)
        {
            if (!wet[k] || double.IsNaN(level[k]) || sizes[comp[k]] * cellArea >= o.MaxEdgeSliverM2) continue;
            int ix = k % w, iz = k / w;
            foreach (var (nx, nz) in Neighbours(ix, iz))
                if (nx >= 0 && nz >= 0 && nx < w && nz < h && double.IsNaN(level[nz * w + nx])) { cut.Add(comp[k]); break; }
        }
        if (cut.Count == 0) return;
        for (int k = 0; k < wet.Length; k++)
            if (comp[k] >= 0 && cut.Contains(comp[k])) { wet[k] = false; depth[k] = 0; }
    }

    /// <summary>
    /// Shallow channels between bodies: the lowest route (by its highest point) from each body to another, over
    /// cells inside a plane that lower brushes cut. Accepted when that high point is within the allowance; the
    /// route is then drawn as a channel of the set width. Returns the channel cells.
    /// </summary>
    static bool[] JoinChannels(CourseModel course, Heightfield ground, double[] level, bool[] wet, int[] comp, List<int> sizes, WaterOptions o)
    {
        int w = ground.Width, h = ground.Height, n = w * h;
        var channel = new bool[n];
        double minCells = o.MinBodyM2 / (ground.Cell * ground.Cell);
        if (sizes.Count(s => s >= minCells) < 2 || o.ChannelAllowanceMetres <= 0) return channel;

        var dug = new bool[n];
        foreach (var s in course.HeightStamps.Where(s => s.Tool == 1 && s.Value < 0 && s.Scale > 0 && s.Scale <= o.DugBrushMaxScale))
            ForEachCell(ground, s.Position, s.Scale / 2, (k, c) => { if (!double.IsNaN(level[k]) && Vec2.Distance(c, s.Position) <= s.Scale / 2) dug[k] = true; });

        var linked = new HashSet<(int, int)>();
        var cost = new double[n];
        var prev = new int[n];
        for (int id = 0; id < sizes.Count; id++)
        {
            if (sizes[id] < minCells) continue;
            Array.Fill(cost, double.PositiveInfinity);
            var queue = new PriorityQueue<int, double>();
            for (int k = 0; k < n; k++) if (comp[k] == id) { cost[k] = 0; prev[k] = -1; queue.Enqueue(k, 0); }
            int hit = -1;
            while (queue.TryDequeue(out int k, out double c))
            {
                if (c > cost[k]) continue;
                if (c > o.ChannelAllowanceMetres) break;
                if (comp[k] >= 0 && comp[k] != id && sizes[comp[k]] >= minCells) { hit = k; break; }
                foreach (var (nx, nz) in Neighbours(k % w, k / w))
                {
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int m = nz * w + nx;
                    if (!wet[m] && !dug[m]) continue;
                    double next = Math.Max(c, wet[m] ? 0 : ground.Values[m] - level[m]);
                    if (next < cost[m]) { cost[m] = next; prev[m] = k; queue.Enqueue(m, next); }
                }
            }
            if (hit < 0 || !linked.Add((Math.Min(id, comp[hit]), Math.Max(id, comp[hit])))) continue;
            double half = o.ChannelWidthMetres / 2;
            for (int k = hit; k >= 0; k = prev[k])
            {
                if (wet[k]) continue;
                var c0 = ground.CellCentre(k % w, k / w);
                ForEachCell(ground, c0, half, (m, c) => { if (!wet[m] && Vec2.Distance(c, c0) <= half) channel[m] = true; });
            }
        }
        for (int k = 0; k < n; k++) if (channel[k]) wet[k] = true;
        return channel;
    }

    static (int, int)[] Neighbours(int ix, int iz) => [(ix + 1, iz), (ix - 1, iz), (ix, iz + 1), (ix, iz - 1)];

    /// <summary>4-connected components of the wet cells; -1 for dry.</summary>
    /// <summary>
    /// Dries every piece of water a "not water in game" mark falls in (<see cref="CourseModel.NotWater"/>). A mark
    /// just off a shoreline still counts: the nearest wet cell within 3 m takes it.
    /// </summary>
    static bool DropMarked(Heightfield ground, IReadOnlyList<Vec2> marks, bool[] wet, double[] depth, int[] comp)
    {
        if (marks.Count == 0) return false;
        int w = ground.Width, h = ground.Height;
        int reach = Math.Max(1, (int)Math.Ceiling(3 / ground.Cell));
        var drop = new HashSet<int>();
        foreach (var m in marks)
        {
            int cx = (int)Math.Floor((m.X - ground.Origin.X) / ground.Cell), cz = (int)Math.Floor((m.Z - ground.Origin.Z) / ground.Cell);
            int best = -1;
            double bestD = double.MaxValue;
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= w || z >= h || comp[z * w + x] < 0) continue;
                    double d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = comp[z * w + x]; }
                }
            if (best >= 0) drop.Add(best);
        }
        if (drop.Count == 0) return false;
        for (int k = 0; k < wet.Length; k++)
            if (comp[k] >= 0 && drop.Contains(comp[k])) { wet[k] = false; depth[k] = 0; }
        return true;
    }

    static int[] Label(Heightfield ground, bool[] wet, out List<int> sizes)
    {
        int w = ground.Width, h = ground.Height;
        var comp = new int[wet.Length];
        Array.Fill(comp, -1);
        sizes = [];
        var stack = new Stack<int>();
        for (int k0 = 0; k0 < wet.Length; k0++)
        {
            if (!wet[k0] || comp[k0] >= 0) continue;
            int id = sizes.Count, count = 0;
            comp[k0] = id;
            stack.Push(k0);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                count++;
                foreach (var (nx, nz) in Neighbours(k % w, k / w))
                {
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int m = nz * w + nx;
                    if (wet[m] && comp[m] < 0) { comp[m] = id; stack.Push(m); }
                }
            }
            sizes.Add(count);
        }
        return comp;
    }

    /// <summary>Calls <paramref name="visit"/>(index, cell centre) for every cell within a square of ±reach.</summary>
    static void ForEachCell(Heightfield f, Vec2 centre, double reach, Action<int, Vec2> visit)
    {
        int x0 = Math.Max(0, (int)Math.Floor((centre.X - reach - f.Origin.X) / f.Cell));
        int x1 = Math.Min(f.Width - 1, (int)Math.Ceiling((centre.X + reach - f.Origin.X) / f.Cell));
        int z0 = Math.Max(0, (int)Math.Floor((centre.Z - reach - f.Origin.Z) / f.Cell));
        int z1 = Math.Min(f.Height - 1, (int)Math.Ceiling((centre.Z + reach - f.Origin.Z) / f.Cell));
        for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
                visit(iz * f.Width + ix, f.CellCentre(ix, iz));
    }

    static List<List<Vec2>> TraceMask(Heightfield field, bool[] mask)
    {
        var values = new double[mask.Length];
        for (int i = 0; i < mask.Length; i++) values[i] = mask[i] ? 1 : 0;
        var all = new bool[mask.Length];
        Array.Fill(all, true);
        return MarchingSquares.Trace(field, values, all, 0.5).Where(l => l.Count >= 2).ToList();
    }
}
