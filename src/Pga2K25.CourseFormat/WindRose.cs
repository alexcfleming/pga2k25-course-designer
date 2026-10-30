using System.Text.Json;

namespace Pga2K25.CourseFormat;

/// <summary>
/// How often the wind blows FROM each of 16 directions, split into speed bands, plus the share of calm hours.
/// Either binned from an hourly record (a real place) or drawn by <see cref="WindClimate"/> (an invented one).
/// Saved inside Place.json. Shares are of all hours, so the sectors and calm add up to 1.
/// </summary>
public sealed class WindRose
{
    /// <summary>Band edges in mph: calm below 3, then 3–8, 8–13, 13–19, 19–25 and 25 and up.</summary>
    public static readonly double[] Edges = [3, 8, 13, 19, 25];
    public static readonly string[] BandLabels = ["3–8", "8–13", "13–19", "19–25", "25+"];
    public static readonly string[] SectorNames = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];

    /// <summary>"model" for the climate model, "data" for an hourly record.</summary>
    public string Kind { get; set; } = "model";

    /// <summary>Where it came from, printed under the rose, e.g. "ERA5 reanalysis via Open-Meteo (CC BY 4.0), 2015–2024, 10 m".</summary>
    public string Source { get; set; } = "";

    /// <summary>Share of hours under 3 mph.</summary>
    public double Calm { get; set; }

    /// <summary>16 sectors from north clockwise, each 5 band shares (see <see cref="Edges"/>).</summary>
    public List<List<double>> Sectors { get; set; } = [];

    public double? MeanMph { get; set; }
    public int? Hours { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>When the record was fetched (UTC), for a real place.</summary>
    public DateTime? Fetched { get; set; }

    public bool IsData => Kind.Equals("data", StringComparison.OrdinalIgnoreCase);

    /// <summary>Share of hours from sector <paramref name="i"/>, all speeds.</summary>
    public double SectorTotal(int i) => Sectors[i].Sum();

    /// <summary>The sector the wind blows from most often, as a bearing (degrees from north).</summary>
    public int PrevailingFrom => (int)Math.Round(Enumerable.Range(0, 16).MaxBy(SectorTotal) * 22.5) % 360;

    /// <summary>Share of hours at 8 mph and up, the breezy hours a golfer notices.</summary>
    public double Breezy => Sectors.Sum(s => s.Skip(1).Sum());

    public static int SectorOf(double fromDegrees) => (int)(((fromDegrees % 360 + 360 + 11.25) % 360) / 22.5) % 16;

    /// <summary>Bins hourly speeds (mph) and FROM directions (degrees) into a rose; hours with either missing are skipped.</summary>
    public static WindRose FromHours(IReadOnlyList<double?> speedsMph, IReadOnlyList<double?> fromDegrees)
    {
        var grid = Enumerable.Range(0, 16).Select(_ => new double[5]).ToArray();
        int n = 0, calm = 0;
        double sum = 0;
        for (int i = 0; i < Math.Min(speedsMph.Count, fromDegrees.Count); i++)
        {
            if (speedsMph[i] is not double s || fromDegrees[i] is not double d) continue;
            n++; sum += s;
            if (s < Edges[0]) { calm++; continue; }
            int band = Edges.Count(e => s >= e) - 1;
            grid[SectorOf(d)][band]++;
        }
        if (n == 0) throw new InvalidDataException("The wind record has no usable hours.");
        return new WindRose
        {
            Kind = "data",
            Calm = (double)calm / n,
            Sectors = grid.Select(row => row.Select(c => c / n).ToList()).ToList(),
            MeanMph = sum / n,
            Hours = n,
        };
    }

    /// <summary>A rose from an Open-Meteo archive response (hourly wind_speed_10m in mph and wind_direction_10m).</summary>
    public static WindRose FromOpenMeteo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("hourly", out var hourly))
            throw new InvalidDataException(doc.RootElement.TryGetProperty("reason", out var why) ? why.GetString() : "The response has no hourly wind.");
        static List<double?> Column(JsonElement h, string name) =>
            h.GetProperty(name).EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToList();
        var rose = FromHours(Column(hourly, "wind_speed_10m"), Column(hourly, "wind_direction_10m"));
        var times = hourly.TryGetProperty("time", out var t) ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
        string span = times.Count > 0 ? $"{times[0][..4]}–{times[^1][..4]}, " : "";
        rose.Source = $"ERA5 reanalysis via Open-Meteo (CC BY 4.0), {span}10 m";
        return rose;
    }
}
