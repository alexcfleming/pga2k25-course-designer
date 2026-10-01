using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using static Pga2K25.Finisher.FinisherGeometry;

namespace Pga2K25.Finisher.Tees;

public sealed record TeePlacement(Vec2 Point, string Source, int? SurfaceIndex, double TerrainRange);

public sealed record TeeHoleReport(int Hole, int Existing, int Requested, int Added, int Final, string Warning, IReadOnlyList<TeePlacement> Placements);

public sealed record RecenteredTee(int Hole, bool Moved, double Distance, string Mode, int SurfaceIndex, Vec2 From, Vec2 To);

public sealed record TeeCreationReport(int TargetPerHole, int Added, int HolesTouched, IReadOnlyList<RecenteredTee> RecenteredBackTees, IReadOnlyList<TeeHoleReport> Details);

/// <summary>
/// Pass 6, <c>smart_complete_tees</c>: first centres every existing back tee left-right on its pad, then tops
/// each hole up to the requested number of tee markers — one per free tee pad ahead of the back tee, then
/// extra sites inside shared/long tee complexes — and parks any it cannot place 1 m in front of the back tee
/// for manual placement. Tees are re-ordered front → back.
/// </summary>
public static class TeeCreation
{
    public const int DefaultTotal = 4, MinTotal = 1, MaxTotal = 5;
    public const double ChainGap = 65.0;

    public static TeeCreationReport Apply(JsonObject course, int targetTotal = DefaultTotal, IProgress<string>? log = null)
    {
        targetTotal = Math.Max(MinTotal, Math.Min(MaxTotal, targetTotal));
        var holes = TeeGeometry.Holes(course);
        var geometries = TeeGeometry.AssignOwners(course, TeeGeometry.SurfaceGeometries(course));

        var recentered = new List<RecenteredTee>();
        for (int h = 0; h < holes.Count; h++)
            if (holes[h] is JsonObject hole && RecenterBackTee(hole, h + 1, geometries, log) is RecenteredTee info) recentered.Add(info);

        var sampler = new FineTerrainSampler(course);
        int totalAdded = 0, holesTouched = 0;
        var details = new List<TeeHoleReport>();
        log?.Report("=== SMART TEE CREATION / PLACEMENT ===");
        log?.Report($"Requested total tee markers per hole: {targetTotal}");

        for (int h = 0; h < holes.Count; h++)
        {
            if (holes[h] is not JsonObject hole) continue;
            int holeIndex = h + 1;
            var tees = Json.EnsureArray(hole, "tees");
            var existing = tees.Select(TeeGeometry.TeePoint).Where(p => p is not null).Select(p => p!.Value).ToList();
            if (existing.Count >= targetTotal)
            {
                log?.Report($"Hole {holeIndex}: already has {existing.Count} tee marker(s); no additions needed.");
                continue;
            }

            var (back, forward, candidates) = TeeGeometry.PadCandidates(hole, geometries, holeIndex);
            if (back is null || forward is null)
            {
                (back, forward) = PlaceholderAxis(hole);
                candidates = [];
            }
            Vec2 b = back!.Value, f = forward!.Value;
            int need = targetTotal - existing.Count;

            // Free pads ahead of the back tee, stopping at the first gap of more than 65 m.
            var chain = new List<TeePadCandidate>();
            foreach (var item in candidates.Where(c => !c.IsOccupied && c.Along >= TeeGeometry.MinForward))
            {
                if (chain.Count > 0 && item.Along - chain[^1].Along > ChainGap) break;
                chain.Add(item);
            }
            var newSites = new List<(Vec2 Point, double Along, double Lateral, double TerrainRange, string Source, int? SurfaceIndex)>();
            foreach (var item in chain)
            {
                if (newSites.Count >= need) break;
                var point = item.Center;
                var occupiedPoints = existing.Concat(newSites.Select(s => s.Point));
                if (occupiedPoints.Any(p => Vec2.Distance(point, p) < TeeGeometry.MinSeparation)) continue;
                double range = LocalTerrainRange(sampler, point, Math.Min(4.0, Math.Max(2.5, 0.35 * item.Minor)));
                newSites.Add((point, item.Along, item.Lateral, range, item.Surface.Kind, item.Surface.Index));
            }

            // Shared or long tee complexes: extra sites inside the occupied pads and the pads just used.
            int remaining = need - newSites.Count;
            if (remaining > 0)
            {
                var supportPolys = candidates.Where(c => c.IsOccupied).Select(c => c.Surface.Poly).ToList();
                foreach (var site in newSites)
                {
                    if (site.SurfaceIndex is not int index) continue;
                    var match = candidates.FirstOrDefault(c => c.Surface.Index == index);
                    if (match is not null) supportPolys.Add(match.Surface.Poly);
                }
                if (supportPolys.Count > 0)
                    foreach (double separation in new[] { 8.0, 5.0, 3.0, 2.0, 1.25, 0.85 })
                    {
                        remaining = need - newSites.Count;
                        if (remaining <= 0) break;
                        var occupiedForInternal = existing.Concat(newSites.Select(s => s.Point)).ToList();
                        var internalSites = ChooseInternalSites(supportPolys, b, f, occupiedForInternal, remaining, sampler, separation);
                        newSites.AddRange(internalSites.Take(remaining).Select(s => (s.Point, s.Along, s.Lateral, s.TerrainRange, "shared-complex", (int?)null)));
                    }
            }

            var accepted = new List<TeePlacement>();
            var occupied = existing.ToList();
            foreach (var site in newSites.OrderBy(s => s.Along))
            {
                double required = site.Source == "shared-complex" ? 0.75 : TeeGeometry.MinSeparation;
                if (occupied.Any(p => Vec2.Distance(site.Point, p) < required)) continue;
                accepted.Add(new TeePlacement(site.Point, site.Source, site.SurfaceIndex, site.TerrainRange));
                occupied.Add(site.Point);
                if (accepted.Count >= need) break;
            }
            foreach (var site in accepted) tees.Add(NewTee(site.Point));

            var placeholders = new List<TeePlacement>();
            int missing = Math.Max(0, targetTotal - tees.Count);
            if (missing > 0)
            {
                var (point, _, _) = PlaceholderPoint(hole, 1.0);
                for (int i = 0; i < missing; i++)
                {
                    tees.Add(NewTee(point));
                    placeholders.Add(new TeePlacement(point, "manual-placeholder", null, 0.0));
                }
                log?.Report($"Hole {holeIndex}: added {missing} manual tee placeholder(s) 1.0 m in front of the back tee.");
            }

            // Front → back: descending distance along the hole from the back tee.
            var ordered = tees.Select(t => (Tee: t, Point: TeeGeometry.TeePoint(t))).Where(e => e.Point is not null)
                .Select(e => (e.Tee, Along: TeeGeometry.Progress(e.Point!.Value, b, f).Along)).ToList();
            if (ordered.Count > 0)
            {
                var sorted = ordered.OrderByDescending(e => e.Along).Select(e => e.Tee!.DeepClone()).ToList();
                hole["tees"] = Json.Array(sorted);
            }

            int added = accepted.Count + placeholders.Count;
            int finalCount = Json.Arr(hole, "tees").Count;
            totalAdded += added;
            if (added > 0) holesTouched++;
            string warning = "";
            if (placeholders.Count > 0) warning = $"{placeholders.Count} tee marker(s) parked 1.0 m ahead of back tee for manual placement";
            if (finalCount < targetTotal) warning = "requested tee count was not reached";
            details.Add(new TeeHoleReport(holeIndex, existing.Count, targetTotal, added, finalCount, warning, accepted.Concat(placeholders).ToList()));
            string msg = $"Hole {holeIndex}: tees {existing.Count} -> {finalCount} (added {added}).";
            if (warning.Length > 0) msg += $" WARNING: {warning}.";
            log?.Report(msg);
        }

        var meta = Provenance.Meta(course);
        Provenance.BumpVersion(meta);
        meta["teeCreation"] = new JsonObject
        {
            ["targetPerHole"] = targetTotal,
            ["added"] = totalAdded,
            ["holesTouched"] = holesTouched,
            ["recenteredExistingBackTees"] = Json.Array(recentered.Select(r => (JsonNode?)RecenteredJson(r))),
            ["details"] = Json.Array(details.Select(d => (JsonNode?)DetailJson(d))),
        };
        log?.Report($"Smart tee creation complete: added {totalAdded} tee marker(s) across {holesTouched} hole(s).");
        return new TeeCreationReport(targetTotal, totalAdded, holesTouched, recentered, details);
    }

    static JsonObject NewTee(Vec2 p) => new() { ["position"] = Json.Xyz(p.X, 0.0, p.Z) };

    static JsonObject RecenteredJson(RecenteredTee r) => new()
    {
        ["hole"] = r.Hole,
        ["moved"] = r.Moved,
        ["distance"] = Json.F(r.Distance),
        ["mode"] = r.Mode,
        ["surfaceIndex"] = r.SurfaceIndex,
        ["from"] = Json.Floats([r.From.X, r.From.Z]),
        ["to"] = Json.Floats([r.To.X, r.To.Z]),
    };

    static JsonObject DetailJson(TeeHoleReport d) => new()
    {
        ["hole"] = d.Hole,
        ["existing"] = d.Existing,
        ["requested"] = d.Requested,
        ["added"] = d.Added,
        ["final"] = d.Final,
        ["warning"] = d.Warning,
        ["placements"] = Json.Array(d.Placements.Select(p => (JsonNode?)new JsonObject
        {
            ["x"] = Json.F(p.Point.X),
            ["z"] = Json.F(p.Point.Z),
            ["source"] = p.Source,
            ["surfaceIndex"] = p.SurfaceIndex is int i ? i : null,
            ["terrainRange"] = Json.F(p.TerrainRange),
        })),
    };

    /// <summary><c>_robust_range</c>: 90th − 10th percentile.</summary>
    internal static double RobustRange(IReadOnlyCollection<double> values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length < 2) return 0.0;
        Array.Sort(finite);
        return NumPy.PercentileSorted(finite, 90.0) - NumPy.PercentileSorted(finite, 10.0);
    }

    /// <summary><c>_tee_local_terrain_range</c>: robust relief on a 1.5 m grid within <paramref name="radius"/> (999 if unsampled).</summary>
    internal static double LocalTerrainRange(FineTerrainSampler sampler, Vec2 point, double radius = 3.0)
    {
        const double step = 1.5;
        var values = new List<double>();
        foreach (double zz in NumPy.Arange(point.Z - radius, point.Z + radius + 1e-6, step))
            foreach (double xx in NumPy.Arange(point.X - radius, point.X + radius + 1e-6, step))
            {
                if ((xx - point.X) * (xx - point.X) + (zz - point.Z) * (zz - point.Z) > radius * radius) continue;
                double v = sampler.Sample(xx, zz);
                if (double.IsFinite(v)) values.Add(v);
            }
        return values.Count > 0 ? RobustRange(values) : 999.0;
    }

    /// <summary><c>_tee_internal_candidate_points</c>: points along the hole axis through the pad centre, else a 1 m grid.</summary>
    static List<(Vec2 Point, double Along, double Lateral, double Clearance)> InternalCandidatePoints(List<Vec2> poly, Vec2 back, Vec2 forward)
    {
        var points = new List<(Vec2, double, double, double)>();
        if (poly.Count < 3 || CentroidPoint(poly) is not Vec2 center) return points;
        var (_, major) = PcaDimensions(poly);
        double extent = Math.Max(8.0, Math.Min(120.0, 0.7 * Math.Max(major, 1.0)));
        double step = Math.Max(1.0, TeeGeometry.InternalGrid);
        foreach (double t in NumPy.Arange(-extent, extent + 1e-6, step))
        {
            var p = new Vec2(center.X + t * forward.X, center.Z + t * forward.Z);
            if (!Contains(poly, p)) continue;
            var (along, lateral) = TeeGeometry.Progress(p, back, forward);
            if (along < -2.0) continue;
            double clearance = EdgeClearance(p, poly);
            if (clearance < 0.6) continue;
            points.Add((p, along, lateral, clearance));
        }
        if (points.Count < 3)
        {
            var (x0, z0, x1, z1) = BBox(poly);
            foreach (double zz in NumPy.Arange(z0 + 0.5 * step, z1 + 1e-6, step))
                foreach (double xx in NumPy.Arange(x0 + 0.5 * step, x1 + 1e-6, step))
                {
                    var p = new Vec2(xx, zz);
                    if (!Contains(poly, p)) continue;
                    var (along, lateral) = TeeGeometry.Progress(p, back, forward);
                    if (along < -2.0) continue;
                    double clearance = EdgeClearance(p, poly);
                    if (clearance < 0.75) continue;
                    points.Add((p, along, lateral, clearance));
                }
        }
        return points;
    }

    /// <summary>
    /// <c>_choose_internal_tee_sites</c>: greedy best-scored sites (edge clearance, spacing, flatness) that keep
    /// <paramref name="minSeparation"/> from every existing and chosen marker.
    /// </summary>
    static List<(Vec2 Point, double Along, double Lateral, double TerrainRange)> ChooseInternalSites(List<List<Vec2>> polys, Vec2 back, Vec2 forward,
        List<Vec2> existingPoints, int wanted, FineTerrainSampler sampler, double minSeparation)
    {
        var selected = new List<(Vec2, double, double, double)>();
        if (wanted <= 0) return selected;
        minSeparation = Math.Max(1.5, minSeparation);
        // Unique by 0.1 m key: first occurrence keeps its place, the last one seen wins the value.
        var unique = new Dictionary<(double, double), (Vec2 Point, double Along, double Lateral, double Clearance)>();
        var order = new List<(double, double)>();
        foreach (var poly in polys)
            foreach (var c in InternalCandidatePoints(poly, back, forward))
            {
                var key = (Py.Round(c.Point.X, 1), Py.Round(c.Point.Z, 1));
                if (!unique.ContainsKey(key)) order.Add(key);
                unique[key] = c;
            }
        var pool = order.Select(k => unique[k]).ToList();
        var occupied = existingPoints.ToList();
        while (selected.Count < wanted && pool.Count > 0)
        {
            (double Score, int Item, double Range)? best = null;
            for (int i = 0; i < pool.Count; i++)
            {
                var (p, along, lateral, clearance) = pool[i];
                double minimumDistance = occupied.Count > 0 ? occupied.Min(o => Vec2.Distance(p, o)) : 999.0;
                if (minimumDistance < minSeparation) continue;
                double range = LocalTerrainRange(sampler, p, 3.0);
                double score = 6.0 * clearance + 1.7 * minimumDistance + 0.05 * Math.Max(0.0, along) - 0.2 * lateral - 5.0 * Math.Min(range, 4.0);
                if (best is null || score > best.Value.Score) best = (score, i, range);
            }
            if (best is null) break;
            var chosen = pool[best.Value.Item];
            selected.Add((chosen.Point, chosen.Along, chosen.Lateral, best.Value.Range));
            occupied.Add(chosen.Point);
            pool = pool.Where(q => Vec2.Distance(q.Point, chosen.Point) >= minSeparation).ToList();
        }
        return selected;
    }

    /// <summary><c>_manual_placeholder_axis</c>: a back reference and forward direction that always exist.</summary>
    static (Vec2 Back, Vec2 Forward) PlaceholderAxis(JsonObject hole)
    {
        var (back, forward) = TeeGeometry.HoleTeeAxis(hole);
        if (back is Vec2 b0 && forward is Vec2 f0) return (b0, f0);
        var wpPoints = Json.Arr(hole, "waypoints").Select(w => Json.XZ(w)).Where(p => Json.Finite(p.X, p.Z)).Select(p => new Vec2(p.X, p.Z)).ToList();
        var tees = TeeGeometry.TeePoints(hole);
        Vec2 b;
        if (back is Vec2 existingBack) b = existingBack;
        else if (wpPoints.Count > 0) b = tees.Count > 0 ? TeeGeometry.MinBy(tees, p => Vec2.Distance(p, wpPoints[0])) : wpPoints[0];
        else if (tees.Count > 0) b = tees[^1];
        else b = new Vec2(0.0, 0.0);

        Vec2? target = wpPoints.Skip(1).Cast<Vec2?>().FirstOrDefault(p => Vec2.Distance(p!.Value, b) > 0.25);
        if (target is null && wpPoints.Count > 0 && Vec2.Distance(wpPoints[^1], b) > 0.25) target = wpPoints[^1];
        if (target is Vec2 t)
        {
            double length = Vec2.Distance(t, b);
            if (length > 1e-9) return (b, new Vec2((t.X - b.X) / length, (t.Z - b.Z) / length));
        }
        return (b, new Vec2(0.0, 1.0));
    }

    /// <summary><c>_manual_placeholder_point</c>.</summary>
    static (Vec2 Point, Vec2 Back, Vec2 Forward) PlaceholderPoint(JsonObject hole, double metres)
    {
        var (back, forward) = PlaceholderAxis(hole);
        return (new Vec2(back.X + metres * forward.X, back.Z + metres * forward.Z), back, forward);
    }

    /// <summary><c>_recenter_existing_back_tee</c>: slide the back tee across (never along) its owned pad to the pad's mid-line.</summary>
    static RecenteredTee? RecenterBackTee(JsonObject hole, int holeIndex, List<TeeSurface> geometries, IProgress<string>? log)
    {
        var tees = Json.Arr(hole, "tees");
        var wps = Json.Arr(hole, "waypoints");
        if (tees.Count == 0 || wps.Count == 0) return null;
        var (wx, wz) = Json.XZ(wps[0]);
        if (!Json.Finite(wx, wz)) return null;
        var wp0 = new Vec2(wx, wz);

        var indexed = new List<(double Distance, int Index, Vec2 Point)>();
        for (int i = 0; i < tees.Count; i++)
            if (TeeGeometry.TeePoint(tees[i]) is Vec2 p) indexed.Add((Vec2.Distance(p, wp0), i, p));
        if (indexed.Count == 0) return null;
        var (_, teeIndex, original) = indexed.OrderBy(e => e.Distance).ThenBy(e => e.Index).ThenBy(e => e.Point.X).ThenBy(e => e.Point.Z).First();

        var containing = geometries.Where(g => g.OwnerHole == holeIndex && Contains(g.Poly, original)).OrderBy(g => g.Area).ToList();
        if (containing.Count == 0) return null;
        var item = containing[0];
        var (_, forward) = TeeGeometry.HoleTeeAxis(hole);
        if (TeeGeometry.CrosswiseCenter(item.Poly, original, forward) is not var (target, _)) return null;

        const string mode = "crosswise-center-preserve-station";
        double distance = Vec2.Distance(target, original);
        var tee = (JsonObject)tees[teeIndex]!;
        var position = Json.EnsureObject(tee, "position");
        position["x"] = Json.F(target.X);
        position["z"] = Json.F(target.Z);
        double y = Json.Num(position["y"], 0.0);   // float(y or 0.0)
        if (double.IsNaN(y)) position["y"] = Json.F(0.0);
        else if (double.IsFinite(y)) position["y"] = Json.F(y);
        if (distance >= 0.1)
            log?.Report($"Hole {holeIndex}: centered existing back tee on tee spline {item.Index} ({mode}, moved {Py.R(distance, 2)} m).");
        return new RecenteredTee(holeIndex, distance >= 0.1, distance, mode, item.Index, original, target);
    }
}
