using System.Runtime.CompilerServices;
using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>
/// Cuts the course's bunkers into a replayed heightfield (<see cref="TerrainModelOptions.BunkerDepths"/>). The game
/// stamps each bunker below its edge when it builds the course, from the designer's bunker sliders
/// (<see cref="BunkerDepression"/>), so the cut is not in <c>height[]</c> and the plain replay shows sand level
/// with the grass around it.
/// <para>
/// What is known: the floor ends up lip + base below the edge (the file stores that sum as <c>stampDepth</c>), and the
/// game's distance markers do not include the cut. On Nodens Estate (2026-11-16), 14 markers at least 0.6 m inside
/// bunkers set 0.5 m deep read within 0.08 m (median) of the marker just outside, so there is no in-game reading of
/// the cut's shape to fit. The shape below is therefore a judgement from what the sliders are called, not a
/// measurement: check a bunker that matters in the game.
/// </para>
/// </summary>
public static class BunkerCut
{
    /// <summary>The lip's face, degrees from level, at steepness 0 and 1.</summary>
    public const double GentlestLipDegrees = 20, SteepestLipDegrees = 80;

    /// <summary>The floor falls its base depth over this many times that depth, from the foot of the lip inwards (1 in 6).</summary>
    public const double FloorRunPerDepth = 6;

    /// <summary>Painted dabs fainter than this are not sand (the same cut-off the maps use).</summary>
    public const double MinBrushOpacity = 0.3;

    /// <summary>How far in from the edge (m) the lip's face runs.</summary>
    public static double LipRun(BunkerDepression d)
    {
        double degrees = GentlestLipDegrees + (SteepestLipDegrees - GentlestLipDegrees) * Math.Clamp(d.LipSteepness, 0, 1);
        return Math.Max(0, d.LipDepth) / Math.Tan(degrees * Math.PI / 180);
    }

    /// <summary>How far in from the edge (m) the cut reaches its full depth.</summary>
    public static double Reach(BunkerDepression d) => LipRun(d) + Math.Max(0, d.BaseDepth) * FloorRunPerDepth;

    /// <summary>
    /// Depth of the sand below the bunker's edge (m) at a point <paramref name="inside"/> metres in from the edge:
    /// down the lip's face, then a floor easing down a further base depth towards the middle. A bunker too narrow
    /// for both never reaches its full depth.
    /// </summary>
    public static double DepthAt(BunkerDepression d, double inside) => DepthAt(d, LipRun(d), inside);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double DepthAt(BunkerDepression d, double lipRun, double inside)
    {
        if (inside <= 0) return 0;
        double lip = Math.Max(0, d.LipDepth), floor = Math.Max(0, d.BaseDepth);
        double depth = inside >= lipRun ? lip : lip * inside / lipRun;
        if (floor > 0 && inside > lipRun)
        {
            double t = Math.Min(1, (inside - lipRun) / (floor * FloorRunPerDepth));
            depth += floor * t * t * (3 - 2 * t);
        }
        return depth;
    }

    enum Kind : byte { Disc, Ribbon, Area }

    /// <summary>One painted dab or drawn bunker that is cut below its edge.</summary>
    sealed class Sand
    {
        public Kind Kind;
        public required BunkerDepression Depression;
        public double LipRun;
        /// <summary>Disc: one point and its radius. Ribbon: the centre line and half its width. Area: the outline.</summary>
        public required IReadOnlyList<Vec2> Points;
        public double Radius;
        public double MinX, MaxX, MinZ, MaxZ;
    }

    static readonly ConditionalWeakTable<CourseModel, List<Sand>> Cache = new();

    /// <summary>
    /// The course's bunkers that have a depth, drawn ones first and painted dabs after, each in file order: where
    /// two overlap, the later one's sliders apply.
    /// </summary>
    static List<Sand> Bunkers(CourseModel course) => Cache.GetValue(course, static c =>
    {
        var list = new List<Sand>();
        foreach (var s in c.SurfaceSplines)
        {
            if (s.Surface != Surfaces.Bunker || s.Points.Count < 2) continue;
            var d = c.Bunkers.Resolve(s.Bunker).Depression;
            if (d.Depth <= 0) continue;
            var pts = Geometry.SampleSpline(s, 12);
            if (s.Closed && pts.Count >= 3) list.Add(Box(new Sand { Kind = Kind.Area, Depression = d, Points = pts }));
            else if (!s.Closed) list.Add(Box(new Sand { Kind = Kind.Ribbon, Depression = d, Points = pts, Radius = Math.Max(s.Width, 0.5) / 2 }));
        }
        foreach (var b in c.SurfaceBrushes)
        {
            if (b.Category != Surfaces.Bunker || b.Scale <= 0 || b.Value < MinBrushOpacity) continue;
            var d = c.Bunkers.Resolve(b.Bunker).Depression;
            if (d.Depth > 0) list.Add(Box(new Sand { Kind = Kind.Disc, Depression = d, Points = [b.Position], Radius = b.Scale / 2 }));
        }
        return list;
    });

    static Sand Box(Sand s)
    {
        s.LipRun = LipRun(s.Depression);
        s.MinX = s.Points.Min(p => p.X) - s.Radius;
        s.MaxX = s.Points.Max(p => p.X) + s.Radius;
        s.MinZ = s.Points.Min(p => p.Z) - s.Radius;
        s.MaxZ = s.Points.Max(p => p.Z) + s.Radius;
        return s;
    }

    /// <summary>
    /// Lowers every cell of sand by its bunker's depth at that cell's distance in from the sand's edge. Dabs and
    /// outlines that touch count as one bunker: the distance is to the edge of the sand as a whole. The sand is
    /// drawn on the field's own cells, with enough of it beyond the window that every window gives the same cut.
    /// </summary>
    public static void Apply(CourseModel course, Heightfield field)
    {
        var bunkers = Bunkers(course);
        if (bunkers.Count == 0) return;
        double cell = field.Cell, ox = field.Origin.X, oz = field.Origin.Z;
        int w = field.Width, h = field.Height;

        // Sand further than `far` cells from the edge is at full depth, so nothing beyond that matters to a cell.
        int far = (int)Math.Ceiling(bunkers.Max(b => Reach(b.Depression)) / cell) + 2;
        double margin = far * cell;
        var near = bunkers.Where(b => b.MaxX >= ox - margin && b.MinX <= ox + w * cell + margin
                                   && b.MaxZ >= oz - margin && b.MinZ <= oz + h * cell + margin).ToList();
        if (near.Count == 0) return;

        // The raster: the window and its margin, no wider than the sand it meets plus a cell of grass all round.
        int x0 = Math.Max(-far, (int)Math.Floor((near.Min(b => b.MinX) - ox) / cell) - 1);
        int x1 = Math.Min(w - 1 + far, (int)Math.Ceiling((near.Max(b => b.MaxX) - ox) / cell) + 1);
        int z0 = Math.Max(-far, (int)Math.Floor((near.Min(b => b.MinZ) - oz) / cell) - 1);
        int z1 = Math.Min(h - 1 + far, (int)Math.Ceiling((near.Max(b => b.MaxZ) - oz) / cell) + 1);
        int rw = x1 - x0 + 1, rh = z1 - z0 + 1;
        if (rw <= 0 || rh <= 0) return;

        // owner: 0 for grass, else 1 + the bunker's place in `near`.
        var owner = new int[rw * rh];
        var crossings = new List<double>();
        for (int n = 0; n < near.Count; n++)
        {
            var b = near[n];
            int bz0 = Math.Max(z0, (int)Math.Floor((b.MinZ - oz) / cell)), bz1 = Math.Min(z1, (int)Math.Ceiling((b.MaxZ - oz) / cell));
            for (int iz = bz0; iz <= bz1; iz++)
            {
                double z = oz + (iz + 0.5) * cell;
                var row = owner.AsSpan((iz - z0) * rw, rw);
                switch (b.Kind)
                {
                    case Kind.Disc:
                    {
                        double dz = z - b.Points[0].Z, rest = b.Radius * b.Radius - dz * dz;
                        if (rest >= 0) Fill(row, b.Points[0].X - Math.Sqrt(rest), b.Points[0].X + Math.Sqrt(rest), n + 1, ox, cell, x0);
                        break;
                    }
                    case Kind.Area:
                    {
                        // Even-odd fill between the outline's crossings of this row.
                        crossings.Clear();
                        var pts = b.Points;
                        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                            if ((pts[i].Z > z) != (pts[j].Z > z))
                                crossings.Add(pts[i].X + (z - pts[i].Z) * (pts[j].X - pts[i].X) / (pts[j].Z - pts[i].Z));
                        crossings.Sort();
                        for (int i = 0; i + 1 < crossings.Count; i += 2) Fill(row, crossings[i], crossings[i + 1], n + 1, ox, cell, x0);
                        break;
                    }
                    default:
                    {
                        // A ribbon is every point within half its width of the centre line: one stretch of the row per segment.
                        var pts = b.Points;
                        for (int i = 0; i + 1 < pts.Count; i++)
                        {
                            if (Math.Min(pts[i].Z, pts[i + 1].Z) - b.Radius > z || Math.Max(pts[i].Z, pts[i + 1].Z) + b.Radius < z) continue;
                            int sx0 = Math.Max(x0, (int)Math.Floor((Math.Min(pts[i].X, pts[i + 1].X) - b.Radius - ox) / cell));
                            int sx1 = Math.Min(x1, (int)Math.Ceiling((Math.Max(pts[i].X, pts[i + 1].X) + b.Radius - ox) / cell));
                            for (int ix = sx0; ix <= sx1; ix++)
                                if (SegmentDistance(new Vec2(ox + (ix + 0.5) * cell, z), pts[i], pts[i + 1]) <= b.Radius) row[ix - x0] = n + 1;
                        }
                        break;
                    }
                }
            }
        }

        // Each cell's distance up or down its column to grass, in cells, capped at `far`. Sand running off the
        // raster's edge carries on beyond it (the raster only stops short of sand at the window's margin).
        var column = new ushort[rw * rh];
        ushort cap = (ushort)Math.Min(far, ushort.MaxValue);
        Parallel.For(0, rw, x =>
        {
            int run = cap;
            for (int z = 0; z < rh; z++)
            {
                run = owner[z * rw + x] == 0 ? 0 : Math.Min(cap, run + 1);
                column[z * rw + x] = (ushort)run;
            }
            run = cap;
            for (int z = rh - 1; z >= 0; z--)
            {
                run = owner[z * rw + x] == 0 ? 0 : Math.Min(cap, run + 1);
                if (run < column[z * rw + x]) column[z * rw + x] = (ushort)run;
            }
        });

        // Then across each row: the nearest grass is the best of the columns either side.
        var values = field.Values;
        int capSquared = cap * cap;
        Parallel.For(Math.Max(0, z0), Math.Min(h - 1, z1) + 1, iz =>
        {
            int rz = iz - z0;
            for (int ix = Math.Max(0, x0); ix <= Math.Min(w - 1, x1); ix++)
            {
                int rx = ix - x0, who = owner[rz * rw + rx];
                if (who == 0) continue;
                int best = capSquared;
                for (int dx = -cap + 1; dx < cap; dx++)
                {
                    int cx = rx + dx;
                    if (cx < 0 || cx >= rw) continue;
                    int c = column[rz * rw + cx], d2 = dx * dx + c * c;
                    if (d2 < best) best = d2;
                }
                // The edge runs half a cell short of the first grass cell's centre.
                double inside = best >= capSquared ? double.PositiveInfinity : (Math.Sqrt(best) - 0.5) * cell;
                var b = near[who - 1];
                values[iz * w + ix] -= DepthAt(b.Depression, b.LipRun, inside);
            }
        });
    }

    /// <summary>Marks the cells of a row whose centres lie between two X positions.</summary>
    static void Fill(Span<int> row, double fromX, double toX, int value, double originX, double cell, int x0)
    {
        int a = Math.Max(x0, (int)Math.Ceiling((fromX - originX) / cell - 0.5));
        int b = Math.Min(x0 + row.Length - 1, (int)Math.Floor((toX - originX) / cell - 0.5));
        for (int ix = a; ix <= b; ix++) row[ix - x0] = value;
    }

    static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double l2 = ab.X * ab.X + ab.Z * ab.Z;
        double t = l2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / l2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }
}
