using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.YardageBook;

public sealed record YardageOptions
{
    /// <summary>Tee set names (shortest → longest); defaults to T1…Tn. Yardages always come from the course file.</summary>
    public IReadOnlyList<string>? TeeNames { get; init; }
    public double MetresBehindTee { get; init; } = 22;
    public double MetresBeyondGreen { get; init; } = 32;
    /// <summary>Fairway widths are measured at these distances from the back tee (yards).</summary>
    public IReadOnlyList<int> WidthStationsYards { get; init; } = [200, 250, 300];
    public int SpotSpacingYards { get; init; } = 50;
    public double BunkerLabelReachMetres { get; init; } = 40;

    /// <summary>Hazards at least this long along the hole (yards) also get a front-edge number.</summary>
    public int FrontLabelMinLengthYards { get; init; } = 25;

    /// <summary>A gap this long (m, along the line of play) splits one hazard into separately labelled stretches.</summary>
    public double StretchGapMetres { get; init; } = 8;

    /// <summary>Hazard points this close to the line of play are "the crossing"; numbers use only those when any exist.</summary>
    public double CrossingReachMetres { get; init; } = 15;
    public double BunkerMergeMetres { get; init; } = 10;

    /// <summary>Bushes/trees closer than this fuse into one vegetation mass (same closing as bunkers).</summary>
    public double VegetationMergeMetres { get; init; } = 8;

    /// <summary>Canopy radius (m) assumed for a bush at scale 1; trees use 2.5×.</summary>
    public double BushRadiusMetres { get; init; } = 1.6;

    /// <summary>Vegetation masses smaller than this (m²) — roughly a lone bush — are dropped.</summary>
    public double MinVegetationArea { get; init; } = 12;

    /// <summary>Rocks closer than this fuse into one outcrop shape.</summary>
    public double RockMergeMetres { get; init; } = 6;

    /// <summary>In-fairway rocks/vegetation within this distance fuse into one hazard (e.g. a dividing outcrop).</summary>
    public double InPlayMergeMetres { get; init; } = 12;

    /// <summary>In-play hazards smaller than this (m²) are drawn but not given carry numbers.</summary>
    public double MinLabelledHazardArea { get; init; } = 30;
    public TerrainModelOptions Terrain { get; init; } = new() { CellMetres = 0.5 };
    public string Credit { get; init; } = "";
}

public sealed record TeeSet(string Name, int Yards, Vec2 Tee, double ElevationFt);
/// <param name="IsFront">True for the front edge of a long hazard (the lay-up number); false for the carry point.</param>
public sealed record BunkerLabel(Vec2 Point, int FromTeeYards, int ToGreenYards, bool IsFront = false);
public sealed record AimPoint(Vec2 Point, int FromTeeYards, int ToGreenYards);
public sealed record WidthLabel(Vec2 Centre, Vec2 Left, Vec2 Right, int Yards, int StationYards);
public sealed record SpotElevation(Vec2 Point, double Feet);

/// <summary>Everything a yardage page needs for one hole, in world coordinates.</summary>
public sealed class HoleAnalysis
{
    public const double Yard = 0.9144, Foot = 0.3048;

    public required Hole Hole { get; init; }
    public required IReadOnlyList<Vec2> Route { get; init; }          // back tee → aim points → green anchor
    public required Vec2 BackTee { get; init; }
    public required Vec2 Green { get; init; }
    public required Vec2 Forward { get; init; }                        // unit, back tee → green
    public required double RotationDegrees { get; init; }              // MapProjection rotation that points Forward up the page
    public required IReadOnlyList<Vec2> Window { get; init; }          // 4 corners (world) of the page view
    public required IReadOnlyList<TeeSet> TeeSets { get; init; }
    public required IReadOnlyList<List<Vec2>> Fairways { get; init; }
    public required IReadOnlyList<List<Vec2>> Rough { get; init; }
    public required IReadOnlyList<List<Vec2>> Greens { get; init; }
    public required List<Vec2>? GreenOutline { get; init; }
    public required IReadOnlyList<List<Vec2>> Bunkers { get; init; }
    public required IReadOnlyList<List<Vec2>> Water { get; init; }

    /// <summary>Ponds and creeks from water planes and painted streams: each body's shores (outer first, then islands; fill even-odd).</summary>
    public required IReadOnlyList<IReadOnlyList<List<Vec2>>> WaterBodies { get; init; }
    public required IReadOnlyList<Ribbon> Paths { get; init; }
    public required IReadOnlyList<PlacedObject> Scrub { get; init; }
    public required IReadOnlyList<PlacedObject> Trees { get; init; }
    public required IReadOnlyList<PlacedObject> Rocks { get; init; }

    /// <summary>Bushes and trees fused into smooth vegetation masses (world outlines).</summary>
    public required IReadOnlyList<List<Vec2>> Vegetation { get; init; }

    /// <summary>Rocks fused into outcrop shapes (world outlines).</summary>
    public required IReadOnlyList<List<Vec2>> Outcrops { get; init; }

    /// <summary>Vegetation/outcrop masses that sit in this hole's fairway (drawn strongly, given carries).</summary>
    public required IReadOnlySet<List<Vec2>> InPlay { get; init; }

    /// <summary>Surfaces that belong to another hole's line of play (drawn faded).</summary>
    public required IReadOnlySet<List<Vec2>> Foreign { get; init; }

    /// <summary>Dogleg / lay-up aim points on the line of play.</summary>
    public required IReadOnlyList<AimPoint> AimPoints { get; init; }
    public required IReadOnlyList<BunkerLabel> BunkerLabels { get; init; }

    /// <summary>Why hazards near the hole were or weren't labelled (for tuning; printed with --verbose).</summary>
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required IReadOnlyList<WidthLabel> Widths { get; init; }
    public required IReadOnlyList<SpotElevation> Spots { get; init; }
    public required int GreenDepthYards { get; init; }
    public required int GreenFrontYards { get; init; }
    public required int GreenBackYards { get; init; }

    public int LengthYards => TeeSets.Count > 0 ? TeeSets[^1].Yards : (int)Math.Round(Hole.RouteLength / Yard);

    public static HoleAnalysis Analyze(CourseModel course, CourseLayers layers, Hole hole, YardageOptions o, double pageAspect,
        RockFootprints? rockSizes = null, WaterMap? inlandWater = null)
    {
        rockSizes ??= new RockFootprints(course);
        inlandWater ??= course.Water.Count > 0 ? WaterTracer.Trace(course, o.Terrain) : null;
        var back = hole.Tees.Count > 0 ? hole.Tees[^1] : hole.Route[0];
        var green = hole.GreenAnchor;
        var route = new List<Vec2> { back };
        route.AddRange(hole.Route.Skip(1));
        var fwd = (green - back).Normalized();
        var side = new Vec2(fwd.Z, -fwd.X);
        double rotation = Math.Atan2(fwd.X, fwd.Z) * 180 / Math.PI;   // MapProjection: v = x·sinθ + z·cosθ is "up"

        // View window: along the tee→green axis, widened to the page aspect; centred on the route's lateral spread.
        double along0 = -o.MetresBehindTee, along1 = Vec2.Distance(back, green) + o.MetresBeyondGreen;
        double halfWidth = (along1 - along0) * pageAspect / 2;
        double lateral = route.Average(p => Dot(p - back, side));
        var origin = back + side * lateral;
        var window = new List<Vec2>
        {
            origin + fwd * along0 - side * halfWidth, origin + fwd * along0 + side * halfWidth,
            origin + fwd * along1 + side * halfWidth, origin + fwd * along1 - side * halfWidth,
        };
        bool InView(Vec2 p, double pad = 10) =>
            Dot(p - origin, fwd) is var a && a >= along0 - pad && a <= along1 + pad &&
            Math.Abs(Dot(p - origin, side)) <= halfWidth + pad;
        bool AnyInView(IReadOnlyList<Vec2> poly) => poly.Any(p => InView(p));

        // Surfaces from the course file.
        List<List<Vec2>> Closed(params int[] surfaces) => course.SurfaceSplines
            .Where(s => s.Closed && s.Points.Count >= 3 && surfaces.Contains(s.Surface))
            .Select(s => Geometry.SampleSpline(s, 12)).Where(AnyInView).ToList();
        var fairways = Closed(Surfaces.Fairway, Surfaces.FairwayCut);
        var rough = Closed(Surfaces.Rough, Surfaces.HeavyRough);
        var greens = Closed(Surfaces.Green);
        var water = Closed(Surfaces.Water);
        var waterBodies = (inlandWater?.Bodies ?? []).Where(b => AnyInView(b.Outline)).ToList();
        var waterShapes = new HashSet<List<Vec2>>(water.Concat(waterBodies.Select(b => b.Outline)), ReferenceEqualityComparer.Instance);
        var greenOutline = greens.Where(g => Geometry.Contains(g, green)).OrderBy(Geometry.Area).FirstOrDefault();
        var bunkers = layers.MergedBunkers(o.BunkerMergeMetres).Where(AnyInView).ToList();
        var paths = layers.TrimToPlay(layers.Paths.Concat(layers.Roads), 45).Where(r => AnyInView(r.Points)).ToList();

        var objects = course.PlacedObjects.Where(p => InView(p.Position, 30)).ToList();
        var trees = objects.Where(p => p.AssetPath.Contains("/Trees/", StringComparison.OrdinalIgnoreCase)).ToList();
        var scrub = objects.Where(p => p.AssetPath.Contains("/Bushes/", StringComparison.OrdinalIgnoreCase)).ToList();
        var rocks = objects.Where(p => p.AssetPath.Contains("/Rocks/", StringComparison.OrdinalIgnoreCase)).ToList();

        // Vegetation: each plant is a canopy disc; nearby canopies fuse into masses, never across fairways/greens.
        var canopies = scrub.Select(s => new Disc(s.Position, o.BushRadiusMetres * Math.Max(0.5, s.Scale)))
            .Concat(trees.Select(t => new Disc(t.Position, 2.5 * o.BushRadiusMetres * Math.Max(0.5, t.Scale)))).ToList();
        var vegetation = canopies.Count == 0 ? [] : ShapeMerger.Merge([], [], canopies,
            closeRadius: o.VegetationMergeMetres / 2, smooth: 1.0,
            keepOut: fairways.Concat(greens).Select(g => (IReadOnlyList<Vec2>)g).ToList(), keepOutReach: 2.5)
            .Where(v => Geometry.Area(v) >= o.MinVegetationArea).ToList();

        // Rocks: merged into outcrop shapes (no keep-out, so an outcrop inside a fairway stays whole).
        // Rock footprints come from the course's own vegetation clearances (see RockFootprints).
        var rockDiscs = rocks.Select(r => new Disc(r.Position, rockSizes.RadiusOf(r))).ToList();
        var outcrops = rockDiscs.Count == 0 ? [] : ShapeMerger.Merge([], [], rockDiscs,
            closeRadius: o.RockMergeMetres / 2, smooth: 0.8).Where(v => Geometry.Area(v) >= 4).ToList();

        // Ownership: each surface belongs to the hole whose line of play most of its outline is nearest to.
        var allRoutes = course.Holes.Select(h =>
        {
            var r = new List<Vec2> { h.Tees.Count > 0 ? h.Tees[^1] : h.Route[0] };
            r.AddRange(h.Route.Skip(1));
            return (h.Number, Route: (IReadOnlyList<Vec2>)r);
        }).ToList();
        int Owner(IReadOnlyList<Vec2> poly)
        {
            int stride = Math.Max(1, poly.Count / 24);
            var votes = new Dictionary<int, int>();
            for (int i = 0; i < poly.Count; i += stride)
            {
                var p = poly[i];
                int best = allRoutes.MinBy(r => DistanceToPolyline(p, r.Route)).Number;
                votes[best] = votes.GetValueOrDefault(best) + 1;
            }
            return votes.MaxBy(v => v.Value).Key;
        }
        // A shape touching this hole's line of play (within 15 m, or containing it) is always ours, even if
        // it is one big polygon shared with neighbours; only shapes away from play are put to the vote.
        bool NearOwnPlay(List<Vec2> shape)
        {
            int stride = Math.Max(1, shape.Count / 60);
            for (int i = 0; i < shape.Count; i += stride)
                if (DistanceToPolyline(shape[i], route) <= 15) return true;
            for (double d = 0, len = RouteLength(route); d <= len; d += 5)
                if (Geometry.Contains(shape, PointAtDistance(route, d).Point)) return true;
            return false;
        }
        var foreign = new HashSet<List<Vec2>>(ReferenceEqualityComparer.Instance);
        foreach (var shape in fairways.Concat(rough).Concat(greens).Concat(bunkers).Concat(waterShapes))
            if (!NearOwnPlay(shape) && Owner(shape) != hole.Number) foreign.Add(shape);
        var ownFairways = fairways.Where(f => !foreign.Contains(f)).ToList();

        // In-play masses: vegetation/outcrops mostly inside this hole's fairway. Scattered pieces are
        // re-merged with a wider closing so a field of rocks and bushes reads as one hazard (a divider).
        bool MostlyInFairway(List<Vec2> m)
        {
            int stride = Math.Max(1, m.Count / 24), inside = 0, total = 0;
            for (int i = 0; i < m.Count; i += stride, total++)
                if (ownFairways.Any(f => Geometry.Contains(f, m[i]))) inside++;
            return total > 0 && inside * 2 >= total;
        }
        var inPlayPieces = vegetation.Concat(outcrops).Where(MostlyInFairway).ToList();
        vegetation = vegetation.Where(v => !inPlayPieces.Contains(v)).ToList();
        outcrops = outcrops.Where(v => !inPlayPieces.Contains(v)).ToList();
        var hazards = inPlayPieces.Count == 0 ? [] : ShapeMerger.Merge(inPlayPieces.Select(p => (IReadOnlyList<Vec2>)p).ToList(), [], [],
            closeRadius: o.InPlayMergeMetres / 2, smooth: 1.0);
        var inPlay = new HashSet<List<Vec2>>(hazards, ReferenceEqualityComparer.Instance);
        outcrops.AddRange(hazards);   // drawn with the outcrop style; InPlay marks them as strong

        // Terrain over the window (calibrated brush replay), green centre = 0.
        var min = new Vec2(window.Min(p => p.X), window.Min(p => p.Z));
        var max = new Vec2(window.Max(p => p.X), window.Max(p => p.Z));
        var field = TerrainReplay.Build(course, min, max, o.Terrain);
        double greenH = field.Sample(green);
        double Feet(Vec2 p) => (field.Sample(p) - greenH) / Foot;

        // Tee sets: yardage by the game's rule; elevation of each tee relative to the green.
        int sets = Math.Max(1, hole.Tees.Count);
        var teeSets = Enumerable.Range(0, sets).Select(i =>
        {
            var tee = hole.Tees.Count > 0 ? hole.Tees[Math.Min(i, hole.Tees.Count - 1)] : back;
            string name = o.TeeNames?.ElementAtOrDefault(i) is { Length: > 0 } n ? n : $"T{i + 1}";
            return new TeeSet(name, (int)Math.Round(hole.TeeSetLength(i) / Yard), tee, Feet(tee));
        }).ToList();
        double holeYards = teeSets[^1].Yards;

        // Fairway hazards (own bunkers, water, in-play vegetation/outcrops): label the carry point
        // (farthest from the back tee) with from-tee / to-green yards.
        var labels = new List<BunkerLabel>();
        var diagnostics = new List<string>();
        foreach (var b in bunkers.Concat(waterShapes).Concat(inPlay.Where(h => Geometry.Area(h) >= o.MinLabelledHazardArea)))
        {
            bool isWater = waterShapes.Contains(b);
            string what = inPlay.Contains(b) ? "in-play hazard" : isWater ? "water" : "bunker";
            // A forced carry over water off the tee always gets its numbers; bunkers that close are forward-tee clutter.
            int minCarry = isWater ? 0 : 120, minFront = isWater ? 0 : 100;
            // On a par 3 the tee shot is the approach: water in front of the green keeps its carry number
            // (Sangoma's hole 11 clears its pond 39 yds short of the green).
            int minCarryToGreen = isWater && hole.Par == 3 ? 0 : 40;
            // Only the part of the hazard near the line of play counts (a waste area can sprawl far off-line)...
            // Prefer the part that actually crosses (or hugs) the line of play; fall back to the wider reach.
            var near = b.Where(p => DistanceToPolyline(p, route) <= o.CrossingReachMetres).ToList();
            if (near.Count == 0) near = b.Where(p => DistanceToPolyline(p, route) <= o.BunkerLabelReachMetres).ToList();
            var along = near.Select(p => (Point: p, S: ProjectOnto(route, p))).OrderBy(t => t.S).ToList();
            if (near.Count == 0) continue;
            if (foreign.Contains(b)) { diagnostics.Add($"{what} {Geometry.Area(b):0} m² near play: skipped (belongs to another hole)"); continue; }

            // ...and a hazard that meets the line of play in several places (e.g. a cross-bunker that wraps up
            // to the green) is split into separate stretches along the hole, each with its own numbers.
            var stretches = new List<List<Vec2>> { new() { along[0].Point } };
            for (int i = 1; i < along.Count; i++)
            {
                if (along[i].S - along[i - 1].S > o.StretchGapMetres) stretches.Add([]);
                stretches[^1].Add(along[i].Point);
            }
            // In-play islands are compact: label the whole island once rather than per stretch.
            if (inPlay.Contains(b)) stretches = [b];
            // Water is one sheet between its banks: the near and far shores are one crossing, not two stretches.
            if (isWater) stretches = [along.Select(t => t.Point).ToList()];
            foreach (var part in stretches)
            {
                var carry = part.MaxBy(p => Vec2.Distance(p, back));
                int fromTee = (int)Math.Round(Vec2.Distance(carry, back) / Yard);
                int toGreen = (int)Math.Round(Vec2.Distance(carry, green) / Yard);
                var frontPt = part.MinBy(p => Vec2.Distance(p, back));
                int frontTee = (int)Math.Round(Vec2.Distance(frontPt, back) / Yard);
                int frontGreen = (int)Math.Round(Vec2.Distance(frontPt, green) / Yard);
                bool isLong = fromTee - frontTee >= o.FrontLabelMinLengthYards;

                // Carry: skip forward-tee clutter, greenside stretches, and anything past the green.
                if (fromTee < minCarry || toGreen < minCarryToGreen || fromTee > holeYards - 25)
                    diagnostics.Add($"{what} {Geometry.Area(b):0} m² stretch: carry {fromTee}/{toGreen} filtered (needs tee ≥{minCarry}, green ≥{minCarryToGreen}, tee ≤ {holeYards - 25})");
                else
                {
                    diagnostics.Add($"{what} {Geometry.Area(b):0} m² stretch: carry {fromTee}/{toGreen} labelled");
                    labels.Add(new BunkerLabel(carry, fromTee, toGreen));
                }

                // Long stretches: also the front edge (how far is safe short of it). Kept even when the carry
                // runs up to the green, since the lay-up number is still what a golfer needs.
                if (isLong && frontTee >= minFront && frontGreen >= 40 && frontTee <= holeYards - 25)
                {
                    diagnostics.Add($"{what} {Geometry.Area(b):0} m² stretch: front {frontTee}/{frontGreen} labelled");
                    labels.Add(new BunkerLabel(frontPt, frontTee, frontGreen, IsFront: true));
                }
            }
        }

        // Fairway widths across the route at fixed distances from the back tee.
        var widths = new List<WidthLabel>();
        foreach (int station in o.WidthStationsYards)
        {
            if (station > holeYards - 60) continue;
            var (pt, _) = PointAtDistance(route, station * Yard);
            // Measured square to the page axis so stations on different dogleg legs never cross.
            if (MeasureAcross(ownFairways, bunkers.Concat(inPlay).ToList(), pt, side) is { } w)
                widths.Add(new WidthLabel((w.Left + w.Right) * 0.5, w.Left, w.Right, (int)Math.Round(Vec2.Distance(w.Left, w.Right) / Yard), station));
        }

        // Spot elevations along the route.
        var spots = new List<SpotElevation>();
        // Offset half a spacing from the width stations so the two label sets don't collide.
        for (int y = 125; y <= holeYards - 40; y += o.SpotSpacingYards)
        {
            var (pt, _) = PointAtDistance(route, y * Yard);
            spots.Add(new SpotElevation(pt, Feet(pt)));
        }

        // Aim points: every intermediate route waypoint (dogleg corner / lay-up target).
        var aims = route.Skip(1).Take(route.Count - 2)
            .Select(p => new AimPoint(p, (int)Math.Round(Vec2.Distance(back, p) / Yard), (int)Math.Round(Vec2.Distance(p, green) / Yard)))
            .ToList();

        // Green front/back along the line of play (last route segment).
        int depth = 0, front = (int)Math.Round(holeYards), backYards = front;
        if (greenOutline is not null)
        {
            var approach = (green - route[^2]).Normalized();
            double lo = greenOutline.Min(p => Dot(p - green, approach)), hi = greenOutline.Max(p => Dot(p - green, approach));
            depth = (int)Math.Round((hi - lo) / Yard);
            front = (int)Math.Round(holeYards + lo / Yard);
            backYards = (int)Math.Round(holeYards + hi / Yard);
        }

        return new HoleAnalysis
        {
            Hole = hole, Route = route, BackTee = back, Green = green, Forward = fwd, RotationDegrees = rotation,
            Window = window, TeeSets = teeSets, Fairways = fairways, Rough = rough, Greens = greens,
            GreenOutline = greenOutline, Bunkers = bunkers, Water = water, Paths = paths,
            WaterBodies = waterBodies.Select(b => b.Shores).ToList(),
            Scrub = scrub, Trees = trees, Rocks = rocks, Vegetation = vegetation, Outcrops = outcrops,
            InPlay = inPlay, Foreign = foreign, AimPoints = aims, BunkerLabels = labels, Diagnostics = diagnostics, Widths = widths, Spots = spots,
            GreenDepthYards = depth, GreenFrontYards = front, GreenBackYards = backYards,
        };
    }

    static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;

    /// <summary>Arc-length position along a polyline of the point on it nearest to <paramref name="p"/>.</summary>
    static double ProjectOnto(IReadOnlyList<Vec2> line, Vec2 p)
    {
        double bestD = double.MaxValue, bestS = 0, run = 0;
        for (int i = 1; i < line.Count; i++)
        {
            var a = line[i - 1];
            var ab = line[i] - a;
            double len2 = Dot(ab, ab), len = Math.Sqrt(len2);
            double t = len2 < 1e-12 ? 0 : Math.Clamp(Dot(p - a, ab) / len2, 0, 1);
            double d = Vec2.Distance(p, a + ab * t);
            if (d < bestD) { bestD = d; bestS = run + t * len; }
            run += len;
        }
        return bestS;
    }

    static double RouteLength(IReadOnlyList<Vec2> line)
    {
        double s = 0;
        for (int i = 1; i < line.Count; i++) s += Vec2.Distance(line[i - 1], line[i]);
        return s;
    }

    static (Vec2 Point, Vec2 Direction) PointAtDistance(IReadOnlyList<Vec2> line, double d)
    {
        for (int i = 1; i < line.Count; i++)
        {
            double seg = Vec2.Distance(line[i - 1], line[i]);
            var dir = (line[i] - line[i - 1]).Normalized();
            if (d <= seg || i == line.Count - 1) return (line[i - 1] + dir * Math.Min(d, seg), dir);
            d -= seg;
        }
        return (line[^1], new Vec2(0, 1));
    }

    static double DistanceToPolyline(Vec2 p, IReadOnlyList<Vec2> line)
    {
        double best = double.MaxValue;
        for (int i = 1; i < line.Count; i++)
        {
            var a = line[i - 1];
            var ab = line[i] - a;
            double len2 = Dot(ab, ab);
            double t = len2 < 1e-12 ? 0 : Math.Clamp(Dot(p - a, ab) / len2, 0, 1);
            best = Math.Min(best, Vec2.Distance(p, a + ab * t));
        }
        return best;
    }

    /// <summary>
    /// Walks across the fairway along ±n from <paramref name="p"/>; returns the contiguous run of playable
    /// fairway (inside a fairway shape and not inside a bunker) nearest p.
    /// </summary>
    static (Vec2 Left, Vec2 Right)? MeasureAcross(IReadOnlyList<List<Vec2>> fairways, IReadOnlyList<List<Vec2>> bunkers, Vec2 p, Vec2 n)
    {
        const double step = 0.5, reach = 70;
        bool Inside(Vec2 q) => fairways.Any(f => Geometry.Contains(f, q)) && !bunkers.Any(b => Geometry.Contains(b, q));
        // Start from the route point, or the nearest fairway cell if the route is off the fairway here.
        double start = double.NaN;
        for (double s = 0; s <= reach && double.IsNaN(start); s += step)
        {
            if (Inside(p + n * s)) start = s;
            else if (Inside(p - n * s)) start = -s;
        }
        if (double.IsNaN(start)) return null;
        double lo = start, hi = start;
        while (hi + step <= reach && Inside(p + n * (hi + step))) hi += step;
        while (lo - step >= -reach && Inside(p + n * (lo - step))) lo -= step;
        return (p + n * lo, p + n * hi);
    }
}
