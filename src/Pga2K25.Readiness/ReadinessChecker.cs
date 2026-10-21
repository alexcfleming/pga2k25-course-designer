using System.Globalization;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;

namespace Pga2K25.Readiness;

/// <summary>
/// The publish-readiness checks a course file can answer by itself: scorecard and routing (§7), the tour-readiness
/// measures (§11: drive tests, scale, corridors), elevation and green relief (§3), and the green speed, fringe
/// and lighting settings. Judgement calls (waypoints, sand, realism, cameras) stay with the designer.
/// Terrain figures come from the replayed brushes, so they say "confirm in game".
/// </summary>
public static class ReadinessChecker
{
    public const double YardsPerMetre = 1.0936133, FeetPerMetre = 3.2808399;

    /// <summary>
    /// <c>greenSpeed</c> as the game's Stimp reading: the line through two in-game readings, Cassimbo 0.97 → 10.9 and
    /// Sagan-Khushun 0.905 → 10.1. Null for −1, the game's default.
    /// </summary>
    public static double? Stimp(double greenSpeed) => greenSpeed < 0 ? null : 10.9 + (greenSpeed - 0.97) * (0.8 / 0.065);

    /// <summary><c>greenFringeRadius</c> at or under this shows no visible fringe (0.5 on Cassimbo and Sagan-Khushun).</summary>
    public const double NoFringeRadius = 0.5;

    /// <summary>A drawn green further than this from every hole's green is a practice green (as in the estimate).</summary>
    const double PracticeGreenMetres = 45 / YardsPerMetre;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string N0(double v) => v.ToString("N0", Inv);
    static string List(IEnumerable<int> holes) => string.Join(", ", holes);
    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <param name="cellMetres">Terrain cell for the elevation survey; the greens use the greens book's own.</param>
    public static ReadinessReport Run(CourseModel course, ReadinessLimits? limits = null, double cellMetres = 2)
    {
        var L = limits ?? new ReadinessLimits();
        var holes = course.Holes.OrderBy(h => h.Number).ToList();
        var outlines = holes.ToDictionary(h => h.Number, h => GreenOutline.Resolve(course, h));
        var sizes = GreenSizes.From(course);
        var fairways = course.SurfaceSplines.Where(s => s.Surface == Surfaces.Fairway && s.Closed)
            .Select(s => Geometry.SampleSpline(s)).Where(p => p.Count >= 3).ToList();
        var survey = TerrainSurvey.Build(course, new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = cellMetres } });
        var practice = CourseLayers.From(course).Greens.Where(g => g.Count >= 3)
            .Where(g => holes.All(h => (Geometry.Contains(g, h.GreenAnchor) ? 0 : Geometry.DistanceToEdge(g, h.GreenAnchor)) > PracticeGreenMetres))
            .ToList();
        var greensOptions = new GreensBookOptions();

        var measures = new List<HoleMeasures>();
        foreach (var h in holes)
        {
            var outline = outlines[h.Number];
            var size = sizes.For(h.Number);
            var tee = BackTee(h);
            int yards = Yards(h);

            string? driveClass = null;
            int? left = null;
            if (L.TestDriveYards is double drive)
            {
                if (h.Par == 4 && yards - drive <= L.DrivableWithinYards) driveClass = "Drivable";
                else if (h.Par == 4 && yards - drive <= L.NearDrivableWithinYards) driveClass = "Near-drivable";
                if (h.Par == 5) left = (int)Math.Round(yards - drive);
            }

            var (width, off) = h.Par >= 4 ? LandingWidth(h, tee, fairways, L) : (null, false);

            double? relief = null, slope = null;
            if (!outline.Estimated)
                try
                {
                    var a = GreenAnalyzer.Analyze(course, h, greensOptions);
                    relief = (a.HighInches - a.LowInches) / 12;
                    slope = a.MedianSlopePct;
                }
                catch (Exception) { /* a green the analysis can't read is left blank */ }

            var line = new List<Vec2> { tee };
            line.AddRange(h.Route.Skip(1));
            int? nearOf = null;
            double? nearYards = null;
            foreach (var o in holes.Where(o => o.Number != h.Number && !outlines[o.Number].Estimated))
            {
                if (SameGreen(outlines[o.Number], outline)) continue;   // a shared (double) green is its own check
                double d = Clearance(line, outlines[o.Number].Points);
                if (nearYards is null || d * YardsPerMetre < nearYards) { nearYards = d * YardsPerMetre; nearOf = o.Number; }
            }
            foreach (var g in practice)
            {
                double d = Clearance(line, g);
                if (nearYards is null || d * YardsPerMetre < nearYards) { nearYards = d * YardsPerMetre; nearOf = 0; }
            }

            var routing = survey.Holes.FirstOrDefault(r => r.Hole.Number == h.Number);
            measures.Add(new HoleMeasures
            {
                Number = h.Number, Par = h.Par, Yards = yards, DriveClass = driveClass, LeftAfterDrive = left,
                GreenSquareFeet = size?.SquareFeet ?? 0, GreenEstimated = outline.Estimated,
                LandingWidthYards = width, LandingOffFairway = off,
                ChangeFeet = routing?.ChangeFeet ?? 0,
                GreenReliefFeet = relief, GreenMedianSlopePct = slope,
                NearGreenOf = nearOf, NearGreenYards = nearYards,
                EndsOnOwnGreen = !outline.Estimated,
                Pins = Pins(h, outline),
            });
        }

        double medGreen = sizes.MedianSquareFeet;
        double medWidth = Median(measures.Where(m => m.LandingWidthYards > 0).Select(m => m.LandingWidthYards!.Value));
        measures = measures.Select(m => m with
        {
            GreenRatio = !m.GreenEstimated && medGreen > 0 ? m.GreenSquareFeet / medGreen : null,
            WidthRatio = m.LandingWidthYards is double w && w > 0 && medWidth > 0 ? w / medWidth : null,
        }).ToList();

        var playPoints = holes.SelectMany(h => h.Tees.Concat(h.Route))
            .Concat(survey.Holes.Select(r => r.GreenSiteCentre)).Select(survey.Feet).ToList();
        double plotRange = playPoints.Count == 0 ? 0 : playPoints.Max() - playPoints.Min();

        var teeSets = TeeSets(holes);
        var runs = Runs(measures, L);
        double? stimp = Stimp(course.GreenSpeed);

        var checks = new List<ReadinessCheck>();
        checks.AddRange(ScorecardChecks(measures, teeSets, runs, L));
        checks.AddRange(DriveChecks(measures, L));
        checks.AddRange(ScaleChecks(measures, medGreen, medWidth, L));
        checks.AddRange(TerrainChecks(measures, plotRange, L));
        checks.AddRange(CorridorChecks(holes, measures, outlines, L));
        checks.AddRange(SettingChecks(course, stimp, L));

        return new ReadinessReport
        {
            CourseName = course.Name, Limits = L, Holes = measures, TeeSets = teeSets, Runs = runs, Checks = checks,
            Atmosphere = course.Atmosphere, Stimp = stimp, MedianGreenSquareFeet = medGreen, MedianLandingYards = medWidth,
            PlotRangeFeet = plotRange,
        };
    }

    static Vec2 BackTee(Hole h) => h.Tees.Count > 0 ? h.Tees[^1] : h.Route[0];

    static int Yards(Hole h) => h.Tees.Count > 0 ? (int)Math.Round(h.TeeSetLength(h.Tees.Count - 1) * YardsPerMetre) : 0;

    static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return 0;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    static bool SameGreen(GreenOutline a, GreenOutline b) =>
        !a.Estimated && !b.Estimated && Vec2.Distance(Centroid(a.Points), Centroid(b.Points)) < 1;

    static Vec2 Centroid(IReadOnlyList<Vec2> p) => new(p.Average(q => q.X), p.Average(q => q.Z));

    /// <summary>Closest the polyline comes to a green (m): 0 when it crosses it. Sampled every metre.</summary>
    static double Clearance(IReadOnlyList<Vec2> line, IReadOnlyList<Vec2> green)
    {
        double best = double.MaxValue;
        var c = Centroid(green);
        double reach = green.Max(p => Vec2.Distance(p, c));
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double len = Vec2.Distance(line[i], line[i + 1]);
            int steps = Math.Max(1, (int)Math.Ceiling(len));
            for (int k = 0; k <= steps; k++)
            {
                var q = line[i] + (line[i + 1] - line[i]) * (k / (double)steps);
                if (Vec2.Distance(q, c) - reach > best) continue;
                double d = Geometry.Contains(green, q) ? 0 : Geometry.DistanceToEdge(green, q);
                if (d < best) best = d;
                if (best == 0) return 0;
            }
        }
        return best;
    }

    /// <summary>
    /// Fairway width across the line through the landing zone: the 40 yds up to where the tee shot lands (the test
    /// drive, or the first aim point if that's shorter, or 260 yds with no drive entered), measured every 5 yds
    /// and the median taken, since one cross-section can sit on a neck or a fairway's edge. At each step the fairway
    /// run under the line counts; where the line is in the rough the nearest run does, and if that's so at most
    /// steps the hole is marked as landing off the fairway.
    /// </summary>
    static (double? Yards, bool Off) LandingWidth(Hole h, Vec2 tee, List<List<Vec2>> fairways, ReadinessLimits L)
    {
        if (fairways.Count == 0) return (null, false);
        bool hasAim = h.Route.Count > 2;
        var aim = hasAim ? h.Route[1] : h.Route[^1];
        double toAim = Vec2.Distance(tee, aim);
        double land = Math.Min((L.TestDriveYards ?? 260) / YardsPerMetre, hasAim ? toAim : toAim - 30);
        if (land <= 0) return (null, false);
        var dir = (aim - tee).Normalized();
        var across = new Vec2(-dir.Z, dir.X);

        var widths = new List<double>();
        int offSteps = 0;
        for (double back = 0; back <= 40 / YardsPerMetre + 1e-9; back += 5 / YardsPerMetre)
        {
            if (land - back <= 0) break;
            var (w, off) = CrossSection(tee + dir * (land - back), across, fairways);
            widths.Add(w);
            if (off) offSteps++;
        }
        if (widths.Count == 0) return (null, false);
        return (Median(widths) * YardsPerMetre, offSteps * 2 > widths.Count);
    }

    static (double Metres, bool Off) CrossSection(Vec2 p, Vec2 across, List<List<Vec2>> fairways)
    {
        const double step = 0.5, reach = 90;
        var runs = new List<(double From, double To)>();
        double? start = null;
        for (double s = -reach; s <= reach + 1e-9; s += step)
        {
            bool inside = fairways.Any(f => Geometry.Contains(f, p + across * s));
            if (inside && start is null) start = s;
            if (!inside && start is double a) { runs.Add((a, s)); start = null; }
        }
        if (start is double open) runs.Add((open, reach));
        if (runs.Count == 0) return (0, true);
        var under = runs.FirstOrDefault(r => r.From <= 0 && r.To >= 0);
        bool off = under == default;
        if (off) under = runs.MinBy(r => Math.Min(Math.Abs(r.From), Math.Abs(r.To)));
        return (under.To - under.From, off);
    }

    /// <summary>Pins by thirds of the green along the approach (front, centre, back) and across it (left, centre, right).</summary>
    static IReadOnlyList<PinPlacement> Pins(Hole h, GreenOutline outline)
    {
        if (h.Pins.Count == 0 || outline.Points.Count < 3) return [];
        var f = GreenOutline.Approach(h);
        var right = new Vec2(f.Z, -f.X);   // compass north is −Z and east −X, so facing −Z the right hand is −X
        double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
        double f0 = outline.Points.Min(q => Dot(q, f)), f1 = outline.Points.Max(q => Dot(q, f));
        double r0 = outline.Points.Min(q => Dot(q, right)), r1 = outline.Points.Max(q => Dot(q, right));
        char Third(double t, char low, char high) => t < 1 / 3.0 ? low : t > 2 / 3.0 ? high : 'C';
        return h.Pins.Select((pin, i) => new PinPlacement(i + 1,
            Third((Dot(pin, f) - f0) / Math.Max(1e-6, f1 - f0), 'F', 'B'),
            Third((Dot(pin, right) - r0) / Math.Max(1e-6, r1 - r0), 'L', 'R'))).ToList();
    }

    static List<TeeSetTotal> TeeSets(List<Hole> holes)
    {
        int sets = holes.Count == 0 ? 0 : holes.Max(h => h.Tees.Count);
        var result = new List<TeeSetTotal>();
        for (int t = 0; t < sets; t++)
        {
            int Sum(IEnumerable<Hole> hs) => (int)Math.Round(hs.Where(h => h.Tees.Count > t).Sum(h => h.TeeSetLength(t) * YardsPerMetre));
            result.Add(new TeeSetTotal(t + 1, Sum(holes.Where(h => h.Number <= 9)), Sum(holes.Where(h => h.Number > 9))));
        }
        return result;
    }

    /// <summary>Section 7's table: the same par on consecutive holes, judged by run length, yardage spread and place.</summary>
    public static List<SameParRun> Runs(IReadOnlyList<HoleMeasures> holes, ReadinessLimits? limits = null)
    {
        var m = holes.OrderBy(h => h.Number).ToList();
        var L = limits ?? new ReadinessLimits();
        var runs = new List<SameParRun>();
        for (int i = 0; i < m.Count;)
        {
            int j = i;
            while (j + 1 < m.Count && m[j + 1].Par == m[i].Par) j++;
            if (j > i)
            {
                var r = m.GetRange(i, j - i + 1);
                var yards = r.Select(x => x.Yards).ToList();
                int spread = yards.Max() - yards.Min();
                (string, Severity) judged = r.Count >= 3
                    ? spread < L.RunNarrowYards && r[0].Number == 1
                        ? ("Highest priority: a narrow run at the start of the round. Address at the routing stage.", Severity.High)
                        : ("High risk: a routing-level flag. Revisit before building.", Severity.High)
                    : spread >= L.RunWideYards ? ("Low risk: length alone likely creates enough variety.", Severity.Pass)
                    : spread < L.RunNarrowYards ? ("Narrow: check the strategic concepts are genuinely distinct.", Severity.Flag)
                    : ("Moderate: make sure each hole has a distinct mechanism, not only a different length.", Severity.Flag);
                runs.Add(new SameParRun(r[0].Number, r[^1].Number, r[0].Par, yards, judged.Item1, judged.Item2));
            }
            i = j + 1;
        }
        return runs;
    }

    static IEnumerable<ReadinessCheck> ScorecardChecks(List<HoleMeasures> m, List<TeeSetTotal> sets, List<SameParRun> runs, ReadinessLimits L)
    {
        int par = m.Sum(x => x.Par), outPar = m.Where(x => x.Number <= 9).Sum(x => x.Par), inPar = par - outPar;
        yield return new("par", "Par and hole count", m.Count == 18 ? Severity.Pass : Severity.Flag,
            $"{m.Count} holes, par {par}: {outPar} out, {inPar} in.", "Publish gate · par check", []);

        var outside = sets.Where(s => s.Total < L.TeeTotalMinYards || s.Total > L.TeeTotalMaxYards).ToList();
        string all = string.Join(" · ", sets.Select(s => N0(s.Total)));
        yield return new("tee-totals", $"Every tee set {N0(L.TeeTotalMinYards)} to {N0(L.TeeTotalMaxYards)} yds",
            outside.Count == 0 ? Severity.Pass : Severity.Flag,
            outside.Count == 0 ? $"Tee sets, front to back: {all}."
                : $"Outside: {string.Join(", ", outside.Select(s => $"set {s.Index} ({N0(s.Total)})"))}. All sets: {all}.",
            "Principles §2 · tee and yardage range", []);

        var worst = runs.Count == 0 ? Severity.Pass : runs.Max(r => r.Severity);
        yield return new("same-par", "Same-par runs (Section 7)", worst,
            runs.Count == 0 ? "No two consecutive holes share a par."
                : string.Join(" ", runs.Select(r => $"Holes {r.FirstHole}–{r.LastHole}, par {r.Par}, spread {r.Spread} yds: {r.Finding}")),
            "Principles §7 · bar chart check", runs.Where(r => r.Severity != Severity.Pass).SelectMany(r => Enumerable.Range(r.FirstHole, r.LastHole - r.FirstHole + 1)).ToList());
    }

    static IEnumerable<ReadinessCheck> DriveChecks(List<HoleMeasures> m, ReadinessLimits L)
    {
        if (L.TestDriveYards is not double drive)
        {
            yield return new("drive", "Drivable par 4s and the par-5 test", Severity.Confirm,
                "Enter the test drive measured with the power profile to run these checks.", "Principles §6 · pre-build checklist", []);
            yield break;
        }
        foreach (var (id, name, nine) in new[] { ("drivable-front", "front", m.Where(x => x.Number <= 9)), ("drivable-back", "back", m.Where(x => x.Number > 9)) })
        {
            var d = nine.Where(x => x.DriveClass is not null).ToList();
            string list = d.Count == 0 ? "none" : string.Join(", ", d.Select(x => $"{x.Number} ({x.Yards}{(x.DriveClass == "Near-drivable" ? ", near" : "")})"));
            yield return new(id, $"Drivable par 4s on the {name} nine: {d.Count} (limit {L.MaxDrivablePerNine})",
                d.Count > L.MaxDrivablePerNine ? Severity.High : Severity.Pass,
                $"Within {L.NearDrivableWithinYards:0} yds of the {drive:0}-yd test drive: {list}.",
                "Principles §11.3 A · repeat hole types", d.Select(x => x.Number).ToList());
        }
        var short5 = m.Where(x => x.LeftAfterDrive < L.Par5MinLeftYards).ToList();
        yield return new("par5", "Par 5s leave a real second-shot decision", short5.Count > 0 ? Severity.High : Severity.Pass,
            short5.Count > 0
                ? Capital($"{string.Join(", ", short5.Select(x => $"hole {x.Number} leaves {x.LeftAfterDrive} yds"))} after the {drive:0}-yd drive (limit {L.Par5MinLeftYards:0}).")
                : $"Every par 5 leaves at least {L.Par5MinLeftYards:0} yds after the {drive:0}-yd drive.",
            "Principles §11.3 A · par-5 test", short5.Select(x => x.Number).ToList());
    }

    static IEnumerable<ReadinessCheck> ScaleChecks(List<HoleMeasures> m, double medGreen, double medWidth, ReadinessLimits L)
    {
        string band = $"{L.ScaleLow:0.0#}–{L.ScaleHigh:0.0#}×";
        var built = m.Where(x => x.GreenRatio is not null).ToList();
        if (built.Count < 2)
            yield return new("green-scale", "Green sizes against the median", Severity.Confirm,
                $"Only {built.Count} green drawn; sizes are compared once the greens are built.", "Principles §11.3 B · scale", []);
        else
        {
            var small = built.Where(x => x.GreenRatio < L.ScaleLow).ToList();
            var large = built.Where(x => x.GreenRatio > L.ScaleHigh).ToList();
            string Fmt(List<HoleMeasures> xs) => string.Join(", ", xs.Select(x => $"{x.Number} ({x.GreenRatio:0.00}×)"));
            yield return new("green-scale", $"Green sizes within {band} the median ({N0(medGreen)} sq ft)",
                small.Count + large.Count > 0 ? Severity.Flag : Severity.Pass,
                small.Count + large.Count == 0 ? $"All {built.Count} drawn greens inside the band."
                    : string.Join(" ", new[] { small.Count > 0 ? $"Small: {Fmt(small)}." : "", large.Count > 0 ? $"Large: {Fmt(large)}." : "" }.Where(s => s.Length > 0)),
                "Principles §11.3 B · scale", small.Concat(large).Select(x => x.Number).OrderBy(n => n).ToList());
        }

        var widths = m.Where(x => x.WidthRatio is not null).ToList();
        var narrow = widths.Where(x => x.WidthRatio < L.ScaleLow).ToList();
        var wide = widths.Where(x => x.WidthRatio > L.ScaleHigh).ToList();
        var offFairway = m.Where(x => x.LandingOffFairway).ToList();
        if (widths.Count < 2)
            yield return new("landing-width", "Landing widths against the median", Severity.Confirm,
                "Fairways aren't drawn on enough holes to compare widths.", "Principles §11.3 B · scale", []);
        else
        {
            string Fmt(List<HoleMeasures> xs) => string.Join(", ", xs.Select(x => $"{x.Number} ({x.LandingWidthYards:0} yds)"));
            var parts = new[]
            {
                narrow.Count > 0 ? $"Narrow: {Fmt(narrow)}." : "", wide.Count > 0 ? $"Wide: {Fmt(wide)}." : "",
                offFairway.Count > 0 ? $"The tee shot lands off the fairway on {List(offFairway.Select(x => x.Number))} (nearest fairway measured)." : "",
            }.Where(s => s.Length > 0).ToList();
            yield return new("landing-width", $"Landing widths within {band} the median ({medWidth:0} yds)",
                parts.Count > 0 ? Severity.Flag : Severity.Pass,
                parts.Count > 0 ? string.Join(" ", parts) : $"All {widths.Count} landing widths inside the band.",
                "Principles §11.3 B · scale", narrow.Concat(wide).Concat(offFairway).Select(x => x.Number).Distinct().OrderBy(n => n).ToList());
        }
    }

    static IEnumerable<ReadinessCheck> TerrainChecks(List<HoleMeasures> m, double plotRange, ReadinessLimits L)
    {
        var drops = m.Where(x => -x.ChangeFeet > L.MaxDropFeet).ToList();
        var climbs = m.Where(x => x.ChangeFeet > L.MaxClimbFeet).ToList();
        var biggestDrop = m.MinBy(x => x.ChangeFeet);
        var parts = new List<string>();
        if (climbs.Count > 0) parts.Add($"Climbs: {string.Join(", ", climbs.Select(x => $"{x.Number} (+{x.ChangeFeet:0} ft)"))}.");
        if (drops.Count > 0) parts.Add($"Drops: {string.Join(", ", drops.Select(x => $"{x.Number} ({x.ChangeFeet:0} ft)"))}.");
        yield return new("elevation", $"Tee to green within {L.MaxDropFeet:0} ft down and {L.MaxClimbFeet:0} ft up",
            parts.Count > 0 ? Severity.Flag : Severity.Pass,
            (parts.Count > 0 ? string.Join(" ", parts) + " " : "") +
                (biggestDrop is not null ? $"Largest drop: hole {biggestDrop.Number}, {-biggestDrop.ChangeFeet:0} ft. " : "") + "Terrain model; confirm in game.",
            "Principles §3 · elevation change", climbs.Concat(drops).Select(x => x.Number).OrderBy(n => n).ToList());

        yield return new("plot-range", $"Ground across play within {L.PlotRangeMinFeet:0}–{L.PlotRangeMaxFeet:0} ft",
            plotRange < L.PlotRangeMinFeet || plotRange > L.PlotRangeMaxFeet ? Severity.Flag : Severity.Pass,
            $"Tees, aim points and greens span {plotRange:0} ft. Terrain model; confirm in game.", "Principles §3 · plot elevation", []);

        var greens = m.Where(x => x.GreenReliefFeet is not null).ToList();
        if (greens.Count == 0)
        {
            yield return new("green-relief", "Greens not flat", Severity.Confirm, "No drawn greens to measure yet.", "Principles §3 · green contouring", []);
            yield break;
        }
        // Judged as printed (0.1 ft), so a page never shows "3.0" as over 3.
        var flat = greens.Where(x => Math.Round(x.GreenReliefFeet!.Value, 1) < L.GreenReliefMinFeet).ToList();
        var steep = greens.Where(x => Math.Round(x.GreenReliefFeet!.Value, 1) > L.GreenReliefMaxFeet).ToList();
        string Fmt(List<HoleMeasures> xs) => string.Join(", ", xs.Select(x => $"{x.Number} ({x.GreenReliefFeet:0.0})"));
        var gp = new[] { flat.Count > 0 ? $"Flat: {Fmt(flat)}." : "", steep.Count > 0 ? $"Over {L.GreenReliefMaxFeet:0.#} ft: {Fmt(steep)}." : "" }
            .Where(s => s.Length > 0).ToList();
        yield return new("green-relief", $"Green relief {L.GreenReliefMinFeet:0.0}–{L.GreenReliefMaxFeet:0.#} ft",
            gp.Count > 0 ? Severity.Flag : Severity.Pass,
            (gp.Count > 0 ? string.Join(" ", gp) : $"All {greens.Count} drawn greens inside the band.") + " Highest minus lowest point on each green, terrain model; confirm in game.",
            "Principles §3 · green internal contouring", flat.Concat(steep).Select(x => x.Number).OrderBy(n => n).ToList());
    }

    static IEnumerable<ReadinessCheck> CorridorChecks(List<Hole> holes, List<HoleMeasures> m, Dictionary<int, GreenOutline> outlines, ReadinessLimits L)
    {
        string Whose(int? of) => of == 0 ? "a practice green" : $"hole {of}'s green";
        var crossing = m.Where(x => x.NearGreenYards is double y && y < 0.5).ToList();
        var close = m.Where(x => x.NearGreenYards is double y && y >= 0.5 && Math.Round(y) < L.CorridorYards).ToList();
        var parts = new List<string>();
        if (crossing.Count > 0) parts.Add(string.Join(" ", crossing.Select(x => $"Hole {x.Number}'s line crosses {Whose(x.NearGreenOf)}.")));
        if (close.Count > 0) parts.Add(string.Join(" ", close.Select(x => $"Hole {x.Number}'s line passes {x.NearGreenYards:0} yds from {Whose(x.NearGreenOf)}.")));
        yield return new("corridor", $"No line of play within {L.CorridorYards:0} yds of another green",
            crossing.Count > 0 ? Severity.High : close.Count > 0 ? Severity.Flag : Severity.Pass,
            parts.Count > 0 ? string.Join(" ", parts) : "Every line of play clears the other greens.",
            "Principles §11.3 A · corridor check", crossing.Concat(close).Select(x => x.Number).OrderBy(n => n).ToList());

        var aims = holes.SelectMany(h => h.Route.Skip(1).Take(Math.Max(0, h.Route.Count - 2)).Select(a => (h.Number, P: a))).ToList();
        var pairs = new List<(int A, int B, double Yards)>();
        for (int i = 0; i < aims.Count; i++)
            for (int j = i + 1; j < aims.Count; j++)
                if (aims[i].Number != aims[j].Number && Vec2.Distance(aims[i].P, aims[j].P) * YardsPerMetre < L.AimSpacingYards)
                    pairs.Add((aims[i].Number, aims[j].Number, Vec2.Distance(aims[i].P, aims[j].P) * YardsPerMetre));
        yield return new("aim-spacing", $"No two holes' aim points within {L.AimSpacingYards:0} yds", pairs.Count > 0 ? Severity.Flag : Severity.Pass,
            pairs.Count > 0 ? string.Join(" ", pairs.Select(p => $"Holes {p.A} and {p.B}: {p.Yards:0} yds apart.")) : "Every landing zone is its own.",
            "Principles §11.3 A · shared landing zones", pairs.SelectMany(p => new[] { p.A, p.B }).Distinct().OrderBy(n => n).ToList());

        var undrawn = m.Where(x => !x.EndsOnOwnGreen).ToList();
        var shared = new List<(int A, int B)>();
        foreach (var a in holes)
            foreach (var b in holes.Where(b => b.Number > a.Number))
                if (SameGreen(outlines[a.Number], outlines[b.Number])) shared.Add((a.Number, b.Number));
        var sp = new List<string>();
        if (shared.Count > 0) sp.Add(string.Join(" ", shared.Select(s => $"Holes {s.A} and {s.B} end on the same green (fine if it's a double green).")));
        if (undrawn.Count > 0) sp.Add($"No green drawn yet on {List(undrawn.Select(x => x.Number))}.");
        yield return new("own-green", "Every route ends on its own drawn green",
            shared.Count > 0 ? Severity.Flag : undrawn.Count > 0 ? Severity.Confirm : Severity.Pass,
            sp.Count > 0 ? string.Join(" ", sp) : "Each hole's route ends on a drawn green of its own.",
            "Principles §11.3 F · aimed at the wrong green", shared.SelectMany(s => new[] { s.A, s.B }).Concat(undrawn.Select(x => x.Number)).Distinct().OrderBy(n => n).ToList());
    }

    static IEnumerable<ReadinessCheck> SettingChecks(CourseModel course, double? stimp, ReadinessLimits L)
    {
        yield return stimp is double s
            ? new("green-speed", $"Green speed Stimp {L.StimpMin:0}–{L.StimpMax:0}", s < L.StimpMin || s > L.StimpMax ? Severity.Flag : Severity.Pass,
                $"Setting {course.GreenSpeed:0.###} reads about Stimp {s:0.0} in game (scale from two in-game readings).", "Build checks · green speeds", [])
            : new("green-speed", $"Green speed Stimp {L.StimpMin:0}–{L.StimpMax:0}", Severity.Confirm,
                "The green speed is left at the game's default; check its Stimp reading in game.", "Build checks · green speeds", []);

        double f = course.GreenFringeRadius;
        yield return f < 0
            ? new("fringe", "No green fringe", Severity.Confirm, "The fringe is left at the game's default; check it in game.", "Principles §5 · no green fringes", [])
            : f <= NoFringeRadius + 1e-6
                ? new("fringe", "No green fringe", Severity.Pass, $"Fringe setting {f:0.##}: no visible fringe.", "Principles §5 · no green fringes", [])
                : new("fringe", "No green fringe", Severity.Flag, $"Fringe setting {f:0.##} is above {NoFringeRadius:0.0}, where no fringe shows.", "Principles §5 · no green fringes", []);

        if (course.Atmosphere is { } a)
        {
            string Cloud(CloudLayer? c) => c is null ? "none" : $"{c.Type} {c.Cloudiness:0.##}";
            yield return new("lighting", "Lighting, cloud and fog recorded", Severity.Info,
                $"Time {a.Clock}, sun {a.SunOrientation:0.#}° at {a.SunInclination:0.#}°, clouds {Cloud(a.Cloud1)} / {Cloud(a.Cloud2)}, " +
                $"fog ceiling {a.FogCeiling:0.##} density {a.FogDensity:0.##}{(a.Haze > 0 ? $", haze {a.Haze:0.##}" : "")}{(a.Rain ? ", rain" : "")}.",
                "Lighting section · match the published course", []);
        }
    }
}
