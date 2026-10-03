using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pga2K25.Studio.Services;

/// <summary>An in-game measure-tool reading along a route leg: tee → aim or aim → green.</summary>
public sealed record MeasuredLeg(int Hole, bool TeeToAim, int Yards, double Feet);

/// <summary>
/// Readings kept beside a course's products as "Measurements.csv" (hole,leg,yards,feet; '#' comments), the same
/// format as samples/calibration/route-heights. Never written into the course file.
/// </summary>
public static partial class Measurements
{
    public static string PathFor(StudioSettings settings, string courseName) =>
        Path.Combine(Products.OutputDir(settings, courseName), "Measurements.csv");

    public static List<MeasuredLeg> Load(string path)
    {
        var legs = new List<MeasuredLeg>();
        if (!File.Exists(path)) return legs;
        foreach (var line in File.ReadAllLines(path))
        {
            var f = line.Split(',');
            if (f.Length < 4 || !int.TryParse(f[0], out int hole)) continue;
            if (!int.TryParse(f[2], out int yards) || !double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double feet)) continue;
            legs.Add(new MeasuredLeg(hole, f[1].Trim() == "tee-aim", yards, feet));
        }
        return legs;
    }

    public static void Save(string path, IEnumerable<MeasuredLeg> legs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine("# In-game measure tool, clicked on the route markers (back tee square, aim-point marker, green circle); feet = second point minus first.");
        sb.AppendLine("hole,leg,yards,feet");
        foreach (var m in legs.OrderBy(m => m.Hole).ThenBy(m => m.TeeToAim ? 0 : 1))
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{m.Hole},{(m.TeeToAim ? "tee-aim" : "aim-green")},{m.Yards},{m.Feet:0.00}"));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Feet as the measure tool shows them or as typed: <c>+13'6"</c>, <c>-0'7</c>, <c>13 ft 6 in</c>, <c>-5.33</c>.
    /// A lone number is feet.
    /// </summary>
    public static bool TryParseFeet(string? text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = FeetPattern().Match(text.Trim());
        if (!m.Success) return false;
        double whole = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double inches = m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (inches >= 12) return false;
        feet = (whole + inches / 12) * (m.Groups[1].Value is "-" or "−" ? -1 : 1);
        return true;
    }

    [GeneratedRegex("""^([+\-−]?)\s*(\d+(?:\.\d+)?)\s*(?:'|ft)?\s*(?:(\d+(?:\.\d+)?)\s*(?:"|in)?)?$""", RegexOptions.IgnoreCase)]
    private static partial Regex FeetPattern();
}
