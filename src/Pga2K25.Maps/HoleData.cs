using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Pga2K25.Maps;

public sealed record HoleDataRow(int Hole, int? Par, IReadOnlyList<int?> Yards, double? TeeBearing, double? ApproachBearing,
    IReadOnlyList<string> Hazards, string? Notes, string? GreenNotes);

/// <summary>
/// Designer-supplied hole data (the Cassimbo "hole data template"): tee set names and yardages,
/// bearings, hazard tags and notes. Read from .xlsx or .csv; the header row is located by its
/// "Hole" and "Par" cells, tee columns by "(yds)", so column order is flexible.
/// </summary>
public sealed partial class HoleData
{
    public required string? CourseTitle { get; init; }
    public required IReadOnlyList<string> TeeNames { get; init; }       // shortest → longest, as in the sheet
    public required IReadOnlyList<HoleDataRow> Rows { get; init; }

    public HoleDataRow? For(int hole) => Rows.FirstOrDefault(r => r.Hole == hole);

    public static HoleData Load(string path) =>
        Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? FromCsv(path) : FromXlsx(path);

    /// <summary>Reads the first worksheet's cell text directly via the OpenXML SDK (tolerant of minimal workbooks).</summary>
    static HoleData FromXlsx(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbPart = doc.WorkbookPart ?? throw new InvalidDataException("Workbook has no workbook part.");
        var sheet = wbPart.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new InvalidDataException("Workbook has no sheets.");
        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
        var shared = wbPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];

        var rows = new List<List<string>>();
        foreach (var row in wsPart.Worksheet?.Descendants<Row>() ?? [])
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                int col = ColumnIndex(cell.CellReference?.Value) ?? cells.Count;
                while (cells.Count < col) cells.Add("");
                cells.Add(CellText(cell, shared));
            }
            rows.Add(cells);
        }
        return Parse(rows);
    }

    static string CellText(Cell cell, List<string> shared)
    {
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out int i) && i < shared.Count)
            return shared[i];
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        string raw = cell.CellValue?.Text ?? "";
        // Numbers are stored as invariant text; show integers without a trailing ".0".
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && Math.Abs(d) < 1e9
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : raw;
    }

    /// <summary>"C12" → 2 (zero-based column).</summary>
    static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        int col = 0, i = 0;
        for (; i < reference.Length && char.IsLetter(reference[i]); i++) col = col * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
        return i == 0 ? null : col - 1;
    }

    static HoleData FromCsv(string path) =>
        Parse(File.ReadAllLines(path).Select(SplitCsv).ToList());

    static HoleData Parse(List<List<string>> rows)
    {
        int header = rows.FindIndex(r => r.Any(c => c.Trim().Equals("Hole", StringComparison.OrdinalIgnoreCase))
                                      && r.Any(c => c.Trim().Equals("Par", StringComparison.OrdinalIgnoreCase)));
        if (header < 0) throw new InvalidDataException("No header row with 'Hole' and 'Par' columns was found.");
        var h = rows[header].Select(c => c.Trim()).ToList();
        int Col(Func<string, bool> match) => h.FindIndex(c => match(c.ToLowerInvariant()));

        int hole = Col(c => c == "hole"), par = Col(c => c == "par");
        var teeCols = h.Select((c, i) => (c, i)).Where(t => YdsHeader().IsMatch(t.c)).ToList();
        int teeBearing = Col(c => c.StartsWith("tee bearing")), appBearing = Col(c => c.StartsWith("approach bearing"));
        var hazardCols = h.Select((c, i) => (c, i)).Where(t => t.c.StartsWith("Hazard", StringComparison.OrdinalIgnoreCase)).Select(t => t.i).ToList();
        int notes = Col(c => c.StartsWith("strategic")), green = Col(c => c.StartsWith("green"));

        string Get(List<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        int? Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        double? Dbl(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        var data = new List<HoleDataRow>();
        foreach (var r in rows.Skip(header + 1))
        {
            if (Int(Get(r, hole)) is not int n) continue;   // skips EX / OUT / IN / TOTAL rows
            data.Add(new HoleDataRow(n, Int(Get(r, par)), teeCols.Select(t => Int(Get(r, t.i))).ToList(),
                Dbl(Get(r, teeBearing)), Dbl(Get(r, appBearing)),
                hazardCols.Select(i => Get(r, i)).Where(s => s.Length > 0).ToList(),
                NullIfEmpty(Get(r, notes)), NullIfEmpty(Get(r, green))));
        }
        return new HoleData
        {
            CourseTitle = NullIfEmpty(rows.Take(header).Select(r => r.FirstOrDefault(c => c.Trim().Length > 0) ?? "").FirstOrDefault(s => s.Length > 0)),
            TeeNames = teeCols.Select(t => YdsHeader().Replace(t.c, "").Trim()).ToList(),
            Rows = data,
        };
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cur.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        cells.Add(cur.ToString());
        return cells;
    }

    [GeneratedRegex(@"\(\s*yds?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex YdsHeader();
}
