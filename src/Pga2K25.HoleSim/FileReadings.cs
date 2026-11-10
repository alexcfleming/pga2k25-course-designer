using Pga2K25.CourseFormat;

namespace Pga2K25.HoleSim;

/// <summary>A measure point from the course file put to a live-check spot: where it really is, and the game's height there (ft).</summary>
public sealed record FilePoint(string Spot, FramePoint Where, double HeightFt);

/// <summary>
/// Live checks read from the course file's measure lines instead of typed in. <see cref="Spots"/> are the points that sit on
/// suggested spots; <see cref="Tee"/> and <see cref="Green"/> are the points that can be the zero (on the chosen tee, on
/// the green point G); <see cref="Extra"/> are the other points near the hole, read as more readings (D1, D2…).
/// </summary>
public sealed record FileReadings(IReadOnlyList<FilePoint> Spots, FilePoint? Tee, IReadOnlyList<FilePoint> Extra, int PointsInFile)
{
    public FilePoint? Green => Spots.FirstOrDefault(s => s.Spot == "G");
    public bool Any => Spots.Count > 0 || Extra.Count > 0;

    /// <summary>
    /// Matches the course's measure points to <paramref name="spots"/>. The pattern Alex lays down in the designer: one
    /// measure line per hole, starting on the chosen tee, clicking each suggested spot (found by its TO TEE and TO GREEN
    /// yards), the green point and the spots round each pin, then saving. The order and the number of lines don't matter:
    /// each point goes to the nearest spot within reach (2 yds for a spot by a pin, 3 for G, 6 along the line), nearest
    /// pairs first, one point per spot. The reading is taken at the point itself, so a click a few yards off a spot
    /// costs nothing. A point within 5 yds of the tee is the tee; other points within 40 yds of the line of play,
    /// from 10 yds behind the tee to 25 past the green, are extra readings.
    /// </summary>
    public static FileReadings Match(CourseModel course, HoleModel hole, IEnumerable<MeasureSpot> spots) => Match(course.MeasureLines, hole, spots);

    public static FileReadings Match(IEnumerable<MeasureLine> lines, HoleModel hole, IEnumerable<MeasureSpot> spots)
    {
        // A point shared by two legs is one point.
        var points = lines.SelectMany(l => l.Points)
            .DistinctBy(p => (Math.Round(p.Position.X, 2), Math.Round(p.Position.Z, 2)))
            .Select(p => (Where: hole.Frame.ToFrame(p.Position), Ft: p.Height / HoleFrame.Foot))
            .ToList();
        var spotList = spots.ToList();
        static double Reach(MeasureSpot s) => s.Name == "G" ? 3 : s.Name.Length >= 3 && s.Name[0] == 'P' && char.IsDigit(s.Name[1]) ? 2 : 6;

        var tee = points.Select((p, i) => (p, i)).Where(t => t.p.Where.Length <= 5).OrderBy(t => t.p.Where.Length).Take(1).ToList();
        var taken = new HashSet<int>(tee.Select(t => t.i));
        var filled = new HashSet<string>();
        var matched = new List<FilePoint>();
        var pairs = from pi in Enumerable.Range(0, points.Count)
                    from s in spotList
                    let d = points[pi].Where.DistanceTo(s.Where)
                    where d <= Reach(s)
                    orderby d
                    select (pi, s, d);
        foreach (var (pi, s, _) in pairs.ToList())
        {
            if (taken.Contains(pi) || filled.Contains(s.Name)) continue;
            taken.Add(pi); filled.Add(s.Name);
            matched.Add(new FilePoint(s.Name, points[pi].Where, points[pi].Ft));
        }
        var extra = points.Where((p, i) => !taken.Contains(i)
                && p.Where.Along > -10 && p.Where.Along < hole.GreenFrame.Along + 25 && Polyline.Nearest(hole.Route, p.Where).Distance <= 40)
            .OrderBy(p => p.Where.Along)
            .Select((p, i) => new FilePoint($"D{i + 1}", p.Where, p.Ft)).ToList();
        var order = spotList.Select(s => s.Name).ToList();
        return new FileReadings(matched.OrderBy(m => order.IndexOf(m.Spot)).ToList(),
            tee.Count == 0 ? null : new FilePoint("tee", tee[0].p.Where, tee[0].p.Ft), extra, points.Count);
    }
}
