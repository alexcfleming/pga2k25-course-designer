using Pga2K25.CourseFormat;

namespace Pga2K25.HoleSim;

/// <summary>A point in a hole's local frame, in yards: <see cref="Lat"/> is right (+) or left (−), <see cref="Along"/> is toward the green.</summary>
public readonly record struct FramePoint(double Lat, double Along)
{
    public double DistanceTo(FramePoint o) => Math.Sqrt((Lat - o.Lat) * (Lat - o.Lat) + (Along - o.Along) * (Along - o.Along));
    public double Length => Math.Sqrt(Lat * Lat + Along * Along);
    public static FramePoint operator +(FramePoint a, FramePoint b) => new(a.Lat + b.Lat, a.Along + b.Along);
    public static FramePoint operator -(FramePoint a, FramePoint b) => new(a.Lat - b.Lat, a.Along - b.Along);
    public static FramePoint operator *(FramePoint a, double s) => new(a.Lat * s, a.Along * s);
}

/// <summary>
/// A hole's local frame: origin at the chosen tee, forward toward the green, in yards. The course file is in metres;
/// world north is −Z and east is −X, and right of a forward unit vector f is (f.Z, −f.X), as everywhere in the repo.
/// </summary>
public sealed class HoleFrame
{
    public const double Yard = 0.9144, Foot = 0.3048;

    public Vec2 Origin { get; }
    public Vec2 Forward { get; }
    public Vec2 Right { get; }

    public HoleFrame(Vec2 origin, Vec2 toward)
    {
        Origin = origin;
        Forward = (toward - origin).Normalized();
        Right = new Vec2(Forward.Z, -Forward.X);
    }

    public FramePoint ToFrame(Vec2 world)
    {
        var d = world - Origin;
        return new FramePoint((d.X * Right.X + d.Z * Right.Z) / Yard, (d.X * Forward.X + d.Z * Forward.Z) / Yard);
    }

    public Vec2 ToWorld(FramePoint p) => Origin + Forward * (p.Along * Yard) + Right * (p.Lat * Yard);
}

/// <summary>Distance from a point to a polyline, and which side of it the point lies on.</summary>
public static class Polyline
{
    /// <summary>Distance (same units as the points) and the signed side: + right of the line's direction, − left.</summary>
    public static (double Distance, int Side, double Along) Nearest(IReadOnlyList<FramePoint> line, FramePoint p)
    {
        double best = double.MaxValue, run = 0, bestAlong = 0; int side = 0;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            var ab = b - a; double len2 = ab.Lat * ab.Lat + ab.Along * ab.Along, len = Math.Sqrt(len2);
            double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.Lat - a.Lat) * ab.Lat + (p.Along - a.Along) * ab.Along) / len2, 0, 1);
            var q = a + ab * t; double d = p.DistanceTo(q);
            if (d < best)
            {
                best = d; bestAlong = run + t * len;
                // Right of the direction of travel: in the frame, right is +Lat when travelling +Along.
                double cross = ab.Along * (p.Lat - a.Lat) - ab.Lat * (p.Along - a.Along);
                side = Math.Sign(cross);
            }
            run += len;
        }
        return (best, side, bestAlong);
    }
}
