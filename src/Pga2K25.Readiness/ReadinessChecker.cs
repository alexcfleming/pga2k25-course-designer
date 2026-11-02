using System.Globalization;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;

namespace Pga2K25.Readiness;

/// <summary>
/// The publish-readiness checks a course file can answer by itself: scorecard and routing (§7), the review measures
/// (§11: drive tests, scale, corridors), elevation and green relief (§3), the green speed, fringe and lighting
/// settings, the TGC Tours minimums a file can show (par, yardage range, pins, fairway width) and a few provisional
/// warnings (fairway start, short par 4s, width steadiness, small greens). Judgement calls (waypoints, sand,
/// realism, cameras) stay with the designer. Terrain figures come from the replayed brushes, so they say "confirm in game".
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

    /// <summary>The fairway strip counts as the line's when it's under the line or within this many yards of it.</summary>
    const double FairwayNearLineYards = 15;

    /// <summary>Slope samples (the greens book's, every 3.5 m) this close to a pin count as its grid square.</summary>
    const double PinSquareMetres = 2.5;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string N0(double v) => v.ToString("N0", Inv);
    static string List(IEnumerable<int> holes) => string.Join(", ", holes);
    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <param name="cellMetres">Terrain cell for the elevation survey; the greens use the greens book's own.</param>
    /// <param name="overrides">The designer's own fairway starts and shared fairways, by hole.</param>
    public static ReadinessReport Run(CourseModel course, ReadinessLimits? limits = null, double cellMetres = 2,
        IReadOnlyDictionary<int, HoleOverride>? overrides = null)
    {
        var L = limits ?? new ReadinessLimits();
        overrides ??= new Dictionary<int, HoleOverride>();
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
            var (start, narrowest, steadiness) = h.Par >= 4 ? FairwayLine(h, tee, fairways, L) : (null, null, null);
            var own = overrides.TryGetValue(h.Number, out var given) ? given : null;

            double? relief = null, slope = null;
            GreenAnalysis? analysis = null;
            if (!outline.Estimated)
                try
                {
                    analysis = GreenAnalyzer.Analyze(course, h, greensOptions);
                    relief = (analysis.HighInches - analysis.LowInches) / 12;
                    slope = analysis.MedianSlopePct;
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
                FairwayStartFileYards = start, FairwayStartOverrideYards = h.Par >= 4 ? own?.FairwayStartYards : null,
                SharedFairway = own?.SharedFairway ?? false, NarrowestFairwayYards = narrowest, WidthSteadiness = steadiness,
                ChangeFeet = routing?.ChangeFeet ?? 0,
                GreenReliefFeet = relief, GreenMedianSlopePct = slope,
                NearGreenOf = nearOf, NearGreenYards = nearYards,
                EndsOnOwnGreen = !outline.Estimated,
                Pins = Pins(h, outline, analysis),
            });
        }

        double medGreen = sizes.MedianSquareFeet;
        // A shared fairway's widths belong to more than one hole, so they stay out of the medians.
        double medWidth = Median(measures.Where(m => m.LandingWidthYards > 0 && !m.SharedFairway).Select(m => m.LandingWidthYards!.Value));
        double medStart = Median(measures.Where(m => m.FairwayStartYards is not null && !m.SharedFairway).Select(m => m.FairwayStartYards!.Value));
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
        checks.AddRange(MinimumChecks(measures, L));
        checks.AddRange(ProvisionalChecks(measures, medStart, L));

        return new ReadinessReport
        {
            CourseName = course.Name, Limits = L, Holes = measures, TeeSets = teeSets, Runs = runs, Checks = checks,
            Atmosphere = course.Atmosphere, Stimp = stimp, MedianGreenSquareFeet = medGreen, MedianLandingYards = medWidth,
            PlotRangeFeet = plotRange, MedianFairwayStartYards = medStart,
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
        var (w, gap) = Strip(p, across, fairways);
        return (w, gap > 0 || w == 0);
    }

    /// <summary>
    /// The fairway run across the line at <paramref name="p"/>: the one under it, else the nearest, as its width and
    /// how far its edge is from the line (0 when under it). No fairway within 90 m is (0, ∞).
    /// </summary>
    static (double Metres, double Gap) Strip(Vec2 p, Vec2 across, List<List<Vec2>> fairways)
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
        if (runs.Count == 0) return (0, double.PositiveInfinity);
        static double Gap((double From, double To) r) => r.From <= 0 && r.To >= 0 ? 0 : Math.Min(Math.Abs(r.From), Math.Abs(r.To));
        var near = runs.MinBy(Gap);
        return (near.To - near.From, Gap(near));
    }

    /// <summary>The point and heading <paramref name="metres"/> along a polyline (clamped to its last leg).</summary>
    static (Vec2 Point, Vec2 Dir) Along(IReadOnlyList<Vec2> line, double metres)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double len = Vec2.Distance(line[i], line[i + 1]);
            var dir = (line[i + 1] - line[i]).Normalized();
            if (metres <= len || i + 2 == line.Count) return (line[i] + dir * metres, dir);
            metres -= len;
        }
        return (line[^1], new Vec2(0, 1));
    }

    /// <summary>
    /// The main fairway along the line of play (back tee, aim points, green), sampled every 5 yds from 60 yds out:
    /// <list type="bullet">
    /// <item>where it starts: the first station where the strip by the line is at least the start width and stays so
    /// for the hold distance (60 means 60 or less; null when it never does)</item>
    /// <item>the narrowest it gets on the line from there to 40 yds short of the green</item>
    /// <item>past 200 yds, its wide stretches over its narrow ones (90th over 10th percentile, so one neck or one
    /// gap between two fairways doesn't decide it)</item>
    /// </list>
    /// </summary>
    static (double? Start, double? Narrowest, double? Steadiness) FairwayLine(Hole h, Vec2 tee, List<List<Vec2>> fairways, ReadinessLimits L)
    {
        if (fairways.Count == 0 || h.Route.Count < 2) return (null, null, null);
        var line = new List<Vec2> { tee };
        line.AddRange(h.Route.Skip(1));
        double total = 0;
        for (int i = 0; i + 1 < line.Count; i++) total += Vec2.Distance(line[i], line[i + 1]);
        double totalYds = total * YardsPerMetre;
        const double step = 5, from = 60;
        int hold = Math.Max(1, (int)Math.Round(L.FairwayStartHoldYards / step));

        var stations = new List<(double Yds, double Width, double Gap)>();
        for (double yd = from; yd < totalYds - 20; yd += step)
        {
            var (p, dir) = Along(line, yd / YardsPerMetre);
            var (w, gap) = Strip(p, new Vec2(-dir.Z, dir.X), fairways);
            stations.Add((yd, w * YardsPerMetre, gap * YardsPerMetre));
        }

        double? start = null;
        for (int i = 0, run = 0; i < stations.Count; i++)
        {
            var st = stations[i];
            run = st.Width >= L.FairwayStartWidthYards && st.Gap <= FairwayNearLineYards ? run + 1 : 0;
            if (run >= hold) { start = stations[i - hold + 1].Yds; break; }
        }

        double end = totalYds - 40;
        var onLine = stations.Where(x => start is double s0 && x.Yds >= s0 && x.Yds <= end && x.Gap == 0).Select(x => x.Width).ToList();
        double? narrowest = onLine.Count > 0 ? onLine.Min() : null;

        var past = stations.Where(x => x.Yds >= 200 && x.Yds <= end && x.Gap <= FairwayNearLineYards && x.Width > 0)
            .Select(x => x.Width).OrderBy(x => x).ToList();
        double? steadiness = past.Count >= 5 ? Percentile(past, 0.9) / Percentile(past, 0.1) : null;
        return (start, narrowest, steadiness);
    }

    static double Percentile(List<double> sorted, double q)
    {
        double i = q * (sorted.Count - 1);
        int lo = (int)Math.Floor(i), hi = (int)Math.Ceiling(i);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (i - lo);
    }

    /// <summary>
    /// Pins by thirds of the green along the approach (front, centre, back) and across it (left, centre, right), with
    /// each one's distance from the green's edge and, from the greens book's slope samples, the steepest slope by it.
    /// </summary>
    static IReadOnlyList<PinPlacement> Pins(Hole h, GreenOutline outline, GreenAnalysis? analysis)
    {
        if (h.Pins.Count == 0 || outline.Points.Count < 3) return [];
        var f = GreenOutline.Approach(h);
        var right = new Vec2(f.Z, -f.X);   // compass north is −Z and east −X, so facing −Z the right hand is −X
        double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
        double f0 = outline.Points.Min(q => Dot(q, f)), f1 = outline.Points.Max(q => Dot(q, f));
        double r0 = outline.Points.Min(q => Dot(q, right)), r1 = outline.Points.Max(q => Dot(q, right));
        char Third(double t, char low, char high) => t < 1 / 3.0 ? low : t > 2 / 3.0 ? high : 'C';
        bool drawn = !outline.Estimated;
        return h.Pins.Select((pin, i) => new PinPlacement(i + 1,
            Third((Dot(pin, f) - f0) / Math.Max(1e-6, f1 - f0), 'F', 'B'),
            Third((Dot(pin, right) - r0) / Math.Max(1e-6, r1 - r0), 'L', 'R'))
        {
            EdgeYards = drawn ? (Geometry.Contains(outline.Points, pin) ? Geometry.DistanceToEdge(outline.Points, pin) * YardsPerMetre : 0) : null,
            SlopePct = analysis?.Arrows.Where(a => Vec2.Distance(a.Position, pin) <= PinSquareMetres).Select(a => (double?)a.SlopePct).Max(),
        }).ToList();
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
                // A design habit, not an approval rule: approved courses have four par 4s in a row.
                (string, Severity) judged = r.Count >= 3
                    ? spread < L.RunNarrowYards && r[0].Number == 1
                        ? ("A narrow run to open the round: make sure each hole plays its own way, ideally at the routing stage.", Severity.Flag)
                        : ("Three or more in a row: check each hole has its own strategy; revisit at the routing stage if not.", Severity.Flag)
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
            // Provisional: the band flags approved courses and clears unapproved ones, so it only warns.
            yield return new("green-scale", $"Green sizes within {band} the median ({N0(medGreen)} sq ft)",
                small.Count + large.Count > 0 ? Severity.Warning : Severity.Pass,
                small.Count + large.Count == 0 ? $"All {built.Count} drawn greens inside the band."
                    : string.Join(" ", new[] { small.Count > 0 ? $"Small: {Fmt(small)}." : "", large.Count > 0 ? $"Large: {Fmt(large)}." : "" }.Where(s => s.Length > 0)),
                "Provisional · Principles §11.3 B · scale", small.Concat(large).Select(x => x.Number).OrderBy(n => n).ToList());
        }

        var widths = m.Where(x => x.WidthRatio is not null && !x.SharedFairway).ToList();
        var narrow = widths.Where(x => x.WidthRatio < L.ScaleLow).ToList();
        var wide = widths.Where(x => x.WidthRatio > L.ScaleHigh).ToList();
        var offFairway = m.Where(x => x.LandingOffFairway && !x.SharedFairway).ToList();
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

    /// <summary>TGC Tours' published minimum approval requirements, where the course file can answer them.</summary>
    static IEnumerable<ReadinessCheck> MinimumChecks(List<HoleMeasures> m, ReadinessLimits L)
    {
        const string rule = "TGC minimums";
        int par = m.Sum(x => x.Par);
        var odd = m.Where(x => x.Par is < 3 or > 5).ToList();
        var parts = new List<string>();
        if (m.Count != 18) parts.Add($"{m.Count} holes, not 18.");
        if (par < L.ParMin || par > L.ParMax) parts.Add($"Par {par} is outside {L.ParMin}–{L.ParMax}.");
        if (odd.Count > 0) parts.Add($"Not a par 3, 4 or 5: {string.Join(", ", odd.Select(x => $"{x.Number} (par {x.Par})"))}.");
        string mix = string.Join(", ", new[] { 3, 4, 5 }.Select(p => $"{m.Count(x => x.Par == p)} par {p}s"));
        yield return new("par-range", $"Total par {L.ParMin}–{L.ParMax}, holes par 3, 4 or 5", parts.Count > 0 ? Severity.Flag : Severity.Pass,
            parts.Count > 0 ? string.Join(" ", parts) : $"Par {par} over {m.Count} holes: {mix}.", $"{rule} · general", odd.Select(x => x.Number).ToList());

        var groups = m.GroupBy(x => x.Par).Where(g => g.Count() >= 2).OrderBy(g => g.Key).ToList();
        var tight = groups.Where(g => g.Max(x => x.Yards) - g.Min(x => x.Yards) < L.SameParSpreadYards).ToList();
        string Span(IGrouping<int, HoleMeasures> g) => $"par {g.Key}s {g.Min(x => x.Yards)}–{g.Max(x => x.Yards)}";
        yield return new("same-par-range", "Holes of the same par have a range of yardages", tight.Count > 0 ? Severity.Flag : Severity.Pass,
            tight.Count > 0 ? Capital($"{string.Join(", ", tight.Select(Span))} yds: under {L.SameParSpreadYards:0} yds between shortest and longest.")
                : Capital($"{string.Join(", ", groups.Select(Span))} yds."),
            $"{rule} · general", tight.SelectMany(g => g.Select(x => x.Number)).OrderBy(n => n).ToList());

        var pins = m.SelectMany(x => x.Pins.Select(p => (Hole: x.Number, Pin: p))).ToList();
        if (pins.All(p => p.Pin.EdgeYards is null))
            yield return new("pins", "Pins clear of the edge and off steep slope", Severity.Confirm, "No pins on drawn greens to measure yet.", $"{rule} · pins", []);
        else
        {
            var edge = pins.Where(p => p.Pin.EdgeYards < L.PinEdgeYards).ToList();
            var steep = pins.Where(p => p.Pin.SlopePct >= L.PinSteepPct).ToList();
            var pp = new List<string>();
            if (edge.Count > 0) pp.Add($"Within {L.PinEdgeYards:0.#} yds of the edge: {string.Join(", ", edge.Select(p => $"{p.Hole}-{p.Pin.Number} ({p.Pin.EdgeYards:0.0})"))}.");
            if (steep.Count > 0) pp.Add($"{L.PinSteepPct:0.#}% or steeper by the cup: {string.Join(", ", steep.Select(p => $"{p.Hole}-{p.Pin.Number} ({p.Pin.SlopePct:0.0}%)"))}.");
            yield return new("pins", "Pins clear of the edge and off steep slope", pp.Count > 0 ? Severity.Flag : Severity.Pass,
                (pp.Count > 0 ? string.Join(" ", pp) : $"All {pins.Count} pins at least {L.PinEdgeYards:0.#} yds in and under {L.PinSteepPct:0.#}%.") +
                    " The edge limit stands in for three grid squares; slope is from the terrain model: confirm in game.",
                $"{rule} · pins", edge.Concat(steep).Select(p => p.Hole).Distinct().OrderBy(n => n).ToList());
        }

        var measured = m.Where(x => x.NarrowestFairwayYards is not null && !x.SharedFairway).ToList();
        var narrow = measured.Where(x => x.NarrowestFairwayYards < L.FairwayMinWidthYards).ToList();
        yield return new("narrow-fairway", $"Fairways at least {L.FairwayMinWidthYards:0} yds wide (recommended)", Severity.Info,
            measured.Count == 0 ? "No fairways along the lines of play to measure yet."
                : narrow.Count == 0 ? $"Every line's fairway stays {L.FairwayMinWidthYards:0} yds or wider until 40 yds short of the green."
                : $"Narrower than {L.FairwayMinWidthYards:0} yds before the green's approach on {string.Join(", ", narrow.Select(x => $"{x.Number} ({x.NarrowestFairwayYards:0})"))}. A note only: TGC recommends it.",
            $"{rule} · playability", narrow.Select(x => x.Number).ToList());
    }

    /// <summary>
    /// Provisional checks from a small sample of reviewed courses: warnings only, never a pass or a fail. Every
    /// threshold is a limit the designer can change.
    /// </summary>
    static IEnumerable<ReadinessCheck> ProvisionalChecks(List<HoleMeasures> m, double medStart, ReadinessLimits L)
    {
        const string rule = "Provisional";
        string band = $"{L.FairwayStartMinYards:0}–{L.FairwayStartMaxYards:0}";
        var longHoles = m.Where(x => x.Par >= 4 && !x.SharedFairway).ToList();
        var started = longHoles.Where(x => x.FairwayStartYards is not null).ToList();
        var outside = started.Where(x => x.FairwayStartYards < L.FairwayStartMinYards || x.FairwayStartYards > L.FairwayStartMaxYards).ToList();
        var none = longHoles.Where(x => x.FairwayStartYards is null).ToList();
        string Yds(HoleMeasures x) => x.FairwayStartYards <= 60 ? "60 or less" : $"{x.FairwayStartYards:0}";
        var sp = new List<string>();
        if (started.Count > 0) sp.Add($"Median {medStart:0} yds; {started.Count - outside.Count} of {started.Count} par 4s and 5s start inside {band} yds.");
        if (outside.Count > 0) sp.Add($"Outside: {string.Join(", ", outside.Select(x => $"{x.Number} ({Yds(x)})"))}.");
        if (none.Count > 0) sp.Add($"No main fairway found along the line on {List(none.Select(x => x.Number))}.");
        int shared = m.Count(x => x.Par >= 4 && x.SharedFairway);
        if (shared > 0) sp.Add($"{shared} shared fairway{(shared == 1 ? "" : "s")} left out.");
        yield return new("fairway-start", $"Main fairway starts {band} yds from the back tee",
            outside.Count + none.Count > 0 ? Severity.Warning : Severity.Pass,
            sp.Count > 0 ? string.Join(" ", sp) : "No par 4s or 5s with fairways to measure yet.",
            $"{rule} · fairway start", outside.Concat(none).Select(x => x.Number).OrderBy(n => n).ToList());

        var shortFours = m.Where(x => x.Par == 4 && x.Yards <= L.ShortPar4Yards).ToList();
        yield return new("short-par4", $"Par 4s of {L.ShortPar4Yards:0} yds or less: at most {L.MaxShortPar4s}",
            shortFours.Count > L.MaxShortPar4s ? Severity.Warning : Severity.Pass,
            shortFours.Count == 0 ? "None." : $"{shortFours.Count}: {string.Join(", ", shortFours.Select(x => $"{x.Number} ({x.Yards})"))}.",
            $"{rule} · hole lengths", shortFours.Count > L.MaxShortPar4s ? shortFours.Select(x => x.Number).ToList() : []);

        var steady = m.Where(x => x.WidthSteadiness is not null && !x.SharedFairway).ToList();
        var uneven = steady.Where(x => x.WidthSteadiness > L.WidthSteadinessMax).ToList();
        yield return new("width-steadiness", $"Fairway width steady past 200 yds (within {L.WidthSteadinessMax:0.#}×)",
            uneven.Count > 0 ? Severity.Warning : Severity.Pass,
            uneven.Count > 0 ? $"Wide stretches over {L.WidthSteadinessMax:0.#}× the narrow ones on {string.Join(", ", uneven.Select(x => $"{x.Number} ({x.WidthSteadiness:0.0}×)"))}."
                : steady.Count > 0 ? $"All {steady.Count} measured fairways within {L.WidthSteadinessMax:0.#}×." : "No fairways past 200 yds to measure yet.",
            $"{rule} · fairway width", uneven.Select(x => x.Number).ToList());

        var drawn = m.Where(x => !x.GreenEstimated).ToList();
        var small = drawn.Where(x => x.GreenSquareFeet < L.SmallGreenSquareFeet).ToList();
        yield return new("small-greens", $"Greens of at least {N0(L.SmallGreenSquareFeet)} sq ft", small.Count > 0 ? Severity.Warning : Severity.Pass,
            small.Count > 0 ? $"Smaller: {string.Join(", ", small.Select(x => $"{x.Number} ({N0(x.GreenSquareFeet)})"))}."
                : drawn.Count > 0 ? $"All {drawn.Count} drawn greens are {N0(L.SmallGreenSquareFeet)} sq ft or more." : "No drawn greens yet.",
            $"{rule} · green size", small.Select(x => x.Number).ToList());

        var threes = drawn.Where(x => x.Par == 3).OrderBy(x => x.Yards).ToList();
        var shrink = threes.Zip(threes.Skip(1)).Where(p => p.Second.GreenSquareFeet < p.First.GreenSquareFeet).ToList();
        string all = string.Join(" · ", threes.Select(x => $"{x.Yards} yds {N0(x.GreenSquareFeet)}"));
        yield return new("par3-greens", "Par 3 greens grow with the hole's length", shrink.Count > 0 ? Severity.Warning : Severity.Pass,
            threes.Count < 2 ? "Fewer than two drawn par 3 greens."
                : shrink.Count > 0 ? $"Longer par 3s with smaller greens: {string.Join(", ", shrink.Select(p => $"{p.Second.Number} against {p.First.Number}"))}. By length: {all} sq ft."
                : $"By length: {all} sq ft.",
            $"{rule} · green size", shrink.Select(p => p.Second.Number).ToList());
    }
}
