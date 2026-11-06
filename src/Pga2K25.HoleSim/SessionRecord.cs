using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pga2K25.HoleSim;

/// <summary>One result as it was shown: when, what kind, its headline lines, and the save it ran on.</summary>
public sealed class RecordEntry
{
    public DateTime At { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Lines { get; set; } = [];
    public string Badge { get; set; } = "";
    public string FileHash { get; set; } = "";
}

/// <summary>
/// Something the simulator found that's worth checking in the game, with your verdict. A finding keeps its id when the
/// same result is run again, so its text updates and your verdict stays (until the save changes, which marks it stale).
/// </summary>
public sealed class Finding
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string HowToCheck { get; set; } = "";
    public DateTime At { get; set; }
    public string FileHash { get; set; } = "";
    public string Verdict { get; set; } = Verdicts.NotChecked;
    public string Note { get; set; } = "";
    public DateTime? VerdictAt { get; set; }

    /// <summary>The verdict was given on an earlier save than the one the finding now comes from.</summary>
    public bool VerdictStale { get; set; }
}

public static class Verdicts
{
    public const string NotChecked = "Not checked", Confirmed = "Confirmed in game", NotSeen = "Not seen in game", Contradicted = "Contradicted";
    public static IReadOnlyList<string> All { get; } = [NotChecked, Confirmed, NotSeen, Contradicted];
}

/// <summary>
/// The session record for one hole and one day: what was run, on which save, with which assumptions, what it found, and
/// what you saw when you checked it in the game. Saved as you go to <c>&lt;products&gt;/Hole Sim/HNN session YYYY-MM-DD.json</c>,
/// with a Markdown copy beside it to read or share. Opening the hole again the same day carries on the same record.
/// </summary>
public sealed class SessionRecord
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public int Version { get; set; } = 1;
    public string Course { get; set; } = "";
    public int Hole { get; set; }
    public int Par { get; set; }
    public DateTime Started { get; set; }
    public DateTime Updated { get; set; }
    public string Baseline { get; set; } = "";
    public List<string> Saves { get; set; } = [];
    public List<string> Placeholders { get; set; } = [];
    public List<RecordEntry> Entries { get; set; } = [];
    public List<Finding> Findings { get; set; } = [];

    public static string PathFor(string outputDir, int hole, DateTime day) => Path.Combine(outputDir, "Hole Sim", $"H{hole:00} session {day.ToString("yyyy-MM-dd", Inv)}.json");

    public static SessionRecord LoadOrNew(string path, string course, int hole, int par, DateTime now)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(path), Json) is { } r && r.Hole == hole) return r;
        }
        catch (Exception) { /* an unreadable record starts afresh; the old file is overwritten on the first save */ }
        return new SessionRecord { Course = course, Hole = hole, Par = par, Started = now, Updated = now };
    }

    /// <summary>Notes the save a result ran on (time and short hash), once.</summary>
    public void NoteSave(FileStamp? stamp)
    {
        if (stamp is null) return;
        string s = $"{stamp.ModifiedUtc.ToLocalTime().ToString("d MMM yyyy HH:mm", Inv)} · sha256 {stamp.ShortHash}";
        if (!Saves.Contains(s)) Saves.Add(s);
    }

    public RecordEntry Log(DateTime at, string kind, string title, IEnumerable<string> lines, string badge, FileStamp? stamp)
    {
        NoteSave(stamp);
        var e = new RecordEntry { At = at, Kind = kind, Title = title, Lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList(), Badge = badge, FileHash = stamp?.ShortHash ?? "" };
        Entries.Add(e);
        Updated = at;
        return e;
    }

    /// <summary>Adds a finding, or updates the one with the same id: its text moves on, your verdict stays (marked stale on a newer save).</summary>
    public Finding Upsert(DateTime at, string id, string kind, string text, string howToCheck, FileStamp? stamp)
    {
        string hash = stamp?.ShortHash ?? "";
        var f = Findings.FirstOrDefault(x => x.Id == id);
        if (f is null) { f = new Finding { Id = id }; Findings.Add(f); }
        if (f.Verdict != Verdicts.NotChecked && f.FileHash != "" && hash != "" && f.FileHash != hash) f.VerdictStale = true;
        f.Kind = kind; f.Text = text; f.HowToCheck = howToCheck; f.At = at; f.FileHash = hash;
        Updated = at;
        return f;
    }

    public void SetVerdict(string id, string verdict, string note, DateTime at)
    {
        var f = Findings.FirstOrDefault(x => x.Id == id);
        if (f is null) return;
        if (f.Verdict != verdict) { f.Verdict = verdict; f.VerdictAt = at; f.VerdictStale = false; }
        f.Note = note;
        Updated = at;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        File.WriteAllText(Path.ChangeExtension(path, ".md"), ToMarkdown());
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Course}, hole {Hole} (par {Par}): simulator session {Started.ToString("d MMM yyyy", Inv)}");
        sb.AppendLine();
        sb.AppendLine($"Started {Started.ToString("HH:mm", Inv)}, last updated {Updated.ToString("HH:mm", Inv)}. Baseline `{Baseline}`.");
        if (Saves.Count > 0) sb.AppendLine($"Course saves used: {string.Join("; ", Saves)}.");
        if (Placeholders.Count > 0) sb.AppendLine($"Results depend on {Placeholders.Count} placeholder values: {string.Join("; ", Placeholders)}.");
        sb.AppendLine();
        sb.AppendLine("## Findings to check in the game");
        sb.AppendLine();
        if (Findings.Count == 0) sb.AppendLine("None yet.");
        foreach (var f in Findings)
        {
            sb.AppendLine($"- **{f.Text}**");
            if (f.HowToCheck.Length > 0) sb.AppendLine($"  - How to check: {f.HowToCheck}");
            sb.AppendLine($"  - Verdict: {f.Verdict}" + (f.VerdictAt is { } va ? $" ({va.ToString("HH:mm", Inv)})" : "") + (f.VerdictStale ? " — given on an earlier save, check again" : "")
                + (f.Note.Length > 0 ? $". {f.Note}" : ""));
        }
        sb.AppendLine();
        sb.AppendLine("## What was run");
        sb.AppendLine();
        foreach (var e in Entries)
        {
            sb.AppendLine($"### {e.At.ToString("HH:mm", Inv)} · {e.Kind}: {e.Title}");
            if (e.Badge.Length > 0) sb.AppendLine($"_{e.Badge}_");
            sb.AppendLine();
            foreach (var l in e.Lines) sb.AppendLine($"- {l}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
