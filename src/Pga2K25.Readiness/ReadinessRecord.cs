using System.Net;
using System.Text;
using System.Text.Json;

namespace Pga2K25.Readiness;

/// <summary>The paths an export wrote.</summary>
public sealed record RecordFiles(string Json, string Html);

/// <summary>
/// The final record of a course's readiness work, written when the course is done: a JSON file with everything
/// (course-file results, judgement calls, notes, sign-off) and a printable HTML page of the same. Both are dated, so
/// each export is kept rather than overwritten on another day.
/// </summary>
public static class ReadinessRecord
{
    public sealed record Snapshot(string Course, string CourseFile, string CourseStamp, DateTime Exported, bool Ready,
        IReadOnlyList<string> Blockers, IReadOnlyList<StageProgress> Progress, ReadinessReport Report, ReadinessState State);

    public static RecordFiles Write(string outputDir, string coursePath, ReadinessReport report, ReadinessState state, DateTime? at = null)
    {
        var when = at ?? DateTime.Now;
        string stamp = ReadinessStore.Stamp(coursePath);
        var blockers = ReadinessEvaluator.GateBlockers(report, state, stamp);
        var snap = new Snapshot(report.CourseName, coursePath, stamp, when, blockers.Count == 0, blockers,
            ReadinessEvaluator.Progress(report, state), report, state);

        Directory.CreateDirectory(outputDir);
        string stem = Path.Combine(outputDir, $"{Safe(report.CourseName)} - Readiness record {when:yyyy-MM-dd}");
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(snap, ReadinessStore.Json));
        File.WriteAllText(stem + ".html", Html(snap));
        return new RecordFiles(stem + ".json", stem + ".html");
    }

    static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    static string StatusText(ItemStatus s) => s switch
    {
        ItemStatus.Done => "Done", ItemStatus.Deliberate => "Deliberate", ItemStatus.NotApplicable => "N/A", _ => "Open",
    };

    static string Cell(ItemStatus s) => s switch
    {
        ItemStatus.Done => "<td class=\"done\">✓</td>", ItemStatus.Deliberate => "<td class=\"delib\">D</td>",
        ItemStatus.NotApplicable => "<td class=\"na\">n/a</td>", _ => "<td class=\"open\">·</td>",
    };

    public static string Html(Snapshot s)
    {
        var r = s.Report;
        var st = s.State;
        var b = new StringBuilder();
        b.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Readiness record</title><style>
            :root{--ink:#18211C;--muted:#56605A;--line:#DDE1DA;--tint:#F3F4F0;--ok:#1F4D30;--okbg:#E4EFE6;--flag:#7A4A00;--flagbg:#FBEFD9;--high:#8E2F1E;--highbg:#F6E0DA;--info:#1E4A70;--infobg:#E3EDF5}
            body{margin:0;background:#fff;color:var(--ink);font:14px/1.5 "Segoe UI",system-ui,sans-serif}
            main{max-width:1100px;margin:0 auto;padding:28px 16px 60px}
            h1{font-size:26px;margin:0 0 4px}h2{font-size:18px;margin:28px 0 8px;border-bottom:2px solid var(--ink);padding-bottom:4px}
            .muted{color:var(--muted)}.banner{padding:12px 14px;border-radius:8px;margin:16px 0;font-weight:600}
            .ready{background:var(--okbg);color:var(--ok)}.shut{background:var(--highbg);color:var(--high)}
            table{border-collapse:collapse;width:100%;margin:6px 0;font-size:13px}th,td{border-bottom:1px solid var(--line);padding:4px 6px;text-align:left;vertical-align:top}
            th{background:var(--tint)}.wrap{overflow-x:auto}td.done{color:var(--ok);text-align:center}td.delib{color:var(--info);text-align:center}td.na,td.open{color:var(--muted);text-align:center}
            .sev{font-weight:600;font-size:12px;padding:1px 6px;border-radius:4px;white-space:nowrap}.Pass{background:var(--okbg);color:var(--ok)}.Info,.Confirm{background:var(--infobg);color:var(--info)}
            .Flag{background:var(--flagbg);color:var(--flag)}.High{background:var(--highbg);color:var(--high)}
            @media print{main{padding:0}h2{break-after:avoid}tr{break-inside:avoid}}
            </style></head><body><main>
            """);
        b.Append($"<h1>{E(s.Course)}: publish readiness record</h1>");
        b.Append($"<div class=\"muted\">Exported {s.Exported:d MMMM yyyy, HH:mm} · course file {E(Path.GetFileName(s.CourseFile))} · fingerprint {E(s.CourseStamp)}</div>");
        b.Append(s.Ready
            ? "<div class=\"banner ready\">Ready to publish: every check is cleared and the sign-off is complete.</div>"
            : $"<div class=\"banner shut\">Not ready: {E(string.Join("; ", s.Blockers))}.</div>");

        b.Append("<h2>Sign-off</h2><table>");
        b.Append($"<tr><th>Signed off by</th><td>{E(st.SignOff.Name)}</td></tr><tr><th>Date</th><td>{E(st.SignOff.Date)}</td></tr>");
        b.Append($"<tr><th>Notes and known issues</th><td>{E(st.SignOff.Notes).Replace("\n", "<br>")}</td></tr>");
        if (st.Info.Designer.Length > 0) b.Append($"<tr><th>Designer</th><td>{E(st.Info.Designer)}</td></tr>");
        if (st.Info.Version.Length > 0) b.Append($"<tr><th>Version</th><td>{E(st.Info.Version)}</td></tr>");
        if (st.Info.TeeNames.Count > 0) b.Append($"<tr><th>Tee sets</th><td>{E(string.Join(", ", st.Info.TeeNames))}</td></tr>");
        b.Append("</table>");

        b.Append("<h2>Progress</h2><table><tr><th>Stage</th><th>Done</th><th>Of</th></tr>");
        foreach (var p in s.Progress) b.Append($"<tr><td>{E(p.Title)}</td><td>{p.Done}</td><td>{p.Total}</td></tr>");
        b.Append("</table>");

        var L = st.Limits;
        b.Append("<h2>From the course file</h2>");
        b.Append($"<p class=\"muted\">Back tees. Test drive {(L.TestDriveYards is double d ? $"{d:0} yds" : "not entered")}; par 5s leave at least {L.Par5MinLeftYards:0}; " +
                 $"drivable within {L.DrivableWithinYards:0} / near within {L.NearDrivableWithinYards:0}, at most {L.MaxDrivablePerNine} per nine; scale {L.ScaleLow:0.0#}–{L.ScaleHigh:0.0#}× the median; " +
                 $"corridor {L.CorridorYards:0} yds; elevation {L.MaxDropFeet:0} ft down, {L.MaxClimbFeet:0} up; plot range {L.PlotRangeMinFeet:0}–{L.PlotRangeMaxFeet:0} ft; green relief {L.GreenReliefMinFeet:0.0}–{L.GreenReliefMaxFeet:0.#} ft.</p>");
        b.Append("<table><tr><th>Result</th><th>Check</th><th>Detail</th><th>Deliberate</th></tr>");
        foreach (var c in r.Checks.OrderByDescending(c => c.Severity))
        {
            var marks = st.Deliberate.Where(m => m.Key == c.Id || m.Key.StartsWith(c.Id + "@", StringComparison.Ordinal))
                .Select(m => (m.Key.Contains('@') ? $"Hole {m.Key[(m.Key.IndexOf('@') + 1)..]}: " : "") + m.Value.Reason);
            b.Append($"<tr><td><span class=\"sev {c.Severity}\">{c.Severity}</span></td><td>{E(c.Title)}<div class=\"muted\">{E(c.Rule)}</div></td><td>{E(c.Detail)}</td><td>{E(string.Join(" · ", marks))}</td></tr>");
        }
        b.Append("</table>");

        b.Append("<h2>Hole by hole</h2><div class=\"wrap\"><table><tr><th>Hole</th><th>Par</th><th>Yds</th><th>Drive</th><th>Green sq ft</th><th>Landing yds</th><th>Tee → green</th><th>Relief</th><th>Nearest other green</th><th>Pins (difficulty)</th></tr>");
        foreach (var h in r.Holes)
        {
            string drive = h.DriveClass ?? (h.LeftAfterDrive is int left ? $"{left} left" : "");
            string green = h.GreenEstimated ? "not drawn" : $"{h.GreenSquareFeet:N0} ({h.GreenRatio:0.00}×)";
            string width = h.LandingWidthYards is double w ? $"{w:0} ({h.WidthRatio:0.00}×){(h.LandingOffFairway ? " off fairway" : "")}" : "";
            string near = h.NearGreenYards is double ny ? $"{ny:0} yds, {(h.NearGreenOf == 0 ? "practice green" : $"hole {h.NearGreenOf}")}" : "";
            string pins = string.Join(" ", h.Pins.Select(p => $"{p.Depth}{p.Side}" +
                (st.Pins.TryGetValue($"{h.Number}.{p.Number}", out var ps) && ps.Difficulty is { Length: > 0 } diff ? $"({diff})" : "")));
            b.Append($"<tr><td>{h.Number}</td><td>{h.Par}</td><td>{h.Yards}</td><td>{E(drive)}</td><td>{green}</td><td>{width}</td><td>{h.ChangeFeet:+0;-0;0} ft</td><td>{(h.GreenReliefFeet is double rf ? $"{rf:0.0} ft" : "")}</td><td>{E(near)}</td><td>{E(pins)}</td></tr>");
        }
        b.Append("</table></div>");

        foreach (var stage in Checklist.Stages)
        {
            b.Append($"<h2>{E(stage.Title)}</h2><table><tr><th>Status</th><th>Check</th><th>Note</th></tr>");
            foreach (var item in stage.Items.Concat(ReadinessEvaluator.CustomItems(stage, st)))
            {
                var status = ReadinessEvaluator.Effective(item, r, st, out bool fromFile);
                string note = st.Items.TryGetValue(item.Key, out var its) ? its.Note : "";
                b.Append($"<tr><td>{StatusText(status)}{(fromFile ? " (file)" : "")}</td><td>{E(item.Text)} <span class=\"muted\">· {Checklist.SourceLabel(item.Source)}</span></td><td>{E(note)}</td></tr>");
            }
            b.Append("</table>");
        }

        foreach (var hs in Checklist.HoleStages)
        {
            b.Append($"<h2>{E(hs.Title)}</h2><div class=\"wrap\"><table><tr><th>Hole</th>{string.Concat(hs.Columns.Select(c => $"<th>{E(c.Label)}</th>"))}</tr>");
            foreach (var h in r.Holes)
                b.Append($"<tr><td>{h.Number}</td>{string.Concat(hs.Columns.Select(c => Cell(st.StatusOf(hs.Key(h.Number, c.Id)))))}</tr>");
            b.Append("</table></div>");
            var noted = r.Holes.SelectMany(h => hs.Columns.Select(c => (h.Number, c.Label, Key: hs.Key(h.Number, c.Id))))
                .Where(x => st.Items.TryGetValue(x.Key, out var i) && i.Note.Length > 0).ToList();
            if (noted.Count > 0)
                b.Append("<ul>" + string.Concat(noted.Select(n => $"<li>Hole {n.Number}, {E(n.Label)}: {E(st.Items[n.Key].Note)}</li>")) + "</ul>");
        }

        b.Append("<h2>Playtest coverage</h2><table><tr><th>Profile</th><th>Winds played (high)</th><th>Tee sets</th><th>Pins</th></tr>");
        for (int i = 0; i < Math.Min(2, st.Playtest.Count); i++)
        {
            var p = st.Playtest[i];
            string name = string.Join(" · ", new[] { p.Style, p.Player, p.Wind }.Where(x => x.Length > 0));
            b.Append($"<tr><td>{(name.Length > 0 ? E(name) : $"Profile {i + 1}")}</td><td>{E(string.Join(" ", Checklist.WindDirections.Where(p.Winds.Contains)))}</td>" +
                     $"<td>{E(string.Join(" ", p.Tees.OrderBy(x => x)))}</td><td>{E(string.Join(" ", p.Pins.OrderBy(x => x)))}</td></tr>");
        }
        b.Append("</table>");

        if (r.Atmosphere is { } a)
        {
            string Cloud(CourseFormat.CloudLayer? c) => c is null ? "none" : $"{c.Type}, cloudiness {c.Cloudiness:0.##}, scale {c.Scale:0.##}, definition {c.Definition:0.##}";
            b.Append("<h2>Lighting, cloud and fog (from the file)</h2><table>");
            b.Append($"<tr><th>Time of day</th><td>{a.Clock} ({a.TimeOfDay:0.###})</td></tr><tr><th>Sun</th><td>orientation {a.SunOrientation:0.#}°, inclination {a.SunInclination:0.#}°</td></tr>");
            b.Append($"<tr><th>First cloud layer</th><td>{E(Cloud(a.Cloud1))}</td></tr><tr><th>Second cloud layer</th><td>{E(Cloud(a.Cloud2))}</td></tr>");
            b.Append($"<tr><th>Cloud height</th><td>{a.CloudHeight:0.##}</td></tr><tr><th>Fog</th><td>ceiling {a.FogCeiling:0.##}, density {a.FogDensity:0.##}</td></tr>");
            b.Append($"<tr><th>Haze, rain</th><td>{a.Haze:0.##}, {(a.Rain ? "rain" : "no rain")}</td></tr></table>");
        }
        if (st.Imported.Count > 0)
        {
            b.Append("<h2>Kept from the HTML checklist</h2><table>");
            foreach (var (k, v) in st.Imported.OrderBy(kv => kv.Key)) b.Append($"<tr><th>{E(k)}</th><td>{E(v)}</td></tr>");
            b.Append("</table>");
        }
        b.Append($"<p class=\"muted\">{E(CourseFormat.Attribution.Text)}.</p></main></body></html>");
        return b.ToString();
    }
}
