using System.Globalization;
using System.Reflection;

namespace Pga2K25.HoleSim;

/// <summary>Lies a strokes-gained baseline is tabled by.</summary>
public enum Lie { Tee, Fairway, Rough, Sand, Recovery, Green }

/// <summary>Whether a distance fell inside the published rows for its lie.</summary>
public enum Coverage { Covered, BelowTable, AboveTable, NoData }

/// <summary>Expected strokes to hole out. When <see cref="Coverage"/> isn't Covered the value is the nearest table end, not an extrapolation.</summary>
public readonly record struct Expected(double Value, Coverage Coverage)
{
    public bool Covered => Coverage == Coverage.Covered;
}

/// <summary>
/// Expected strokes to hole out by lie and distance (yards off the green, feet on it). Every computed value stores
/// the baseline id it came from; values from different baselines are never compared.
/// </summary>
public interface IBaseline
{
    string Id { get; }
    string Name { get; }
    string Source { get; }

    /// <summary>Estimated baselines (e.g. a scaled handicap table) carry this flag and document their method.</summary>
    bool Estimated { get; }

    /// <summary>True when the values are quoted from secondary sources rather than the primary table.</summary>
    bool Secondary { get; }

    Expected ExpectedStrokes(Lie lie, double distance);

    /// <summary>Local reversals and other oddities in the table, reported rather than smoothed away.</summary>
    IReadOnlyList<string> Anomalies { get; }
}

/// <summary>A baseline read from a versioned CSV with provenance columns, interpolated linearly between published points.</summary>
public sealed class TableBaseline : IBaseline
{
    public sealed record Row(Lie Lie, double Distance, string Unit, double Expected, string Source, string SourceType, string Retrieved);

    readonly Dictionary<Lie, Row[]> _rows;

    public string Id { get; }
    public string Name { get; }
    public string Source { get; }
    public bool Estimated { get; }
    public bool Secondary { get; }
    public string Note { get; }
    public IReadOnlyList<string> Anomalies { get; }
    public IReadOnlyList<Row> Rows => _rows.Values.SelectMany(r => r).ToList();

    TableBaseline(string id, string name, string note, bool estimated, List<Row> rows)
    {
        Id = id; Name = name; Note = note; Estimated = estimated;
        _rows = rows.GroupBy(r => r.Lie).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Distance).ToArray());
        Secondary = rows.Any(r => r.SourceType.Equals("secondary", StringComparison.OrdinalIgnoreCase));
        Source = string.Join("; ", rows.Select(r => r.Source).Distinct());
        var anomalies = new List<string>();
        foreach (var (lie, rs) in _rows)
            for (int i = 1; i < rs.Length; i++)
                if (rs[i].Expected < rs[i - 1].Expected)
                    anomalies.Add($"{lie}: {rs[i - 1].Expected} at {rs[i - 1].Distance} {rs[i - 1].Unit} falls to {rs[i].Expected} at {rs[i].Distance} {rs[i].Unit}");
        Anomalies = anomalies;
    }

    /// <summary>Lies distances are measured in: feet on the green, yards everywhere else.</summary>
    public static string UnitFor(Lie lie) => lie == Lie.Green ? "ft" : "yd";

    public Expected ExpectedStrokes(Lie lie, double distance)
    {
        if (!_rows.TryGetValue(lie, out var rs) || rs.Length == 0) return new Expected(double.NaN, Coverage.NoData);
        if (distance < rs[0].Distance - 1e-9) return new Expected(rs[0].Expected, Coverage.BelowTable);
        if (distance > rs[^1].Distance + 1e-9) return new Expected(rs[^1].Expected, Coverage.AboveTable);
        for (int i = 1; i < rs.Length; i++)
        {
            if (distance > rs[i].Distance) continue;
            var a = rs[i - 1]; var b = rs[i];
            double t = b.Distance - a.Distance < 1e-12 ? 0 : (distance - a.Distance) / (b.Distance - a.Distance);
            return new Expected(a.Expected + (b.Expected - a.Expected) * t, Coverage.Covered);
        }
        return new Expected(rs[0].Expected, Coverage.Covered);   // a single-row lie hit exactly
    }

    public static TableBaseline Parse(string csv)
    {
        string id = "", name = "", note = ""; bool estimated = false;
        var rows = new List<Row>();
        string[]? header = null;
        foreach (var raw in csv.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                var kv = line[1..].Split(':', 2);
                if (kv.Length < 2) continue;
                string key = kv[0].Trim(), value = kv[1].Trim();
                if (key == "baseline_id") id = value;
                else if (key == "name") name = value;
                else if (key == "note") note = value;
                else if (key == "estimated") estimated = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var cells = line.Split(',').Select(c => c.Trim()).ToArray();
            if (header is null) { header = cells; continue; }
            string Cell(string col) { int i = Array.IndexOf(header, col); return i >= 0 && i < cells.Length ? cells[i] : ""; }
            if (!Enum.TryParse<Lie>(Cell("lie"), true, out var lie)) throw new FormatException($"Unknown lie '{Cell("lie")}' in baseline {id}.");
            string unit = Cell("unit");
            if (unit != UnitFor(lie)) throw new FormatException($"Baseline {id}: {lie} rows must be in {UnitFor(lie)}, found '{unit}'.");
            if (Cell("baseline_id") is { Length: > 0 } rid && id != "" && rid != id) throw new FormatException($"Baseline {id} has a row from {rid}; don't mix baselines.");
            rows.Add(new Row(lie, double.Parse(Cell("distance"), CultureInfo.InvariantCulture), unit,
                double.Parse(Cell("expected"), CultureInfo.InvariantCulture), Cell("source"), Cell("source_type"), Cell("retrieved")));
        }
        if (id == "") throw new FormatException("The baseline CSV has no '# baseline_id:' line.");
        return new TableBaseline(id, name == "" ? id : name, note, estimated, rows);
    }

    /// <summary>Baselines shipped with the app (assets/holesim/baselines).</summary>
    public static IReadOnlyList<string> Shipped() =>
        typeof(TableBaseline).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("holesim.baselines.") && n.EndsWith(".csv"))
            .Select(n => n["holesim.baselines.".Length..^4]).OrderBy(n => n).ToList();

    public static TableBaseline LoadShipped(string id)
    {
        using var s = typeof(TableBaseline).Assembly.GetManifestResourceStream($"holesim.baselines.{id}.csv")
            ?? throw new FileNotFoundException($"No shipped baseline '{id}'.");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }
}

/// <summary>
/// Strokes gained (Broadie): SG(shot) = E(start) − E(end) − 1 − penalty strokes, with E(end) = 0 when holed.
/// Positive = strokes gained against the baseline, negative = lost. Shot values telescope across a hole, so their sum
/// is E(tee) minus the strokes taken, penalties included.
/// </summary>
public static class StrokesGained
{
    public const string SignConvention = "Positive = strokes gained against the baseline; negative = strokes lost.";

    public static double Shot(double expectedStart, double expectedEnd, int penaltyStrokes = 0, bool holed = false) =>
        expectedStart - (holed ? 0 : expectedEnd) - 1 - penaltyStrokes;

    /// <summary>The invariant: Σ SG over a complete hole = E(tee) − strokes (penalties included).</summary>
    public static bool InvariantHolds(IEnumerable<double> shotValues, double expectedTee, int strokesIncludingPenalties, double tolerance = 1e-9) =>
        Math.Abs(shotValues.Sum() - (expectedTee - strokesIncludingPenalties)) <= tolerance;
}
