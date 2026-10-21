using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pga2K25.Readiness;

/// <summary>One course in an HTML checklist backup.</summary>
public sealed record BackupCourse(string Id, string Name, JsonObject Data);

/// <summary>What an import brought in.</summary>
public sealed record ImportSummary(int Ticks, int Deliberate, int Pins, int Custom, int Fields, IReadOnlyList<string> Notes)
{
    public override string ToString() =>
        $"{Ticks} ticks, {Deliberate} deliberate marks, {Pins} pins, {Custom} added checks and {Fields} settings imported";
}

/// <summary>
/// Reads the HTML checklist's "Export backup" file (<c>courseQA.v1</c>: <c>{current, courses: {id: {name, k, t, pins,
/// custom}}}</c>) into a course's readiness state. Keys carry straight across. Nothing is downgraded: a tick only
/// closes an open item, and a typed setting only fills an empty one. Pars, yardages, green areas and widths typed
/// into the checklist are left out, because the course file now supplies them.
/// </summary>
public static class ChecklistImport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>A node's text whether it was saved as a string or a number (the checklist stores both).</summary>
    static string? Str(JsonNode? node) => node is JsonValue v ? (v.TryGetValue(out string? s) ? s : v.ToJsonString()) : null;

    static readonly Dictionary<string, string> CustomPrefix = new() { ["pre"] = "pre", ["build"] = "b", ["pub"] = "pg", ["tourc"] = "tcl" };

    public static IReadOnlyList<BackupCourse> ReadBackup(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Not a checklist backup.");
        if (root["courses"] is not JsonObject courses) throw new InvalidDataException("Not a checklist backup: it has no courses.");
        return courses.Select(kv => kv.Value is JsonObject c
                ? new BackupCourse(Str(c["id"]) ?? kv.Key, Str(c["name"]) ?? kv.Key, c)
                : null)
            .Where(c => c is not null).Select(c => c!).ToList();
    }

    /// <summary>The backup's course for this course name (case and spacing ignored), or null.</summary>
    public static BackupCourse? Match(IReadOnlyList<BackupCourse> courses, string courseName)
    {
        static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return courses.FirstOrDefault(c => Norm(c.Name) == Norm(courseName));
    }

    public static ImportSummary Import(BackupCourse course, ReadinessState into)
    {
        var d = course.Data;
        var notes = new List<string>();
        int ticks = 0, deliberate = 0, pins = 0, custom = 0, fields = 0;

        if (d["custom"] is JsonObject cs)
            foreach (var (sec, list) in cs)
            {
                if (list is not JsonArray items || !CustomPrefix.TryGetValue(sec, out var prefix)) continue;
                foreach (var it in items.OfType<JsonObject>())
                {
                    string key = $"{prefix}.{Str(it["id"])}";
                    string text = Str(it["t"]) ?? "";
                    if (text.Length == 0 || into.Custom.Any(c => c.Key == key)) continue;
                    into.Custom.Add(new CustomItem { Stage = sec, Key = key, Text = text });
                    custom++;
                }
            }

        if (d["k"] is JsonObject k)
            foreach (var (key, v) in k)
            {
                if (v is not JsonValue jv || !jv.TryGetValue(out bool on) || !on) continue;
                if (key.StartsWith("td.", StringComparison.Ordinal) && int.TryParse(key[3..], out int hole))
                {
                    // "Size is deliberate" on a hole covers both scale measures there.
                    foreach (var check in new[] { "green-scale", "landing-width" })
                        if (into.Deliberate.TryAdd($"{check}@{hole}", new DeliberateMark { Reason = "Size marked deliberate in the HTML checklist" })) deliberate++;
                    continue;
                }
                if (key.StartsWith("play.", StringComparison.Ordinal))
                {
                    var parts = key.Split('.');
                    if (parts.Length == 3 && int.TryParse(parts[1], out int i) && i is 0 or 1)
                    {
                        var p = into.Profile(i);
                        string what = parts[2];
                        if (Checklist.WindDirections.Contains(what)) { if (!p.Winds.Contains(what)) { p.Winds.Add(what); ticks++; } }
                        else if (what.Length > 1 && int.TryParse(what[1..], out int n))
                        {
                            var set = what[0] == 't' ? p.Tees : what[0] == 'p' ? p.Pins : null;
                            if (set is not null && !set.Contains(n)) { set.Add(n); ticks++; }
                        }
                    }
                    continue;
                }
                if (key.StartsWith("atmok.", StringComparison.Ordinal)) { into.Imported[key] = "matched"; continue; }
                if (into.StatusOf(key) == ItemStatus.Open) { into.Set(key, ItemStatus.Done); ticks++; }
            }

        if (d["pins"] is JsonObject ps)
            foreach (var (key, v) in ps)
            {
                if (v is not JsonObject pin) continue;
                if (!into.Pins.TryGetValue(key, out var state)) into.Pins[key] = state = new PinState();
                string? r = Str(pin["r"]), depth = Str(pin["d"]), side = Str(pin["l"]);
                if (string.IsNullOrEmpty(state.Difficulty) && !string.IsNullOrEmpty(r)) { state.Difficulty = r; pins++; }
                if (!string.IsNullOrEmpty(depth)) state.NotedDepth ??= depth;
                if (!string.IsNullOrEmpty(side)) state.NotedSide ??= side;
            }

        if (d["t"] is JsonObject t)
        {
            string? T(string key) => Str(t[key]) is { } s && s.Trim().Length > 0 ? s.Trim() : null;
            double? Num(string key) => T(key) is { } s && double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : null;
            void Fill(string? value, Func<string> get, Action<string> set) { if (value is not null && get().Length == 0) { set(value); fields++; } }

            Fill(T("designer"), () => into.Info.Designer, v => into.Info.Designer = v);
            Fill(T("version"), () => into.Info.Version, v => into.Info.Version = v);
            Fill(T("prevWind"), () => into.Info.PrevailingWind, v => into.Info.PrevailingWind = v);
            if (T("tees") is { } tees && into.Info.TeeNames.Count == 0)
            {
                into.Info.TeeNames = tees.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                fields++;
            }
            Fill(T("soName"), () => into.SignOff.Name, v => into.SignOff.Name = v);
            Fill(T("soDate"), () => into.SignOff.Date, v => into.SignOff.Date = v);
            Fill(T("soNotes"), () => into.SignOff.Notes, v => into.SignOff.Notes = v);
            for (int i = 0; i < 2; i++)
            {
                var p = into.Profile(i);
                Fill(T($"pf{i}n"), () => p.Style, v => p.Style = v);
                Fill(T($"pf{i}l"), () => p.Wind, v => p.Wind = v);
                Fill(T($"pf{i}g"), () => p.Player, v => p.Player = v);
            }

            // Limits only where this course still has the default.
            var defaults = new ReadinessLimits();
            var L = into.Limits;
            if (Num("driveYds") is double drive && L.TestDriveYards is null) { L = L with { TestDriveYards = drive }; fields++; }
            if (Num("lim5") is double l5 && L.Par5MinLeftYards == defaults.Par5MinLeftYards) { L = L with { Par5MinLeftYards = l5 }; fields++; }
            if (Num("limD") is double ld && L.DrivableWithinYards == defaults.DrivableWithinYards) { L = L with { DrivableWithinYards = ld }; fields++; }
            if (Num("limN") is double ln && L.NearDrivableWithinYards == defaults.NearDrivableWithinYards) { L = L with { NearDrivableWithinYards = ln }; fields++; }
            if (Num("limMax") is double lm && L.MaxDrivablePerNine == defaults.MaxDrivablePerNine) { L = L with { MaxDrivablePerNine = (int)lm }; fields++; }
            if (Num("limHi") is double hi && L.ScaleHigh == defaults.ScaleHigh) { L = L with { ScaleHigh = hi }; fields++; }
            if (Num("limLo") is double lo && L.ScaleLow == defaults.ScaleLow) { L = L with { ScaleLow = lo }; fields++; }
            into.Limits = L;

            foreach (var (key, v) in t)
                if (key.StartsWith("atm", StringComparison.Ordinal) && Str(v) is { Length: > 0 } s) into.Imported[key] = s;

            if (t.Any(kv => kv.Key.StartsWith("h.", StringComparison.Ordinal) || kv.Key.StartsWith("t.", StringComparison.Ordinal)))
                notes.Add("Pars, yardages, green areas and widths typed into the checklist were left out: the course file supplies them now.");
            if (T("refTee") is { } refTee)
                notes.Add($"The checklist's reference tee was {refTee}; the Studio uses the back tees.");
        }

        return new ImportSummary(ticks, deliberate, pins, custom, fields, notes);
    }
}
