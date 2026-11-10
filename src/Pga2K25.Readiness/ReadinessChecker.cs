using System.Globalization;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;

namespace Pga2K25.Readiness;

/// <summary>
/// The publish-readiness checks a course file can answer by itself: scorecard and routing (§7), the review measures
/// (§11: drive tests, scale, corridors), elevation and green relief (§3), the green speed, fringe and lighting
/// settings, the TGC Tours minimums a file can show (par, yardage range, pins) and the provisional §12 length and
/// width warnings, measured from a 300–350 yd design tee shot. Judgement calls (waypoints, sand,
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

    /// <summary>
    /// The measuring that doesn't depend on the limits or the designer's overrides: green outlines and sizes, fairways,
    /// the terrain survey, practice greens, hazards and the green analyses. It is most of the cost of a run, so a caller
    /// that runs the same course with different limits (the Readiness page, the tests) prepares once and passes it in.
    /// </summary>
    public sealed class Inputs
    {
        internal CourseModel Course { get; init; } = default!;
        internal double CellMetres { get; init; } = default!;
        internal List<Hole> Holes { get; init; } = default!;
        internal Dictionary<int, GreenOutline> Outlines { get; init; } = default!;
        internal GreenSizes Sizes { get; init; } = default!;
        internal List<List<Vec2>> Fairways { get; init; } = default!;
        internal TerrainSurvey Survey { get; init; } = default!;
        internal List<IReadOnlyList<Vec2>> Practice { get; init; } = default!;
        internal CourseLayers Layers { get; init; } = default!;
        internal List<Hazard> Hazards { get; init; } = default!;
        internal Dictionary<int, GreenAnalysis?> Analyses { get; init; } = default!;
    }

    /// <param name="cellMetres">Terrain cell for the elevation survey; the greens use the greens book's own.</param>
    public static Inputs Prepare(CourseModel course, double cellMetres = 2)
    {
        var holes = course.Holes.OrderBy(h => h.Number).ToList();
        var outlines = holes.ToDictionary(h => h.Number, h => GreenOutline.Resolve(course, h));
        var survey = TerrainSurvey.Build(course, new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = cellMetres } });
        var layers = CourseLayers.From(course);
        var greensOptions = new GreensBookOptions();
        var analyses = new Dictionary<int, GreenAnalysis?>();
        foreach (var h in holes)
        {
            GreenAnalysis? analysis = null;
            if (!outlines[h.Number].Estimated)
                try { analysis = GreenAnalyzer.Analyze(course, h, greensOptions); }
                catch (Exception) { /* a green the analysis can't read is left blank */ }
            analyses[h.Number] = analysis;
        }
        return new Inputs
        {
            Course = course, CellMetres = cellMetres, Holes = holes, Outlines = outlines, Sizes = GreenSizes.From(course),
            Fairways = course.SurfaceSplines.Where(s => s.Surface == Surfaces.Fairway && s.Closed)
                .Select(s => Geometry.SampleSpline(s)).Where(p => p.Count >= 3).ToList(),
            Survey = survey,
            Practice = CourseLayers.From(course).Greens.Where(g => g.Count >= 3)
                .Where(g => holes.All(h => (Geometry.Contains(g, h.GreenAnchor) ? 0 : Geometry.DistanceToEdge(g, h.GreenAnchor)) > PracticeGreenMetres))
                .Select(g => (IReadOnlyList<Vec2>)g).ToList(),
            Layers = layers, Hazards = Hazards(layers, survey), Analyses = analyses,
        };
    }

    /// <param name="cellMetres">Terrain cell for the elevation survey; the greens use the greens book's own.</param>
    /// <param name="overrides">The designer's own fairway starts and shared fairways, by hole.</param>
    /// <param name="inputs">The course already measured (<see cref="Prepare"/>), so only the limits are applied; prepared here when null.</param>
    public static ReadinessReport Run(CourseModel course, ReadinessLimits? limits = null, double cellMetres = 2,
        IReadOnlyDictionary<int, HoleOverride>? overrides = null, Inputs? inputs = null)
    {
        if (inputs is not null && (!ReferenceEquals(inputs.Course, course) || inputs.CellMetres != cellMetres))
            throw new ArgumentException("The prepared inputs are for another course or terrain cell.", nameof(inputs));
        inputs ??= Prepare(course, cellMetres);
        var L = limits ?? new ReadinessLimits();
        overrides ??= new Dictionary<int, HoleOverride>();
        var holes = inputs.Holes;
        var outlines = inputs.Outlines;
        var sizes = inputs.Sizes;
        var fairways = inputs.Fairways;
        var survey = inputs.Survey;
        var practice = inputs.Practice;

        var measures = new List<HoleMeasures>();
        var hazards = inputs.Hazards;
        foreach (var h in holes)
        {
            var outline = outlines[h.Number];
            var size = sizes.For(h.Number);
            var tee = BackTee(h);
            int yards = Yards(h);
            var own = overrides.TryGetValue(h.Number, out var given) ? given : null;
            var fig = h.Par >= 4 ? LineFigures(h, tee, fairways, hazards, L) : null;

            double? relief = null, slope = null;
            var analysis = inputs.Analyses[h.Number];
            if (analysis is not null)
            {
                relief = (analysis.HighInches - analysis.LowInches) / 12;
                slope = analysis.MedianSlopePct;
            }

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
                Number = h.Number, Par = h.Par, Yards = yards,
                StraightYards = (int)Math.Round(Vec2.Distance(tee, h.Route[^1]) * YardsPerMetre),
                GreenSquareFeet = size?.SquareFeet ?? 0, GreenEstimated = outline.Estimated,
                Stations = fig?.Stations ?? [], LayupWidthYards = fig?.Layup, LandingWidthYards = fig?.Landing,
                LandingHazards = fig?.Hazards ?? [], ForwardCarryYards = fig is null ? null : ForwardCarry(h, fig.StartPoint),
                FairwayStartFileYards = fig?.Start, FairwayStartOverrideYards = h.Par >= 4 ? own?.FairwayStartYards : null,
                SharedFairway = own?.SharedFairway ?? false, WidthSteadiness = fig?.Steadiness,
                ChangeFeet = routing?.ChangeFeet ?? 0,
                GreenReliefFeet = relief, GreenMedianSlopePct = slope,
                NearGreenOf = nearOf, NearGreenYards = nearYards,
                EndsOnOwnGreen = !outline.Estimated,
                Pins = Pins(h, outline, analysis),
            });
        }

        double medGreen = sizes.MedianSquareFeet;
        // A shared fairway's start belongs to more than one hole, so it stays out of the median. Its widths count.
        double medStart = Median(measures.Where(m => m.FairwayStartYards is not null && !m.SharedFairway).Select(m => m.FairwayStartYards!.Value));
        measures = measures.Select(m => m with
        {
            GreenRatio = !m.GreenEstimated && medGreen > 0 ? m.GreenSquareFeet / medGreen : null,
        }).ToList();

        var playPoints = holes.SelectMany(h => h.Tees.Concat(h.Route))
            .Concat(survey.Holes.Select(r => r.GreenSiteCentre)).Select(survey.Feet).ToList();
        double plotRange = playPoints.Count == 0 ? 0 : playPoints.Max() - playPoints.Min();

        var teeSets = TeeSets(holes);
        var runs = Runs(measures, L);
        double? stimp = Stimp(course.GreenSpeed);

        var checks = new List<ReadinessCheck>();
        checks.AddRange(ScorecardChecks(measures, runs));
        checks.AddRange(MinimumChecks(measures, L));
        checks.AddRange(LengthChecks(measures, teeSets, L));
        checks.AddRange(WidthChecks(measures, L));
        checks.AddRange(TerrainChecks(measures, plotRange, L));
        checks.AddRange(CorridorChecks(holes, measures, outlines, L));
        // Slope read across about 12 m, so the 2 m raster's single-cell steps don't count as cliffs.
        checks.AddRange(RoutingChecks.Run(holes, survey.Feet, p =>
        {
            const double d = 6;
            double gx = (survey.Feet(p + new Vec2(d, 0)) - survey.Feet(p - new Vec2(d, 0))) / (2 * d * FeetPerMetre);
            double gz = (survey.Feet(p + new Vec2(0, d)) - survey.Feet(p - new Vec2(0, d))) / (2 * d * FeetPerMetre);
            return Math.Sqrt(gx * gx + gz * gz) * 100;
        }, L));
        checks.AddRange(SettingChecks(course, stimp, L));
        checks.AddRange(PromptChecks(measures, medGreen, medStart, L));

        return new ReadinessReport
        {
            CourseName = course.Name, Limits = L, Holes = measures, TeeSets = teeSets, Runs = runs, Checks = checks,
            Atmosphere = course.Atmosphere, Stimp = stimp, MedianGreenSquareFeet = medGreen,
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
    /// The fairway run across the line at <paramref name="p"/>: the one under it, else the nearest, as its width and
    /// how far its edge is from the line (0 when under it). No fairway within 90 m is (0, ∞).
    /// </summary>
    /// <summary>Each fairway's bounding box (min X, max X, min Z, max Z), so a point can skip the polygons it can't be in.</summary>
    static (double X0, double X1, double Z0, double Z1)[] Boxes(List<List<Vec2>> fairways) =>
        fairways.Select(f => (f.Min(q => q.X), f.Max(q => q.X), f.Min(q => q.Z), f.Max(q => q.Z))).ToArray();

    static (double Metres, double Gap) Strip(Vec2 p, Vec2 across, List<List<Vec2>> fairways, (double X0, double X1, double Z0, double Z1)[] boxes)
    {
        const double step = 0.5, reach = 90;
        var runs = new List<(double From, double To)>();
        double? start = null;
        for (double s = -reach; s <= reach + 1e-9; s += step)
        {
            var q = p + across * s;
            bool inside = false;
            for (int i = 0; i < fairways.Count && !inside; i++)
            {
                var b = boxes[i];
                // Outside a fairway's box is outside the fairway: only the boxes it falls in need the polygon test.
                if (q.X < b.X0 || q.X > b.X1 || q.Z < b.Z0 || q.Z > b.Z1) continue;
                inside = Geometry.Contains(fairways[i], q);
            }
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

    /// <summary>What the line of play shows on a par 4 or 5 (see <see cref="LineFigures"/>).</summary>
    sealed record LineFigure(double? Start, Vec2? StartPoint, double? Steadiness, IReadOnlyList<StationWidth> Stations,
        double? Layup, double? Landing, IReadOnlyList<LandingHazard> Hazards);

    /// <summary>A bunker or water outline, with what to call it.</summary>
    internal sealed record Hazard(string Kind, IReadOnlyList<Vec2> Outline)
    {
        readonly double _minX = Outline.Count == 0 ? 0 : Outline.Min(p => p.X), _maxX = Outline.Count == 0 ? 0 : Outline.Max(p => p.X);
        readonly double _minZ = Outline.Count == 0 ? 0 : Outline.Min(p => p.Z), _maxZ = Outline.Count == 0 ? 0 : Outline.Max(p => p.Z);

        /// <summary>
        /// Distance (m) from a point to the outline's bounding box, 0 inside it. Never more than the distance to the
        /// outline itself, so a point further than this from the box is outside the hazard and at least as far from its edge.
        /// </summary>
        public double BoxDistance(Vec2 p)
        {
            double dx = Math.Max(Math.Max(_minX - p.X, 0), p.X - _maxX), dz = Math.Max(Math.Max(_minZ - p.Z, 0), p.Z - _maxZ);
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Every bunker (painted dabs merged as the scorecard maps merge them) and every water outline: painted water,
    /// stream ribbons (their edges) and the ponds the terrain survey traced.
    /// </summary>
    static List<Hazard> Hazards(CourseLayers layers, TerrainSurvey survey)
    {
        var list = layers.MergedBunkers().Where(b => b.Count >= 3).Select(b => new Hazard("bunker", b)).ToList();
        list.AddRange(layers.WaterAreas.Where(w => w.Count >= 3).Select(w => new Hazard("water", w)));
        list.AddRange(survey.PaintedWaterEdges.Where(w => w.Count >= 3).Select(w => new Hazard("water", w)));
        list.AddRange(layers.WaterRibbons.Where(r => r.Points.Count >= 2).Select(r => new Hazard("water", RibbonOutline(r.Points, r.Width / 2))));
        return list;
    }

    /// <summary>A stroke's outline: its points offset by half its width either side, out one side and back the other.</summary>
    static List<Vec2> RibbonOutline(IReadOnlyList<Vec2> pts, double half)
    {
        var left = new List<Vec2>();
        var right = new List<Vec2>();
        for (int i = 0; i < pts.Count; i++)
        {
            var d = (pts[Math.Min(i + 1, pts.Count - 1)] - pts[Math.Max(i - 1, 0)]).Normalized();
            var n = new Vec2(-d.Z, d.X) * half;
            left.Add(pts[i] + n);
            right.Add(pts[i] - n);
        }
        right.Reverse();
        return [.. left, .. right];
    }

    /// <summary>
    /// The line of play (back tee, aim points, green), sampled every 5 yds from 60 yds out:
    /// <list type="bullet">
    /// <item>where the main fairway starts: the first station where the strip by the line is at least the start width
    /// and stays so for the hold distance (60 means 60 or less; null when it never does)</item>
    /// <item>past 200 yds, its wide stretches over its narrow ones (90th over 10th percentile, so one neck or one
    /// gap between two fairways doesn't decide it)</item>
    /// <item>the width at the design tee shot's short, middle and long distances, and the median width over
    /// 250–290 yds (the lay-up) and over the design tee shot (the landing)</item>
    /// <item>bunkers and water within reach of the line between the landing-hazard distances</item>
    /// </list>
    /// </summary>
    static LineFigure? LineFigures(Hole h, Vec2 tee, List<List<Vec2>> fairways, List<Hazard> hazards, ReadinessLimits L)
    {
        if (h.Route.Count < 2) return null;
        var line = new List<Vec2> { tee };
        line.AddRange(h.Route.Skip(1));
        double total = 0;
        for (int i = 0; i + 1 < line.Count; i++) total += Vec2.Distance(line[i], line[i + 1]);
        double totalYds = total * YardsPerMetre;
        const double step = 5, from = 60;
        var boxes = Boxes(fairways);
        int hold = Math.Max(1, (int)Math.Round(L.FairwayStartHoldYards / step));
        double end = totalYds - 40;

        (double Width, double Gap) At(double yd)
        {
            var (p, dir) = Along(line, yd / YardsPerMetre);
            var (w, gap) = fairways.Count == 0 ? (0, double.PositiveInfinity) : Strip(p, new Vec2(-dir.Z, dir.X), fairways, boxes);
            return (w * YardsPerMetre, gap * YardsPerMetre);
        }

        var stations = new List<(double Yds, double Width, double Gap)>();
        for (double yd = from; yd < totalYds - 20; yd += step)
        {
            var (w, gap) = At(yd);
            stations.Add((yd, w, gap));
        }
        bool Near((double Yds, double Width, double Gap) x) => x.Gap <= FairwayNearLineYards && x.Width > 0;

        double? start = null;
        for (int i = 0, run = 0; i < stations.Count; i++)
        {
            var st = stations[i];
            run = st.Width >= L.FairwayStartWidthYards && st.Gap <= FairwayNearLineYards ? run + 1 : 0;
            if (run >= hold) { start = stations[i - hold + 1].Yds; break; }
        }
        Vec2? startPoint = start is double s0 ? Along(line, s0 / YardsPerMetre).Point : null;

        var past = stations.Where(x => x.Yds >= 200 && x.Yds <= end && Near(x)).Select(x => x.Width).OrderBy(x => x).ToList();
        double? steadiness = past.Count >= 5 ? Percentile(past, 0.9) / Percentile(past, 0.1) : null;

        // The design tee shot's three stations, where the hole is long enough to land short of the green.
        var marks = new[] { L.DesignDriveMinYards, L.DesignDriveMidYards, L.DesignDriveMaxYards }.Where(y => y <= end).ToList();
        var widths = marks.Select(y => { var (w, gap) = At(y); return new StationWidth((int)Math.Round(y), gap <= FairwayNearLineYards && w > 0 ? w : null); }).ToList();

        double? MedianOver(double a, double b)
        {
            var ws = stations.Where(x => x.Yds >= a && x.Yds <= b && x.Yds <= end).Select(x => Near(x) ? x.Width : 0).ToList();
            return ws.Count > 0 ? Median(ws) : null;
        }

        // Hazards within reach of the line through the landing zone: the nearest station each comes to.
        var near = new List<LandingHazard>();
        var zone = new List<(double Yds, Vec2 P, Vec2 Dir)>();
        for (double yd = L.LandingHazardFromYards; yd <= Math.Min(L.LandingHazardToYards, totalYds - 10); yd += step)
        {
            var (p, dir) = Along(line, yd / YardsPerMetre);
            zone.Add((yd, p, dir));
        }
        double reach = L.LandingHazardReachYards / YardsPerMetre;
        foreach (var hz in hazards)
        {
            (double D, double Yds, string Side)? best = null;
            foreach (var (yd, p, dir) in zone)
            {
                // Out of reach of the hazard's box means out of reach of the hazard: skip the polygon tests.
                if (hz.BoxDistance(p) > reach) continue;
                double d = Geometry.Contains(hz.Outline, p) ? 0 : Geometry.DistanceToEdge(hz.Outline, p);
                if (d > reach || (best is { } b && b.D <= d)) continue;
                var c = Centroid(hz.Outline) - p;
                // Facing along the line, right is (dir.Z, -dir.X): compass north is −Z and east −X.
                double side = c.X * dir.Z - c.Z * dir.X;
                best = (d, yd, d == 0 ? "on the line" : side > 0 ? "right" : "left");
            }
            if (best is { } hit) near.Add(new LandingHazard(hz.Kind, (int)Math.Round(hit.Yds), hit.D * YardsPerMetre, hit.Side));
        }

        return new LineFigure(start, startPoint, steadiness, widths,
            MedianOver(250, 290), MedianOver(L.DesignDriveMinYards, L.DesignDriveMaxYards), near.OrderBy(x => x.FromTeeYards).ToList());
    }

    /// <summary>The longest straight carry from a forward tee to where the main fairway starts (yds).</summary>
    static double? ForwardCarry(Hole h, Vec2? start) =>
        start is Vec2 s && h.Tees.Count > 1 ? h.Tees.Take(h.Tees.Count - 1).Max(t => Vec2.Distance(t, s)) * YardsPerMetre : null;

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

    static IEnumerable<ReadinessCheck> ScorecardChecks(List<HoleMeasures> m, List<SameParRun> runs)
    {
        int par = m.Sum(x => x.Par), outPar = m.Where(x => x.Number <= 9).Sum(x => x.Par), inPar = par - outPar;
        yield return new("par", "Par and hole count", m.Count == 18 ? Severity.Pass : Severity.Flag,
            $"{m.Count} holes, par {par}: {outPar} out, {inPar} in.", "Publish gate · par check", []);

        var worst = runs.Count == 0 ? Severity.Pass : runs.Max(r => r.Severity);
        yield return new("same-par", "Same-par runs (Section 7)", worst,
            runs.Count == 0 ? "No two consecutive holes share a par."
                : string.Join(" ", runs.Select(r => $"Holes {r.FirstHole}–{r.LastHole}, par {r.Par}, spread {r.Spread} yds: {r.Finding}")),
            "Principles §7 · a design habit, not an approval rule", runs.Where(r => r.Severity != Severity.Pass).SelectMany(r => Enumerable.Range(r.FirstHole, r.LastHole - r.FirstHole + 1)).ToList());
    }

    /// <summary>
    /// Principles §12.4, card yards from the back tee: the back-tee total as a range, then the median par 4, how many
    /// par 4s are under 400 and the shortest par 5. Provisional, so they warn.
    /// </summary>
    static IEnumerable<ReadinessCheck> LengthChecks(List<HoleMeasures> m, List<TeeSetTotal> sets, ReadinessLimits L)
    {
        const string rule = "Provisional · Principles §12.4 length";
        int back = sets.Count > 0 ? sets[^1].Total : m.Sum(x => x.Yards);
        string others = sets.Count > 1 ? $" Forward tees, shortest first: {string.Join(" · ", sets.SkipLast(1).Select(s => N0(s.Total)))} (no limit)." : "";
        yield return new("length-total", $"Back tee about {N0(L.BackTeeMinYards)}–{N0(L.BackTeeMaxYards)} yds", Severity.Info,
            $"Back tee {N0(back)} yds{(back < L.BackTeeMinYards || back > L.BackTeeMaxYards ? ", outside the range passing courses span" : "")}. A range seen on passing courses, not a score.{others}",
            "Principles §2 · tee and yardage range", []);

        var fours = m.Where(x => x.Par == 4).OrderBy(x => x.Yards).ToList();
        if (fours.Count > 0)
        {
            double med = Median(fours.Select(x => (double)x.Yards));
            yield return new("median-par4", $"Median par 4 at least {L.MedianPar4MinYards:0} yds", med < L.MedianPar4MinYards ? Severity.Warning : Severity.Pass,
                $"Median par 4 {med:0} yds over {fours.Count} par 4s ({fours[0].Yards}–{fours[^1].Yards}).", rule, []);

            var shortFours = fours.Where(x => x.Yards < L.ShortPar4Yards).ToList();
            yield return new("par4-under400", $"Par 4s under {L.ShortPar4Yards:0} yds: at most {L.MaxShortPar4s}",
                shortFours.Count > L.MaxShortPar4s ? Severity.Warning : Severity.Pass,
                shortFours.Count == 0 ? "None." : $"{shortFours.Count}: {string.Join(", ", shortFours.OrderBy(x => x.Number).Select(x => $"{x.Number} ({x.Yards})"))}.",
                rule, shortFours.Count > L.MaxShortPar4s ? shortFours.Select(x => x.Number).OrderBy(n => n).ToList() : []);
        }

        var fives = m.Where(x => x.Par == 5).OrderBy(x => x.Yards).ToList();
        if (fives.Count > 0)
        {
            var shortest = fives[0];
            var under = fives.Where(x => x.Yards < L.ShortestPar5MinYards).ToList();
            yield return new("shortest-par5", $"Shortest par 5 at least {L.ShortestPar5MinYards:0} yds", under.Count > 0 ? Severity.Warning : Severity.Pass,
                (under.Count > 0 ? $"Under {L.ShortestPar5MinYards:0}: {string.Join(", ", under.OrderBy(x => x.Number).Select(x => $"{x.Number} ({x.Yards})"))}. " : $"Shortest: hole {shortest.Number}, {shortest.Yards} yds. ") +
                    $"After a {L.DesignDriveMaxYards:0}-yd drive the par 5s leave {string.Join(", ", fives.OrderBy(x => x.Number).Select(x => $"{x.Number}: {x.LeftAfter(L.DesignDriveMaxYards)}"))} yds, straight from the back tee.",
                rule, under.Select(x => x.Number).OrderBy(n => n).ToList());
        }

        var bent = m.Where(x => x.StraightYards > 0 && Math.Abs(x.Yards - x.StraightYards) >= L.StraightDiffYards).ToList();
        if (bent.Count > 0)
            yield return new("card-straight", "Card yards and straight-line yards", Severity.Info,
                $"Lengths are judged on card yards; yards left use the straight line from the back tee. They differ by {L.StraightDiffYards:0}+ yds on " +
                $"{string.Join(", ", bent.Select(x => $"{x.Number} ({x.Yards} card, {x.StraightYards} straight)"))}.",
                "Principles §12 · design assumption", bent.Select(x => x.Number).ToList());
    }

    /// <summary>
    /// Principles §12.2, across the fairway at the design tee shot: warn above the high width or below the low one at
    /// any station (shared fairways count), note what needs a written reason, the swing past 200 yds, and whether the
    /// hole is roomier in the lay-up than in the landing zone.
    /// </summary>
    static IEnumerable<ReadinessCheck> WidthChecks(List<HoleMeasures> m, ReadinessLimits L)
    {
        const string rule = "Provisional · Principles §12.2 width";
        string band = $"{L.DesignDriveMinYards:0}–{L.DesignDriveMaxYards:0}";
        var measured = m.Where(x => x.Stations.Count > 0).ToList();
        string W(StationWidth s) => s.WidthYards is double w ? $"{w:0}" : "none";
        string Line(HoleMeasures x) => $"{x.Number} ({string.Join("/", x.Stations.Select(W))})";
        var wide = measured.Where(x => x.Stations.Any(s => s.WidthYards > L.WidthHighYards)).ToList();
        var narrow = measured.Where(x => x.Stations.Any(s => (s.WidthYards ?? 0) < L.WidthLowYards)).ToList();
        var reason = measured.Where(x => x.Stations.Any(s => s.WidthYards > L.WidthReasonYards)).ToList();
        var parts = new List<string>();
        if (wide.Count > 0) parts.Add($"Over {L.WidthHighYards:0}: {string.Join(", ", wide.Select(Line))}.");
        if (narrow.Count > 0) parts.Add($"Under {L.WidthLowYards:0} (or no fairway by the line): {string.Join(", ", narrow.Select(Line))}.");
        if (reason.Count > 0) parts.Add($"Over {L.WidthReasonYards:0}, so the hole needs its reason written: {List(reason.Select(x => x.Number))}.");
        string stations = string.Join("/", new[] { L.DesignDriveMinYards, L.DesignDriveMidYards, L.DesignDriveMaxYards }.Select(y => $"{y:0}"));
        yield return new("landing-widths", $"Landing width {L.WidthLowYards:0}–{L.WidthHighYards:0} yds at {band} (target {L.LandingTargetMinYards:0}–{L.LandingTargetMaxYards:0})",
            wide.Count + narrow.Count > 0 ? Severity.Warning : Severity.Pass,
            measured.Count == 0 ? "No par 4s or 5s long enough to measure yet."
                : (parts.Count > 0 ? string.Join(" ", parts) : $"All {measured.Count} holes between {L.WidthLowYards:0} and {L.WidthHighYards:0} yds.") + $" Widths at {stations} yds; shared fairways count.",
            rule, wide.Concat(narrow).Select(x => x.Number).Distinct().OrderBy(n => n).ToList());

        var steady = m.Where(x => x.WidthSteadiness is not null).ToList();
        var uneven = steady.Where(x => x.WidthSteadiness >= L.WidthSteadinessMax).ToList();
        yield return new("width-steadiness", $"Fairway width steady past 200 yds (under {L.WidthSteadinessMax:0.#}×)",
            uneven.Count > 0 ? Severity.Warning : Severity.Pass,
            uneven.Count > 0 ? $"Wide stretches {L.WidthSteadinessMax:0.#}× the narrow ones or more on {string.Join(", ", uneven.Select(x => $"{x.Number} ({x.WidthSteadiness:0.0}×)"))}."
                : steady.Count > 0 ? $"All {steady.Count} measured fairways under {L.WidthSteadinessMax:0.#}×." : "No fairways past 200 yds to measure yet.",
            rule, uneven.Select(x => x.Number).ToList());

        var both = m.Where(x => x.LayupWidthYards is not null && x.LandingWidthYards is not null && x.LayupWidthYards > 0).ToList();
        var reverse = both.Where(x => x.LandingWidthYards > x.LayupWidthYards * 1.1).ToList();
        yield return new("width-pattern", $"Roomier at 250–290 yds than at {band}",
            reverse.Count > 0 ? Severity.Warning : Severity.Pass,
            reverse.Count > 0 ? $"Wider in the landing zone than the lay-up on {string.Join(", ", reverse.Select(x => $"{x.Number} ({x.LayupWidthYards:0} then {x.LandingWidthYards:0})"))}: the safe lay-back should be the roomy one."
                : both.Count > 0 ? $"All {both.Count} measured holes tighten (or hold) into the landing zone." : "No fairways to measure yet.",
            rule, reverse.Select(x => x.Number).ToList());
    }

    /// <summary>Prompts with no limit: things to look at hole by hole, never a pass or a fail.</summary>
    static IEnumerable<ReadinessCheck> PromptChecks(List<HoleMeasures> m, double medGreen, double medStart, ReadinessLimits L)
    {
        string band = $"{L.ScaleLow:0.0#}–{L.ScaleHigh:0.0#}×";
        var built = m.Where(x => x.GreenRatio is not null).ToList();
        var outside = built.Where(x => x.GreenRatio < L.ScaleLow || x.GreenRatio > L.ScaleHigh).ToList();
        yield return new("green-scale", $"Green sizes against the median ({N0(medGreen)} sq ft)", Severity.Info,
            built.Count < 2 ? "Greens are compared once they're built."
                : outside.Count > 0 ? $"Outside {band}: {string.Join(", ", outside.Select(x => $"{x.Number} ({x.GreenRatio:0.00}×)"))}. A prompt to look, not a limit."
                : $"All {built.Count} drawn greens inside {band}.",
            "Prompt · Principles §12.3 greens suit the approach", outside.Select(x => x.Number).ToList());

        var approach = m.Where(x => x.Par >= 4 && !x.GreenEstimated).ToList();
        yield return new("green-approach", $"Green size against the approach after {L.DesignDriveMidYards:0}", Severity.Info,
            approach.Count == 0 ? "No drawn greens on par 4s or 5s yet."
                : string.Join(", ", approach.Select(x => $"{x.Number}: {N0(x.GreenSquareFeet)} sq ft, {x.LeftAfter(L.DesignDriveMidYards)} left")) + ". A short approach wants a smaller or more demanding target.",
            "Prompt · Principles §12.3 greens suit the approach", []);

        var hz = m.Where(x => x.LandingHazards.Count > 0).ToList();
        yield return new("landing-hazards", $"Hazards within {L.LandingHazardReachYards:0} yds of the line at {L.LandingHazardFromYards:0}–{L.LandingHazardToYards:0} yds", Severity.Info,
            hz.Count == 0 ? "None near the line in the landing zone."
                : string.Join(" ", hz.Select(x => $"{x.Number}: {string.Join(", ", x.LandingHazards.Select(Describe))}.")) +
                    " For each, write what changes if the player goes left, right or over. Not scored by count.",
            "Prompt · Principles §12.3 hazards", hz.Select(x => x.Number).ToList());

        var longHoles = m.Where(x => x.Par >= 4).ToList();
        var started = longHoles.Where(x => x.FairwayStartYards is not null && !x.SharedFairway).ToList();
        int inBand = started.Count(x => x.FairwayStartYards >= L.FairwayStartMinYards && x.FairwayStartYards <= L.FairwayStartMaxYards);
        string Yds(HoleMeasures x) => x.FairwayStartYards <= 60 ? "60 or less" : $"{x.FairwayStartYards:0}";
        yield return new("fairway-start", $"Main fairway start from the back tee (band {L.FairwayStartMinYards:0}–{L.FairwayStartMaxYards:0})", Severity.Info,
            started.Count == 0 ? "No par 4s or 5s with fairways to measure yet."
                : $"Median {medStart:0} yds; {inBand} of {started.Count} inside the band. By hole: {string.Join(", ", started.Select(x => $"{x.Number} ({Yds(x)})"))}. A note: no reviewer has mentioned it.",
            "Note · fairway start", []);

        var carries = m.Where(x => x.ForwardCarryYards is not null).ToList();
        yield return new("forward-tees", "Every forward tee reaches its fairway", Severity.Info,
            carries.Count == 0 ? "No forward tees with a main fairway to reach."
                : $"Longest carry from a forward tee to where the main fairway starts: {string.Join(", ", carries.OrderByDescending(x => x.ForwardCarryYards).Take(6).Select(x => $"{x.Number} ({x.ForwardCarryYards:0})"))} yds. Play the forward tees to confirm.",
            "Prompt · Principles §2 forward tees", []);
    }

    /// <summary>"bunker at 312, 8 yds right".</summary>
    public static string Describe(LandingHazard h) =>
        $"{h.Kind} at {h.FromTeeYards}{(h.Side == "on the line" ? ", on the line" : $", {h.OffLineYards:0} yds {h.Side}")}";

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

        // Pre-made hills (hillsHeight) are terrain the brush replay can't see, so every height here would be off.
        yield return course.HasPremadeHills
            ? new("premade-hills", "No pre-made plot hills", Severity.Flag,
                $"The plot's pre-made hills are set to {course.HillsHeightMetres * 3.2808399:0} ft. That ground isn't in the terrain model, so elevations, slopes " +
                "and green relief here can be wrong. Set the hills to 0 and flatten the plot before building, or confirm heights in game.",
                "Studio terrain model · pre-made plot terrain", [])
            : new("premade-hills", "No pre-made plot hills", Severity.Pass, "Pre-made hills are off: all the ground comes from brushes.", "Studio terrain model · pre-made plot terrain", []);

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
                    " Count the three grid squares in the designer; slope is from the terrain model: confirm in game.",
                $"{rule} · pins", edge.Concat(steep).Select(p => p.Hole).Distinct().OrderBy(n => n).ToList());
        }

    }
}
