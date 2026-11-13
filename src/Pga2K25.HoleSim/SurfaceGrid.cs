using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.HoleSim;

/// <summary>What the ball lies on, as the simulator sees it. Painted in this order, low to high.</summary>
public enum Ground : byte { Native, HeavyRough, Rough, Scrub, Water, Fairway, Bunker, Green }

public static class Grounds
{
    public static IReadOnlyList<Ground> All { get; } = Enum.GetValues<Ground>();

    public static string Name(Ground g) => g switch
    {
        Ground.Native => "Native",
        Ground.HeavyRough => "Heavy rough",
        Ground.Rough => "Rough",
        Ground.Scrub => "Scrub",
        Ground.Water => "Water",
        Ground.Fairway => "Fairway",
        Ground.Bunker => "Bunker",
        _ => "Green",
    };

    /// <summary>Hazards the read-back lists along the line of play.</summary>
    public static bool IsHazard(Ground g) => g is Ground.Bunker or Ground.Scrub or Ground.Water;

    /// <summary>The ball stops where it lands on these (bunker sand, scrub, water).</summary>
    public static bool Stops(Ground g) => g is Ground.Bunker or Ground.Scrub or Ground.Water;
}

/// <summary>A shape to paint onto the grid, in world metres.</summary>
public abstract record PaintShape(Ground Ground);
public sealed record PaintPolygon(Ground Ground, IReadOnlyList<Vec2> Points) : PaintShape(Ground);
public sealed record PaintDisc(Ground Ground, Vec2 Centre, double RadiusMetres) : PaintShape(Ground);

/// <summary>
/// The hole's surfaces on a 1-yard grid in the hole frame. Cells are painted in <see cref="Ground"/> order, so later
/// grounds cover earlier ones (green over bunker over fairway over water over scrub over rough).
/// </summary>
public sealed class SurfaceGrid
{
    const int Code = 30;   // grey step per ground, wide enough that resampling never confuses neighbours

    readonly byte[] _cells;

    public HoleFrame Frame { get; }
    public int LatMin { get; }
    public int AlongMin { get; }
    public int Width { get; }
    public int Height { get; }

    SurfaceGrid(HoleFrame frame, int latMin, int alongMin, int width, int height, byte[] cells)
    {
        Frame = frame; LatMin = latMin; AlongMin = alongMin; Width = width; Height = height; _cells = cells;
    }

    public int LatMax => LatMin + Width;
    public int AlongMax => AlongMin + Height;

    /// <summary>The ground at a frame point; outside the grid is native ground.</summary>
    public Ground At(FramePoint p)
    {
        int x = (int)Math.Floor(p.Lat - LatMin), y = (int)Math.Floor(p.Along - AlongMin);
        return x < 0 || y < 0 || x >= Width || y >= Height ? Ground.Native : (Ground)_cells[y * Width + x];
    }

    public Ground this[int x, int y] => (Ground)_cells[y * Width + x];

    public FramePoint CellCentre(int x, int y) => new(LatMin + x + 0.5, AlongMin + y + 0.5);

    /// <summary>A copy with each cell replaced by <paramref name="edit"/>(x, y, ground): the hook for virtual design edits.</summary>
    public SurfaceGrid With(Func<int, int, Ground, Ground> edit)
    {
        var cells = new byte[_cells.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                cells[y * Width + x] = (byte)edit(x, y, (Ground)_cells[y * Width + x]);
        return new SurfaceGrid(Frame, LatMin, AlongMin, Width, Height, cells);
    }

    /// <summary>
    /// How many steps each cell is from the nearest of <paramref name="cells"/>, a step being a move to any of the
    /// eight cells round a cell (so a yard on the diagonal counts as one). Cells more than <paramref name="reach"/>
    /// steps away, or with none of them in reach, are <see cref="int.MaxValue"/>. Cells are numbered row by row:
    /// y × <see cref="Width"/> + x.
    /// </summary>
    public int[] StepsFrom(IEnumerable<int> cells, int reach)
    {
        int w = Width, h = Height;
        var steps = new int[w * h];
        Array.Fill(steps, int.MaxValue);
        var queue = new Queue<int>();
        foreach (int k in cells) { steps[k] = 0; queue.Enqueue(k); }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue(), x = k % w, y = k / w, d = steps[k];
            if (d >= reach) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int kk = yy * w + xx;
                    if (steps[kk] <= d + 1) continue;
                    steps[kk] = d + 1; queue.Enqueue(kk);
                }
        }
        return steps;
    }

    /// <summary>Cell count per ground.</summary>
    public int[] Counts()
    {
        var n = new int[Grounds.All.Count];
        foreach (var c in _cells) n[c]++;
        return n;
    }

    public static SurfaceGrid Build(HoleFrame frame, int latMin, int latMax, int alongMin, int alongMax, IEnumerable<PaintShape> shapes)
    {
        int w = latMax - latMin, h = alongMax - alongMin;
        using var bmp = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);   // native
            SKPoint Px(Vec2 world) { var p = frame.ToFrame(world); return new SKPoint((float)(p.Lat - latMin), (float)(p.Along - alongMin)); }
            foreach (var group in shapes.GroupBy(s => s.Ground).OrderBy(g => g.Key))
            {
                if (group.Key == Ground.Native) continue;
                byte v = (byte)((int)group.Key * Code);
                using var paint = new SKPaint { Color = new SKColor(v, v, v), IsAntialias = false, Style = SKPaintStyle.Fill };
                foreach (var s in group)
                {
                    switch (s)
                    {
                        case PaintPolygon poly when poly.Points.Count >= 3:
                            using (var b = new SKPathBuilder())
                            {
                                b.MoveTo(Px(poly.Points[0]));
                                for (int i = 1; i < poly.Points.Count; i++) b.LineTo(Px(poly.Points[i]));
                                b.Close();
                                using var path = b.Detach();
                                canvas.DrawPath(path, paint);
                            }
                            break;
                        case PaintDisc disc when disc.RadiusMetres > 0:
                            canvas.DrawCircle(Px(disc.Centre), (float)(disc.RadiusMetres / HoleFrame.Yard), paint);
                            break;
                    }
                }
            }
        }
        var cells = new byte[w * h];
        var bytes = bmp.GetPixelSpan();
        int rowBytes = bmp.RowBytes;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells[y * w + x] = (byte)Math.Clamp((int)Math.Round(bytes[y * rowBytes + x] / (double)Code), 0, Grounds.All.Count - 1);
        return new SurfaceGrid(frame, latMin, alongMin, w, h, cells);
    }
}
