using Pga2K25.CourseFormat;

namespace Pga2K25.Readiness;

/// <summary>
/// Routing checks taken from Plot Starter's route engine (2026-11-08/09), whose limits came from Alex's ten 18-hole
/// courses and its Peahi ground rules: green-to-tee walks, the climb along a walk, each shot's rise or fall, tees, aim
/// points and greens on cliff-steep ground, shots played over a gorge, play near the plot edge, and doglegs bending one
/// way. All provisional (warnings) or information: they never hold the gate. Read-only, from the course and the model.
/// </summary>
public static class RoutingChecks
{
    const double YardsPerMetre = ReadinessChecker.YardsPerMetre, FeetPerMetre = ReadinessChecker.FeetPerMetre;
    const string Rule = "Routing · from Plot Starter's rules and your courses";

    /// <param name="feet">Ground height (ft) at a point, from the terrain model.</param>
    /// <param name="slopePct">Ground slope (%) at a point, read across about 12 m so a single cell's step doesn't count.</param>
    public static IEnumerable<ReadinessCheck> Run(IReadOnlyList<Hole> holes, Func<Vec2, double> feet, Func<Vec2, double> slopePct,
        ReadinessLimits L, double plotHalfMetres = 1000)
    {
        var ordered = holes.OrderBy(h => h.Number).ToList();
        if (ordered.Count == 0) yield break;
        static Vec2 Tee(Hole h) => h.Tees.Count > 0 ? h.Tees[^1] : h.Route[0];
        static List<Vec2> Line(Hole h) { var l = new List<Vec2> { Tee(h) }; l.AddRange(h.Route.Skip(1)); return l; }

        // Walks: from each green to the next hole's back tee.
        var walks = ordered.Zip(ordered.Skip(1), (a, b) => (From: a, To: b, Yards: Vec2.Distance(a.GreenAnchor, Tee(b)) * YardsPerMetre,
            Climb: Range(Along(a.GreenAnchor, Tee(b), 5).Select(feet)))).ToList();
        if (walks.Count > 0)
        {
            var longest = walks.MaxBy(w => w.Yards);
            var longWalks = walks.Where(w => w.Yards > L.MaxWalkYards).ToList();
            yield return new("walks", $"Green-to-tee walks up to {L.MaxWalkYards:0} yds", longWalks.Count > 0 ? Severity.Warning : Severity.Pass,
                longWalks.Count > 0
                    ? $"Over {L.MaxWalkYards:0}: {string.Join(", ", longWalks.Select(w => $"{w.From.Number} → {w.To.Number} ({w.Yards:0})"))}. Your courses' longest walks run 90–241 yds."
                    : $"Longest {longest.Yards:0} yds, {longest.From.Number} → {longest.To.Number}; median {Median(walks.Select(w => w.Yards)):0}. Your courses: medians 52–84, longest 90–241.",
                Rule, longWalks.Select(w => w.To.Number).ToList());

            var steepWalks = walks.Where(w => w.Climb > L.WalkClimbFeet).ToList();
            yield return new("walk-climb", $"No walk climbs or drops more than {L.WalkClimbFeet:0} ft along the way",
                steepWalks.Count > 0 ? Severity.Warning : Severity.Pass,
                steepWalks.Count > 0
                    ? $"{string.Join(", ", steepWalks.Select(w => $"{w.From.Number} → {w.To.Number} ({w.Climb:0} ft)"))}: a walk through a gorge or over a ridge."
                    : "Every walk keeps within it.",
                Rule, steepWalks.Select(w => w.To.Number).ToList());
        }

        // Each shot's rise or fall: tee to aim point, aim to aim, aim point to green.
        var steepShots = new List<string>();
        var steepShotHoles = new HashSet<int>();
        foreach (var h in ordered)
        {
            var line = Line(h);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double change = feet(line[i + 1]) - feet(line[i]);
                if (Math.Abs(change) <= L.ShotRiseFeet) continue;
                steepShots.Add($"{h.Number} {(i == 0 ? "tee" : "aim")} → {(i + 2 == line.Count ? "green" : "aim")} {change:+0;-0} ft");
                steepShotHoles.Add(h.Number);
            }
        }
        yield return new("shot-rise", $"Each shot rises or falls no more than {L.ShotRiseFeet:0} ft", steepShots.Count > 0 ? Severity.Warning : Severity.Pass,
            steepShots.Count > 0 ? string.Join("; ", steepShots) + "." : "Tee to aim point and aim point to green all keep within it.",
            Rule, steepShotHoles.Order().ToList());

        // Tees, aim points and greens on cliff-steep ground.
        var steepSites = new List<string>();
        var steepSiteHoles = new HashSet<int>();
        foreach (var h in ordered)
        {
            var line = Line(h);
            for (int i = 0; i < line.Count; i++)
            {
                double s = slopePct(line[i]);
                if (s < L.SteepSitePct) continue;
                steepSites.Add($"{h.Number} {(i == 0 ? "tee" : i == line.Count - 1 ? "green" : "aim point")} {s:0} %");
                steepSiteHoles.Add(h.Number);
            }
        }
        yield return new("steep-sites", $"Tees, aim points and greens off ground of {L.SteepSitePct:0} % or more", steepSites.Count > 0 ? Severity.Warning : Severity.Pass,
            steepSites.Count > 0 ? string.Join("; ", steepSites) + ". Cliff-edge ground: check the spot in game." : "None sits on cliff-steep ground.",
            Rule, steepSiteHoles.Order().ToList());

        // Shots over a gorge: a run of ground far below both ends of the shot.
        var carries = new List<string>();
        var carryHoles = new HashSet<int>();
        foreach (var h in ordered)
        {
            var line = Line(h);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double floor = Math.Min(feet(line[i]), feet(line[i + 1])) - L.GorgeFeet;
                var pts = Along(line[i], line[i + 1], 2).ToList();
                double run = 0, best = 0, deepest = double.MaxValue;
                for (int k = 1; k < pts.Count; k++)
                {
                    double f = feet(pts[k]);
                    if (f < floor) { run += Vec2.Distance(pts[k - 1], pts[k]); deepest = Math.Min(deepest, f); best = Math.Max(best, run); }
                    else run = 0;
                }
                if (best * YardsPerMetre < L.GorgeRunYards) continue;
                carries.Add($"{h.Number}: {best * YardsPerMetre:0} yds over ground {Math.Min(feet(line[i]), feet(line[i + 1])) - deepest:0} ft below");
                carryHoles.Add(h.Number);
            }
        }
        yield return new("gorges", "Shots played over a gorge", Severity.Info,
            carries.Count > 0 ? string.Join("; ", carries) + ". Fine as a deliberate carry; check it can be played from the back tee."
                              : $"No shot crosses {L.GorgeRunYards:0} yds or more of ground {L.GorgeFeet:0} ft below its ends.",
            Rule, carryHoles.Order().ToList());

        // Play near the plot edge: room to hide the edge with planting and hills.
        var near = ordered.Select(h => (Hole: h, Metres: h.Tees.Concat(h.Route).Min(p => plotHalfMetres - Math.Max(Math.Abs(p.X), Math.Abs(p.Z)))))
            .Where(x => x.Metres < L.EdgeSetbackMetres).OrderBy(x => x.Metres).ToList();
        yield return new("edge-setback", $"Play at least {L.EdgeSetbackMetres * YardsPerMetre:0} yds from the plot edge", near.Count > 0 ? Severity.Warning : Severity.Pass,
            near.Count > 0
                ? $"{string.Join(", ", near.Select(x => $"{x.Hole.Number} ({Math.Max(0, x.Metres) * YardsPerMetre:0})"))} yds from the edge: little room to hide it with planting and hills."
                : "Every hole leaves room to disguise the plot edge.",
            Rule, near.Select(x => x.Hole.Number).Order().ToList());

        // Doglegs: balanced left and right, with no long run one way.
        var bends = ordered.Where(h => h.Par >= 4).Select(h => (Hole: h, Turn: Turn(Line(h)))).Where(b => Math.Abs(b.Turn) >= L.DoglegDegrees).ToList();
        int left = bends.Count(b => b.Turn > 0), right = bends.Count - left;
        int run1 = 0, longestRun = 0;
        int? side = null;
        foreach (var b in bends)
        {
            int s = Math.Sign(b.Turn);
            run1 = s == side ? run1 + 1 : 1;
            side = s;
            longestRun = Math.Max(longestRun, run1);
        }
        bool lopsided = bends.Count >= 6 && Math.Max(left, right) > L.DoglegShareMax * bends.Count;
        bool runOn = longestRun >= L.DoglegRunMax;
        yield return new("doglegs", "Doglegs bend both ways", lopsided || runOn ? Severity.Warning : Severity.Pass,
            $"{left} bend left, {right} right (turns of {L.DoglegDegrees:0}° or more at the first aim point)"
            + (lopsided ? $"; more than {L.DoglegShareMax:P0} one way" : "")
            + (runOn ? $"; {longestRun} in a row the same way" : "") + ".",
            Rule, lopsided || runOn ? bends.Select(b => b.Hole.Number).ToList() : []);
    }

    /// <summary>Turn (°, left positive seen north up) at the line's first bend: tee → first aim → next point.</summary>
    public static double Turn(IReadOnlyList<Vec2> line)
    {
        if (line.Count < 3) return 0;
        Vec2 d1 = line[1] - line[0], d2 = line[2] - line[1];
        // North is −Z and east −X, so the X/Z cross product's sign is the same as on the north-up map: positive turns left.
        double cross = d1.X * d2.Z - d1.Z * d2.X, dot = d1.X * d2.X + d1.Z * d2.Z;
        return Math.Atan2(cross, dot) * 180 / Math.PI;
    }

    static IEnumerable<Vec2> Along(Vec2 a, Vec2 b, double stepMetres)
    {
        int n = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(a, b) / stepMetres));
        for (int i = 0; i <= n; i++) yield return a + (b - a) * ((double)i / n);
    }

    static double Range(IEnumerable<double> values)
    {
        var l = values.ToList();
        return l.Count == 0 ? 0 : l.Max() - l.Min();
    }

    static double Median(IEnumerable<double> values)
    {
        var l = values.Order().ToList();
        return l.Count == 0 ? 0 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }
}
