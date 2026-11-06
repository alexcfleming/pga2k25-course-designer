using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.HoleSim;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The session record (Stage 4e): every result is logged as it finishes, the ones worth checking become findings, and
/// the Record tab takes your in-game verdict on each. Saved as you go beside the live checks, in the products' Hole Sim
/// folder, as JSON with a Markdown copy.
/// </summary>
public partial class HoleSimViewModel
{
    public const string RecordStep = "Record";

    SessionRecord? _record;
    string _recordPath = "";

    public bool IsRecord => Step == RecordStep;
    public ObservableCollection<FindingRowViewModel> FindingRows { get; } = [];
    public ObservableCollection<EntryRowViewModel> EntryRows { get; } = [];
    [ObservableProperty] private string _recordFileText = "";
    [ObservableProperty] private string _recordSummary = "";

    /// <summary>Findings still waiting for a verdict (shown on the Record tab).</summary>
    public int OpenFindings => _record?.Findings.Count(f => f.Verdict == Verdicts.NotChecked || f.VerdictStale) ?? 0;

    void LoadRecord()
    {
        if (_model is null) return;
        _recordPath = SessionRecord.PathFor(_outputDir, _model.Number, DateTime.Now);
        _record = SessionRecord.LoadOrNew(_recordPath, CourseName, _model.Number, _model.Par, DateTime.Now);
        RefreshRecord();
    }

    void RecordResult(string kind, string title, IEnumerable<string> lines, string badge, params (string Id, string Text, string How)[] findings)
    {
        if (_record is null || _model is null) return;
        var now = DateTime.Now;
        _record.Baseline = _sheet.BaselineId;
        _record.Placeholders = _sheet.Rows.Where(r => r.IsPlaceholder).Select(r => r.Label).ToList();
        _record.Log(now, kind, title, lines, badge, _stamp);
        foreach (var (id, text, how) in findings) _record.Upsert(now, id, kind, text, how, _stamp);
        SaveRecord();
        RefreshRecord();
    }

    void SaveRecord()
    {
        if (_record is null) return;
        try { _record.Save(_recordPath); }
        catch (Exception ex) { Show($"Could not save the session record: {ex.Message}", error: true); }
    }

    void RefreshRecord()
    {
        FindingRows.Clear(); EntryRows.Clear();
        if (_record is null) return;
        foreach (var f in _record.Findings.OrderBy(f => f.Verdict == Verdicts.NotChecked || f.VerdictStale ? 0 : 1).ThenByDescending(f => f.At))
        {
            string id = f.Id;
            FindingRows.Add(new FindingRowViewModel(f, (verdict, note) =>
            {
                _record.SetVerdict(id, verdict, note, DateTime.Now);
                SaveRecord();
                RecordSummary = RecordSummaryText();
                OnPropertyChanged(nameof(OpenFindings));
                UpdateNav();
            }));
        }
        foreach (var e in Enumerable.Reverse(_record.Entries)) EntryRows.Add(new EntryRowViewModel(e));
        RecordFileText = $"Saved as you go to {Path.Combine("Hole Sim", Path.GetFileName(_recordPath))} beside the products, with a Markdown copy ({Path.GetFileNameWithoutExtension(_recordPath)}.md) to read or share.";
        RecordSummary = RecordSummaryText();
        OnPropertyChanged(nameof(OpenFindings));
        UpdateNav();
    }

    string RecordSummaryText()
    {
        if (_record is null) return "";
        int n = _record.Findings.Count, open = OpenFindings;
        var counts = Verdicts.All.Skip(1).Select(v => (v, _record.Findings.Count(f => f.Verdict == v && !f.VerdictStale))).Where(x => x.Item2 > 0).Select(x => $"{x.Item2} {x.v.ToLowerInvariant()}");
        return n == 0 ? $"Nothing found yet today. Results land here as they finish ({_record.Entries.Count} so far)."
            : $"{n} finding{(n == 1 ? "" : "s")} from {_record.Entries.Count} results: {open} still to check in the game" + (counts.Any() ? "; " + string.Join(", ", counts) : "") + ".";
    }

    [RelayCommand] private void OpenRecordFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_recordPath);
            if (dir is null) return;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Show($"Could not open the folder: {ex.Message}", error: true); }
    }

    // ───────── what each result logs, and the findings it raises ─────────

    static string Slug(string s) => new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
    string PlayerName(string id) => id == "power" ? "Power Drive" : "Normal";

    void LogDrive(GeometryRunResult run)
    {
        var b = run.Best;
        RecordResult("Drive", $"{run.Player.Name}, pin {run.Pin}", [BestText, .. RunNotes], ResultBadge,
            ($"drive.{run.Player.Id}.{run.Pin}",
             $"{run.Player.Name}'s best drive line: {Math.Abs(b.Aim):0} yds {(b.Aim >= 0 ? "right" : "left")} of the aim marker, {b.Share(Ground.Fairway):P0} in the fairway, {b.MeanYardsLeft:0} yds left on average.",
             "Hit a few drives on that line in playtest and note carry and total: they should land in the fairway about as often."));
    }

    void LogApproach(ApproachResult r, string aimMode)
    {
        // A par 3 tee shot is played from the tee (its lie reads as fairway inside the engine).
        bool tee = _fromMode == "tee";
        RecordResult(tee ? "Tee shot" : "Approach", $"pin {r.Pin}, {r.Yards:0} yds from the {(tee ? "tee" : Grounds.Name(r.Lie).ToLowerInvariant())}",
            [ApproachTitle, ApproachHeadline, ApproachShape, ApproachSg], ApproachBadge);
    }

    void LogMaps(int pin, ApproachHeatMap? heat, IReadOnlyList<TeeShotOption>? tees, IReadOnlyList<AngleOption> angles,
        double angleYards, string line, IReadOnlyList<(string Tee, double Degrees, bool Selected)> teeAngles)
    {
        var m = _model!; var list = new List<(string, string, string)>(); var lines = new List<string>();
        if (heat?.BestAngle is { } ba && heat.Relative[ba] >= 0.05)
        {
            string side = ba.Where.Lat < 0 ? "left" : "right";
            list.Add(($"map.angle.{pin}", $"Pin {pin}: from {ba.Yards:0} yds the {side} side of the fairway plays {heat.Relative[ba]:0.00} better than the typical spot that far out.",
                $"Play a few approaches to pin {pin} from about {ba.Yards:0} yds on the {side} side and a few from the other side; compare how many hold the green and how close."));
            lines.Add($"Best angle {ba.Yards:0} yds, {Math.Abs(ba.Where.Lat):0} yds {side} of the tee line, {heat.Relative[ba]:+0.00} on its band.");
        }
        if (tees is { Count: > 0 })
        {
            var t = tees.FirstOrDefault(x => x.Tee.Index == m.TeeIndex) ?? tees[0];
            double save = t.AtPin.Expected - t.Best.Expected;
            lines.Add($"From {t.Tee.Name}: best aim {AimWords(t.Best.Offset)}, {t.Best.Expected:0.00}; at the pin {t.AtPin.Expected:0.00}.");
            if (save >= 0.05)
                list.Add(($"tee.aim.{pin}.{t.Tee.Name}", $"Pin {pin} from {t.Tee.Name}: aiming {AimWords(t.Best.Offset)} saves {save:0.00} on firing at the pin.",
                    "Hit a few tee shots at the pin and a few at the suggested aim; compare how many hold the green."));
        }
        var best = angles.MinBy(a => a.Expected)!; var worst = angles.MaxBy(a => a.Expected)!;
        lines.Add($"Ideal angle from {angleYards:0} yds {DegreesWords(best.Degrees, line)} ({best.Expected:0.00}); worst {DegreesWords(worst.Degrees, line)} ({worst.Expected:0.00}).");
        if (worst.Expected - best.Expected >= 0.1)
            list.Add(($"map.ideal.{pin}" + (m.Par == 3 ? $".{m.Tee.Name}" : ""), $"Pin {pin} is best approached {DegreesWords(best.Degrees, line)}: from {angleYards:0} yds, {worst.Expected - best.Expected:0.00} better than {DegreesWords(worst.Degrees, line)}.",
                m.Par == 3 ? $"Hit a few tee shots to pin {pin} from each tee and watch how the green takes them from each direction."
                           : $"From about 150 yds, play a few shots into pin {pin} from each side and watch how the green takes them."));
        if (teeAngles.FirstOrDefault(t => t.Selected) is { Tee: not null } mine && Math.Abs(mine.Degrees) <= 82)
        {
            var near = angles.MinBy(a => Math.Abs(a.Degrees - mine.Degrees))!;
            lines.Add($"{mine.Tee} comes in {DegreesWords(mine.Degrees, line)}: {near.Expected:0.00}, {near.Expected - best.Expected:0.00} off the ideal.");
            if (near.Expected - best.Expected >= 0.1)
                list.Add(($"tee.angle.{pin}.{mine.Tee}", $"Pin {pin} from {mine.Tee}: the tee shot comes in {DegreesWords(mine.Degrees, line)}, {near.Expected - best.Expected:0.00} worse than the ideal direction ({DegreesWords(best.Degrees, line)}).",
                    "Compare tee shots to this pin from this tee with ones from a tee nearer the ideal line; if the gap holds, a tee there would play better."));
        }
        RecordResult(m.Par == 3 ? "Tee shot map" : "Approach map", $"pin {pin}", lines, MapBadge, [.. list]);
    }

    void LogSecond(SecondShotPlan plan, string playerId)
    {
        var b = plan.BestLayup!;
        int band = (int)Math.Round(plan.Yards / 10) * 10;
        RecordResult("Second shot", $"{PlayerName(playerId)}, pin {_secondPin}, {plan.Yards:0} yds", [SecondVerdict, SecondGo, SecondLayup], SecondBadge,
            ($"second.{playerId}.{_secondPin}.{band}",
             $"{PlayerName(playerId)} from about {band} yds to pin {_secondPin}: {SecondVerdict}",
             plan.ShouldGo ? $"From about {band} yds, go for the green a few times and lay up to {b.TargetYards:0} yds a few times; compare the scores."
                           : $"From about {band} yds, lay up to {b.TargetYards:0} yds a few times and go for it a few times; compare the scores."));
    }

    void LogSummary(IReadOnlyList<HoleSummary> list, string playerId)
    {
        var findings = new List<(string, string, string)>();
        var hard = list.MaxBy(s => s.Expected)!; var easy = list.MinBy(s => s.Expected)!;
        if (list.Count > 1 && hard.Expected - easy.Expected >= 0.15)
            findings.Add(($"summary.pins.{playerId}", $"For {PlayerName(playerId)}, pin {hard.Pin} plays {hard.Expected - easy.Expected:0.00} harder than pin {easy.Pin} ({hard.Expected:0.00} against {easy.Expected:0.00}).",
                $"Play the hole a few times to pin {hard.Pin} and to pin {easy.Pin}, and compare the scores."));
        double tee = list.Average(s => s.Sg.Tee), app = list.Average(s => s.Sg.Approach), sh = list.Average(s => s.Sg.Short), pu = list.Average(s => s.Sg.Putting);
        var phases = new List<(string Name, double V)> { ("off the tee", tee), ("on approach", app), ("around the green", sh), ("putting", pu) };
        if (_model!.Par == 3) phases.RemoveAt(0);
        var worst = phases.MinBy(p => p.V);
        if (worst.V <= -0.15)
            findings.Add(($"summary.phase.{playerId}", $"For {PlayerName(playerId)}, most strokes go {worst.Name} ({worst.V:+0.00;-0.00} against the baseline, averaged over the pins).",
                worst.Name == "putting" ? "Read the green in the game (Set up › Live checks, readings round the pins) and putt a few from 15–30 ft." : $"Watch the {worst.Name} shots when you play the hole."));
        RecordResult("Summary", $"{PlayerName(playerId)}, every pin", [SummaryHeadline, SummaryPhases, SummaryPlan, .. list.Select(s => $"Pin {s.Pin}: {s.Expected:0.00} ({s.ToPar:+0.00;-0.00;0.00}), GIR {s.GreenInRegulation:P0}")], HoleBadge, [.. findings]);
    }

    void LogExperiments(IReadOnlyList<ExperimentResult> results, string playerId)
    {
        var findings = results.Where(r => Math.Abs(r.Delta) > Math.Max(r.Ci95, 0.005))
            .Select(r => ($"exp.{playerId}.{Slug(r.Name.StartsWith("Your last save") ? "last-save-" + _stamp?.ShortHash : r.Name)}",
                $"{r.Name}: {r.Delta:+0.000;-0.000} strokes for {PlayerName(playerId)} ({(r.Delta < 0 ? "easier" : "harder")}).",
                r.Name.StartsWith("Your last save") ? "Play the hole a few times on this save; the scores should move the same way."
                    : "Make the change in the designer, save, and run the experiments again: it shows up as your last save. Then play it."))
            .ToArray();
        RecordResult("Experiments", $"{PlayerName(playerId)}, {results.Count} changes", [ExperimentsHeadline, .. results.Select(r => $"{r.Name}: {r.Delta:+0.000;-0.000;0.000} ± {r.Ci95:0.000}")], HoleBadge, findings);
    }
}

public sealed partial class FindingRowViewModel : ObservableObject
{
    readonly Action<string, string> _changed;
    public string Text { get; }
    public string HowToCheck { get; }
    public string Meta { get; }
    public bool Stale { get; }
    public ObservableCollection<TabViewModel> VerdictButtons { get; } = [];
    [ObservableProperty] private string _note;
    string _verdict;

    public FindingRowViewModel(Finding f, Action<string, string> changed)
    {
        _changed = changed;
        Text = f.Text; HowToCheck = "How to check: " + f.HowToCheck; Stale = f.VerdictStale;
        Meta = $"{f.Kind} · {f.At:HH:mm} · save {f.FileHash}" + (f.VerdictStale ? " · the verdict was given on an earlier save: check again" : "");
        _verdict = f.Verdict; _note = f.Note;
        foreach (var v in Verdicts.All) { string vv = v; VerdictButtons.Add(new TabViewModel(v, v == f.Verdict, () => SetVerdict(vv))); }
    }

    public IBrush MetaInk => Brush.Parse(Stale ? "#B4471F" : "#56605A");

    void SetVerdict(string v)
    {
        _verdict = v;
        foreach (var b in VerdictButtons) b.IsSelected = b.Label == v;
        _changed(v, Note);
    }

    partial void OnNoteChanged(string value) => _changed(_verdict, value);
}

public sealed class EntryRowViewModel(RecordEntry e)
{
    public string When { get; } = e.At.ToString("HH:mm");
    public string Title { get; } = $"{e.Kind}: {e.Title}";
    public string Lines { get; } = string.Join("\n", e.Lines);
}
