using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pga2K25.Readiness;

public enum ItemStatus { Open, Done, Deliberate, NotApplicable }

public sealed class ItemState
{
    public ItemStatus Status { get; set; }
    public string Note { get; set; } = "";
    public DateTime? Updated { get; set; }
}

/// <summary>A course-file flag accepted on purpose, with the reason written down (§11.3 B).</summary>
public sealed class DeliberateMark
{
    public string Reason { get; set; } = "";
    public DateTime Marked { get; set; } = DateTime.Now;
}

/// <summary>A pin's difficulty (R, A or G) and, from an imported checklist, where the designer noted it.</summary>
public sealed class PinState
{
    public string? Difficulty { get; set; }
    public string? NotedDepth { get; set; }
    public string? NotedSide { get; set; }
}

/// <summary>One playtest profile's coverage: wind directions at high intensity, tee sets and pins played.</summary>
public sealed class PlaytestProfile
{
    public string Style { get; set; } = "";
    public string Wind { get; set; } = "";
    public string Player { get; set; } = "";
    public List<string> Winds { get; set; } = [];
    public List<int> Tees { get; set; } = [];
    public List<int> Pins { get; set; } = [];
}

public sealed class CustomItem
{
    public string Stage { get; set; } = "";
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class CourseInfo
{
    public string Designer { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>Tee set names, front to back.</summary>
    public List<string> TeeNames { get; set; } = [];
    public string PrevailingWind { get; set; } = "";
}

public sealed class SignOff
{
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>The course file's fingerprint when it was signed off; a later change re-opens the gate.</summary>
    public string? CourseStamp { get; set; }
}

/// <summary>
/// A course's publish-readiness progress: judgement calls, deliberate marks, pins, playtest coverage, limits and the
/// sign-off. Saved as Readiness.json beside the products; the course file is never changed.
/// </summary>
public sealed class ReadinessState
{
    public int Version { get; set; } = 1;
    public CourseInfo Info { get; set; } = new();
    public ReadinessLimits Limits { get; set; } = new();
    public Dictionary<string, ItemState> Items { get; set; } = [];
    /// <summary>Keyed by check id (the whole check) or <c>{check}@{hole}</c> (one hole of it).</summary>
    public Dictionary<string, DeliberateMark> Deliberate { get; set; } = [];
    /// <summary>Keyed <c>{hole}.{pin}</c>.</summary>
    public Dictionary<string, PinState> Pins { get; set; } = [];
    public List<PlaytestProfile> Playtest { get; set; } = [new(), new()];
    public List<CustomItem> Custom { get; set; } = [];
    public SignOff SignOff { get; set; } = new();
    /// <summary>Values kept from an imported checklist that the course file now supplies (lighting as typed, etc.).</summary>
    public Dictionary<string, string> Imported { get; set; } = [];
    public DateTime Modified { get; set; } = DateTime.Now;

    public ItemStatus StatusOf(string key) => Items.TryGetValue(key, out var s) ? s.Status : ItemStatus.Open;

    public void Set(string key, ItemStatus status, string? note = null)
    {
        if (!Items.TryGetValue(key, out var s)) Items[key] = s = new ItemState();
        s.Status = status;
        if (note is not null) s.Note = note;
        s.Updated = DateTime.Now;
    }

    public PlaytestProfile Profile(int i)
    {
        while (Playtest.Count <= i) Playtest.Add(new PlaytestProfile());
        return Playtest[i];
    }
}

/// <summary>Reads and writes Readiness.json, and fingerprints the course file.</summary>
public static class ReadinessStore
{
    public const string FileName = "Readiness.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string outputDir) => Path.Combine(outputDir, FileName);

    public static ReadinessState Load(string path)
    {
        if (!File.Exists(path)) return new ReadinessState();
        try { return JsonSerializer.Deserialize<ReadinessState>(File.ReadAllText(path), Json) ?? new ReadinessState(); }
        catch (JsonException) { return new ReadinessState(); }
    }

    public static void Save(string path, ReadinessState state)
    {
        state.Modified = DateTime.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write beside and swap, so a crash mid-write can't lose the progress.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The course file's content fingerprint (first 16 hex digits of its SHA-256).</summary>
    public static string Stamp(string coursePath)
    {
        using var f = File.OpenRead(coursePath);
        return Convert.ToHexString(SHA256.HashData(f))[..16];
    }
}

/// <summary>Progress through one stage.</summary>
public sealed record StageProgress(string Id, string Title, int Done, int Total, string Note)
{
    public bool Complete => Total > 0 && Done >= Total;
}

/// <summary>
/// Puts the course-file report and the saved judgement calls together: what each item's status really is,
/// progress per stage, and whether the publish gate is open.
/// </summary>
public static class ReadinessEvaluator
{
    /// <summary>A check is cleared when it passes, or it (or every hole it names) is marked deliberate.</summary>
    public static bool IsCleared(ReadinessCheck c, ReadinessState s) =>
        c.Passed || s.Deliberate.ContainsKey(c.Id) || (c.Holes.Count > 0 && c.Holes.All(h => s.Deliberate.ContainsKey($"{c.Id}@{h}")));

    static bool Cleared(string id, ReadinessReport r, ReadinessState s) => id switch
    {
        Checklist.AllChecks => r.Checks.All(c => IsCleared(c, s)),
        "drive-entered" => s.Limits.TestDriveYards is not null,
        "playtest-winds" => s.Playtest.Count >= 2 && s.Playtest.Take(2).All(p => Checklist.WindDirections.All(p.Winds.Contains)),
        "playtest-tees" => s.Playtest.Count >= 2 && s.Playtest.Take(2).All(p =>
            Enumerable.Range(1, Math.Max(1, r.TeeSets.Count)).All(p.Tees.Contains) && Enumerable.Range(1, 4).All(p.Pins.Contains)),
        _ => r.Check(id) is { } c && IsCleared(c, s),
    };

    /// <summary>
    /// The item's status: the designer's own when they've set one, otherwise Done when every course-file check
    /// linked to it is cleared (<paramref name="fromFile"/> true), otherwise Open.
    /// </summary>
    public static ItemStatus Effective(ChecklistItem item, ReadinessReport r, ReadinessState s, out bool fromFile)
    {
        fromFile = false;
        var own = s.StatusOf(item.Key);
        if (own != ItemStatus.Open) return own;
        if (item.Checks.Count > 0 && item.Checks.All(id => Cleared(id, r, s))) { fromFile = true; return ItemStatus.Done; }
        return ItemStatus.Open;
    }

    static bool IsClosed(ItemStatus s) => s != ItemStatus.Open;

    public static IEnumerable<ChecklistItem> CustomItems(ChecklistStage stage, ReadinessState s) =>
        s.Custom.Where(c => c.Stage == stage.Id).Select(c => new ChecklistItem(c.Key, c.Text, ItemSource.YourNotes));

    public static List<StageProgress> Progress(ReadinessReport r, ReadinessState s)
    {
        var holes = r.Holes.Select(h => h.Number).ToList();
        var list = new List<StageProgress>();
        int cleared = r.Checks.Count(c => IsCleared(c, s));
        list.Add(new("file", "From the course file", cleared, r.Checks.Count, "Pass, or marked deliberate"));
        foreach (var stage in Checklist.Stages)
        {
            var items = stage.Items.Concat(CustomItems(stage, s)).ToList();
            int done = items.Count(i => IsClosed(Effective(i, r, s, out _)));
            int auto = stage.Items.Count(i => i.Checks.Count > 0);
            list.Add(new(stage.Id, stage.Title, done, items.Count, auto > 0 ? $"{auto} answered from the file" : ""));
        }
        foreach (var hs in Checklist.HoleStages)
        {
            int total = holes.Count * hs.Columns.Count;
            int done = holes.Sum(h => hs.Columns.Count(c => IsClosed(s.StatusOf(hs.Key(h, c.Id)))));
            list.Add(new(hs.Id, hs.Title, done, total, $"{hs.Columns.Count} per hole"));
        }
        int pinsSet = holes.Sum(h => Enumerable.Range(1, 4).Count(p => s.Pins.TryGetValue($"{h}.{p}", out var ps) && ps.Difficulty is { Length: > 0 }));
        list.Add(new("pins", "Pin difficulty", pinsSet, holes.Count * 4, "Positions from the file; difficulty is yours"));
        int tees = Math.Max(1, r.TeeSets.Count);
        int perProfile = Checklist.WindDirections.Count + tees + 4;
        int played = s.Playtest.Take(2).Sum(p => p.Winds.Count(Checklist.WindDirections.Contains) + p.Tees.Count(t => t >= 1 && t <= tees) + p.Pins.Count(x => x is >= 1 and <= 4));
        list.Add(new("playtest", "Playtest coverage", played, perProfile * 2, "Winds, tee sets and pins, two profiles"));
        return list;
    }

    /// <summary>Why the gate is shut (empty when the course is ready to publish).</summary>
    public static List<string> GateBlockers(ReadinessReport r, ReadinessState s, string? currentStamp)
    {
        var why = new List<string>();
        int open = r.Checks.Count(c => !IsCleared(c, s));
        if (open > 0) why.Add($"{open} course-file check{(open == 1 ? "" : "s")} to fix or mark deliberate");
        int items = Progress(r, s).Where(p => p.Id != "file").Sum(p => Math.Max(0, p.Total - p.Done));
        if (items > 0) why.Add($"{items} check{(items == 1 ? "" : "s")} still open");
        if (string.IsNullOrWhiteSpace(s.SignOff.Name) || string.IsNullOrWhiteSpace(s.SignOff.Date)) why.Add("sign-off missing");
        else if (currentStamp is not null && s.SignOff.CourseStamp is { } stamp && stamp != currentStamp) why.Add("the course file has changed since the sign-off");
        return why;
    }
}
