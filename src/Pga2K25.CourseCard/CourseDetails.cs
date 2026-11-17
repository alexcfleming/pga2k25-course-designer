using System.Globalization;
using Pga2K25.CourseFormat;

namespace Pga2K25.CourseCard;

/// <param name="Note">A short second line in a fainter face, or null.</param>
public sealed record DetailRow(string Label, string Value, string? Note = null);

/// <summary>
/// The Course Details Card: what a player sees in the game's course details panel before a round (par, holes, length,
/// difficulty, green speed, pins, conditions, time, weather), from the course file, plus the settings that go with it
/// (tee colours, playoff holes, tee markers, pars set by hand). The game's Fairways and Greens ratings and its Slope and
/// Handicap figures aren't stored in a course file before it's played, so they're left out.
/// </summary>
public static class CourseDetails
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The panel's figures, top to bottom, in two groups: as players see them, and the settings behind them.</summary>
    public static (IReadOnlyList<DetailRow> Players, IReadOnlyList<DetailRow> Settings) For(CourseModel course, CourseCardData data, int? windFromDegrees)
    {
        var stored = course.Stored;
        int Count(int par) => data.Holes.Count(h => h.Par == par);
        var players = new List<DetailRow>
        {
            new("Par", data.Total.Par.ToString(Inv), $"{Count(3)} par 3s · {Count(4)} par 4s · {Count(5)} par 5s · out {data.Out.Par}, in {data.In.Par}"),
            new("Holes", data.Holes.Count.ToString(Inv)),
            new("Length", $"{data.Total.Yards[^1]:N0} yds", data.TeeNames.Count > 1
                ? string.Join(" · ", Enumerable.Range(0, data.TeeNames.Count).Reverse().Select(i => $"{data.TeeNames[i]} {data.Total.Yards[i]:N0}"))
                : $"{data.TeeNames[0]} tees"),
            new("Difficulty", stored?.DifficultyName ?? "Not rated", stored?.DifficultyName is null ? "the game hasn't rated this copy of the course" : "the game's rating"),
            GreenSpeed(course.GreenSpeed),
            new("Pins", stored is { PinSets: > 0 } ? $"{stored.PinSets} pin set{(stored.PinSets == 1 ? "" : "s")}" : "Not saved", null),
        };
        if (windFromDegrees is int w)
            players.Add(new("Wind", $"From the {WindRose.SectorNames[WindRose.SectorOf(w)]}", "prevailing, from the wind rose or set by hand; the game sets wind each round"));
        if (course.Atmosphere is { } a)
            players.Add(new("Time of day", a.Clock, Weather(a)));
        players.Add(new("Theme", course.Look.ThemeLabel, course.Look.BackdropLabel is { Length: > 0 } b ? b : null));

        var settings = new List<DetailRow>();
        var c = course.Conditions;
        string Slider(double v) => v < 0 ? "default" : $"{v * 100:0}";
        settings.Add(new("Firmness", $"greens {Slider(c.GreenFirmness)} · fairways {Slider(c.FairwayFirmness)} · terrain {Slider(c.TerrainFirmness)}",
            $"rough {Slider(c.RoughFirmness)} · heavy rough {Slider(c.HeavyRoughFirmness)} · bunkers {Slider(c.BunkerFirmness)}  (sliders 0–100)"));
        settings.Add(new("Speed", $"fairways {Slider(c.FairwaySpeed)} · bunkers {Slider(c.BunkerSpeed)}", "sliders 0–100; the green speed is above"));
        if (stored is not null)
        {
            string playoff = stored.SuddenDeathHoles.Count > 0 ? string.Join(", ", stored.SuddenDeathHoles) : "none";
            settings.Add(new("Playoff holes", $"sudden death {playoff}",
                stored.PlayoffsAreDefault ? "the game's default for a new course" : PlayoffNote(stored)));
            var colourNames = stored.TeeColours.Select(StoredScorecard.TeeColourName).ToList();
            if (colourNames.Count > 0)
                settings.Add(new("Tee colours", string.Join(", ", colourNames.Select(n => n ?? "?")), "front to back, as the game saved them"));
        }
        if (course.TeeMarkerWidthName is { } marker)
            settings.Add(new("Tee markers", marker, $"{course.TeeMarkerWidthMetres * 3.2808399:0} ft apart"));
        var hand = course.Holes.Where(h => h.CreatorPar is not null).Select(h => h.Number).ToList();
        settings.Add(new("Par set by hand", hand.Count == 0 ? "none" : string.Join(", ", hand),
            hand.Count == 0 ? "every hole takes the game's par for its length" : "the other holes take the game's par for their length"));
        return (players, settings);
    }

    static DetailRow GreenSpeed(double setting)
    {
        // The same scale as Publish readiness: two in-game readings.
        if (Readiness.ReadinessChecker.Stimp(setting) is not double stimp) return new("Green speed", "Game default", "Stimp shown in the game");
        return new("Green speed", $"Stimp {stimp:0.0}", $"slider {setting * 100:0}; Stimp from two in-game readings");
    }

    static string Weather(Atmosphere a)
    {
        var parts = new List<string>();
        // Cloud types are stored as "cumulus_02": the kind is the part before the number.
        if (a.Cloud1 is { } c) parts.Add($"{c.Type.Split('_')[0].ToLowerInvariant()} cloud");
        if (a.Rain) parts.Add("rain");
        if (a.FogDensity > 0.01) parts.Add("fog");
        if (a.Haze > 0.01) parts.Add("haze");
        return parts.Count == 0 ? "clear" : string.Join(", ", parts);
    }

    static string PlayoffNote(StoredScorecard s)
    {
        var parts = new List<string>();
        if (s.AggregateHoles.Count > 0) parts.Add($"aggregate {string.Join(", ", s.AggregateHoles)}");
        if (s.PlayoffFront9 is int f) parts.Add($"front nine {f}");
        if (s.PlayoffBack9 is int b) parts.Add($"back nine {b}");
        if (s.PlayoffFull is int full) parts.Add($"full round {full}");
        return parts.Count == 0 ? "chosen in the designer" : string.Join(" · ", parts);
    }
}
