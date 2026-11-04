using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.HoleSim;

/// <summary>One terrain candidate on one hole.</summary>
/// <param name="Readings">How many readings were compared.</param>
/// <param name="TestedOn">"all markers", or for a fitted candidate "N markers it didn't see".</param>
public sealed record StudyRow(int Hole, string Candidate, int Readings, string TestedOn, double RmsInches, double RangeInches, double Percent, GateBand Band,
    double SlopeP50, double SlopeP90, double SlopeP99, double SteepShare);

/// <summary>
/// Compares terrain candidates against in-game heights on a few holes (the continuation prompt's section 6): the plain
/// replay, the replay smoothed outside greens, and each of those with a correction fitted to a realistic handful of
/// readings (the markers nearest the suggested MEASURE spots) and tested on every other marker. It reports the gate band
/// and the fairway slope spread the model implies. It never changes <see cref="TerrainReplay"/> itself.
/// </summary>
public static class TerrainStudy
{
    /// <summary>
    /// Readings from the course's distance markers, whose height is the game's ground (Sangoma): markers within
    /// <paramref name="reachYards"/> of the line of play, relative to the marker nearest the green centre.
    /// </summary>
    public static IReadOnlyList<HeightReading> MarkerReadings(CourseModel course, HoleModel hole, double reachYards = 40)
    {
        var near = course.DistanceMarkers
            .Select(m => (m, f: hole.Frame.ToFrame(m.Position)))
            .Where(t => t.f.Along > -10 && t.f.Along < hole.GreenFrame.Along + 25 && Polyline.Nearest(hole.Route, t.f).Distance <= reachYards)
            .ToList();
        if (near.Count == 0) return [];
        var zero = near.OrderBy(t => t.f.DistanceTo(hole.GreenFrame)).First();
        return near.Where(t => !ReferenceEquals(t.m, zero.m))
            .Select((t, i) => new HeightReading($"D{i + 1}", t.f, zero.f, (t.m.Height - zero.m.Height) / HoleFrame.Foot,
                $"{(t.m.Height - zero.m.Height) / HoleFrame.Foot:+0.0;-0.0} ft", Source: "distance marker"))
            .ToList();
    }

    public static IReadOnlyList<StudyRow> Run(CourseModel course, JsonObject? desc, IEnumerable<int> holes, double smoothingMetres = 8, int routineReadings = 10, double fitLengthYards = 25)
    {
        var layers = CourseLayers.From(course);
        var water = HoleModelBuilder.TraceWater(course);
        var rows = new List<StudyRow>();
        foreach (int number in holes)
        {
            var hole = HoleModelBuilder.Build(course, desc, number, new HoleModelOptions { Water = water }, null, layers);
            var all = MarkerReadings(course, hole);
            if (all.Count == 0) { rows.Add(new StudyRow(number, "no distance markers near the line", 0, "", double.NaN, double.NaN, double.NaN, GateBand.Check, double.NaN, double.NaN, double.NaN, double.NaN)); continue; }
            var field = ReplayTerrain.BuildField(course, hole);
            var replay = ReplayTerrain.FromField(hole, field);
            var smooth = ReplayTerrain.Smoothed(hole, field, smoothingMetres);

            // A realistic session: the markers nearest the suggested spots stand in for MEASURE readings.
            var spots = TerrainGate.SuggestSpots(hole);
            var picked = spots.Select(s => all.OrderBy(r => r.Where.DistanceTo(s.Where)).First()).Distinct().Take(routineReadings).ToList();
            var rest = all.Except(picked).Select(r => r with { HeldOut = true }).ToList();

            foreach (var model in new ITerrainModel[] { replay, smooth })
            {
                rows.Add(Row(number, model.Name, TerrainGate.Evaluate(model, all), all.Count, "all markers", hole, model));
                var fitted = new CorrectedTerrain(model, picked, fitLengthYards);
                var gate = TerrainGate.Evaluate(fitted, picked.Concat(rest).ToList(), fitted: true);
                rows.Add(Row(number, fitted.Name + $" ({picked.Count})", gate, rest.Count, $"{rest.Count} markers it didn't see", hole, fitted));
            }
        }
        return rows;
    }

    static StudyRow Row(int hole, string name, GateResult g, int n, string testedOn, HoleModel m, ITerrainModel model)
    {
        var (p50, p90, p99, steep) = FairwaySlopes(m, model);
        return new StudyRow(hole, name, n, testedOn, g.RmsInches, g.RangeInches, g.Percent, g.Band, p50, p90, p99, steep);
    }

    /// <summary>Slope (%) over this hole's fairway cells near the line: p50, p90, p99 and the share steeper than 10%.</summary>
    public static (double P50, double P90, double P99, double Over10) FairwaySlopes(HoleModel hole, ITerrainModel model)
    {
        var s = new List<double>();
        var g = hole.Grid;
        for (int y = 0; y < g.Height; y += 2)
            for (int x = 0; x < g.Width; x += 2)
            {
                if (g[x, y] != Ground.Fairway) continue;
                var c = g.CellCentre(x, y);
                if (c.Along < 0 || c.Along > hole.GreenFrame.Along || Polyline.Nearest(hole.Route, c).Distance > 60) continue;
                s.Add(model.Slope(c).Length * 100);
            }
        if (s.Count == 0) return (double.NaN, double.NaN, double.NaN, double.NaN);
        s.Sort();
        double Q(double q) => s[Math.Clamp((int)Math.Round(q * (s.Count - 1)), 0, s.Count - 1)];
        return (Q(0.5), Q(0.9), Q(0.99), s.Count(v => v > 10) / (double)s.Count);
    }
}
