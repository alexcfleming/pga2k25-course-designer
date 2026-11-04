using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pga2K25.HoleSim;

/// <summary>
/// An in-game height: the MEASURE tool's reading at <see cref="Where"/>, relative to <see cref="Reference"/> (usually the
/// green centre). <see cref="HeldOut"/> readings aren't used to fit a correction, so they test it.
/// </summary>
public sealed record HeightReading(string Spot, FramePoint Where, FramePoint Reference, double Feet, string Label, bool HeldOut = false, string Source = "MEASURED")
{
    public double Inches => Feet * 12;
}

/// <summary>A place to take a reading, named for the in-game task list.</summary>
public sealed record MeasureSpot(string Name, FramePoint Where, int FromTeeYards, double ModelFt, bool HeldOut)
{
    /// <summary>Straight-line yards to the green point (the route's last waypoint, which the game's TO GREEN uses).</summary>
    public int ToGreenYards(HoleModel hole) => (int)Math.Round(Where.DistanceTo(hole.GreenFrame));
}

public static partial class HeightLabel
{
    /// <summary>
    /// Reads an in-game height label: <c>+1'4"</c> is 16 in (not 14), <c>+1'</c> is 12 in, <c>-0'7</c> is −7 in, the game's
    /// ▲ and ▼ read as + and −, a missing sign reads as above, and a plain number is feet (<c>-5.3</c>). Inches of 12 or more are refused. Same rules as the
    /// Studio's Measurements page.
    /// </summary>
    public static bool TryParse(string? text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        // The game shows ▲ for above and ▼ for below instead of a sign.
        var s = text.Trim().Replace('’', '\'').Replace('′', '\'').Replace('”', '"').Replace('″', '"')
            .Replace("▲", "+").Replace("△", "+").Replace("▼", "-").Replace("▽", "-");
        var m = Pattern().Match(s.Trim());
        if (!m.Success) return false;
        double whole = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double inches = m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (inches >= 12) return false;
        feet = (whole + inches / 12) * (m.Groups[1].Value is "-" or "−" ? -1 : 1);
        return true;
    }

    /// <summary>The parsed value said back, so a misread label is caught: "+1 ft 4 in (16 in)".</summary>
    public static string Describe(double feet)
    {
        double inches = Math.Round(Math.Abs(feet) * 12, 1);
        int ft = (int)Math.Floor(inches / 12); double rest = Math.Round(inches - ft * 12, 1);
        string sign = feet < 0 ? "−" : "+";
        string body = ft > 0 ? $"{ft} ft {rest:0.#} in" : $"{rest:0.#} in";
        return $"{sign}{body} ({sign}{inches:0.#} in)";
    }

    [GeneratedRegex("""^([+\-−]?)\s*(\d+(?:\.\d+)?)\s*(?:'|ft)?\s*(?:(\d+(?:\.\d+)?)\s*(?:"|in)?)?$""", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}

public enum GateBand { Trustworthy, Indicative, Check }

/// <summary>One reading compared with a model.</summary>
public sealed record ReadingCheck(HeightReading Reading, double ModelFt, double ErrorInches);

/// <summary>
/// Alex's fit bands: RMS error under 10% of the readings' elevation range is Trustworthy (all results), 10–18% is
/// Indicative (results carry a warning), over 18% or too few readings is Check (roll and sightlines off by default).
/// </summary>
public sealed record GateResult(string ModelId, string ModelName, IReadOnlyList<ReadingCheck> Checks, double RmsInches, double RangeInches,
    double? HeldOutRmsInches, GateBand Band, string Reason)
{
    public double Percent => RangeInches > 0 ? RmsInches / RangeInches * 100 : double.PositiveInfinity;
    public string BandText => Band switch { GateBand.Trustworthy => "Trustworthy", GateBand.Indicative => "Indicative", _ => "Check" };
}

public static class TerrainGate
{
    public const int MinReadings = 5;
    public const double TrustworthyPct = 10, IndicativePct = 18;

    /// <summary>
    /// Compares <paramref name="model"/> with the readings. A fitted model is judged on its held-out readings only, since
    /// it was built to match the rest; an unfitted one on all of them.
    /// </summary>
    public static GateResult Evaluate(ITerrainModel model, IReadOnlyList<HeightReading> readings, bool fitted = false)
    {
        var checks = readings.Select(r =>
        {
            double modelFt = model.HeightFt(r.Where) - model.HeightFt(r.Reference);
            return new ReadingCheck(r, modelFt, (modelFt - r.Feet) * 12);
        }).ToList();
        var judged = fitted ? checks.Where(c => c.Reading.HeldOut).ToList() : checks;
        double rms = judged.Count == 0 ? double.NaN : Math.Sqrt(judged.Average(c => c.ErrorInches * c.ErrorInches));
        var heights = readings.Select(r => r.Inches).Append(0).ToList();     // the zero reference counts toward the range
        double range = heights.Max() - heights.Min();
        var held = checks.Where(c => c.Reading.HeldOut).ToList();
        double? heldRms = held.Count == 0 ? null : Math.Sqrt(held.Average(c => c.ErrorInches * c.ErrorInches));

        GateBand band; string reason;
        int needed = fitted ? 2 : MinReadings;
        if (judged.Count < needed)
        {
            band = GateBand.Check;
            reason = fitted ? $"Only {judged.Count} held-out reading{(judged.Count == 1 ? "" : "s")} to test the fit; at least 2 are needed."
                            : $"Only {readings.Count} reading{(readings.Count == 1 ? "" : "s")}; at least {MinReadings} are needed.";
        }
        else if (range < 6)
        {
            band = GateBand.Check;
            reason = $"The readings span only {range:0} in, too flat to judge the error as a share of the range.";
        }
        else
        {
            double pct = rms / range * 100;
            band = pct < TrustworthyPct ? GateBand.Trustworthy : pct <= IndicativePct ? GateBand.Indicative : GateBand.Check;
            reason = $"RMS {rms:0.#} in = {pct:0}% of the {range:0} in range" + (fitted ? " (held-out readings only)." : ".");
        }
        return new GateResult(model.Id, model.Name, checks, rms, range, heldRms, band, reason);
    }

    /// <summary>
    /// Suggested spots for MEASURE readings: along the line of play (every 50 yds on a long hole, closer on a short one) from the tee to
    /// the green, alternating a little left and right of the line so slope across the hole shows, plus the green's front
    /// and back. Every fourth spot is held back to test a fitted correction.
    /// </summary>
    public static IReadOnlyList<MeasureSpot> SuggestSpots(HoleModel hole, ITerrainModel? model = null, int? spacing = null)
    {
        var spots = new List<FramePoint>();
        double total = 0; var segs = new List<(FramePoint A, FramePoint B, double Len)>();
        for (int i = 0; i + 1 < hole.Route.Count; i++) { double l = hole.Route[i].DistanceTo(hole.Route[i + 1]); segs.Add((hole.Route[i], hole.Route[i + 1], l)); total += l; }
        // About eight spots on the line whatever the length: every 50 yds on a long hole, closer on a par 3.
        double spacingYards = spacing ?? Math.Clamp(total / 9, 15, 50);
        int n = 0;
        for (double s = spacingYards; s < total - 25; s += spacingYards, n++)
        {
            double run = s;
            foreach (var (a, b, len) in segs)
            {
                if (run > len) { run -= len; continue; }
                var dir = (b - a) * (1 / len); var right = new FramePoint(dir.Along, -dir.Lat);
                spots.Add(a + dir * run + right * (n % 2 == 0 ? -10 : 10));
                break;
            }
        }
        // The green's front and back on the line through its anchor (the anchor isn't always the middle), 4 yds in.
        var g = hole.GreenFrame;
        double front = g.Along, back = g.Along;
        for (double a = g.Along; a > g.Along - 80 && hole.Grid.At(new FramePoint(g.Lat, a)) == Ground.Green; a -= 0.5) front = a;
        for (double a = g.Along; a < g.Along + 80 && hole.Grid.At(new FramePoint(g.Lat, a)) == Ground.Green; a += 0.5) back = a;
        if (back - front >= 12) { spots.Add(new FramePoint(g.Lat, front + 4)); spots.Add(new FramePoint(g.Lat, back - 4)); }
        else spots.Add(new FramePoint(g.Lat, (front + back) / 2 - 3));
        var list = spots.Select((p, i) => new MeasureSpot($"M{i + 1}", p, (int)Math.Round(p.Length),
            model is null ? double.NaN : model.HeightFt(p) - model.HeightFt(g), i % 4 == 2)).ToList();
        // G: the green's height in the game's tee table (GREEN HEIGHT for the chosen tee), no cursor needed.
        list.Insert(0, new MeasureSpot("G", g, (int)Math.Round(g.Length), model is null ? double.NaN : 0, false));
        return list;
    }
}

/// <summary>
/// Readings and quick checks saved per course and hole (in a "Hole Sim" folder beside the products), so a later
/// session can reuse them as PRIOR values. Spots are stored in world metres so another tee doesn't move them.
/// </summary>
public sealed class LiveChecks
{
    public sealed record SavedReading(string Spot, double X, double Z, double RefX, double RefZ, double Feet, string Label, bool HeldOut, string Reference, int Pin, DateTime TakenAt);
    public sealed record Drive(double? Carry, double? Total, string Side, DateTime TakenAt);

    public int Version { get; set; } = 1;
    public int Hole { get; set; }
    public string FileSha256 { get; set; } = "";
    public List<SavedReading> Readings { get; set; } = [];
    public string? Sightline { get; set; }
    public string? SightlineTee { get; set; }
    public List<Drive> Drives { get; set; } = [];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string outputDir, int hole) => Path.Combine(outputDir, "Hole Sim", $"H{hole:00} live checks.json");

    public static LiveChecks Load(string path, int hole)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<LiveChecks>(File.ReadAllText(path), Json) ?? new LiveChecks { Hole = hole }; }
        catch (Exception) { }
        return new LiveChecks { Hole = hole };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public IReadOnlyList<HeightReading> ToReadings(HoleModel hole) => Readings.Select(r => new HeightReading(
        r.Spot, hole.Frame.ToFrame(new CourseFormat.Vec2(r.X, r.Z)), hole.Frame.ToFrame(new CourseFormat.Vec2(r.RefX, r.RefZ)),
        r.Feet, r.Label, r.HeldOut)).ToList();
}
