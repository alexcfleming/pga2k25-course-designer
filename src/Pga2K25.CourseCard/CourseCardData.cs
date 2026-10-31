using Pga2K25.CourseFormat;

namespace Pga2K25.CourseCard;

/// <param name="Yards">Scorecard yards per tee set, shortest → longest (the game's rule, from the course file).</param>
/// <param name="TeeBearing">Whole degrees clockwise from north: back tee → first aim point (par 3s: tee → green).</param>
/// <param name="ApproachBearing">Last aim point → green (par 3s: same as the tee bearing).</param>
/// <param name="CornerFraction">Where the tee leg turns onto the approach leg, as a share of the route length (from the file).</param>
/// <param name="Hazards">Hazard tags written by hand in the Studio, else read from the course file.</param>
/// <param name="GreenNotes">Green contours and hole positions written by hand in the Studio, else read from the course file.</param>
/// <param name="Name">The hole's name from the Studio's Holes card, or null.</param>
public sealed record CardHole(int Number, int Par, IReadOnlyList<int> Yards, int TeeBearing, int ApproachBearing,
    double CornerFraction, IReadOnlyList<string> Hazards, string? GreenNotes, string? Name = null)
{
    /// <summary>Signed bend from tee to approach bearing, −180…180.</summary>
    public int Bend => ((ApproachBearing - TeeBearing) % 360 + 540) % 360 - 180;
}

public sealed record CardTotals(int Par, IReadOnlyList<int> Yards);

public enum Severity { Info, Warn, Error }

public sealed record CardIssue(Severity Severity, string Message)
{
    public override string ToString() => $"[{Severity.ToString().ToUpperInvariant()}] {Message}";
}

/// <summary>
/// Everything the course cards show, read from the .course file (the source of truth). The Studio's Holes card
/// (Holes.json) adds only what the file lacks: tee set names, hole names, and any hazards or green notes written by hand.
/// </summary>
public sealed class CourseCardData
{
    public const double YardsPerMetre = 1.0936133;

    public required string CourseName { get; init; }
    public required IReadOnlyList<string> TeeNames { get; init; }
    public required IReadOnlyList<CardHole> Holes { get; init; }
    public required CardTotals Out { get; init; }
    public required CardTotals In { get; init; }
    public required CardTotals Total { get; init; }

    /// <summary>Green areas and perimeters from the course file's green splines (estimated greens are listed, never counted).</summary>
    public required GreenSizes Greens { get; init; }

    /// <summary>The longest tee set, which the rose and bar charts use.</summary>
    public int RoseTee => TeeNames.Count - 1;

    /// <param name="entries">The Studio's hole entries (Holes.json): tee names, hole names and hand-written notes.</param>
    /// <param name="teeNames">Tee names from the card settings, which win over <paramref name="entries"/>.</param>
    /// <param name="notes">Hazards and green notes written from the course file; a hand-written entry wins where it has one.</param>
    public static CourseCardData From(CourseModel course, CourseHoles? entries, IReadOnlyList<string>? teeNames, List<CardIssue> issues,
        IReadOnlyList<HoleNote>? notes = null)
    {
        int sets = Math.Max(1, course.Holes.Max(h => h.Tees.Count));
        var studio = entries?.Tees ?? [];
        var names = teeNames is { Count: > 0 } && teeNames.Count == sets ? teeNames
            : studio.Count == sets ? studio
            : sets == 1 ? ["Back"] : Enumerable.Range(1, sets).Select(i => i == sets ? "Back" : $"Tee {i}").ToList();
        if (teeNames is { Count: > 0 } && teeNames.Count != sets)
            issues.Add(new(Severity.Warn, $"Card settings name {teeNames.Count} tee sets ({string.Join(", ", teeNames)}) " +
                                          $"but the course file has {sets}; those names are not used."));
        else if (teeNames is not { Count: > 0 } && studio.Count > 0 && studio.Count != sets)
            issues.Add(new(Severity.Warn, $"The Holes card names {studio.Count} tee sets ({string.Join(", ", studio)}) " +
                                          $"but the course file has {sets}; those names are not used."));

        var holes = course.Holes.OrderBy(h => h.Number)
            .Select(h => FromHole(h, sets, entries?.For(h.Number), notes?.FirstOrDefault(n => n.Hole == h.Number))).ToList();
        CardTotals Sum(IEnumerable<CardHole> part)
        {
            var list = part.ToList();
            return new(list.Sum(h => h.Par), Enumerable.Range(0, sets).Select(i => list.Sum(h => h.Yards[i])).ToList());
        }
        return new CourseCardData
        {
            CourseName = string.IsNullOrWhiteSpace(course.Name) ? "Course" : course.Name,
            TeeNames = names,
            Holes = holes,
            Out = Sum(holes.Where(h => h.Number <= 9)),
            In = Sum(holes.Where(h => h.Number >= 10)),
            Total = Sum(holes),
            Greens = GreenSizes.From(course),
        };
    }

    static CardHole FromHole(Hole h, int sets, HoleEntry? entry, HoleNote? note)
    {
        var yards = Enumerable.Range(0, sets).Select(i => (int)Math.Round(h.TeeSetLength(i) * YardsPerMetre)).ToList();
        var back = h.Tees.Count > 0 ? h.Tees[^1] : h.Route[0];
        var route = new List<Vec2> { back };
        route.AddRange(h.Route.Skip(1));

        int tee, approach;
        double corner = 1;
        if (h.Par == 3 || route.Count < 3)
        {
            // Par 3s play straight at the green; the routing tool's aim point on a par 3 isn't a corner.
            tee = approach = Bearing(back, h.GreenAnchor);
        }
        else
        {
            tee = Bearing(route[0], route[1]);
            approach = Bearing(route[^2], route[^1]);
            double total = route.Zip(route.Skip(1), Vec2.Distance).Sum();
            corner = total > 0 ? Vec2.Distance(route[0], route[1]) / total : 0.5;
        }
        var hand = entry?.Hazards.Select(z => z.Trim()).Where(z => z.Length > 0).ToList() ?? [];
        IReadOnlyList<string> hazards = hand.Count > 0 ? hand : note?.Hazards ?? [];
        string? green = entry?.GreenNotes.Trim() is { Length: > 0 } g ? g : note is { GreenNotes.Length: > 0 } ? note.GreenNotes : null;
        string? name = entry?.Name.Trim() is { Length: > 0 } n ? n : null;
        return new CardHole(h.Number, h.Par, yards, tee, approach, corner, hazards, green, name);
    }

    /// <summary>Whole-degree compass bearing from a to b. Game north is −Z and east is −X.</summary>
    public static int Bearing(Vec2 a, Vec2 b)
    {
        var d = b - a;
        double deg = Math.Atan2(-d.X, -d.Z) * 180 / Math.PI;
        return (int)Math.Round((deg % 360 + 360) % 360) % 360;
    }

    /// <summary>The handoff's validation rules: report, never correct.</summary>
    public IEnumerable<CardIssue> Validate(int maxBend = 45)
    {
        if (Holes.Count != 18) yield return new(Severity.Error, $"Expected 18 holes, found {Holes.Count}.");
        foreach (var h in Holes)
        {
            if (h.Par is < 3 or > 5) yield return new(Severity.Error, $"Hole {h.Number}: par {h.Par} is not 3, 4 or 5.");
            if (h.Yards.Zip(h.Yards.Skip(1)).Any(p => p.First > p.Second))
                yield return new(Severity.Error, $"Hole {h.Number}: tee yardages not ascending ({string.Join(" ≤ ", h.Yards)}).");
            if (Math.Abs(h.Bend) > maxBend)
                yield return new(Severity.Warn, $"Hole {h.Number}: dogleg bend {h.Bend:+0;-0} deg (tee {h.TeeBearing} · approach {h.ApproachBearing}).");
        }
    }
}
