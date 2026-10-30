using SkiaSharp;

namespace Pga2K25.Maps;

public enum RegionShape { Island, Peninsula, Bay, Lake, Inland }

/// <summary>
/// An invented landscape around the course, in a north-up page-like frame (x east, y south) about 280 units across,
/// centred near (0, 0). <see cref="SeaAround"/>: land is drawn over a hatched sea; otherwise water is drawn on land.
/// </summary>
public sealed class Region
{
    public required RegionShape Shape { get; init; }
    public required bool SeaAround { get; init; }
    public List<List<SKPoint>> Land { get; init; } = [];
    public List<List<SKPoint>> Water { get; init; } = [];
    public List<List<SKPoint>> Rivers { get; init; } = [];
    public List<SKPoint> Hills { get; init; } = [];
    /// <summary>Where the course sits, always on a shore that faces the plot's sea side (inland: the middle).</summary>
    public required SKPoint Pin { get; init; }
    /// <summary>Middle of the land, for its name.</summary>
    public required SKPoint LabelAt { get; init; }
    public required double KmPerUnit { get; init; }
}

/// <summary>
/// Grows a fictional region from a seed. Each shape is built with its sea to the west of the course, then turned so
/// that shore faces <c>seaBearing</c>, the plot's real sea side. Uses its own random numbers (mulberry32), so a seed
/// draws the same coast in every version of the app.
/// </summary>
public static class RegionGenerator
{
    sealed class Rng(uint seed)
    {
        uint _s = seed;
        public double Next()
        {
            unchecked
            {
                _s += 0x6D2B79F5;
                uint t = (_s ^ (_s >> 15)) * (1 | _s);
                t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    /// <param name="seaBearing">Bearing of the plot's sea (west 270); null for none, which turns nothing.</param>
    /// <param name="sizeKm">Roughly how far across the region is.</param>
    public static Region Generate(RegionShape shape, int seed, double? seaBearing, double sizeKm = 25)
    {
        var r = new Rng(unchecked((uint)(seed * 31 + (int)shape * 7919)));
        List<SKPoint> land = [], water = [];
        List<List<SKPoint>> rivers = [];
        List<SKPoint> hills = [];
        SKPoint pin, label = new(0, 0);
        bool seaAround;

        switch (shape)
        {
            case RegionShape.Island:
            {
                land = Roughen(Blob(r, 0, 0, 95, 70, 18, 0.17), r, 4, 0.3);
                pin = land.Where(p => Math.Abs(p.Y) < 35).DefaultIfEmpty(land[0]).MinBy(p => p.X);
                seaAround = true;
                break;
            }
            case RegionShape.Peninsula:
            {
                var arm = Roughen([new(150, -120), new(70, -55), new(10, -30), new(-75, -24), new(-100, 0), new(-70, 26), new(10, 32), new(70, 60), new(150, 120)], r, 4, 0.35, close: false);
                land = [.. arm, new(400, 400), new(400, -400)];
                pin = arm.MinBy(p => p.X);
                label = new(90, 0);
                seaAround = true;
                break;
            }
            case RegionShape.Bay:
            {
                // Open sea to the west with a bay cut into the land; the course on the bay's inner shore.
                var north = Roughen([new(-160, -400), new(-150, -200), new(-170, -60)], r, 4, 0.3, close: false);
                var inner = Roughen([new(-170, -60), new(-90, -55), new(10, -35), new(40, 0), new(10, 35), new(-90, 55), new(-170, 60)], r, 4, 0.3, close: false);
                var south = Roughen([new(-170, 60), new(-150, 200), new(-160, 400)], r, 4, 0.3, close: false);
                water = [new(-400, -400), .. north, .. inner.Skip(1), .. south.Skip(1), new(-400, 400)];
                var tip = inner.MaxBy(p => p.X);
                pin = new(tip.X + 3, tip.Y);
                label = new(100, -70);
                seaAround = false;
                break;
            }
            case RegionShape.Lake:
            {
                water = Roughen(Blob(r, -55, 0, 45, 85, 16, 0.18), r, 4, 0.3);
                var east = water.Where(p => Math.Abs(p.Y) < 30).DefaultIfEmpty(water[0]).MaxBy(p => p.X);
                pin = new(east.X + 4, east.Y);
                label = new(-55, 0);
                seaAround = false;
                break;
            }
            default:
            {
                // No shore: a river wandering past the course, two lakes, a scatter of hills.
                rivers.Add(Roughen([new(-260, (float)(-60 + r.Next() * 60)), new(-90, (float)(-30 + r.Next() * 40)), new(30, (float)(30 + r.Next() * 30)),
                                    new(140, (float)(10 + r.Next() * 50)), new(300, (float)(70 + r.Next() * 40))], r, 5, 0.35, close: false));
                var lakes = Enumerable.Range(0, 2).Select(i => Roughen(Blob(r, -120 + i * 210 + r.Next() * 40, (i == 1 ? -70 : 75) + r.Next() * 20,
                    16 + r.Next() * 10, 10 + r.Next() * 8, 10, 0.2), r, 3, 0.3)).ToList();
                for (int i = 0; i < 7; i++)
                {
                    var h = new SKPoint((float)(-150 + r.Next() * 300), (float)(-110 + r.Next() * 220));
                    if (Math.Sqrt(h.X * h.X + h.Y * h.Y) > 40) hills.Add(h);
                }
                // Nothing faces a sea, so nothing is turned.
                return new Region
                {
                    Shape = shape, SeaAround = false, Water = lakes, Rivers = rivers, Hills = hills,
                    Pin = new(0, 0), LabelAt = new(0, 120), KmPerUnit = sizeKm / 200,
                };
            }
        }

        // Turn so the pin's sea (built to the west) faces the plot's sea side.
        double rot = seaBearing is double b ? (b - 270) * Math.PI / 180 : 0;
        return new Region
        {
            Shape = shape, SeaAround = seaAround,
            Land = land.Count > 0 ? [Turn(land, rot)] : [],
            Water = water.Count > 0 ? [Turn(water, rot)] : [],
            Pin = Turn([pin], rot)[0], LabelAt = Turn([label], rot)[0], KmPerUnit = sizeKm / 200,
        };
    }

    static List<SKPoint> Turn(List<SKPoint> pts, double rot)
    {
        if (rot == 0) return pts;
        float c = (float)Math.Cos(rot), s = (float)Math.Sin(rot);
        return pts.Select(p => new SKPoint(p.X * c - p.Y * s, p.X * s + p.Y * c)).ToList();
    }

    /// <summary>A lumpy ellipse: a few random harmonics on the radius.</summary>
    static List<SKPoint> Blob(Rng r, double cx, double cy, double rx, double ry, int n, double amp)
    {
        var k = Enumerable.Range(0, 4).Select(_ => (A: r.Next() * amp, Ph: r.Next() * Math.PI * 2)).ToList();
        return Enumerable.Range(0, n).Select(i =>
        {
            double t = (double)i / n * Math.PI * 2;
            double f = 1 + k.Select((h, j) => h.A * Math.Sin((j + 2) * t + h.Ph)).Sum();
            return new SKPoint((float)(cx + Math.Cos(t) * rx * f), (float)(cy + Math.Sin(t) * ry * f));
        }).ToList();
    }

    /// <summary>Midpoint displacement: each pass splits every edge and nudges the midpoint sideways.</summary>
    static List<SKPoint> Roughen(List<SKPoint> pts, Rng r, int depth, double amp, bool close = true)
    {
        var output = pts;
        for (int d = 0; d < depth; d++)
        {
            var next = new List<SKPoint>();
            int n = output.Count, m = close ? n : n - 1;
            for (int i = 0; i < m; i++)
            {
                SKPoint a = output[i], b = output[(i + 1) % n];
                float dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(1e-3f, MathF.Sqrt(dx * dx + dy * dy));
                float off = (float)((r.Next() - 0.5) * len * amp);
                next.Add(a);
                next.Add(new SKPoint((a.X + b.X) / 2 - dy / len * off, (a.Y + b.Y) / 2 + dx / len * off));
            }
            if (!close) next.Add(output[n - 1]);
            output = next;
        }
        return output;
    }

    public static RegionShape Parse(string? shape) =>
        Enum.TryParse<RegionShape>(shape, ignoreCase: true, out var s) ? s : RegionShape.Island;
}
