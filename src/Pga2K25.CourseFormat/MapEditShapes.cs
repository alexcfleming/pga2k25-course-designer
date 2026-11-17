namespace Pga2K25.CourseFormat;

/// <summary>
/// A painted map layer (<see cref="MapEdits"/>) as plain outlines, for drawing and for distances: filled splines
/// as their sampled outline, brushes as discs of their diameter (as the maps draw painted surfaces), open splines
/// as the band their width wide.
/// </summary>
public static class MapEditShapes
{
    public static List<List<Vec2>> Outlines(MapEdits edits, int samplesPerSegment = 12)
    {
        var result = new List<List<Vec2>>();
        foreach (var s in edits.Splines.Where(s => s.Points.Count >= 2))
        {
            var pts = Geometry.SampleSpline(s, samplesPerSegment);
            if (s.Closed && pts.Count >= 3) result.Add(pts);
            else if (!s.Closed) result.Add(Band(pts, Math.Max(s.Width, 0.5) / 2));
        }
        foreach (var b in edits.Brushes.Where(b => b.Scale > 0 && b.Value >= 0.3))
            result.Add(Disc(b.Position, b.Scale / 2));
        return result;
    }

    /// <summary>A circle as a 32-sided outline.</summary>
    public static List<Vec2> Disc(Vec2 centre, double radius) =>
        Enumerable.Range(0, 32).Select(i => centre + new Vec2(Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16)) * radius).ToList();

    /// <summary>A polyline's band <paramref name="half"/> m to each side, as one outline (square ends).</summary>
    public static List<Vec2> Band(IReadOnlyList<Vec2> line, double half)
    {
        var left = new List<Vec2>();
        var right = new List<Vec2>();
        for (int i = 0; i < line.Count; i++)
        {
            var a = line[Math.Max(0, i - 1)];
            var b = line[Math.Min(line.Count - 1, i + 1)];
            var d = (b - a).Normalized();
            var n = new Vec2(-d.Z, d.X) * half;
            left.Add(line[i] + n);
            right.Add(line[i] - n);
        }
        right.Reverse();
        left.AddRange(right);
        return left;
    }
}
