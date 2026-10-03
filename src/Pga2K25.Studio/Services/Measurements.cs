using System.Globalization;

namespace Pga2K25.Studio.Services;

/// <summary>An in-game measure-tool reading along a route leg: tee → aim or aim → green.</summary>
public sealed record MeasuredLeg(int Hole, bool TeeToAim, int Yards, double Feet);

/// <summary>Readings kept beside a course's products as "Measurements.csv" (hole,leg,yards,feet; '#' comments).</summary>
public static class Measurements
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
}
