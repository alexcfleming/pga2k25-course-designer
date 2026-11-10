using System.Text.Json.Nodes;

namespace Pga2K25.CourseFormat;

/// <summary>
/// One line drawn with the designer's measure tool, as saved in <c>distanceMarkers[]</c>: the points in the order they
/// join, each with the game's own ground height (m). A line of one click-to-click leg has two points.
/// </summary>
public sealed record MeasureLine(IReadOnlyList<DistanceMarker> Points)
{
    /// <summary>The line's legs, point to next point.</summary>
    public IEnumerable<(DistanceMarker A, DistanceMarker B)> Legs()
    {
        for (int i = 0; i + 1 < Points.Count; i++) yield return (Points[i], Points[i + 1]);
    }
}

/// <summary>
/// Reads the measure tool's lines. The file keeps each leg as two entries that point at each other
/// (<c>{position, adjacentMarkers: [the other end]}</c>); a line clicked on through several points repeats the shared
/// point once per leg (Halvoy, Berwick Coast). Legs are joined into lines where their ends are the same point.
/// </summary>
public static class MeasureLines
{
    public static IReadOnlyList<MeasureLine> Read(JsonArray? markers)
    {
        // Points keyed to the centimetre, so a point repeated for two legs is one node.
        var nodes = new Dictionary<(long, long), DistanceMarker>();
        var links = new Dictionary<(long, long), HashSet<(long, long)>>();
        (long, long) Key(DistanceMarker m) => ((long)Math.Round(m.Position.X * 100), (long)Math.Round(m.Position.Z * 100));
        static DistanceMarker? Read(JsonNode? p)
        {
            if (p is null) return null;
            double y = CourseModel.Num(p["y"], double.NaN);
            return double.IsFinite(y) ? new DistanceMarker(new Vec2(CourseModel.Num(p["x"]), CourseModel.Num(p["z"])), y) : null;
        }
        void Add(DistanceMarker m) { var k = Key(m); nodes.TryAdd(k, m); if (!links.ContainsKey(k)) links[k] = []; }

        foreach (var entry in markers ?? [])
        {
            if (Read(entry?["position"]) is not { } a) continue;
            Add(a);
            foreach (var adj in entry!["adjacentMarkers"] as JsonArray ?? [])
            {
                if (Read(adj) is not { } b) continue;
                Add(b);
                var (ka, kb) = (Key(a), Key(b));
                if (ka == kb) continue;
                links[ka].Add(kb); links[kb].Add(ka);
            }
        }

        // Walk each chain from an end (a point with one neighbour), then any loops that are left; a branch starts a new line.
        var lines = new List<MeasureLine>();
        var used = new HashSet<((long, long), (long, long))>();
        bool Use((long, long) a, (long, long) b) => used.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a));
        var order = nodes.Keys.OrderBy(k => links[k].Count == 1 ? 0 : 1).ToList();   // file order within each group
        foreach (var start in order)
        {
            while (true)
            {
                var path = new List<DistanceMarker> { nodes[start] };
                var at = start;
                while (true)
                {
                    var from = at;
                    var open = links[from].Where(n => !used.Contains(from.CompareTo(n) < 0 ? (from, n) : (n, from))).Take(1).ToList();
                    if (open.Count == 0) break;
                    Use(from, open[0]);
                    path.Add(nodes[open[0]]);
                    at = open[0];
                }
                if (path.Count < 2) break;
                lines.Add(new MeasureLine(path));
            }
        }
        // A lone point (no leg) still carries a height.
        foreach (var (k, m) in nodes)
            if (links[k].Count == 0) lines.Add(new MeasureLine([m]));
        return lines;
    }
}
