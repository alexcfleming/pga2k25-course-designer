using Pga2K25.CourseFormat;

namespace Pga2K25.TerrainMap;

/// <summary>
/// One body of inland water a hole plays over or beside, measured along its line of play from the back tee.
/// <see cref="Crosses"/>: front and carry are where the line enters and leaves the water. Otherwise
/// <see cref="FrontYards"/> is where the water comes closest, <see cref="OffsetYards"/> away on <see cref="Side"/>.
/// </summary>
public sealed record HoleWaterRow(int Body, double LevelFeet, bool Crosses, int FrontYards, int CarryYards, int ToGreenYards,
    int OffsetYards, string Side);

public static class HoleWater
{
    /// <summary>Water closer than this to the line of play (m) is listed as alongside.</summary>
    public const double AlongsideMetres = 30;

    /// <summary>The survey's ponds and creeks along a hole's line of play, nearest the tee first.</summary>
    public static List<HoleWaterRow> Along(TerrainSurvey s, HoleRouting h)
    {
        var rows = new List<HoleWaterRow>();
        if (s.InlandWater is not { } water) return rows;
        var route = h.Route;
        double total = 0;
        for (int i = 1; i < route.Count; i++) total += Vec2.Distance(route[i - 1], route[i]);
        double Feet(int body) => (water.Bodies[body].Level - s.DatumMetres) * TerrainSurvey.FeetPerMetre;
        int Yd(double m) => (int)Math.Round(m * TerrainSurvey.YardsPerMetre);

        // Crossings: walk the line in half-metre steps.
        var crossed = new HashSet<int>();
        int current = -1;
        double entered = 0, run = 0;
        for (int i = 1; i < route.Count; i++)
        {
            var a = route[i - 1];
            var b = route[i];
            double len = Vec2.Distance(a, b);
            for (double t = 0; t < len; t += 0.5)
            {
                int body = water.BodyAt(a + (b - a) * (t / len));
                double at = run + t;
                if (body != current)
                {
                    if (current >= 0) Add(current, entered, at);
                    current = body;
                    entered = at;
                }
            }
            run += len;
        }
        if (current >= 0) Add(current, entered, run);

        void Add(int body, double from, double to)
        {
            // A creek crossed twice (in, out, in again) is one hazard from its first bank to its last.
            int i = rows.FindIndex(r => r.Body == body && r.Crosses);
            if (i >= 0) { rows[i] = rows[i] with { CarryYards = Yd(to), ToGreenYards = Yd(total - to) }; return; }
            crossed.Add(body);
            rows.Add(new HoleWaterRow(body, Feet(body), true, Yd(from), Yd(to), Yd(total - to), 0, ""));
        }

        // Alongside: bodies not crossed whose shore comes within reach of the line.
        for (int body = 0; body < water.Bodies.Count; body++)
        {
            if (crossed.Contains(body)) continue;
            var best = (D: double.MaxValue, Along: 0.0, Right: false);
            foreach (var p in water.Bodies[body].Outline)
            {
                double along = 0;
                for (int i = 1; i < route.Count; i++)
                {
                    var a = route[i - 1];
                    var ab = route[i] - a;
                    double len = ab.Length;
                    double t = len > 0 ? Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / (len * len), 0, 1) : 0;
                    var q = a + ab * t;
                    double d = Vec2.Distance(p, q);
                    // Right-hand side of a player facing along ab (north = −Z, east = −X): (ab.Z, −ab.X).
                    if (d < best.D) best = (d, along + t * len, (p.X - q.X) * ab.Z - (p.Z - q.Z) * ab.X > 0);
                    along += len;
                }
            }
            if (best.D <= AlongsideMetres)
                rows.Add(new HoleWaterRow(body, Feet(body), false, Yd(best.Along), 0, Yd(total - best.Along), Yd(best.D), best.Right ? "right" : "left"));
        }
        return rows.OrderBy(r => r.FrontYards).ToList();
    }
}
