using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.Services;

/// <summary>
/// An in-game measure-tool reading along a route leg: tee → aim or aim → green. <paramref name="FromCourseFile"/> marks
/// one read from a measure line saved in the course file rather than typed in.
/// </summary>
public sealed record MeasuredLeg(int Hole, bool TeeToAim, int Yards, double Feet, bool FromCourseFile = false);

/// <summary>
/// Any other leg of a measure line in the course file: its two ends and the game's height change between them, with the
/// hole whose line of play it is nearest (0 when none is within 60 m).
/// </summary>
public sealed record MeasuredLine(int Hole, Vec2 From, Vec2 To, int Yards, double Feet);

/// <summary>
/// Readings kept beside a course's products as "Measurements.csv" (hole,leg,yards,feet; '#' comments), the same
/// format as samples/calibration/route-heights. Never written into the course file.
/// </summary>
public static partial class Measurements
{
    public static string PathFor(StudioSettings settings, string courseName) =>
        Path.Combine(Products.OutputDir(settings, courseName), "Measurements.csv");

    public static List<MeasuredLeg> Load(string path)
    {
        var legs = new List<MeasuredLeg>();
        if (!File.Exists(path)) return legs;
        foreach (var line in File.ReadAllLines(path))
        {
            var f = line.Split(',');
            if (f.Length < 4 || !int.TryParse(f[0], out int hole)) continue;
            if (!int.TryParse(f[2], out int yards) || !double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double feet)) continue;
            legs.Add(new MeasuredLeg(hole, f[1].Trim() == "tee-aim", yards, feet));
        }
        return legs;
    }

    /// <summary>How close a measure point must sit to a route marker (the route's tee, aim point or green point) to count as on it.</summary>
    public const double OnMarkerMetres = 4.6;   // 5 yds

    /// <summary>
    /// Route legs read from the measure lines saved in the course file: any leg of a line whose two ends sit on a hole's
    /// tee and aim markers, or its aim and green markers (within <see cref="OnMarkerMetres"/>, either way round), with the
    /// game's own heights at the two points. Feet run in the route's direction, as typed readings do.
    /// </summary>
    public static List<MeasuredLeg> FromCourse(CourseModel course) => FromLines(course.Holes, course.MeasureLines);

    public static List<MeasuredLeg> FromLines(IEnumerable<Hole> holes, IReadOnlyList<MeasureLine> lines)
    {
        var legs = new List<MeasuredLeg>();
        foreach (var hole in holes)
        {
            var r = hole.Route;
            if (r.Count < 2) continue;
            var wanted = new List<(bool TeeToAim, Vec2 From, Vec2 To)> { (true, r[0], r[1]) };
            if (r.Count > 2) wanted.Add((false, r[^2], r[^1]));   // on a two-point route the one leg is tee → aim
            foreach (var (teeToAim, from, to) in wanted)
            {
                // The closest-fitting leg if there are several.
                var best = lines.SelectMany(l => l.Legs())
                    .SelectMany(l => new[] { (A: l.A, B: l.B), (A: l.B, B: l.A) })
                    .Select(l => (l.A, l.B, Off: Math.Max(Vec2.Distance(l.A.Position, from), Vec2.Distance(l.B.Position, to))))
                    .Where(l => l.Off <= OnMarkerMetres)
                    .OrderBy(l => l.Off).FirstOrDefault();
                if (best.A is null) continue;
                int yards = (int)Math.Round(Vec2.Distance(best.A.Position, best.B.Position) * YardsPerMetre);
                legs.Add(new MeasuredLeg(hole.Number, teeToAim, yards, (best.B.Height - best.A.Height) / MetresPerFoot, FromCourseFile: true));
            }
        }
        return legs;
    }

    /// <summary>
    /// Every measure-line leg that isn't a route leg (<see cref="FromLines"/> takes those): each one checks the terrain
    /// model wherever it was laid, since the game's height change between its ends needs no datum.
    /// </summary>
    public static List<MeasuredLine> OtherLegs(CourseModel course) => OtherLegs(course.Holes, course.MeasureLines);

    public static List<MeasuredLine> OtherLegs(IEnumerable<Hole> holes, IReadOnlyList<MeasureLine> lines)
    {
        var list = holes.ToList();
        bool OnMarkers(Vec2 a, Vec2 b) => list.Any(h => h.Route.Any(r => Vec2.Distance(r, a) <= OnMarkerMetres) && h.Route.Any(r => Vec2.Distance(r, b) <= OnMarkerMetres));
        var result = new List<MeasuredLine>();
        foreach (var (a, b) in lines.SelectMany(l => l.Legs()))
        {
            if (OnMarkers(a.Position, b.Position)) continue;
            var mid = new Vec2((a.Position.X + b.Position.X) / 2, (a.Position.Z + b.Position.Z) / 2);
            var near = list.Select(h => (h.Number, D: RouteDistance(h.Route, mid))).Where(t => t.D <= 60).OrderBy(t => t.D).FirstOrDefault();
            result.Add(new MeasuredLine(near.Number, a.Position, b.Position,
                (int)Math.Round(Vec2.Distance(a.Position, b.Position) * YardsPerMetre), (b.Height - a.Height) / MetresPerFoot));
        }
        return result;
    }

    static double RouteDistance(IReadOnlyList<Vec2> route, Vec2 p)
    {
        double best = route.Count == 1 ? Vec2.Distance(route[0], p) : double.MaxValue;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            var (a, b) = (route[i], route[i + 1]);
            double dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
            double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2, 0, 1);
            best = Math.Min(best, Vec2.Distance(p, new Vec2(a.X + t * dx, a.Z + t * dz)));
        }
        return best;
    }

    /// <summary>
    /// The readings to use: every leg from the course file, and each typed leg the file doesn't also have (the file's
    /// heights are the game's own, so they win).
    /// </summary>
    public static List<MeasuredLeg> Combine(IEnumerable<MeasuredLeg> fromFile, IEnumerable<MeasuredLeg> typed)
    {
        var file = fromFile.ToList();
        return file.Concat(typed.Where(t => !file.Any(f => f.Hole == t.Hole && f.TeeToAim == t.TeeToAim))).ToList();
    }

    const double MetresPerFoot = 0.3048, YardsPerMetre = 1 / 0.9144;

    public static void Save(string path, IEnumerable<MeasuredLeg> legs)
    {
        legs = legs.Where(m => !m.FromCourseFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine("# In-game measure tool, clicked on the route markers (back tee square, aim-point marker, green circle); feet = second point minus first.");
        sb.AppendLine("hole,leg,yards,feet");
        foreach (var m in legs.OrderBy(m => m.Hole).ThenBy(m => m.TeeToAim ? 0 : 1))
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{m.Hole},{(m.TeeToAim ? "tee-aim" : "aim-green")},{m.Yards},{m.Feet:0.00}"));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Feet as the measure tool shows them or as typed: <c>+13'6"</c>, <c>-0'7</c>, <c>13 ft 6 in</c>, <c>-5.33</c>.
    /// A lone number is feet.
    /// </summary>
    public static bool TryParseFeet(string? text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = FeetPattern().Match(text.Trim());
        if (!m.Success) return false;
        double whole = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double inches = m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (inches >= 12) return false;
        feet = (whole + inches / 12) * (m.Groups[1].Value is "-" or "−" ? -1 : 1);
        return true;
    }

    [GeneratedRegex("""^([+\-−]?)\s*(\d+(?:\.\d+)?)\s*(?:'|ft)?\s*(?:(\d+(?:\.\d+)?)\s*(?:"|in)?)?$""", RegexOptions.IgnoreCase)]
    private static partial Regex FeetPattern();
}
