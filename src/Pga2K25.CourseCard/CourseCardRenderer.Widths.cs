using Pga2K25.Readiness;
using SkiaSharp;

namespace Pga2K25.CourseCard;

// Fairway width at 3-wood and driver distance, for the hole data card: the figures the readiness width check judges.
public sealed partial class CourseCardRenderer
{
    /// <summary>Each par 4 and 5's fairway widths, by hole number; empty leaves the two width columns off the card.</summary>
    public IReadOnlyDictionary<int, HoleFairwayWidths> FairwayWidths { get; init; } = new Dictionary<int, HoleFairwayWidths>();

    /// <summary>The ranges and distances the widths are shown against (the course's readiness limits).</summary>
    public ReadinessLimits WidthLimits { get; init; } = new();

    /// <summary>What a width cell prints, and how: plain inside the range, flagged outside it, quiet when there is nothing to measure.</summary>
    public enum WidthTone { Plain, Flag, Quiet }

    /// <summary>
    /// One width cell: the narrowest to the widest of the distance's stations ("28–31", or "30" when they agree),
    /// "none" where a built hole has no fairway by the line, "not built" where the hole has no fairway spline, and
    /// "–" where the hole is too short to have a station there.
    /// </summary>
    public static (string Text, WidthTone Tone) WidthCell(HoleFairwayWidths w, IReadOnlyList<StationWidth> stations, double min, double max)
    {
        if (stations.Count == 0) return ("–", WidthTone.Quiet);
        if (!w.Built) return ("not built", WidthTone.Quiet);
        var yards = stations.Select(s => s.WidthYards is double v ? (int)Math.Round(v) : (int?)null).ToList();
        var have = yards.Where(v => v is not null).Select(v => v!.Value).ToList();
        bool gap = have.Count < yards.Count;
        if (have.Count == 0) return ("none", WidthTone.Flag);
        string text = gap ? $"none–{have.Max()}" : have.Min() == have.Max() ? $"{have.Min()}" : $"{have.Min()}–{have.Max()}";
        bool outside = gap || stations.Any(s => s.WidthYards < min || s.WidthYards > max);
        return (text, outside ? WidthTone.Flag : WidthTone.Plain);
    }

    /// <summary>The hole data card's two footnote lines for the width columns.</summary>
    (string, string) WidthNote()
    {
        var L = WidthLimits;
        return ($"Width: yards across the built fairway, narrowest to widest of three stations from the back tee.  " +
                $"3W: at 3-wood distance, {L.ThreeWoodMinYards:0}–{L.ThreeWoodMaxYards:0} yds ({L.ThreeWoodWidthMinYards:0}–{L.ThreeWoodWidthMaxYards:0} wide wanted)  ·  " +
                $"Dr: at driver distance, {L.DesignDriveMinYards:0}–{L.DesignDriveMaxYards:0} yds ({L.DriverWidthMinYards:0}–{L.DriverWidthMaxYards:0} wide wanted)",
                "red: outside its range, or no fairway by the line at a station (none)  ·  not built: no fairway spline by the hole's line to measure yet  ·  –: the hole is too short to measure there");
    }
}
